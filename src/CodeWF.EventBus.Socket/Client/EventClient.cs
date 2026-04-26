using System.Threading.Channels;
using Heartbeat = CodeWF.EventBus.Socket.Models.Heartbeat;

// ReSharper disable once CheckNamespace
namespace CodeWF.EventBus.Socket;

public class EventClient : IEventClient
{
    private const int ReconnectInterval = 3000;
    private const int HeartbeatInterval = 5000;
    private const int MaxTrySendHeartTime = 3;

    // 当前线程是否处于“处理查询请求”的上下文中。
    // 这样响应端在查询处理器里直接调用 Publish 时，客户端就能自动带上原查询 TaskId。
    private readonly AsyncLocal<QueryResponseContext?> _queryResponseContext = new();
    // 查询发起方本地缓存，键为查询 TaskId，值为服务端回推的最终响应。
    private readonly ConcurrentDictionary<string, UpdateEvent?> _queryTaskIdAndResponse = new();
    private readonly Channel<SocketCommand> _responses = Channel.CreateUnbounded<SocketCommand>();
    private readonly ConcurrentDictionary<string, List<Delegate>> _subjectAndHandlers = new();

    private CancellationTokenSource? _cancellationTokenSource;
    private System.Net.Sockets.Socket? _client;
    private string? _host;
    private int _port;
    private string? _requestIsEventServerTaskId;
    private int _trySendHeartbeatTimes;

    private sealed record QueryResponseContext(string Subject, string TaskId);

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

        _cancellationTokenSource = new CancellationTokenSource();
        var ipEndPoint = new IPEndPoint(IPAddress.Parse(host), port);
        ConnectStatus = ConnectStatus.IsConnecting;

        await Task.Run(async () =>
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    _client = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream,
                        ProtocolType.Tcp);
                    await _client.ConnectAsync(ipEndPoint);

