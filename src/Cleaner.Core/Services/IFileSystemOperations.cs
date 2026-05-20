namespace Cleaner.Core.Services;

public interface IFileSystemOperations
{
    /// <summary>
    /// Löscht eine Datei. Falls <paramref name="useRecycleBin"/> true ist, wird der Windows-Papierkorb verwendet.
    /// </summary>
    bool DeleteFile(string path, bool useRecycleBin);

    /// <summary>Löscht einen Ordner rekursiv. Mit Papierkorb-Option.</summary>
    bool DeleteDirectory(string path, bool useRecycleBin);
}
