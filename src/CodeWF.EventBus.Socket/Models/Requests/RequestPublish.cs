namespace CodeWF.EventBus.Socket.Models.Requests;

[NetHead(4, 1)]
internal class RequestPublish : INetObject
{
    public string TaskId { get; set; } = null!;

    public string Subject { get; set; } = null!;

    public byte[]? Buffer { get; set; }
}