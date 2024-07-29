// ReSharper disable once CheckNamespace

using Heartbeat = CodeWF.EventBus.Socket.Models.Heartbeat;

namespace CodeWF.EventBus.Socket;

public class EventClient : IEventClient
{
    private System.Net.Sockets.Socket? _client;
    private string? _host;
    private int _port;
    private CancellationTokenSource? _cancellationTokenSource;

    private readonly ConcurrentDictionary<string, List<Delegate>> _subjectAndHandlers = new();

    private readonly BlockingCollection<SocketCommand> _responses = new(new ConcurrentQueue<SocketCommand>());

    private int? _requestIsEventServerTaskId;
    private const int ReconnectInterval = 3000;

    #region interface methods

    public ConnectStatus ConnectStatus { get; private set; }

    public void Connect(string host, int port)
    {
        _host = host;
        _port = port;

        _cancellationTokenSource = new CancellationTokenSource();
        var ipEndPoint = new IPEndPoint(IPAddress.Parse(host), port);
        ConnectStatus = ConnectStatus.IsConnecting;

        Task.Run(async () =>
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
                    Debug.Write(
                        $"TCP service connection exception, will reconnect in {ReconnectInterval / 1000} seconds：{ex.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(ReconnectInterval));
                }
                catch (Exception)
                {
                    // TODO Need to handle event services that are connected incorrectly (i.e. event services are not responding to its type correctly)
                }
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
        }
    }

    public void Subscribe<T>(string subject, Action<T> eventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
        {
            handlers = new List<Delegate>();
            _subjectAndHandlers.TryAdd(subject, handlers);

            SendCommand(new RequestSubscribe()
            {
                TaskId = SocketHelper.GetNewTaskId(),
                Subject = subject
            });
        }

        handlers.Add(eventHandler);
    }

    public void Subscribe<T>(string subject, Func<T, Task> asyncEventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
        {
            handlers = new List<Delegate>();
            _subjectAndHandlers.TryAdd(subject, handlers);

            SendCommand(new RequestSubscribe()
            {
                TaskId = SocketHelper.GetNewTaskId(),
                Subject = subject
            });
        }

        handlers.Add(asyncEventHandler);
    }

    public void Unsubscribe<T>(string subject, Action<T> eventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers)) return;
        handlers.Remove(eventHandler);

        if (handlers.Count > 0) return;

        _subjectAndHandlers.TryRemove(subject, out _);
        SendCommand(new RequestUnsubscribe()
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
        SendCommand(new RequestUnsubscribe()
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
                Buffer = message.SerializeObject()
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

    #endregion

    #region private methods

    private void ListenForServer()
    {
        Task.Run(() =>
        {
            while (_client != null && _cancellationTokenSource is { IsCancellationRequested: false })
            {
                try
                {
                    if (_client.ReadPacket(out var buffer, out var headInfo))
                    {
                        _responses.Add(new SocketCommand(headInfo, buffer, _client));
                    }
                }
                catch (SocketException ex)
                {
                    Debug.Write($"Receive data exception：{ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.Write($"Receive data exception：{ex.Message}");
                }
            }

            return Task.CompletedTask;
        });
    }

    private void CheckResponse()
    {
        Task.Run(async () =>
        {
            while (_cancellationTokenSource is { IsCancellationRequested: false })
            {
                if (!_responses.TryTake(out var response, TimeSpan.FromMilliseconds(10))) continue;

                if (response.IsMessage<ResponseCommon>())
                {
                    HandleResponse(response.Message<ResponseCommon>());
                }
                else if (response.IsMessage<UpdateEvent>())
                {
                    HandleResponse(response.Message<UpdateEvent>());
                }
            }
        });
    }

    private const int HeartbeatInterval = 5000;
    private const int MaxTrySendHeartTime = 3;
    private int _trySendHeartbeatTimes;

    private void SendHeartbeat()
    {
        Task.Run(async () =>
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
        SendCommand(new RequestIsEventServer() { TaskId = _requestIsEventServerTaskId.Value }, needCheckConnectStatus: false);

        if (!ActionHelper.CheckOvertime(() =>
            {
                while (_cancellationTokenSource is { IsCancellationRequested: false } &&
                       _requestIsEventServerTaskId != null)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(10));
                }
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
        if (_requestIsEventServerTaskId == response.TaskId)
        {
            _requestIsEventServerTaskId = null;
        }
    }

    private void HandleResponse(UpdateEvent response)
    {
        if (!_subjectAndHandlers.TryGetValue(response.Subject, out var handlers)) return;
        foreach (var handler in handlers)
        {
            try
            {
                var param1 = handler.Method.GetParameters().First();
                var param1Type = param1.ParameterType;
                var param1Value = response.Buffer?.DeserializeObject(param1Type);
                if (handler.Method.ReturnType == typeof(Task))
                {
                    ((Task)handler.DynamicInvoke(param1Value)).GetAwaiter().GetResult();
                }
                else
                {
                    handler.DynamicInvoke(param1Value);
                }
            }
            catch (Exception ex)
            {
                Debug.Write($"Send data exception：{ex.Message}");
            }
        }
    }

    private void SendCommand(INetObject command, bool needCheckConnectStatus = true)
    {
        if (needCheckConnectStatus && ConnectStatus != ConnectStatus.Connected)
        {
            throw new Exception("Event service not connected, unable to send event!");
        }

        var buffer = command.Serialize(DateTime.Now.ToFileTimeUtc());
        _client?.Send(buffer);
    }

    #endregion
}