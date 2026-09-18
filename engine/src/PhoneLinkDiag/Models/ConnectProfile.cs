namespace PhoneLinkDiag.Models;

public sealed record ConnectProfile(
    int AttemptNumber,
    string Name,
    string SpecBasis,
    ushort MaxPacketSize,
    bool IncludeMapSupportedFeatures,
    uint MapSupportedFeatures,
    byte[] Packet,
    ObexValidationResult Validation);
