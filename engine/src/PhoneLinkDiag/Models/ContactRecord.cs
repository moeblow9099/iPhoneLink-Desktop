namespace PhoneLinkDiag.Models;

public sealed record ContactRecord(
    string DeviceId,
    string DeviceName,
    string Name,
    IReadOnlyList<string> Phones,
    IReadOnlyList<string> Emails,
    string Organization);
