using System;

namespace EventBusDemo.Commands;

public class StoreStatusCommand
{
    public string StoreName { get; set; } = string.Empty;
    public string BusinessDate { get; set; } = string.Empty;
    public int WaitingOrders { get; set; }
    public int ActiveCouriers { get; set; }
    public long UpdatedAt { get; set; }

    public override string ToString()
    {
        return $"{StoreName} 营运播报：营业日 {BusinessDate}，待出餐 {WaitingOrders} 单，配送骑手 {ActiveCouriers} 人，更新时间 {DateTimeOffset.FromUnixTimeSeconds(UpdatedAt):yyyy-MM-dd HH:mm:ss}";
    }
}
