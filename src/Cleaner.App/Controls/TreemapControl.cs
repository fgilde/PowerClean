using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cleaner.Core.Models;

namespace Cleaner.App.Controls;

/// <summary>
/// Custom-Treemap mit Squarified-Layout. Rendert jeden FileSystemNode als Rechteck
/// proportional zu seiner Größe. Klick auf Verzeichnisse drilled rein.
/// </summary>
public sealed class TreemapControl : FrameworkElement
{
    private static readonly Color[] DirectoryColors =
    {
        Color.FromRgb(0x58, 0xA6, 0xFF),
        Color.FromRgb(0x3F, 0xB9, 0x50),
        Color.FromRgb(0xF8, 0x51, 0x49),
        Color.FromRgb(0xD2, 0x99, 0x22),
        Color.FromRgb(0xBC, 0x8C, 0xFF),
        Color.FromRgb(0xFF, 0xA6, 0x57),
        Color.FromRgb(0x39, 0xD3, 0xCB),
        Color.FromRgb(0xFF, 0x7B, 0x72),
    };

    public static readonly DependencyProperty RootNodeProperty =
        DependencyProperty.Register(nameof(RootNode), typeof(FileSystemNode), typeof(TreemapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRootChanged));

    public static readonly DependencyProperty SelectedNodeProperty =
        DependencyProperty.Register(nameof(SelectedNode), typeof(FileSystemNode), typeof(TreemapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public FileSystemNode? RootNode
    {
        get => (FileSystemNode?)GetValue(RootNodeProperty);
        set => SetValue(RootNodeProperty, value);
    }

    public FileSystemNode? SelectedNode
    {
        get => (FileSystemNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public event EventHandler<FileSystemNode>? NodeDoubleClicked;
    public event EventHandler<FileSystemNode>? NodeClicked;
    public event EventHandler<FileSystemNode>? NodeRightClicked;

    private List<(Rect Rect, FileSystemNode Node, int Depth)> _layout = new();

    /// <summary>Forciert ein Re-Render — wird während eines Live-Scans z.B. alle 500ms gerufen.</summary>
    public void Refresh() => InvalidateVisual();

    private static void OnRootChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TreemapControl)d).InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var area = new Rect(0, 0, ActualWidth, ActualHeight);

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)), null, area);

        if (RootNode is null || RootNode.Size == 0) return;

        // Während eines Live-Scans kann der Baum unter uns mutieren. Falls eine Race
        // doch durchschlägt, lieber den Render abbrechen als die App killen.
        _layout.Clear();
        try { Squarify(RootNode.ChildrenSnapshot(), area, 0); }
        catch { return; }

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 0.5);
        pen.Freeze();

        foreach (var (rect, node, depth) in _layout)
        {
            if (rect.Width < 1 || rect.Height < 1) continue;

            var color = ColorFor(node, depth);
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            dc.DrawRectangle(brush, pen, rect);

            // Highlight selection
            if (node == SelectedNode)
            {
                var sel = new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), 2);
                sel.Freeze();
                dc.DrawRectangle(null, sel, rect);
            }

            // Label (nur wenn Rechteck groß genug)
            if (rect.Width > 80 && rect.Height > 24 && !string.IsNullOrEmpty(node.Name))
            {
                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                    FontWeights.SemiBold, FontStretches.Normal);

                var ft = new FormattedText(
                    TruncateForWidth(node.Name, rect.Width),
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface, 11.5, Brushes.White, 1.0);

                ft.MaxTextWidth = rect.Width - 6;
                dc.DrawText(ft, new Point(rect.X + 4, rect.Y + 3));
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        try
        {
            var pos = e.GetPosition(this);
            var hit = HitTestNode(pos);
            if (hit is null) return;

            if (e.ClickCount == 2 && hit.IsDirectory)
                NodeDoubleClicked?.Invoke(this, hit);
            else
                NodeClicked?.Invoke(this, hit);
        }
        catch { /* ignore */ }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        try
        {
            var pos = e.GetPosition(this);
            var hit = HitTestNode(pos);
            if (hit is null) return;
            SelectedNode = hit;
            NodeRightClicked?.Invoke(this, hit);
        }
        catch { /* ignore */ }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        try
        {
            var pos = e.GetPosition(this);
            var hit = HitTestNode(pos);
            ToolTip = hit is null
                ? null
                : $"{hit.FullPath}\n{Cleaner.Core.Utils.ByteFormatter.Format(hit.Size)} ({hit.FileCount} Dateien)";
        }
        catch { /* ignore */ }
    }

    private FileSystemNode? HitTestNode(Point p)
    {
        // Topmost rectangle (later in _layout = innerer Knoten)
        for (int i = _layout.Count - 1; i >= 0; i--)
        {
            if (_layout[i].Rect.Contains(p))
                return _layout[i].Node;
        }
        return null;
    }

    private static string TruncateForWidth(string name, double width)
    {
        // 1 char ≈ 7px für Segoe UI 11.5
        int max = Math.Max(3, (int)(width / 7) - 2);
        return name.Length > max ? name[..(max - 1)] + "…" : name;
    }

    private static Color ColorFor(FileSystemNode node, int depth)
    {
        if (!node.IsDirectory)
        {
            // Datei: heller / desaturierter Ton basierend auf Endung
            var ext = Path.GetExtension(node.Name).ToLowerInvariant();
            return ext switch
            {
                ".exe" or ".dll" or ".pdb" => Color.FromRgb(0x6F, 0x6F, 0x6F),
                ".log" or ".txt" or ".md"  => Color.FromRgb(0x88, 0x88, 0x88),
                ".jpg" or ".png" or ".gif" or ".webp" => Color.FromRgb(0xFF, 0xA6, 0x57),
                ".mp4" or ".mkv" or ".avi" => Color.FromRgb(0xBC, 0x8C, 0xFF),
                ".zip" or ".7z" or ".rar"  => Color.FromRgb(0xD2, 0x99, 0x22),
                ".dmp"                     => Color.FromRgb(0xF8, 0x51, 0x49),
                ".iso" or ".vhdx"          => Color.FromRgb(0xBC, 0x8C, 0xFF),
                _ => Color.FromRgb(0xA0, 0xA0, 0xA0),
            };
        }

        var baseColor = DirectoryColors[Math.Abs(node.Name.GetHashCode()) % DirectoryColors.Length];
        // mit zunehmender Tiefe heller
        byte mix = (byte)Math.Min(255, 40 + depth * 18);
        return Color.FromRgb(
            (byte)Math.Min(255, baseColor.R + mix / 2),
            (byte)Math.Min(255, baseColor.G + mix / 2),
            (byte)Math.Min(255, baseColor.B + mix / 2));
    }

    // ---- Squarified treemap algorithm (Bruls, Huijsen, van Wijk 1999) ----

    // Maximum visible Treemap-Kinder pro Ordner. Bei C:\ können das hunderttausende werden —
    // ein Squarify mit N=100k frisst Stack und CPU. Top-200 reichen für visuelle Information.
    private const int MaxTreemapChildren = 200;

    private void Squarify(IReadOnlyList<FileSystemNode> nodes, Rect area, int depth)
    {
        if (area.Width <= 1 || area.Height <= 1 || nodes.Count == 0) return;

        // Top-N nach Größe — Rest ist visuell sowieso nicht sichtbar.
        var sorted = nodes
            .Where(n => n.Size > 0)
            .OrderByDescending(n => n.Size)
            .Take(MaxTreemapChildren)
            .ToList();
        if (sorted.Count == 0) return;

        long totalSize = 0;
        foreach (var n in sorted) totalSize += n.Size;
        if (totalSize == 0) return;

        double areaPerByte = (area.Width * area.Height) / totalSize;
        var sizes = new double[sorted.Count];
        for (int i = 0; i < sorted.Count; i++) sizes[i] = sorted[i].Size * areaPerByte;

        SquarifyIterative(sizes, sorted, area, depth);
    }

    // ITERATIV — vorherige Implementierung rekursierte O(N)-tief und sprengte den Stack
    // bei großen Ordnern (z.B. C:\Windows\WinSxS mit > 100k Dateien).
    private void SquarifyIterative(double[] sizes, List<FileSystemNode> nodes, Rect rect, int depth)
    {
        var row = new List<double>();
        var rowNodes = new List<FileSystemNode>();
        var current = rect;

        for (int i = 0; i < sizes.Length; i++)
        {
            double c = sizes[i];
            double shortSide = ShortSide(current);
            double currentWorst = WorstOf(SumOf(row), row.Count == 0 ? 0 : MinOf(row), row.Count == 0 ? 0 : MaxOf(row), shortSide);
            // newWorst kostenlos berechnen ohne Liste zu kopieren
            double newSum = SumOf(row) + c;
            double newMin = row.Count == 0 ? c : Math.Min(MinOf(row), c);
            double newMax = row.Count == 0 ? c : Math.Max(MaxOf(row), c);
            double newWorst = WorstOf(newSum, newMin, newMax, shortSide);

            if (row.Count == 0 || currentWorst >= newWorst)
            {
                row.Add(c);
                rowNodes.Add(nodes[i]);
            }
            else
            {
                LayoutRow(row, rowNodes, current, depth, out current);
                row.Clear();
                rowNodes.Clear();
                row.Add(c);
                rowNodes.Add(nodes[i]);
            }
        }

        if (row.Count > 0)
            LayoutRow(row, rowNodes, current, depth, out _);
    }

    private static double SumOf(List<double> row) { double s = 0; foreach (var v in row) s += v; return s; }
    private static double MinOf(List<double> row) { double m = double.PositiveInfinity; foreach (var v in row) if (v < m) m = v; return m; }
    private static double MaxOf(List<double> row) { double m = double.NegativeInfinity; foreach (var v in row) if (v > m) m = v; return m; }
    private static double WorstOf(double sum, double min, double max, double width)
    {
        if (sum <= 0 || min <= 0 || width <= 0) return double.PositiveInfinity;
        double w2 = width * width;
        double s2 = sum * sum;
        return Math.Max((w2 * max) / s2, s2 / (w2 * min));
    }

    private static double Worst(List<double> row, double width)
    {
        if (row.Count == 0) return double.PositiveInfinity;
        double sum = row.Sum();
        double w2 = width * width;
        double s2 = sum * sum;
        double max = row.Max();
        double min = row.Min();
        return Math.Max((w2 * max) / s2, s2 / (w2 * min));
    }

    private static double ShortSide(Rect r) => Math.Min(r.Width, r.Height);

    private void LayoutRow(
        List<double> row,
        List<FileSystemNode> nodes,
        Rect rect,
        int depth,
        out Rect rest)
    {
        double sum = row.Sum();
        bool horizontal = rect.Width >= rect.Height;
        double rowSize = horizontal ? sum / rect.Height : sum / rect.Width;

        double x = rect.X, y = rect.Y;
        for (int i = 0; i < row.Count; i++)
        {
            double w = horizontal ? rowSize : row[i] / rowSize;
            double h = horizontal ? row[i] / rowSize : rowSize;
            var nodeRect = new Rect(x, y, w, h);
            _layout.Add((nodeRect, nodes[i], depth));

            // bei Ordnern noch eine Ebene tiefer rendern (mit innerem Padding)
            if (nodes[i].IsDirectory && depth < 4 && w > 30 && h > 30)
            {
                var childSnapshot = nodes[i].ChildrenSnapshot();
                if (childSnapshot.Length > 0)
                {
                    var pad = depth == 0 ? 12 : 4;
                    var inner = new Rect(nodeRect.X + pad, nodeRect.Y + pad + 14,
                        Math.Max(0, nodeRect.Width - 2 * pad),
                        Math.Max(0, nodeRect.Height - 2 * pad - 14));
                    if (inner.Width > 4 && inner.Height > 4)
                        Squarify(childSnapshot, inner, depth + 1);
                }
            }

            if (horizontal) y += h; else x += w;
        }

        rest = horizontal
            ? new Rect(rect.X + rowSize, rect.Y, Math.Max(0, rect.Width - rowSize), rect.Height)
            : new Rect(rect.X, rect.Y + rowSize, rect.Width, Math.Max(0, rect.Height - rowSize));
    }
}
