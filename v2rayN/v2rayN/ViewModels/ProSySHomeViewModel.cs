using System.Runtime.CompilerServices;
using v2rayN.ProSyS;

namespace v2rayN.ViewModels;

/// <summary>Minimal ICommand used by the ProSyS home screen.</summary>
public sealed class ProSySCommand(Func<Task> run, Func<bool>? canRun = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && (canRun?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (_running)
        {
            return;
        }
        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await run();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ProSySCommand", ex);
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

/// <summary>State of the gaming-style home screen: big connect orb, daily quota, live speed and purchase links.</summary>
public sealed class ProSySHomeViewModel : INotifyPropertyChanged
{
    private readonly Config _config;
    private readonly DispatcherTimer _fastTimer;
    private readonly DispatcherTimer _syncTimer;
    private static readonly CultureInfo Fa = new("fa-IR");

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the user asks for the classic v2rayN interface.</summary>
    public event Action? AdvancedRequested;

    public ProSySHomeViewModel()
    {
        _config = AppManager.Instance.Config;

        ToggleConnectCmd = new ProSySCommand(ToggleConnectAsync);
        RetryCmd = new ProSySCommand(() => SyncAsync(true));
        UseFreeServerCmd = new ProSySCommand(UseFreeServerAsync);
        OpenSiteCmd = new ProSySCommand(() => Open(ProSySFreeManager.Instance.Status?.SiteUrl ?? ProSySBrand.SiteUrl));
        OpenBotCmd = new ProSySCommand(() => Open(ProSySFreeManager.Instance.Status?.BotUrl ?? ProSySBrand.BotUrl));
        OpenChannelCmd = new ProSySCommand(() => Open(ProSySFreeManager.Instance.Status?.ChannelUrl ?? ProSySBrand.ChannelUrl));
        ShowAdvancedCmd = new ProSySCommand(() =>
        {
            AdvancedRequested?.Invoke();
            return Task.CompletedTask;
        });

        ProSySFreeManager.Instance.StatusChanged += () => Application.Current?.Dispatcher.BeginInvoke(ApplyFreeStatus);

        AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .Subscribe(item => Application.Current?.Dispatcher.BeginInvoke(() => ApplySpeed(item)));

        _fastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fastTimer.Tick += async (_, _) => await RefreshLocalStateAsync();
        _fastTimer.Start();

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _syncTimer.Tick += async (_, _) => await SyncAsync(false);
        _syncTimer.Start();

        ApplyFreeStatus();
        _ = RefreshLocalStateAsync();
        _ = InitialSyncAsync();
    }

    private async Task InitialSyncAsync()
    {
        // Let the main window finish loading profiles and starting the core first.
        await Task.Delay(2500);
        await SyncAsync(false);
        await RefreshLocalStateAsync();
    }

    #region Commands

    public ICommand ToggleConnectCmd { get; }
    public ICommand RetryCmd { get; }
    public ICommand UseFreeServerCmd { get; }
    public ICommand OpenSiteCmd { get; }
    public ICommand OpenBotCmd { get; }
    public ICommand OpenChannelCmd { get; }
    public ICommand ShowAdvancedCmd { get; }

    #endregion Commands

    #region Bindable state

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value, [nameof(StatusTitle), nameof(StatusSubtitle), nameof(ConnectButtonText)]); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value, [nameof(StatusTitle)]); }

    public string StatusTitle => IsBusy ? "در حال برقراری…" : IsConnected ? "متصل هستید" : "قطع است";

    public string StatusSubtitle => IsConnected
        ? (TunEnabled ? "کل ترافیک سیستم از تونل امن ProSyS عبور می‌کند." : "پراکسی سیستم فعال است؛ مرورگر و برنامه‌ها از ProSyS عبور می‌کنند.")
        : "برای اتصال امن روی اوربِ ProSyS بزنید.";

    public string ConnectButtonText => IsConnected ? "قطع اتصال" : "اتصال";

    private bool _tunEnabled;
    public bool TunEnabled
    {
        get => _tunEnabled;
        set
        {
            if (_tunEnabled == value)
            {
                return;
            }
            _tunEnabled = value;
            OnChanged(nameof(TunEnabled), nameof(StatusSubtitle));
            if (StatusBarViewModel.Instance.EnableTun != value)
            {
                StatusBarViewModel.Instance.EnableTun = value;
            }
        }
    }

    public bool IsAdmin { get; } = Utils.IsAdministrator();

    private string _serverName = "در حال آماده‌سازی سرور رایگان…";
    public string ServerName { get => _serverName; private set => Set(ref _serverName, value); }

    private bool _isFreeServer = true;
    public bool IsFreeServer { get => _isFreeServer; private set => Set(ref _isFreeServer, value, [nameof(ServerBadge)]); }

    public string ServerBadge => IsFreeServer ? "رایگان" : "شخصی";

    private string _serverPing = string.Empty;
    public string ServerPing { get => _serverPing; private set => Set(ref _serverPing, value, [nameof(HasServerPing)]); }

    public bool HasServerPing => ServerPing.IsNotEmpty();

    private string _remainingText = "—";
    public string RemainingText { get => _remainingText; private set => Set(ref _remainingText, value); }

    private string _quotaText = "از ۵۰۰ مگابایت امروز";
    public string QuotaText { get => _quotaText; private set => Set(ref _quotaText, value); }

    private double _remainingPercent = 100;
    public double RemainingPercent { get => _remainingPercent; private set => Set(ref _remainingPercent, value); }

    private string _resetText = "تمدید خودکار هر شب ساعت ۰۰:۰۰ به وقت تهران";
    public string ResetText { get => _resetText; private set => Set(ref _resetText, value); }

    private bool _isExhausted;
    public bool IsExhausted { get => _isExhausted; private set => Set(ref _isExhausted, value); }

    private string _errorText = string.Empty;
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value, [nameof(HasError)]); }

    public bool HasError => ErrorText.IsNotEmpty();

    private string _downSpeed = "—";
    public string DownSpeed { get => _downSpeed; private set => Set(ref _downSpeed, value); }

    private string _upSpeed = "—";
    public string UpSpeed { get => _upSpeed; private set => Set(ref _upSpeed, value); }

    public string VersionText { get; } = $"نسخهٔ ویندوز {ToFa(ProSySBrand.Version)}";

    #endregion Bindable state

    #region Actions

    private async Task ToggleConnectAsync()
    {
        if (IsConnected)
        {
            await DisconnectAsync();
            return;
        }

        IsBusy = true;
        try
        {
            var server = await ConfigHandler.GetDefaultServer(_config);
            if (server == null || (Global.IsProSySManaged(server) && !ProSySFreeManager.Instance.HasManagedProfiles))
            {
                await ProSySFreeManager.Instance.SyncAsync(_config, true);
                await ProSySFreeManager.Instance.SelectFreeServerAsync(_config);
                server = await ConfigHandler.GetDefaultServer(_config);
            }
            if (server == null)
            {
                ErrorText = ProSySFreeManager.Instance.LastError ?? "هنوز سروری برای اتصال در دسترس نیست. اتصال اینترنت را بررسی کنید.";
                return;
            }
            if (Global.IsProSySManaged(server) && ProSySFreeManager.Instance.Status?.Exhausted == true)
            {
                ErrorText = "حجم رایگان امروز تمام شده است. برای ادامه، سرویس نامحدود تهیه کنید.";
                return;
            }

            ErrorText = string.Empty;
            AppEvents.SysProxyChangeRequested.Publish(ESysProxyType.ForcedChange);
            await Task.Delay(600);
        }
        finally
        {
            IsBusy = false;
            await RefreshLocalStateAsync();
        }
    }

    private async Task DisconnectAsync()
    {
        if (StatusBarViewModel.Instance.EnableTun)
        {
            TunEnabled = false;
        }
        AppEvents.SysProxyChangeRequested.Publish(ESysProxyType.ForcedClear);
        await Task.Delay(300);
        await RefreshLocalStateAsync();
    }

    private async Task UseFreeServerAsync()
    {
        await ProSySFreeManager.Instance.SyncAsync(_config, false);
        if (!await ProSySFreeManager.Instance.SelectFastestFreeServerAsync(_config))
        {
            ErrorText = ProSySFreeManager.Instance.LastError ?? "سرور رایگان هنوز آماده نیست.";
        }
        await RefreshLocalStateAsync();
    }

    private async Task SyncAsync(bool force)
    {
        await ProSySFreeManager.Instance.SyncAsync(_config, force);
    }

    private static Task Open(string url)
    {
        ProcUtils.ProcessStart(url);
        return Task.CompletedTask;
    }

    #endregion Actions

    #region State refresh

    private async Task RefreshLocalStateAsync()
    {
        IsConnected = _config.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange || _config.TunModeItem.EnableTun;
        if (_tunEnabled != _config.TunModeItem.EnableTun)
        {
            _tunEnabled = _config.TunModeItem.EnableTun;
            OnChanged(nameof(TunEnabled), nameof(StatusSubtitle));
        }

        var server = await ConfigHandler.GetDefaultServer(_config);
        if (server != null)
        {
            IsFreeServer = Global.IsProSySManaged(server);
            ServerName = CleanName(server.Remarks.IsNotEmpty() ? server.Remarks : server.Address);
        }
        else if (!ProSySFreeManager.Instance.HasManagedProfiles)
        {
            ServerName = "در حال آماده‌سازی سرور رایگان…";
        }

        UpdateResetText();
    }

    private void ApplyFreeStatus()
    {
        var manager = ProSySFreeManager.Instance;
        var status = manager.Status;
        ErrorText = manager.LastError ?? string.Empty;
        ServerPing = manager.IsTesting
            ? "در حال یافتن سریع‌ترین سرور…"
            : manager.BestDelayMs > 0 && IsFreeServer ? $"پینگ {ToFa(manager.BestDelayMs.ToString(CultureInfo.InvariantCulture))} میلی‌ثانیه · سریع‌ترین سرور" : string.Empty;
        if (status == null)
        {
            return;
        }

        var quota = status.QuotaBytes > 0 ? status.QuotaBytes : ProSySBrand.DefaultDailyQuotaBytes;
        RemainingPercent = Math.Clamp(status.RemainingBytes * 100.0 / quota, 0, 100);
        RemainingText = FormatBytes(status.RemainingBytes);
        QuotaText = $"باقی‌مانده از {FormatBytes(quota)} امروز";
        IsExhausted = status.Exhausted;
        UpdateResetText();

        // Out of free quota while browsing on the free server: stop routing traffic into a dead tunnel.
        if (status.Exhausted && IsConnected && IsFreeServer)
        {
            _ = DisconnectAsync();
        }
    }

    private void UpdateResetText()
    {
        var resetAt = ProSySFreeManager.Instance.Status?.ResetAt ?? 0;
        if (resetAt <= 0)
        {
            return;
        }
        var left = DateTimeOffset.FromUnixTimeMilliseconds(resetAt) - DateTimeOffset.UtcNow;
        ResetText = left <= TimeSpan.Zero
            ? "در حال تمدید سهمیهٔ امروز…"
            : $"تمدید سهمیه تا {ToFa($"{(int)left.TotalHours}:{left.Minutes:00}")} دیگر (۰۰:۰۰ تهران)";
    }

    private void ApplySpeed(ServerSpeedItem item)
    {
        // StatisticsXrayService / StatisticsSingboxService report per-second deltas already divided into KB.
        DownSpeed = $"{FormatBytes(item.ProxyDown * 1024)}/ث";
        UpSpeed = $"{FormatBytes(item.ProxyUp * 1024)}/ث";
    }

    #endregion State refresh

    #region Helpers

    /// <summary>WPF cannot draw colour flag emoji (they render as "NL"), so drop regional-indicator pairs and tidy spaces.</summary>
    private static string CleanName(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length)
            {
                var cp = char.ConvertToUtf32(value[i], value[i + 1]);
                if (cp is >= 0x1F1E6 and <= 0x1F1FF)
                {
                    i++;
                    continue;
                }
            }
            sb.Append(value[i]);
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "۰ مگ";
        }
        double mb = bytes / 1024d / 1024d;
        if (mb >= 1024)
        {
            return $"{ToFa((mb / 1024).ToString("0.##", CultureInfo.InvariantCulture))} گیگ";
        }
        if (mb >= 1)
        {
            return $"{ToFa(mb.ToString(mb >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture))} مگ";
        }
        return $"{ToFa((bytes / 1024d).ToString("0", CultureInfo.InvariantCulture))} کیلو";
    }

    private static string ToFa(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            sb.Append(ch is >= '0' and <= '9' ? (char)('۰' + (ch - '0')) : ch == '.' ? '٫' : ch);
        }
        return sb.ToString();
    }

    private void Set<T>(ref T field, T value, string[]? also = null, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        OnChanged([name, .. also ?? []]);
    }

    private void OnChanged(params string[] names)
    {
        foreach (var name in names.Where(n => n.IsNotEmpty()))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    #endregion Helpers
}
