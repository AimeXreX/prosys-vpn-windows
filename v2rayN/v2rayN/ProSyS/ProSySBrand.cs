namespace v2rayN.ProSyS;

/// <summary>Fixed ProSyS VPN endpoints and brand values shared by the Windows app.</summary>
public static class ProSySBrand
{
    public const string ProductName = "ProSyS VPN";
    public const string Platform = "windows";

    /// <summary>Public host of the free-access backend (nginx routes /api/free/ to the Android/desktop backend on port 3010).</summary>
    public const string ApiBase = "https://vpn.prosysvpn.top";

    public const string SiteUrl = "https://vpn.prosysvpn.top/";
    public const string BotUrl = "https://t.me/ProSySsellbot";
    public const string ChannelUrl = "https://t.me/Prosys_VPN";
    public const string PrivacyUrl = "https://vpn.prosysvpn.top/privacy";
    public const string SourceUrl = "https://github.com/AimeXreX/prosys-vpn-windows";
    public const string UpstreamUrl = "https://github.com/2dust/v2rayN";

    public const long DefaultDailyQuotaBytes = 512L * 1024 * 1024;

    /// <summary>ProSyS release version (v2rayN.csproj &lt;Version&gt;), not the upstream ServiceLib version.</summary>
    public static string Version { get; } =
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    public static string UserAgent => $"ProSySVPN-Windows/{Version}";
}
