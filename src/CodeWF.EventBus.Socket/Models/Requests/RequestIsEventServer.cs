namespace CodeWF.EventBus.Socket.Models.Requests;

[NetHead(1, 1)]
internal class RequestIsEventServer : INetObject
{
    public int TaskId { get; set; }
}