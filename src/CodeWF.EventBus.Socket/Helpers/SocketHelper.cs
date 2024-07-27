namespace CodeWF.EventBus.Socket.Helpers;

internal static class SocketHelper
{
    private static int _taskId;

    internal static int GetNewTaskId()
    {
        return ++_taskId;
    }
}