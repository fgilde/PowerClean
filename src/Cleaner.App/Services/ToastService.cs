using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Cleaner.App.Services;

/// <summary>
/// Kleine Topmost-Benachrichtigung unten rechts — sichtbar auch wenn PowerClean minimiert ist.
/// Generisch für alle Hotkey-/Hintergrund-Aktionen: <c>toast.Show("Titel", "Text")</c>.
/// Ein neuer Toast ersetzt den sichtbaren (kein Stacking) und startet die Anzeigezeit neu.
/// </summary>
public sealed class ToastService
{
    private ToastWindow? _window;

    public void Show(string title, string message, TimeSpan? duration = null)
    {
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _window ??= new ToastWindow();
                _window.ShowToast(title, message, duration ?? TimeSpan.FromSeconds(3.5));
            }
            catch (Exception ex) { App.LogException("Toast", ex); }
        });
    }
}

internal sealed class ToastWindow : Window
{
    private readonly TextBlock _title;
    private readonly TextBlock _message;
    private readonly DispatcherTimer _hideTimer;

    public ToastWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 420;

        _title = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        };
        _message = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var panel = new StackPanel { Children = { _title, _message } };
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x2B, 0x2B, 0x2B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Child = panel,
        };

        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) => FadeOut();

        // Klick schließt sofort.
        MouseDown += (_, _) => { _hideTimer.Stop(); Hide(); };
        SizeChanged += (_, _) => Reposition();
    }

    public void ShowToast(string title, string message, TimeSpan duration)
    {
        BeginAnimation(OpacityProperty, null); // laufenden Fade abbrechen
        Opacity = 1;
        _title.Text = title;
        _message.Text = message;
        _message.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;

        if (!IsVisible) Show();
        Reposition();

        _hideTimer.Stop();
        _hideTimer.Interval = duration;
        _hideTimer.Start();
    }

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Bottom - ActualHeight - 16;
    }

    private void FadeOut()
    {
        _hideTimer.Stop();
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        anim.Completed += (_, _) => { Hide(); BeginAnimation(OpacityProperty, null); Opacity = 1; };
        BeginAnimation(OpacityProperty, anim);
    }
}
