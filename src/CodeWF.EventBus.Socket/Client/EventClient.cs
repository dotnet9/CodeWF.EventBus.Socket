using CodeWF.EventBus;

// ReSharper disable once CheckNamespace
namespace CodeWF.EventBus.Socket;

public class EventClient : IEventClient
{
    private const int ReconnectInterval = 3000;
    private const int HeartbeatInterval = 5000;
    private const int MaxTrySendHeartTime = 3;

    // 当前线程是否正处于“处理查询请求”的上下文中。
    // 这样订阅处理器内部直接调用 Publish 时，客户端就能自动带上原查询 TaskId。
    private readonly AsyncLocal<QueryResponseContext?> _queryResponseContext = new();
    // 查询发起方本地缓存，键为查询 TaskId，值为该查询专属的响应通道。
    private readonly ConcurrentDictionary<string, Channel<UpdateEvent>> _queryResponseChannels = new();
    // 每个主题在本地注册的处理器列表。
    private readonly ConcurrentDictionary<string, List<Delegate>> _subjectAndHandlers = new();

    private readonly Func<SocketCommand, Task> _socketCommandHandler;
    private readonly Func<TcpClientErrorCommand, Task> _clientErrorHandler;

    private Channel<SocketCommand> _inboundCommands = CreateInboundCommandChannel();
    private Channel<OutboundCommand> _outboundCommands = CreateOutboundCommandChannel();
    private CancellationTokenSource? _cancellationTokenSource;
    private TcpSocketClient? _client;
    private string? _host;
    private int _port;
    // 连接建立后先做一次轻量握手，确认对端真的是事件总线服务。
    private TaskCompletionSource<bool>? _eventServerHandshakeCompletion;
    private string? _requestIsEventServerTaskId;
    private int _trySendHeartbeatTimes;
    private int _reconnectScheduled;
    private bool _isSubscribedToTransportEvents;

    private sealed record QueryResponseContext(string Subject, string TaskId);
    private sealed record OutboundCommand(INetObject Command, bool NeedCheckConnectStatus);

    public EventClient()
    {
        _socketCommandHandler = HandleSocketCommandAsync;
        _clientErrorHandler = HandleClientErrorAsync;
    }

    #region interface methods

    public ConnectStatus ConnectStatus { get; private set; }

