namespace CodeWF.EventBus.Socket.Models.Responses;

[NetHead(5, 1)]
internal class UpdateEvent : INetObject
{
    public int TaskId { get; set; }

    public string Subject { get; set; } = null!;

    public byte[]? Buffer { get; set; }
}