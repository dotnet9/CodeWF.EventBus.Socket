namespace CodeWF.EventBus.Socket.Models.Requests;

[NetHead(3, 1)]
internal class RequestUnsubscribe : INetObject
{
    public int TaskId { get; set; }

    public string Subject { get; set; } = null!;
}