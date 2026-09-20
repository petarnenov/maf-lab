using System.Net;
using System.Text.Json;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Tests;

/// <summary>
/// What an authorised person can verify and be handed — and the two things that must never happen: another firm's
/// data in the package, or an extraction that leaves no trace.
/// </summary>
public class ComplianceApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<string> ChatAsync(ApiFactory api, HttpClient client, string message)
    {
        var events = await ApiFactory.ChatAsync(client, message);
        return events[^1].Data.GetProperty("conversationId").GetString()!;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, Ct);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(Ct), Json)!;
    }

    [Fact]
    public async Task Only_a_firm_admin_may_verify_or_export()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var advisor = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        Assert.Equal(HttpStatusCode.Forbidden, (await advisor.GetAsync("/api/admin/compliance/verify", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await advisor.GetAsync("/api/admin/compliance/export", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/admin/compliance/verify", Ct)).StatusCode);
    }

    [Fact]
    public async Task Verification_reports_an_intact_chain_and_then_the_row_that_was_altered()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        await ChatAsync(api, adam, "what is the procedure when a fee schedule is missing");
        await ChatAsync(api, adam, "how do I issue a billing credit?");

        var intact = await GetAsync<ChainReport>(alice, "/api/admin/compliance/verify");
        Assert.True(intact.Intact);
        Assert.True(intact.Checked >= 2);
        Assert.NotNull(intact.Head);
        Assert.Null(intact.FirstBrokenId);

        long tamperedId;
        await using (var db = ChatApiTests.Db(api))
        {
            var row = await db.Audit.OrderBy(a => a.Id).FirstAsync(Ct);
            tamperedId = row.Id;
            row.Outcome = "error";
            await db.SaveChangesAsync(Ct);
        }

        var broken = await GetAsync<ChainReport>(alice, "/api/admin/compliance/verify");
        Assert.False(broken.Intact);
        Assert.Equal(tamperedId, broken.FirstBrokenId);
        Assert.NotNull(broken.Reason);
    }

    [Fact]
    public async Task Deleting_a_conversation_is_recorded_and_a_refused_delete_is_not()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var rita = api.ClientFor("rita", "firm-a", Role.READ_ONLY);
        var conversationId = await ChatAsync(api, adam, "what is the procedure when a fee schedule is missing");

        // Not Adam's conversation: refused, and attributed to nobody.
        Assert.Equal(HttpStatusCode.NotFound, (await rita.DeleteAsync($"/api/conversations/{conversationId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await adam.DeleteAsync($"/api/conversations/{conversationId}", Ct)).StatusCode);

        await using var db = ChatApiTests.Db(api);
        var deletions = await db.Audit.AsNoTracking().Where(a => a.Kind == AuditKinds.ConversationDelete).ToListAsync(Ct);
        var deletion = Assert.Single(deletions);
        Assert.Equal("adam", deletion.PrincipalId);
        Assert.Equal(conversationId, deletion.ConversationId);
        Assert.Equal("ok", deletion.Outcome);
        Assert.DoesNotContain(deletions, d => d.PrincipalId == "rita");

        // The record outlives the soft delete it describes.
        Assert.NotNull(await db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversationId && c.DeletedAt != null, Ct));
        Assert.True(AuditChain.Verify(await db.Audit.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct)).Intact);
    }

    [Fact]
    public async Task A_tool_call_and_a_deletion_sit_in_one_ordered_chain()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var conversationId = await ChatAsync(api, adam, "what is the procedure when a fee schedule is missing");
        await adam.DeleteAsync($"/api/conversations/{conversationId}", Ct);

        await using var db = ChatApiTests.Db(api);
        var rows = await db.Audit.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);

        Assert.Equal(AuditKinds.Tool, rows[0].Kind);
        Assert.Equal("search_documents", rows[0].ToolName);
        Assert.Equal("", rows[0].Arguments); // still no query text
        Assert.Equal(AuditKinds.ConversationDelete, rows[^1].Kind);
        Assert.Equal(rows[^2].Hash, rows[^1].PreviousHash);
    }

    [Fact]
    public async Task The_package_holds_the_firms_own_data_with_a_manifest_that_can_be_rechecked()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        var bob = api.ClientFor("bob", "firm-b", Role.ADVISOR);
        await ChatAsync(api, adam, "what is the procedure when a fee schedule is missing");
        var deleted = await ChatAsync(api, adam, "how do I issue a billing credit?");
        await adam.DeleteAsync($"/api/conversations/{deleted}", Ct);
        await ChatAsync(api, bob, "what is the procedure when a fee schedule is missing");

        var package = await GetAsync<ExportPackage>(alice, "/api/admin/compliance/export");

        Assert.Equal("firm-a", package.Manifest.FirmId);
        Assert.Equal("alice", package.Manifest.By);
        Assert.All(package.Conversations, c => Assert.NotEqual("bob", c.UserId));
        Assert.All(package.Turns, t => Assert.NotEqual("bob", t.UserId));
        Assert.All(package.Actions, a => Assert.NotEqual("bob", a.PrincipalId));
        Assert.Equal(package.Conversations.Count, package.Manifest.Counts["conversations"]);
        Assert.Equal(package.Turns.Count, package.Manifest.Counts["turns"]);
        Assert.Equal(package.Actions.Count, package.Manifest.Counts["actions"]);

        // A deleted conversation is in the package, marked as deleted.
        var deletedRow = Assert.Single(package.Conversations, c => c.ConversationId == deleted);
        Assert.NotNull(deletedRow.DeletedAt);

        // The recipient can recompute the digest over the content alone — rebuilt here by hand, exactly as a
        // recipient in another language would, rather than by calling the producer's own helper.
        Assert.Equal(package.Manifest.Sha256, RecomputedByHand(package));
        Assert.NotNull(package.Manifest.AuditChainHead);
    }

    [Fact]
    public async Task The_record_can_be_browsed_a_page_at_a_time_newest_first()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        var first = await ChatAsync(api, adam, "what is the procedure when a fee schedule is missing");
        await ChatAsync(api, adam, "how do I issue a billing credit?");
        await adam.DeleteAsync($"/api/conversations/{first}", Ct);

        var all = await GetAsync<ActionPage>(alice, "/api/admin/compliance/actions");
        Assert.Null(all.NextCursor);
        Assert.Equal(3, all.Actions.Count);
        Assert.Equal(all.Actions.Select(a => a.Id).OrderByDescending(id => id), all.Actions.Select(a => a.Id));

        // Paged: every row exactly once, no repeats.
        var page1 = await GetAsync<ActionPage>(alice, "/api/admin/compliance/actions?limit=2");
        Assert.Equal(2, page1.Actions.Count);
        Assert.NotNull(page1.NextCursor);
        var page2 = await GetAsync<ActionPage>(alice, $"/api/admin/compliance/actions?limit=2&before={page1.NextCursor}");
        Assert.Single(page2.Actions);
        Assert.Null(page2.NextCursor);
        Assert.Equal(all.Actions.Select(a => a.Id), page1.Actions.Concat(page2.Actions).Select(a => a.Id));
    }

    [Fact]
    public async Task Browsing_filters_by_person_and_kind_and_says_when_there_is_nothing()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var olga = api.ClientFor("olga", "firm-a", Role.OPS);
        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        var conversation = await ChatAsync(api, adam, "what is the procedure when a fee schedule is missing");
        await adam.DeleteAsync($"/api/conversations/{conversation}", Ct);
        await ChatAsync(api, olga, "how do I issue a billing credit?");

        var byPerson = await GetAsync<ActionPage>(alice, "/api/admin/compliance/actions?userId=adam");
        Assert.All(byPerson.Actions, a => Assert.Equal("adam", a.PrincipalId));
        Assert.Equal(2, byPerson.Actions.Count);

        var byKind = await GetAsync<ActionPage>(alice, $"/api/admin/compliance/actions?userId=adam&kind={AuditKinds.ConversationDelete}");
        var deletion = Assert.Single(byKind.Actions);
        Assert.Equal(conversation, deletion.ConversationId);

        var empty = await GetAsync<ActionPage>(alice, "/api/admin/compliance/actions?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");
        Assert.Empty(empty.Actions);
        Assert.Null(empty.NextCursor);
    }

    [Fact]
    public async Task Browsing_stays_in_the_firm_leaves_no_trace_and_needs_an_admin()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var bob = api.ClientFor("bob", "firm-b", Role.ADVISOR);
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        await ChatAsync(api, bob, "what is the procedure when a fee schedule is missing");
        await ChatAsync(api, adam, "what is the procedure when a fee schedule is missing");

        var before = await GetAsync<ChainReport>(alice, "/api/admin/compliance/verify");
        var page = await GetAsync<ActionPage>(alice, "/api/admin/compliance/actions?firmId=firm-b&userId=bob");
        var after = await GetAsync<ChainReport>(alice, "/api/admin/compliance/verify");

        Assert.Empty(page.Actions); // bob is another firm's user: nothing, whatever the parameters say
        Assert.All((await GetAsync<ActionPage>(alice, "/api/admin/compliance/actions")).Actions,
            a => Assert.NotEqual("bob", a.PrincipalId));
        // Reading does not grow the record.
        Assert.Equal(before.Checked, after.Checked);
        Assert.Equal(before.Head, after.Head);

        Assert.Equal(HttpStatusCode.Forbidden, (await adam.GetAsync("/api/admin/compliance/actions", Ct)).StatusCode);
    }

    /// <summary>The canonical rendering the manifest documents, rebuilt from the package without the producer's code.</summary>
    private static string RecomputedByHand(ExportPackage package)
    {
        static string Iso(DateTimeOffset at) =>
            at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        static string Row(params string[] fields) => string.Join('\u001f', fields) + "\n";

        var text = new System.Text.StringBuilder("conversations\n");
        foreach (var c in package.Conversations)
        {
            text.Append(Row(c.ConversationId, c.UserId, Iso(c.CreatedAt), Iso(c.LastActivityAt), c.Title ?? "",
                c.DeletedAt is null ? "" : Iso(c.DeletedAt.Value)));
        }
        text.Append("turns\n");
        foreach (var t in package.Turns)
        {
            text.Append(Row(t.TurnId, t.ConversationId, t.UserId, Iso(t.CreatedAt), t.Question, t.Answer, t.Intent,
                t.ForcedRetrieval ? "true" : "false", t.ToolCallsJson, t.SourcesJson, t.SignalsJson));
        }
        text.Append("actions\n");
        foreach (var a in package.Actions)
        {
            text.Append(Row(a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Iso(a.At), a.PrincipalId,
                a.Kind ?? "", a.Action, a.Arguments, a.Outcome,
                a.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                a.ConversationId ?? "", a.TurnId ?? "", a.Hash ?? ""));
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    [Fact]
    public async Task Another_firm_cannot_be_reached_by_any_parameter()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var bob = api.ClientFor("bob", "firm-b", Role.ADVISOR);
        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        await ChatAsync(api, bob, "what is the procedure when a fee schedule is missing");

        var package = await GetAsync<ExportPackage>(alice, "/api/admin/compliance/export?firmId=firm-b&userId=bob");

        Assert.Equal("firm-a", package.Manifest.FirmId);
        Assert.Empty(package.Conversations);
        Assert.Empty(package.Turns);
        Assert.Empty(package.Actions);
    }

    [Fact]
    public async Task A_subject_scoped_package_holds_only_that_person()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var olga = api.ClientFor("olga", "firm-a", Role.OPS);
        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        await ChatAsync(api, adam, "what is the procedure when a fee schedule is missing");
        await ChatAsync(api, olga, "how do I issue a billing credit?");

        var package = await GetAsync<ExportPackage>(alice, "/api/admin/compliance/export?userId=adam");

        Assert.NotEmpty(package.Conversations);
        Assert.All(package.Conversations, c => Assert.Equal("adam", c.UserId));
        Assert.All(package.Turns, t => Assert.Equal("adam", t.UserId));
        Assert.All(package.Actions, a => Assert.Equal("adam", a.PrincipalId));
        Assert.Equal("adam", package.Manifest.SubjectUserId);
    }

    [Fact]
    public async Task The_export_is_itself_recorded_and_appears_in_the_next_one()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        await ChatAsync(api, adam, "what is the procedure when a fee schedule is missing");

        var first = await GetAsync<ExportPackage>(alice, "/api/admin/compliance/export");
        Assert.DoesNotContain(first.Actions, a => a.Kind == AuditKinds.ComplianceExport); // not its own row

        var second = await GetAsync<ExportPackage>(alice, "/api/admin/compliance/export");
        var export = Assert.Single(second.Actions, a => a.Kind == AuditKinds.ComplianceExport);
        Assert.Equal("alice", export.PrincipalId);
        Assert.Contains("from=", export.Arguments);
        Assert.Contains("turns=", export.Arguments);
        // Identifiers only: no question or answer text travels into the record.
        Assert.DoesNotContain("procedure", export.Arguments);

        Assert.True((await GetAsync<ChainReport>(alice, "/api/admin/compliance/verify")).Intact);
    }
}
