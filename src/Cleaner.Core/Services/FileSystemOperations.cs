using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Cleaner.Core.Services;

public sealed class FileSystemOperations : IFileSystemOperations
{
    private readonly ILogger<FileSystemOperations>? _logger;

    public FileSystemOperations(ILogger<FileSystemOperations>? logger = null)
    {
        _logger = logger;
    }

    public bool DeleteFile(string path, bool useRecycleBin)
    {
        try
        {
            if (!File.Exists(path)) return false;

            if (useRecycleBin)
                return ShellDelete(path);

            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Konnte Datei nicht löschen: {Path}", path);
            return false;
        }
    }

    public bool DeleteDirectory(string path, bool useRecycleBin)
    {
        try
        {
            if (!Directory.Exists(path)) return false;

            if (useRecycleBin)
                return ShellDelete(path);

            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Konnte Ordner nicht löschen: {Path}", path);
            return false;
        }
    }

    // SHFileOperation für Papierkorb-Support
    private const int FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    private bool ShellDelete(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
        };
        int rc = SHFileOperation(ref op);
        if (rc != 0)
        {
            _logger?.LogDebug("SHFileOperation lieferte {Rc} für {Path}", rc, path);
            return false;
        }
        return !op.fAnyOperationsAborted;
    }
}
