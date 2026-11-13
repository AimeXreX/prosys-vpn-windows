using System.IO.Compression;
using System.Text.RegularExpressions;

namespace v2rayN.ProSyS;

/// <summary>
/// Windows notifications (tray balloons), only after the user agreed once. Each key is shown at most once per
/// <see cref="Cooldown"/>, so a flaky network or a long-expired subscription never spams the user.
/// </summary>
public static class ProSySNotifier
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(12);
    private static readonly Dictionary<string, DateTime> Shown = [];

    /// <summary>Set by the tray icon view: (title, message).</summary>
    public static Action<string, string>? Sink { get; set; }

    public static void Notify(string key, string title, string message)
    {
        if (AppManager.Instance.Config.UiItem.ProSySNotifications != true || Sink == null)
        {
            return;
        }
        lock (Shown)
        {
            if (Shown.TryGetValue(key, out var at) && DateTime.UtcNow - at < Cooldown)
            {
                return;
            }
            Shown[key] = DateTime.UtcNow;
        }
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Sink?.Invoke(title, message);
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ProSySNotifier", ex);
            }
        });
    }

    /// <summary>Lets a condition notify again (e.g. the connection came back, so a later drop is news again).</summary>
    public static void Reset(string key)
    {
        lock (Shown)
        {
            Shown.Remove(key);
        }
    }
}

/// <summary>"Report a problem": zips recent logs with secrets masked, saves it on the Desktop and opens support.</summary>
public static class ProSySDiagnostics
{
    // Links and credentials that must never leave the PC: config links, UUIDs, passwords, tokens and subscription URLs.
    private static readonly Regex[] Secrets =
    [
        new(@"\b(vmess|vless|trojan|ss|ssr|hysteria2|hy2|tuic|wireguard|wg|anytls|naive\+https?|socks|http)://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"https?://[^\s""']*(sub|token|link|api)[^\s""']*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(""?(password|pass|pwd|token|private_?key|psk|id)""?\s*[:=]\s*)(""[^""]*""|\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    public static string Mask(string text)
    {
        foreach (var re in Secrets)
        {
            text = re.Replace(text, m => m.Groups.Count > 3 && m.Groups[1].Success ? m.Groups[1].Value + "\"***\"" : "***");
        }
        return text;
    }

    /// <summary>Creates the report and returns its path, or null when it could not be written.</summary>
    public static string? CreateReport()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(desktop, $"ProSySVPN-report-{DateTime.Now:yyyyMMdd-HHmm}.zip");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

            var info = new StringBuilder()
                .AppendLine($"ProSyS VPN {ProSySBrand.Version}")
                .AppendLine($"Windows {Environment.OSVersion.Version} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}")
                .AppendLine($"Admin: {Utils.IsAdministrator()}")
                .AppendLine($"TUN: {AppManager.Instance.Config.TunModeItem.EnableTun}, system proxy: {AppManager.Instance.Config.SystemProxyItem.SysProxyType}")
                .AppendLine($"Gaming mode: {AppManager.Instance.Config.UiItem.ProSySGamingMode}, fragment: {AppManager.Instance.Config.CoreBasicItem.EnableFragment}")
                .AppendLine($"Direct DNS: {AppManager.Instance.Config.SimpleDNSItem?.DirectDNS}")
                .ToString();
            WriteEntry(zip, "info.txt", info);

            var logs = new DirectoryInfo(Utils.GetLogPath());
            foreach (var file in logs.Exists ? logs.GetFiles("*.txt").OrderByDescending(f => f.LastWriteTimeUtc).Take(2) : [])
            {
                using var reader = new StreamReader(new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                WriteEntry(zip, "logs/" + file.Name, Mask(reader.ReadToEnd()));
            }
            return path;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ProSySDiagnostics", ex);
            return null;
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
