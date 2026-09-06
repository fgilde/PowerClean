using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using Cleaner.App.Localization;
using Cleaner.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cleaner.App.Controls;

/// <summary>
/// Wiederverwendbare "richtig gute" Tabelle: Live-Suche über alle gebundenen Spalten,
/// Gruppierung, Spalten ein-/ausblenden, Spalten verschieben/sortieren (nativ) und pro Tabelle
/// speicherbare Ansichten (Layout + Gruppierung + Sortierung) mit Ansicht-Dropdown.
/// Spalten werden wie beim normalen DataGrid direkt als Content definiert:
/// <code>
/// &lt;controls:PowerGrid GridId="services" ItemsSource="{Binding ...}"&gt;
///     &lt;DataGridTextColumn ... /&gt;
/// &lt;/controls:PowerGrid&gt;
/// </code>
/// </summary>
[ContentProperty(nameof(Columns))]
public partial class PowerGrid : UserControl
{
    private const string DefaultViewMarker = "⌂"; // internes Sentinel, Anzeige kommt aus Übersetzung

    private GridViewService? _viewService;
    private GridViewState? _defaultState;
    private bool _applyingView;
    private string _searchText = "";
    private readonly Dictionary<Type, PropertyInfo?[]> _propCache = new();
    private string[] _searchPaths = [];

    public PowerGrid()
    {
        InitializeComponent();
        // Direkt die Collection des inneren DataGrid — keine Transplantation, damit die
        // native Breiten-/Star-Logik des DataGrid ungestört bleibt.
        Columns.CollectionChanged += ColumnsOnCollectionChanged;
    }

    /// <summary>Spalten — Content-Property, ist 1:1 die Collection des inneren DataGrid.</summary>
    public ObservableCollection<DataGridColumn> Columns => InnerGrid.Columns;

