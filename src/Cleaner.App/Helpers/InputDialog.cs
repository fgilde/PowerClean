using System.Windows;
using System.Windows.Controls;

namespace Cleaner.App.Helpers;

/// <summary>Minimaler modaler Text-Eingabedialog (WPF kennt von Haus aus keine InputBox).</summary>
public static class InputDialog
{
    /// <summary>Zeigt den Dialog. Liefert den Text oder null bei Abbruch.</summary>
    public static string? Show(string title, string prompt, string initial = "")
    {
        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Owner = Application.Current?.MainWindow,
        };

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        });

        var box = new TextBox { Text = initial };
        box.SelectAll();
        root.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        string? result = null;
        var ok = new Button { Content = "OK", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Abbrechen", Width = 90, IsCancel = true };
        ok.Click += (_, _) => { result = box.Text; window.DialogResult = true; };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        window.Content = root;
        box.Focus();

        return window.ShowDialog() == true ? result : null;
    }
}
