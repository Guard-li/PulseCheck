using System.Globalization;

namespace PulseCheck.Agent.Scheduling;

public sealed class CronExpression
{
    private readonly HashSet<int> _minutes;
    private readonly HashSet<int> _hours;
    private readonly HashSet<int> _days;
    private readonly HashSet<int> _months;
    private readonly HashSet<int> _weekdays;

    public CronExpression(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5) throw new FormatException("Cron 表达式必须包含 5 段：分 时 日 月 周");
        _minutes = ParseField(fields[0], 0, 59);
        _hours = ParseField(fields[1], 0, 23);
        _days = ParseField(fields[2], 1, 31);
        _months = ParseField(fields[3], 1, 12);
        _weekdays = ParseField(fields[4], 0, 7, normalizeSunday: true);
        Expression = expression;
    }

    public string Expression { get; }

    public DateTimeOffset NextOccurrence(DateTimeOffset afterUtc, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(afterUtc, zone).DateTime;
        local = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified).AddMinutes(1);
        for (var attempt = 0; attempt < 60 * 24 * 366 * 5; attempt++)
        {
            if (_months.Contains(local.Month) && _days.Contains(local.Day) && _weekdays.Contains((int)local.DayOfWeek) && _hours.Contains(local.Hour) && _minutes.Contains(local.Minute))
            {
                var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
                if (zone.IsInvalidTime(unspecified)) { local = local.AddMinutes(1); continue; }
                try
                {
                    var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
                    return new DateTimeOffset(utc, TimeSpan.Zero);
                }
                catch (ArgumentException)
                {
                    local = local.AddMinutes(1);
                    continue;
                }
            }
            local = local.AddMinutes(1);
        }
        throw new InvalidOperationException("在可接受范围内找不到下一次 Cron 执行时间");
    }

    private static HashSet<int> ParseField(string field, int minimum, int maximum, bool normalizeSunday = false)
    {
        var values = new HashSet<int>();
        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("*/", StringComparison.Ordinal))
            {
                if (!int.TryParse(part[2..], out var step) || step <= 0) throw new FormatException($"无效 Cron 字段：{field}");
                for (var value = minimum; value <= maximum; value += step) values.Add(value);
                continue;
            }
            if (part == "*")
            {
                for (var value = minimum; value <= maximum; value++) values.Add(value);
                continue;
            }
            var rangeParts = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rangeParts.Length > 2 || !int.TryParse(rangeParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start)) throw new FormatException($"无效 Cron 字段：{field}");
            var end = rangeParts.Length == 2 && int.TryParse(rangeParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedEnd) ? parsedEnd : start;
            if (start < minimum || end > maximum || start > end) throw new FormatException($"Cron 值超出范围：{field}");
            for (var value = start; value <= end; value++) values.Add(value);
        }
        if (normalizeSunday && values.Remove(7)) values.Add(0);
        return values;
    }
}