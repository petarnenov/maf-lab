using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// The chain is what makes the record evidence: a changed row, a removed row or a replaced row must be detectable,
/// and rows written before chaining began must be reported rather than rewritten.
/// </summary>
public class AuditChainTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Principal Adam => new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, []);

    private static AuditEntry Entry(string action, string kind = AuditKinds.Tool, string args = "") =>
        new(Adam, "c_1", "t_1", action, args, "ok", 5, kind);

    private static async Task<List<AuditRow>> RowsAsync(ApiFactory api)
    {
        await using var db = ChatApiTests.Db(api);
        return await db.Audit.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
    }

    [Fact]
    public async Task Three_actions_form_a_chain()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var audit = api.Services.GetRequiredService<ToolAudit>();

        await audit.RecordAsync(Entry("search_documents"), Ct);
        await audit.RecordAsync(Entry("conversation.delete", AuditKinds.ConversationDelete), Ct);
        var head = await audit.RecordAsync(Entry("get_billing_run_status", args: "runId=4417"), Ct);

        var rows = await RowsAsync(api);
        Assert.Equal(3, rows.Count);
        Assert.Null(rows[0].PreviousHash);
        Assert.Equal(rows[0].Hash, rows[1].PreviousHash);
        Assert.Equal(rows[1].Hash, rows[2].PreviousHash);
        Assert.Equal(head, rows[2].Hash);
        Assert.Equal(head, await audit.HeadAsync(Ct));

        var report = AuditChain.Verify(rows);
        Assert.True(report.Intact);
        Assert.Equal(3, report.Checked);
        Assert.Equal(head, report.Head);
        Assert.Null(report.FirstBrokenId);
    }

    [Fact]
    public async Task Kinds_share_one_ordered_record()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var audit = api.Services.GetRequiredService<ToolAudit>();

        await audit.RecordAsync(Entry("search_documents"), Ct);
        await audit.RecordAsync(Entry("conversation.delete", AuditKinds.ConversationDelete), Ct);
        await audit.RecordAsync(Entry("compliance.export", AuditKinds.ComplianceExport, "from=2026-09-01 to=2026-09-30"), Ct);

        var rows = await RowsAsync(api);
        Assert.Equal([AuditKinds.Tool, AuditKinds.ConversationDelete, AuditKinds.ComplianceExport], rows.Select(r => r.Kind));
        Assert.True(AuditChain.Verify(rows).Intact);
    }

    [Fact]
    public async Task An_altered_row_is_named()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var audit = api.Services.GetRequiredService<ToolAudit>();
        await audit.RecordAsync(Entry("search_documents"), Ct);
        await audit.RecordAsync(Entry("get_billing_run_status", args: "runId=4417"), Ct);
        await audit.RecordAsync(Entry("search_documents"), Ct);

        // Somebody edits the record in the store, as they can with any table.
        await using (var db = ChatApiTests.Db(api))
        {
            var row = await db.Audit.OrderBy(a => a.Id).Skip(1).FirstAsync(Ct);
            row.Arguments = "runId=9999";
            await db.SaveChangesAsync(Ct);
        }

        var rows = await RowsAsync(api);
        var report = AuditChain.Verify(rows);
        Assert.False(report.Intact);
        Assert.Equal(rows[1].Id, report.FirstBrokenId);
        Assert.Contains("does not match", report.Reason);
        Assert.Null(report.Head);
    }

    [Fact]
    public async Task A_removed_row_is_detected_at_its_successor()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var audit = api.Services.GetRequiredService<ToolAudit>();
        await audit.RecordAsync(Entry("search_documents"), Ct);
        await audit.RecordAsync(Entry("conversation.delete", AuditKinds.ConversationDelete), Ct);
        await audit.RecordAsync(Entry("search_documents"), Ct);

        long survivorId;
        await using (var db = ChatApiTests.Db(api))
        {
            var middle = await db.Audit.OrderBy(a => a.Id).Skip(1).FirstAsync(Ct);
            survivorId = await db.Audit.OrderBy(a => a.Id).Skip(2).Select(a => a.Id).FirstAsync(Ct);
            db.Audit.Remove(middle);
            await db.SaveChangesAsync(Ct);
        }

        var report = AuditChain.Verify(await RowsAsync(api));
        Assert.False(report.Intact);
        Assert.Equal(survivorId, report.FirstBrokenId);
        Assert.Contains("missing", report.Reason);
    }

    [Fact]
    public async Task Rows_written_before_the_chain_are_reported_not_rewritten()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        // The 65 rows this lab already had: no kind, no hash.
        await using (var db = ChatApiTests.Db(api))
        {
            db.Audit.Add(new AuditRow
            {
                At = DateTime.UtcNow.AddDays(-1), PrincipalId = "adam", FirmId = "firm-a",
                ToolName = "search_documents", Arguments = "", Outcome = "ok", DurationMs = 12,
            });
            await db.SaveChangesAsync(Ct);
        }
        await api.Services.GetRequiredService<ToolAudit>().RecordAsync(Entry("search_documents"), Ct);

        var rows = await RowsAsync(api);
        var report = AuditChain.Verify(rows);

        Assert.True(report.Intact);
        Assert.Equal(1, report.Checked);
        Assert.Equal(1, report.Unchained);
        Assert.Contains("predate the chain", report.Reason);
        // The old row keeps its content exactly as it was.
        Assert.Null(rows[0].Hash);
        Assert.Null(rows[0].Kind);
    }

    [Fact]
    public async Task Parallel_appends_leave_one_intact_chain()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var audit = api.Services.GetRequiredService<ToolAudit>();

        // Two replicas share one database; appending at the same moment must not link to the same predecessor.
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            audit.RecordAsync(Entry($"tool_{i % 3}", args: $"n={i}"), Ct)));

        var rows = await RowsAsync(api);
        Assert.Equal(12, rows.Count);
        Assert.Equal(12, rows.Select(r => r.Hash).Distinct().Count());
        var report = AuditChain.Verify(rows);
        Assert.True(report.Intact, report.Reason);
        Assert.Equal(12, report.Checked);
    }

    [Fact]
    public void An_empty_record_verifies()
    {
        var report = AuditChain.Verify([]);

        Assert.True(report.Intact);
        Assert.Equal(0, report.Checked);
        Assert.Null(report.Head);
        Assert.Equal("no records", report.Reason);
    }
}
