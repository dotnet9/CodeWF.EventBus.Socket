namespace CodeWF.EventBus.Socket.Models;

internal class SocketCommand(NetHeadInfo netHead, byte[] buffer, System.Net.Sockets.Socket? client = null)
{
    private NetHeadInfo HeadInfo { get; } = netHead;

    private byte[] Buffer { get; } = buffer;

    public System.Net.Sockets.Socket? Client { get; } = client;

    public bool IsMessage<T>()
    {
        return HeadInfo.IsNetObject<T>();
    }

    public T Message<T>() where T : new()
    {
        return Buffer.Deserialize<T>();
    }
}