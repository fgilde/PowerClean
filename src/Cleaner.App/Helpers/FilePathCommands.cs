using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.Helpers;

/// <summary>
/// App-weit verfügbare statische ICommands für Datei/Ordner-Aktionen.
/// CommandParameter ist immer ein Pfad-String — funktioniert unabhängig vom ViewModel,
/// damit das zentrale FileSystemPathMenu in jeder Page eingebunden werden kann.
/// </summary>
public static class FilePathCommands
{
    public static ICommand RevealInExplorer { get; } =
        new RelayCommand<string>(p => { if (!string.IsNullOrWhiteSpace(p)) PathOpener.RevealInExplorer(p); });

    public static ICommand OpenDefault { get; } =
        new RelayCommand<string>(p => { if (!string.IsNullOrWhiteSpace(p)) PathOpener.OpenDefault(p!); });

    public static ICommand OpenWithDialog { get; } =
        new RelayCommand<string>(p => { if (!string.IsNullOrWhiteSpace(p)) PathOpener.OpenWithDialog(p!); });

    public static ICommand OpenInTerminal { get; } =
        new RelayCommand<string>(p =>
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try
            {
                var dir = System.IO.Directory.Exists(p) ? p : System.IO.Path.GetDirectoryName(p);
                if (string.IsNullOrEmpty(dir)) return;
                foreach (var shell in new[] { "wt.exe", "powershell.exe", "cmd.exe" })
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(shell)
                        {
                            UseShellExecute = true,
                            WorkingDirectory = dir,
                        });
                        return;
                    }
                    catch { /* try next */ }
                }
            }
            catch (Exception ex) { App.LogException("FilePathCommands.OpenInTerminal", ex); }
        });

    public static ICommand CopyPath { get; } =
        new RelayCommand<string>(p => { if (!string.IsNullOrWhiteSpace(p)) PathOpener.CopyToClipboard(p!); });

    public static ICommand ShowProperties { get; } =
        new RelayCommand<string>(p => { if (!string.IsNullOrWhiteSpace(p)) PathOpener.ShowProperties(p!); });

    public static ICommand OpenSystemContextMenu { get; } =
        new RelayCommand<string>(p =>
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            var owner = Application.Current?.MainWindow;
            if (owner is null) return;
            ShellContextMenu.ShowFor(owner, p!, extendedVerbs: false);
        });
}
