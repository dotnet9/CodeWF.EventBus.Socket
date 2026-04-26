namespace EventBusDemo.Queries;

public class SalesSnapshotQueryResponse
{
    public string Region { get; set; } = string.Empty;
    public string BusinessDate { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
    // 演示对象在网络传输时尽量使用更稳妥的基础类型，这里用“分”为单位避免 decimal 序列化兼容问题。
    public long TotalRevenueInCents { get; set; }
    public string BestSellingProduct { get; set; } = string.Empty;

    public override string ToString()
    {
        var revenue = TotalRevenueInCents / 100m;
        return $"{Region} 区域 {BusinessDate} 销售快照：订单 {TotalOrders} 单，营收 {revenue:N2} 元，爆品 {BestSellingProduct}";
    }
}
