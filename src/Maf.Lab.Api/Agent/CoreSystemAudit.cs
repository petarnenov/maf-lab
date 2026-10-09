using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Agent;

public sealed class CoreSystemAudit(ToolAudit audit) : ISystemAudit
{
    public Task RecordAsync(string kind, string operation, string arguments, string outcome, long durationMs, CancellationToken ct) =>
        audit.RecordAsync(new AuditEntry(new Principal("maf-lab-assistant", TenantId.Shared, Role.READ_ONLY),
            null, null, operation, arguments, outcome, durationMs, kind), ct);
}
