using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using Heartbeat = CodeWF.EventBus.Socket.Models.Heartbeat;

// ReSharper disable once CheckNamespace
namespace CodeWF.EventBus.Socket;

public class EventClient : IEventClient
{
    private const int ReconnectInterval = 3000;
    private readonly ConcurrentDictionary<string, UpdateEvent?> _queryTaskIdAndResponse = new();

    private readonly BlockingCollection<SocketCommand> _responses = new(new ConcurrentQueue<SocketCommand>());

    private readonly ConcurrentDictionary<string, List<Delegate>> _subjectAndHandlers = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private System.Net.Sockets.Socket? _client;
    private string? _host;
    private int _port;

    private string? _requestIsEventServerTaskId;

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
                    Debug.WriteLine(
                        $"TCP服务连接异常，将在 {ReconnectInterval / 1000} 秒后重新连接：{ex.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(ReconnectInterval));
                }
                catch (Exception)
                {
                    // TODO Need to handle event services that are connected incorrectly (i.e. event services are not responding to its type correctly)
                }
        }, _cancellationTokenSource.Token);
        return ConnectStatus.Connected == ConnectStatus;
    }

    private void Reconnect()
    {
        Disconnect();
        Connect(_host!, _port);
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
    }

    public void Subscribe<T>(string subject, Action<T> eventHandler)
    {
        AddSubscribe(subject, eventHandler);
    }

    public void Subscribe<T>(string subject, Func<T, Task> asyncEventHandler)
    {
        AddSubscribe(subject, asyncEventHandler);
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
            SendCommand(new RequestPublish
            {
                TaskId = SocketHelper.GetNewTaskId(),
                Subject = subject,
                Buffer = message.SerializeObject(typeof(T))
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

            // 使用TaskCompletionSource实现异步等待响应
            var tcs = new TaskCompletionSource<UpdateEvent>();

            // 创建一个定时器用于超时处理
            using var timeoutTimer = new Timer(_ => { tcs.TrySetException(new Exception("查询超时，请重试！")); }, null,
                overtimeMilliseconds, Timeout.Infinite);

            // 创建一个安全的取消令牌
            var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

            // 循环检查是否有响应，使用异步等待避免线程阻塞
            while (_cancellationTokenSource is null || !_cancellationTokenSource.IsCancellationRequested)
            {
                // 检查是否有响应
                if (_queryTaskIdAndResponse.TryGetValue(taskId, out var responseEvent) && responseEvent != default)
                {
                    // 取消定时器
                    timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    tcs.TrySetResult(responseEvent);
                    break;
                }

                // 异步等待10毫秒，使用安全的取消令牌
                await Task.Delay(10, cancellationToken);

                // 检查是否已经超时
                if (tcs.Task.IsCompleted)
                {
                    break;
                }
            }

            // 如果循环退出且TaskCompletionSource未完成，则检查状态
            if (!tcs.Task.IsCompleted)
            {
                if (_cancellationTokenSource?.IsCancellationRequested == true)
                {
                    tcs.TrySetException(new OperationCanceledException());
                }
                else
                {
                    // 如果不是取消，可能是连接断开或其他原因，设置超时
                    timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    tcs.TrySetException(new Exception("查询超时，请重试！"));
                }
            }

            // 等待完成
            var updateEvent = await tcs.Task;
            if (updateEvent != null && updateEvent.Buffer != null)
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

    // 保留同步版本，但内部使用异步实现，避免死锁
    public TResponse? Query<TQuery, TResponse>(string subject, TQuery message, out string errorMessage,
        int overtimeMilliseconds = 3000)
    {
        try
        {
            // 使用ConfigureAwait(false)避免死锁
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

    private void ListenForServer()
    {
        Task.Factory.StartNew(async () =>
        {
            while (_client != null && _cancellationTokenSource is { IsCancellationRequested: false })
                try
                {
                    var (success, buffer, headInfo) = await _client.ReadPacketAsync(_cancellationTokenSource.Token);
                    if (!success) break;

                    _responses.Add(new SocketCommand(headInfo, buffer, _client));
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

            return Task.CompletedTask;
        });
    }

    private void CheckResponse()
    {
        Task.Factory.StartNew(() =>
        {
            while (_cancellationTokenSource is { IsCancellationRequested: false })
            {
                if (!_responses.TryTake(out var response, TimeSpan.FromMilliseconds(10))) continue;

                if (response.IsMessage<ResponseCommon>())
                    HandleResponse(response.Message<ResponseCommon>());
                else if (response.IsMessage<UpdateEvent>()) HandleResponse(response.Message<UpdateEvent>());
                else if (response.IsMessage<Heartbeat>()) HandleResponse(response.Message<Heartbeat>());
            }
        });
    }

    private const int HeartbeatInterval = 5000;
    private const int MaxTrySendHeartTime = 3;
    private int _trySendHeartbeatTimes;

    private void SendHeartbeat()
    {
        Task.Factory.StartNew(async () =>
        {
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
        SendCommand(new RequestIsEventServer { TaskId = _requestIsEventServerTaskId },
            false);

        var timeoutTask = Task.Delay(3000); // 默认3秒超时
        var completionTask = new TaskCompletionSource<bool>();

        // 使用异步方式等待响应，避免线程阻塞
        var checkTask = Task.Run(async () =>
        {
            while (_cancellationTokenSource is { IsCancellationRequested: false } &&
                   _requestIsEventServerTaskId != null)
            {
                await Task.Delay(10); // 异步等待10毫秒
            }

            completionTask.SetResult(true);
        });

        // 等待完成或超时
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

    // 保留原有方法用于向后兼容
    private void CheckIsEventServer()
    {
        CheckIsEventServerAsync().Wait();
    }

    private void HandleResponse(ResponseCommon response)
    {
        if (_requestIsEventServerTaskId == response.TaskId) _requestIsEventServerTaskId = null;
    }

    private void HandleResponse(UpdateEvent response)
    {
        if (_queryTaskIdAndResponse.ContainsKey(response.TaskId))
        {
            _queryTaskIdAndResponse[response.TaskId] = response;
            return;
        }

        if (!_subjectAndHandlers.TryGetValue(response.Subject, out var handlers)) return;
        foreach (var handler in handlers)
            try
            {
                var param1 = handler.Method.GetParameters().First();
                var param1Type = param1.ParameterType;
                var param1Value = response.Buffer?.DeserializeObject(param1Type);
                if (handler.Method.ReturnType == typeof(Task))
                    ((Task)handler.DynamicInvoke(param1Value)).GetAwaiter().GetResult();
                else
                    handler.DynamicInvoke(param1Value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"发送数据异常：{ex.Message}");
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

    #endregion
}