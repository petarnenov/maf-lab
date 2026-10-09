using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

public sealed class TenantPluginGatingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static Principal Operator(string scope) => new("operator", TenantId.Firm(scope), Role.PLATFORM_ADMIN);

    private static ApiFactory Factory(FakeToolSource? tools = null) => new(ApiFactory.ProceduralModel(), tools)
    {
        BootstrapTenantPlugins = false,
        InstalledPlugins = [.. StandInDomains.Installed, Plugins.FixturePlugin.Manifest() with { Scope = PluginScopes.Tenant }],
    };

    [Fact]
    public async Task Two_tenants_see_only_their_enabled_plugins_domains_cards_and_routes()
    {
        using var api = Factory();
        var a = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        var b = api.ClientFor("bob", "firm-b", Role.TENANT_ADMIN);
        var store = api.Services.GetRequiredService<IPluginEntitlements>();
        await store.AllowAsync(Operator("firm-a"), StandInDomains.PortfolioManifest.Name, true, true, Ct);
        await store.AllowAsync(Operator("firm-a"), Plugins.FixturePlugin.PluginName, true, true, Ct);
        await store.AllowAsync(Operator("firm-b"), StandInDomains.BillingManifest.Name, true, true, Ct);
        var first = await a.GetFromJsonAsync<JsonElement>("/api/plugins", Ct);
        var second = await b.GetFromJsonAsync<JsonElement>("/api/plugins", Ct);
        Assert.Equal(["portfolio"], first.GetProperty("domains").EnumerateArray().Select(d => d.GetProperty("id").GetString()));
        Assert.NotEmpty(first.GetProperty("plugins").EnumerateArray().Single(p => p.GetProperty("name").GetString() == StandInDomains.PortfolioManifest.Name).GetProperty("cardTypes").EnumerateArray());
        Assert.Equal(["billing"], second.GetProperty("domains").EnumerateArray().Select(d => d.GetProperty("id").GetString()));
        Assert.DoesNotContain(second.GetProperty("plugins").EnumerateArray(), p => p.GetProperty("name").GetString() == StandInDomains.PortfolioManifest.Name);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/fixture/ping", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/fixture/ping", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync("/api/fixture/ping", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_disabled_domain_is_not_a_routing_question_or_a_tool_domain()
    {
        using var api = Factory(new FakeToolSource { WithPortfolio = true });
        var client = api.ClientFor("adam", "firm-b", Role.USER);
        await api.Services.GetRequiredService<IPluginEntitlements>().AllowAsync(Operator("firm-b"), StandInDomains.BillingManifest.Name, true, true, Ct);
        await ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing");
        Assert.NotEmpty(api.Jev.Requests);
        foreach (var request in api.Jev.Requests)
        {
            using var body = JsonDocument.Parse(request.Body);
            Assert.False(body.RootElement.GetProperty("questions").TryGetProperty("in_portfolio", out _));
        }
        Assert.All(api.Tools.RequestedDomains.Where(d => d is not null), d => Assert.DoesNotContain("portfolio", d!));
        Assert.DoesNotContain("search_portfolio_documents", api.Tools.Invocations);
    }

    [Fact]
    public async Task A_change_during_a_turn_does_not_split_the_turn_and_applies_to_the_next_one()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new FakeToolSource { BeforeSearchExecutes = async () => { entered.TrySetResult(); await release.Task.WaitAsync(Ct); } };
        using var api = Factory(tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);
        var store = api.Services.GetRequiredService<IPluginEntitlements>();
        await store.AllowAsync(Operator("firm-a"), StandInDomains.BillingManifest.Name, true, true, Ct);
        var pending = ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await store.AllowAsync(Operator("firm-a"), StandInDomains.BillingManifest.Name, false, false, Ct);
        release.TrySetResult();
        var first = await pending;
        Assert.Contains("search_documents", tools.Invocations);
        Assert.DoesNotContain("No domain", ApiFactory.AnswerOf(first), StringComparison.OrdinalIgnoreCase);
        var calls = api.Jev.Requests.Count;
        var second = await ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing");
        Assert.Equal(calls, api.Jev.Requests.Count);
        Assert.Single(tools.Invocations, t => t == "search_documents");
        Assert.Equal(NoDomain.ReplyEnglish, ApiFactory.AnswerOf(second));
    }

    [Fact]
    public async Task Injected_and_static_domain_readers_share_a_tenants_frozen_scope()
    {
        using var api = Factory();
        _ = api.ClientFor("adam", "firm-a", Role.USER);
        var principal = new Principal("adam", TenantId.Firm("firm-a"), Role.USER);
        await api.Services.GetRequiredService<IPluginEntitlements>().AllowAsync(Operator("firm-a"), StandInDomains.PortfolioManifest.Name, true, true, Ct);
        var access = await api.Services.GetRequiredService<IPluginAccess>().For(principal, Ct);
        var host = api.Services.GetRequiredService<DomainCatalogue>();
        using (PluginAccessContext.Use(principal, access))
        using (DomainCatalogue.Use(host.For(access)))
        {
            Assert.Equal(["portfolio"], host.Ids);
            Assert.Equal(host.Ids, DomainCatalogue.Current.Ids);
            Assert.True(PluginAccessContext.For(principal)!.IsInUse(StandInDomains.PortfolioManifest.Name));
        }
        Assert.Null(PluginAccessContext.Current);
    }
    [Fact]
    public async Task System_prompts_are_assembled_only_from_the_callers_domains()
    {
        var manifests = StandInDomains.Installed.Select(m => m with { Domain = m.Domain! with { Prompt = "prompt.md" } }).ToArray();
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { BootstrapTenantPlugins = false, InstalledPlugins = manifests };
        _ = api.ClientFor("adam", "firm-a", Role.USER);
        foreach (var manifest in manifests)
        {
            var directory = Path.Combine(api.PluginsRoot, manifest.Name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "prompt.md"), $"<!-- summary -->{manifest.Name}-SUMMARY<!-- scope -->{manifest.Name}-SCOPE<!-- tools -->{manifest.Name}-TOOLS");
        }
        var store = api.Services.GetRequiredService<IPluginEntitlements>();
        await store.AllowAsync(Operator("firm-a"), StandInDomains.PortfolioManifest.Name, true, true, Ct);
        await store.AllowAsync(Operator("firm-b"), StandInDomains.BillingManifest.Name, true, true, Ct);
        var access = api.Services.GetRequiredService<IPluginAccess>();
        var domains = api.Services.GetRequiredService<DomainCatalogue>();
        var prompt = api.Services.GetRequiredService<SystemPrompt>();
        foreach (var (scope, included, excluded) in new[] {
            ("firm-a", StandInDomains.PortfolioManifest.Name, StandInDomains.BillingManifest.Name),
            ("firm-b", StandInDomains.BillingManifest.Name, StandInDomains.PortfolioManifest.Name),
        })
        {
            var principal = new Principal("adam", TenantId.Firm(scope), Role.USER);
            using var permissions = PluginAccessContext.Use(principal, await access.For(principal, Ct));
            using var context = DomainCatalogue.Use(domains.For(PluginAccessContext.Current!));
            Assert.Contains(included + "-TOOLS", prompt.Text);
            Assert.DoesNotContain(excluded + "-TOOLS", prompt.Text);
        }
    }

}
