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

/// <summary>One choice in the quick-connect picker: every subscription, a single subscription, or the current server.</summary>
public sealed class ProSySQuickSource
{
    public const string All = "all";
    public const string Current = "current";
    public const string Manual = "manual";

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public string Icon { get; init; } = "Web";
    public bool IsSelected { get; init; }
    public bool IsEnabled { get; init; } = true;
    public required ICommand PickCmd { get; init; }
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
        ChangeSourceCmd = new ProSySCommand(ChangeSourceAsync);
        CancelPickerCmd = new ProSySCommand(() =>
        {
            ClosePicker(null);
            return Task.CompletedTask;
        });
        _rememberSource = _config.UiItem.ProSySQuickRemember;

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
        _ = Task.Run(async () =>
        {
            var sources = await BuildSourcesAsync(_config.UiItem.ProSySQuickSource);
            Application.Current?.Dispatcher.BeginInvoke(() => UpdateSourceText(sources));
        });
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
    public ICommand ChangeSourceCmd { get; }
    public ICommand CancelPickerCmd { get; }

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

    private bool _isPickerOpen;
    public bool IsPickerOpen { get => _isPickerOpen; private set => Set(ref _isPickerOpen, value); }

    private List<ProSySQuickSource> _quickSources = [];
    public List<ProSySQuickSource> QuickSources { get => _quickSources; private set => Set(ref _quickSources, value); }

    private bool _rememberSource;
    public bool RememberSource { get => _rememberSource; set => Set(ref _rememberSource, value); }

    private string _sourceText = "منبع اتصال: هر بار بپرس";
    public string SourceText { get => _sourceText; private set => Set(ref _sourceText, value); }

    #endregion Bindable state

    #region Actions

