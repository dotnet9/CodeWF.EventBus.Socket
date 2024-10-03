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
        _host = host;
        _port = port;

        _cancellationTokenSource = new CancellationTokenSource();
        var ipEndPoint = new IPEndPoint(IPAddress.Parse(host), port);
        ConnectStatus = ConnectStatus.IsConnecting;

        Task.Factory.StartNew(async () =>
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
                        $"TCP service connection exception, will reconnect in {ReconnectInterval / 1000} seconds：{ex.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(ReconnectInterval));
                }
                catch (Exception)
                {
                    // TODO Need to handle event services that are connected incorrectly (i.e. event services are not responding to its type correctly)
                }
        }, _cancellationTokenSource.Token);
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

    public TResponse? Query<TQuery, TResponse>(string subject, TQuery message, out string errorMessage,
        int overtimeMilliseconds)
    {
        errorMessage = string.Empty;
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
            if (!ActionHelper.CheckOvertime(() =>
                {
                    while (_cancellationTokenSource is { IsCancellationRequested: false }
                           && _queryTaskIdAndResponse.TryGetValue(taskId, out var updateEvent)
                           && updateEvent == default)
                        Thread.Sleep(TimeSpan.FromMilliseconds(10));
                }, overtimeMilliseconds))
                throw new Exception("Query timeout, please try again!");

            if (_queryTaskIdAndResponse.TryGetValue(taskId, out var value)
                && value != null)
                return (TResponse)value.Buffer!.DeserializeObject(typeof(TResponse));

            return default;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            Debug.WriteLine(ex.Message);
            return default;
        }
        finally
        {
            _queryTaskIdAndResponse.TryRemove(taskId, out _);
        }
    }

    #endregion

    #region private methods

    private void ListenForServer()
    {
        Task.Factory.StartNew(() =>
        {
            while (_client != null && _cancellationTokenSource is { IsCancellationRequested: false })
                try
                {
                    if (_client.ReadPacket(out var buffer, out var headInfo))
                        _responses.Add(new SocketCommand(headInfo, buffer, _client));
                }
                catch (SocketException ex)
                {
                    Debug.WriteLine($"Receive data exception：{ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Receive data exception：{ex.Message}");
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
                    _trySendHeartbeatTimes++;
                    Debug.WriteLine(
                        $"Sending heartbeat abnormality, will attempt to resend!({MaxTrySendHeartTime - _trySendHeartbeatTimes}/{MaxTrySendHeartTime})");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(HeartbeatInterval));
            }
        });
    }

    private void CheckIsEventServer()
    {
        _requestIsEventServerTaskId = SocketHelper.GetNewTaskId();
        SendCommand(new RequestIsEventServer { TaskId = _requestIsEventServerTaskId },
            false);

        if (!ActionHelper.CheckOvertime(() =>
            {
                while (_cancellationTokenSource is { IsCancellationRequested: false } &&
                       _requestIsEventServerTaskId != null)
                    Thread.Sleep(TimeSpan.FromMilliseconds(10));
            }))
        {
            ConnectStatus = ConnectStatus.DisconnectedNeedCheckEventServer;
            _cancellationTokenSource?.Cancel();
            throw new Exception("Please check if the event bus service is connected correctly");
        }

        ConnectStatus = ConnectStatus.Connected;
        SendHeartbeat();
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
                Debug.WriteLine($"Send data exception：{ex.Message}");
            }
    }

    private void SendCommand(INetObject command, bool needCheckConnectStatus = true)
    {
        if (needCheckConnectStatus && ConnectStatus != ConnectStatus.Connected)
            throw new Exception("Event service not connected, unable to send event!");

        var buffer = command.Serialize(DateTime.Now.ToFileTimeUtc());
        _client?.Send(buffer);
    }

    #endregion
}