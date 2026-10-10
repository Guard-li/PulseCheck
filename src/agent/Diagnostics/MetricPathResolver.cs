using System.Collections;
using System.Reflection;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Diagnostics;

public static class MetricPathResolver
{
    public static object? Resolve(MetricSnapshot snapshot, string path)
    {
        if (path.Equals("disk.system.freePercent", StringComparison.OrdinalIgnoreCase))
        {
            var system = snapshot.Disk.Volumes.FirstOrDefault(x => x.IsSystemVolume);
            return system is null ? null : 100 - system.UsedPercent;
        }
        if (path.Equals("disk.highestTemperatureCelsius", StringComparison.OrdinalIgnoreCase))
            return snapshot.Disk.PhysicalDisks.Where(x => x.TemperatureCelsius is not null).Select(x => x.TemperatureCelsius).DefaultIfEmpty().Max();
        if (path.Equals("disk.lowestRemainingLifePercent", StringComparison.OrdinalIgnoreCase))
        {
            var values = snapshot.Disk.PhysicalDisks.Where(x => x.RemainingLifePercent is not null).Select(x => x.RemainingLifePercent!.Value).ToArray();
            return values.Length == 0 ? null : values.Min();
        }
        return ResolveObject(snapshot, path.Split('.', StringSplitOptions.RemoveEmptyEntries));
    }

    private static object? ResolveObject(object? current, IReadOnlyList<string> segments)
    {
        foreach (var segment in segments)
        {
            if (current is null) return null;
            if (current is IDictionary dictionary)
            {
                current = dictionary.Contains(segment) ? dictionary[segment] : null;
                continue;
            }
            var type = current.GetType();
            var property = type.GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is null) return null;
            current = property.GetValue(current);
        }
        return current;
    }
}