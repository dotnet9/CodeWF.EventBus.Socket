namespace CodeWF.EventBus.Socket.Models.Responses;

[NetHead(254, 1)]
internal class ResponseCommon : INetObject
{
    public string TaskId { get; set; } = null!;

    public byte Status { get; set; }

    public string? Message { get; set; }
}

internal enum ResponseCommonStatus
{
    Wait,
    Success,
    Fail
}