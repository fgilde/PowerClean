using Cleaner.App.ViewModels;
using Cleaner.Core.Cleaners;
using Cleaner.Core.Cleaners.Browsers;
using Cleaner.Core.Cleaners.Developer;
using Cleaner.Core.Cleaners.Windows;
using Cleaner.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cleaner.App.Helpers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCleanerCore(this IServiceCollection services)
    {
        services.AddSingleton<AppSettings>();

        // Infrastructure
        services.AddSingleton<IFileSystemOperations, FileSystemOperations>();
        services.AddSingleton<IDiskScanner, DiskScanner>();
        services.AddSingleton<IDuplicateFinder, DuplicateFinder>();
        services.AddSingleton<ILargeFilesFinder, LargeFilesFinder>();
        services.AddSingleton<IDriveInfoService, DriveInfoService>();
        services.AddSingleton<ICleanerRegistry, CleanerRegistry>();
        services.AddSingleton<IAutostartScanner, AutostartScanner>();
        services.AddSingleton<IInstalledProgramsScanner, InstalledProgramsScanner>();
        services.AddSingleton<IServiceScanner, ServiceScanner>();
        services.AddSingleton<IProcessMonitor, ProcessMonitorService>();
        services.AddSingleton<IRegistryScanner, RegistryScanner>();
        services.AddSingleton<ISystemMaintenanceService, SystemMaintenanceService>();
        services.AddSingleton<Cleaner.App.Services.RunningTaskRegistry>();
        services.AddSingleton<Cleaner.App.Services.UpdateService>();

        // Windows-System cleaners
        services.AddSingleton<ICleanupTarget, UserTempCleaner>();
        services.AddSingleton<ICleanupTarget, WindowsTempCleaner>();
        services.AddSingleton<ICleanupTarget, ThumbnailCacheCleaner>();
        services.AddSingleton<ICleanupTarget, IconCacheCleaner>();
        services.AddSingleton<ICleanupTarget, WindowsUpdateCacheCleaner>();
        services.AddSingleton<ICleanupTarget, WindowsLogsCleaner>();
        services.AddSingleton<ICleanupTarget, RecycleBinCleaner>();
        services.AddSingleton<ICleanupTarget, DeliveryOptimizationCleaner>();
        services.AddSingleton<ICleanupTarget, PrefetchCleaner>();
        services.AddSingleton<ICleanupTarget, MemoryDumpCleaner>();

        // Browser
        services.AddSingleton<ICleanupTarget, ChromeCacheCleaner>();
        services.AddSingleton<ICleanupTarget, EdgeCacheCleaner>();
        services.AddSingleton<ICleanupTarget, BraveCacheCleaner>();
        services.AddSingleton<ICleanupTarget, FirefoxCacheCleaner>();

        // Developer
        services.AddSingleton<ICleanupTarget, JetBrainsCleaner>();
        services.AddSingleton<ICleanupTarget, VisualStudioCleaner>();
        services.AddSingleton<ICleanupTarget, NuGetHttpCacheCleaner>();
        services.AddSingleton<ICleanupTarget, NuGetGlobalPackagesCleaner>();
        services.AddSingleton<ICleanupTarget, NpmCacheCleaner>();
        services.AddSingleton<ICleanupTarget, DotNetBuildArtifactsCleaner>();
        services.AddSingleton<ICleanupTarget, NodeModulesCleaner>();
        services.AddSingleton<ICleanupTarget, DockerCleaner>();

        return services;
    }
}
