using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Data;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class ServicesViewModel : ObservableObject
{
    private readonly IServiceScanner _scanner;
    private List<WindowsService> _all = new();

    public ServicesViewModel(IServiceScanner scanner)
    {
        _scanner = scanner;
        ServicesView = CollectionViewSource.GetDefaultView(Services);
    }

    public ObservableCollection<WindowsService> Services { get; } = new();
    public ICollectionView ServicesView { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private bool _showOnlyRunning;

    [ObservableProperty]
    private bool _showOnlyRecommendations;

    [ObservableProperty]
    private bool _hideMicrosoft;

    [ObservableProperty]
    private string _statusText = "";

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnShowOnlyRunningChanged(bool value) => ApplyFilter();
    partial void OnShowOnlyRecommendationsChanged(bool value) => ApplyFilter();
    partial void OnHideMicrosoftChanged(bool value) => ApplyFilter();

    [RelayCommand]
    public async Task ScanAsync()
    {
        IsLoading = true;
        Services.Clear();
        try
        {
            _all = (await _scanner.ScanAsync()).ToList();
            ApplyFilter();
            var running = _all.Count(s => s.Status == ServiceControllerStatus.Running);
            var withRec = _all.Count(s => s.Recommendation is not null);
            StatusText = $"{_all.Count} Dienste · {running} laufen · {withRec} mit Empfehlung";
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("Services.Scan", ex);
            StatusText = "Fehler: " + ex.Message;
        }
        finally { IsLoading = false; }
    }

    private void ApplyFilter()
    {
        Services.Clear();
        IEnumerable<WindowsService> q = _all;

        if (!string.IsNullOrWhiteSpace(Filter))
            q = q.Where(s => s.DisplayName.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                              s.ServiceName.Contains(Filter, StringComparison.OrdinalIgnoreCase));

        if (ShowOnlyRunning) q = q.Where(s => s.Status == ServiceControllerStatus.Running);
        if (ShowOnlyRecommendations) q = q.Where(s => s.Recommendation is not null);
        if (HideMicrosoft) q = q.Where(s => !s.IsMicrosoftService);

        foreach (var s in q) Services.Add(s);
    }

    [RelayCommand]
    public async Task StopAsync(WindowsService? svc)
    {
        if (svc is null) return;
        var ok = await Task.Run(() => _scanner.Stop(svc.ServiceName));
        if (ok) await ScanAsync();
        else MessageBox.Show($"Konnte Dienst '{svc.DisplayName}' nicht stoppen.\n(Admin-Rechte nötig?)",
            "Cleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    public async Task StartAsync(WindowsService? svc)
    {
        if (svc is null) return;
        var ok = await Task.Run(() => _scanner.Start(svc.ServiceName));
        if (ok) await ScanAsync();
        else MessageBox.Show($"Konnte Dienst '{svc.DisplayName}' nicht starten.\n(Admin-Rechte nötig?)",
            "Cleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    public async Task DisableAsync(WindowsService? svc)
    {
        if (svc is null) return;
        if (MessageBox.Show($"Dienst '{svc.DisplayName}' DEAKTIVIEREN?\n\n" +
                            "Er wird beim nächsten Boot nicht mehr gestartet.\n" +
                            "(Aktueller Status bleibt unverändert.)",
                "Bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        var ok = await Task.Run(() => _scanner.SetStartType(svc.ServiceName, ServiceStartMode.Disabled));
        if (ok) await ScanAsync();
        else MessageBox.Show($"Konnte Start-Typ nicht ändern.\n(Admin-Rechte nötig)",
            "Cleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    public async Task SetManualAsync(WindowsService? svc)
    {
        if (svc is null) return;
        var ok = await Task.Run(() => _scanner.SetStartType(svc.ServiceName, ServiceStartMode.Manual));
        if (ok) await ScanAsync();
    }

    [RelayCommand]
    public async Task SetAutomaticAsync(WindowsService? svc)
    {
        if (svc is null) return;
        var ok = await Task.Run(() => _scanner.SetStartType(svc.ServiceName, ServiceStartMode.Automatic));
        if (ok) await ScanAsync();
    }

    [RelayCommand]
    public void ShowFolder(WindowsService? svc)
    {
        if (svc?.ImagePath is null) return;
        var resolved = Cleaner.App.Helpers.PathOpener.Resolve(svc.ImagePath);
        if (resolved is not null && Cleaner.App.Helpers.PathOpener.RevealInExplorer(resolved)) return;

        MessageBox.Show($"Dienst-Datei konnte nicht aufgelöst werden:\n\n{svc.ImagePath}",
            "Cleaner", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    public void OpenProperties(WindowsService? svc)
    {
        if (svc?.ImagePath is null) return;
        var resolved = Cleaner.App.Helpers.PathOpener.Resolve(svc.ImagePath);
        if (resolved is not null) Cleaner.App.Helpers.PathOpener.ShowProperties(resolved);
    }

    [RelayCommand]
    public void CopyServiceName(WindowsService? svc)
    {
        if (svc is null) return;
        Cleaner.App.Helpers.PathOpener.CopyToClipboard(svc.ServiceName);
    }

    [RelayCommand]
    public void OpenServicesMmc()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("services.msc")
            { UseShellExecute = true });
        }
        catch (Exception ex) { Cleaner.App.App.LogException("OpenServicesMmc", ex); }
    }
}
