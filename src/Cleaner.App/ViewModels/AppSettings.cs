using System.ComponentModel;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.App.ViewModels;

/// <summary>
/// App-weite Einstellungen. Wird per DI bereitgestellt und von Pages observiert.
/// Persistiert sich selbst nach %LocalAppData%\PowerClean\settings.json.
/// </summary>
public sealed partial class AppSettings : ObservableObject
{
    private readonly AppDataService? _store;
    private bool _loaded;

    /// <summary>Design-Time / Fallback-Konstruktor ohne Persistenz.</summary>
    public AppSettings() { }

    public AppSettings(AppDataService store)
    {
        _store = store;
        Load();
        _loaded = true;
    }

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

    /// <summary>
    /// Globale Schutzregeln: Pfade/Substrings (eine pro Zeile), die nie gelöscht werden.
    /// </summary>
    [ObservableProperty]
    private string _exclusionPatterns = string.Empty;

    /// <summary>Nur Dateien älter als X Tage aufräumen (0 = kein Filter).</summary>
    [ObservableProperty]
    private int _cleanMinAgeDays;

    partial void OnLanguageChanged(string value)
    {
        Localization.L.Current.Language = value;
    }

    public List<string> CustomScanRoots { get; } = new();

    /// <summary>Liefert die Schutzregeln als bereinigte Liste.</summary>
    public IReadOnlyList<string> GetExclusionList()
        => ExclusionPatterns
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    // --- Persistenz -------------------------------------------------------

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loaded) Save();
    }

    /// <summary>Explizit speichern (z. B. nach Mutation von <see cref="CustomScanRoots"/>).</summary>
    public void Save() => _store?.Save("settings.json", ToState());

    private void Load()
    {
        if (_store is null) return;
        var s = _store.Load("settings.json", () => new AppSettingsState());
        ApplyFrom(s);
    }

    private AppSettingsState ToState() => new()
    {
        UseRecycleBin = UseRecycleBin,
        UseDarkTheme = UseDarkTheme,
        LargeFileThresholdMb = LargeFileThresholdMb,
        DuplicateMinSizeKb = DuplicateMinSizeKb,
        LogFinderPatterns = LogFinderPatterns,
        LogFinderMinAgeDays = LogFinderMinAgeDays,
        Language = Language,
        ExclusionPatterns = ExclusionPatterns,
        CleanMinAgeDays = CleanMinAgeDays,
        CustomScanRoots = CustomScanRoots.ToList(),
    };

    private void ApplyFrom(AppSettingsState s)
    {
        UseRecycleBin = s.UseRecycleBin;
        UseDarkTheme = s.UseDarkTheme;
        LargeFileThresholdMb = s.LargeFileThresholdMb;
        DuplicateMinSizeKb = s.DuplicateMinSizeKb;
        LogFinderPatterns = string.IsNullOrWhiteSpace(s.LogFinderPatterns) ? LogFinderPatterns : s.LogFinderPatterns;
        LogFinderMinAgeDays = s.LogFinderMinAgeDays;
        if (!string.IsNullOrWhiteSpace(s.Language)) Language = s.Language;
        ExclusionPatterns = s.ExclusionPatterns ?? string.Empty;
        CleanMinAgeDays = s.CleanMinAgeDays;
        CustomScanRoots.Clear();
        if (s.CustomScanRoots is not null) CustomScanRoots.AddRange(s.CustomScanRoots);
    }
}

/// <summary>Serialisierbares Abbild der persistierten Einstellungen.</summary>
public sealed class AppSettingsState
{
    public bool UseRecycleBin { get; set; } = true;
    public bool UseDarkTheme { get; set; } = true;
    public long LargeFileThresholdMb { get; set; } = 100;
    public long DuplicateMinSizeKb { get; set; } = 100;
    public string LogFinderPatterns { get; set; } = string.Empty;
    public int LogFinderMinAgeDays { get; set; } = 7;
    public string Language { get; set; } = string.Empty;
    public string ExclusionPatterns { get; set; } = string.Empty;
    public int CleanMinAgeDays { get; set; }
    public List<string> CustomScanRoots { get; set; } = new();
}
