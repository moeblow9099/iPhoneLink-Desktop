using PhoneLinkDiag.Models;

namespace PhoneLinkDiag.Services;

public sealed class DiagnosticLogger
{
    private readonly object _gate = new();
    private readonly List<DiagnosticLogEntry> _entries = new();

    public event Action<DiagnosticLogEntry>? EntryAdded;

    public IReadOnlyList<DiagnosticLogEntry> Entries
    {
        get
        {
            lock (_gate) return _entries.ToArray();
        }
    }

    public void Info(string source, string message) => Add("INFO", source, message);
    public void Warn(string source, string message) => Add("WARN", source, message);
    public void Error(string source, string message) => Add("ERROR", source, message);
    public void Debug(string source, string message) => Add("DEBUG", source, message);

    private void Add(string level, string source, string message)
    {
        var entry = new DiagnosticLogEntry(DateTimeOffset.Now, level, source, message);
        lock (_gate) _entries.Add(entry);
        EntryAdded?.Invoke(entry);
    }

    public string SaveToFile(string? folder = null)
    {
        folder ??= Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"phonelink-diag-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.WriteAllLines(path, Entries.Select(e => e.ToString()));
        return path;
    }
}
