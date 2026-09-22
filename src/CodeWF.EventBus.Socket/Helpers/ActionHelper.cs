namespace CodeWF.EventBus.Socket.Helpers;

public static class ActionHelper
{
    public static bool CheckOvertime(Action checkAction, int overtimeMilliseconds = 3000)
    {
        ArgumentNullException.ThrowIfNull(checkAction);
        if (overtimeMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overtimeMilliseconds));
        }

        try
        {
            var task = Task.Run(checkAction);
            return task.Wait(TimeSpan.FromMilliseconds(overtimeMilliseconds)) && task.IsCompletedSuccessfully;
        }
        catch (AggregateException)
        {
            return false;
        }
    }
}
