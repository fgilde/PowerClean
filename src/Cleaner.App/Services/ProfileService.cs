namespace Cleaner.App.Services;

/// <summary>Benanntes Auswahl-Profil: welche Cleaner-Kategorien (per Id) angehakt sind.</summary>
public sealed class CleanupProfile
{
    public string Name { get; set; } = string.Empty;
    public List<string> TargetIds { get; set; } = new();
}

/// <summary>
/// Verwaltet gespeicherte Auswahl-Profile (Presets) für die Cleaner-Seiten.
/// Persistiert nach %LocalAppData%\PowerClean\profiles.json. Profile sind seitenübergreifend
/// — beim Anwenden werden nur die auf der jeweiligen Seite vorhandenen Kategorien gesetzt.
/// </summary>
public sealed class ProfileService
{
    private const string FileName = "profiles.json";

    private readonly AppDataService _data;
    private readonly List<CleanupProfile> _profiles;

    public ProfileService(AppDataService data)
    {
        _data = data;
        _profiles = _data.Load(FileName, () => new List<CleanupProfile>());
    }

    public event EventHandler? Changed;

    public IReadOnlyList<CleanupProfile> Profiles => _profiles;

    public CleanupProfile? Get(string name)
        => _profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Legt ein Profil an oder überschreibt ein gleichnamiges.</summary>
    public void Save(string name, IEnumerable<string> targetIds)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var existing = Get(name);
        if (existing is not null)
            existing.TargetIds = targetIds.ToList();
        else
            _profiles.Add(new CleanupProfile { Name = name.Trim(), TargetIds = targetIds.ToList() });

        _data.Save(FileName, _profiles);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string name)
    {
        _profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        _data.Save(FileName, _profiles);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
