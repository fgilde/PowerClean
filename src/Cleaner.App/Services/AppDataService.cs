using System.IO;
using System.Text.Json;

namespace Cleaner.App.Services;

/// <summary>
/// Zentraler Zugriff auf den App-Datenordner (%LocalAppData%\PowerClean) und
/// generisches Laden/Speichern von JSON-Dateien. Fundament für Einstellungen,
/// Profile, Schutzregeln und Cleanup-Verlauf.
/// </summary>
public sealed class AppDataService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public AppDataService()
    {
        RootDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerClean");
        try { Directory.CreateDirectory(RootDir); } catch { /* best effort */ }
    }

    /// <summary>Basis-Ordner für alle persistierten Daten.</summary>
    public string RootDir { get; }

    public string PathFor(string fileName) => Path.Combine(RootDir, fileName);

    /// <summary>Lädt eine JSON-Datei. Bei Fehler/fehlend wird der Fallback erzeugt.</summary>
    public T Load<T>(string fileName, Func<T> fallback)
    {
        try
        {
            var path = PathFor(fileName);
            if (!File.Exists(path)) return fallback();
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return fallback();
            return JsonSerializer.Deserialize<T>(json, Json) ?? fallback();
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("AppData.Load:" + fileName, ex);
            return fallback();
        }
    }

    /// <summary>Speichert ein Objekt als JSON. Fehler werden geloggt, nie geworfen.</summary>
    public void Save<T>(string fileName, T value)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, Json);
            File.WriteAllText(PathFor(fileName), json);
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("AppData.Save:" + fileName, ex);
        }
    }
}
