namespace PhoneLinkDiag.Models;

public sealed record ObexValidationResult(
    bool IsValid,
    string Summary,
    IReadOnlyList<string> Details,
    IReadOnlyList<string> Errors);
