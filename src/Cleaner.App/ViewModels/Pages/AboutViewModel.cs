using System.Windows;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class AboutViewModel : ObservableObject
{
    public const string WebsiteUrl = "https://fgilde.github.io/PowerClean/";
    public const string GildeUrl = "https://www.gilde.org";

    private readonly UpdateService _updates;

    public AboutViewModel(UpdateService updates)
    {
        _updates = updates;
    }

    public string Version => _updates.CurrentVersion;
    public bool IsManaged => _updates.IsManaged;
    public string Repository => _updates.Repository;
    public string IssuesUrl => Repository.TrimEnd('/') + "/issues";
    public string ReleasesUrl => Repository.TrimEnd('/') + "/releases";

    [ObservableProperty]
    private string _updateStatus = "";

    [ObservableProperty]
    private bool _isCheckingUpdate;

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
