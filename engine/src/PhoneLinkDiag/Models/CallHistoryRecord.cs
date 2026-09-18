namespace PhoneLinkDiag.Models;

public sealed record CallHistoryRecord(
    string DeviceId,
    string DeviceName,
    string Type,
    string Name,
    string Phone,
    string Timestamp);
