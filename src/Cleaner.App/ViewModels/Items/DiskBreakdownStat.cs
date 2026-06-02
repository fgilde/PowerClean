namespace Cleaner.App.ViewModels.Items;

/// <summary>
/// Eine Zeile in der Disk-Analyzer-Aufschlüsselung (Dateityp oder Altersgruppe):
/// Label, Größe, Anteil (0–1) und Balkenfarbe.
/// </summary>
public sealed class DiskBreakdownStat
{
    public required string Label { get; init; }
    public long Bytes { get; init; }
    public int FileCount { get; init; }

    /// <summary>Anteil an der Gesamtgröße (0–1) — für den Balken.</summary>
    public double Fraction { get; init; }

    /// <summary>Balkenfarbe als Hex (#RRGGBB).</summary>
    public required string ColorHex { get; init; }
}
