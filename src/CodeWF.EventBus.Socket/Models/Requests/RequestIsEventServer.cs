namespace CodeWF.EventBus.Socket.Models.Requests;

[NetHead(1, 1)]
internal class RequestIsEventServer : INetObject
{
    public string TaskId { get; set; } = null!;
}