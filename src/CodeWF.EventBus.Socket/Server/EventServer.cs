// ReSharper disable once CheckNamespace
namespace CodeWF.EventBus.Socket;

public class EventServer : IEventServer, IDisposable
{
    private const int StartTimeoutMilliseconds = 3000;

    private readonly EventBusOptions _options;
    private readonly ConcurrentDictionary<string, PendingQuery> _pendingQueries = new();
    private readonly ConcurrentDictionary<System.Net.Sockets.Socket, string?> _authorizedClients = new();
    private readonly Dictionary<string, HashSet<System.Net.Sockets.Socket>> _subscribedSubjectAndClients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, OfflineSubscriptionState> _offlineSubscriptions = new();
    private readonly ConcurrentDictionary<System.Net.Sockets.Socket, DateTimeOffset> _lastClientActivity = new();
    private readonly object _subscriptionSync = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private ServerSession? _session;
    private int _disposed;

    private sealed class ServerSession
    {
        public ServerSession(EventBusOptions options)
        {
            Cancellation = new CancellationTokenSource();
            Inbound = CreateInboundCommandChannel(options);
        }

        public CancellationTokenSource Cancellation { get; }
        public Channel<SocketCommand> Inbound { get; }
        public Dictionary<System.Net.Sockets.Socket, ClientOutboundQueue> OutboundQueues { get; } = new();
        public object OutboundSync { get; } = new();
        public TcpSocketServer? Server { get; set; }
        public IDisposable? CommandRegistration { get; set; }
        public Task InboundTask { get; set; } = Task.CompletedTask;
        public Task CleanupTask { get; set; } = Task.CompletedTask;
        public int Stopped;
    }

    private sealed class ClientOutboundQueue
    {
        public ClientOutboundQueue(EventBusOptions options)
        {
            Commands = CreateOutboundCommandChannel(options);
        }

        public Channel<INetObject> Commands { get; }
        public Task Worker { get; set; } = Task.CompletedTask;
    }

    private sealed record PendingQuery(
        string Subject,
        System.Net.Sockets.Socket Client,
        DateTimeOffset ExpiresAt);

    /// <summary>
    /// 客户端断线后的离线订阅状态：按主题缓冲断线期间的广播消息，重连订阅成功后补发。
    /// </summary>
    private sealed class OfflineSubscriptionState
    {
        public object Sync { get; } = new();
        public Dictionary<string, Channel<UpdateEvent>> PendingEvents { get; } = new(StringComparer.Ordinal);
        public DateTimeOffset LastActiveAt { get; set; }
    }

    public EventServer(EventBusOptions? options = null)
    {
        _options = options ?? new EventBusOptions();
        _options.Validate();
    }

    public ConnectStatus ConnectStatus { get; private set; } = ConnectStatus.Disconnected;

