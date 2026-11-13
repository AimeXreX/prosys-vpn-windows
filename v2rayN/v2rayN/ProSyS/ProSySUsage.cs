namespace v2rayN.ProSyS;

/// <summary>
/// Traffic that went through the tunnel, per local day, kept in guiConfigs/prosys-usage.json (last ~2 months).
/// "This month" follows the Solar Hijri calendar, like the rest of the Persian UI.
/// </summary>
public static class ProSySUsage
{
    private const string FileName = "prosys-usage.json";
    private static readonly Lock Gate = new();
    private static readonly PersianCalendar Persian = new();
    private static Dictionary<string, long>? _days;
    private static DateTime _lastSave = DateTime.UtcNow;

    private static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static void Add(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }
        lock (Gate)
        {
            var days = Load();
            var key = Key(DateTime.Now);
            days[key] = days.GetValueOrDefault(key) + bytes;
            if (DateTime.UtcNow - _lastSave > TimeSpan.FromSeconds(60))
            {
                SaveLocked();
            }
        }
    }

    public static (long today, long month) Totals()
    {
        lock (Gate)
        {
            var days = Load();
            var now = DateTime.Now;
            var today = days.GetValueOrDefault(Key(now));
            var y = Persian.GetYear(now);
            var m = Persian.GetMonth(now);
            long month = 0;
            foreach (var (key, bytes) in days)
            {
                if (DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    && Persian.GetYear(d) == y && Persian.GetMonth(d) == m)
                {
                    month += bytes;
                }
            }
            return (today, month);
        }
    }

    public static void Save()
    {
        lock (Gate)
        {
            SaveLocked();
        }
    }

    private static void SaveLocked()
    {
        _lastSave = DateTime.UtcNow;
        if (_days == null)
        {
            return;
        }
        try
        {
            var cutoff = Key(DateTime.Now.AddDays(-62));
            foreach (var old in _days.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList())
            {
                _days.Remove(old);
            }
            File.WriteAllText(Utils.GetConfigPath(FileName), JsonUtils.Serialize(_days));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ProSySUsage", ex);
        }
    }

    private static Dictionary<string, long> Load()
    {
        if (_days != null)
        {
            return _days;
        }
        try
        {
            var path = Utils.GetConfigPath(FileName);
            _days = File.Exists(path) ? JsonUtils.Deserialize<Dictionary<string, long>>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ProSySUsage", ex);
        }
        return _days ??= [];
    }
}
