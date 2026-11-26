using System.Management;
using System.Runtime.InteropServices;
using SellSpec.Core.Diagnostics;

namespace SellSpec.Windows.Wmi;

/// <summary>A WMI result row copied into plain CLR values so the COM objects can be released immediately.</summary>
public sealed class WmiRow
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    internal WmiRow(ManagementBaseObject source)
    {
        foreach (var property in source.Properties)
        {
            _values[property.Name] = Detach(property.Value);
        }
    }

    internal WmiRow(Dictionary<string, object?> values)
    {
        _values = values;
    }

    private static object? Detach(object? value) => value switch
    {
        null => null,
        ManagementBaseObject mbo => new WmiRow(mbo),
        ManagementBaseObject[] array => array.Select(a => new WmiRow(a)).ToArray(),
        _ => value,
    };

    public bool Has(string name) => _values.TryGetValue(name, out var v) && v is not null;

    public object? Raw(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public string? GetString(string name)
    {
        var v = Raw(name);
        if (v is null) return null;
        var s = v is string str ? str : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    public ulong? GetUInt64(string name)
    {
        var v = Raw(name);
        if (v is null) return null;
        try
        {
            return v switch
            {
                ulong u => u,
                uint u32 => u32,
                ushort u16 => u16,
                byte b => b,
                long l when l >= 0 => (ulong)l,
                int i when i >= 0 => (ulong)i,
                string s when ulong.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => Convert.ToUInt64(v, System.Globalization.CultureInfo.InvariantCulture),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    public long? GetInt64(string name)
    {
        var v = Raw(name);
        if (v is null) return null;
        try
        {
            return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    public int? GetInt32(string name)
    {
        var v = GetInt64(name);
        return v is null or > int.MaxValue or < int.MinValue ? null : (int)v.Value;
    }

    public uint? GetUInt32(string name)
    {
        var v = GetInt64(name);
        return v is null or > uint.MaxValue or < 0 ? null : (uint)v.Value;
    }

    public bool? GetBool(string name)
    {
        var v = Raw(name);
        return v switch
        {
            null => null,
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    public int[]? GetInt32Array(string name)
    {
        var v = Raw(name);
        return v switch
        {
            null => null,
            int[] ints => ints,
            ushort[] shorts => shorts.Select(x => (int)x).ToArray(),
            uint[] uints => uints.Select(x => (int)Math.Min(x, int.MaxValue)).ToArray(),
            byte[] bytes => bytes.Select(x => (int)x).ToArray(),
            Array array => array.Cast<object>().Select(x => Convert.ToInt32(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            _ => null,
        };
    }

    public byte[]? GetBytes(string name) => Raw(name) as byte[];

    public WmiRow[]? GetRows(string name) => Raw(name) switch
    {
        WmiRow[] rows => rows,
        WmiRow row => new[] { row },
        _ => null,
    };

    /// <summary>Converts a CIM DATETIME string ("20240612000000.000000+000") to a DateTime.</summary>
    public DateTime? GetDateTime(string name)
    {
        var s = GetString(name);
        if (s is null) return null;
        try
        {
            return ManagementDateTimeConverter.ToDateTime(s);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Decodes a UInt16[] of character codes (used by WmiMonitorID) into a string, trimming NULs.</summary>
    public string? GetCharArrayString(string name)
    {
        var v = Raw(name);
        if (v is ushort[] codes)
        {
            var chars = codes.TakeWhile(c => c != 0).Select(c => (char)c).ToArray();
            var s = new string(chars).Trim();
            return s.Length == 0 ? null : s;
        }
        if (v is Array array)
        {
            var chars = array.Cast<object>().Select(o => (char)Convert.ToInt32(o, System.Globalization.CultureInfo.InvariantCulture)).TakeWhile(c => c != '\0').ToArray();
            var s = new string(chars).Trim();
            return s.Length == 0 ? null : s;
        }
        return null;
    }

    public override string ToString() => string.Join(", ", _values.Select(kv => $"{kv.Key}={kv.Value}"));
}

/// <summary>Runs WMI queries on a worker thread, with a per-query timeout and detached results.</summary>
public static class WmiQuery
{
    public const string Cimv2 = @"root\CIMV2";
    public const string WmiRoot = @"root\WMI";
    public const string Storage = @"root\Microsoft\Windows\Storage";
    public const string StandardCimv2 = @"root\StandardCimv2";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(12);

    /// <summary>Executes the query; throws on failure. Prefer <see cref="TryQueryAsync"/> inside providers.</summary>
    public static Task<IReadOnlyList<WmiRow>> QueryAsync(string scopePath, string wql, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        return Task.Run(() => Query(scopePath, wql, timeout ?? DefaultTimeout), cancellationToken);
    }

    /// <summary>Executes the query and returns an empty list (after logging) when WMI is unavailable, denied or broken.</summary>
    public static async Task<IReadOnlyList<WmiRow>> TryQueryAsync(string scopePath, string wql, CancellationToken cancellationToken, IScanLogger? logger = null, string component = "WMI", TimeSpan? timeout = null)
    {
        try
        {
            return await QueryAsync(scopePath, wql, cancellationToken, timeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidOperationException or TypeInitializationException or PlatformNotSupportedException)
        {
            // Classes that simply do not exist on this machine (e.g. battery classes on a desktop) are expected, not errors.
            var level = ex is ManagementException { ErrorCode: ManagementStatus.InvalidClass or ManagementStatus.NotSupported or ManagementStatus.InvalidNamespace or ManagementStatus.NotFound }
                ? LogLevel.Debug
                : LogLevel.Warning;
            logger?.Log(level, component, $"WMI query failed in {scopePath}: {DescribeQuery(wql)}", ex);
            return Array.Empty<WmiRow>();
        }
    }

    public static IReadOnlyList<WmiRow> Query(string scopePath, string wql, TimeSpan timeout)
    {
        var scope = new ManagementScope(scopePath, new ConnectionOptions { Timeout = timeout, EnablePrivileges = false, Impersonation = ImpersonationLevel.Impersonate });
        var options = new System.Management.EnumerationOptions { Timeout = timeout, ReturnImmediately = true, Rewindable = false };

        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql), options);
        using var results = searcher.Get();

        var rows = new List<WmiRow>();
        foreach (ManagementBaseObject item in results)
        {
            using (item)
            {
                rows.Add(new WmiRow(item));
            }
        }
        return rows;
    }

    /// <summary>Reduces a WQL statement to its FROM clause for logs (never includes result data).</summary>
    private static string DescribeQuery(string wql)
    {
        var index = wql.IndexOf(" FROM ", StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? wql[(index + 6)..].Split(' ')[0] : wql.Length > 40 ? wql[..40] : wql;
    }
}
