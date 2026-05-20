using System.Windows;
using System.Windows.Controls;
using Cleaner.App.ViewModels.Pages;
using Cleaner.Core.Models;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class DiskAnalyzerPage : INavigableView<DiskAnalyzerViewModel>
{
    public DiskAnalyzerViewModel ViewModel { get; }

    public DiskAnalyzerPage(DiskAnalyzerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        Treemap.NodeDoubleClicked += (_, node) => ViewModel.DrillIntoCommand.Execute(node);
        Treemap.NodeClicked += (_, node) => Treemap.SelectedNode = node;
        Treemap.NodeRightClicked += OnTreemapRightClick;

        ViewModel.TreeUpdated += (_, _) => Treemap.Refresh();
    }

    private void OnTreemapRightClick(object? sender, FileSystemNode node)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        if (node.IsDirectory)
        {
            menu.Items.Add(MakeMenuItem("Reinzoomen", "ZoomIn24",
                () => ViewModel.DrillIntoCommand.Execute(node)));
            menu.Items.Add(new System.Windows.Controls.Separator());
        }
        menu.Items.Add(MakeMenuItem("Im Explorer öffnen", "Folder24",
            () => ViewModel.OpenInExplorerCommand.Execute(node)));
        if (!node.IsDirectory)
        {
            menu.Items.Add(MakeMenuItem("Öffnen mit Standard-App", "Open24",
                () => ViewModel.OpenWithCommand.Execute(node)));
            menu.Items.Add(MakeMenuItem("Öffnen mit...", "AppsListDetail24",
                () => ViewModel.OpenWithDialogCommand.Execute(node)));
        }
        menu.Items.Add(MakeMenuItem("Im Terminal öffnen", "WindowConsole20",
            () => ViewModel.OpenInTerminalCommand.Execute(node)));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(MakeMenuItem("Pfad kopieren", "Copy24",
            () => ViewModel.CopyPathCommand.Execute(node)));
        menu.Items.Add(MakeMenuItem("Windows-Eigenschaften...", "Info24",
            () => ViewModel.ShowPropertiesCommand.Execute(node)));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(MakeMenuItem("Löschen", "Delete24",
            () => ViewModel.DeleteNodeCommand.Execute(node), danger: true));

        menu.PlacementTarget = Treemap;
        menu.IsOpen = true;
    }

    private static System.Windows.Controls.MenuItem MakeMenuItem(string header, string symbol, Action onClick,
        bool danger = false, string? tooltip = null)
    {
        var mi = new System.Windows.Controls.MenuItem { Header = header };
        if (tooltip is not null) mi.ToolTip = tooltip;
        var icon = new SymbolIcon { Symbol = Enum.Parse<SymbolRegular>(symbol) };
        if (danger)
            icon.Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("WarningBrush");
        mi.Icon = icon;
        mi.Click += (_, _) => onClick();
        return mi;
    }
}
