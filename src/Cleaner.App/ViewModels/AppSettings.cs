using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.App.ViewModels;

/// <summary>
/// App-weite Einstellungen. Wird per DI bereitgestellt und von Pages observiert.
/// </summary>
public sealed partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private bool _useRecycleBin = true;

    [ObservableProperty]
    private bool _useDarkTheme = true;

    [ObservableProperty]
    private long _largeFileThresholdMb = 100;

    [ObservableProperty]
    private long _duplicateMinSizeKb = 100;

    [ObservableProperty]
    private string _logFinderPatterns = "*.log, log*.txt, *.tmp, *.temp, *.bak, *.old, *.dmp, *.mdmp";

    [ObservableProperty]
    private int _logFinderMinAgeDays = 7;

    [ObservableProperty]
    private string _language = Localization.Translations.DetectInitialLanguage();

    partial void OnLanguageChanged(string value)
    {
        Localization.L.Current.Language = value;
    }

    public List<string> CustomScanRoots { get; } = new();
}
