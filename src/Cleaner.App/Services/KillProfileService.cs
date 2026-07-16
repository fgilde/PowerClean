using Cleaner.Core.Services;
using Nextended.UI.Input;

namespace Cleaner.App.Services;

/// <summary>
/// Ein Kill-Profil: benannte Wildcard-Patterns + Kill-Modus + optionaler globaler Shortcut
/// (ein Binding — darf ein Akkord wie Strg+Alt+K oder Gamepad LT+A sein).
/// </summary>
public sealed class KillProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> Patterns { get; set; } = new();
    public KillMode Mode { get; set; } = KillMode.Standard;
    public StoredInputBinding? Shortcut { get; set; }
}

/// <summary>
/// Verwaltet Kill-Profile. Persistiert nach %LocalAppData%\PowerClean\killprofiles.json.
/// Beim allerersten Start werden sinnvolle Default-Profile angelegt.
/// </summary>
public sealed class KillProfileService
{
    private const string FileName = "killprofiles.json";

    private readonly AppDataService _data;
    private readonly List<KillProfile> _profiles;

    public KillProfileService(AppDataService data)
    {
        _data = data;
        _profiles = _data.Load(FileName, () => new List<KillProfile>());
        // Leer = erster Start oder unlesbare Alt-Datei — in beiden Fällen Defaults anlegen.
        if (_profiles.Count == 0)
        {
            _profiles.AddRange(CreateDefaults());
            _data.Save(FileName, _profiles);
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<KillProfile> Profiles => _profiles;

    public KillProfile? Get(string id) => _profiles.FirstOrDefault(p => p.Id == id);

    /// <summary>Legt an oder aktualisiert (per Id) und persistiert.</summary>
    public void Save(KillProfile profile)
    {
        var idx = _profiles.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0) _profiles[idx] = profile;
        else _profiles.Add(profile);
        _data.Save(FileName, _profiles);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        _profiles.RemoveAll(p => p.Id == id);
        _data.Save(FileName, _profiles);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IEnumerable<KillProfile> CreateDefaults() =>
    [
        new KillProfile
        {
            Name = "Browser",
            Patterns = ["*chrome*", "*msedge*", "firefox*", "opera*", "brave*", "vivaldi*"],
            Mode = KillMode.Standard,
        },
        new KillProfile
        {
            Name = "Chat & Meetings",
            Patterns = ["*teams*", "slack*", "discord*", "zoom*", "telegram*", "whatsapp*", "signal*"],
            Mode = KillMode.Standard,
        },
        new KillProfile
        {
            Name = "Gaming Launcher",
            Patterns = ["steam*", "epicgameslauncher*", "epicwebhelper*", "battle.net*", "riotclient*", "eadesktop*", "origin*", "gog*", "ubisoftconnect*"],
            Mode = KillMode.Standard,
        },
        new KillProfile
        {
            Name = "Dev Tools",
            Patterns = ["node*", "java*", "msbuild*", "vbcscompiler*", "servicehub*", "testhost*", "gradle*"],
            Mode = KillMode.ForceTree,
        },
    ];
}
