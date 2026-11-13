using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.ProSyS;

/// <summary>ProSyS-specific behaviour: Iran-direct routing, DNS, subscription info, versions and per-app rules.</summary>
public class ProSySTests
{
    private static List<RulesItem> IranExceptRules() =>
        JsonUtils.Deserialize<List<RulesItem>>(ConfigHandler.ProSySIranRuleSet("all_except_ir.json")!)!;

    [Test]
    public async Task IranRuleSet_IsBundledAndMappedByFileName()
    {
        await ConfigHandler.ProSySIranRuleSet("https://x/v2rayN/all_except_ir.json").Should().NotBeNull();
        await ConfigHandler.ProSySIranRuleSet("https://x/v2rayN/all.json").Should().NotBeNull();
        await ConfigHandler.ProSySIranRuleSet("https://x/other.json").Should().BeNull();
        await ConfigHandler.ProSySIranRuleSet(null).Should().BeNull();
    }

    [Test]
    public async Task IranExceptRules_SendIranDirectBeforeTheCatchAllProxy()
    {
        var rules = IranExceptRules();
        var catchAll = rules.FindIndex(r => r.Port == "0-65535" && r.OutboundTag == Global.ProxyTag);
        var irDomains = rules.FindIndex(r => r.Domain?.Contains("geosite:ir") == true && r.OutboundTag == Global.DirectTag);
        var irIps = rules.FindIndex(r => r.Ip?.Contains("geoip:ir") == true && r.OutboundTag == Global.DirectTag);

        await catchAll.Should().BeEqualTo(rules.Count - 1);
        await (irDomains >= 0 && irDomains < catchAll).Should().BeTrue();
        await (irIps >= 0 && irIps < catchAll).Should().BeTrue();
    }

    [Test]
    public async Task IranExceptRules_DoNotSendPoisonableDnsDirect()
    {
        // 8.8.8.8 reached directly from Iran answers filtered sites with poisoned IPs.
        await IranExceptRules().Any(r => r.Ip?.Contains("8.8.8.8") == true).Should().BeFalse();
    }

    [Test]
    public async Task IranSimpleDns_UsesIranianResolverNotTheChinaDefault()
    {
        var dns = ConfigHandler.InitProSySIranSimpleDNS();
        await dns.DirectDNS.Should().BeEqualTo(Global.ProSySIranDirectDNS);
        await dns.DirectDNS!.Contains("119.29.29.29").Should().BeFalse();
        await dns.BootstrapDNS.Should().BeEqualTo(Global.ProSySIranBootstrapDNS);
        await dns.RemoteDNS!.StartsWith("https://").Should().BeTrue();
    }

    [Test]
    public async Task XrayConfig_DnsRulesComeBeforeTheIranCatchAll()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        config.SimpleDNSItem = ConfigHandler.InitProSySIranSimpleDNS();
        config.RoutingBasicItem.DomainStrategy = Global.IPOnDemand;
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray) with
        {
            SimpleDnsItem = config.SimpleDNSItem,
            RoutingItem = new RoutingItem
            {
                Id = "ir",
                Remarks = "IR-direct",
                RuleSet = ConfigHandler.ProSySIranRuleSet("all_except_ir.json")!,
                DomainStrategy = Global.IPOnDemand,
                DomainStrategy4Singbox = string.Empty,
            },
        };

        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();
        await result.Success.Should().BeTrue();
        var json = JsonNode.Parse(result.Data!.ToString()!)!;
        var rules = json["routing"]!["rules"]!.AsArray();

        int IndexOf(Func<JsonNode, bool> match) => rules.Select((r, i) => (r, i)).FirstOrDefault(x => x.r != null && match(x.r)).i;
        var hasInbound = rules.Any(r => r?["inboundTag"] != null);
        var firstDnsRule = IndexOf(r => r["inboundTag"] != null);
        var catchAll = IndexOf(r => r["port"]?.GetValue<string>() == "0-65535");

        await hasInbound.Should().BeTrue();
        await (firstDnsRule < catchAll).Should().BeTrue();

        var servers = json["dns"]!["servers"]!.AsArray().Select(s => s is JsonObject o ? o["address"]?.GetValue<string>() : s?.GetValue<string>()).ToList();
        await servers.Contains("78.157.42.100").Should().BeTrue();
        await servers.Contains("119.29.29.29").Should().BeFalse();
    }

    [Test]
    public async Task SingboxConfig_DirectAppsBypassTheTunnel()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        config.UiItem.ProSySDirectApps = ["game.exe", @"C:\Games\Launcher.exe"];
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.sing_box);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box);
        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();
        await result.Success.Should().BeTrue();

        var rules = JsonNode.Parse(result.Data!.ToString()!)!["route"]!["rules"]!.AsArray();
        var byName = rules.Any(r => r?["outbound"]?.GetValue<string>() == Global.DirectTag
            && r["process_name"]?.AsArray().Any(p => p?.GetValue<string>() == "game.exe") == true);
        var byPath = rules.Any(r => r?["outbound"]?.GetValue<string>() == Global.DirectTag
            && r["process_path"]?.AsArray().Any(p => p?.GetValue<string>() == @"C:\Games\Launcher.exe") == true);
        await byName.Should().BeTrue();
        await byPath.Should().BeTrue();
    }

    [Test]
    public async Task SubscriptionUserInfo_IsParsed()
    {
        var info = ProSySUtils.ParseSubscriptionUserInfo("upload=1073741824; download=2147483648; total=10737418240; expire=4102444800")!;
        await info.Should().NotBeNull();
        await info.Used.Should().BeEqualTo(3221225472L);
        await info.Remaining.Should().BeEqualTo(7516192768L);
        await info.HasExpire.Should().BeTrue();
        await info.IsEndingSoon(DateTimeOffset.FromUnixTimeSeconds(4102444800 - 86400)).Should().BeTrue();
        await info.IsEndingSoon(DateTimeOffset.FromUnixTimeSeconds(4102444800 - 30 * 86400)).Should().BeFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("hello world")]
    public async Task SubscriptionUserInfo_IgnoresMissingOrUnknownHeaders(string? header)
    {
        await ProSySUtils.ParseSubscriptionUserInfo(header).Should().BeNull();
    }

    [Test]
    public async Task SubscriptionUserInfo_UnlimitedVolumeIsNotEndingSoon()
    {
        var info = ProSySUtils.ParseSubscriptionUserInfo("upload=5; download=5; total=0; expire=0")!;
        await info.HasTotal.Should().BeFalse();
        await info.IsEndingSoon(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Test]
    [Arguments("v1.3.0", "1.2.0", 1)]
    [Arguments("1.2.0", "1.2.0", 0)]
    [Arguments("1.2", "1.2.0", 0)]
    [Arguments("v1.10.0", "1.9.9", 1)]
    [Arguments("1.2.0", "v1.2.1", -1)]
    [Arguments("1.3.0+abc", "1.3.0", 0)]
    public async Task CompareVersions_OrdersNumerically(string a, string b, int expected)
    {
        await Math.Sign(ProSySUtils.CompareVersions(a, b)).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task UdpNativeProtocols_ArePreferredInGamingMode()
    {
        await ProSySUtils.IsUdpNative(EConfigType.WireGuard).Should().BeTrue();
        await ProSySUtils.IsUdpNative(EConfigType.Hysteria2).Should().BeTrue();
        await ProSySUtils.IsUdpNative(EConfigType.TUIC).Should().BeTrue();
        await ProSySUtils.IsUdpNative(EConfigType.VLESS).Should().BeFalse();
    }
}
