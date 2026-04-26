using CodeWF.EventBus;

// ReSharper disable once CheckNamespace
namespace CodeWF.EventBus.Socket;

public class EventServer : IEventServer
{
    private const int RestartInterval = 3000;

    // 待响应查询表：键为请求 TaskId，值为原始请求方连接。
    // 响应端回包时，服务端据此把结果定向返回给真正的请求方。
    private readonly ConcurrentDictionary<string, PendingQuery> _pendingQueries = new();
    // 主题订阅表：键为主题，值为订阅该主题的客户端连接集合。
    private readonly ConcurrentDictionary<string, List<System.Net.Sockets.Socket>> _subscribedSubjectAndClients = new();
    private readonly Func<SocketCommand, Task> _socketCommandHandler;

    private Channel<SocketCommand> _inboundCommands = CreateInboundCommandChannel();
    private Channel<OutboundCommand> _outboundCommands = CreateOutboundCommandChannel();
    private CancellationTokenSource? _cancellationTokenSource;
    private TcpSocketServer? _server;
    private bool _isSubscribedToTransportEvents;

    private sealed record PendingQuery(string Subject, System.Net.Sockets.Socket Client);
    private sealed record OutboundCommand(System.Net.Sockets.Socket Client, INetObject Command);

    public EventServer()
    {
        _socketCommandHandler = HandleSocketCommandAsync;
    }

    #region interface methods

    public ConnectStatus ConnectStatus { get; private set; }

