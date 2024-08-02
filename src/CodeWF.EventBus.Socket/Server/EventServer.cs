﻿// ReSharper disable once CheckNamespace

namespace CodeWF.EventBus.Socket;

public class EventServer : IEventServer
{
    private System.Net.Sockets.Socket? _server;

    private readonly ConcurrentDictionary<string, List<System.Net.Sockets.Socket>>
        _subjectAndClients = new();

    private readonly BlockingCollection<RequestPublish> _needPublishEvents = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private const int RestartInterval = 3000;

    #region interface methods

    public ConnectStatus ConnectStatus { get; private set; }

    public void Start(string? host, int port)
    {
        ConnectStatus = ConnectStatus.IsConnecting;
        _cancellationTokenSource = new CancellationTokenSource();
        var ipEndPort = string.IsNullOrWhiteSpace(host)
            ? new IPEndPoint(IPAddress.Any, port)
            : new IPEndPoint(IPAddress.Parse(host), port);

        Task.Factory.StartNew(async () =>
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    _server = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream,
                        ProtocolType.Tcp);
                    _server.Bind(ipEndPort);
                    _server.Listen(10);

                    ListenForClients();
                    ListenPublish();
                    ConnectStatus = ConnectStatus.Connected;
                    break;
                }
                catch (Exception ex)
                {
                    ConnectStatus = ConnectStatus.Disconnected;
                    Debug.Write(
                        $"TCP service startup exception, will restart in {RestartInterval / 1000} seconds：{ex.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(RestartInterval));
                }
            }
        }, _cancellationTokenSource.Token);
    }

    public void Stop()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _server?.Dispose();
            _server = null;
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

    #endregion

    private void ListenForClients()
    {
        Task.Factory.StartNew(async () =>
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
        Task.Factory.StartNew(async () =>
        {
            while (_cancellationTokenSource?.IsCancellationRequested == false)
            {
                try
                {
                    if (!_needPublishEvents.TryTake(out var @event, TimeSpan.FromMilliseconds(10))) continue;

                    if (!_subjectAndClients.TryGetValue(@event.Subject, out var clients)) continue;

                    var updateEvent = new UpdateEvent()
                    {
                        TaskId = @event.TaskId,
                        Subject = @event.Subject,
                        Buffer = @event.Buffer
                    };

                    for (var i = clients.Count - 1; i >= 0; i--)
                    {
                        var client = clients[i];
                        try
                        {
                            SendCommand(client, updateEvent);
                        }
                        catch (SocketException ex)
                        {
                            RemoveClient(client);
                        }
                        catch (Exception ex)
                        {
                            Debug.Write($"Handle publish exception：{ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Write($"Handle publish exception：{ex.Message}");
                }
            }

            await Task.CompletedTask;
        });
    }

    private void HandleClient(System.Net.Sockets.Socket tcpClient)
    {
        Task.Factory.StartNew(async () =>
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
                        else if (headInfo.IsNetObject<RequestQuery>())
                        {
                            //HandleRequest(tcpClient, buffer.Deserialize<RequestQuery>());
                        }
                        else if (headInfo.IsNetObject<Heartbeat>())
                        {
                            HandleRequest(tcpClient, buffer.Deserialize<Heartbeat>());
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
            else // if (!sockets.Contains(client))
            {
                sockets.Add(client);
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

    private void HandleRequest(System.Net.Sockets.Socket client, Heartbeat command)
    {
        try
        {
            SendCommand(client, command);
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