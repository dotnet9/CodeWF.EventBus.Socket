namespace EventBusDemo.Queries;

public class ServiceClockQueryResponse
{
    public string ServiceName { get; set; } = string.Empty;
    public string CurrentTime { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{ServiceName} 当前时间：{CurrentTime} ({TimeZoneId})";
    }
}
