namespace CodeWF.EventBus.Socket.NetWeaver.Base;

public class NetHeadInfo
{
    public int BufferLen { get; set; }

    public long SystemId { get; set; }

    public byte ObjectId { get; set; }

    public byte ObjectVersion { get; set; }

    public long UnixTimeMilliseconds { get; set; }

    public override string ToString()
    {
        return
            $"{nameof(BufferLen)}: {BufferLen}, {nameof(SystemId)}: {SystemId}，{nameof(ObjectId)}: {ObjectId}，{nameof(ObjectVersion)}: {ObjectVersion}";
    }
}