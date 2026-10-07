using Maf.Lab.Indexing;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Sparse;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.IndexAdmin;

/// <summary>The corpora a tenant admin may index: the installed plugins' tenant-layout corpora, by plugin name.</summary>
public sealed class OfferedCorpora(IInstalledPlugins installed)
{
    /// <summary>The repository layout (the code corpus) is the installation's, never one tenant's to rebuild.</summary>
    public IReadOnlyList<PluginCorpus> All() =>
        [.. installed.Corpora().Where(c => c.Layout == CorpusLayoutNames.Tenants).OrderBy(c => c.Plugin, StringComparer.Ordinal)];

    /// <summary>
    /// The corpus a request names, or the only one offered when it names none. Null corpus with an error: the request is
    /// answered with that error and nothing runs.
    /// </summary>
    public (PluginCorpus? Corpus, CorpusChoice Choice) Resolve(string? name)
    {
        var offered = All();
        if (string.IsNullOrWhiteSpace(name))
        {
            return offered.Count switch
            {
                1 => (offered[0], CorpusChoice.Chosen),
                0 => (null, CorpusChoice.NotOffered),
                _ => (null, CorpusChoice.Ambiguous),
            };
        }
        return offered.FirstOrDefault(c => c.Plugin == name) is { } corpus ? (corpus, CorpusChoice.Chosen) : (null, CorpusChoice.NotOffered);
    }
}

public enum CorpusChoice
{
    Chosen,
    /// <summary>Not declared by an installed plugin, or not a tenant-layout corpus: 404.</summary>
    NotOffered,
    /// <summary>None named while several are offered: 400 naming the choices.</summary>
    Ambiguous,
}

/// <summary>
/// The indexing services over one corpus (design D4): the core's Qdrant and indexing settings, read from configuration as
/// the options pattern binds them, with the corpus's root, layout, collections and graph source in place of the defaults.
/// Built per use with <see cref="ActivatorUtilities"/>, as <c>DomainChunkStore.For</c> builds a plugin's store; every read and
/// write still goes through <see cref="TenantScopedMaintenance"/>.
/// </summary>
public sealed class CorpusIndexing(IServiceProvider services, IConfiguration configuration)
{
    public CorpusServices For(PluginCorpus corpus)
    {
        var qdrant = configuration.GetSection(QdrantOptions.Section).Get<QdrantOptions>() ?? new QdrantOptions();
        qdrant.Collection = corpus.Collection;
        qdrant.MetaCollection = corpus.MetaCollection;
        var indexing = configuration.GetSection(IndexingOptions.Section).Get<IndexingOptions>() ?? new IndexingOptions();
        indexing.CorpusRoot = corpus.Root;
        indexing.Layout = corpus.Layout;
        indexing.GraphSource = corpus.Graph ?? "";

        var qdrantOptions = Options.Create(qdrant);
        var indexingOptions = Options.Create(indexing);
        var bootstrapper = ActivatorUtilities.CreateInstance<CollectionBootstrapper>(services, qdrantOptions);
        var store = ActivatorUtilities.CreateInstance<TenantScopedMaintenance>(services, qdrantOptions);
        var bm25 = ActivatorUtilities.CreateInstance<Bm25Store>(services, qdrantOptions);
        return new CorpusServices(
            bootstrapper,
            store,
            ActivatorUtilities.CreateInstance<IndexingPipeline>(services, bootstrapper, store, bm25, indexingOptions),
            ActivatorUtilities.CreateInstance<DriftService>(services, store, indexingOptions),
            ActivatorUtilities.CreateInstance<MigrationService>(services, bootstrapper, store, indexingOptions));
    }
}

public sealed record CorpusServices(
    CollectionBootstrapper Bootstrapper,
    TenantScopedMaintenance Store,
    IndexingPipeline Pipeline,
    DriftService Drift,
    MigrationService Migration);
