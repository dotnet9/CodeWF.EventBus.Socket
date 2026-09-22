using CodeWF.EventBus;

// ReSharper disable once CheckNamespace
namespace CodeWF.EventBus.Socket;

public class EventClient : IEventClient
{
    private const int HandshakeTimeoutMilliseconds = 3000;

    private readonly EventBusOptions _options;
    private readonly AsyncLocal<QueryResponseContext?> _queryResponseContext = new();
    private readonly ConcurrentDictionary<string, Channel<UpdateEvent>> _queryResponseChannels = new();
    private readonly Dictionary<string, List<Delegate>> _subjectAndHandlers = new(StringComparer.Ordinal);
    private readonly object _subscriptionSync = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _reconnectSync = new();
    private readonly Func<TcpClientErrorCommand, Task> _clientErrorHandler;

    private ClientSession? _session;
    private CancellationTokenSource? _reconnectCancellation;
    private Task? _reconnectTask;
    private string? _host;
    private int _port;
    private bool _isSubscribedToClientErrorEvents;

    private sealed class ClientSession
    {
        public ClientSession(EventBusOptions options)
        {
            Cancellation = new CancellationTokenSource();
            Inbound = CreateInboundCommandChannel(options);
            Outbound = CreateOutboundCommandChannel(options);
        }

        public CancellationTokenSource Cancellation { get; }
        public Channel<SocketCommand> Inbound { get; }
        public Channel<OutboundCommand> Outbound { get; }
        public TcpSocketClient? Client { get; set; }
        public IDisposable? CommandRegistration { get; set; }
        public Task InboundTask { get; set; } = Task.CompletedTask;
        public Task OutboundTask { get; set; } = Task.CompletedTask;
        public Task HeartbeatTask { get; set; } = Task.CompletedTask;
        public TaskCompletionSource<bool>? HandshakeCompletion { get; set; }
        public string? HandshakeTaskId { get; set; }
        public int ReconnectScheduled;
        public int Stopped;
    }

    private sealed record QueryResponseContext(string Subject, string TaskId);
    private sealed record OutboundCommand(INetObject Command, bool NeedCheckConnectStatus);

    public EventClient(EventBusOptions? options = null)
    {
        _options = options ?? new EventBusOptions();
        _options.Validate();
        _clientErrorHandler = HandleClientErrorAsync;
    }

    public ConnectStatus ConnectStatus { get; private set; } = ConnectStatus.Disconnected;