    public void Start(string? host, int port)
    {
        ConnectStatus = ConnectStatus.IsConnecting;
        _cancellationTokenSource = new CancellationTokenSource();
        ResetPipelines();
        EnsureTransportSubscriptions();
        var listenIp = string.IsNullOrWhiteSpace(host) ? "0.0.0.0" : host;

        _ = Task.Run(async () =>
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    _server?.StopAsync().GetAwaiter().GetResult();
                    _server = new TcpSocketServer();
                    var (isSuccess, errorMessage) = await _server.StartAsync(nameof(EventServer), listenIp, port);
                    if (!isSuccess)
                    {
                        throw new Exception(errorMessage ?? "事件服务启动失败。");
                    }

                    ConnectStatus = ConnectStatus.Connected;
                    break;
                }
                catch (Exception ex)
                {
                    ConnectStatus = ConnectStatus.Disconnected;
                    Debug.WriteLine($"TCP 服务启动异常，将在 {RestartInterval / 1000} 秒后重启：{ex.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(RestartInterval));
                }
            }
        }, _cancellationTokenSource.Token);
    }

    public async Task StartAsync(string? host, int port, CancellationTokenSource? cancellationToken = null)
    {
        ConnectStatus = ConnectStatus.IsConnecting;
        _cancellationTokenSource = cancellationToken ?? new CancellationTokenSource();
        ResetPipelines();
        EnsureTransportSubscriptions();
        var listenIp = string.IsNullOrWhiteSpace(host) ? "0.0.0.0" : host;

        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                _server?.StopAsync().GetAwaiter().GetResult();
                _server = new TcpSocketServer();
                var (isSuccess, errorMessage) = await _server.StartAsync(nameof(EventServer), listenIp, port);
                if (!isSuccess)
                {
                    throw new Exception(errorMessage ?? "事件服务启动失败。");
                }

                ConnectStatus = ConnectStatus.Connected;
                return;
            }
            catch (Exception ex)
            {
                ConnectStatus = ConnectStatus.Disconnected;
                Debug.WriteLine($"TCP 服务启动异常，将在 {RestartInterval / 1000} 秒后重启：{ex.Message}");
                await Task.Delay(TimeSpan.FromMilliseconds(RestartInterval));
            }
        }
    }

    public void Stop()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _server?.StopAsync().GetAwaiter().GetResult();
            _server = null;
        }
        catch
        {
            // ignored
        }
        finally
        {
            ConnectStatus = ConnectStatus.Disconnected;
            _inboundCommands.Writer.TryComplete();
            _outboundCommands.Writer.TryComplete();
            _pendingQueries.Clear();
            _subscribedSubjectAndClients.Clear();
            RemoveTransportSubscriptions();
        }
    }

    #endregion

    #region private methods

    private void EnsureTransportSubscriptions()
    {
        if (_isSubscribedToTransportEvents)
        {
            return;
        }

        EventBus.Default.Subscribe(_socketCommandHandler);
        _isSubscribedToTransportEvents = true;
    }

    private void RemoveTransportSubscriptions()
    {
        if (!_isSubscribedToTransportEvents)
        {
            return;
        }

        EventBus.Default.Unsubscribe(_socketCommandHandler);
        _isSubscribedToTransportEvents = false;
    }

    private Task HandleSocketCommandAsync(SocketCommand command)
    {
        if (!BelongsToCurrentServer(command.Client))
        {
            return Task.CompletedTask;
        }

        // 传输层回调只做快速入队；若服务正在停止，直接忽略残留消息即可。
        _inboundCommands.Writer.TryWrite(command);
        return Task.CompletedTask;
    }

    private bool BelongsToCurrentServer(System.Net.Sockets.Socket? socket)
    {
        if (socket?.LocalEndPoint is not IPEndPoint localEndPoint)
        {
            return false;
        }

        return _server != null && localEndPoint.Port == _server.ServerPort;
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
                var socketClient = command.Client;
                if (socketClient == null)
                {
                    continue;
                }

                try
                {
                    if (command.IsCommand<RequestIsEventServer>())
                    {
                        HandleRequest(socketClient, command.GetCommand<RequestIsEventServer>());
                    }
                    else if (command.IsCommand<RequestSubscribe>())
                    {
                        HandleRequest(socketClient, command.GetCommand<RequestSubscribe>());
                    }
                    else if (command.IsCommand<RequestUnsubscribe>())
                    {
                        HandleRequest(socketClient, command.GetCommand<RequestUnsubscribe>());
                    }
                    else if (command.IsCommand<RequestPublish>())
                    {
                        HandleRequest(socketClient, command.GetCommand<RequestPublish>());
                    }
                    else if (command.IsCommand<RequestQuery>())
                    {
                        HandleRequest(socketClient, command.GetCommand<RequestQuery>());
                    }
                    else if (command.IsCommand<Heartbeat>())
                    {
                        HandleRequest(socketClient, command.GetCommand<Heartbeat>());
                    }
                }
                catch (SocketException ex)
                {
                    Debug.WriteLine($"远程主机异常，客户端将被移除：{ex.Message}");
                    RemoveClient(socketClient);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"处理客户端请求异常：{ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 停止服务时允许消费者自然退出。
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
                    if (_server == null)
                    {
                        continue;
                    }

                    await _server.SendCommandAsync(command.Client, command.Command);
                }
                catch (SocketException ex)
                {
                    Debug.WriteLine($"发送命令异常，客户端将被移除：{ex.Message}");
                    RemoveClient(command.Client);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"发送命令异常：{ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 停止服务时允许消费者自然退出。
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
            Debug.WriteLine($"发送命令异常：{ex.Message}");
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
            else if (!sockets.Contains(client))
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
            Debug.WriteLine($"处理订阅请求异常：{ex.Message}");
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
            Debug.WriteLine($"处理取消订阅请求异常：{ex.Message}");
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

            PublishToSubscribers(command);

            SendCommand(client, new ResponseCommon
            {
                TaskId = command.TaskId,
                Status = (byte)ResponseCommonStatus.Success
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"处理发布请求异常：{ex.Message}");
        }
    }

    private void HandleRequest(System.Net.Sockets.Socket client, RequestQuery query)
    {
        try
        {
            // 先记住查询是谁发起的，再把请求转给订阅者。
            _pendingQueries[query.TaskId] = new PendingQuery(query.Subject, client);
            PublishQueryToSubscribers(query);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"处理查询请求异常：{ex.Message}");
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
            Debug.WriteLine($"处理心跳请求异常：{ex.Message}");
        }
    }

    private void PublishToSubscribers(RequestPublish @event)
    {
        if (!_subscribedSubjectAndClients.TryGetValue(@event.Subject, out var clients))
        {
            return;
        }

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
            SendCommand(clients[i], updateEvent);
        }
    }

    private void PublishQueryToSubscribers(RequestQuery query)
    {
        if (!_subscribedSubjectAndClients.TryGetValue(query.Subject, out var clients))
        {
            return;
        }

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
            SendCommand(clients[i], updateEvent);
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
        catch (Exception ex)
        {
            Debug.WriteLine($"处理查询响应异常：{ex.Message}");
        }
    }

    private void SendCommand(System.Net.Sockets.Socket client, INetObject command)
    {
        if (!_outboundCommands.Writer.TryWrite(new OutboundCommand(client, command)))
        {
            throw new Exception("服务端发送通道已关闭，无法继续发送命令。");
        }
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

    #endregion
}
