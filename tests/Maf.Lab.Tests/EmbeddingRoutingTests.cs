using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>Queries and documents go to their own Ollama instance, each request carrying that instance's thread count.</summary>
public class EmbeddingRoutingTests
{
    private const string Interactive = "http://interactive:11434";
    private const string Batch = "http://batch:11434";

    [Fact]
    public async Task Queries_go_to_the_interactive_instance_with_its_thread_count()
    {
        var (encoder, seen) = Encoder(new ModelOptions { OllamaEndpoint = Interactive, OllamaNumThread = 4, BatchOllamaEndpoint = Batch, BatchOllamaNumThread = 12 });

        await encoder.EmbedQueryAsync("dense_v3", "fee schedule", CancellationToken.None);

        var request = Assert.Single(seen);
        Assert.Equal("interactive", request.Host);
        Assert.Equal(4, request.NumThread);
    }

    [Fact]
    public async Task Documents_go_to_the_batch_instance_with_its_thread_count_and_refuse_truncation()
    {
        var (encoder, seen) = Encoder(new ModelOptions { OllamaEndpoint = Interactive, OllamaNumThread = 4, BatchOllamaEndpoint = Batch, BatchOllamaNumThread = 12 });

        await encoder.EmbedDocumentsAsync("dense_v3", ["one chunk", "another chunk"], CancellationToken.None);

        var request = Assert.Single(seen);
        Assert.Equal("batch", request.Host);
        Assert.Equal(12, request.NumThread);
        Assert.False(request.Truncate);
    }

    [Fact]
    public async Task Without_a_batch_endpoint_documents_use_the_one_instance_and_its_thread_count()
    {
        var (encoder, seen) = Encoder(new ModelOptions { OllamaEndpoint = Interactive, OllamaNumThread = 4, BatchOllamaNumThread = 12 });

        await encoder.EmbedQueryAsync("dense_v3", "fee schedule", CancellationToken.None);
        await encoder.EmbedDocumentsAsync("dense_v3", ["one chunk"], CancellationToken.None);

        Assert.All(seen, r => Assert.Equal("interactive", r.Host));
        Assert.All(seen, r => Assert.Equal(4, r.NumThread));
    }

    [Fact]
    public async Task No_thread_count_configured_sends_none()
    {
        var (encoder, seen) = Encoder(new ModelOptions { OllamaEndpoint = Interactive, BatchOllamaEndpoint = Batch });

        await encoder.EmbedQueryAsync("dense_v3", "fee schedule", CancellationToken.None);
        await encoder.EmbedDocumentsAsync("dense_v3", ["one chunk"], CancellationToken.None);

        Assert.All(seen, r => Assert.Null(r.NumThread));
    }

    [Fact]
    public void The_batch_settings_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Models:BatchOllamaEndpoint"] = Batch,
            ["Models:OllamaNumThread"] = "4",
            ["Models:BatchOllamaNumThread"] = "12",
        }).Build();
        using var provider = new ServiceCollection().AddLogging().AddMafIndexing(configuration).BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ModelOptions>>().Value;

        Assert.Equal(Batch, options.BatchOllamaEndpoint);
        Assert.Equal(4, options.OllamaNumThread);
        Assert.Equal(12, options.BatchOllamaNumThread);
    }

    private static (DenseEncoder Encoder, List<SeenRequest> Seen) Encoder(ModelOptions options)
    {
        var seen = new List<SeenRequest>();
        var providers = new ModelProviders(Options.Create(options), () => new RecordingHandler(seen));
        return (new DenseEncoder(providers), seen);
    }

    private sealed record SeenRequest(string Host, int? NumThread, bool? Truncate);

    private sealed class RecordingHandler(List<SeenRequest> seen) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            var inputs = body["input"]!.AsArray().Count;
            lock (seen)
            {
                seen.Add(new SeenRequest(request.RequestUri!.Host, body["options"]?["num_thread"]?.GetValue<int>(), body["truncate"]?.GetValue<bool>()));
            }
            var response = new JsonObject
            {
                ["model"] = "embeddinggemma",
                ["embeddings"] = new JsonArray(Enumerable.Range(0, inputs).Select(_ => (JsonNode)new JsonArray(0.1f, 0.2f, 0.3f)).ToArray()),
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.ToJsonString(JsonSerializerOptions.Default), Encoding.UTF8, "application/json"),
            };
        }
    }
}
