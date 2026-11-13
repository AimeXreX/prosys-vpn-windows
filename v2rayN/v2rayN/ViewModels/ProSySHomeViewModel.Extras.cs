using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using v2rayN.ProSyS;

namespace v2rayN.ViewModels;

/// <summary>One program in the "outside the VPN" list.</summary>
public sealed class ProSySDirectApp
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileName(Path);
    public required ICommand RemoveCmd { get; init; }
}

/// <summary>
/// 1.3.0 additions to the home screen: add a subscription, quick settings (gaming mode, anti-filter, notifications,
/// programs outside the VPN), auto-reconnect, subscription volume / expiry, usage, and app updates.
/// </summary>
public sealed partial class ProSySHomeViewModel
{
    /// <summary>The single home screen of this process (tray menu actions use it).</summary>
    public static ProSySHomeViewModel? Instance { get; private set; }

    /// <summary>Raised when the home screen must be visible (the picker opens while the window is in the tray).</summary>
    public event Action? ShowWindowRequested;

    private DispatcherTimer? _healthTimer;
    private DispatcherTimer? _updateTimer;
    private int _healthFailures;
    private DateTime _lastReconnect = DateTime.MinValue;
    private bool _wasExhausted;
    private ProSySRelease? _release;

    private void InitExtras()
    {
        Instance = this;

        OpenAddSubCmd = new ProSySCommand(OpenAddSubAsync);
        AddSubCmd = new ProSySCommand(AddSubAsync);
        OpenSettingsCmd = new ProSySCommand(() =>
        {
            IsSettingsOpen = true;
            return Task.CompletedTask;
        });
        CloseOverlaysCmd = new ProSySCommand(() =>
        {
            IsAddSubOpen = false;
            IsSettingsOpen = false;
            return Task.CompletedTask;
        });
        AddDirectAppCmd = new ProSySCommand(AddDirectAppAsync);
        RenewCmd = new ProSySCommand(() => Open(ProSySFreeManager.Instance.Status?.BotUrl ?? ProSySBrand.BotUrl));
        UpdateCmd = new ProSySCommand(InstallUpdateAsync);
        SkipUpdateCmd = new ProSySCommand(async () =>
        {
            _config.UiItem.ProSySSkippedVersion = _release?.Tag;
            HasUpdate = false;
            await ConfigHandler.SaveConfig(_config);
        });
        ReportCmd = new ProSySCommand(CreateReportAsync);

        _gamingMode = _config.UiItem.ProSySGamingMode;
        _fragment = _config.CoreBasicItem.EnableFragment;
        _notifications = _config.UiItem.ProSySNotifications == true;
        LoadDirectApps();

        // Auto-reconnect: watch the tunnel while connected.
        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _healthTimer.Tick += async (_, _) => await CheckHealthAsync();
        _healthTimer.Start();

        // App updates: shortly after start, then every 6 hours.
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            await CheckUpdateAsync();
        };
        _updateTimer.Start();

