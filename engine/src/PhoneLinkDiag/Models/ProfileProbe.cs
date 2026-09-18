namespace PhoneLinkDiag.Models;

public sealed record ProfileProbe(
    string ProfileName,
    string ServiceRole,
    Guid ServiceUuid,
    bool Found,
    string Status,
    string? ServiceName = null,
    string? HostName = null,
    string? Error = null);
