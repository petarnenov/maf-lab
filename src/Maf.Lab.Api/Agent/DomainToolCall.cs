using System.Text.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// A domain's tool called for a principal through the core's tool source (extract-a2a): the token is minted for the
/// principal, the tool is found among the domain's offered tools, and its result is read as every tool result is read —
/// its structured content, unless it is an error. Null for "absent or unavailable"; never throws for a server that cannot
/// answer.
/// </summary>
public sealed class DomainToolCall(IToolSource tools, IOptions<AuthOptions> auth, ILogger<DomainToolCall> logger, DomainCatalogue domains, IPluginAccess access) : IDomainToolCall
{
    public async Task<JsonElement?> CallAsync(Principal principal, string domain, string tool, IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct)
    {
        var snapshot = PluginAccessContext.For(principal) ?? await access.For(principal, ct);
        using var permissionScope = PluginAccessContext.Use(principal, snapshot);
        var permitted = domains.For(snapshot);
        using var domainScope = DomainCatalogue.Use(permitted);
        if (permitted.Get(domain) is null) return null;
        var (token, _) = DevJwt.Issue(auth.Value, principal.UserId, principal.TenantId, principal.Role);
        try
        {
            await using var toolSet = await tools.GetToolsAsync(token, null, ct, new HashSet<string>(StringComparer.Ordinal) { domain });
            if (toolSet.Tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == tool) is not { } found)
            {
                return null;
            }
            var (_, structured, isError) = ToolDataEnvelope.Unpack(await found.InvokeAsync(new AIFunctionArguments(arguments.ToDictionary()), ct));
            return isError || structured is not { } content ? null : content.Clone();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("domain tool {Domain}/{Tool} could not answer ({Error})", domain, tool, ex.GetType().Name);
            return null;
        }
    }
}

/// <summary>The audit record of protocol activity with an explicit actor, over the core's audit chain (extract-a2a).</summary>
public sealed class ActivityAudit(ToolAudit audit) : IActivityAudit
{
    public Task RecordAsync(Principal actor, string kind, string action, string identifiers, string outcome, long elapsedMs,
        CancellationToken ct) =>
        audit.RecordAsync(new AuditEntry(actor, null, null, action, identifiers, outcome, elapsedMs, kind), ct);
}
