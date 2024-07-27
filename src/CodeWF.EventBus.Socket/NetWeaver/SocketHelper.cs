namespace CodeWF.EventBus.Socket.NetWeaver;

public static partial class SerializeHelper
{
    public const int MaxUdpPacketSize = 65507;

    public static bool ReadPacket(this System.Net.Sockets.Socket socket, out byte[] buffer, out NetHeadInfo netObject)
    {
        var lenBuffer = ReceiveBuffer(socket, 4);
        var bufferLen = BitConverter.ToInt32(lenBuffer, 0);

        var exceptLenBuffer = ReceiveBuffer(socket, bufferLen - 4);

        buffer = new byte[bufferLen];

        Array.Copy(lenBuffer, buffer, 4);
        Buffer.BlockCopy(exceptLenBuffer, 0, buffer, 4, bufferLen - 4);

        var readIndex = 0;
        return ReadHead(buffer, ref readIndex, out netObject);
    }

    private static byte[] ReceiveBuffer(System.Net.Sockets.Socket client, int count)
    {
        var buffer = new byte[count];
        var bytesReadAllCount = 0;
        while (bytesReadAllCount != count)
            bytesReadAllCount +=
                client.Receive(buffer, bytesReadAllCount, count - bytesReadAllCount, SocketFlags.None);

        return buffer;
    }
}