    public void Start(string? host, int port)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(StartTimeoutMilliseconds));
        StartAsync(host, port, timeout.Token).GetAwaiter().GetResult();
    }

    public Task StartAsync(string? host, int port, CancellationTokenSource? cancellationToken = null)
    {
        return StartAsync(host, port, cancellationToken?.Token ?? CancellationToken.None);
    }

    public async Task StartAsync(string? host, int port, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var listenIp = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopSessionAsync(_session).ConfigureAwait(false);
            ClearState();
            _session = new ServerSession(_options);
            var session = _session;
            ConnectStatus = ConnectStatus.IsConnecting;
            session.InboundTask = ProcessInboundCommandsAsync(session);
            session.CleanupTask = CleanupPendingQueriesAsync(session);

            try
            {
                var server = new TcpSocketServer();
                session.Server = server;
                var (isSuccess, errorMessage) = await server.StartAsync(
                    nameof(EventServer), listenIp, port).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!isSuccess)
                {
                    throw new InvalidOperationException(errorMessage ?? "事件服务启动失败。");
                }

                session.CommandRegistration = server.RegisterCommandHandler(
                    (clientKey, tcpSession, command) => HandleSocketCommandAsync(session, clientKey, tcpSession, command));
                ConnectStatus = ConnectStatus.Connected;
            }
            catch (OperationCanceledException)
            {
                await StopSessionAsync(session).ConfigureAwait(false);
                ClearState();
                ConnectStatus = ConnectStatus.Disconnected;
                throw;
            }
            catch (Exception ex)
            {
                _options.Report("启动事件服务失败", ex);
                await StopSessionAsync(session).ConfigureAwait(false);
                ClearState();
                ConnectStatus = ConnectStatus.Disconnected;
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Stop()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _lifecycleGate.Wait();
        try
        {
            StopSessionAsync(_session).GetAwaiter().GetResult();
            _session = null;
            ClearState();
            ConnectStatus = ConnectStatus.Disconnected;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifecycleGate.Wait();
        try
        {
            StopSessionAsync(_session).GetAwaiter().GetResult();
            _session = null;
            ClearState();
            ConnectStatus = ConnectStatus.Disconnected;
        }
        finally
        {
            _lifecycleGate.Release();
        }

        GC.SuppressFinalize(this);
    }

    private Task<bool> HandleSocketCommandAsync(
        ServerSession session,
        string clientKey,
        TcpSession tcpSession,
        SocketCommand command)
    {
        if (!ReferenceEquals(_session, session) || session.Cancellation.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        var accepted = session.Inbound.Writer.TryWrite(command);
        if (!accepted)
        {
            _options.Report($"服务端入站队列已满，客户端 {clientKey} 的命令被拒绝");
        }

        return Task.FromResult(accepted);
    }

    private async Task ProcessInboundCommandsAsync(ServerSession session)
    {
        try
        {
            await foreach (var command in session.Inbound.Reader.ReadAllAsync(session.Cancellation.Token)
                               .ConfigureAwait(false))
            {
                var socketClient = command.Client;
                if (socketClient is null)
                {
                    continue;
                }

                _lastClientActivity[socketClient] = DateTimeOffset.UtcNow;

                try
                {
                    if (command.IsCommand<RequestIsEventServer>())
                    {
                        HandleRequest(session, socketClient, command.GetCommand<RequestIsEventServer>());
                    }
                    else if (!IsAuthorized(socketClient))
                    {
                        _options.Report("拒绝未完成握手的客户端命令");
                    }
                    else if (command.IsCommand<RequestSubscribe>())
                    {
                        HandleRequest(session, socketClient, command.GetCommand<RequestSubscribe>());
                    }
                    else if (command.IsCommand<RequestUnsubscribe>())
                    {
                        HandleRequest(session, socketClient, command.GetCommand<RequestUnsubscribe>());
                    }
                    else if (command.IsCommand<RequestPublish>())
                    {
                        HandleRequest(session, socketClient, command.GetCommand<RequestPublish>());
                    }
                    else if (command.IsCommand<RequestQuery>())
                    {
                        HandleRequest(session, socketClient, command.GetCommand<RequestQuery>());
                    }
                    else if (command.IsCommand<Heartbeat>())
                    {
                        HandleRequest(session, socketClient, command.GetCommand<Heartbeat>());
                    }
                }
                catch (Exception ex)
                {
                    _options.Report("处理客户端命令失败", ex);
                }
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessClientOutboundCommandsAsync(
        ServerSession session,
        System.Net.Sockets.Socket client,
        ClientOutboundQueue queue)
    {
        try
        {
            await foreach (var command in queue.Commands.Reader.ReadAllAsync(session.Cancellation.Token)
                               .ConfigureAwait(false))
            {
                try
                {
                    if (session.Server is not null)
                    {
                        await session.Server.SendCommandAsync(client, command).ConfigureAwait(false);
                    }
                }
                catch (SocketException ex)
                {
                    _options.Report("发送服务端命令失败，客户端将被移除", ex);
                    RemoveClient(session, client);
                    break;
                }
                catch (Exception ex)
                {
                    _options.Report("发送服务端命令失败", ex);
                    RemoveClient(session, client);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task CleanupPendingQueriesAsync(ServerSession session)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(session.Cancellation.Token).ConfigureAwait(false))
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var pair in _pendingQueries)
                {
                    if (pair.Value.ExpiresAt > now || !_pendingQueries.TryRemove(pair.Key, out var pendingQuery))
                    {
                        continue;
                    }

                    _ = pendingQuery;
                }

                CleanupExpiredOfflineSubscriptions();
                CleanupIdleClients(session);
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
    }

    private void HandleRequest(ServerSession session, System.Net.Sockets.Socket client, RequestIsEventServer command)
    {
        if (!string.IsNullOrWhiteSpace(_options.AuthenticationToken) &&
            !string.Equals(command.AuthenticationToken, _options.AuthenticationToken, StringComparison.Ordinal))
        {
            SendCommand(session, client, new ResponseCommon
            {
                TaskId = command.TaskId,
                Status = (byte)ResponseCommonStatus.Fail,
                Message = "客户端认证失败。"
            });
            return;
        }

        // 同一 ClientId 的旧连接视为僵尸，先走下线流程（订阅转入离线缓冲），避免重复投递。
        var clientId = string.IsNullOrWhiteSpace(command.ClientId) ? null : command.ClientId.Trim();
        if (clientId is not null)
        {
            foreach (var pair in _authorizedClients.ToArray())
            {
                if (!string.Equals(pair.Value, clientId, StringComparison.Ordinal))
                {
                    continue;
                }

                _options.Report($"客户端 {clientId} 重连，移除旧连接。");
                RemoveClient(session, pair.Key);
            }
        }

        _authorizedClients[client] = clientId;
        SendCommand(session, client, new ResponseCommon
        {
            TaskId = command.TaskId,
            Status = (byte)ResponseCommonStatus.Success
        });
    }

    private void HandleRequest(ServerSession session, System.Net.Sockets.Socket client, RequestSubscribe command)
    {
        ValidateRequest(command.Subject, null);
        lock (_subscriptionSync)
        {
            if (!_subscribedSubjectAndClients.TryGetValue(command.Subject, out var sockets))
            {
                sockets = new HashSet<System.Net.Sockets.Socket>();
                _subscribedSubjectAndClients.Add(command.Subject, sockets);
            }

            sockets.Add(client);
        }

        SendCommand(session, client, new ResponseCommon
        {
            TaskId = command.TaskId,
            Status = (byte)ResponseCommonStatus.Success
        });

        FlushOfflineBuffer(session, client, command.Subject);
    }

    private void HandleRequest(ServerSession session, System.Net.Sockets.Socket client, RequestUnsubscribe command)
    {
        ValidateRequest(command.Subject, null);
        lock (_subscriptionSync)
        {
            if (_subscribedSubjectAndClients.TryGetValue(command.Subject, out var sockets))
            {
                sockets.Remove(client);
                if (sockets.Count == 0)
                {
                    _subscribedSubjectAndClients.Remove(command.Subject);
                }
            }
        }

        SendCommand(session, client, new ResponseCommon
        {
            TaskId = command.TaskId,
            Status = (byte)ResponseCommonStatus.Success
        });
    }

    private void HandleRequest(ServerSession session, System.Net.Sockets.Socket client, RequestPublish command)
    {
        ValidateRequest(command.Subject, command.Buffer);
        if (!string.IsNullOrWhiteSpace(command.QueryTaskId))
        {
            HandleQueryResponse(session, client, command);
            return;
        }

        PublishToSubscribers(session, command);
        SendCommand(session, client, new ResponseCommon
        {
            TaskId = command.TaskId,
            Status = (byte)ResponseCommonStatus.Success
        });
    }

    private void HandleRequest(ServerSession session, System.Net.Sockets.Socket client, RequestQuery query)
    {
        ValidateRequest(query.Subject, query.Buffer);
        var pendingQuery = new PendingQuery(
            query.Subject,
            client,
            DateTimeOffset.UtcNow.Add(_options.PendingQueryTimeout));
        if (!_pendingQueries.TryAdd(query.TaskId, pendingQuery))
        {
            throw new InvalidOperationException("查询 TaskId 已存在。");
        }

        if (!PublishQueryToSubscribers(session, query))
        {
            _pendingQueries.TryRemove(query.TaskId, out _);
        }
    }

    private void HandleRequest(ServerSession session, System.Net.Sockets.Socket client, Heartbeat command)
    {
        SendCommand(session, client, command);
    }

    private void PublishToSubscribers(ServerSession session, RequestPublish @event)
    {
        var clients = GetSubscribers(@event.Subject);
        var updateEvent = new UpdateEvent
        {
            TaskId = @event.TaskId,
            Subject = @event.Subject,
            IsQueryRequest = false,
            Buffer = @event.Buffer
        };

        foreach (var client in clients)
        {
            SendCommand(session, client, updateEvent);
        }

        DeliverToOfflineSubscribers(@event.Subject, updateEvent);
    }

    private void DeliverToOfflineSubscribers(string subject, UpdateEvent updateEvent)
    {
        if (!_options.EnableOfflineMessage)
        {
            return;
        }

        foreach (var state in _offlineSubscriptions.Values.ToArray())
        {
            Channel<UpdateEvent>? buffer;
            lock (state.Sync)
            {
                if (!state.PendingEvents.TryGetValue(subject, out buffer))
                {
                    continue;
                }
            }

            // 缓冲有界（DropOldest），写入失败仅代表状态已被并发清理。
            buffer.Writer.TryWrite(updateEvent);
        }
    }

    private void FlushOfflineBuffer(ServerSession session, System.Net.Sockets.Socket client, string subject)
    {
        if (!_options.EnableOfflineMessage)
        {
            return;
        }

        var clientId = _authorizedClients.TryGetValue(client, out var id) ? id : null;
        if (string.IsNullOrEmpty(clientId) ||
            !_offlineSubscriptions.TryGetValue(clientId, out var state))
        {
            return;
        }

        Channel<UpdateEvent>? buffer;
        lock (state.Sync)
        {
            if (!state.PendingEvents.Remove(subject, out buffer))
            {
                return;
            }
        }

        buffer.Writer.TryComplete();
        while (buffer.Reader.TryRead(out var pendingEvent))
        {
            SendCommand(session, client, pendingEvent);
        }

        lock (state.Sync)
        {
            if (state.PendingEvents.Count == 0)
            {
                _offlineSubscriptions.TryRemove(clientId, out _);
            }
        }
    }

    private bool PublishQueryToSubscribers(ServerSession session, RequestQuery query)
    {
        var clients = GetSubscribers(query.Subject);
        if (clients.Length == 0)
        {
            return false;
        }

        var updateEvent = new UpdateEvent
        {
            TaskId = query.TaskId,
            Subject = query.Subject,
            IsQueryRequest = true,
            Buffer = query.Buffer
        };

        foreach (var client in clients)
        {
            SendCommand(session, client, updateEvent);
        }

        return true;
    }

    private void HandleQueryResponse(
        ServerSession session,
        System.Net.Sockets.Socket responder,
        RequestPublish response)
    {
        if (string.IsNullOrWhiteSpace(response.QueryTaskId) ||
            !_pendingQueries.TryGetValue(response.QueryTaskId, out var pendingQuery) ||
            !string.Equals(pendingQuery.Subject, response.Subject, StringComparison.Ordinal) ||
            !IsSubscribed(response.Subject, responder) ||
            !_pendingQueries.TryRemove(response.QueryTaskId, out pendingQuery))
        {
            _options.Report($"忽略无效或重复的查询响应：{response.QueryTaskId}");
            return;
        }

        SendCommand(session, pendingQuery.Client, new UpdateEvent
        {
            TaskId = response.QueryTaskId,
            Subject = pendingQuery.Subject,
            Buffer = response.Buffer
        });
        SendCommand(session, responder, new ResponseCommon
        {
            TaskId = response.TaskId,
            Status = (byte)ResponseCommonStatus.Success
        });
    }

    private void RemoveClient(ServerSession session, System.Net.Sockets.Socket tcpClient)
    {
        _authorizedClients.TryRemove(tcpClient, out var clientId);
        _lastClientActivity.TryRemove(tcpClient, out _);
        List<string> subscribedSubjects;
        ClientOutboundQueue? queue = null;
        lock (session.OutboundSync)
        {
            if (session.OutboundQueues.Remove(tcpClient, out var removedQueue))
            {
                queue = removedQueue;
            }
        }

        queue?.Commands.Writer.TryComplete();
        lock (_subscriptionSync)
        {
            subscribedSubjects = _subscribedSubjectAndClients
                .Where(pair => pair.Value.Contains(tcpClient))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var subject in _subscribedSubjectAndClients.Keys.ToArray())
            {
                var sockets = _subscribedSubjectAndClients[subject];
                sockets.Remove(tcpClient);
                if (sockets.Count == 0)
                {
                    _subscribedSubjectAndClients.Remove(subject);
                }
            }
        }

        foreach (var pendingQuery in _pendingQueries.Where(x => x.Value.Client == tcpClient).ToArray())
        {
            _pendingQueries.TryRemove(pendingQuery.Key, out _);
        }

        // 断线的订阅转入离线缓冲，断线期间的广播暂存，重连订阅成功后补发。
        if (clientId is null ||
            !_options.EnableOfflineMessage ||
            subscribedSubjects.Count == 0)
        {
            return;
        }

        var state = _offlineSubscriptions.GetOrAdd(clientId, _ => new OfflineSubscriptionState());
        lock (state.Sync)
        {
            foreach (var subject in subscribedSubjects)
            {
                if (state.PendingEvents.ContainsKey(subject))
                {
                    continue;
                }

                state.PendingEvents[subject] = Channel.CreateBounded<UpdateEvent>(
                    new BoundedChannelOptions(_options.OfflineMessageCapacity)
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        FullMode = BoundedChannelFullMode.DropOldest
                    });
            }

            state.LastActiveAt = DateTimeOffset.UtcNow;
        }
    }

    private void SendCommand(ServerSession session, System.Net.Sockets.Socket client, INetObject command)
    {
        ClientOutboundQueue queue;
        lock (session.OutboundSync)
        {
            if (!session.OutboundQueues.TryGetValue(client, out queue!))
            {
                queue = new ClientOutboundQueue(_options);
                queue.Worker = ProcessClientOutboundCommandsAsync(session, client, queue);
                session.OutboundQueues.Add(client, queue);
            }
        }

        if (!queue.Commands.Writer.TryWrite(command))
        {
            RemoveClient(session, client);
            throw new InvalidOperationException("服务端发送队列已关闭或已满。");
        }
    }

    private bool IsAuthorized(System.Net.Sockets.Socket client)
    {
        return _authorizedClients.ContainsKey(client);
    }

    private bool IsSubscribed(string subject, System.Net.Sockets.Socket client)
    {
        lock (_subscriptionSync)
        {
            return _subscribedSubjectAndClients.TryGetValue(subject, out var clients) && clients.Contains(client);
        }
    }

    private System.Net.Sockets.Socket[] GetSubscribers(string subject)
    {
        lock (_subscriptionSync)
        {
            return _subscribedSubjectAndClients.TryGetValue(subject, out var clients)
                ? clients.ToArray()
                : Array.Empty<System.Net.Sockets.Socket>();
        }
    }

    private async Task StopSessionAsync(ServerSession? session)
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
            session.Server?.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _options.Report("关闭事件服务失败", ex);
        }

        session.Inbound.Writer.TryComplete();
        ClientOutboundQueue[] queues;
        lock (session.OutboundSync)
        {
            queues = session.OutboundQueues.Values.ToArray();
            foreach (var queue in queues)
            {
                queue.Commands.Writer.TryComplete();
            }

            session.OutboundQueues.Clear();
        }

        try
        {
            await Task.WhenAll(
                    new[] { session.InboundTask, session.CleanupTask }
                        .Concat(queues.Select(queue => queue.Worker)))
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            _options.Report("等待服务端后台任务退出超时", ex);
        }
        finally
        {
            session.Cancellation.Dispose();
        }
    }

    /// <summary>
    /// 主动下线空闲超过 ClientIdleTimeout 的连接：客户端主动断开不会触发服务端发送失败，
    /// 若只依赖心跳回包失败兜底，死连接与其订阅会一直滞留。
    /// </summary>
    private void CleanupIdleClients(ServerSession session)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _lastClientActivity.ToArray())
        {
            if (now - pair.Value <= _options.ClientIdleTimeout)
            {
                continue;
            }

            _options.Report("客户端空闲超时，服务端主动下线。");
            RemoveClient(session, pair.Key);
        }
    }

    private void CleanupExpiredOfflineSubscriptions()
    {
        if (!_options.EnableOfflineMessage)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _offlineSubscriptions.ToArray())
        {
            if (now - pair.Value.LastActiveAt <= _options.OfflineMessageRetention)
            {
                continue;
            }

            if (!_offlineSubscriptions.TryRemove(pair.Key, out var stale))
            {
                continue;
            }

            lock (stale.Sync)
            {
                foreach (var buffer in stale.PendingEvents.Values)
                {
                    buffer.Writer.TryComplete();
                }

                stale.PendingEvents.Clear();
            }
        }
    }

    private void RemoveClientState()
    {
        _authorizedClients.Clear();
        _lastClientActivity.Clear();
        lock (_subscriptionSync)
        {
            _subscribedSubjectAndClients.Clear();
        }

        foreach (var state in _offlineSubscriptions.Values)
        {
            lock (state.Sync)
            {
                foreach (var buffer in state.PendingEvents.Values)
                {
                    buffer.Writer.TryComplete();
                }
            }
        }

        _offlineSubscriptions.Clear();
        _pendingQueries.Clear();
    }

    private void ClearState()
    {
        RemoveClientState();
    }

    private void ValidateRequest(string subject, byte[]? buffer)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > _options.MaxSubjectLength)
        {
            throw new InvalidOperationException("主题不能为空且长度不能超过配置上限。");
        }

        if (buffer is not null && buffer.Length > _options.MaxMessageSizeBytes)
        {
            throw new InvalidOperationException("消息体超过配置大小限制。");
        }
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

    private static Channel<INetObject> CreateOutboundCommandChannel(EventBusOptions options)
    {
        return Channel.CreateBounded<INetObject>(new BoundedChannelOptions(options.OutboundQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }
}
