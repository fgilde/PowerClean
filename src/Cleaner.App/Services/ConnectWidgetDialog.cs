using System.IO;
using System.Windows;
using System.Windows.Media;
using Cleaner.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Cleaner.App.Services;

/// <summary>
/// Dialog mit eingebettetem Browser (WebView2) für die gilde-Connect-Widgets
/// (Kontakt / Unterstützen). Accent-Farbe, Theme und Sprache folgen der App.
/// Fällt auf die Website-Kontakt-Sektion im Standard-Browser zurück, wenn keine
/// WebView2-Runtime installiert ist.
/// </summary>
public static class ConnectWidgetDialog
{
    private const string FallbackUrl = "https://fgilde.github.io/PowerClean/#connect";

    public static async void Show(string widget, string title)
    {
        var settings = App.Services?.GetService<AppSettings>();
        var dark = settings?.UseDarkTheme != false;
        var language = string.IsNullOrWhiteSpace(settings?.Language) ? "auto" : settings!.Language;
        var accent = Application.Current?.Resources["CleanerAccentColor"] is Color c
            ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            : "#3EC5FF";

        var html = BuildHtml(widget, dark, language, accent);

        var webView = new WebView2();
        var window = new Window
        {
            Title = title,
            Width = 660,
            Height = 800,
            MinWidth = 480,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            Background = new SolidColorBrush(dark ? Color.FromRgb(0x1B, 0x1B, 0x1F) : Colors.White),
            Content = webView,
        };

        try
        {
            window.Show();
            // Eigener UserData-Ordner — neben der EXE (Program Files) wäre er nicht beschreibbar.
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PowerClean", "webview2"));
            await webView.EnsureCoreWebView2Async(env);
            webView.DefaultBackgroundColor = dark
                ? System.Drawing.Color.FromArgb(0x1B, 0x1B, 0x1F)
                : System.Drawing.Color.White;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            // Externe Links (Homepage, Support-Anbieter) im Standard-Browser öffnen.
            webView.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                OpenExternal(e.Uri);
            };
            webView.NavigateToString(html);
        }
        catch (Exception ex)
        {
            App.LogException("ConnectWidget", ex);
            try { window.Close(); } catch { }
            // Keine WebView2-Runtime o.ä. — Website-Kontakt im Browser öffnen.
            OpenExternal(FallbackUrl);
        }
    }

    private static string BuildHtml(string widget, bool dark, string language, string accent)
    {
        var theme = dark ? "dark" : "light";
        var bg = dark ? "#1b1b1f" : "#ffffff";
        var hint = language == "en" ? "You will be redirected to the provider." : "Du wechselst zum jeweiligen Anbieter.";
        var contactTitle = language == "en" ? "Contact PowerClean" : "Kontakt PowerClean";
        var supportExtras = widget == "support"
            ? $" show-support-hint=\"false\" support-hint-text=\"{hint}\" support-layout=\"rows\" show-support-icons=\"true\" show-support-qr=\"true\""
            : $" title=\"{contactTitle}\"";

        // Inline-Widget: show-footer/description immer false, Homepage-Link erlaubt (App ist
        // nicht die Website). Accent/Theme/Sprache kommen live aus der App.
        return $$"""
            <!doctype html>
            <html>
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <style>
                    html, body { margin: 0; background: {{bg}}; }
                    body { display: flex; justify-content: center; padding: 18px 12px; font-family: 'Segoe UI', sans-serif; }
                    gilde-contact, gilde-support { display: block; width: 100%; max-width: 560px; }
                </style>
            </head>
            <body>
                <gilde-{{widget}} project="fgilde/PowerClean" widget="{{widget}}" inline theme="{{theme}}"
                    accent="{{accent}}" language="{{language}}" width="560" radius="18" padding="28"
                    show-logo="true" show-description="false" show-homepage="true"
                    show-preview-notice="false" show-footer="false"{{supportExtras}}></gilde-{{widget}}>
                <script type="module" src="https://connect.gilde.org/widgets/v1.js"></script>
            </body>
            </html>
            """;
    }

    private static void OpenExternal(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }
}
