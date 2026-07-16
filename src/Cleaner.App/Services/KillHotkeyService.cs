using Cleaner.Core.Services;
using Nextended.UI.Input;

namespace Cleaner.App.Services;

/// <summary>
/// Globale Shortcuts für Kill-Profile: solange PowerClean läuft (auch minimiert), lauscht ein
/// systemweiter Hook (Tastatur/Maus/Gamepad). Wird der Shortcut eines Profils gedrückt (einzelne
/// Taste oder Akkord wie Strg+Alt+K, Gamepad LT+A), führt das Profil sofort aus — ohne Rückfrage,
/// das ist der Sinn des Shortcuts. Jede Ausführung wird per Toast angezeigt.
/// </summary>
public sealed class KillHotkeyService : IDisposable
{
    private const string IdPrefix = "killprofile_";

    private readonly KillProfileService _profiles;
    private readonly IProcessKiller _killer;
    private readonly ToastService _toast;
    private readonly Dictionary<string, KillProfile> _registered = new();
    private InputBindingManager? _manager;

    public KillHotkeyService(KillProfileService profiles, IProcessKiller killer, ToastService toast)
    {
        _profiles = profiles;
        _killer = killer;
        _toast = toast;
    }

    /// <summary>Gemeinsamer Manager — auch die Kill-Profil-Seite bindet ihre Controls hieran.</summary>
    public InputBindingManager Manager => _manager ??= new InputBindingManager(enableGamepad: true);

    /// <summary>Nach Ausführung per Hotkey (für Status/Log). Kommt NICHT auf dem UI-Thread.</summary>
    public event Action<KillProfile, KillExecutionResult>? ProfileExecuted;

    /// <summary>Auf dem UI-Thread aufrufen (der Hook braucht einen Message-Loop-Thread).</summary>
    public void Start()
    {
        Manager.OnBindingPressed += OnBindingPressed;
        Rebuild();
        _profiles.Changed += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        foreach (var id in _registered.Keys)
            Manager.RemoveBinding(id);
        _registered.Clear();

        foreach (var profile in _profiles.Profiles)
        {
            if (profile.Shortcut is not { IsValid: true } shortcut) continue;
            var id = IdPrefix + profile.Id;
            _registered[id] = profile;
            // MinTime ist hier Hold-Zeit — bewusst übernehmen (Hotkey erst nach Halten).
            Manager.RegisterBinding(id, shortcut);
        }
    }

    private void OnBindingPressed(string bindingId)
    {
        if (!_registered.TryGetValue(bindingId, out var profile)) return;
        _toast.Show($"Kill-Profil \"{profile.Name}\"", "Wird ausgeführt…", TimeSpan.FromSeconds(10));
        Task.Run(() => Execute(profile));
    }

    private void Execute(KillProfile profile)
    {
        try
        {
            var result = _killer.Execute(profile.Patterns, profile.Mode);
            App.LogInfo($"KillProfile '{profile.Name}' via Hotkey: {result.KilledCount}/{result.MatchedCount} beendet, ~{result.FreedBytesEstimate / (1024 * 1024)} MB frei");
            _toast.Show(
                $"Kill-Profil \"{profile.Name}\" ausgeführt",
                result.MatchedCount == 0
                    ? "Keine passenden Prozesse gefunden."
                    : $"{result.KilledCount}/{result.MatchedCount} Prozesse beendet — ~{Cleaner.Core.Utils.ByteFormatter.Format(result.FreedBytesEstimate)} RAM frei" +
                      (result.Failed.Count > 0 ? $"\nFehlgeschlagen: {string.Join(", ", result.Failed.Take(4))}" : ""));
            ProfileExecuted?.Invoke(profile, result);
        }
        catch (Exception ex)
        {
            App.LogException("KillHotkey", ex);
            _toast.Show($"Kill-Profil \"{profile.Name}\"", "Fehler bei der Ausführung — siehe Log.");
        }
    }

    public void Dispose()
    {
        if (_manager != null)
        {
            _manager.OnBindingPressed -= OnBindingPressed;
            _manager.Dispose();
            _manager = null;
        }
        _registered.Clear();
    }
}
