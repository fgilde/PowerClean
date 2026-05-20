namespace Cleaner.Core.Models;

public readonly record struct ScanProgress(
    string TargetId,
    string CurrentPath,
    long BytesSoFar,
    int FilesSoFar);

public readonly record struct CleanProgress(
    string TargetId,
    string CurrentPath,
    long BytesFreed,
    int FilesDeleted,
    int FilesTotal);
