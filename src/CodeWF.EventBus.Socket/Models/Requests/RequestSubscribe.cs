namespace CodeWF.EventBus.Socket.Models.Requests;

[NetHead(2, 1)]
internal class RequestSubscribe : INetObject
{
    public string TaskId { get; set; } = null!;

    public string Subject { get; set; } = null!;
}