    private async Task ToggleConnectAsync()
    {
        if (IsConnected)
        {
            await DisconnectAsync();
            return;
        }

        // Quick connect: ask which subscription to ping (or all of them), unless the user asked us to remember.
        var sourceId = _config.UiItem.ProSySQuickRemember ? _config.UiItem.ProSySQuickSource : null;
        var sources = await BuildSourcesAsync(sourceId ?? _config.UiItem.ProSySQuickSource);
        if (sourceId == null || sources.All(s => s.Id != sourceId || !s.IsEnabled))
        {
            sourceId = await AskSourceAsync(sources);
            if (sourceId == null)
            {
                return;
            }
        }

        IsBusy = true;
        try
        {
            if (!await PickFastestAsync(sourceId, sources.First(s => s.Id == sourceId).Title))
            {
                return;
            }
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

    #region Quick connect source

    private TaskCompletionSource<string?>? _picker;

    /// <summary>Picker choices: all subscriptions, each subscription with servers (the free one included), manual servers, current server.</summary>
    private async Task<List<ProSySQuickSource>> BuildSourcesAsync(string? selectedId)
    {
        var profiles = await AppManager.Instance.ProfileItems(string.Empty) ?? [];
        var subs = await AppManager.Instance.SubItems() ?? [];
        var exhausted = ProSySFreeManager.Instance.Status?.Exhausted == true;
        var current = await ConfigHandler.GetDefaultServer(_config);
        selectedId ??= ProSySQuickSource.All;

        var list = new List<ProSySQuickSource>();
        ProSySQuickSource Make(string id, string title, string subtitle, string icon, bool enabled = true) => new()
        {
            Id = id,
            Title = title,
            Subtitle = subtitle,
            Icon = icon,
            IsEnabled = enabled,
            IsSelected = id == selectedId,
            PickCmd = new ProSySCommand(() =>
            {
                ClosePicker(id);
                return Task.CompletedTask;
            }, () => enabled),
        };

        var testable = profiles.Where(p => !(exhausted && Global.IsProSySManaged(p))).ToList();
        list.Add(Make(ProSySQuickSource.All, "همهٔ اشتراک‌ها",
            $"پینگ {ToFa(testable.Count.ToString(CultureInfo.InvariantCulture))} سرور گرفته و سریع‌ترین وصل می‌شود", "LightningBolt", testable.Count > 0));

        foreach (var sub in subs.OrderBy(s => s.Id == Global.ProSySManagedSubId ? 0 : 1).ThenBy(s => s.Sort))
        {
            var count = profiles.Count(p => p.Subid == sub.Id);
            if (count == 0)
            {
                continue;
            }
            var free = sub.Id == Global.ProSySManagedSubId;
            var subtitle = free
                ? exhausted ? "حجم رایگان امروز تمام شده است" : $"سرویس رایگان روزانه · {ToFa(count.ToString(CultureInfo.InvariantCulture))} سرور"
                : $"{ToFa(count.ToString(CultureInfo.InvariantCulture))} سرور";
            list.Add(Make(sub.Id, free ? "ProSyS رایگان (۵۰۰ مگ روزانه)" : CleanName(sub.Remarks.IsNotEmpty() ? sub.Remarks : "اشتراک"),
                subtitle, free ? "Gift" : "CloudOutline", !(free && exhausted)));
        }

        var manual = profiles.Count(p => p.Subid.IsNullOrEmpty() || subs.All(s => s.Id != p.Subid));
        if (manual > 0)
        {
            list.Add(Make(ProSySQuickSource.Manual, "سرورهای افزوده‌شدهٔ دستی", $"{ToFa(manual.ToString(CultureInfo.InvariantCulture))} سرور", "ServerNetwork"));
        }
        if (current != null)
        {
            list.Add(Make(ProSySQuickSource.Current, "همین سرور فعلی، بدون تست", CleanName(current.Remarks.IsNotEmpty() ? current.Remarks : current.Address), "CheckCircleOutline",
                !(exhausted && Global.IsProSySManaged(current))));
        }
        return list;
    }

    private async Task<string?> AskSourceAsync(List<ProSySQuickSource> sources)
    {
        _picker?.TrySetResult(null);
        _picker = new TaskCompletionSource<string?>();
        QuickSources = sources;
        IsPickerOpen = true;
        var choice = await _picker.Task;
        if (choice != null)
        {
            _config.UiItem.ProSySQuickSource = choice;
            _config.UiItem.ProSySQuickRemember = RememberSource;
            await ConfigHandler.SaveConfig(_config);
        }
        UpdateSourceText(sources);
        return choice;
    }

    private void ClosePicker(string? choice)
    {
        IsPickerOpen = false;
        _picker?.TrySetResult(choice);
    }

    private async Task ChangeSourceAsync()
    {
        var sources = await BuildSourcesAsync(_config.UiItem.ProSySQuickSource);
        RememberSource = _config.UiItem.ProSySQuickRemember;
        await AskSourceAsync(sources);
    }

    private void UpdateSourceText(List<ProSySQuickSource>? sources = null)
    {
        var id = _config.UiItem.ProSySQuickSource;
        var title = sources?.FirstOrDefault(s => s.Id == id)?.Title;
        SourceText = _config.UiItem.ProSySQuickRemember && title != null
            ? $"منبع اتصال: {title}"
            : "منبع اتصال: هر بار بپرس";
    }

    /// <summary>Real-pings the servers of the chosen source and activates the fastest. False when none answered.</summary>
    private async Task<bool> PickFastestAsync(string sourceId, string title)
    {
        if (sourceId == ProSySQuickSource.Current)
        {
            return true;
        }
        var exhausted = ProSySFreeManager.Instance.Status?.Exhausted == true;
        if (sourceId == Global.ProSySManagedSubId || sourceId == ProSySQuickSource.All)
        {
            // Make sure the free servers are there (first run, or the hourly refresh is due).
            await ProSySFreeManager.Instance.SyncAsync(_config, false);
        }

        var profiles = await AppManager.Instance.ProfileItems(sourceId is ProSySQuickSource.All or ProSySQuickSource.Manual ? string.Empty : sourceId) ?? [];
        if (sourceId == ProSySQuickSource.Manual)
        {
            var subIds = (await AppManager.Instance.SubItems() ?? []).Select(s => s.Id).ToHashSet();
            profiles = profiles.Where(p => p.Subid.IsNullOrEmpty() || !subIds.Contains(p.Subid)).ToList();
        }
        if (exhausted)
        {
            profiles = profiles.Where(p => !Global.IsProSySManaged(p)).ToList();
        }
        if (profiles.Count == 0)
        {
            ErrorText = $"در «{title}» سروری برای تست وجود ندارد. اشتراک را در حالت پیشرفته به‌روزرسانی کنید.";
            return false;
        }

        ErrorText = string.Empty;
        ServerPing = $"در حال گرفتن پینگ {ToFa(profiles.Count.ToString(CultureInfo.InvariantCulture))} سرور از «{title}»…";
        var best = await ProSySFreeManager.Instance.SelectFastestAsync(_config, profiles, title);
        if (best == null)
        {
            ErrorText = $"هیچ‌کدام از سرورهای «{title}» پاسخ ندادند. منبع دیگری را انتخاب یا اشتراک را به‌روزرسانی کنید.";
            ApplyFreeStatus();
            return false;
        }
        // Let the main window reload the core with the new server before the proxy is switched on.
        await Task.Delay(800);
        await RefreshLocalStateAsync();
        ApplyFreeStatus();
        return true;
    }

    #endregion Quick connect source

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
        if (!manager.IsTesting || ServerPing.IsNullOrEmpty())
        {
            ServerPing = manager.IsTesting
                ? "در حال یافتن سریع‌ترین سرور…"
                : manager.BestDelayMs > 0
                    ? $"پینگ {ToFa(manager.BestDelayMs.ToString(CultureInfo.InvariantCulture))} میلی‌ثانیه · سریع‌ترین سرور{(manager.BestSourceName.IsNotEmpty() ? $" از «{manager.BestSourceName}»" : string.Empty)}"
                    : string.Empty;
        }
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
