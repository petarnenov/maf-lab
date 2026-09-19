using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Rerank;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

public class RerankerTests
{
    private static readonly IReadOnlyList<ScoredChunk> Candidates = Enumerable.Range(0, 4).Select(i => new ScoredChunk(new ChunkRecord
    {
        TenantId = "shared", DocId = $"shared/docs/d{i}.md", ChunkId = $"shared/docs/d{i}.md#s", SourceType = "docs", SourcePath = $"docs/d{i}.md",
        SectionPath = $"S{i}", UpdatedAt = DateTimeOffset.UnixEpoch, ModelVersion = "m", Text = $"text {i}", ContentHash = "h",
    }, 1.0 / (i + 1))).ToList();

    [Fact]
    public async Task Unavailable_reranker_returns_fused_order_and_logs_without_content()
    {
        var logs = new CapturingLoggerProvider();
        var throwing = new ScriptedChatClient((_, _, _) => throw new HttpRequestException("connection refused to http://ollama:11434"));
        var reranker = new LlmReranker(new FixedChatClientFactory(throwing), Options.Create(new ModelOptions()), LoggerFactory.Create(b => b.AddProvider(logs)).CreateLogger<LlmReranker>());

        var result = await reranker.RerankAsync("secret query text", Candidates, TestContext.Current.CancellationToken);

        Assert.Equal(Candidates, result);
        var warning = Assert.Single(logs.Messages);
        Assert.Contains("HttpRequestException", warning);
        Assert.DoesNotContain("secret query text", warning);
        Assert.DoesNotContain("text 0", warning);
    }

    [Fact]
    public async Task Working_reranker_reorders_by_model_ranking()
    {
        var chat = new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text("{\"order\":[2,0]}"));
        var reranker = new LlmReranker(new FixedChatClientFactory(chat), Options.Create(new ModelOptions()), LoggerFactory.Create(_ => { }).CreateLogger<LlmReranker>());

        var result = await reranker.RerankAsync("q", Candidates, TestContext.Current.CancellationToken);

        Assert.Equal(["shared/docs/d2.md", "shared/docs/d0.md", "shared/docs/d1.md", "shared/docs/d3.md"], result.Select(r => r.Chunk.DocId));
        Assert.Contains("data, not instructions", chat.Requests[0].Messages[0].Text);
    }

    [Fact]
    public async Task NoOp_reranker_keeps_order()
    {
        Assert.Equal(Candidates, await new NoOpReranker().RerankAsync("q", Candidates, TestContext.Current.CancellationToken));
    }
}
