using System.Data;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Plugins;

/// <summary>Atomic permission switches and audit, under SQLite's shared write transaction.</summary>
public sealed class PluginEntitlements(IDbContextFactory<MafDbContext> db, PluginCatalogue installed,
    IPluginAccessChanges changes, ToolAudit audit, TimeProvider time) : IPluginEntitlements
{
    public async Task<IReadOnlyList<PluginEntitlement>> ReadAsync(Principal principal, CancellationToken ct)
    {
        if (principal.TenantId.IsShared || string.IsNullOrWhiteSpace(principal.TenantId.Value)) return [];
        await using var context = await db.CreateDbContextAsync(ct);
        return await context.PluginEntitlements.AsNoTracking().Where(row => row.TenantId == principal.TenantId.Value)
            .OrderBy(row => row.Plugin).Select(row => new PluginEntitlement(row.Plugin, row.Allowed, row.Enabled)).ToListAsync(ct);
    }

    public Task<PluginChange> AllowAsync(Principal principal, string plugin, bool allowed, bool enable, CancellationToken ct) =>
        ChangeAsync(principal, plugin, allowed, enable, operatorWrite: true, ct);

    public Task<PluginChange> EnableAsync(Principal principal, string plugin, bool enabled, CancellationToken ct) =>
        ChangeAsync(principal, plugin, null, enabled, operatorWrite: false, ct);

    private async Task<PluginChange> ChangeAsync(Principal principal, string plugin, bool? allowed, bool enable, bool operatorWrite, CancellationToken ct)
    {
        if (principal.TenantId.IsShared || string.IsNullOrWhiteSpace(principal.TenantId.Value)
            || (operatorWrite ? !principal.IsPlatformAdmin : !principal.IsTenantAdmin)) return new(PluginChangeStatus.Forbidden);
        var manifest = installed.Current.Plugins.FirstOrDefault(p => p.Name == plugin)?.Manifest;
        if (manifest is null) return new(PluginChangeStatus.NotInstalled);
        if (manifest.Scope != PluginScopes.Tenant) return new(PluginChangeStatus.NotTenantScoped);
        if (manifest.PrivateTo is not null && manifest.PrivateTo != principal.TenantId.Value) return new(PluginChangeStatus.PrivateToAnotherTenant);
        await using var context = await db.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await context.PluginEntitlements.SingleOrDefaultAsync(r => r.TenantId == principal.TenantId.Value && r.Plugin == plugin, ct);
        if (!operatorWrite && row?.Allowed != true) return new(PluginChangeStatus.NotAllowed);
        var nextAllowed = operatorWrite ? allowed!.Value : row!.Allowed;
        var nextEnabled = nextAllowed && (operatorWrite ? enable || row?.Enabled == true : enable);
        if (row is null && !nextAllowed || row is not null && row.Allowed == nextAllowed && row.Enabled == nextEnabled)
            return new(PluginChangeStatus.Unchanged, new(plugin, nextAllowed, nextEnabled));
        if (row is null)
        {
            row = new PluginEntitlementRow { TenantId = principal.TenantId.Value, Plugin = plugin, ChangedBy = principal.UserId };
            context.PluginEntitlements.Add(row);
        }
        row.Allowed = nextAllowed;
        row.Enabled = nextEnabled;
        row.ChangedAt = time.GetUtcNow().UtcDateTime;
        row.ChangedBy = principal.UserId;
        var action = operatorWrite ? "plugin.allowance" : "plugin.enablement";
        await audit.RecordInTransactionAsync(context, new AuditEntry(principal, null, null, action,
            $"plugin={plugin},allowed={nextAllowed},enabled={nextEnabled}", "changed", 0, action), ct);
        await transaction.CommitAsync(ct);
        // Once committed, cancellation cannot suppress local notification. Redis delivery failure is TTL bounded.
        await changes.PublishAsync(principal.TenantId);
        return new(PluginChangeStatus.Changed, new(plugin, nextAllowed, nextEnabled));
    }
}
