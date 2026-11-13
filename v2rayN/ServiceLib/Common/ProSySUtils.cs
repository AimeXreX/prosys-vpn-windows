using System.Globalization;

namespace ServiceLib.Common;

/// <summary>Traffic and expiry a subscription panel reports in its "subscription-userinfo" response header.</summary>
public sealed class ProSySSubInfo
{
    public long Upload { get; set; }
    public long Download { get; set; }

    /// <summary>Total volume in bytes; 0 means unlimited.</summary>
    public long Total { get; set; }

    /// <summary>Expiry as Unix seconds; 0 means never.</summary>
    public long Expire { get; set; }

    public long UpdatedAt { get; set; }

    public long Used => Upload + Download;
    public long Remaining => Total > 0 ? Math.Max(0, Total - Used) : 0;
    public bool HasTotal => Total > 0;
    public bool HasExpire => Expire > 0;

    public double? DaysLeft(DateTimeOffset now) =>
        HasExpire ? (DateTimeOffset.FromUnixTimeSeconds(Expire) - now).TotalDays : null;

    /// <summary>True when less than 10% of the volume or fewer than 3 days are left (or it already ran out).</summary>
    public bool IsEndingSoon(DateTimeOffset now) =>
        (HasTotal && Remaining < Total / 10) || (DaysLeft(now) is { } d && d < 3);
}

public static class ProSySUtils
{
    /// <summary>
    /// Parses "upload=1; download=2; total=3; expire=1700000000" (3x-ui, Marzban, Hiddify, sing-box panels).
    /// Returns null when the header carries none of the known keys.
    /// </summary>
    public static ProSySSubInfo? ParseSubscriptionUserInfo(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }
        var info = new ProSySSubInfo();
        var found = false;
        foreach (var part in header.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 || !double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0)
            {
                continue;
            }
            var value = (long)Math.Min(number, long.MaxValue);
            switch (kv[0].ToLowerInvariant())
            {
                case "upload": info.Upload = value; found = true; break;
                case "download": info.Download = value; found = true; break;
                case "total": info.Total = value; found = true; break;
                case "expire": info.Expire = value; found = true; break;
            }
        }
        if (!found)
        {
            return null;
        }
        info.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return info;
    }

    /// <summary>Compares "1.2.0" / "v1.10.3" style versions. Returns &gt; 0 when <paramref name="a"/> is newer.</summary>
    public static int CompareVersions(string? a, string? b)
    {
        static int[] Parse(string? v)
        {
            v = (v ?? string.Empty).Trim().TrimStart('v', 'V');
            var cut = v.IndexOfAny(['-', '+', ' ']);
            if (cut >= 0)
            {
                v = v[..cut];
            }
            return v.Split('.').Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray();
        }
        var x = Parse(a);
        var y = Parse(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0)
            {
                return c;
            }
        }
        return 0;
    }

    /// <summary>Protocols that carry UDP natively (no TCP head-of-line blocking): preferred in gaming mode.</summary>
    public static bool IsUdpNative(EConfigType type) =>
        type is EConfigType.WireGuard or EConfigType.Hysteria2 or EConfigType.TUIC or EConfigType.MASQUE;
}

/// <summary>Per-subscription info from the last successful update, kept in guiConfigs/prosys-subinfo.json.</summary>
public static class ProSySSubInfoStore
{
    private const string FileName = "prosys-subinfo.json";
    private static readonly Lock Gate = new();
    private static Dictionary<string, ProSySSubInfo>? _cache;

    public static ProSySSubInfo? Get(string? subId)
    {
        if (subId.IsNullOrEmpty())
        {
            return null;
        }
        lock (Gate)
        {
            return Load().TryGetValue(subId, out var info) ? info : null;
        }
    }

    public static void Set(string subId, ProSySSubInfo info)
    {
        lock (Gate)
        {
            var all = Load();
            all[subId] = info;
            try
            {
                File.WriteAllText(Utils.GetConfigPath(FileName), JsonUtils.Serialize(all));
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ProSySSubInfoStore", ex);
            }
        }
    }

    private static Dictionary<string, ProSySSubInfo> Load()
    {
        if (_cache != null)
        {
            return _cache;
        }
        try
        {
            var path = Utils.GetConfigPath(FileName);
            _cache = File.Exists(path) ? JsonUtils.Deserialize<Dictionary<string, ProSySSubInfo>>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ProSySSubInfoStore", ex);
        }
        return _cache ??= [];
    }
}