        Application.Current.Exit += (_, _) => ProSySUsage.Save();
    }

    #region Commands

    public ICommand OpenAddSubCmd { get; private set; } = null!;
    public ICommand AddSubCmd { get; private set; } = null!;
    public ICommand OpenSettingsCmd { get; private set; } = null!;
    public ICommand CloseOverlaysCmd { get; private set; } = null!;
    public ICommand AddDirectAppCmd { get; private set; } = null!;
    public ICommand RenewCmd { get; private set; } = null!;
    public ICommand UpdateCmd { get; private set; } = null!;
    public ICommand SkipUpdateCmd { get; private set; } = null!;
    public ICommand ReportCmd { get; private set; } = null!;

    #endregion Commands

    #region Add subscription

    private bool _isAddSubOpen;
    public bool IsAddSubOpen { get => _isAddSubOpen; private set => Set(ref _isAddSubOpen, value); }

    private string _addSubText = string.Empty;
    public string AddSubText { get => _addSubText; set => Set(ref _addSubText, value); }

    private string _addSubMessage = string.Empty;
    public string AddSubMessage { get => _addSubMessage; private set => Set(ref _addSubMessage, value); }

    private static readonly string[] LinkPrefixes =
        ["http://", "https://", "vmess://", "vless://", "trojan://", "ss://", "hysteria2://", "hy2://", "tuic://", "wireguard://", "anytls://"];

    private Task OpenAddSubAsync()
    {
        AddSubMessage = string.Empty;
        var clip = WindowsUtils.GetClipboardData()?.Trim() ?? string.Empty;
        AddSubText = LinkPrefixes.Any(p => clip.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ? clip : string.Empty;
        IsAddSubOpen = true;
        return Task.CompletedTask;
    }

    private async Task AddSubAsync()
    {
        var text = AddSubText.Trim();
        if (text.IsNullOrEmpty())
        {
            AddSubMessage = "لینک اشتراک یا کانفیگ را وارد کنید.";
            return;
        }

        var singleLine = !text.Contains('\n');
        if (singleLine && (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            var uri = Utils.TryUri(text);
            if (uri == null)
            {
                AddSubMessage = "لینک اشتراک معتبر نیست.";
                return;
            }
            var exists = (await AppManager.Instance.SubItems() ?? []).Any(s => s.Url == text);
            if (exists)
            {
                AddSubMessage = "این اشتراک قبلاً اضافه شده است.";
                return;
            }
            // Saving fires the immediate fetch (and the via-proxy retry) in MainWindowViewModel.
            var ok = await ConfigHandler.AddSubItem(_config, new SubItem
            {
                Id = string.Empty,
                Remarks = uri.Host,
                Url = text,
                Enabled = true,
            }) == 0;
            if (!ok)
            {
                AddSubMessage = "افزودن اشتراک انجام نشد.";
                return;
            }
            IsAddSubOpen = false;
            ShowNotice($"اشتراک «{uri.Host}» اضافه شد و سرورهایش در حال دریافت است.");
            return;
        }

        var count = await ConfigHandler.AddBatchServers(_config, text, string.Empty, false);
        if (count <= 0)
        {
            AddSubMessage = "کانفیگ معتبری پیدا نشد. لینک subscription یا کانفیگ‌هایی مثل vless:// را وارد کنید.";
            return;
        }
        AppEvents.ProSySProfilesChanged.Publish();
        IsAddSubOpen = false;
        ShowNotice($"{ToFa(count.ToString(CultureInfo.InvariantCulture))} سرور اضافه شد.");
    }

    private void ShowNotice(string text) => NoticeManager.Instance.Enqueue(text);

    #endregion Add subscription

    #region Quick settings

    private bool _isSettingsOpen;
    public bool IsSettingsOpen { get => _isSettingsOpen; private set => Set(ref _isSettingsOpen, value); }

    private bool _gamingMode;

    /// <summary>Gaming mode: whole system through the tunnel (TUN) and UDP-native protocols preferred when connecting.</summary>
    public bool GamingMode
    {
        get => _gamingMode;
        set
        {
            if (_gamingMode == value)
            {
                return;
            }
            Set(ref _gamingMode, value);
            _config.UiItem.ProSySGamingMode = value;
            _ = ConfigHandler.SaveConfig(_config);
            if (value && !TunEnabled)
            {
                TunEnabled = true;
            }
        }
    }

    private bool _fragment;

    /// <summary>Anti-filter: Xray TLS fragmentation (CoreBasicItem.EnableFragment).</summary>
    public bool Fragment
    {
        get => _fragment;
        set
        {
            if (_fragment == value)
            {
                return;
            }
            Set(ref _fragment, value);
            _ = ApplyFragmentAsync(value, auto: false);
        }
    }

    private bool _notifications;
    public bool Notifications
    {
        get => _notifications;
        set
        {
            if (_notifications == value)
            {
                return;
            }
            Set(ref _notifications, value);
            _config.UiItem.ProSySNotifications = value;
            _ = ConfigHandler.SaveConfig(_config);
        }
    }

    public ObservableCollection<ProSySDirectApp> DirectApps { get; } = [];

    private void LoadDirectApps()
    {
        DirectApps.Clear();
        foreach (var path in _config.UiItem.ProSySDirectApps ?? [])
        {
            DirectApps.Add(new ProSySDirectApp
            {
                Path = path,
                RemoveCmd = new ProSySCommand(() => SetDirectAppsAsync((_config.UiItem.ProSySDirectApps ?? []).Where(p => p != path).ToList())),
            });
        }
        OnChanged(nameof(HasDirectApps));
    }

    public bool HasDirectApps => DirectApps.Count > 0;

    private async Task AddDirectAppAsync()
    {
        if (UI.OpenFileDialog(out var fileName, "Programs|*.exe") != true || fileName.IsNullOrEmpty())
        {
            return;
        }
        var list = _config.UiItem.ProSySDirectApps ?? [];
        if (!list.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            await SetDirectAppsAsync([.. list, fileName]);
        }
    }

    private async Task SetDirectAppsAsync(List<string> apps)
    {
        _config.UiItem.ProSySDirectApps = apps;
        await ConfigHandler.SaveConfig(_config);
        LoadDirectApps();
        // The rule is written into the TUN (sing-box) config: reload the core so it applies now.
        AppEvents.ProSySProfilesChanged.Publish();
    }

    private async Task ApplyFragmentAsync(bool on, bool auto)
    {
        _config.CoreBasicItem.EnableFragment = on;
        _config.UiItem.ProSySAutoFragment = on && auto;
        await ConfigHandler.SaveConfig(_config);
        if (_fragment != on)
        {
            _fragment = on;
            OnChanged(nameof(Fragment));
        }
        if (!auto)
        {
            AppEvents.ProSySProfilesChanged.Publish();
        }
    }

    #endregion Quick settings

    #region Subscription info and usage

    private string _subInfoText = string.Empty;
    public string SubInfoText { get => _subInfoText; private set => Set(ref _subInfoText, value, [nameof(HasSubInfo)]); }

    public bool HasSubInfo => SubInfoText.IsNotEmpty();

    private bool _isSubEnding;
    public bool IsSubEnding { get => _isSubEnding; private set => Set(ref _isSubEnding, value); }

    private string _usageText = string.Empty;
    public string UsageText { get => _usageText; private set => Set(ref _usageText, value); }

    /// <summary>"12 GB of 50 left · 9 days" for a personal subscription whose panel sends subscription-userinfo.</summary>
    private static string? DescribeSubInfo(ProSySSubInfo? info)
    {
        if (info == null || (!info.HasTotal && !info.HasExpire))
        {
            return null;
        }
        var parts = new List<string>();
        if (info.HasTotal)
        {
            parts.Add($"{FormatBytes(info.Remaining)} از {FormatBytes(info.Total)} باقی‌مانده");
        }
        if (info.DaysLeft(DateTimeOffset.UtcNow) is { } days)
        {
            parts.Add(days <= 0 ? "منقضی شده" : days < 1 ? "کمتر از یک روز مانده" : $"{ToFa(((int)days).ToString(CultureInfo.InvariantCulture))} روز مانده");
        }
        return string.Join(" · ", parts);
    }

    private void UpdateSubInfo(ProfileItem? server)
    {
        var info = server == null || Global.IsProSySManaged(server) ? null : ProSySSubInfoStore.Get(server.Subid);
        var text = DescribeSubInfo(info);
        SubInfoText = text != null ? $"اشتراک: {text}" : string.Empty;
        IsSubEnding = info?.IsEndingSoon(DateTimeOffset.UtcNow) == true;
        if (IsSubEnding && server != null)
        {
            ProSySNotifier.Notify($"sub-{server.Subid}", "اشتراک رو به پایان است", $"{text} — برای تمدید از ربات ProSyS اقدام کنید.");
        }

        var (today, month) = ProSySUsage.Totals();
        UsageText = today + month > 0 ? $"مصرف امروز {FormatBytes(today)} · این ماه {FormatBytes(month)}" : string.Empty;
    }

    private void TrackUsage(ServerSpeedItem item) => ProSySUsage.Add((item.ProxyDown + item.ProxyUp) * 1024);

    #endregion Subscription info and usage

    #region Notifications

    /// <summary>Asks once, after the first successful connection, whether Windows notifications are wanted.</summary>
    private void AskNotificationsOnce()
    {
        if (_config.UiItem.ProSySNotifications != null)
        {
            return;
        }
        var yes = UI.ShowYesNo("ProSyS VPN می‌تواند با اعلان ویندوز خبر بدهد وقتی:\n" +
            "• حجم رایگان امروز تمام شد\n• اشتراک شما رو به اتمام یا انقضا است\n• اتصال قطع شد یا به سرور دیگری منتقل شد\n• نسخهٔ جدید برنامه آمد\n\n" +
            "اعلان‌ها فعال شوند؟ (بعداً از تنظیمات سریع قابل تغییر است)") == MessageBoxResult.Yes;
        Notifications = yes;
    }

    private void NotifyExhausted(bool exhausted)
    {
        if (exhausted && !_wasExhausted)
        {
            ProSySNotifier.Notify("free-exhausted", "حجم رایگان امروز تمام شد", "سهمیه ساعت ۰۰:۰۰ به وقت تهران تمدید می‌شود. برای ادامه، اشتراک نامحدود تهیه کنید.");
        }
        _wasExhausted = exhausted;
    }

    #endregion Notifications

    #region Auto-reconnect

    private static readonly HttpClient Direct = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(6) })
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    private static async Task<bool> ProbeAsync(HttpClient http, string url)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            return (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Every 20 s while connected: is the tunnel still passing traffic? After three failures in a row (about a minute),
    /// and only when the internet itself works, switch to the fastest working server of the same source.
    /// </summary>
    private async Task CheckHealthAsync()
    {
        if (!IsConnected || IsBusy || IsPickerOpen || ProSySFreeManager.Instance.IsTesting)
        {
            _healthFailures = 0;
            return;
        }
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        using var viaTunnel = new HttpClient(new SocketsHttpHandler
        {
            Proxy = new WebProxy($"http://127.0.0.1:{port}"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromSeconds(8),
        })
        { Timeout = TimeSpan.FromSeconds(10) };

        if (await ProbeAsync(viaTunnel, "http://cp.cloudflare.com/generate_204")
            || await ProbeAsync(viaTunnel, "http://www.gstatic.com/generate_204"))
        {
            _healthFailures = 0;
            ProSySNotifier.Reset("tunnel-down");
            return;
        }
        if (++_healthFailures < 3 || DateTime.UtcNow - _lastReconnect < TimeSpan.FromMinutes(2))
        {
            return;
        }
        _healthFailures = 0;

        // Internet down altogether (not the server's fault): switching servers would not help.
        if (!await ProbeAsync(Direct, "https://www.aparat.com/") && !await ProbeAsync(Direct, "https://www.digikala.com/"))
        {
            return;
        }

        _lastReconnect = DateTime.UtcNow;
        var oldServer = ServerName;
        var sourceId = _config.UiItem.ProSySQuickSource is { Length: > 0 } s && s != ProSySQuickSource.Current ? s : ProSySQuickSource.All;
        var sources = await BuildSourcesAsync(sourceId);
        var source = sources.FirstOrDefault(x => x.Id == sourceId && x.IsEnabled) ?? sources.First(x => x.Id == ProSySQuickSource.All);
        Logging.SaveLog($"ProSyS: tunnel stopped passing traffic on {oldServer}; re-picking from {source.Id}");

        IsBusy = true;
        try
        {
            if (await PickFastestAsync(source.Id, source.Title))
            {
                ProSySNotifier.Notify("reconnected", "اتصال دوباره برقرار شد",
                    $"سرور «{oldServer}» جواب نمی‌داد؛ به «{ServerName}» منتقل شدید.");
            }
            else
            {
                ProSySNotifier.Notify("tunnel-down", "اتصال VPN قطع است", "هیچ سرور سالمی پیدا نشد. اشتراک را به‌روزرسانی یا منبع دیگری انتخاب کنید.");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion Auto-reconnect

    #region App update

    private bool _hasUpdate;
    public bool HasUpdate { get => _hasUpdate; private set => Set(ref _hasUpdate, value); }

    private string _updateText = string.Empty;
    public string UpdateText { get => _updateText; private set => Set(ref _updateText, value); }

    private bool _isUpdating;
    public bool IsUpdating { get => _isUpdating; private set => Set(ref _isUpdating, value); }

    private async Task CheckUpdateAsync()
    {
        var release = await ProSySUpdater.CheckAsync();
        if (release == null || release.Tag == _config.UiItem.ProSySSkippedVersion)
        {
            return;
        }
        _release = release;
        UpdateText = ProSySUpdater.IsInstalled
            ? $"نسخهٔ {ToFa(release.Version)} ProSyS VPN آماده است."
            : $"نسخهٔ {ToFa(release.Version)} ProSyS VPN آماده است (نسخهٔ پرتابل: از صفحهٔ دانلود بگیرید).";
        HasUpdate = true;
        ProSySNotifier.Notify($"update-{release.Tag}", "نسخهٔ جدید ProSyS VPN", $"نسخهٔ {ToFa(release.Version)} آماده است. از صفحهٔ اصلی برنامه به‌روزرسانی کنید.");
    }

    private async Task InstallUpdateAsync()
    {
        if (_release == null)
        {
            return;
        }
        if (!ProSySUpdater.IsInstalled || _release.SetupUrl.IsNullOrEmpty())
        {
            await Open(_release.PageUrl);
            return;
        }
        IsUpdating = true;
        try
        {
            var progress = new Progress<double>(p => UpdateText = $"در حال دانلود نسخهٔ {ToFa(_release.Version)}… {ToFa(((int)p).ToString(CultureInfo.InvariantCulture))}٪");
            var (ok, error) = await ProSySUpdater.DownloadAndInstallAsync(_release, progress);
            UpdateText = ok ? "در حال نصب نسخهٔ جدید؛ برنامه خودکار دوباره باز می‌شود…" : error ?? "به‌روزرسانی انجام نشد.";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    #endregion App update

    #region Report

    private Task CreateReportAsync()
    {
        var path = ProSySDiagnostics.CreateReport();
        if (path == null)
        {
            UI.Show("ساخت گزارش انجام نشد.");
            return Task.CompletedTask;
        }
        ProcUtils.ProcessStart("explorer.exe", $"/select,\"{path}\"");
        if (UI.ShowYesNo($"گزارش (بدون رمزها و لینک‌های کانفیگ) روی دسکتاپ ذخیره شد:\n{System.IO.Path.GetFileName(path)}\n\nربات پشتیبانی ProSyS باز شود تا فایل را بفرستید؟") == MessageBoxResult.Yes)
        {
            ProcUtils.ProcessStart(ProSySBrand.BotUrl);
        }
        return Task.CompletedTask;
    }

    #endregion Report

    /// <summary>Tray menu: connect (or disconnect); shows the window first when the source picker has to ask.</summary>
    public void TrayToggleConnect()
    {
        if (!IsConnected && !_config.UiItem.ProSySQuickRemember)
        {
            ShowWindowRequested?.Invoke();
        }
        if (ToggleConnectCmd.CanExecute(null))
        {
            ToggleConnectCmd.Execute(null);
        }
    }
}
