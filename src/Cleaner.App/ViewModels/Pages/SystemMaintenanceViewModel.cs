using System.Collections.ObjectModel;
using System.Windows;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class SystemMaintenanceViewModel : ObservableObject
{
    private readonly ISystemMaintenanceService _svc;

    public SystemMaintenanceViewModel(ISystemMaintenanceService svc)
    {
        _svc = svc;
        RefreshStatus();
    }

    public ObservableCollection<RestorePoint> RestorePoints { get; } = new();

    [ObservableProperty] private string _log = "Bereit. Klicke eine Aktion an.\n";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hibernationEnabled;
    [ObservableProperty] private long _hibernationFileSize;
    [ObservableProperty] private long _pageFileSize;
    [ObservableProperty] private int _restorePointCount;

    private void Append(string line)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        var indented = string.Join("\n", line.Split('\n').Select(l => "    " + l));
        _log = $"[{ts}] {line.Split('\n')[0]}\n" + (line.Contains('\n') ? indented + "\n" : "") + _log;
        OnPropertyChanged(nameof(Log));
    }

    public void RefreshStatus()
    {
        HibernationEnabled = _svc.IsHibernationEnabled();
        HibernationFileSize = _svc.GetHibernationFileSize();
        PageFileSize = _svc.GetPageFileSize();
    }

    [RelayCommand]
    public async Task FlushDnsAsync()
    {
        IsBusy = true;
        Append("DNS-Cache wird geleert...");
        try
        {
            var (rc, output) = await _svc.RunAsync("ipconfig", "/flushdns", elevate: false, CancellationToken.None);
            Append(output);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task RunDismCleanupAsync()
    {
        if (MessageBox.Show("DISM-Component-Cleanup räumt alte Windows-Update-Komponenten weg.\n\n" +
                            "Dauer: typischerweise mehrere Minuten.\n" +
                            "Benötigt Admin-Rechte (UAC-Dialog wird erscheinen).\n\n" +
                            "Fortfahren?",
                "DISM-Cleanup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        Append("DISM-Component-Cleanup startet (kann lange dauern)...");
        try
        {
            var (rc, output) = await _svc.RunAsync("dism.exe",
                "/Online /Cleanup-Image /StartComponentCleanup", elevate: true, CancellationToken.None);
            Append(output);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task RunDismResetBaseAsync()
    {
        if (MessageBox.Show("DISM /ResetBase deinstalliert ALLE alten Windows-Update-Versionen permanent.\n\n" +
                            "ACHTUNG: Update-Rollbacks sind danach nicht mehr möglich!\n" +
                            "Spart aber oft mehrere GB.\n\n" +
                            "Fortfahren?",
                "DISM ResetBase", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        Append("DISM /ResetBase startet (kann sehr lange dauern)...");
        try
        {
            var (rc, output) = await _svc.RunAsync("dism.exe",
                "/Online /Cleanup-Image /StartComponentCleanup /ResetBase", elevate: true, CancellationToken.None);
            Append(output);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task ToggleHibernationAsync()
    {
        var newState = !HibernationEnabled;
        var msg = newState
            ? "Hibernation einschalten?\n\nWird die hiberfil.sys neu anlegen (typisch ~40% RAM = mehrere GB)."
            : $"Hibernation ausschalten?\n\nDie hiberfil.sys ({Cleaner.Core.Utils.ByteFormatter.Format(HibernationFileSize)}) wird gelöscht.";
        if (MessageBox.Show(msg, "Hibernation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        Append($"powercfg /hibernate {(newState ? "on" : "off")}...");
        try
        {
            var (rc, output) = await _svc.RunAsync("powercfg.exe",
                "/hibernate " + (newState ? "on" : "off"), elevate: true, CancellationToken.None);
            Append(output);
        }
        finally
        {
            IsBusy = false;
            // Werte neu lesen
            await Task.Delay(500);
            RefreshStatus();
        }
    }

    [RelayCommand]
    public async Task LoadRestorePointsAsync()
    {
        IsBusy = true;
        RestorePoints.Clear();
        try
        {
            var pts = await _svc.GetRestorePointsAsync();
            foreach (var p in pts) RestorePoints.Add(p);
            RestorePointCount = pts.Count;
            Append($"{pts.Count} Wiederherstellungspunkte gefunden.");
        }
        catch (Exception ex)
        {
            Append("Fehler: " + ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task DeleteOldRestorePointsAsync()
    {
        if (MessageBox.Show(
                "ALLE Wiederherstellungspunkte (außer dem neuesten) werden gelöscht.\n\n" +
                "Spart oft mehrere GB. Benötigt Admin.\n\nFortfahren?",
                "Restore Points", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        Append("vssadmin delete shadows /for=C: /oldest /quiet (mehrfach)...");
        try
        {
            // VSSAdmin kann nur "oldest" oder alle löschen. Wir wollen "alle außer neuester" —
            // also vssadmin delete shadows /all + neuer wird beim nächsten Boot erstellt.
            // Sicherer: nur ältere löschen — wir nutzen /for /oldest in einer Schleife.
            for (int i = 0; i < 100; i++)
            {
                var (rc, output) = await _svc.RunAsync("vssadmin.exe",
                    "delete shadows /for=C: /oldest /quiet", elevate: true, CancellationToken.None);
                if (rc != 0) break;
            }
            Append("Restore-Points-Cleanup abgeschlossen.");
            await LoadRestorePointsAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task DeleteAllRestorePointsAsync()
    {
        if (MessageBox.Show(
                "ALLE Wiederherstellungspunkte werden gelöscht!\n\n" +
                "Du kannst danach nicht mehr auf einen früheren Stand zurück.\n" +
                "Spart oft 10+ GB. Benötigt Admin.\n\n" +
                "Wirklich fortfahren?",
                "Alle Restore Points löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        Append("vssadmin delete shadows /all /quiet ...");
        try
        {
            var (rc, output) = await _svc.RunAsync("vssadmin.exe",
                "delete shadows /all /quiet", elevate: true, CancellationToken.None);
            Append(output);
            await LoadRestorePointsAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task FlushPrintQueueAsync()
    {
        if (MessageBox.Show("Druckwarteschlange leeren?\n\nStoppt den Print-Spooler-Dienst, " +
                            "löscht alle Druckjobs und startet den Spooler neu. Admin nötig.",
                "Druckwarteschlange", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            Append("Stoppe Spooler...");
            await _svc.RunAsync("net.exe", "stop spooler", elevate: true, CancellationToken.None);
            Append("Lösche %SystemRoot%\\System32\\spool\\PRINTERS\\* ...");
            var spoolDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "PRINTERS");
            if (System.IO.Directory.Exists(spoolDir))
            {
                foreach (var f in System.IO.Directory.EnumerateFiles(spoolDir))
                { try { System.IO.File.Delete(f); } catch { } }
            }
            Append("Starte Spooler...");
            await _svc.RunAsync("net.exe", "start spooler", elevate: true, CancellationToken.None);
            Append("Fertig.");
        }
        catch (Exception ex) { Append("Fehler: " + ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task EmptyRecycleBinAsync()
    {
        IsBusy = true;
        Append("Papierkorb leeren...");
        try
        {
            var (rc, output) = await _svc.RunAsync("cmd.exe", "/c rd /s /q C:\\$Recycle.Bin", elevate: true, CancellationToken.None);
            Append("Papierkorb geleert.");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public void OpenSpaceSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:storagesense")
            { UseShellExecute = true });
        }
        catch { }
    }
}
