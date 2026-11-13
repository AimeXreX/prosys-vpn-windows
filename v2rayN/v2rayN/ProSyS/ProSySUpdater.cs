using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace v2rayN.ProSyS;

/// <summary>A newer ProSyS release found on GitHub.</summary>
public sealed record ProSySRelease(string Tag, string Version, string PageUrl, string? SetupUrl, string? SetupName, string? ShaUrl);

/// <summary>
/// Self-update from this project's own GitHub releases (never from upstream v2rayN).
/// Installed copies download the matching Setup, check its SHA-256 and run it silently; the installer restarts the app.
/// Portable (zip) copies only get a link to the release page.
/// </summary>
public static class ProSySUpdater
{
    private const string Tag = "ProSySUpdater";
    private const string LatestApi = "https://api.github.com/repos/AimeXreX/prosys-vpn-windows/releases/latest";

    /// <summary>True when this copy was installed by Setup (it can update itself in place).</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(Utils.StartupPath(), "unins000.exe"));

    private static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    public static async Task<ProSySRelease?> CheckAsync(CancellationToken ct = default)
    {
        foreach (var viaProxy in new[] { false, true })
        {
            try
            {
                using var http = CreateClient(viaProxy, TimeSpan.FromSeconds(20));
                if (http == null)
                {
                    continue;
                }
                using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
                var tag = json?["tag_name"]?.GetValue<string>();
                if (tag.IsNullOrEmpty() || json?["draft"]?.GetValue<bool>() == true || json?["prerelease"]?.GetValue<bool>() == true)
                {
                    return null;
                }
                if (ProSySUtils.CompareVersions(tag, ProSySBrand.Version) <= 0)
                {
                    return null;
                }
                string? Asset(string name) => (json?["assets"] as JsonArray)?
                    .FirstOrDefault(a => a?["name"]?.GetValue<string>() == name)?["browser_download_url"]?.GetValue<string>();
                var version = tag!.TrimStart('v', 'V');
                var setupName = $"ProSySVPN-{version}-windows-{Arch}-setup.exe";
                return new ProSySRelease(tag, version, json?["html_url"]?.GetValue<string>() ?? $"{ProSySBrand.SourceUrl}/releases/latest",
                    Asset(setupName), setupName, Asset(setupName + ".sha256"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Logging.SaveLog($"{Tag}: check via {(viaProxy ? "proxy" : "direct")} failed: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>Downloads and verifies the Setup, then starts it. Returns false (with a reason) on failure.</summary>
    public static async Task<(bool ok, string? error)> DownloadAndInstallAsync(ProSySRelease release, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (release.SetupUrl.IsNullOrEmpty() || release.ShaUrl.IsNullOrEmpty())
        {
            return (false, "فایل نصب این نسخه پیدا نشد.");
        }
        var target = Path.Combine(Path.GetTempPath(), release.SetupName!);
        foreach (var viaProxy in new[] { false, true })
        {
            try
            {
                using var http = CreateClient(viaProxy, Timeout.InfiniteTimeSpan);
                if (http == null)
                {
                    continue;
                }
                var shaText = await http.GetStringAsync(release.ShaUrl, ct);
                var expected = shaText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();

                using var response = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using (var file = File.Create(target))
                await using (var stream = await response.Content.ReadAsStreamAsync(ct))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), ct);
                        done += read;
                        if (total > 0)
                        {
                            progress?.Report(done * 100.0 / total);
                        }
                    }
                }

                await using (var check = File.OpenRead(target))
                {
                    var actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant();
                    if (expected.IsNullOrEmpty() || actual != expected)
                    {
                        File.Delete(target);
                        return (false, "فایل دانلودشده سالم نبود (هش SHA-256 مطابقت ندارد).");
                    }
                }

                // The app is elevated, so Setup inherits that and can close and replace it. /prosysupdate=1 makes
                // the installer start the new version when it is done.
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    Arguments = "/SILENT /SP- /NOCANCEL /SUPPRESSMSGBOXES /NORESTART /prosysupdate=1",
                    UseShellExecute = true,
                });
                return (true, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Logging.SaveLog($"{Tag}: download via {(viaProxy ? "proxy" : "direct")} failed: {ex.Message}");
            }
        }
        return (false, "دانلود نسخهٔ جدید انجام نشد. اینترنت را بررسی و دوباره تلاش کنید.");
    }

    /// <summary>GitHub is often slow or filtered in Iran: try direct first, then through this app's own local proxy.</summary>
    private static HttpClient? CreateClient(bool viaProxy, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(15) };
        if (viaProxy)
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            if (port <= 0)
            {
                return null;
            }
            handler.Proxy = new WebProxy($"http://127.0.0.1:{port}");
            handler.UseProxy = true;
        }
        else
        {
            handler.UseProxy = false;
        }
        var http = new HttpClient(handler) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(ProSySBrand.UserAgent);
        return http;
    }
}
