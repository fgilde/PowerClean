using System.Runtime.InteropServices;
using System.Text;

namespace Cleaner.App.Helpers;

public static class WindowsUserHelper
{
    [DllImport("secur32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetUserNameEx(int format, StringBuilder name, ref int size);

    private const int NameDisplay = 3;       // Vor- + Nachname
    private const int NameSamCompatible = 2; // DOMAIN\user

    /// <summary>
    /// Liefert den Anzeigenamen des aktuellen Windows-Users (Vor- + Nachname falls verfügbar)
    /// oder fällt auf Environment.UserName zurück.
    /// </summary>
    public static string GetDisplayName()
    {
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (GetUserNameEx(NameDisplay, sb, ref size) != 0)
            {
                var name = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch { /* ignore */ }
        return Environment.UserName;
    }

    /// <summary>Vorname (erstes Wort des Display-Names).</summary>
    public static string GetFirstName()
    {
        var name = GetDisplayName();
        var idx = name.IndexOf(' ');
        return idx > 0 ? name[..idx] : name;
    }
}
