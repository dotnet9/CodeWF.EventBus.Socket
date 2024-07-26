namespace CodeWF.EventBus.Socket.Models.Responses;

[NetHead(254, 1)]
internal class ResponseCommon : INetObject
{
    public int TaskId { get; set; }

    public byte Status { get; set; }

    public string? Message { get; set; }
}

internal enum ResponseCommonStatus
{
    Wait,
    Success,
    Fail
}