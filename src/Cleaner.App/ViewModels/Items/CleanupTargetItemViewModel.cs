using Cleaner.Core.Cleaners;
using Cleaner.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Items;

/// <summary>
/// Wrappt einen <see cref="ICleanupTarget"/> für die UI: hält Auswahl-Zustand,
/// Scan-Ergebnis, Fortschritts-Anzeige.
/// </summary>
public sealed partial class CleanupTargetItemViewModel : ObservableObject
{
    public CleanupTargetItemViewModel(ICleanupTarget target)
    {
        Target = target;
        // Default-Selection: Safe + Recommended sind vorausgewählt.
        IsSelected = target.SafetyLevel <= SafetyLevel.Recommended;
    }

    public ICleanupTarget Target { get; }
    public string Id => Target.Id;
    public string Name => Target.Name;
    public string Description => Target.Description;
    public string IconGlyph => Target.IconGlyph;
    public CleanupCategory Category => Target.Category;
    public SafetyLevel SafetyLevel => Target.SafetyLevel;
    public bool RequiresAdmin => Target.RequiresAdmin;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private bool _hasScanResult;

    [ObservableProperty]
    private long _scannedSize;

    [ObservableProperty]
    private int _scannedFiles;

    [ObservableProperty]
    private long _freedSize;

    [ObservableProperty]
    private int _filesDeleted;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private ScanResult? _lastScan;

    public const int MaxVisiblePaths = 200;

    /// <summary>Die ersten N Pfade aus dem letzten Scan — für die expandierte Detail-Anzeige.</summary>
    public IReadOnlyList<string> VisiblePaths
        => LastScan is null ? Array.Empty<string>() : LastScan.Paths.Take(MaxVisiblePaths).ToList();

    /// <summary>Wie viele Pfade ausgeblendet werden (= zu viele um alle in der UI zu zeigen).</summary>
    public int HiddenPathCount
        => (LastScan?.Paths.Count ?? 0) - VisiblePaths.Count;

    partial void OnLastScanChanged(ScanResult? value)
    {
        OnPropertyChanged(nameof(VisiblePaths));
        OnPropertyChanged(nameof(HiddenPathCount));
    }

    [RelayCommand]
    private void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Cleaner.App.Helpers.PathOpener.RevealInExplorer(path);
    }

    [RelayCommand]
    private void CopyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Cleaner.App.Helpers.PathOpener.CopyToClipboard(path);
    }
}