    #region Dependency properties

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(PowerGrid),
            new PropertyMetadata(null, (d, e) => ((PowerGrid)d).OnItemsSourceChanged((IEnumerable?)e.NewValue)));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty GridIdProperty =
        DependencyProperty.Register(nameof(GridId), typeof(string), typeof(PowerGrid), new PropertyMetadata(null));

    /// <summary>Stabile Id für die Ansichts-Persistenz (z.B. "services"). Ohne Id kein Speichern.</summary>
    public string? GridId
    {
        get => (string?)GetValue(GridIdProperty);
        set => SetValue(GridIdProperty, value);
    }

    public static readonly DependencyProperty RowContextMenuProperty =
        DependencyProperty.Register(nameof(RowContextMenu), typeof(ContextMenu), typeof(PowerGrid),
            new PropertyMetadata(null, (d, _) => ((PowerGrid)d).ApplyRowStyle()));

    /// <summary>
    /// Kontextmenü für Zeilen. Menü-Items erreichen die Page über
    /// <c>{Binding Tag.ViewModel.XCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}</c>
    /// — das Tag des Menüs wird automatisch auf den DataContext der Page gesetzt.
    /// </summary>
    public ContextMenu? RowContextMenu
    {
        get => (ContextMenu?)GetValue(RowContextMenuProperty);
        set => SetValue(RowContextMenuProperty, value);
    }

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(PowerGrid),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    #endregion

    #region Setup

    private void PowerGrid_OnLoaded(object sender, RoutedEventArgs e)
    {
        // SelectedItem durchreichen (einmalig verdrahten)
        if (InnerGrid.GetBindingExpression(DataGrid.SelectedItemProperty) is null)
            InnerGrid.SetBinding(DataGrid.SelectedItemProperty, new Binding(nameof(SelectedItem))
            {
                Source = this,
                Mode = BindingMode.TwoWay,
            });

        ApplyRowStyle();

        if (_defaultState == null)
        {
            _defaultState = CaptureState("");
            _viewService = App.Services?.GetService<GridViewService>();
            RefreshViewList();
            RestoreLastActiveView();
        }
    }

    private void ColumnsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _searchPaths = CollectSearchPaths();
    }

    private void OnItemsSourceChanged(IEnumerable? value)
    {
        InnerGrid.ItemsSource = value;
        if (value == null) return;
        var view = CollectionViewSource.GetDefaultView(value);
        if (view != null)
            view.Filter = FilterItem;
    }

    private void ApplyRowStyle()
    {
        var baseStyle = (Style)FindResource("PowerGridRowStyle");
        if (RowContextMenu is null)
        {
            InnerGrid.RowStyle = baseStyle;
            return;
        }
        // Tag = Page-DataContext, damit Menü-Bindings die Page-Commands erreichen
        // (ContextMenus hängen nicht im Visual Tree — FindAncestor zur Page schlägt dort fehl).
        RowContextMenu.Tag = DataContext;
        var style = new Style(typeof(DataGridRow), baseStyle);
        style.Setters.Add(new Setter(ContextMenuProperty, RowContextMenu));
        InnerGrid.RowStyle = style;
    }

    #endregion

    #region Suche

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text?.Trim() ?? "";
        CurrentView?.Refresh();
    }

    private ICollectionView? CurrentView
        => ItemsSource is null ? null : CollectionViewSource.GetDefaultView(ItemsSource);

    private bool FilterItem(object item)
    {
        if (string.IsNullOrEmpty(_searchText)) return true;
        var props = GetProps(item.GetType());
        foreach (var p in props)
        {
            if (p is null) continue;
            object? v;
            try { v = p.GetValue(item); } catch { continue; }
            if (v is null) continue;
            if (v.ToString()?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }
        return false;
    }

    // Durchsuchte Properties = die Property-Pfade der Spalten (nur einfache Pfade ohne Punkt).
    private string[] CollectSearchPaths() =>
        Columns.Select(PathOf)
            .Where(p => !string.IsNullOrEmpty(p) && !p!.Contains('.'))
            .Distinct()
            .ToArray()!;

    private static string? PathOf(DataGridColumn col)
    {
        if (!string.IsNullOrEmpty(col.SortMemberPath)) return col.SortMemberPath;
        if (col is DataGridBoundColumn { Binding: Binding b }) return b.Path?.Path;
        return null;
    }

    private PropertyInfo?[] GetProps(Type type)
    {
        if (_propCache.TryGetValue(type, out var cached)) return cached;
        var props = _searchPaths.Select(p => type.GetProperty(p)).ToArray();
        _propCache[type] = props;
        return props;
    }

    #endregion

    #region Gruppierung

    private sealed record GroupOption(string Header, string? Path)
    {
        public override string ToString() => Header;
    }

    private void GroupCombo_OnDropDownOpened(object? sender, EventArgs e) => RefreshGroupOptions();

    private void RefreshGroupOptions()
    {
        var current = (GroupCombo.SelectedItem as GroupOption)?.Path;
        var options = new List<GroupOption> { new(L.Current["Grid.NoGrouping"], null) };
        options.AddRange(Columns
            .Select(c => (Header: c.Header?.ToString(), Path: PathOf(c)))
            .Where(t => !string.IsNullOrEmpty(t.Header) && !string.IsNullOrEmpty(t.Path) && !t.Path!.Contains('.'))
            .Select(t => new GroupOption(t.Header!, t.Path)));

        _applyingView = true;
        GroupCombo.ItemsSource = options;
        GroupCombo.SelectedItem = options.FirstOrDefault(o => o.Path == current) ?? options[0];
        _applyingView = false;
    }

    private void GroupCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingView) return;
        ApplyGrouping((GroupCombo.SelectedItem as GroupOption)?.Path);
    }

    private void ApplyGrouping(string? path)
    {
        var view = CurrentView;
        if (view is null) return;
        view.GroupDescriptions.Clear();
        // GroupStyle nur bei aktiver Gruppierung anhängen — ein permanent gesetzter GroupStyle
        // bricht die Spaltenbreiten-Verteilung des DataGrid (alles klebt auf MinWidth).
        InnerGrid.GroupStyle.Clear();
        if (!string.IsNullOrEmpty(path))
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(path));
            InnerGrid.GroupStyle.Add((GroupStyle)FindResource("PowerGridGroupStyle"));
        }
    }

    private string? CurrentGroupPath => CurrentView?.GroupDescriptions.OfType<PropertyGroupDescription>()
        .FirstOrDefault()?.PropertyName;

    private void SelectGroupInCombo(string? path)
    {
        RefreshGroupOptions();
        _applyingView = true;
        GroupCombo.SelectedItem = (GroupCombo.ItemsSource as List<GroupOption>)?
            .FirstOrDefault(o => o.Path == path) ?? (GroupCombo.ItemsSource as List<GroupOption>)?[0];
        _applyingView = false;
    }

    #endregion

    #region Spaltenauswahl

    private void ColumnsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ColumnsList.Items.Clear();
        foreach (var col in Columns)
        {
            var header = col.Header?.ToString();
            if (string.IsNullOrEmpty(header)) header = $"({L.Current["Grid.UnnamedColumn"]} {Columns.IndexOf(col) + 1})";
            var cb = new CheckBox
            {
                Content = header,
                IsChecked = col.Visibility == Visibility.Visible,
                Margin = new Thickness(0, 2, 0, 2),
            };
            var captured = col;
            cb.Checked += (_, _) => captured.Visibility = Visibility.Visible;
            cb.Unchecked += (_, _) => captured.Visibility = Visibility.Collapsed;
            ColumnsList.Items.Add(cb);
        }
        ColumnsPopup.PlacementTarget = (UIElement)sender;
        ColumnsPopup.IsOpen = true;
    }

    #endregion

    #region Ansichten (speichern / laden / zurücksetzen)

    private sealed record ViewItem(string Display, string? Name)
    {
        public override string ToString() => Display;
    }

    private void RefreshViewList(string? select = null)
    {
        var items = new List<ViewItem> { new(L.Current["Grid.DefaultView"], null) };
        if (_viewService != null && GridId is { } id)
            items.AddRange(_viewService.For(id).Views.Select(v => new ViewItem(v.Name, v.Name)));

        _applyingView = true;
        ViewCombo.ItemsSource = items;
        ViewCombo.SelectedItem = items.FirstOrDefault(i => i.Name == select) ?? items[0];
        _applyingView = false;
        DeleteViewButton.IsEnabled = (ViewCombo.SelectedItem as ViewItem)?.Name != null;
    }

    private void RestoreLastActiveView()
    {
        if (_viewService == null || GridId is not { } id) return;
        var entry = _viewService.For(id);
        var view = entry.Views.FirstOrDefault(v => v.Name == entry.LastActive);
        if (view == null) return;
        ApplyState(view);
        RefreshViewList(view.Name);
    }

    private void ViewCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingView) return;
        var item = ViewCombo.SelectedItem as ViewItem;
        DeleteViewButton.IsEnabled = item?.Name != null;
        if (_viewService == null || GridId is not { } id) { ApplyIfDefault(item); return; }

        if (item?.Name is null)
        {
            if (_defaultState != null) ApplyState(_defaultState);
            _viewService.SetLastActive(id, null);
            return;
        }

        var view = _viewService.For(id).Views.FirstOrDefault(v => v.Name == item.Name);
        if (view == null) return;
        ApplyState(view);
        _viewService.SetLastActive(id, view.Name);
    }

    private void ApplyIfDefault(ViewItem? item)
    {
        if (item?.Name is null && _defaultState != null)
            ApplyState(_defaultState);
    }

    private void SaveViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewService == null || GridId is null) return;
        ViewNameBox.Text = (ViewCombo.SelectedItem as ViewItem)?.Name ?? "";
        SaveViewPopup.IsOpen = true;
        ViewNameBox.Focus();
    }

    private void ViewNameBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            ConfirmSaveView_OnClick(sender, e);
    }

    private void ConfirmSaveView_OnClick(object sender, RoutedEventArgs e)
    {
        var name = ViewNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name) || _viewService == null || GridId is not { } id) return;
        SaveViewPopup.IsOpen = false;
        var state = CaptureState(name!);
        _viewService.Save(id, state);
        RefreshViewList(name);
    }

    private void DeleteViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewService == null || GridId is not { } id) return;
        if ((ViewCombo.SelectedItem as ViewItem)?.Name is not { } name) return;
        _viewService.Delete(id, name);
        RefreshViewList();
        if (_defaultState != null) ApplyState(_defaultState);
    }

    private GridViewState CaptureState(string name)
    {
        var state = new GridViewState
        {
            Name = name,
            GroupBy = CurrentGroupPath,
        };
        var sort = CurrentView?.SortDescriptions.FirstOrDefault();
        if (sort is { PropertyName.Length: > 0 } s)
        {
            state.SortBy = s.PropertyName;
            state.SortDirection = (int)s.Direction;
        }
        for (var i = 0; i < Columns.Count; i++)
        {
            var c = Columns[i];
            state.Columns.Add(new GridColumnState
            {
                Index = i,
                Visible = c.Visibility == Visibility.Visible,
                DisplayIndex = c.DisplayIndex,
                Width = c.Width.IsStar ? c.Width.Value : c.Width.IsAuto ? 0 : c.Width.DisplayValue,
                WidthUnit = (int)c.Width.UnitType,
            });
        }
        return state;
    }

    private void ApplyState(GridViewState state)
    {
        _applyingView = true;
        try
        {
            // Spalten: Sichtbarkeit + Breite, dann DisplayIndex in Zielreihenfolge
            foreach (var cs in state.Columns.Where(c => c.Index >= 0 && c.Index < Columns.Count))
            {
                var col = Columns[cs.Index];
                col.Visibility = cs.Visible ? Visibility.Visible : Visibility.Collapsed;
                var unit = (DataGridLengthUnitType)cs.WidthUnit;
                col.Width = unit == DataGridLengthUnitType.Auto
                    ? DataGridLength.Auto
                    : new DataGridLength(Math.Max(cs.Width, unit == DataGridLengthUnitType.Star ? 0.1 : 20), unit);
            }
            foreach (var cs in state.Columns
                         .Where(c => c.Index >= 0 && c.Index < Columns.Count)
                         .OrderBy(c => c.DisplayIndex))
            {
                var target = Math.Clamp(cs.DisplayIndex, 0, Columns.Count - 1);
                Columns[cs.Index].DisplayIndex = target;
            }

            // Sortierung
            var view = CurrentView;
            if (view != null)
            {
                view.SortDescriptions.Clear();
                foreach (var c in Columns) c.SortDirection = null;
                if (state.SortBy is { Length: > 0 } sortBy && state.SortDirection >= 0)
                {
                    var dir = (ListSortDirection)state.SortDirection;
                    view.SortDescriptions.Add(new SortDescription(sortBy, dir));
                    var col = Columns.FirstOrDefault(c => PathOf(c) == sortBy);
                    if (col != null) col.SortDirection = dir;
                }
            }

            // Gruppierung
            ApplyGrouping(state.GroupBy);
            SelectGroupInCombo(state.GroupBy);
        }
        finally
        {
            _applyingView = false;
        }
    }

    #endregion
}
