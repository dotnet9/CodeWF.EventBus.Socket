namespace CodeWF.EventBus.Socket.Models.Requests;

[NetHead(1, 1)]
internal class RequestIsEventServer : INetObject
{
    public string TaskId { get; set; } = null!;

    public string? AuthenticationToken { get; set; }

    /// <summary>
    /// 客户端实例标识，用于断线后关联离线订阅缓冲并补发消息；旧版本客户端为空。
    /// </summary>
    public string? ClientId { get; set; }
}
