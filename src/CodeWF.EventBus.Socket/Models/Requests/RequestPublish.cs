namespace CodeWF.EventBus.Socket.Models.Requests;

[NetHead(4, 1)]
internal class RequestPublish : INetObject
{
    public int TaskId { get; set; }

    public string Subject { get; set; } = null!;

    public string? Message { get; set; }
}