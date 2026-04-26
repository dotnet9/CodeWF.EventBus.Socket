namespace EventBusDemo.Models;

public static class EventNames
{
    public const string InventoryAlertCommand = nameof(InventoryAlertCommand);
    public const string StoreStatusCommand = nameof(StoreStatusCommand);
    public const string SalesSnapshotQuery = nameof(SalesSnapshotQuery);
    public const string ServiceClockQuery = nameof(ServiceClockQuery);
}
