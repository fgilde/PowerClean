using System.Windows;
using Cleaner.App.Helpers;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly UpdateService _updates;

    public SettingsViewModel(AppSettings settings, UpdateService updates)
    {
        _settings = settings;
        _updates = updates;
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.UseDarkTheme))
                ApplyTheme();
        };
    }

    public AppSettings Settings => _settings;

    public string Version => _updates.CurrentVersion;
    public bool IsManaged => _updates.IsManaged;
    public string Repository => _updates.Repository;

    [ObservableProperty]
    private string _updateStatus = "";

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [RelayCommand]
    public void ApplyTheme()
    {
        ApplicationThemeManager.Apply(_settings.UseDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    /// <summary>
    /// Erzwingt ein erneutes Lesen der aktuellen Version (z.B. nach Update oder
    /// beim Öffnen der Settings-Page) und triggert die UI-Aktualisierung.
    /// </summary>
    public void RefreshVersion() => OnPropertyChanged(nameof(Version));

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (!IsManaged)
        {
            UpdateStatus = "Auto-Update funktioniert nur bei einer via Velopack-Setup installierten Version. Aktuell läuft die App im Dev-Modus.";
            return;
        }

        IsCheckingUpdate = true;
        UpdateStatus = "Suche nach Updates...";
        try
        {
            var info = await _updates.CheckAsync();
            OnPropertyChanged(nameof(Version));
            if (info is null)
            {
                UpdateStatus = $"Aktuell auf {Version} — keine Updates verfügbar.";
                return;
            }

            var newVer = info.TargetFullRelease.Version.ToString();
            var ask = MessageBox.Show(
                $"Update verfügbar: {newVer} (aktuell: {Version}).\n\nJetzt herunterladen und neu starten?",
                "PowerClean Update", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (ask != MessageBoxResult.Yes) { UpdateStatus = $"Update {newVer} bereit — Installation übersprungen."; return; }

            UpdateStatus = "Lade Update herunter...";
            var ok = await _updates.DownloadAsync(info, new Progress<int>(p => UpdateStatus = $"Download: {p}%"));
            if (!ok) { UpdateStatus = "Download fehlgeschlagen — siehe Log."; return; }

            UpdateStatus = "Update installiert — App startet neu.";
            _updates.ApplyAndRestart(info);
        }
        finally { IsCheckingUpdate = false; }
    }

    [RelayCommand]
    public void OpenRepository() => OpenUrl(Repository);

    [RelayCommand]
    public void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            { UseShellExecute = true });
        }
        catch { }
    }
}
