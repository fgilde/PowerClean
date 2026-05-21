using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.App.ViewModels.Items;

/// <summary>
/// UI-Wrapper um einen <see cref="LogFileEntry"/> — fügt einen Multi-Select-Zustand hinzu,
/// damit Bulk-Löschen aus der DataGrid heraus funktioniert.
/// Der Core-Eintrag bleibt unverändert (immutable mit init-Settern).
/// </summary>
public sealed partial class LogFileItemViewModel : ObservableObject
{
    public LogFileItemViewModel(LogFileEntry entry, bool isSelected = false)
    {
        Entry = entry;
        _isSelected = isSelected;
    }

    public LogFileEntry Entry { get; }

    public string Path => Entry.Path;
    public long Size => Entry.Size;
    public DateTime LastWriteUtc => Entry.LastWriteUtc;
    public string Root => Entry.Root;
    public string Pattern => Entry.Pattern;

    [ObservableProperty]
    private bool _isSelected;
}
