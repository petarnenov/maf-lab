using Maf.Lab.Plugins.Coverage;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Maf.Lab.Tests;

/// <summary>
/// The reuse of the availability answer (model-selection): a hit is handed out as-is without writing the cache
/// again, whatever the answer says, every checker on the same replica shares it, and an entry that holds no
/// answer is not mistaken for one.
/// </summary>
public sealed class ModelAvailabilityCacheTests
{
    private const string Tag = "glm-5.3:cloud";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Hands one client out to every model, and remembers which models were asked for.</summary>
    private sealed class CountingFactory(IChatClient client) : IChatClientFactory
    {
        public List<string?> Asked { get; } = [];

        public IChatClient CreateChatClient(string? model = null)
        {
            Asked.Add(model);
            return client;
        }

        public ChatOptions BaseChatOptions() => new();
    }

    private static ScriptedChatClient Answering(string text) => new((_, _, _) => ScriptedChatClient.Text(text));

    private static ScriptedChatClient Throwing(Exception error) => new((_, _, _) => throw error);

    private static (ModelAvailability Availability, CountingFactory Factory) NewAvailability(IChatClient client,
        IMemoryCache cache)
    {
        var factory = new CountingFactory(client);
        return (new ModelAvailability(factory, cache, Options.Create(new TestAgentOptions()),
            NullLogger<ModelAvailability>.Instance), factory);
    }

    [Fact]
    public async Task A_cache_hit_is_handed_out_without_touching_the_cache_again()
    {
        Availability? known = new Availability(true, null);
        var cache = Substitute.For<IMemoryCache>();
        cache.TryGetValue(Arg.Any<object>(), out Arg.Any<Availability?>())
            .Returns(call =>
            {
                call[1] = known;
                return true;
            });
        var (availability, factory) = NewAvailability(Answering("OK"), cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(known, answer);
        Assert.Empty(factory.Asked);
        // A hit must not overwrite the entry it just read.
        cache.DidNotReceive().CreateEntry(Arg.Any<object>());
    }

    [Fact]
    public async Task A_cached_could_not_check_answer_is_returned_without_asking_the_provider()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var known = new Availability(false, ModelAvailability.CouldNotCheck);
        cache.Set($"model-availability:{Tag}", known, TimeSpan.FromSeconds(30));
        var (availability, factory) = NewAvailability(Throwing(new HttpRequestException("connection refused")), cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(known, answer);
        Assert.Empty(factory.Asked);
    }

    [Fact]
    public async Task Every_checker_on_the_same_replica_shares_one_answer()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var (first, firstFactory) = NewAvailability(Answering("OK"), cache);
        var (second, secondFactory) = NewAvailability(Answering("OK"), cache);

        var one = await first.CheckAsync(Tag, Ct);
        var two = await second.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(true, null), one);
        Assert.Equal(one, two);
        Assert.Single(firstFactory.Asked);
        Assert.Empty(secondFactory.Asked);
    }

    [Fact]
    public async Task A_cache_entry_that_holds_something_other_than_an_answer_is_ignored()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        // Whatever else a replica keeps under that key, it is not an answer and must not be handed out as one.
        cache.Set($"model-availability:{Tag}", (object)"not an availability", TimeSpan.FromMinutes(10));
        var (availability, factory) = NewAvailability(Answering("OK"), cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(true, null), answer);
        Assert.Single(factory.Asked);
    }
}
