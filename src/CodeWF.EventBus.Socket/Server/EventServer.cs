using CodeWF.EventBus.Socket.Models.Responses;

namespace CodeWF.EventBus.Socket.Server;

public class EventServer : IEventServer
{
    private System.Net.Sockets.Socket? _server;

    private readonly ConcurrentDictionary<string, List<System.Net.Sockets.Socket>>
        _subjectAndClients = new();

    private readonly BlockingCollection<RequestPublish> _needPublishEvents = new();
    private CancellationTokenSource? _cancellationTokenSource;

    #region interface methods

    public void Start(string? host, int port)
    {
        var ipEndPort = string.IsNullOrWhiteSpace(host)
            ? new IPEndPoint(IPAddress.Any, port)
            : new IPEndPoint(IPAddress.Parse(host), port);
        _server = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _server.Bind(ipEndPort);
        _server.Listen(10);

        _cancellationTokenSource = new CancellationTokenSource();
        ListenForClients();
        ListenPublish();
    }

    public void Stop()
    {
        try
        {
            _server?.Dispose();
            _server = null;
            _cancellationTokenSource?.Cancel();
        }
        catch
        {
            // ignored
        }
    }

    #endregion

    private void ListenForClients()
    {
        Task.Run(async () =>
        {
            while (_cancellationTokenSource?.IsCancellationRequested == false)
                try
                {
                    var socketClient = await _server!.AcceptAsync();
                    HandleClient(socketClient);
                }
                catch (Exception ex)
                {
                    Debug.Write($"Handle client connection online exception：{ex.Message}");
                }
        });
    }

    private void ListenPublish()
    {
        Task.Run(async () =>
        {
            while (_cancellationTokenSource?.IsCancellationRequested == false)
            {
                if (!_needPublishEvents.TryTake(out var @event, TimeSpan.FromMilliseconds(10))) continue;

                if (!_subjectAndClients.TryGetValue(@event.Subject, out var clients)) continue;

                var updateEvent = new UpdateEvent()
                {
                    TaskId = @event.TaskId,
                    Subject = @event.Subject,
                    Message = @event.Message
                };

                foreach (var client in clients)
                {
                    try
                    {
                        SendCommand(client, updateEvent);
                    }
                    catch (Exception ex)
                    {
                        Debug.Write($"Handle publish exception：{ex.Message}");
                    }
                }
            }

            await Task.CompletedTask;
        });
    }

    private void HandleClient(System.Net.Sockets.Socket tcpClient)
    {
        Task.Run(async () =>
        {
            while (_cancellationTokenSource?.IsCancellationRequested == false)
            {
                try
                {
                    if (tcpClient.ReadPacket(out var buffer, out var headInfo))
                    {
                        if (headInfo.IsNetObject<RequestIsEventServer>())
                        {
                            HandleRequest(tcpClient, buffer.Deserialize<RequestIsEventServer>());
                        }
                        else if (headInfo.IsNetObject<RequestSubscribe>())
                        {
                            HandleRequest(tcpClient, buffer.Deserialize<RequestSubscribe>());
                        }
                        else if (headInfo.IsNetObject<RequestUnsubscribe>())
                        {
                            HandleRequest(tcpClient, buffer.Deserialize<RequestUnsubscribe>());
                        }
                        else if (headInfo.IsNetObject<RequestPublish>())
                        {
                            HandleRequest(tcpClient, buffer.Deserialize<RequestPublish>());
                        }
                    }
                }
                catch (SocketException ex)
                {
                    Debug.Write($"Remote host exception, the client will be removed：{ex.Message}");
                    RemoveClient(tcpClient);
                    break;
                }
                catch (Exception ex)
                {
                    Debug.Write($"接收数据异常：{ex.Message}");
                }
            }

            await Task.CompletedTask;
        });
    }

    private void RemoveClient(System.Net.Sockets.Socket tcpClient)
    {
        var key = tcpClient.RemoteEndPoint!.ToString()!;
        foreach (var subject in _subjectAndClients)
        {
            subject.Value.Remove(tcpClient);
        }
    }

    private void HandleRequest(System.Net.Sockets.Socket tcpClient, RequestIsEventServer command)
    {
        try
        {
            SendCommand(tcpClient, new ResponseCommon()
            {
                TaskId = command.TaskId,
                Status = (byte)ResponseCommonStatus.Success
            });
        }
        catch (Exception ex)
        {
            Debug.Write($"Send command：{ex.Message}");
        }
    }

    private void HandleRequest(System.Net.Sockets.Socket client, RequestSubscribe command)
    {
        try
        {
            if (!_subjectAndClients.TryGetValue(command.Subject, out var sockets))
            {
                sockets = [client];
                _subjectAndClients.TryAdd(command.Subject, sockets);
            }

            sockets.Add(client);

            SendCommand(client, new ResponseCommon()
            {
                TaskId = command.TaskId,
                Status = (byte)ResponseCommonStatus.Success
            });
        }
        catch (Exception ex)
        {
            Debug.Write($"Send command：{ex.Message}");
        }
    }

    private void HandleRequest(System.Net.Sockets.Socket client, RequestUnsubscribe command)
    {
        try
        {
            if (_subjectAndClients.TryGetValue(command.Subject, out var sockets))
            {
                sockets.Remove(client);
            }

            SendCommand(client, new ResponseCommon()
            {
                TaskId = command.TaskId,
                Status = (byte)ResponseCommonStatus.Success
            });
        }
        catch (Exception ex)
        {
            Debug.Write($"Send command：{ex.Message}");
        }
    }

    private void HandleRequest(System.Net.Sockets.Socket client, RequestPublish command)
    {
        try
        {
            _needPublishEvents.Add(command);

            SendCommand(client, new ResponseCommon()
            {
                TaskId = command.TaskId,
                Status = (byte)ResponseCommonStatus.Success
            });
        }
        catch (Exception ex)
        {
            Debug.Write($"Send command：{ex.Message}");
        }
    }

    private void SendCommand(System.Net.Sockets.Socket client, INetObject command)
    {
        var buffer = command.Serialize(DateTime.Now.ToFileTimeUtc());
        client.Send(buffer);
    }
}