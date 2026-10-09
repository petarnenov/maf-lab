using Maf.Lab.Api.Plugins;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Maf.Lab.Tests;

public sealed class TenantPluginAccessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PluginAccessChanges Changes() => new(NullLogger<PluginAccessChanges>.Instance);

    [Fact]
    public async Task Default_is_closed_installation_scope_remains_and_two_tenants_are_independent()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider());
        await f.InitializeAsync(Ct);
        var changes = Changes();
        var store = f.Store(changes);
        var access = f.Access(store, changes);
        Assert.Equal(["history"], (await access.For(TenantPluginFixture.User, Ct)).Names);
        Assert.Equal(PluginChangeStatus.NotAllowed, (await store.EnableAsync(TenantPluginFixture.Admin, "weather", true, Ct)).Status);
        Assert.Equal(PluginChangeStatus.Changed, (await store.AllowAsync(TenantPluginFixture.Operator, "weather", true, false, Ct)).Status);
        Assert.False((await access.For(TenantPluginFixture.User, Ct)).IsInUse("weather"));
        Assert.Equal(PluginChangeStatus.Changed, (await store.EnableAsync(TenantPluginFixture.Admin, "weather", true, Ct)).Status);
        Assert.True((await access.For(TenantPluginFixture.User, Ct)).IsInUse("weather"));
        Assert.False((await access.For(TenantPluginFixture.User with { TenantId = TenantId.Firm("firm-b") }, Ct)).IsInUse("weather"));
    }

    [Fact]
    public async Task Withdrawing_an_allowance_atomically_disables_and_chains_audit_without_deleting_data()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider());
        await f.InitializeAsync(Ct);
        var changes = Changes();
        var store = f.Store(changes);
        await store.AllowAsync(TenantPluginFixture.Operator, "weather", true, true, Ct);
        var frozen = await f.Access(store, changes).For(TenantPluginFixture.User, Ct);
        await store.AllowAsync(TenantPluginFixture.Operator, "weather", false, false, Ct);
        var row = Assert.Single(await store.ReadAsync(TenantPluginFixture.Admin, Ct));
        Assert.False(row.Allowed);
        Assert.False(row.Enabled);
        Assert.True(frozen.IsInUse("weather"));
        await using var db = await f.Db.CreateDbContextAsync(Ct);
        var audits = await db.Audit.OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal(2, audits.Count);
        Assert.Equal(audits[0].Hash, audits[1].PreviousHash);
        Assert.All(audits, a => Assert.Equal("firm-a", a.TenantId));
        Assert.All(audits, a => Assert.Equal("operator", a.PrincipalId));
        Assert.Single(await db.PluginEntitlements.ToListAsync(Ct));
    }

    [Fact]
    public async Task Noop_is_idempotent_and_invalid_actor_scope_or_private_tenant_cannot_write()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider());
        await f.InitializeAsync(Ct);
        var store = f.Store(Changes());
        Assert.Equal(PluginChangeStatus.Forbidden, (await store.AllowAsync(TenantPluginFixture.Admin, "weather", true, true, Ct)).Status);
        Assert.Equal(PluginChangeStatus.Forbidden, (await store.EnableAsync(TenantPluginFixture.User, "weather", true, Ct)).Status);
        Assert.Equal(PluginChangeStatus.NotInstalled, (await store.AllowAsync(TenantPluginFixture.Operator, "missing", true, true, Ct)).Status);
        Assert.Equal(PluginChangeStatus.NotTenantScoped, (await store.AllowAsync(TenantPluginFixture.Operator, "history", true, true, Ct)).Status);
        Assert.Equal(PluginChangeStatus.PrivateToAnotherTenant, (await store.AllowAsync(TenantPluginFixture.Operator with { TenantId = TenantId.Firm("firm-b") }, "private-notes", true, true, Ct)).Status);
        Assert.Equal(PluginChangeStatus.Forbidden, (await store.AllowAsync(TenantPluginFixture.Operator with { TenantId = TenantId.Shared }, "weather", true, true, Ct)).Status);
        await store.AllowAsync(TenantPluginFixture.Operator, "weather", true, true, Ct);
        Assert.Equal(PluginChangeStatus.Unchanged, (await store.AllowAsync(TenantPluginFixture.Operator, "weather", true, true, Ct)).Status);
        await using var db = await f.Db.CreateDbContextAsync(Ct);
        Assert.Single(await db.Audit.ToListAsync(Ct));
    }

    [Fact]
    public async Task Missed_notifications_expire_at_thirty_seconds_and_a_snapshot_stays_frozen()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider());
        await f.InitializeAsync(Ct);
        var writer = f.Store(Changes());
        var reader = f.Access(f.Store(Changes()), Changes());
        await writer.AllowAsync(TenantPluginFixture.Operator, "weather", true, true, Ct);
        var before = await reader.For(TenantPluginFixture.User, Ct);
        await writer.AllowAsync(TenantPluginFixture.Operator, "weather", false, false, Ct);
        ((FakeTimeProvider)f.Clock).Advance(TimeSpan.FromSeconds(29));
        Assert.True((await reader.For(TenantPluginFixture.User, Ct)).IsInUse("weather"));
        ((FakeTimeProvider)f.Clock).Advance(TimeSpan.FromSeconds(1));
        Assert.False((await reader.For(TenantPluginFixture.User, Ct)).IsInUse("weather"));
        Assert.True(before.IsInUse("weather"));
    }

    [Fact]
    public async Task An_invalidation_during_a_read_cannot_repopulate_a_stale_cache()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider());
        await f.InitializeAsync(Ct);
        var changes = Changes();
        var held = new HeldStore();
        var reader = f.Access(held, changes);
        var read = reader.For(TenantPluginFixture.User, Ct);
        await held.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await changes.PublishAsync(TenantPluginFixture.User.TenantId);
        held.Release.SetResult();
        Assert.False((await read).IsInUse("weather"));
        Assert.Equal(2, held.Reads);
    }

    [Fact]
    public async Task An_audit_write_failure_rolls_back_the_permission_change()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider());
        await f.InitializeAsync(Ct);
        await using (var db = await f.Db.CreateDbContextAsync(Ct))
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectAudit BEFORE INSERT ON Audit BEGIN SELECT RAISE(ABORT, 'fixture refuses audit'); END;", Ct);
        var store = f.Store(Changes());
        await Assert.ThrowsAsync<DbUpdateException>(() => store.AllowAsync(TenantPluginFixture.Operator, "weather", true, true, Ct));
        Assert.Empty(await store.ReadAsync(TenantPluginFixture.Admin, Ct));
        await using var check = await f.Db.CreateDbContextAsync(Ct);
        Assert.Empty(await check.Audit.ToListAsync(Ct));
    }

    [Fact]
    public async Task Disabling_and_enabling_keep_the_tenants_existing_content()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider());
        await f.InitializeAsync(Ct);
        await using (var db = await f.Db.CreateDbContextAsync(Ct))
        {
            db.Conversations.Add(new Maf.Lab.Api.Storage.ConversationRow { Id = "existing", UserId = "adam", TenantId = "firm-a", Title = "kept" });
            await db.SaveChangesAsync(Ct);
        }
        var store = f.Store(Changes());
        await store.AllowAsync(TenantPluginFixture.Operator, "weather", true, true, Ct);
        await store.EnableAsync(TenantPluginFixture.Admin, "weather", false, Ct);
        await store.EnableAsync(TenantPluginFixture.Admin, "weather", true, Ct);
        await using var check = await f.Db.CreateDbContextAsync(Ct);
        Assert.Equal("kept", (await check.Conversations.SingleAsync(Ct)).Title);
    }

    private sealed class HeldStore : IPluginEntitlements
    {
        public readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads;
        public async Task<IReadOnlyList<PluginEntitlement>> ReadAsync(Principal principal, CancellationToken ct)
        {
            if (Interlocked.Increment(ref Reads) > 1) return [];
            ReadStarted.SetResult();
            await Release.Task.WaitAsync(ct);
            return [new("weather", true, true)];
        }
        public Task<PluginChange> AllowAsync(Principal principal, string plugin, bool allowed, bool enable, CancellationToken ct) => throw new NotSupportedException();
        public Task<PluginChange> EnableAsync(Principal principal, string plugin, bool enabled, CancellationToken ct) => throw new NotSupportedException();
    }
}
