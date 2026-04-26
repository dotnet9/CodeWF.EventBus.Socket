namespace EventBusDemo.Queries;

public class SalesSnapshotQuery
{
    public string Region { get; set; } = string.Empty;
    public string BusinessDate { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"请求查询 {Region} 区域在 {BusinessDate} 的销售快照";
    }
}
