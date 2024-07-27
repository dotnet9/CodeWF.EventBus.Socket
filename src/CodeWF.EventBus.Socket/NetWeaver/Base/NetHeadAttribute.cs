namespace CodeWF.EventBus.Socket.NetWeaver.Base;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class NetHeadAttribute(byte id, byte version) : Attribute
{
    public byte Id { get; set; } = id;

    public byte Version { get; set; } = version;
}