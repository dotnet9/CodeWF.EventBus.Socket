using System.Threading.Channels;

// ReSharper disable once CheckNamespace
namespace CodeWF.EventBus.Socket;

public class EventServer : IEventServer
{
    private const int RestartInterval = 3000;

    private readonly Channel<RequestPublish> _needPublishEvents = Channel.CreateUnbounded<RequestPublish>();
    private readonly Channel<RequestQuery> _needQueryEvents = Channel.CreateUnbounded<RequestQuery>();
    // 待响应查询表：键为请求 TaskId，值为原始请求方连接。
    // 响应端回包时，服务端据此把结果定向返回给真正的请求方。
    private readonly ConcurrentDictionary<string, PendingQuery> _pendingQueries = new();
    private readonly ConcurrentDictionary<string, List<System.Net.Sockets.Socket>> _subscribedSubjectAndClients = new();

    private CancellationTokenSource? _cancellationTokenSource;
    private System.Net.Sockets.Socket? _server;

    private sealed record PendingQuery(string Subject, System.Net.Sockets.Socket Client);

    #region interface methods

    public ConnectStatus ConnectStatus { get; private set; }

    public void Start(string? host, int port)
    {
        ConnectStatus = ConnectStatus.IsConnecting;
        _cancellationTokenSource = new CancellationTokenSource();
        var ipEndPoint = CreateEndPoint(host, port);

        _ = Task.Run(async () =>
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    _server = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream,
                        ProtocolType.Tcp);
                    _server.Bind(ipEndPoint);
                    _server.Listen(10);

                    ListenForClients();
                    ListenPublish();
                    ListenQuery();
                    ConnectStatus = ConnectStatus.Connected;
                    break;
                }
                catch (Exception ex)
                {
                    ConnectStatus = ConnectStatus.Disconnected;
                    Debug.WriteLine($"TCP服务启动异常，将在 {RestartInterval / 1000} 秒后重启：{ex.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(RestartInterval));
                }
            }
        }, _cancellationTokenSource.Token);
    }

    public Task StartAsync(string? host, int port, CancellationTokenSource? cancellationToken = null)
    {
        ConnectStatus = ConnectStatus.IsConnecting;
        _cancellationTokenSource = cancellationToken ?? new CancellationTokenSource();
        var ipEndPoint = CreateEndPoint(host, port);

        return Task.Run(async () =>
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    _server = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream,
                        ProtocolType.Tcp);
                    _server.Bind(ipEndPoint);
                    _server.Listen(10);

                    ListenForClients();
                    ListenPublish();
                    ListenQuery();
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

    #region private methods

    private static IPEndPoint CreateEndPoint(string? host, int port)
    {
        return string.IsNullOrWhiteSpace(host)
            ? new IPEndPoint(IPAddress.Any, port)
            : new IPEndPoint(IPAddress.Parse(host), port);
    }

    private void ListenForClients()
    {
        _ = Task.Run(async () =>
        {
            while (_cancellationTokenSource?.IsCancellationRequested == false)
            {
                try
                {
                    var socketClient = await _server!.AcceptAsync();
                    HandleClient(socketClient);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"处理客户端连接上线异常：{ex.Message}");
                }
            }
        });
    }

    private void ListenPublish()
    {
        _ = Task.Run(async () =>
        {
            // 普通发布事件走广播通道，查询响应不会进入这里。
            while (_cancellationTokenSource?.IsCancellationRequested == false)
            {
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
                        // 普通发布只需要广播给订阅者，不应触发查询响应逻辑。
                        IsQueryRequest = false,
                        Buffer = @event.Buffer
                    };

                    for (var i = clients.Count - 1; i >= 0; i--)
                    {
                        var client = clients[i];
                        try
                        {
                            SendCommand(client, updateEvent);
                        }
                        catch (SocketException)
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
            }
        });
    }

    private void ListenQuery()
    {
        _ = Task.Run(async () =>
        {
            // 查询请求会被转发给同主题订阅者，但保留原 TaskId，后续靠它做响应关联。
            while (_cancellationTokenSource?.IsCancellationRequested == false)
            {
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
                        TaskId = query.TaskId,
                        Subject = query.Subject,
                        // 客户端据此知道当前正在处理“查询请求”，后续 Publish 需要回填 QueryTaskId。
                        IsQueryRequest = true,
                        Buffer = query.Buffer
                    };

                    for (var i = clients.Count - 1; i >= 0; i--)
                    {
                        var client = clients[i];
                        try
                        {
                            SendCommand(client, updateEvent);
                        }
                        catch (SocketException)
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
            }
        });
    }

    private void HandleClient(System.Net.Sockets.Socket tcpClient)
    {
        _ = Task.Run(async () =>
        {
            while (_cancellationTokenSource?.IsCancellationRequested == false)
            {
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
            }
        });
    }

    private void RemoveClient(System.Net.Sockets.Socket tcpClient)
    {
        // 连接失效时，同时清理订阅关系和该连接尚未完成的查询，避免留下悬挂映射。
        foreach (var subject in _subscribedSubjectAndClients)
        {
            subject.Value.Remove(tcpClient);
        }

        foreach (var pendingQuery in _pendingQueries.Where(x => x.Value.Client == tcpClient).ToArray())
        {
            _pendingQueries.TryRemove(pendingQuery.Key, out _);
        }
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
            else
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
            if (_subscribedSubjectAndClients.TryGetValue(command.Subject, out var sockets))
            {
                sockets.Remove(client);
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

    private void HandleRequest(System.Net.Sockets.Socket client, RequestPublish command)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(command.QueryTaskId))
            {
                // 带 QueryTaskId 的 Publish 不是普通广播，而是某个查询的定向响应。
                HandleQueryResponse(client, command);
                return;
            }

            _needPublishEvents.Writer.TryWrite(command);

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
            // 先记住查询是谁发起的，再把请求转给订阅者。
            _pendingQueries[query.TaskId] = new PendingQuery(query.Subject, client);
            _needQueryEvents.Writer.TryWrite(query);
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

    private void HandleQueryResponse(System.Net.Sockets.Socket responder, RequestPublish response)
    {
        if (string.IsNullOrWhiteSpace(response.QueryTaskId))
        {
            return;
        }

        if (!_pendingQueries.TryRemove(response.QueryTaskId, out var pendingQuery))
        {
            // 查询已超时、已返回或请求方已断开时，忽略迟到响应，避免误广播。
            Debug.WriteLine($"收到已过期或重复的查询响应：{response.QueryTaskId}");
            return;
        }

        try
        {
            var updateEvent = new UpdateEvent
            {
                TaskId = response.QueryTaskId,
                Subject = pendingQuery.Subject,
                IsQueryRequest = false,
                Buffer = response.Buffer
            };

            // 查询结果只回给原请求方，不广播给所有订阅者。
            SendCommand(pendingQuery.Client, updateEvent);
            SendCommand(responder, new ResponseCommon
            {
                TaskId = response.TaskId,
                Status = (byte)ResponseCommonStatus.Success
            });
        }
        catch (SocketException)
        {
            RemoveClient(pendingQuery.Client);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"处理查询响应异常：{ex.Message}");
        }
    }

    private void SendCommand(System.Net.Sockets.Socket client, INetObject command)
    {
        var buffer = command.Serialize(DateTime.Now.ToFileTimeUtc());
        client.Send(buffer);
    }

    #endregion
}
