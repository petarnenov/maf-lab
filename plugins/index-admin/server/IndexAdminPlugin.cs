using Maf.Lab.Indexing;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Plugins.IndexAdmin;

/// <summary>
/// The index-admin plugin (extract-index-admin-plugin): the index administration routes over the indexing pipeline, for
/// the corpora the installed plugins declare, run as jobs in the core's admin job store. Without it the api has no
/// indexing pipeline and no index admin route.
/// </summary>
public sealed class IndexAdminPlugin : IMafPlugin, IContributesServices, IContributesEndpoints, IContributesOpenWork
{
    public const string PluginName = "index-admin";

    /// <summary>The job kinds this plugin starts; one of each runs per tenant at a time.</summary>
    public const string IndexKind = "index";
    public const string MigrateKind = "migrate";
    internal static readonly string[] Kinds = [IndexKind, MigrateKind];

    public string Name => PluginName;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMafIndexing(configuration);
        services.AddSingleton<OfferedCorpora>();
        services.AddSingleton<CorpusIndexing>();
    }

    public void MapEndpoints(IMafEndpoints endpoints) => IndexAdminEndpoints.Map(endpoints.Routes);

    public IOpenWork CreateOpenWork(IServiceProvider services) =>
        new IndexAdminOpenWork(services.GetRequiredService<IServiceScopeFactory>());
}

/// <summary>
/// The open index and migrate jobs, so plugin-off refuses while one runs or cancels it through the job store first. The
/// job port is request-scoped, so each call takes its own scope.
/// </summary>
internal sealed class IndexAdminOpenWork(IServiceScopeFactory scopes) : IOpenWork
{
    public async Task<IReadOnlyList<OpenWorkItem>> ListOpenAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var jobs = await scope.ServiceProvider.GetRequiredService<IAdminJobs>().OpenAsync(IndexAdminPlugin.Kinds, ct);
        return [.. jobs.Select(j => new OpenWorkItem(j.Kind, j.JobId, j.State))];
    }

    public async Task CancelAllAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAdminJobs>().CancelOpenAsync(IndexAdminPlugin.Kinds, ct);
    }
}
