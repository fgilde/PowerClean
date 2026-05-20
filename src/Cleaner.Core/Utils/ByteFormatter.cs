using System.Globalization;

namespace Cleaner.Core.Utils;

public static class ByteFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes < 0) return "-" + Format(-bytes);
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        string fmt = unit == 0 ? "0" : value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
        return value.ToString(fmt, CultureInfo.InvariantCulture) + " " + Units[unit];
    }
}