    public void Connect(string host, int port)
    {
        ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(3));
    }

    public async Task<bool> ConnectAsync(string host, int port)
    {
        _host = host;
        _port = port;

        // 每次连接或重连都重建通道和后台消费者，避免沿用已关闭的管道。
        _cancellationTokenSource = new CancellationTokenSource();
        ConnectStatus = ConnectStatus.IsConnecting;
        ResetPipelines();
        EnsureTransportSubscriptions();

        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                _client?.Stop();
                _client = new TcpSocketClient();
                var (isSuccess, errorMessage) = await _client.ConnectAsync(nameof(EventClient), host, port);
                if (!isSuccess)
                {
                    throw new Exception(errorMessage ?? "连接事件总线服务失败。");
                }

                Interlocked.Exchange(ref _reconnectScheduled, 0);
                await CheckIsEventServerAsync();
                return ConnectStatus == ConnectStatus.Connected;
            }
            catch (SocketException ex)
            {
                Debug.WriteLine($"TCP 服务连接异常，将在 {ReconnectInterval / 1000} 秒后重新连接：{ex.Message}");
                await Task.Delay(TimeSpan.FromMilliseconds(ReconnectInterval), CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"事件服务连接异常：{ex.Message}");
                ConnectStatus = ConnectStatus.Disconnected;
                await Task.Delay(TimeSpan.FromMilliseconds(ReconnectInterval), CancellationToken.None);
            }
        }

        return false;
    }

    public void Disconnect()
    {
        try
        {
            // 先通知后台循环退出，再关闭底层连接。
            _cancellationTokenSource?.Cancel();
            _client?.Stop();
            _client = null;
        }
        catch
        {
            // ignored
        }
        finally
        {
            ConnectStatus = ConnectStatus.Disconnected;
            _eventServerHandshakeCompletion?.TrySetCanceled();
            _eventServerHandshakeCompletion = null;
            _requestIsEventServerTaskId = null;
            Interlocked.Exchange(ref _trySendHeartbeatTimes, 0);
            Interlocked.Exchange(ref _reconnectScheduled, 0);
            _inboundCommands.Writer.TryComplete();
            _outboundCommands.Writer.TryComplete();
            CompleteQueryChannels();
            RemoveTransportSubscriptions();
        }
    }

    public void Subscribe<T>(string subject, Action<T> eventHandler)
    {
        AddSubscribe(subject, eventHandler);
    }

    public void Subscribe<T>(string subject, Func<T, Task> asyncEventHandler)
    {
        AddSubscribe(subject, asyncEventHandler);
    }

    public void Unsubscribe<T>(string subject, Action<T> eventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
        {
            return;
        }

        handlers.Remove(eventHandler);
        if (handlers.Count > 0)
        {
            return;
        }

        _subjectAndHandlers.TryRemove(subject, out _);
        SendCommand(new RequestUnsubscribe
        {
            TaskId = SocketHelper.GetNewTaskId(),
            Subject = subject
        });
    }

    public void Unsubscribe<T>(string subject, Func<T, Task> asyncEventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
        {
            return;
        }

        handlers.Remove(asyncEventHandler);
        if (handlers.Count > 0)
        {
            return;
        }

        _subjectAndHandlers.TryRemove(subject, out _);
        SendCommand(new RequestUnsubscribe
        {
            TaskId = SocketHelper.GetNewTaskId(),
            Subject = subject
        });
    }

    public bool Publish<T>(string subject, T message, out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            // 如果当前 Publish 发生在查询处理器内部，则把原查询 TaskId 透传给服务端。
            // 这样服务端就会把它识别为查询响应，而不是普通广播事件。
            SendCommand(new RequestPublish
            {
                TaskId = SocketHelper.GetNewTaskId(),
                Subject = subject,
                QueryTaskId = GetCurrentQueryTaskId(subject),
                Buffer = message is null ? null : message.SerializeObject(typeof(T))
            });
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            Debug.WriteLine(ex.Message);
            return false;
        }
    }

    public async Task<(TResponse? Result, string ErrorMessage)> QueryAsync<TQuery, TResponse>(
        string subject,
        TQuery message,
        int overtimeMilliseconds)
    {
        var errorMessage = string.Empty;
        var taskId = SocketHelper.GetNewTaskId();

        try
        {
            var request = new RequestQuery
            {
                TaskId = taskId,
                Subject = subject,
                Buffer = message.SerializeObject()
            };
            var responseChannel = Channel.CreateBounded<UpdateEvent>(new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = true,
                // 查询语义只关心最后一条响应，理论上也只应收到一条响应。
                FullMode = BoundedChannelFullMode.DropOldest
            });

            _queryResponseChannels[request.TaskId] = responseChannel;
            SendCommand(request);

            using var timeoutCancellation = new CancellationTokenSource(overtimeMilliseconds);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationTokenSource?.Token ?? CancellationToken.None,
                timeoutCancellation.Token);

            var updateEvent = await responseChannel.Reader.ReadAsync(linkedCancellation.Token);
            if (updateEvent.Buffer != null)
            {
                return ((TResponse)updateEvent.Buffer.DeserializeObject(typeof(TResponse)), string.Empty);
            }

            return (default, "未从服务端收到响应。");
        }
        catch (OperationCanceledException)
        {
            if (_cancellationTokenSource?.IsCancellationRequested == true)
            {
                return (default, "操作已取消。");
            }

            return (default, "查询超时，请重试。");
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            Debug.WriteLine(ex.Message);
            return (default, errorMessage);
        }
        finally
        {
            if (_queryResponseChannels.TryRemove(taskId, out var responseChannel))
            {
                responseChannel.Writer.TryComplete();
            }
        }
    }

    public TResponse? Query<TQuery, TResponse>(
        string subject,
        TQuery message,
        out string errorMessage,
        int overtimeMilliseconds = 3000)
    {
        try
        {
            var result = QueryAsync<TQuery, TResponse>(subject, message, overtimeMilliseconds)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            errorMessage = result.ErrorMessage;
            return result.Result;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return default;
        }
    }

    #endregion

    #region private methods

    private void Reconnect()
    {
        if (string.IsNullOrWhiteSpace(_host))
        {
            return;
        }

        Disconnect();
        Connect(_host, _port);
    }

    private void AddSubscribe(string subject, Delegate eventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
        {
            handlers = [eventHandler];
            _subjectAndHandlers.TryAdd(subject, handlers);

            // 本地首个处理器注册时，才需要通知服务端建立远程订阅关系。
            SendCommand(new RequestSubscribe
            {
                TaskId = SocketHelper.GetNewTaskId(),
                Subject = subject
            });
        }
        else
        {
            handlers.Add(eventHandler);
        }
    }

    private void EnsureTransportSubscriptions()
    {
        if (_isSubscribedToTransportEvents)
        {
            return;
        }

        EventBus.Default.Subscribe(_socketCommandHandler);
        EventBus.Default.Subscribe(_clientErrorHandler);
        _isSubscribedToTransportEvents = true;
    }

    private void RemoveTransportSubscriptions()
    {
        if (!_isSubscribedToTransportEvents)
        {
            return;
        }

        EventBus.Default.Unsubscribe(_socketCommandHandler);
        EventBus.Default.Unsubscribe(_clientErrorHandler);
        _isSubscribedToTransportEvents = false;
    }

    private Task HandleSocketCommandAsync(SocketCommand command)
    {
        if (!BelongsToCurrentClient(command.Client))
        {
            return Task.CompletedTask;
        }

        // 传输层回调只负责入队；若连接正在关闭，直接忽略残留消息即可。
        _inboundCommands.Writer.TryWrite(command);
        return Task.CompletedTask;
    }

    private Task HandleClientErrorAsync(TcpClientErrorCommand error)
    {
        if (!ReferenceEquals(error.Client, _client))
        {
            return Task.CompletedTask;
        }

        ConnectStatus = ConnectStatus.Disconnected;
        Debug.WriteLine(error.ErrorMessage);
        return Task.CompletedTask;
    }

    private void SendHeartbeat()
    {
        _ = Task.Run(async () =>
        {
            // 心跳请求也进入发送通道，避免在计时线程里直接阻塞等待网络发送。
            while (_cancellationTokenSource is { IsCancellationRequested: false })
            {
                try
                {
                    SendCommand(new Heartbeat());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"发送心跳入队失败：{ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(HeartbeatInterval));
            }
        });
    }

    private void ResetPipelines()
    {
        _inboundCommands = CreateInboundCommandChannel();
        _outboundCommands = CreateOutboundCommandChannel();
        _ = Task.Run(ProcessInboundCommandsAsync);
        _ = Task.Run(ProcessOutboundCommandsAsync);
    }

    private async Task ProcessInboundCommandsAsync()
    {
        var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

        try
        {
            await foreach (var command in _inboundCommands.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    if (command.IsCommand<ResponseCommon>())
                    {
                        HandleResponse(command.GetCommand<ResponseCommon>());
                    }
                    else if (command.IsCommand<UpdateEvent>())
                    {
                        HandleResponse(command.GetCommand<UpdateEvent>());
                    }
                    else if (command.IsCommand<Heartbeat>())
                    {
                        HandleResponse(command.GetCommand<Heartbeat>());
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"处理响应异常：{ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 断开连接时允许消费者自然退出。
        }
    }

    private async Task ProcessOutboundCommandsAsync()
    {
        var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

        try
        {
            await foreach (var command in _outboundCommands.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    if (command.NeedCheckConnectStatus && ConnectStatus != ConnectStatus.Connected)
                    {
                        continue;
                    }

                    if (_client == null)
                    {
                        continue;
                    }

                    await _client.SendCommandAsync(command.Command);
                }
                catch (Exception ex)
                {
                    if (command.Command is Heartbeat)
                    {
                        var currentRetry = Interlocked.Increment(ref _trySendHeartbeatTimes);
                        Debug.WriteLine(
                            $"发送心跳异常，将尝试重新发送！({MaxTrySendHeartTime - currentRetry}/{MaxTrySendHeartTime})");

                        if (currentRetry >= MaxTrySendHeartTime &&
                            Interlocked.CompareExchange(ref _reconnectScheduled, 1, 0) == 0)
                        {
                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    Reconnect();
                                }
                                finally
                                {
                                    Interlocked.Exchange(ref _reconnectScheduled, 0);
                                }
                            });
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"发送命令异常：{ex.Message}");
                        ConnectStatus = ConnectStatus.Disconnected;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 断开连接时允许消费者自然退出。
        }
    }

    private async Task CheckIsEventServerAsync()
    {
        _requestIsEventServerTaskId = SocketHelper.GetNewTaskId();
        _eventServerHandshakeCompletion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SendCommand(new RequestIsEventServer { TaskId = _requestIsEventServerTaskId }, false);

        // 连接建立后先做一次轻量握手，确认对端确实是本事件总线服务，而不是普通 TCP 服务。
        var timeoutTask = Task.Delay(3000);
        var completedTask = await Task.WhenAny(_eventServerHandshakeCompletion.Task, timeoutTask);
        if (completedTask == timeoutTask)
        {
            ConnectStatus = ConnectStatus.DisconnectedNeedCheckEventServer;
            _cancellationTokenSource?.Cancel();
            _eventServerHandshakeCompletion.TrySetCanceled();
            _eventServerHandshakeCompletion = null;
            throw new Exception("请检查事件总线服务是否连接正确");
        }

        await _eventServerHandshakeCompletion.Task;
        _eventServerHandshakeCompletion = null;
        ConnectStatus = ConnectStatus.Connected;
        SendHeartbeat();
    }

    private void HandleResponse(ResponseCommon response)
    {
        if (_requestIsEventServerTaskId == response.TaskId)
        {
            _requestIsEventServerTaskId = null;
            _eventServerHandshakeCompletion?.TrySetResult(true);
        }
    }

    private void HandleResponse(UpdateEvent response)
    {
        // 只有真正的查询响应才应该命中等待中的 QueryAsync。
        // 如果当前消息仍是查询请求，即便 TaskId 与本地待查询映射相同，也要继续分发给订阅处理器。
        if (!response.IsQueryRequest &&
            _queryResponseChannels.TryGetValue(response.TaskId, out var responseChannel))
        {
            responseChannel.Writer.TryWrite(response);
            return;
        }

        if (!_subjectAndHandlers.TryGetValue(response.Subject, out var handlers))
        {
            return;
        }

        foreach (var handler in handlers.ToArray())
        {
            try
            {
                var previousContext = _queryResponseContext.Value;
                if (response.IsQueryRequest)
                {
                    // 仅在处理查询请求时注入上下文，普通广播事件不应携带 QueryTaskId。
                    _queryResponseContext.Value = new QueryResponseContext(response.Subject, response.TaskId);
                }

                try
                {
                    var parameter = handler.Method.GetParameters().First();
                    var parameterValue = response.Buffer?.DeserializeObject(parameter.ParameterType);
                    if (handler.Method.ReturnType == typeof(Task))
                    {
                        var task = handler.DynamicInvoke(parameterValue) as Task;
                        task?.GetAwaiter().GetResult();
                    }
                    else
                    {
                        handler.DynamicInvoke(parameterValue);
                    }
                }
                finally
                {
                    _queryResponseContext.Value = previousContext;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"分发订阅消息异常：{ex.Message}");
            }
        }
    }

    private void HandleResponse(Heartbeat response)
    {
        Interlocked.Exchange(ref _trySendHeartbeatTimes, 0);
    }

    private void SendCommand(INetObject command, bool needCheckConnectStatus = true)
    {
        if (needCheckConnectStatus && ConnectStatus != ConnectStatus.Connected)
        {
            throw new Exception("事件服务未连接，无法发送事件！");
        }

        if (!_outboundCommands.Writer.TryWrite(new OutboundCommand(command, needCheckConnectStatus)))
        {
            throw new Exception("发送通道已关闭，无法继续发送事件。");
        }
    }

    private bool BelongsToCurrentClient(System.Net.Sockets.Socket? socket)
    {
        return socket?.LocalEndPoint?.ToString() == _client?.LocalEndPoint;
    }

    private void CompleteQueryChannels()
    {
        foreach (var responseChannel in _queryResponseChannels.Values)
        {
            responseChannel.Writer.TryComplete();
        }

        _queryResponseChannels.Clear();
    }

    private static Channel<SocketCommand> CreateInboundCommandChannel()
    {
        return Channel.CreateUnbounded<SocketCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    private static Channel<OutboundCommand> CreateOutboundCommandChannel()
    {
        return Channel.CreateUnbounded<OutboundCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    private string? GetCurrentQueryTaskId(string subject)
    {
        var context = _queryResponseContext.Value;
        // 只有“同主题 + 当前正处于查询处理器内”时，才把 Publish 识别为查询响应。
        return context is not null && string.Equals(context.Subject, subject, StringComparison.Ordinal)
            ? context.TaskId
            : null;
    }

    #endregion
}
