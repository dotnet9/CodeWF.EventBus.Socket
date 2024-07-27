namespace CodeWF.EventBus.Socket.Helpers;

public static class ActionHelper
{
    public static bool CheckOvertime(Action checkAction, int overtimeMilliseconds = 3000)
    {
        var task = new Task(checkAction);
        task.Start();
        return task.Wait(TimeSpan.FromMilliseconds(overtimeMilliseconds));
    }
}