using System.Threading.Channels;

// ReSharper disable once CheckNamespace

namespace CodeWF.EventBus.Socket;

public class EventServer : IEventServer
{
    private const int RestartInterval = 3000;

    private readonly Channel<RequestPublish> _needPublishEvents = Channel.CreateUnbounded<RequestPublish>();
    private readonly Channel<RequestQuery> _needQueryEvents = Channel.CreateUnbounded<RequestQuery>();
    private readonly Channel<RequestPublish> _needResponseQueryEvents = Channel.CreateUnbounded<RequestPublish>();

    private readonly ConcurrentDictionary<string, System.Net.Sockets.Socket>
        _querySubjectAndClients = new();

    private readonly ConcurrentDictionary<string, RequestQuery> _querySubjectAndQueries = new();

    private readonly ConcurrentDictionary<string, List<System.Net.Sockets.Socket>>
        _subscribedSubjectAndClients = new();


    private CancellationTokenSource? _cancellationTokenSource;
    private System.Net.Sockets.Socket? _server;

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
                    Debug.WriteLine($"处理客户端连接上线异常：{ex.Message}");
                }
        });
    }

    private void ListenPublish()
    {
        Task.Factory.StartNew(async () =>
        {
            while (_cancellationTokenSource?.IsCancellationRequested == false)
                try
                {
                    if (!_needPublishEvents.Reader.TryRead(out var @event))
                    {
                        var readTask = _needPublishEvents.Reader.WaitToReadAsync(_cancellationTokenSource.Token);
                        if (!await readTask) break;
                        if (!_needPublishEvents.Reader.TryRead(out @event)) continue;
                    }

                    if (!_subscribedSubjectAndClients.TryGetValue(@event.Subject, out var clients)) continue;

                    var updateEvent = new UpdateEvent
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
                            Debug.WriteLine($"处理发布异常：{ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"处理发布异常：{ex.Message}");
                }

            await Task.CompletedTask;
        });
    }

    private void ListenQuery()
    {
        Task.Factory.StartNew(async () =>
        {
            while (_cancellationTokenSource?.IsCancellationRequested == false)
                try
                {
                    if (!_needQueryEvents.Reader.TryRead(out var query))
                    {
                        var readTask = _needQueryEvents.Reader.WaitToReadAsync(_cancellationTokenSource.Token);
                        if (!await readTask) break;
                        if (!_needQueryEvents.Reader.TryRead(out query)) continue;
                    }

                    if (!_subscribedSubjectAndClients.TryGetValue(query.Subject, out var clients)) continue;

                    var updateEvent = new UpdateEvent
                    {
                        TaskId = SocketHelper.GetNewTaskId(),
                        Subject = query.Subject,
                        Buffer = query.Buffer
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
                            Debug.WriteLine($"处理查询异常：{ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"处理查询异常：{ex.Message}");
                }

            await Task.CompletedTask;
        });
    }

    private void ListenResponseQuery()
    {
        Task.Factory.StartNew(async () =>
        {
            while (_cancellationTokenSource?.IsCancellationRequested == false)
                try
                {
                    if (!_needResponseQueryEvents.Reader.TryRead(out var response))
                    {
                        var readTask = _needResponseQueryEvents.Reader.WaitToReadAsync(_cancellationTokenSource.Token);
                        if (!await readTask) break;
                        if (!_needResponseQueryEvents.Reader.TryRead(out response)) continue;
                    }

                    if (!_querySubjectAndClients.TryGetValue(response.Subject, out var client)
                        || !_querySubjectAndQueries.TryGetValue(response.Subject, out var query))
                        continue;

                    var updateEvent = new UpdateEvent
                    {
                        TaskId = query!.TaskId,
                        Subject = response.Subject,
                        Buffer = response.Buffer
                    };

                    try
                    {
                        SendCommand(client!, updateEvent);
                    }
                    catch (SocketException ex)
                    {
                        RemoveClient(client!);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"处理查询响应异常：{ex.Message}");
                    }
                    finally
                    {
                        _querySubjectAndClients.TryRemove(response.Subject, out _);
                        _querySubjectAndQueries.TryRemove(response.Subject, out _);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"处理查询响应异常：{ex.Message}");
                }

            await Task.CompletedTask;
        });
    }

    private void HandleClient(System.Net.Sockets.Socket tcpClient)
    {
        Task.Factory.StartNew(async () =>
        {
            while (_cancellationTokenSource?.IsCancellationRequested == false)
                try
                {
                    var (success, buffer, headInfo) = await tcpClient.ReadPacketAsync(_cancellationTokenSource.Token);
                    if (!success) break;

                    if (headInfo.IsNetObject<RequestIsEventServer>())
                        HandleRequest(tcpClient, buffer.Deserialize<RequestIsEventServer>());
                    else if (headInfo.IsNetObject<RequestSubscribe>())
                        HandleRequest(tcpClient, buffer.Deserialize<RequestSubscribe>());
                    else if (headInfo.IsNetObject<RequestUnsubscribe>())
                        HandleRequest(tcpClient, buffer.Deserialize<RequestUnsubscribe>());
                    else if (headInfo.IsNetObject<RequestPublish>())
                        HandleRequest(tcpClient, buffer.Deserialize<RequestPublish>());
                    else if (headInfo.IsNetObject<RequestQuery>())
                        HandleRequest(tcpClient, buffer.Deserialize<RequestQuery>());
                    else if (headInfo.IsNetObject<Heartbeat>())
                        HandleRequest(tcpClient, buffer.Deserialize<Heartbeat>());
                }
                catch (SocketException ex)
                {
                    Debug.WriteLine($"远程主机异常，客户端将被移除：{ex.Message}");
                    RemoveClient(tcpClient);
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"接收数据异常：{ex.Message}");
                }

            await Task.CompletedTask;
        });
    }

    private void RemoveClient(System.Net.Sockets.Socket tcpClient)
    {
        var key = tcpClient.RemoteEndPoint!.ToString()!;
        foreach (var subject in _subscribedSubjectAndClients) subject.Value.Remove(tcpClient);
    }

    private void HandleRequest(System.Net.Sockets.Socket tcpClient, RequestIsEventServer command)
    {
        try
        {
            SendCommand(tcpClient, new ResponseCommon
            {
                TaskId = command.TaskId,
                Status = (byte)ResponseCommonStatus.Success
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"发送命令：{ex.Message}");
        }
    }

    private void HandleRequest(System.Net.Sockets.Socket client, RequestSubscribe command)
    {
        try
        {
            if (!_subscribedSubjectAndClients.TryGetValue(command.Subject, out var sockets))
            {
                sockets = [client];
                _subscribedSubjectAndClients.TryAdd(command.Subject, sockets);
            }
            else // if (!sockets.Contains(client))
            {
                sockets.Add(client);
            }


            SendCommand(client, new ResponseCommon
            {
                TaskId = command.TaskId,
                Status = (byte)ResponseCommonStatus.Success
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Send command：{ex.Message}");
        }
    }

    private void HandleRequest(System.Net.Sockets.Socket client, RequestUnsubscribe command)
    {
        try
        {
            if (_subscribedSubjectAndClients.TryGetValue(command.Subject, out var sockets)) sockets.Remove(client);

            SendCommand(client, new ResponseCommon
            {
                TaskId = command.TaskId,
                Status = (byte)ResponseCommonStatus.Success
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Send command：{ex.Message}");
        }
    }

    private void HandleRequest(System.Net.Sockets.Socket client, RequestPublish command)
    {
        try
        {
            if (_querySubjectAndClients.TryGetValue(command.Subject, out _))
            {
                _needResponseQueryEvents.Writer.WriteAsync(command);
                return;
            }

            _needPublishEvents.Writer.WriteAsync(command);

            SendCommand(client, new ResponseCommon
            {
                TaskId = command.TaskId,
                Status = (byte)ResponseCommonStatus.Success
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Send command：{ex.Message}");
        }
    }

    private void HandleRequest(System.Net.Sockets.Socket client, RequestQuery query)
    {
        try
        {
            _querySubjectAndClients[query.Subject] = client;
            _querySubjectAndQueries[query.Subject] = query;
            _needQueryEvents.Writer.WriteAsync(query);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Send command：{ex.Message}");
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
            Debug.WriteLine($"Send command：{ex.Message}");
        }
    }

    private void SendCommand(System.Net.Sockets.Socket client, INetObject command)
    {
        var buffer = command.Serialize(DateTime.Now.ToFileTimeUtc());
        client.Send(buffer);
    }

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
                try
                {
                    _server = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream,
                        ProtocolType.Tcp);
                    _server.Bind(ipEndPort);
                    _server.Listen(10);

                    ListenForClients();
                    ListenPublish();
                    ListenQuery();
                    ListenResponseQuery();
                    ConnectStatus = ConnectStatus.Connected;
                    break;
                }
                catch (Exception ex)
                {
                    ConnectStatus = ConnectStatus.Disconnected;
                    Debug.WriteLine(
                        $"TCP服务启动异常，将在 {RestartInterval / 1000} 秒后重启：{ex.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(RestartInterval));
                }
        }, _cancellationTokenSource.Token);
    }

    public Task StartAsync(string? host, int port, CancellationTokenSource? cancellationToken = null)
    {
        ConnectStatus = ConnectStatus.IsConnecting;
        _cancellationTokenSource = cancellationToken ?? new CancellationTokenSource();
        var ipEndPort = string.IsNullOrWhiteSpace(host)
            ? new IPEndPoint(IPAddress.Any, port)
            : new IPEndPoint(IPAddress.Parse(host), port);

        return Task.Factory.StartNew(async () =>
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
                try
                {
                    _server = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream,
                        ProtocolType.Tcp);
                    _server.Bind(ipEndPort);
                    _server.Listen(10);

                    ListenForClients();
                    ListenPublish();
                    ListenQuery();
                    ListenResponseQuery();
                    ConnectStatus = ConnectStatus.Connected;
                    break;
                }
                catch (Exception ex)
                {
                    ConnectStatus = ConnectStatus.Disconnected;
                    Debug.WriteLine(
                        $"TCP service startup exception, will restart in {RestartInterval / 1000} seconds：{ex.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(RestartInterval));
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
}