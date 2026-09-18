namespace PhoneLinkDiag.Models;

public sealed record ObexProbeResult(
    string TargetName,
    bool ConnectedToRfcomm,
    bool ObexAccepted,
    string ResponseHex,
    string Status,
    string? Error = null);
