using Cleaner.App.ViewModels.Pages;
using Nextended.UI.WPF.Controls;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class KillProfilesPage : INavigableView<KillProfilesViewModel>
{
    public KillProfilesViewModel ViewModel { get; }

    public KillProfilesPage(KillProfilesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    // MinTime-Änderungen mutieren dieselbe Binding-Instanz — das TwoWay-Binding feuert dann
    // nicht. Deshalb hier immer explizit persistieren.
    private void Shortcut_OnKeyBindChanged(object? sender, KeyBindChangedEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement { DataContext: KillProfileItemViewModel item } changer)
            item.PersistShortcut(((KeyBindChanger)changer).KeyBind);
    }
}
