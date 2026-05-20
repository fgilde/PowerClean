using System.Windows;
using Velopack;

namespace Cleaner.App;

/// <summary>
/// Eigener Entry-Point — wird gebraucht damit Velopack vor allem anderen laufen kann
/// (sonst werden Install-/Uninstall-/Update-Hooks beim Launch über die Squirrel-Args nicht abgefangen).
/// In csproj via <c>&lt;StartupObject&gt;</c> registriert.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack-Hooks ZUERST: bei --veloapp-install / --uninstall / --restarted exit Velopack selbst.
        try
        {
            VelopackApp.Build().Run();
        }
        catch (Exception ex)
        {
            // Velopack-Fehler dürfen den normalen App-Start nicht killen
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "powerclean.log"),
                    $"[{DateTimeOffset.UtcNow:O}] [Velopack] {ex}\n\n");
            }
            catch { }
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
