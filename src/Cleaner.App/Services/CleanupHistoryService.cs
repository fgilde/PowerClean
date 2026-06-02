using Cleaner.Core.Models;

namespace Cleaner.App.Services;

/// <summary>Ein abgeschlossener Aufräum-Vorgang — für Verlauf und Wiederherstellung.</summary>
public sealed class CleanupHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CleanedAt { get; set; } = DateTimeOffset.Now;
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public long FreedBytes { get; set; }
    public int FilesDeleted { get; set; }
    public bool UsedRecycleBin { get; set; }

    /// <summary>Gelöschte Pfade (gekappt) — Basis für Wiederherstellung aus dem Papierkorb.</summary>
    public List<string> Paths { get; set; } = new();
}

/// <summary>
/// Persistierter Verlauf aller Aufräum-Vorgänge (%LocalAppData%\PowerClean\history.json).
/// Ermöglicht Nachvollziehbarkeit und – bei Papierkorb-Löschungen – Wiederherstellung.
/// </summary>
public sealed class CleanupHistoryService
{
    private const string FileName = "history.json";

    // Pfade pro Eintrag kappen, damit die Datei nicht explodiert.
    private const int MaxPathsPerEntry = 2000;
    private const int MaxEntries = 500;

    private readonly AppDataService _data;
    private readonly List<CleanupHistoryEntry> _entries;
    private readonly object _gate = new();

    public CleanupHistoryService(AppDataService data)
    {
        _data = data;
        _entries = _data.Load(FileName, () => new List<CleanupHistoryEntry>());
    }

    public event EventHandler? Changed;

    public IReadOnlyList<CleanupHistoryEntry> Entries
    {
        get { lock (_gate) return _entries.OrderByDescending(e => e.CleanedAt).ToList(); }
    }

    public long TotalFreedAllTime
    {
        get { lock (_gate) return _entries.Sum(e => e.FreedBytes); }
    }

    /// <summary>Hält einen Aufräum-Vorgang fest.</summary>
    public void Record(CleanResult result, string targetName, bool usedRecycleBin, IReadOnlyList<string> paths)
    {
        if (result.FilesDeleted <= 0 && result.FreedBytes <= 0) return;

        var entry = new CleanupHistoryEntry
        {
            CleanedAt = DateTimeOffset.Now,
            TargetId = result.TargetId,
            TargetName = targetName,
            FreedBytes = result.FreedBytes,
            FilesDeleted = result.FilesDeleted,
            UsedRecycleBin = usedRecycleBin,
            Paths = paths.Take(MaxPathsPerEntry).ToList(),
        };

        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => e.Id == id);
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Persist() => _data.Save(FileName, _entries);
}
