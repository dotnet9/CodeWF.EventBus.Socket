namespace CodeWF.EventBus.Socket.Helpers;

internal static class SocketHelper
{
    internal static string GetNewTaskId()
    {
        return Guid.NewGuid().ToString();
    }
}