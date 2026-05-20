using System.ComponentModel;

namespace Cleaner.App.Localization;

/// <summary>
/// Singleton-Wrapper für die aktive Sprache. XAML bindet via <see cref="TExtension"/>
/// auf den Indexer — beim Sprachwechsel feuern wir <c>"Item[]"</c>-PropertyChanged
/// und WPF aktualisiert ALLE Indexer-Bindings.
/// </summary>
public sealed class L : INotifyPropertyChanged
{
    public static L Current { get; } = new();

    private L() { _language = Translations.DetectInitialLanguage(); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _language;
    public string Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            // ALLE Indexer-Bindings erneuern
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Feuert nach jedem Language-Wechsel — für Code-Behind, das nicht über Binding läuft.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Indexer für die TExtension: <c>L.Current["MeinKey"]</c>.</summary>
    public string this[string key] => Translations.Get(key, _language);

    /// <summary>Convenience: T mit Argumenten (string.Format).</summary>
    public string Format(string key, params object[] args)
        => string.Format(Translations.Get(key, _language), args);
}
