using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using ServiceLib.Services;

namespace v2rayN.ProSyS;

/// <summary>Daily free quota as reported by the backend.</summary>
public sealed class ProSySFreeStatus
{
    public long QuotaBytes { get; init; }
    public long UsedBytes { get; init; }
    public long RemainingBytes { get; init; }

    /// <summary>Unix time in milliseconds when the quota renews (Tehran midnight).</summary>
    public long ResetAt { get; init; }

    public bool Exhausted { get; init; }
    public string SiteUrl { get; init; } = ProSySBrand.SiteUrl;
    public string BotUrl { get; init; } = ProSySBrand.BotUrl;
    public string ChannelUrl { get; init; } = ProSySBrand.ChannelUrl;

    internal static ProSySFreeStatus FromJson(JsonNode? node)
    {
        static long L(JsonNode? n) => n is null ? 0 : (long)n.GetValue<double>();
        static string S(JsonNode? n, string fallback) => n?.GetValue<string>() is { Length: > 0 } v ? v : fallback;
        return new ProSySFreeStatus
        {
            QuotaBytes = L(node?["quotaBytes"]),
            UsedBytes = L(node?["usedBytes"]),
            RemainingBytes = L(node?["remainingBytes"]),
            ResetAt = L(node?["resetAt"]),
            Exhausted = node?["exhausted"]?.GetValue<bool>() ?? false,
            SiteUrl = S(node?["siteUrl"], ProSySBrand.SiteUrl),
            BotUrl = S(node?["botUrl"], ProSySBrand.BotUrl),
            ChannelUrl = S(node?["channelUrl"], ProSySBrand.ChannelUrl),
        };
    }
}

/// <summary>
/// Windows counterpart of the Android FreeAccessManager (v4.0.0).
/// - Installation id: SHA-256 of the Windows MachineGuid (the raw id never leaves the PC).
/// - Device token: stored only with DPAPI (CurrentUser) outside the profile database, so it is not part of backups.
/// - Managed subscription: fetched with the X-ProSys-Device header and imported into a locked profile group.
/// </summary>
public sealed class ProSySFreeManager
{
    private static readonly Lazy<ProSySFreeManager> _instance = new(() => new());
    public static ProSySFreeManager Instance => _instance.Value;

    private const string Tag = "ProSySFree";
    private const string TokenFile = "prosys-device.bin";
    private const string FallbackIdFile = "prosys-install.id";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ProSySVPN.Windows.DeviceToken.v1");
    private static readonly TimeSpan SubscriptionInterval = TimeSpan.FromMinutes(60);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private string? _subscriptionUrl;
    private DateTime _lastSubscription = DateTime.MinValue;

    public ProSySFreeStatus? Status { get; private set; }
    public string? LastError { get; private set; }
    public bool HasManagedProfiles { get; private set; }

    public event Action? StatusChanged;

    private ProSySFreeManager()
    {
        // Talk to the backend directly; the app's own proxy may be the thing that is out of quota.
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(ProSySBrand.UserAgent);
        _token = LoadToken();
    }

    #region Public API

    /// <summary>Registers the device if needed, refreshes the quota and (hourly or when forced) the managed servers.</summary>
    public async Task<bool> SyncAsync(Config config, bool forceSubscription = false)
    {
        await _gate.WaitAsync();
        try
        {
            LastError = null;
            if (_token.IsNullOrEmpty() || _subscriptionUrl.IsNullOrEmpty())
            {
                if (!await RegisterAsync())
                {
                    return false;
                }
            }
            else if (!await RefreshStatusAsync())
            {
                // Token rejected (401) or network error. Re-register once on 401.
                if (_token.IsNullOrEmpty() && !await RegisterAsync())
                {
                    return false;
                }
            }

            var managed = await AppManager.Instance.ProfileItems(Global.ProSySManagedSubId);
            HasManagedProfiles = managed is { Count: > 0 };
            if (forceSubscription || !HasManagedProfiles || DateTime.UtcNow - _lastSubscription > SubscriptionInterval)
            {
                await RefreshSubscriptionAsync(config);
            }
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            LastError = "ارتباط با سرور ProSyS برقرار نشد. اینترنت را بررسی و دوباره تلاش کنید.";
            return false;
        }
        finally
        {
            _gate.Release();
            StatusChanged?.Invoke();
        }
    }

    /// <summary>Real delay (ms) of the free server picked by the last fastest-server test, or 0 when unknown.</summary>
    public int BestDelayMs { get; private set; }

