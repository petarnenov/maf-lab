using Maf.Lab.A2A;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace Maf.Lab.Plugins.A2A;

/// <summary>Snapshots the partner's registered tenants once, before the protocol dispatches or starts a worker.</summary>
public sealed class PartnerPluginAccess(IPartnerAccessor partners, IPluginAccess access,
    IOptions<A2AOptions> a2a, IOptions<AuthOptions> auth, IInstalledPlugins installed) : IPluginRouteAccess
{
    public string Plugin => A2APlugin.PluginName;
    private sealed record Scope(string PartnerId, IReadOnlyList<PartnerReader> Readers)
    {
        // The SDK's channel consumer saves events on a separate async flow. The request's selected owner must reach
        // that consumer before Submitted, without accepting any ownership field from a caller's message metadata.
        public ConcurrentDictionary<string, Principal> TaskOwners { get; } = new(StringComparer.Ordinal);
    }
    private sealed class ScopedResult(IResult inner, Scope scope) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            var previous = Ambient.Value;
            Ambient.Value = scope;
            try { await inner.ExecuteAsync(context); }
            finally { Ambient.Value = previous; }
        }
    }
    private static readonly AsyncLocal<Scope?> Ambient = new();
    internal static IReadOnlyList<PartnerReader>? Current => Ambient.Value?.Readers;
    internal static string? PartnerId => Ambient.Value?.PartnerId;
    internal static void SelectTask(string taskId, Principal principal)
    {
        if (Ambient.Value is { } scope && scope.Readers.Any(reader => reader.Principal == principal))
            scope.TaskOwners.TryAdd(taskId, principal);
    }
    internal static string? TaskTenant(string taskId) =>
        Ambient.Value?.TaskOwners.TryGetValue(taskId, out var owner) == true ? owner.TenantId.Value : null;

    internal static IQueryable<A2ATaskRow> VisibleTasks(IQueryable<A2ATaskRow> query, IInstalledPlugins installed)
    {
        if (Ambient.Value is not { } scope) return query;
        var tenants = scope.Readers.Where(reader => AssistantAgentCard.HasBilling(installed, [reader]))
            .Select(reader => reader.Principal.TenantId.Value).ToArray();
        var ownedIds = scope.TaskOwners.Keys.ToArray();
        var ownedTenants = scope.TaskOwners.Values.Select(owner => owner.TenantId.Value).ToArray();
        return query.Where(task => task.PartnerId == scope.PartnerId && task.TenantId != null
            && (tenants.Contains(task.TenantId) || (ownedIds.Contains(task.Id) && ownedTenants.Contains(task.TenantId))));
    }

    internal static async Task<IReadOnlyList<PartnerReader>> CaptureAsync(PartnerPrincipal partner, IPluginAccess access,
        CancellationToken ct)
    {
        var readers = new List<PartnerReader>();
        foreach (var tenant in partner.AllowedFirms)
        {
            var principal = new Principal($"a2a:{tenant.Value}", tenant, Role.READ_ONLY);
            var snapshot = await access.For(principal, ct);
            if (snapshot.IsInUse(A2APlugin.PluginName)) readers.Add(new(principal, snapshot));
        }
        return readers;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        // Discovery and client-credentials issuance are public by the A2A security scheme. No other route is public.
        if (http.Request.Path == AgentCardFactory.WellKnownPath || http.Request.Path == AgentCardFactory.TokenPath)
            return await next(context);
        if (!http.Request.Path.StartsWithSegments(AgentCardFactory.A2APath))
            return PluginAccessContext.Current?.IsInUse(Plugin) == true ? await next(context) : Results.NotFound();

        var partner = partners.Current;
        var readers = await CaptureAsync(partner, access, http.RequestAborted);
        if (readers.Count == 0) return Results.NotFound();
        var previous = Ambient.Value;
        Ambient.Value = new(partner.PartnerId, readers);
        try
        {
            // The SDK freezes its HTTP card at mapping time. Render this protocol route from the same request snapshot
            // as the authenticated extended card, including after an allowance changes or the installed set refreshes.
            if (http.Request.Path == "/a2a/card")
                return Results.Json(AgentCardFactory.Signed(AgentCardFactory.Public(a2a.Value,
                    AssistantAgentCard.ForReaders(installed, readers)), auth.Value), global::A2A.A2AJsonUtilities.DefaultOptions);
            var result = await next(context);
            return result is IResult executable ? new ScopedResult(executable, Ambient.Value!) : result;
        }
        finally { Ambient.Value = previous; }
    }
}

internal sealed record PartnerReader(Principal Principal, PluginAccessSnapshot Access);
