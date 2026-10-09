using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

public sealed class PluginAdministrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ApiFactory Factory() => new(ApiFactory.ProceduralModel())
    {
        BootstrapTenantPlugins = false,
        InstalledPlugins = [.. StandInDomains.Installed,
            Plugins.FixturePlugin.Manifest(),
            StandInDomains.BillingManifest with { Name = "private-weather", PrivateTo = "firm-a" }],
    };

    [Fact]
    public async Task Usage_and_permission_audit_expose_only_the_principals_tenant_without_message_content()
    {
        using var api = Factory();
        var tenant = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        var platform = api.ClientFor("operator", "firm-a", Role.PLATFORM_ADMIN);
        await platform.PutAsJsonAsync($"/api/platform/plugins/{StandInDomains.BillingManifest.Name}", new { allowed = true }, Ct);
        await api.ClientFor("operator", "firm-b", Role.PLATFORM_ADMIN).PutAsJsonAsync($"/api/platform/plugins/{StandInDomains.PortfolioManifest.Name}", new { allowed = true }, Ct);
        await using var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var now = DateTime.UtcNow;
        foreach (var (id, scope, user, date) in new[] { ("a1", "firm-a", "alice", now), ("a2", "firm-a", "alice", now), ("a3", "firm-a", "rita", now), ("b1", "firm-b", "bob", now), ("old", "firm-a", "rita", now.AddDays(-31)) })
            db.Turns.Add(new TurnRow { Id = id, ConversationId = "fixture", UserId = user, TenantId = scope, Question = "secret question", Answer = "secret answer", CreatedAt = date,
                RecordJson = id == "a3" ? "[{\"kind\":5}]" : "[{\"kind\":\"turn.end\",\"data\":{\"domainsTouched\":[\"weather\",\"weather\"]}}]" });
        await db.SaveChangesAsync(Ct);
        var usage = await tenant.GetFromJsonAsync<JsonElement>("/api/admin/usage", Ct);
        Assert.Equal(3, usage.GetProperty("turns").GetInt32());
        Assert.Equal(2, usage.GetProperty("activeUsers").GetInt32());
        Assert.Equal(2, usage.GetProperty("domains").GetProperty("weather").GetInt32());
        Assert.DoesNotContain("secret", usage.GetRawText());
        Assert.DoesNotContain("alice", usage.GetRawText());
        var audit = await platform.GetFromJsonAsync<JsonElement>("/api/platform/plugin-audit", Ct);
        var action = Assert.Single(audit.GetProperty("actions").EnumerateArray());
        Assert.Contains(StandInDomains.BillingManifest.Name, action.GetProperty("arguments").GetString());
        Assert.DoesNotContain(StandInDomains.PortfolioManifest.Name, audit.GetRawText());
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.GetAsync("/api/platform/plugin-audit", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/admin/usage", Ct)).StatusCode);
    }

    [Fact]
    public async Task Operator_and_tenant_use_the_same_atomic_rows_without_dashboard_shells()
    {
        using var api = Factory();
        var platform = api.ClientFor("operator", "firm-a", Role.PLATFORM_ADMIN);
        var tenant = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        var other = api.ClientFor("bob", "firm-b", Role.TENANT_ADMIN);
        var plugin = StandInDomains.PortfolioManifest.Name;
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.PutAsJsonAsync($"/api/admin/plugins/{plugin}", new { enabled = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await platform.PutAsJsonAsync($"/api/platform/plugins/{plugin}", new { allowed = true }, Ct)).StatusCode);
        var offered = await tenant.GetFromJsonAsync<JsonElement>("/api/admin/plugins", Ct);
        Assert.Equal(plugin, Assert.Single(offered.EnumerateArray()).GetProperty("name").GetString());
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/api/admin/plugins", Ct)).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await tenant.PutAsJsonAsync($"/api/admin/plugins/{plugin}", new { enabled = true }, Ct)).StatusCode);
        Assert.Contains((await tenant.GetFromJsonAsync<JsonElement>("/api/plugins", Ct)).GetProperty("plugins").EnumerateArray(), p => p.GetProperty("name").GetString() == plugin);
        await using var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        db.Conversations.Add(new ConversationRow { Id = "kept", UserId = "alice", TenantId = "firm-a" });
        await db.SaveChangesAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, (await tenant.PutAsJsonAsync($"/api/admin/plugins/{plugin}", new { enabled = false }, Ct)).StatusCode);
        Assert.DoesNotContain((await tenant.GetFromJsonAsync<JsonElement>("/api/plugins", Ct)).GetProperty("plugins").EnumerateArray(), p => p.GetProperty("name").GetString() == plugin);
        Assert.True(await db.Conversations.AnyAsync(row => row.Id == "kept", Ct));
        await tenant.PutAsJsonAsync($"/api/admin/plugins/{plugin}", new { enabled = true }, Ct);
        await platform.PutAsJsonAsync($"/api/platform/plugins/{plugin}", new { allowed = false }, Ct);
        var row = await db.PluginEntitlements.AsNoTracking().SingleAsync(p => p.Plugin == plugin, Ct);
        Assert.False(row.Allowed);
        Assert.False(row.Enabled);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.PutAsJsonAsync($"/api/admin/plugins/{plugin}", new { enabled = true }, Ct)).StatusCode);
        Assert.True(await db.Conversations.AnyAsync(c => c.Id == "kept", Ct));
        var audit = await db.Audit.Where(a => a.Kind == "plugin.allowance" || a.Kind == "plugin.enablement").ToListAsync(Ct);
        Assert.Equal(5, audit.Count);
        Assert.All(audit, a => Assert.Equal("firm-a", a.TenantId));
        Assert.Contains(audit, a => a.PrincipalId == "operator" && a.Kind == "plugin.allowance");
        Assert.Contains(audit, a => a.PrincipalId == "alice" && a.Kind == "plugin.enablement");
    }

    [Theory]
    [InlineData(Role.USER)]
    [InlineData(Role.READ_ONLY)]
    [InlineData(Role.TENANT_ADMIN)]
    public async Task Platform_reads_and_writes_refuse_non_operator_roles(Role role)
    {
        using var api = Factory();
        var client = api.ClientFor("person", "firm-a", role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/platform/plugins", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/platform/plugins/{StandInDomains.BillingManifest.Name}", new { allowed = true }, Ct)).StatusCode);
        await using var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        Assert.Empty(await db.PluginEntitlements.ToListAsync(Ct));
        Assert.Empty(await db.Audit.ToListAsync(Ct));
    }

    [Fact]
    public async Task Private_plugins_and_installation_plugins_cannot_be_switched_for_another_organization()
    {
        using var api = Factory();
        var a = api.ClientFor("operator", "firm-a", Role.PLATFORM_ADMIN);
        var b = api.ClientFor("operator", "firm-b", Role.PLATFORM_ADMIN);
        var first = await a.GetFromJsonAsync<JsonElement>("/api/platform/plugins", Ct);
        var second = await b.GetFromJsonAsync<JsonElement>("/api/platform/plugins", Ct);
        Assert.Contains(first.EnumerateArray(), p => p.GetProperty("name").GetString() == "private-weather" && p.GetProperty("private").GetBoolean());
        Assert.DoesNotContain(second.EnumerateArray(), p => p.GetProperty("name").GetString() == "private-weather");
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync("/api/platform/plugins/private-weather", new { allowed = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PutAsJsonAsync($"/api/platform/plugins/{Plugins.FixturePlugin.PluginName}", new { allowed = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PutAsJsonAsync("/api/platform/plugins/missing", new { allowed = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/platform/plugins", Ct)).StatusCode);
    }

    [Fact]
    public async Task Request_tenant_fields_cannot_change_the_principals_organization_and_repeating_is_idempotent()
    {
        using var api = Factory();
        var platform = api.ClientFor("operator", "firm-a", Role.PLATFORM_ADMIN);
        var plugin = StandInDomains.PortfolioManifest.Name;
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await platform.PutAsJsonAsync($"/api/platform/plugins/{plugin}?tenant=firm-b", new { allowed = true, enabled = true, tenantId = "firm-b" }, Ct)).StatusCode);
        await using var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal("firm-a", (await db.PluginEntitlements.SingleAsync(Ct)).TenantId);
        Assert.Single(await db.Audit.Where(row => row.Kind == "plugin.allowance").ToListAsync(Ct));
    }
}