    public bool IsTesting { get; private set; }

    private int _testing;

    /// <summary>
    /// Real-pings every managed free endpoint (direct, CDN and operator-specific variants) through a temporary core and
    /// makes the fastest one active. Falls back to the first server when nothing answers.
    /// </summary>
    public async Task<bool> SelectFastestFreeServerAsync(Config config)
    {
        var managed = await AppManager.Instance.ProfileItems(Global.ProSySManagedSubId);
        if (managed is not { Count: > 1 } || Interlocked.Exchange(ref _testing, 1) == 1)
        {
            return await SelectFreeServerAsync(config);
        }

        IsTesting = true;
        StatusChanged?.Invoke();
        try
        {
            // The speed test builds core configs, which needs the main window's Init() to have finished.
            await ServiceLib.ViewModels.MainWindowViewModel.InitCompleted.WaitAsync(TimeSpan.FromSeconds(90));

            var delays = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
            var speedtest = new SpeedtestService(config, result =>
            {
                if (result.IndexId.IsNotEmpty() && int.TryParse(result.Delay, out var ms) && ms > 0)
                {
                    delays[result.IndexId!] = ms;
                }
                return Task.CompletedTask;
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await speedtest.RunLoop(ESpeedActionType.Realping, managed, timeout.Token);

            if (delays.IsEmpty)
            {
                return await SelectFreeServerAsync(config);
            }
            var best = delays.MinBy(kv => kv.Value);
            BestDelayMs = best.Value;
            Logging.SaveLog($"{Tag}: fastest free server {best.Key} ({best.Value} ms) of {delays.Count}/{managed.Count}");
            if (config.IndexId != best.Key)
            {
                await ConfigHandler.SetDefaultServerIndex(config, best.Key);
                AppEvents.ProSySProfilesChanged.Publish();
            }
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            return await SelectFreeServerAsync(config);
        }
        finally
        {
            IsTesting = false;
            Interlocked.Exchange(ref _testing, 0);
            StatusChanged?.Invoke();
        }
    }

    /// <summary>Selects the first managed free server as the active profile.</summary>
    public async Task<bool> SelectFreeServerAsync(Config config)
    {
        var managed = await AppManager.Instance.ProfileItems(Global.ProSySManagedSubId);
        var first = managed?.FirstOrDefault();
        if (first == null)
        {
            return false;
        }
        if (config.IndexId != first.IndexId)
        {
            await ConfigHandler.SetDefaultServerIndex(config, first.IndexId);
            AppEvents.ProSySProfilesChanged.Publish();
        }
        return true;
    }

    #endregion Public API

    #region Backend calls

    private async Task<bool> RegisterAsync()
    {
        var body = new JsonObject { ["installationId"] = InstallationId() };
        if (_token.IsNotEmpty())
        {
            body["deviceToken"] = _token;
        }
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{ProSySBrand.ApiBase}/api/free/register", content);
        var text = await response.Content.ReadAsStringAsync();
        var json = SafeParse(text);

        if (!response.IsSuccessStatusCode)
        {
            LastError = TranslateError((int)response.StatusCode, json?["error"]?.GetValue<string>());
            Logging.SaveLog($"{Tag}: register failed {(int)response.StatusCode} {json?["error"]}");
            return false;
        }

        var token = json?["deviceToken"]?.GetValue<string>();
        if (token.IsNullOrEmpty())
        {
            LastError = "پاسخ نامعتبر از سرور دریافت شد.";
            return false;
        }
        if (token != _token)
        {
            _token = token;
            SaveToken(token);
        }
        _subscriptionUrl = json?["subscriptionUrl"]?.GetValue<string>();
        Status = ProSySFreeStatus.FromJson(json);
        return true;
    }

    private async Task<bool> RefreshStatusAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ProSySBrand.ApiBase}/api/free/status");
        request.Headers.Add("X-ProSys-Device", _token);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await _http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            ClearToken();
            return false;
        }
        if (!response.IsSuccessStatusCode)
        {
            LastError = TranslateError((int)response.StatusCode, null);
            return false;
        }
        Status = ProSySFreeStatus.FromJson(SafeParse(await response.Content.ReadAsStringAsync()));
        return true;
    }

    private async Task RefreshSubscriptionAsync(Config config)
    {
        if (_subscriptionUrl.IsNullOrEmpty() || _token.IsNullOrEmpty())
        {
            return;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, _subscriptionUrl);
        request.Headers.Add("X-ProSys-Device", _token);
        using var response = await _http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            ClearToken();
            return;
        }
        if (!response.IsSuccessStatusCode)
        {
            Logging.SaveLog($"{Tag}: subscription failed {(int)response.StatusCode}");
            return;
        }
        var data = (await response.Content.ReadAsStringAsync()).Trim();
        if (data.IsNullOrEmpty())
        {
            return;
        }

        await EnsureManagedGroupAsync(config);
        var hadActive = (await ConfigHandler.GetDefaultServer(config)) != null;
        var count = await ConfigHandler.AddBatchServers(config, data, Global.ProSySManagedSubId, true);
        if (count > 0)
        {
            _lastSubscription = DateTime.UtcNow;
            HasManagedProfiles = true;
            var active = await AppManager.Instance.GetProfileItem(config.IndexId);
            if (!hadActive || active == null)
            {
                await SelectFreeServerAsync(config);
            }
            AppEvents.ProSySProfilesChanged.Publish();

            // Endpoints may have changed: re-pick the fastest in the background, but never override a personal server.
            if (Global.IsProSySManaged(await ConfigHandler.GetDefaultServer(config)))
            {
                _ = Task.Run(() => SelectFastestFreeServerAsync(config));
            }
        }
    }

    private static async Task EnsureManagedGroupAsync(Config config)
    {
        var existing = await AppManager.Instance.GetSubItem(Global.ProSySManagedSubId);
        if (existing != null)
        {
            return;
        }
        // Url stays empty on purpose: the generic updater skips it and the device token is never written to the database.
        await ConfigHandler.AddSubItem(config, new SubItem
        {
            Id = Global.ProSySManagedSubId,
            Remarks = "ProSyS رایگان",
            Url = string.Empty,
            Enabled = true,
            Sort = 0,
        });
    }

    #endregion Backend calls

    #region Device identity

    /// <summary>64 lowercase hex characters, the format the backend validates.</summary>
    private static string InstallationId()
    {
        var seed = ReadMachineGuid() ?? FallbackSeed();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"prosys-{ProSySBrand.Platform}:{seed.Trim().ToLowerInvariant()}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            return null;
        }
    }

    private static string FallbackSeed()
    {
        var path = Path.Combine(Path.GetDirectoryName(TokenPath())!, FallbackIdFile);
        if (File.Exists(path))
        {
            var value = File.ReadAllText(path).Trim();
            if (value.Length >= 16)
            {
                return value;
            }
        }
        var created = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(path, created);
        return created;
    }

    /// <summary>
    /// The token lives per Windows user (%LOCALAPPDATA%\ProSySVPN), not in the portable app folder. The backend answers
    /// 409 when a known PC registers again without its token, so losing it on every re-extract or update locked users out.
    /// </summary>
    private static string TokenPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProSySVPN");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, TokenFile);
    }

    private static string? LoadToken()
    {
        try
        {
            var path = TokenPath();
            var legacy = Utils.GetConfigPath(TokenFile);
            if (!File.Exists(path) && File.Exists(legacy))
            {
                File.Copy(legacy, path);
            }
            if (!File.Exists(path))
            {
                return null;
            }
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            return null;
        }
    }

    private static void SaveToken(string token)
    {
        try
        {
            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenPath(), cipher);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
    }

    private void ClearToken()
    {
        _token = null;
        _subscriptionUrl = null;
        foreach (var path in new[] { TokenPath(), Utils.GetConfigPath(TokenFile) })
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    #endregion Device identity

    #region Helpers

    private static JsonNode? SafeParse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch
        {
            return null;
        }
    }

    private static string TranslateError(int status, string? message) => status switch
    {
        409 => "این دستگاه قبلاً ثبت شده است. لطفاً چند ساعت دیگر دوباره تلاش کنید.",
        429 when message?.Contains("network", StringComparison.OrdinalIgnoreCase) == true
            => "تعداد دستگاه‌های جدید از این شبکه امروز به سقف رسیده است. فردا دوباره تلاش کنید.",
        429 => "ثبت‌نام رایگان موقتاً متوقف است. کمی بعد دوباره تلاش کنید.",
        400 => "شناسهٔ دستگاه نامعتبر است.",
        >= 500 => "سرویس رایگان موقتاً در دسترس نیست. کمی بعد دوباره تلاش کنید.",
        _ => "ارتباط با سرور ProSyS برقرار نشد.",
    };

    #endregion Helpers
}
