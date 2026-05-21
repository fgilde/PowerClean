using System.Windows;
using System.Windows.Media;
using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.App.Views.Pages;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Cleaner.App;

public partial class MainWindow : FluentWindow
{
    private readonly AppSettings _settings;

    public MainWindow(MainWindowViewModel viewModel, AppSettings settings, IServiceProvider services,
        RunningTaskRegistry taskRegistry)
    {
        _settings = settings;
        DataContext = viewModel;
        InitializeComponent();

        RootNavigation.SetServiceProvider(services);
        TasksHost.DataContext = taskRegistry;

        Loaded += (_, _) => RootNavigation.Navigate(typeof(DashboardPage));
    }

    private void TasksTriggerButton_Click(object sender, RoutedEventArgs e)
    {
        TasksPopup.IsOpen = !TasksPopup.IsOpen;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var targetTheme = _settings.UseDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light;
        var oppositeTheme = _settings.UseDarkTheme ? ApplicationTheme.Light : ApplicationTheme.Dark;

        // Brute-force toggle: bypasst WPF-UI's "schon gleicher Theme → skip"-Logik
        ApplicationThemeManager.Apply(oppositeTheme, WindowBackdropType.None, updateAccent: false);
        ApplicationThemeManager.Apply(targetTheme, WindowBackdropType.Mica, updateAccent: true);

        // Windows-Akzentfarbe direkt aus der Registry lesen und als App-weiten Brush hinterlegen.
        // WPF-UI's eingebauter SystemAccentColor* sind tinted Varianten — der USER will aber
        // 1:1 die Farbe sehen, die in seinen Windows-Einstellungen steht.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                // Format: 0xAABBGGRR (Alpha-Blue-Green-Red)
                byte r = (byte)(abgr & 0xFF);
                byte g = (byte)((abgr >> 8) & 0xFF);
                byte b = (byte)((abgr >> 16) & 0xFF);
                var color = Color.FromRgb(r, g, b);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                Application.Current.Resources["CleanerAccentBrush"] = brush;
                Application.Current.Resources["CleanerAccentColor"] = color;
                App.LogInfo($"Accent-Color aus Registry: #{r:X2}{g:X2}{b:X2}");
            }
        }
        catch (Exception ex)
        {
            App.LogException("ReadAccentColor", ex);
        }
    }
}
