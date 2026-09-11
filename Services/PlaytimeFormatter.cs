using System.Globalization;

namespace Finestats.Services;

public static class PlaytimeFormatter
{
    public static string Format(double? seconds)
    {
        if (seconds is not double value || !double.IsFinite(value) || value < 0 || value > TimeSpan.MaxValue.TotalSeconds)
            return "—";
        long minutes = (long)Math.Floor(value / 60);
        var days = minutes / 1440;
        var hours = minutes / 60 % 24;
        minutes %= 60;
        var parts = new List<string>();
        void Add(long count, string unit) => parts.Add(count.ToString(CultureInfo.InvariantCulture) + " " + unit + (count == 1 ? "" : "s"));
        if (days > 0)
            Add(days, "day");
        if (hours > 0)
            Add(hours, "hour");
        if (minutes > 0 || parts.Count == 0)
            Add(minutes, "minute");
        return string.Join(" ", parts);
    }
}