                    ListenForServer();
                    CheckResponse();
                    CheckIsEventServer();
                    break;
                }
                catch (SocketException ex)
                {
                    Debug.WriteLine($"TCP服务连接异常，将在 {ReconnectInterval / 1000} 秒后重新连接：{ex.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(ReconnectInterval));
                }
                catch (Exception)
                {
                    // TODO Need to handle event services that are connected incorrectly (i.e. event services are not responding to its type correctly)
                }
            }
        }, _cancellationTokenSource.Token);

        return ConnectStatus == ConnectStatus.Connected;
    }

    public void Disconnect()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _client?.Disconnect(false);
            _client = null;
        }
        catch
        {
            // ignored
        }
        finally
        {
            ConnectStatus = ConnectStatus.Disconnected;
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
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers)) return;
        handlers.Remove(eventHandler);

        if (handlers.Count > 0) return;

        _subjectAndHandlers.TryRemove(subject, out _);
        SendCommand(new RequestUnsubscribe
        {
            TaskId = SocketHelper.GetNewTaskId(),
            Subject = subject
        });
    }

    public void Unsubscribe<T>(string subject, Func<T, Task> asyncEventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers)) return;
        handlers.Remove(asyncEventHandler);

        if (handlers.Count > 0) return;

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
            // 如果当前 Publish 发生在查询处理器内部，则把原查询 TaskId 透传给服务端，
            // 让服务端把这条消息识别为“查询响应”而不是普通广播事件。
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

    public async Task<(TResponse? Result, string ErrorMessage)> QueryAsync<TQuery, TResponse>(string subject,
        TQuery message, int overtimeMilliseconds)
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
            _queryTaskIdAndResponse[request.TaskId] = default;
            SendCommand(request);

            // 用轮询配合超时控制等待响应，避免同步阻塞调用线程。
            var tcs = new TaskCompletionSource<UpdateEvent>();
            using var timeoutTimer = new Timer(_ => { tcs.TrySetException(new Exception("查询超时，请重试！")); }, null,
                overtimeMilliseconds, Timeout.Infinite);

            var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

            while (_cancellationTokenSource is null || !_cancellationTokenSource.IsCancellationRequested)
            {
                if (_queryTaskIdAndResponse.TryGetValue(taskId, out var responseEvent) && responseEvent != default)
                {
                    timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    tcs.TrySetResult(responseEvent);
                    break;
                }

                await Task.Delay(10, cancellationToken);
                if (tcs.Task.IsCompleted)
                {
                    break;
                }
            }

            if (!tcs.Task.IsCompleted)
            {
                if (_cancellationTokenSource?.IsCancellationRequested == true)
                {
                    tcs.TrySetException(new OperationCanceledException());
                }
                else
                {
                    timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    tcs.TrySetException(new Exception("查询超时，请重试！"));
                }
            }

            var updateEvent = await tcs.Task;
            if (updateEvent.Buffer != null)
            {
                return ((TResponse)updateEvent.Buffer.DeserializeObject(typeof(TResponse)), string.Empty);
            }

            return (default, "未从服务器收到响应");
        }
        catch (OperationCanceledException)
        {
            return (default, "操作已取消");
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            Debug.WriteLine(ex.Message);
            return (default, errorMessage);
        }
        finally
        {
            _queryTaskIdAndResponse.TryRemove(taskId, out _);
        }
    }

    public TResponse? Query<TQuery, TResponse>(string subject, TQuery message, out string errorMessage,
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

    private void ListenForServer()
    {
        _ = Task.Run(async () =>
        {
            // 持续从 Socket 读取完整协议包，再交给响应分发循环统一处理。
            while (_client != null && _cancellationTokenSource is { IsCancellationRequested: false })
            {
                try
                {
                    var (success, buffer, headInfo) = await _client.ReadPacketAsync(_cancellationTokenSource.Token);
                    if (!success) break;

                    await _responses.Writer.WriteAsync(new SocketCommand(headInfo, buffer, _client));
                }
                catch (SocketException ex)
                {
                    Debug.WriteLine($"接收数据异常：{ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"接收数据异常：{ex.Message}");
                }
            }
        });
    }

    private void CheckResponse()
    {
        _ = Task.Run(async () =>
        {
            // 客户端所有入站消息都先进入 Channel，再按消息类型分流，
            // 这样能把网络读取和业务处理解耦。
            while (_cancellationTokenSource is { IsCancellationRequested: false })
            {
                try
                {
                    var readTask = _responses.Reader.WaitToReadAsync(_cancellationTokenSource.Token);
                    if (!await readTask) break;
                    if (!_responses.Reader.TryRead(out var response)) continue;

                    if (response.IsMessage<ResponseCommon>())
                    {
                        HandleResponse(response.Message<ResponseCommon>());
                    }
                    else if (response.IsMessage<UpdateEvent>())
                    {
                        HandleResponse(response.Message<UpdateEvent>());
                    }
                    else if (response.IsMessage<Heartbeat>())
                    {
                        HandleResponse(response.Message<Heartbeat>());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"处理响应异常：{ex.Message}");
                }
            }
        });
    }

    private void SendHeartbeat()
    {
        _ = Task.Run(async () =>
        {
            // 心跳连续失败达到阈值后，认为连接可能已失效，主动触发重连。
            while (_cancellationTokenSource is { IsCancellationRequested: false })
            {
                try
                {
                    if (_trySendHeartbeatTimes >= MaxTrySendHeartTime)
                    {
                        Reconnect();
                        break;
                    }

                    SendCommand(new Heartbeat());
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref _trySendHeartbeatTimes);
                    Debug.WriteLine(
                        $"发送心跳异常，将尝试重新发送！({MaxTrySendHeartTime - _trySendHeartbeatTimes}/{MaxTrySendHeartTime})");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(HeartbeatInterval));
            }
        });
    }

    private async Task CheckIsEventServerAsync()
    {
        _requestIsEventServerTaskId = SocketHelper.GetNewTaskId();
        SendCommand(new RequestIsEventServer { TaskId = _requestIsEventServerTaskId }, false);

        // 连接建立后先做一次轻量握手，确认对端确实是本事件总线服务，而不是普通 TCP 服务。
        var timeoutTask = Task.Delay(3000);
        var completionTask = new TaskCompletionSource<bool>();

        var checkTask = Task.Run(async () =>
        {
            while (_cancellationTokenSource is { IsCancellationRequested: false } &&
                   _requestIsEventServerTaskId != null)
            {
                await Task.Delay(10);
            }

            completionTask.SetResult(true);
        });

        var completedTask = await Task.WhenAny(checkTask, timeoutTask);
        if (completedTask == timeoutTask)
        {
            ConnectStatus = ConnectStatus.DisconnectedNeedCheckEventServer;
            _cancellationTokenSource?.Cancel();
            throw new Exception("请检查事件总线服务是否连接正确");
        }

        ConnectStatus = ConnectStatus.Connected;
        SendHeartbeat();
    }

    private void CheckIsEventServer()
    {
        CheckIsEventServerAsync().GetAwaiter().GetResult();
    }

    private void HandleResponse(ResponseCommon response)
    {
        if (_requestIsEventServerTaskId == response.TaskId)
        {
            _requestIsEventServerTaskId = null;
        }
    }

    private void HandleResponse(UpdateEvent response)
    {
        if (_queryTaskIdAndResponse.ContainsKey(response.TaskId))
        {
            _queryTaskIdAndResponse[response.TaskId] = response;
            return;
        }

        if (!_subjectAndHandlers.TryGetValue(response.Subject, out var handlers)) return;

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
                Debug.WriteLine($"发送数据异常：{ex.Message}");
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
            throw new Exception("事件服务未连接，无法发送事件！");

        var buffer = command.Serialize(DateTime.Now.ToFileTimeUtc());
        _client?.Send(buffer);
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
