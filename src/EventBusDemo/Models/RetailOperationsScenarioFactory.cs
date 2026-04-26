using System;
using EventBusDemo.Commands;
using EventBusDemo.Queries;

namespace EventBusDemo.Models;

public static class RetailOperationsScenarioFactory
{
    private static readonly string[] StoreNames =
    [
        "陆家嘴旗舰店",
        "前滩体验店",
        "张江智慧门店",
        "虹桥枢纽店"
    ];

    private static readonly string[] ProductNames =
    [
        "冷萃燕麦拿铁",
        "桂花乌龙茶",
        "火炙鸡腿饭",
        "海盐牛角包"
    ];

    private static readonly string[] Regions =
    [
        "华东",
        "华南",
        "华北",
        "西南"
    ];

    public static InventoryAlertCommand GenerateInventoryAlert()
    {
        return new InventoryAlertCommand
        {
            StoreName = StoreNames[Random.Shared.Next(StoreNames.Length)],
            ProductName = ProductNames[Random.Shared.Next(ProductNames.Length)],
            RemainingQuantity = Random.Shared.Next(3, 18),
            AlertLevel = Random.Shared.Next(0, 2) == 0 ? "黄色预警" : "红色预警",
            DetectedAt = DateTimeOffset.Now.ToUnixTimeSeconds()
        };
    }

    public static StoreStatusCommand GenerateStoreStatus()
    {
        return new StoreStatusCommand
        {
            StoreName = StoreNames[Random.Shared.Next(StoreNames.Length)],
            BusinessDate = DateTime.Today.ToString("yyyy-MM-dd"),
            WaitingOrders = Random.Shared.Next(8, 42),
            ActiveCouriers = Random.Shared.Next(2, 10),
            UpdatedAt = DateTimeOffset.Now.ToUnixTimeSeconds()
        };
    }

    public static SalesSnapshotQueryResponse BuildSalesSnapshot(SalesSnapshotQuery query)
    {
        return new SalesSnapshotQueryResponse
        {
            Region = string.IsNullOrWhiteSpace(query.Region) ? Regions[0] : query.Region,
            BusinessDate = string.IsNullOrWhiteSpace(query.BusinessDate)
                ? DateTime.Today.ToString("yyyy-MM-dd")
                : query.BusinessDate,
            TotalOrders = Random.Shared.Next(320, 980),
            TotalRevenueInCents = Random.Shared.NextInt64(6_800_000, 18_600_000),
            BestSellingProduct = ProductNames[Random.Shared.Next(ProductNames.Length)]
        };
    }

    public static ServiceClockQueryResponse BuildServiceClock(ServiceClockQuery query)
    {
        return new ServiceClockQueryResponse
        {
            ServiceName = string.IsNullOrWhiteSpace(query.ServiceName) ? "StoreOpsGateway" : query.ServiceName,
            CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff"),
            TimeZoneId = TimeZoneInfo.Local.Id
        };
    }
}
