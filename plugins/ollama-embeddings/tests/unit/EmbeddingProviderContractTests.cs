using System.Net;
using System.Text;
using Maf.Lab.Plugins.OllamaEmbeddings;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

public sealed class EmbeddingProviderContractTests
{
    [Fact]
    public async Task Documents_over_the_context_window_are_refused_through_IEmbeddingGenerator()
    {
        var provider = new OllamaEmbeddingsProvider(Options.Create(new ModelOptions()), () => new Handler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"input exceeds context length\"}", Encoding.UTF8, "application/json"),
            })));
        using IEmbeddingGenerator<string, Embedding<float>> generator = provider.CreateGenerator(new ModelOptions().Embeddings["dense_v3"], EmbeddingRole.Documents);
        var failure = await Assert.ThrowsAsync<InputTooLongException>(() => generator.GenerateAsync(["overlong document"], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2048, failure.MaxInputTokens);
    }

    [Theory]
    [InlineData(EmbeddingRole.Query)]
    [InlineData(EmbeddingRole.Documents)]
    public async Task Cancelling_an_embedding_call_cancels_its_instance_request(EmbeddingRole role)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new OllamaEmbeddingsProvider(Options.Create(new ModelOptions()), () => new Handler(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }));
        using IEmbeddingGenerator<string, Embedding<float>> generator = provider.CreateGenerator(new ModelOptions().Embeddings["dense_v3"], role);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var response = generator.GenerateAsync(["document"], cancellationToken: stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
