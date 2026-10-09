using Maf.Lab.Api.Plugins;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tracing;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Maf.Lab.Api.Compliance;

namespace Maf.Lab.Api.Endpoints;

/// <summary>Permission controls remain available to the terminal when dashboard shells are not installed.</summary>
public static class PluginAdministrationEndpoints
{
    public sealed record EnableRequest(bool Enabled);
    public sealed record AllowRequest(bool Allowed, bool Enabled = false);
    public sealed record AdminPlugin(string Name, string Description, string Scope, string Health,
        IReadOnlyList<string> Environments, bool Private, bool Allowed, bool Enabled);
    public sealed record OperatorEntry(long Id, DateTimeOffset At, string OperatorId);
    public sealed record OperatorEntryPage(IReadOnlyList<OperatorEntry> Actions, long? NextCursor);

    public static IEndpointRouteBuilder MapPluginAdministration(this IEndpointRouteBuilder app)
    {
        var tenant = app.MapGroup("/api/admin/plugins").RequireAuthorization(PolicyNames.TenantAdmin);
        tenant.WithMetadata(OperatorConfigurationAccess.Instance);
        tenant.MapGet("", (IPrincipalAccessor principals, PluginCatalogue catalogue, IPluginEntitlements permissions,
            PluginHealth health, CancellationToken ct) => ListAsync(principals.Current, catalogue, permissions, health, onlyAllowed: true, ct));
        tenant.MapPut("/{plugin}", async (string plugin, EnableRequest request, IPrincipalAccessor principals,
            IPluginEntitlements permissions, CancellationToken ct) =>
            Result(await permissions.EnableAsync(principals.Current, plugin, request.Enabled, ct)));
        app.MapGet("/api/admin/operator-audit", async (long? before, IPrincipalAccessor principals,
            IDbContextFactory<MafDbContext> database, CancellationToken ct) =>
        {
            var tenantId = principals.Current.TenantId.Value;
            await using var db = await database.CreateDbContextAsync(ct);
            var rows = await db.Audit.AsNoTracking().Where(row => row.TenantId == tenantId
                && row.Kind == AuditKinds.OperatorEnter && (!before.HasValue || row.Id < before.Value))
                .OrderByDescending(row => row.Id).Take(51).Select(row => new { row.Id, row.At, row.PrincipalId }).ToListAsync(ct);
            var actions = rows.Take(50).Select(row => new OperatorEntry(row.Id,
                new DateTimeOffset(DateTime.SpecifyKind(row.At, DateTimeKind.Utc)), row.PrincipalId)).ToList();
            return Results.Ok(new OperatorEntryPage(actions, rows.Count > 50 ? rows[49].Id : null));
        }).RequireAuthorization(PolicyNames.TenantAdmin);

        var platform = app.MapGroup("/api/platform/plugins").RequireAuthorization(PolicyNames.PlatformAdmin);
        platform.WithMetadata(OperatorConfigurationAccess.Instance);
        platform.MapGet("", (IPrincipalAccessor principals, PluginCatalogue catalogue, IPluginEntitlements permissions,
            PluginHealth health, CancellationToken ct) => ListAsync(principals.Current, catalogue, permissions, health, onlyAllowed: false, ct));
        platform.MapPut("/{plugin}", async (string plugin, AllowRequest request, IPrincipalAccessor principals,
            IPluginEntitlements permissions, CancellationToken ct) =>
            Result(await permissions.AllowAsync(principals.Current, plugin, request.Allowed, request.Enabled, ct)));
        app.MapGet("/api/admin/usage", async (IPrincipalAccessor principals, IDbContextFactory<MafDbContext> database,
            TimeProvider clock, CancellationToken ct) =>
        {
            var since = clock.GetUtcNow().AddDays(-30).UtcDateTime;
            var tenantId = principals.Current.TenantId.Value;
            await using var db = await database.CreateDbContextAsync(ct);
            var turns = db.Turns.AsNoTracking().Where(t => t.TenantId == tenantId && t.CreatedAt >= since);
            var users = await turns.Select(t => t.UserId).Distinct().CountAsync(ct);
            var records = await turns.Select(t => t.RecordJson).ToListAsync(ct);
            var domains = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                try
                {
                    using var json = JsonDocument.Parse(record);
                    if (json.RootElement.ValueKind != JsonValueKind.Array) continue;
                    var touched = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var step in json.RootElement.EnumerateArray())
                        if (step.ValueKind == JsonValueKind.Object && step.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == TraceKinds.TurnEnd
                            && step.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                            && data.TryGetProperty("domainsTouched", out var names) && names.ValueKind == JsonValueKind.Array)
                            foreach (var name in names.EnumerateArray())
                                if (name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } value) touched.Add(value);
                    foreach (var domain in touched) domains[domain] = domains.GetValueOrDefault(domain) + 1;
                }
                catch (JsonException) { /* Older records without structured events still count as turns. */ }
            }
            return Results.Ok(new { from = since, turns = records.Count, activeUsers = users, domains });
        }).RequireAuthorization(PolicyNames.TenantAdmin);
        app.MapGet("/api/platform/plugin-audit", async (long? before, IPrincipalAccessor principals,
            IDbContextFactory<MafDbContext> database, CancellationToken ct) =>
        {
            var tenantId = principals.Current.TenantId.Value;
            await using var db = await database.CreateDbContextAsync(ct);
            var rows = await db.Audit.AsNoTracking().Where(row => row.TenantId == tenantId
                && (row.Kind == "plugin.allowance" || row.Kind == "plugin.enablement") && (!before.HasValue || row.Id < before.Value))
                .OrderByDescending(row => row.Id).Take(51)
                .Select(row => new { row.Id, row.At, actor = row.PrincipalId, action = row.Kind, row.Arguments, row.Outcome }).ToListAsync(ct);
            return Results.Ok(new { actions = rows.Take(50), nextCursor = rows.Count > 50 ? (long?)rows[49].Id : null });
        }).RequireAuthorization(PolicyNames.PlatformAdmin).WithMetadata(OperatorConfigurationAccess.Instance);
        return app;
    }

    private static async Task<IResult> ListAsync(Principal principal, PluginCatalogue catalogue, IPluginEntitlements permissions,
        PluginHealth health, bool onlyAllowed, CancellationToken ct)
    {
        var rows = (await permissions.ReadAsync(principal, ct)).ToDictionary(row => row.Plugin, StringComparer.Ordinal);
        var offered = catalogue.Current.Plugins.Where(plugin =>
            (plugin.Manifest.PrivateTo is null || plugin.Manifest.PrivateTo == principal.TenantId.Value)
            && (!onlyAllowed || plugin.Manifest.Scope == PluginScopes.Tenant && rows.GetValueOrDefault(plugin.Name)?.Allowed == true));
        var items = await Task.WhenAll(offered.Select(async plugin => new AdminPlugin(plugin.Name, plugin.Manifest.Description,
            plugin.Manifest.Scope, await health.GetAsync(plugin, ct), plugin.Manifest.Environments,
            plugin.Manifest.PrivateTo is not null, rows.GetValueOrDefault(plugin.Name)?.Allowed == true,
            rows.GetValueOrDefault(plugin.Name)?.Enabled == true)));
        return Results.Ok(items);
    }

    private static IResult Result(PluginChange change) => change.Status switch
    {
        PluginChangeStatus.Changed or PluginChangeStatus.Unchanged => Results.Ok(change.Entitlement),
        PluginChangeStatus.NotInstalled or PluginChangeStatus.PrivateToAnotherTenant => Results.NotFound(),
        PluginChangeStatus.NotTenantScoped => Results.Conflict(new { error = "Installation plugins have no tenant switch." }),
        _ => Results.Forbid(),
    };
}
