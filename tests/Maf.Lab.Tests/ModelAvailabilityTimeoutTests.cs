using System.Net;
using Maf.Lab.Api.Coverage;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The probe's cancellation and the refusals it can meet (model-selection): the caller's cancellation reaches the
/// provider call through the probe's own token, a 402 is a refusal, and a neutral failure is only "could not check".
/// </summary>
public sealed class ModelAvailabilityTimeoutTests
{
    private const string Tag = "glm-5.3:cloud";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Hands one client out to every model.</summary>
    private sealed class FixedFactory(IChatClient client) : IChatClientFactory
    {
        public IChatClient CreateChatClient(string? model = null) => client;

        public ChatOptions BaseChatOptions() => new();
    }

    /// <summary>
    /// Answers only once the token it was handed fires (at most five seconds), and remembers that token: the probe
    /// must hand the provider its own linked token, so the caller's cancellation and the timeout both reach it.
    /// </summary>
    private sealed class HandshakeClient : IChatClient
    {
        public CancellationToken Handed { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Handed = cancellationToken;
            // Bounded: a broken link must fail the assertion below, not hang the run.
            await Task.Run(() => cancellationToken.WaitHandle.WaitOne(5000));
            return new List<ChatResponseUpdate> { new(ChatRole.Assistant, "OK") }.ToChatResponse();
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("the probe asks for one response, not a stream");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static ScriptedChatClient Throwing(Exception error) => new((_, _, _) => throw error);

    private static ModelAvailability NewAvailability(IChatClient client, IMemoryCache? cache = null) =>
        new(new FixedFactory(client), cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new TestAgentOptions()), NullLogger<ModelAvailability>.Instance);

    [Fact]
    public async Task Cancelling_the_caller_cancels_the_token_handed_to_the_provider()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var caller = new CancellationTokenSource();
        var client = new HandshakeClient();
        var availability = NewAvailability(client, cache);

        var check = availability.CheckAsync(Tag, caller.Token);
        caller.Cancel();
        var answer = await check;

        Assert.Equal(new Availability(true, null), answer);
        // The provider is handed the probe's own linked token (the one that times out), not the caller's raw token,
        // and the caller's cancellation still reaches it.
        Assert.NotEqual(caller.Token, client.Handed);
        Assert.True(client.Handed.IsCancellationRequested);
    }

    [Fact]
    public async Task A_payment_required_status_is_a_refusal_the_account_can_do_nothing_about()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var availability = NewAvailability(
            Throwing(new HttpRequestException("payment required for this model", null, HttpStatusCode.PaymentRequired)));

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.NotInPlan), answer);
    }

    [Fact]
    public async Task A_neutral_failure_that_is_not_an_http_error_is_could_not_check()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var availability = NewAvailability(Throwing(new InvalidOperationException("the endpoint is unreachable")));

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.CouldNotCheck), answer);
    }

    [Fact]
    public void The_refusal_classifier_takes_status_codes_and_words_but_not_a_neutral_timeout()
    {
        Assert.True(ModelAvailability.IsRefusal(new HttpRequestException("payment required", null, HttpStatusCode.PaymentRequired)));
        Assert.True(ModelAvailability.IsRefusal(new HttpRequestException("request unauthorized", null, HttpStatusCode.Unauthorized)));
        Assert.False(ModelAvailability.IsRefusal(new TimeoutException("the endpoint is unreachable")));
        Assert.False(ModelAvailability.IsRefusal(new OperationCanceledException()));
    }
}