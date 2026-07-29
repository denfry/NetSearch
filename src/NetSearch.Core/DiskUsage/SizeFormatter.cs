using System.Globalization;

namespace NetSearch.Core.DiskUsage;

public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1) { value /= 1024; unit++; }
        // Bytes are whole; KB and above show one decimal.
        return unit == 0
            ? $"{(long)value} {Units[unit]}"
            : value.ToString("0.0", CultureInfo.InvariantCulture) + " " + Units[unit];
    }
}
