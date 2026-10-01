namespace CodeWF.EventBus.Socket;

public sealed class EventBusOptions
{
    public int InboundQueueCapacity { get; init; } = 1024;

    public int OutboundQueueCapacity { get; init; } = 4096;

    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan PendingQueryTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否启用离线消息补发：客户端断线期间，其订阅主题的广播会进入服务端缓冲，
    /// 重连并重新订阅成功后按原顺序补发。客户端握手需携带 ClientId 才会启用缓冲。
    /// </summary>
    public bool EnableOfflineMessage { get; init; } = true;

    /// <summary>
    /// 每个客户端每个主题的离线消息缓冲上限，超出后丢弃最旧的消息。
    /// </summary>
    public int OfflineMessageCapacity { get; init; } = 1024;

    /// <summary>
    /// 离线订阅缓冲在客户端断线后的保留时长，超时后缓冲会被清理。
    /// </summary>
    public TimeSpan OfflineMessageRetention { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 服务端判定客户端离线的空闲阈值：超过该时长未收到任何命令（含心跳）的连接
    /// 会被服务端主动下线并转入离线缓冲。需大于客户端的心跳间隔。
    /// </summary>
    public TimeSpan ClientIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxSubjectLength { get; init; } = 256;

    public int MaxMessageSizeBytes { get; init; } = 1024 * 1024;

    public string? AuthenticationToken { get; init; }

    public Action<string, Exception?>? ErrorHandler { get; init; }

    internal void Validate()
    {
        if (InboundQueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InboundQueueCapacity));
        }

        if (OutboundQueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutboundQueueCapacity));
        }

        if (ReconnectInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ReconnectInterval));
        }

        if (HeartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval));
        }

        if (PendingQueryTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PendingQueryTimeout));
        }

        if (OfflineMessageCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OfflineMessageCapacity));
        }

        if (OfflineMessageRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(OfflineMessageRetention));
        }

        if (ClientIdleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ClientIdleTimeout));
        }

        if (MaxSubjectLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSubjectLength));
        }

        if (MaxMessageSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMessageSizeBytes));
        }
    }

    internal void Report(string message, Exception? exception = null)
    {
        try
        {
            ErrorHandler?.Invoke(message, exception);
        }
        catch
        {
            // Diagnostics must not break transport processing.
        }

        Debug.WriteLine(exception is null ? message : $"{message}: {exception.Message}");
    }
}
