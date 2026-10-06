using Maf.Lab.Indexing.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// A small corpus of no domain's, indexed once with the fake embedder and shared by the core library's acceptance tests:
/// tenant scoping, the relevance gate and floors, and query normalisation. It is <see cref="TempCorpus.Small"/> (shared
/// procedures, an Acme firm-a policy, 30 Northwind firm-b households, one Contoso firm-c doc) plus enough shared and
/// firm-c material that a small tenant's own top 10, and a 20-candidate rerank, are full, while every global neighbour
/// of the Northwind households' words stays in firm-b.
/// </summary>
public sealed class SmallCorpusFixture(QdrantFixture qdrant) : IAsyncLifetime
{
    private readonly TempCorpus _corpus = Build();

    public string Collection { get; } = $"corpus_{Guid.NewGuid():N}";
    public string CorpusRoot => _corpus.Root;
    public QdrantFixture Qdrant => qdrant;

    public async ValueTask InitializeAsync()
    {
        await using var services = qdrant.Services(Collection, CorpusRoot);
        await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), CancellationToken.None);
    }

    private static TempCorpus Build()
    {
        var c = TempCorpus.Small();
        string[] topics = ["Invoice delivery by email", "Custodian statements", "Account opening checklist", "Quarterly review meeting",
            "Client address change", "Document retention period", "Holiday calendar", "Office contacts", "Advisor onboarding",
            "Glossary", "Statement archive", "Branch hours", "Phone etiquette", "Meeting rooms", "Travel expenses", "Training plan"];
        for (var i = 0; i < topics.Length; i++)
        {
            c.Write($"shared/docs/topic-{i:D2}.md", $"""
                # {topics[i]}
                General guidance on {topics[i].ToLowerInvariant()} for every advisor. Note {i}.
                """);
        }
        // Each names a fee once, in a longer text: found by a keyword search, never closer than a Northwind household.
        for (var i = 0; i < 12; i++)
        {
            c.Write($"firm-c/docs/contoso-client-{i:D2}.md", $"""
                # Contoso client note {i}
                Contoso Advisors client {i} pays a flat fee billed quarterly; the client agreement covers reporting, statements,
                meetings, contacts and the review cadence agreed with the advisor.
                """);
        }
        return c;
    }

    public ValueTask DisposeAsync()
    {
        _corpus.Dispose();
        return ValueTask.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class SmallCorpusCollection : ICollectionFixture<SmallCorpusFixture>
{
    public const string Name = "small-corpus";
}
