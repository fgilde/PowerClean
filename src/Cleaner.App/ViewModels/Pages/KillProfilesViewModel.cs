using System.Collections.ObjectModel;
using System.Windows;
using Cleaner.App.Services;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nextended.UI.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class KillProfilesViewModel : ObservableObject
{
    private readonly KillProfileService _service;
    private readonly IProcessKiller _killer;
    private readonly KillHotkeyService _hotkeys;

    public KillProfilesViewModel(KillProfileService service, IProcessKiller killer, KillHotkeyService hotkeys)
    {
        _service = service;
        _killer = killer;
        _hotkeys = hotkeys;

        foreach (var p in service.Profiles)
            Profiles.Add(new KillProfileItemViewModel(p, service, killer));

        _hotkeys.ProfileExecuted += (profile, result) =>
            Application.Current?.Dispatcher.BeginInvoke(() =>
                StatusText = $"'{profile.Name}' per Shortcut ausgeführt: {result.KilledCount}/{result.MatchedCount} Prozesse beendet, " +
                             $"~{Cleaner.Core.Utils.ByteFormatter.Format(result.FreedBytesEstimate)} RAM frei");
    }

    /// <summary>Der globale Binding-Manager des Hotkey-Dienstes — die Seite bindet ihre Controls hieran.</summary>
    public InputBindingManager BindingManager => _hotkeys.Manager;

    public ObservableCollection<KillProfileItemViewModel> Profiles { get; } = new();

    [ObservableProperty]
    private string _statusText = "";

    [RelayCommand]
    public void AddProfile()
    {
        var profile = new KillProfile { Name = "Neues Profil" };
        _service.Save(profile);
        Profiles.Add(new KillProfileItemViewModel(profile, _service, _killer));
    }

    [RelayCommand]
    public void DeleteProfile(KillProfileItemViewModel? item)
    {
        if (item is null) return;
        if (MessageBox.Show($"Profil \"{item.Name}\" löschen?", "Kill-Profil löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _service.Delete(item.Model.Id);
        Profiles.Remove(item);
    }

    [RelayCommand]
    public void ExecuteProfile(KillProfileItemViewModel? item)
    {
        if (item is null) return;
        var matches = _killer.FindMatches(item.Model.Patterns);
        if (matches.Count == 0)
        {
            StatusText = $"'{item.Name}': aktuell keine passenden Prozesse.";
            return;
        }

        if (MessageBox.Show(
                $"{matches.Count} Prozesse BEENDEN? (Modus: {item.Mode})\n\n" +
                string.Join(", ", matches.Take(15).Select(m => m.Name).Distinct()) +
                (matches.Count > 15 ? ", …" : "") +
                "\n\nUngespeicherte Daten gehen verloren.",
                $"Kill-Profil \"{item.Name}\" ausführen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var result = _killer.Execute(item.Model.Patterns, item.Model.Mode);
        StatusText = $"'{item.Name}': {result.KilledCount}/{result.MatchedCount} Prozesse beendet, " +
                     $"~{Cleaner.Core.Utils.ByteFormatter.Format(result.FreedBytesEstimate)} RAM frei" +
                     (result.Failed.Count > 0 ? $" — fehlgeschlagen: {string.Join(", ", result.Failed.Take(5))}" : "");
        _ = item.RefreshMatchesAsync();
    }
}

/// <summary>Ein editierbares Kill-Profil in der Liste (persistiert jede Änderung sofort).</summary>
public sealed partial class KillProfileItemViewModel : ObservableObject
{
    private readonly KillProfileService _service;
    private readonly IProcessKiller _killer;
    private readonly bool _initialized;
    private bool _refreshing;

    internal KillProfile Model { get; }

    public KillProfileItemViewModel(KillProfile model, KillProfileService service, IProcessKiller killer)
    {
        Model = model;
        _service = service;
        _killer = killer;

        _name = model.Name;
        _patternsText = string.Join("; ", model.Patterns);
        _mode = model.Mode;
        _shortcutBinding = model.Shortcut;
        _initialized = true;
    }

    public static KillMode[] Modes { get; } = Enum.GetValues<KillMode>();

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _patternsText;

    [ObservableProperty]
    private KillMode _mode;

    /// <summary>Der Shortcut (einzelnes Binding, darf ein Akkord sein) — TwoWay an KeyBindChanger.</summary>
    [ObservableProperty]
    private StoredInputBinding? _shortcutBinding;

    public ObservableCollection<KillMatch> Matches { get; } = new();

    [ObservableProperty]
    private string _matchSummary = "";

    partial void OnNameChanged(string value)
    {
        if (!_initialized) return;
        Model.Name = value.Trim();
        _service.Save(Model);
    }

    partial void OnPatternsTextChanged(string value)
    {
        if (!_initialized) return;
        Model.Patterns = value
            .Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _service.Save(Model);
        // Preview absichtlich nicht automatisch — Prozess-Scan nur auf Knopfdruck.
        Matches.Clear();
        MatchSummary = "";
    }

    partial void OnModeChanged(KillMode value)
    {
        if (!_initialized) return;
        Model.Mode = value;
        _service.Save(Model);
    }

    partial void OnShortcutBindingChanged(StoredInputBinding? value)
    {
        if (!_initialized) return;
        PersistShortcut(value);
    }

    /// <summary>
    /// Auch von der Page bei KeyBindChanged aufgerufen: eine MinTime-Änderung mutiert dieselbe
    /// Instanz (DP-Wert unverändert), da feuert das ObservableProperty nicht.
    /// </summary>
    public void PersistShortcut(StoredInputBinding? value)
    {
        Model.Shortcut = value is { IsValid: true } ? value : null;
        _service.Save(Model);
    }

    [RelayCommand]
    public async Task RefreshMatchesAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var patterns = Model.Patterns;
            var matches = await Task.Run(() => _killer.FindMatches(patterns));
            Matches.Clear();
            foreach (var m in matches.Take(60)) Matches.Add(m);
            MatchSummary = matches.Count == 0
                ? "Trifft aktuell keine Prozesse"
                : $"Würde jetzt {matches.Count} Prozesse beenden (~{Cleaner.Core.Utils.ByteFormatter.Format(matches.Sum(m => m.WorkingSetBytes))} RAM)";
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("KillProfileRefresh", ex);
        }
        finally
        {
            _refreshing = false;
        }
    }
}
