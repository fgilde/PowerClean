using System.Runtime.InteropServices;
using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Windows;

public sealed class RecycleBinCleaner : ICleanupTarget
{
    private readonly IFileSystemOperations _fs;

    public RecycleBinCleaner(IFileSystemOperations fs) { _fs = fs; }

    public string Id => "system.recycle-bin";
    public string Name => "Papierkorb";
    public string Description => "Endgültig löschen aller im Windows-Papierkorb befindlichen Dateien.";
    public string IconGlyph => "";
    public CleanupCategory Category => CleanupCategory.WindowsSystem;
    public SafetyLevel SafetyLevel => SafetyLevel.Recommended;
    public bool RequiresAdmin => false;

    public bool IsAvailable() => true;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        int hr = SHQueryRecycleBin(null, ref info);
        if (hr != 0)
            return Task.FromResult(ScanResult.Empty(Id));

        // Wir verwenden 1 Pseudo-Pfad, damit FreedBytes nach dem Clean korrekt protokolliert wird
        return Task.FromResult(new ScanResult
        {
            TargetId = Id,
            SizeBytes = info.i64Size,
            FileCount = (int)Math.Min(info.i64NumItems, int.MaxValue),
            Paths = info.i64NumItems > 0 ? new[] { "<<RecycleBin>>" } : Array.Empty<string>(),
        });
    }

    public Task<CleanResult> CleanAsync(
        ScanResult scan,
        bool useRecycleBin,
        IProgress<CleanProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (scan.FileCount == 0)
            return Task.FromResult(new CleanResult { TargetId = Id, FreedBytes = 0, FilesDeleted = 0 });

        int hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        if (hr == 0)
        {
            return Task.FromResult(new CleanResult
            {
                TargetId = Id,
                FreedBytes = scan.SizeBytes,
                FilesDeleted = scan.FileCount,
            });
        }

        return Task.FromResult(new CleanResult
        {
            TargetId = Id,
            FreedBytes = 0,
            FilesDeleted = 0,
            Errors = new[] { $"SHEmptyRecycleBin lieferte HRESULT 0x{hr:X8}" },
        });
    }
}
