using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Cleaner.App.Services;

/// <summary>
/// Wrappt Velopack-Update-Logik. Update-Source ist das GitHub-Repo.
/// Funktioniert nur wenn die App via Velopack-Installer installiert wurde
/// (siehe <see cref="IsManaged"/>). Im Dev-Build immer no-op.
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager? _mgr;

    public string CurrentVersion { get; }
    public string Repository { get; } = "https://github.com/fgilde/PowerClean";

    public UpdateService()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersion = ver is null ? "dev" : $"{ver.Major}.{ver.Minor}.{ver.Build}";

        try
        {
            _mgr = new UpdateManager(new GithubSource(Repository, null, prerelease: false));
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("UpdateService.ctor", ex);
        }
    }

    /// <summary>True wenn die App via Velopack-Installer installiert wurde.</summary>
    public bool IsManaged => _mgr?.IsInstalled == true;

    public async Task<UpdateInfo?> CheckAsync()
    {
        if (_mgr is null || !IsManaged) return null;
        try
        {
            return await _mgr.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("UpdateService.CheckAsync", ex);
            return null;
        }
    }

    public async Task<bool> DownloadAsync(UpdateInfo info, IProgress<int>? progress = null)
    {
        if (_mgr is null) return false;
        try
        {
            await _mgr.DownloadUpdatesAsync(info, progress is null ? null : p => progress.Report(p));
            return true;
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("UpdateService.DownloadAsync", ex);
            return false;
        }
    }

    public void ApplyAndRestart(UpdateInfo info)
    {
        if (_mgr is null) return;
        try
        {
            _mgr.ApplyUpdatesAndRestart(info);
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("UpdateService.ApplyAndRestart", ex);
        }
    }
}
