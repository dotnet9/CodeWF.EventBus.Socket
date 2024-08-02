namespace CodeWF.EventBus.Socket.Models.Requests;

[NetHead(6, 1)]
internal class RequestQuery : INetObject
{
    public string TaskId { get; set; } = null!;

    public string Subject { get; set; } = null!;

    public byte[]? Buffer { get; set; }
}