using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.TestSupport;

public sealed class TenantPluginFixture : IAsyncDisposable
{
    public static readonly Principal Operator = new("operator", TenantId.Firm("firm-a"), Role.PLATFORM_ADMIN);
    public static readonly Principal Admin = new("alice", TenantId.Firm("firm-a"), Role.TENANT_ADMIN);
    public static readonly Principal User = new("adam", TenantId.Firm("firm-a"), Role.USER);
    public readonly TimeProvider Clock;
    public readonly string DirectoryPath = Directory.CreateTempSubdirectory("maf-tenant-plugins-").FullName;
    public readonly IDbContextFactory<MafDbContext> Db;
    public readonly PluginCatalogue Catalogue;
    private readonly List<PluginAccess> _access = [];

    public TenantPluginFixture(TimeProvider? time = null, string tenantPlugin = "weather")
    {
        Clock = time ?? TimeProvider.System;
        Db = new Factory(new DbContextOptionsBuilder<MafDbContext>()
            .UseSqlite($"Data Source={Path.Combine(DirectoryPath, "state.db")};Pooling=False")
            .AddInterceptors(new SqlitePragmaInterceptor()).Options);
        File.WriteAllText(Path.Combine(DirectoryPath, ".installed"), JsonSerializer.Serialize(new
        {
            env = "dev", plugins = new[] {
                Item(tenantPlugin, PluginScopes.Tenant), Item("history", PluginScopes.Installation),
                Item("private-notes", PluginScopes.Tenant, "firm-a"),
            },
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Catalogue = new(Options.Create(new PluginOptions { Root = DirectoryPath }), NullLogger<PluginCatalogue>.Instance);
    }

    private static object Item(string name, string scope, string? privateTo = null) => new
    {
        manifest = new PluginManifest { Name = name, Kind = "app", Scope = scope, PrivateTo = privateTo,
            Environments = ["dev"], Description = "fixture", Progress = "None", Stopping = "None" }, hasServer = false,
    };

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var context = await Db.CreateDbContextAsync(ct);
        await DatabaseInitializer.InitializeAsync(context, ct);
    }

    public PluginEntitlements Store(IPluginAccessChanges changes) => new(Db, Catalogue, changes,
        new ToolAudit(Db, NullLogger<ToolAudit>.Instance, Clock), Clock);

    public PluginAccess Access(IPluginEntitlements store, IPluginAccessChanges changes)
    {
        var access = new PluginAccess(store, Catalogue, changes, Clock);
        _access.Add(access);
        return access;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var access in _access) access.Dispose();
        Catalogue.Dispose();
        await Task.CompletedTask;
        Directory.Delete(DirectoryPath, true);
    }

    private sealed class Factory(DbContextOptions<MafDbContext> options) : IDbContextFactory<MafDbContext>
    {
        public MafDbContext CreateDbContext() => new(options);
        public Task<MafDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