    public void Connect(string host, int port)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var connected = ConnectAsync(host, port, timeout.Token).GetAwaiter().GetResult();
        if (!connected)
        {
            throw new TimeoutException("连接事件服务超时。");
        }
    }

    public Task<bool> ConnectAsync(string host, int port)
    {
        return ConnectAsync(host, port, CancellationToken.None);
    }

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        ValidateEndpoint(host, port);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            StopReconnectLoop();
            return await ConnectCoreAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Disconnect()
    {
        _lifecycleGate.Wait();
        try
        {
            StopReconnectLoop();
            StopSessionAsync(_session).GetAwaiter().GetResult();
            _session = null;
            CompleteQueryChannels();
            RemoveClientErrorSubscription();
            ConnectStatus = ConnectStatus.Disconnected;
        }
        finally
        {
            _lifecycleGate.Release();
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
        RemoveSubscribe(subject, eventHandler);
    }

    public void Unsubscribe<T>(string subject, Func<T, Task> asyncEventHandler)
    {
        RemoveSubscribe(subject, asyncEventHandler);
    }

    public bool Publish<T>(string subject, T message, out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            ValidateSubject(subject);
            var buffer = message is null ? null : message.SerializeObject(typeof(T));
            ValidateBuffer(buffer);

            SendCommand(new RequestPublish
            {
                TaskId = SocketHelper.GetNewTaskId(),
                Subject = subject,
                QueryTaskId = GetCurrentQueryTaskId(subject),
                Buffer = buffer
            });
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            Report("发布事件失败", ex);
            return false;
        }
    }

    public async Task<(TResponse? Result, string ErrorMessage)> QueryAsync<TQuery, TResponse>(
        string subject,
        TQuery message,
        int overtimeMilliseconds = 3000)
    {
        var taskId = SocketHelper.GetNewTaskId();
        try
        {
            ValidateSubject(subject);
            if (overtimeMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(overtimeMilliseconds));
            }

            var request = new RequestQuery
            {
                TaskId = taskId,
                Subject = subject,
                Buffer = message is null ? null : message.SerializeObject()
            };
            ValidateBuffer(request.Buffer);

            var responseChannel = Channel.CreateBounded<UpdateEvent>(new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            _queryResponseChannels[taskId] = responseChannel;
            SendCommand(request);

            using var timeoutCancellation = new CancellationTokenSource(overtimeMilliseconds);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                GetSessionCancellationToken(),
                timeoutCancellation.Token);

            var updateEvent = await responseChannel.Reader.ReadAsync(linkedCancellation.Token).ConfigureAwait(false);
            if (updateEvent.Buffer is null)
            {
                return (default, "未从服务端收到响应。");
            }

            var response = updateEvent.Buffer.DeserializeObject(typeof(TResponse));
            return response is TResponse typedResponse
                ? (typedResponse, string.Empty)
                : (default, "服务端响应反序列化失败。");
        }
        catch (OperationCanceledException)
        {
            return (default, ConnectStatus == ConnectStatus.Disconnected
                ? "操作已取消。"
                : "查询超时，请重试。");
        }
        catch (Exception ex)
        {
            Report("查询事件失败", ex);
            return (default, ex.Message);
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
        var result = QueryAsync<TQuery, TResponse>(subject, message, overtimeMilliseconds)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        errorMessage = result.ErrorMessage;
        return result.Result;
    }

    private async Task<bool> ConnectCoreAsync(string host, int port, CancellationToken cancellationToken)
    {
        await StopSessionAsync(_session).ConfigureAwait(false);
        _session = new ClientSession(_options);
        _host = host;
        _port = port;
        ConnectStatus = ConnectStatus.IsConnecting;
        EnsureClientErrorSubscription();

        var session = _session;
        session.InboundTask = ProcessInboundCommandsAsync(session);
        session.OutboundTask = ProcessOutboundCommandsAsync(session);

        try
        {
            var client = new TcpSocketClient();
            session.Client = client;
            var (isSuccess, errorMessage) = await client.ConnectAsync(nameof(EventClient), host, port)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isSuccess)
            {
                throw new InvalidOperationException(errorMessage ?? "连接事件总线服务失败。");
            }

            session.CommandRegistration = client.RegisterCommandHandler(
                command => HandleSocketCommandAsync(session, command));
            await CheckIsEventServerAsync(session, cancellationToken).ConfigureAwait(false);
            ConnectStatus = ConnectStatus.Connected;
            await ResubscribeAsync(session, cancellationToken).ConfigureAwait(false);
            session.HeartbeatTask = HeartbeatLoopAsync(session);
            return true;
        }
        catch (OperationCanceledException)
        {
            await StopSessionAsync(session).ConfigureAwait(false);
            ConnectStatus = ConnectStatus.Disconnected;
            return false;
        }
        catch (Exception ex)
        {
            _options.Report("连接事件服务失败", ex);
            await StopSessionAsync(session).ConfigureAwait(false);
            ConnectStatus = ConnectStatus.Disconnected;
            return false;
        }
    }

    private async Task CheckIsEventServerAsync(ClientSession session, CancellationToken cancellationToken)
    {
        session.HandshakeTaskId = SocketHelper.GetNewTaskId();
        session.HandshakeCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        SendCommand(session, new RequestIsEventServer
        {
            TaskId = session.HandshakeTaskId,
            AuthenticationToken = _options.AuthenticationToken
        }, false);

        using var timeout = new CancellationTokenSource(HandshakeTimeoutMilliseconds);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            session.Cancellation.Token,
            timeout.Token);
        if (!await session.HandshakeCompletion.Task.WaitAsync(linked.Token).ConfigureAwait(false))
        {
            throw new InvalidOperationException("请检查事件总线服务认证配置。");
        }

        session.HandshakeCompletion = null;
        session.HandshakeTaskId = null;
    }

    private Task ResubscribeAsync(ClientSession session, CancellationToken cancellationToken)
    {
        string[] subjects;
        lock (_subscriptionSync)
        {
            subjects = _subjectAndHandlers.Keys.ToArray();
        }

        foreach (var subject in subjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendCommand(session, new RequestSubscribe
            {
                TaskId = SocketHelper.GetNewTaskId(),
                Subject = subject
            }, false);
        }

        return Task.CompletedTask;
    }

    private void AddSubscribe(string subject, Delegate eventHandler)
    {
        ArgumentNullException.ThrowIfNull(eventHandler);
        ValidateSubject(subject);

        var shouldRegister = false;
        lock (_subscriptionSync)
        {
            if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
            {
                handlers = new List<Delegate>();
                _subjectAndHandlers.Add(subject, handlers);
                shouldRegister = true;
            }

            handlers.Add(eventHandler);
        }

        if (shouldRegister && _session is { } session && ConnectStatus == ConnectStatus.Connected)
        {
            SendCommand(session, new RequestSubscribe
            {
                TaskId = SocketHelper.GetNewTaskId(),
                Subject = subject
            });
        }
    }

    private void RemoveSubscribe(string subject, Delegate eventHandler)
    {
        ArgumentNullException.ThrowIfNull(eventHandler);
        ValidateSubject(subject);

        var shouldUnregister = false;
        lock (_subscriptionSync)
        {
            if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
            {
                return;
            }

            handlers.Remove(eventHandler);
            if (handlers.Count == 0)
            {
                _subjectAndHandlers.Remove(subject);
                shouldUnregister = true;
            }
        }

        if (shouldUnregister && _session is { } session && ConnectStatus == ConnectStatus.Connected)
        {
            SendCommand(session, new RequestUnsubscribe
            {
                TaskId = SocketHelper.GetNewTaskId(),
                Subject = subject
            });
        }
    }

    private Task<bool> HandleSocketCommandAsync(ClientSession session, SocketCommand command)
    {
        if (!ReferenceEquals(_session, session) || session.Cancellation.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        var accepted = session.Inbound.Writer.TryWrite(command);
        if (!accepted)
        {
            _options.Report("客户端入站队列已满");
        }

        return Task.FromResult(accepted);
    }

    private Task HandleClientErrorAsync(TcpClientErrorCommand error)
    {
        var session = _session;
        if (session?.Client is null || !ReferenceEquals(error.Client, session.Client))
        {
            return Task.CompletedTask;
        }

        ConnectStatus = ConnectStatus.Disconnected;
        _options.Report(error.ErrorMessage ?? "TCP 客户端连接异常");
        ScheduleReconnect(session);
        return Task.CompletedTask;
    }

    private async Task HeartbeatLoopAsync(ClientSession session)
    {
        try
        {
            while (!session.Cancellation.IsCancellationRequested)
            {
                SendCommand(session, new Heartbeat());
                await Task.Delay(_options.HeartbeatInterval, session.Cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ConnectStatus = ConnectStatus.Disconnected;
            _options.Report("发送心跳失败", ex);
            ScheduleReconnect(session);
        }
    }

    private async Task ProcessInboundCommandsAsync(ClientSession session)
    {
        try
        {
            await foreach (var command in session.Inbound.Reader.ReadAllAsync(session.Cancellation.Token)
                               .ConfigureAwait(false))
            {
                try
                {
                    if (command.IsCommand<ResponseCommon>())
                    {
                        HandleResponse(session, command.GetCommand<ResponseCommon>());
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
                    _options.Report("处理服务端响应失败", ex);
                }
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessOutboundCommandsAsync(ClientSession session)
    {
        try
        {
            await foreach (var command in session.Outbound.Reader.ReadAllAsync(session.Cancellation.Token)
                               .ConfigureAwait(false))
            {
                try
                {
                    if (command.NeedCheckConnectStatus &&
                        (!ReferenceEquals(_session, session) || ConnectStatus != ConnectStatus.Connected))
                    {
                        continue;
                    }

                    if (session.Client is null)
                    {
                        continue;
                    }

                    await session.Client.SendCommandAsync(command.Command).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ConnectStatus = ConnectStatus.Disconnected;
                    _options.Report("发送事件失败", ex);
                    ScheduleReconnect(session);
                }
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
    }

    private void HandleResponse(ClientSession session, ResponseCommon response)
    {
        if (session.HandshakeTaskId == response.TaskId)
        {
            session.HandshakeCompletion?.TrySetResult(response.Status == (byte)ResponseCommonStatus.Success);
        }
    }

    private void HandleResponse(UpdateEvent response)
    {
        if (!response.IsQueryRequest &&
            _queryResponseChannels.TryGetValue(response.TaskId, out var responseChannel))
        {
            responseChannel.Writer.TryWrite(response);
            return;
        }

        Delegate[] handlers;
        lock (_subscriptionSync)
        {
            if (!_subjectAndHandlers.TryGetValue(response.Subject, out var registeredHandlers))
            {
                return;
            }

            handlers = registeredHandlers.ToArray();
        }

        foreach (var handler in handlers)
        {
            try
            {
                var previousContext = _queryResponseContext.Value;
                if (response.IsQueryRequest)
                {
                    _queryResponseContext.Value = new QueryResponseContext(response.Subject, response.TaskId);
                }

                try
                {
                    var parameter = handler.Method.GetParameters().First();
                    var parameterValue = response.Buffer?.DeserializeObject(parameter.ParameterType);
                    if (handler.Method.ReturnType == typeof(Task))
                    {
                        (handler.DynamicInvoke(parameterValue) as Task)?.GetAwaiter().GetResult();
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
                _options.Report("分发订阅消息失败", ex);
            }
        }
    }

    private static void HandleResponse(Heartbeat response)
    {
    }

    private void SendCommand(INetObject command, bool needCheckConnectStatus = true)
    {
        var session = _session ?? throw new InvalidOperationException("事件服务未连接。");
        SendCommand(session, command, needCheckConnectStatus);
    }

    private static void SendCommand(ClientSession session, INetObject command, bool needCheckConnectStatus = true)
    {
        if (!session.Outbound.Writer.TryWrite(new OutboundCommand(command, needCheckConnectStatus)))
        {
            throw new InvalidOperationException("客户端发送队列已关闭或已满。");
        }
    }

    private void ScheduleReconnect(ClientSession failedSession)
    {
        lock (_reconnectSync)
        {
            if (!ReferenceEquals(_session, failedSession) ||
                failedSession.Cancellation.IsCancellationRequested ||
                Interlocked.CompareExchange(ref failedSession.ReconnectScheduled, 1, 0) != 0)
            {
                return;
            }

            _reconnectCancellation = new CancellationTokenSource();
            var reconnectCancellation = _reconnectCancellation;
            _reconnectTask = Task.Run(() => ReconnectLoopAsync(reconnectCancellation));
        }
    }

    private async Task ReconnectLoopAsync(CancellationTokenSource reconnectCancellation)
    {
        try
        {
            while (!reconnectCancellation.IsCancellationRequested && !string.IsNullOrWhiteSpace(_host))
            {
                await Task.Delay(_options.ReconnectInterval, reconnectCancellation.Token).ConfigureAwait(false);
                var host = _host;
                var port = _port;
                if (host is null)
                {
                    return;
                }

                await _lifecycleGate.WaitAsync(reconnectCancellation.Token).ConfigureAwait(false);
                try
                {
                    if (await ConnectCoreAsync(host, port, reconnectCancellation.Token).ConfigureAwait(false))
                    {
                        return;
                    }
                }
                finally
                {
                    _lifecycleGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (reconnectCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_reconnectSync)
            {
                if (ReferenceEquals(_reconnectCancellation, reconnectCancellation))
                {
                    _reconnectCancellation = null;
                }

                _reconnectTask = null;
            }

            reconnectCancellation.Dispose();
        }
    }

    private void StopReconnectLoop()
    {
        lock (_reconnectSync)
        {
            _reconnectCancellation?.Cancel();
            _reconnectCancellation = null;
            _reconnectTask = null;
        }
    }

    private async Task StopSessionAsync(ClientSession? session)
    {
        if (session is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref session.Stopped, 1) != 0)
        {
            return;
        }

        session.Cancellation.Cancel();
        session.CommandRegistration?.Dispose();
        session.CommandRegistration = null;
        try
        {
            session.Client?.Stop();
        }
        catch (Exception ex)
        {
            _options.Report("关闭客户端连接失败", ex);
        }

        session.Inbound.Writer.TryComplete();
        session.Outbound.Writer.TryComplete();

        try
        {
            await Task.WhenAll(session.InboundTask, session.OutboundTask, session.HeartbeatTask)
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            _options.Report("等待客户端后台任务退出超时", ex);
        }
        finally
        {
            session.Cancellation.Dispose();
        }
    }

    private void CompleteQueryChannels()
    {
        foreach (var channel in _queryResponseChannels.Values)
        {
            channel.Writer.TryComplete();
        }

        _queryResponseChannels.Clear();
    }

    private void EnsureClientErrorSubscription()
    {
        if (_isSubscribedToClientErrorEvents)
        {
            return;
        }

        EventBus.Default.Subscribe(_clientErrorHandler);
        _isSubscribedToClientErrorEvents = true;
    }

    private void RemoveClientErrorSubscription()
    {
        if (!_isSubscribedToClientErrorEvents)
        {
            return;
        }

        EventBus.Default.Unsubscribe(_clientErrorHandler);
        _isSubscribedToClientErrorEvents = false;
    }

    private CancellationToken GetSessionCancellationToken()
    {
        return _session?.Cancellation.Token ?? CancellationToken.None;
    }

    private string? GetCurrentQueryTaskId(string subject)
    {
        var context = _queryResponseContext.Value;
        return context is not null && string.Equals(context.Subject, subject, StringComparison.Ordinal)
            ? context.TaskId
            : null;
    }

    private void ValidateSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > _options.MaxSubjectLength)
        {
            throw new ArgumentException("主题不能为空且长度不能超过配置上限。", nameof(subject));
        }
    }

    private void ValidateBuffer(byte[]? buffer)
    {
        if (buffer is not null && buffer.Length > _options.MaxMessageSizeBytes)
        {
            throw new ArgumentException("消息体超过配置大小限制。", nameof(buffer));
        }
    }

    private static void ValidateEndpoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("主机地址不能为空。", nameof(host));
        }

        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
    }

    private void Report(string message, Exception? exception = null)
    {
        _options.Report(message, exception);
    }

    private static Channel<SocketCommand> CreateInboundCommandChannel(EventBusOptions options)
    {
        return Channel.CreateBounded<SocketCommand>(new BoundedChannelOptions(options.InboundQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    private static Channel<OutboundCommand> CreateOutboundCommandChannel(EventBusOptions options)
    {
        return Channel.CreateBounded<OutboundCommand>(new BoundedChannelOptions(options.OutboundQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }
}
