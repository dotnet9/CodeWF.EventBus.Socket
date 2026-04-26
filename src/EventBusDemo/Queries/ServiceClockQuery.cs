namespace EventBusDemo.Queries;

public class ServiceClockQuery
{
    public string ServiceName { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"请求查询服务时钟：{ServiceName}";
    }
}
