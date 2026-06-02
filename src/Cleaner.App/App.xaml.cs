using System.Windows;
using Cleaner.App.Helpers;
using Cleaner.App.ViewModels;
using Cleaner.App.ViewModels.Pages;
using Cleaner.App.Views;
using Cleaner.App.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui.Appearance;

namespace Cleaner.App;

public partial class App : Application
{
    private readonly IHost _host;

    // Log liegt direkt neben der EXE — leicht zu finden für User & Entwickler.
    private static readonly string LogPath = System.IO.Path.Combine(
        AppContext.BaseDirectory, "powerclean.log");

    public App()
    {
        LogInfo("=========================================================");
        LogInfo($"App startup, PID={Environment.ProcessId}, .NET={Environment.Version}");

        DispatcherUnhandledException += (_, e) =>
        {
            LogException("Dispatcher", e.Exception);
            try
            {
                MessageBox.Show(
                    e.Exception.ToString() + "\n\nLog: " + LogPath,
                    "Cleaner — unerwarteter Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* Window might already be gone */ }
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException("AppDomain", ex);
                try
                {
                    MessageBox.Show(ex.ToString() + "\n\nLog: " + LogPath,
                        "Cleaner — fatal", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { /* shutdown is happening; best-effort */ }
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogException("UnobservedTask", e.Exception);
            e.SetObserved();
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddCleanerCore();

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<SystemCleanerViewModel>();
                services.AddSingleton<DeveloperCleanerViewModel>();
                services.AddSingleton<DiskAnalyzerViewModel>();
                services.AddSingleton<DuplicatesViewModel>();
                services.AddSingleton<LargeFilesViewModel>();
                services.AddSingleton<LogFinderViewModel>();
                services.AddSingleton<FolderCompareViewModel>();
                services.AddSingleton<OrphanUserDataViewModel>();
                services.AddSingleton<CleanupHistoryViewModel>();
                services.AddSingleton<AutostartViewModel>();
                services.AddSingleton<InstalledProgramsViewModel>();
                services.AddSingleton<ServicesViewModel>();
                services.AddSingleton<ProcessMonitorViewModel>();
                services.AddSingleton<RegistryCleanerViewModel>();
                services.AddSingleton<SystemMaintenanceViewModel>();
                services.AddSingleton<SettingsViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
                services.AddSingleton<DashboardPage>();
                services.AddSingleton<SystemCleanerPage>();
                services.AddSingleton<DeveloperCleanerPage>();
                services.AddSingleton<DiskAnalyzerPage>();
                services.AddSingleton<DuplicatesPage>();
                services.AddSingleton<LargeFilesPage>();
                services.AddSingleton<LogFinderPage>();
                services.AddSingleton<FolderComparePage>();
                services.AddSingleton<OrphanUserDataPage>();
                services.AddSingleton<CleanupHistoryPage>();
                services.AddSingleton<AutostartPage>();
                services.AddSingleton<InstalledProgramsPage>();
                services.AddSingleton<ServicesPage>();
                services.AddSingleton<ProcessMonitorPage>();
                services.AddSingleton<RegistryCleanerPage>();
                services.AddSingleton<SystemMaintenancePage>();
                services.AddSingleton<SettingsPage>();
            })
            .Build();

        Services = _host.Services;
    }

    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await _host.StartAsync();

        // Globale Schutzregeln aus den Einstellungen anwenden und auf Änderungen reagieren.
        var settings = _host.Services.GetRequiredService<AppSettings>();
        ApplyCleanupPolicy(settings);
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.ExclusionPatterns) or nameof(AppSettings.CleanMinAgeDays))
                ApplyCleanupPolicy(settings);
        };

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // Update-Check im Hintergrund — wenn was Neues da ist, zeig einen unobtrusive Dialog
        _ = CheckForUpdatesOnStartupAsync();
    }

    private static void ApplyCleanupPolicy(AppSettings s)
    {
        Cleaner.Core.Cleaners.GlobalCleanupPolicy.ExcludeSubstrings = s.GetExclusionList();
        Cleaner.Core.Cleaners.GlobalCleanupPolicy.MinimumAge =
            s.CleanMinAgeDays > 0 ? TimeSpan.FromDays(s.CleanMinAgeDays) : null;
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var updates = _host.Services.GetRequiredService<Cleaner.App.Services.UpdateService>();
            if (!updates.IsManaged) return;

            var info = await updates.CheckAsync();
            if (info is null) return;

            await Dispatcher.InvokeAsync(() =>
            {
                var ver = info.TargetFullRelease.Version.ToString();
                var ask = MessageBox.Show(
                    $"PowerClean {ver} ist verfügbar (aktuell: {updates.CurrentVersion}).\n\n" +
                    "Jetzt herunterladen und neu starten?",
                    "PowerClean Update", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (ask == MessageBoxResult.Yes)
                {
                    _ = Task.Run(async () =>
                    {
                        if (await updates.DownloadAsync(info))
                            await Dispatcher.InvokeAsync(() => updates.ApplyAndRestart(info));
                    });
                }
            });
        }
        catch (Exception ex) { LogException("UpdateCheck", ex); }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync(TimeSpan.FromSeconds(2));
        _host.Dispose();
        base.OnExit(e);
    }

    public static void LogException(string source, Exception ex)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(LogPath)!;
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(LogPath,
                $"[{DateTimeOffset.UtcNow:O}] [EXCEPTION] [{source}]\n{ex}\n\n");
        }
        catch { /* logging must never throw */ }
    }

    public static void LogInfo(string message)
    {
        try
        {
            System.IO.File.AppendAllText(LogPath,
                $"[{DateTimeOffset.UtcNow:O}] [INFO] {message}\n");
        }
        catch { }
    }
}
