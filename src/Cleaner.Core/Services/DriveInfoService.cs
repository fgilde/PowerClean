namespace Cleaner.Core.Services;

public sealed class DriveSummary
{
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public required string Label { get; init; }
    public required long TotalSize { get; init; }
    public required long FreeSpace { get; init; }
    public long UsedSpace => TotalSize - FreeSpace;
    public double UsedPercent => TotalSize == 0 ? 0 : (double)UsedSpace / TotalSize;
    public required string Format { get; init; }
    public required DriveType DriveType { get; init; }
}

public interface IDriveInfoService
{
    IReadOnlyList<DriveSummary> EnumerateDrives();
}

public sealed class DriveInfoService : IDriveInfoService
{
    public IReadOnlyList<DriveSummary> EnumerateDrives()
    {
        var list = new List<DriveSummary>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            if (d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;

            list.Add(new DriveSummary
            {
                Name = d.Name,
                RootPath = d.RootDirectory.FullName,
                Label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Lokaler Datenträger" : d.VolumeLabel,
                TotalSize = d.TotalSize,
                FreeSpace = d.AvailableFreeSpace,
                Format = d.DriveFormat,
                DriveType = d.DriveType,
            });
        }
        return list;
    }
}
