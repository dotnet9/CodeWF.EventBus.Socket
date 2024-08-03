using System;

namespace EventBusDemo.Commands;

public class NewEmailCommand
{
    public string? Subject { get; set; }
    public string? Content { get; set; }
    public long SendTime { get; set; }

    public override string ToString()
    {
        return
            $"{nameof(Subject)}: {Subject}, {nameof(Content)}: {Content}, {nameof(SendTime)}: {DateTimeOffset.FromFileTime(SendTime):yyyy-MM-dd HH:mm:ss fff}";
    }
}