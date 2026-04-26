using System;

namespace EventBusDemo.Commands;

public class InventoryAlertCommand
{
    public string StoreName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int RemainingQuantity { get; set; }
    public string AlertLevel { get; set; } = string.Empty;
    public long DetectedAt { get; set; }

    public override string ToString()
    {
        return $"{StoreName} 库存预警：{ProductName} 剩余 {RemainingQuantity} 件，等级 {AlertLevel}，时间 {DateTimeOffset.FromUnixTimeSeconds(DetectedAt):yyyy-MM-dd HH:mm:ss}";
    }
}
