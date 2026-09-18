namespace PhoneLinkDiag.Models;

public sealed record DiagnosticLogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Source,
    string Message)
{
    public override string ToString()
    {
        return $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Source}: {Message}";
    }
}
