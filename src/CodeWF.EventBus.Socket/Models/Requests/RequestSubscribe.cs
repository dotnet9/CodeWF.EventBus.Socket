namespace CodeWF.EventBus.Socket.Models.Requests;

[NetHead(2, 1)]
internal class RequestSubscribe : INetObject
{
    public int TaskId { get; set; }

    public string Subject { get; set; } = null!;
}