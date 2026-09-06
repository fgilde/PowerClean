using Cleaner.App.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.UseDarkTheme))
                ApplyTheme();
        };
    }

    public AppSettings Settings => _settings;

    [RelayCommand]
    public void ApplyTheme()
    {
        ApplicationThemeManager.Apply(_settings.UseDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }
}
