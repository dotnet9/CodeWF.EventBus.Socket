namespace CodeWF.EventBus.Socket;

public sealed class EventBusOptions
{
    public int InboundQueueCapacity { get; init; } = 1024;

    public int OutboundQueueCapacity { get; init; } = 4096;

    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan PendingQueryTimeout { get; init; } = TimeSpan.FromSeconds(30);

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
