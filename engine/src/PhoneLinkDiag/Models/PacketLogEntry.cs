namespace PhoneLinkDiag.Models;

public sealed record PacketLogEntry(
    DateTimeOffset Timestamp,
    string Layer,
    string Operation,
    string RequestHex,
    string RequestDecoded,
    string ResponseHex,
    string ResponseDecoded,
    long ElapsedMilliseconds,
    bool Success,
    string Status,
    int? AttemptNumber = null,
    string? ProfileName = null,
    string? MessageHandle = null,
    string? Error = null,
    string? ResponseBodyText = null);
