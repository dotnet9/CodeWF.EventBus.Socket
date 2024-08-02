namespace CodeWF.EventBus.Socket.Models.Responses;

[NetHead(5, 1)]
internal class UpdateEvent : INetObject
{
    public string TaskId { get; set; } = null!;

    public string Subject { get; set; } = null!;

    public byte[]? Buffer { get; set; }
}