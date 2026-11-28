using System.Globalization;
namespace NileShop.Services;

/// <summary>
/// Store time is Africa/Cairo. Timestamps are stored in UTC and converted here for display and day-based logic.
/// The zone is looked up by IANA id, then Windows id (the host may be either), so Egypt's DST is honoured.
/// If neither exists on the host, Egypt's current DST rule is applied (last Friday of April to last Thursday of October, +3).
/// </summary>
public static class CairoTime
{
    static readonly TimeZoneInfo? Zone = Find();
    static TimeZoneInfo? Find()
    {
        foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        return null;
    }
    static DateTime LastWeekday(int year, int month, DayOfWeek day)
    {
        var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        while (d.DayOfWeek != day) d = d.AddDays(-1);
        return d;
    }
    static TimeSpan FallbackOffset(DateTime utc)
    {
        // DST from 00:00 local (22:00 UTC the day before) on the last Friday of April to 24:00 local on the last Thursday of October.
        var start = LastWeekday(utc.Year, 4, DayOfWeek.Friday).AddHours(-2);
        var end = LastWeekday(utc.Year, 10, DayOfWeek.Thursday).AddDays(1).AddHours(-3);
        return utc >= start && utc < end ? TimeSpan.FromHours(3) : TimeSpan.FromHours(2);
    }

    /// <summary>Treats the value as UTC (database values come back with an unspecified kind) and returns Cairo wall-clock time.</summary>
    public static DateTime FromUtc(DateTime utc)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return Zone is null ? DateTime.SpecifyKind(utc + FallbackOffset(utc), DateTimeKind.Unspecified) : DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone), DateTimeKind.Unspecified);
    }
    /// <summary>Converts a Cairo wall-clock time to UTC (used for day boundaries in queries).</summary>
    public static DateTime ToUtc(DateTime cairo)
    {
        cairo = DateTime.SpecifyKind(cairo, DateTimeKind.Unspecified);
        if (Zone is not null) return TimeZoneInfo.ConvertTimeToUtc(cairo, Zone);
        var asDst = cairo - TimeSpan.FromHours(3);
        return FallbackOffset(asDst) == TimeSpan.FromHours(3) ? asDst : cairo - TimeSpan.FromHours(2);
    }
    public static DateTime Now => FromUtc(DateTime.UtcNow);
    public static DateTime Today => Now.Date;

    /// <summary>Formats a stored UTC time in Cairo time. "g" keeps the visitor's culture; other formats are invariant, as before.</summary>
    public static string Fmt(DateTime utc, string format) => FromUtc(utc).ToString(format, format == "g" ? CultureInfo.CurrentCulture : CultureInfo.InvariantCulture);
    public static string? Fmt(DateTime? utc, string format) => utc is null ? null : Fmt(utc.Value, format);
}
