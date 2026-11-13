namespace ServiceLib.Events;

public static class AppEvents
{
    public static readonly EventChannel<RxVoid> AddServerViaClipboardRequested = new();
    public static readonly EventChannel<bool> HasUpdateNotified = new();

    public static readonly EventChannel<ServerSpeedItem> DispatcherStatisticsRequested = new();

    public static readonly EventChannel<string> SendSnackMsgRequested = new();
    public static readonly EventChannel<string> SendMsgViewRequested = new();

    public static readonly EventChannel<RxVoid> AppExitRequested = new();
    public static readonly EventChannel<bool> ShutdownRequested = new();

    public static readonly EventChannel<ESysProxyType> SysProxyChangeRequested = new();

    /// <summary>ProSyS: managed free profiles changed; refresh the server list and reload the core.</summary>
    public static readonly EventChannel<RxVoid> ProSySProfilesChanged = new();
}
