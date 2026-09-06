namespace Cleaner.App.Services;

/// <summary>Zustand einer Spalte innerhalb einer gespeicherten Tabellen-Ansicht.</summary>
public sealed class GridColumnState
{
    /// <summary>Stabile Id = Position der Spalte in der XAML-Definition.</summary>
    public int Index { get; set; }
    public bool Visible { get; set; } = true;
    public int DisplayIndex { get; set; }
    public double Width { get; set; }
    /// <summary>DataGridLengthUnitType als int (Pixel/Star/Auto …).</summary>
    public int WidthUnit { get; set; }
}

/// <summary>Eine benannte Tabellen-Ansicht: Gruppierung, Sortierung, Spalten-Layout.</summary>
public sealed class GridViewState
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Property-Pfad der Gruppier-Spalte (null = keine Gruppierung).</summary>
    public string? GroupBy { get; set; }
    public string? SortBy { get; set; }
    /// <summary>ListSortDirection als int, -1 = keine Sortierung.</summary>
    public int SortDirection { get; set; } = -1;
    public List<GridColumnState> Columns { get; set; } = new();
}

public sealed class GridViewsEntry
{
    /// <summary>Zuletzt aktive Ansicht — wird beim nächsten Öffnen automatisch angewendet.</summary>
    public string? LastActive { get; set; }
    public List<GridViewState> Views { get; set; } = new();
}

/// <summary>
/// Persistiert gespeicherte Tabellen-Ansichten pro Grid-Id nach
/// %LocalAppData%\PowerClean\gridviews.json.
/// </summary>
public sealed class GridViewService
{
    private const string FileName = "gridviews.json";

    private readonly AppDataService _data;
    private readonly Dictionary<string, GridViewsEntry> _store;

    public GridViewService(AppDataService data)
    {
        _data = data;
        _store = _data.Load(FileName, () => new Dictionary<string, GridViewsEntry>());
    }

    public GridViewsEntry For(string gridId)
    {
        if (!_store.TryGetValue(gridId, out var entry))
            _store[gridId] = entry = new GridViewsEntry();
        return entry;
    }

    public void Save(string gridId, GridViewState view)
    {
        var entry = For(gridId);
        entry.Views.RemoveAll(v => string.Equals(v.Name, view.Name, StringComparison.OrdinalIgnoreCase));
        entry.Views.Add(view);
        entry.LastActive = view.Name;
        Persist();
    }

    public void Delete(string gridId, string name)
    {
        var entry = For(gridId);
        entry.Views.RemoveAll(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(entry.LastActive, name, StringComparison.OrdinalIgnoreCase))
            entry.LastActive = null;
        Persist();
    }

    public void SetLastActive(string gridId, string? name)
    {
        For(gridId).LastActive = name;
        Persist();
    }

    private void Persist() => _data.Save(FileName, _store);
}
