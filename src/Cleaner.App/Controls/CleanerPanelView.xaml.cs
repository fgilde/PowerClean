using System.Windows;
using System.Windows.Controls;
using Cleaner.App.ViewModels.Pages;

namespace Cleaner.App.Controls;

public partial class CleanerPanelView : UserControl
{
    public CleanerPanelView()
    {
        InitializeComponent();
        Loaded += (_, _) => ResolveViewModel();
    }

    public static readonly DependencyProperty PageTitleProperty =
        DependencyProperty.Register(nameof(PageTitle), typeof(string), typeof(CleanerPanelView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PageSubtitleProperty =
        DependencyProperty.Register(nameof(PageSubtitle), typeof(string), typeof(CleanerPanelView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(CleanerPageViewModelBase), typeof(CleanerPanelView), new PropertyMetadata(null));

    public string PageTitle
    {
        get => (string)GetValue(PageTitleProperty);
        set => SetValue(PageTitleProperty, value);
    }

    public string PageSubtitle
    {
        get => (string)GetValue(PageSubtitleProperty);
        set => SetValue(PageSubtitleProperty, value);
    }

    public CleanerPageViewModelBase? ViewModel
    {
        get => (CleanerPageViewModelBase?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Wenn der enthaltende Page einen DataContext mit ViewModel-Property hat,
    /// nehmen wir das automatisch.
    /// </summary>
    private void ResolveViewModel()
    {
        if (ViewModel is not null) return;

        var parentContext = DataContext;
        var vmProp = parentContext?.GetType().GetProperty("ViewModel");
        if (vmProp?.GetValue(parentContext) is CleanerPageViewModelBase vm)
            ViewModel = vm;
    }
}
