namespace Cleaner.Core.Models;

public sealed class ScanResult
{
    public required string TargetId { get; init; }
    public required long SizeBytes { get; init; }
    public required int FileCount { get; init; }
    public required IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.UtcNow;

    public static ScanResult Empty(string targetId) => new()
    {
        TargetId = targetId,
        SizeBytes = 0,
        FileCount = 0,
        Paths = Array.Empty<string>(),
    };
}
