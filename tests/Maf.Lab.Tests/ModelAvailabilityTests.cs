using System.Net;
using Maf.Lab.Api.Coverage;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Maf.Lab.Tests;

/// <summary>
/// The model picker's availability check (model-selection): a one-token probe carrying nothing but itself, the answer
/// reused per replica, and a refusal the account can do nothing about told apart from a hiccup.
/// </summary>
public sealed class ModelAvailabilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Tag = "glm-5.3:cloud";

    /// <summary>Hands one client out to every model, and remembers which models were asked for.</summary>
    private sealed class CountingFactory(IChatClient client, ChatOptions? baseOptions = null) : IChatClientFactory
    {
        public List<string?> Asked { get; } = [];

        public IChatClient CreateChatClient(string? model = null)
        {
            Asked.Add(model);
            return client;
        }

        public ChatOptions BaseChatOptions() => baseOptions ?? new();
    }

    /// <summary>Cannot create any client at all; the check must still answer rather than throw.</summary>
    private sealed class ExplodingFactory : IChatClientFactory
    {
        public IChatClient CreateChatClient(string? model = null) => throw new InvalidOperationException("client factory is offline");

        public ChatOptions BaseChatOptions() => new();
    }

    private static ScriptedChatClient Answering(string text) => new((_, _, _) => ScriptedChatClient.Text(text));

    private static ScriptedChatClient Throwing(Exception error) => new((_, _, _) => throw error);

    private static (ModelAvailability Availability, CountingFactory Factory) NewAvailability(IChatClient client,
        IMemoryCache? cache = null, TestAgentOptions? options = null, ILogger<ModelAvailability>? logger = null,
        ChatOptions? baseOptions = null)
    {
        var factory = new CountingFactory(client, baseOptions);
        return (new ModelAvailability(factory, cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options ?? new TestAgentOptions()), logger ?? NullLogger<ModelAvailability>.Instance), factory);
    }

    [Fact]
    public async Task The_probe_is_a_single_one_token_user_message_and_nothing_else()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var chat = Answering("OK");
        var (availability, factory) = NewAvailability(chat, cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(true, null), answer);
        var request = Assert.Single(chat.Requests);
        var message = Assert.Single(request.Messages);
        Assert.Equal((ChatRole.User, ModelAvailability.Probe), (message.Role, message.Text));
        Assert.Equal(1, request.Options!.MaxOutputTokens);
        Assert.Equal(Tag, Assert.Single(factory.Asked));
    }

    [Fact]
    public async Task The_probe_overrides_the_base_options_max_output_tokens_but_keeps_the_rest()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var chat = Answering("OK");
        // Whatever the factory's base options say, the probe itself is capped at one token.
        var (availability, factory) = NewAvailability(chat, cache,
            baseOptions: new ChatOptions { MaxOutputTokens = 500, Temperature = 0.3f });

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(true, null), answer);
        var request = Assert.Single(chat.Requests);
        Assert.Equal(1, request.Options!.MaxOutputTokens);
        Assert.Equal((float?)0.3f, request.Options!.Temperature);
        Assert.Null(request.Options!.Tools);
        Assert.Equal(Tag, Assert.Single(factory.Asked));
    }

    [Fact]
    public async Task A_second_check_within_the_cache_window_does_not_ask_the_provider_again()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var (availability, factory) = NewAvailability(Answering("OK"), cache);

        var first = await availability.CheckAsync(Tag, Ct);
        var second = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(first, second);
        Assert.Single(factory.Asked);
    }

    [Fact]
    public async Task A_cached_answer_is_returned_without_asking_the_provider()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var known = new Availability(true, null);
        // The key the check itself caches under (model-availability:{tag}).
        cache.Set($"model-availability:{Tag}", known, TimeSpan.FromMinutes(10));
        var (availability, factory) = NewAvailability(Answering("OK"), cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(known, answer);
        Assert.Empty(factory.Asked);
    }

    [Fact]
    public async Task A_cached_refusal_is_returned_without_asking_the_provider()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var known = new Availability(false, ModelAvailability.NotInPlan);
        cache.Set($"model-availability:{Tag}", known, TimeSpan.FromMinutes(10));
        var (availability, factory) = NewAvailability(Answering("OK"), cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(known, answer);
        Assert.Empty(factory.Asked);
    }

    [Fact]
    public async Task A_cache_entry_that_holds_no_answer_is_ignored()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        // A cached entry whose value is not an answer must not be handed out as one.
        cache.Set($"model-availability:{Tag}", (Availability?)null, TimeSpan.FromMinutes(10));
        var (availability, factory) = NewAvailability(Answering("OK"), cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(true, null), answer);
        Assert.Single(factory.Asked);
    }

    [Fact]
    public async Task Each_model_s_answer_is_cached_under_its_own_tag()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var (availability, factory) = NewAvailability(Answering("OK"), cache);

        await availability.CheckAsync(Tag, Ct);
        await availability.CheckAsync("qwen-4:cloud", Ct);
        await availability.CheckAsync(Tag, Ct);

        // The reuse is per replica and per model: the second model is asked, the first is not asked twice.
        Assert.Equal(new[] { Tag, "qwen-4:cloud" }, factory.Asked);
    }

    [Fact]
    public async Task One_model_s_refusal_does_not_mark_another_model_unavailable()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var asked = 0;
        var chat = new ScriptedChatClient((_, _, _) =>
        {
            if (Interlocked.Increment(ref asked) == 1) throw new HttpRequestException("not included in your plan");
            return ScriptedChatClient.Text("OK");
        });
        var (availability, factory) = NewAvailability(chat, cache);

        var first = await availability.CheckAsync(Tag, Ct);
        var second = await availability.CheckAsync("qwen-4:cloud", Ct);

        Assert.Equal(new Availability(false, ModelAvailability.NotInPlan), first);
        Assert.Equal(new Availability(true, null), second);
        Assert.Equal(new[] { Tag, "qwen-4:cloud" }, factory.Asked);
    }

    [Fact]
    public async Task The_answer_is_cached_under_the_model_s_tag()
    {
        var cache = Substitute.For<IMemoryCache>();
        var entry = Substitute.For<ICacheEntry>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var (availability, _) = NewAvailability(Answering("OK"), cache);

        await availability.CheckAsync(Tag, Ct);

        cache.Received(1).CreateEntry($"model-availability:{Tag}");
    }

    [Fact]
    public async Task The_cached_entry_holds_the_answer_itself()
    {
        var cache = Substitute.For<IMemoryCache>();
        var entry = Substitute.For<ICacheEntry>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var (availability, _) = NewAvailability(Answering("OK"), cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        entry.Received().Value = answer;
    }

    [Fact]
    public async Task An_available_answer_is_kept_for_the_configured_window()
    {
        var cache = Substitute.For<IMemoryCache>();
        var entry = Substitute.For<ICacheEntry>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var (availability, _) = NewAvailability(Answering("OK"), cache,
            new TestAgentOptions { AvailabilityCacheFor = TimeSpan.FromMinutes(3) });

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(true, null), answer);
        entry.Received().AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3);
    }

    [Fact]
    public async Task An_available_answer_is_asked_again_once_the_configured_window_has_expired()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var (availability, factory) = NewAvailability(Answering("OK"), cache,
            options: new TestAgentOptions { AvailabilityCacheFor = TimeSpan.FromMilliseconds(50) });

        var first = await availability.CheckAsync(Tag, Ct);
        await Task.Delay(200);
        var second = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(first, second);
        Assert.Equal(2, factory.Asked.Count);
    }

    [Fact]
    public async Task A_refusal_marks_the_model_unavailable_and_is_kept_for_the_configured_window()
    {
        var cache = Substitute.For<IMemoryCache>();
        var entry = Substitute.For<ICacheEntry>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var chat = Throwing(new HttpRequestException("this model is not included in your free usage", null, HttpStatusCode.Forbidden));
        var (availability, _) = NewAvailability(chat, cache, new TestAgentOptions { AvailabilityCacheFor = TimeSpan.FromMinutes(7) });

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.NotInPlan), answer);
        entry.Received().AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(7);
    }

    [Fact]
    public async Task A_refused_model_is_not_asked_again_within_the_cache_window()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var (availability, factory) = NewAvailability(Throwing(new HttpRequestException("not included in your plan")), cache);

        var first = await availability.CheckAsync(Tag, Ct);
        var second = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.NotInPlan), first);
        Assert.Equal(first, second);
        Assert.Single(factory.Asked);
    }

    [Fact]
    public async Task A_refusal_in_the_provider_s_own_words_also_marks_the_model_unavailable()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        // No status code to read: Ollama Cloud words the refusal ("not found", "not included") in the message.
        var chat = Throwing(new HttpRequestException("model glm-5.3:cloud not found for this account"));
        var (availability, _) = NewAvailability(chat, cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.NotInPlan), answer);
    }

    [Fact]
    public async Task A_refusal_hidden_in_an_inner_exception_is_still_recognised()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        // The provider's SDK wraps the refusal: the outer message is neutral, the inner one names the account.
        var chat = Throwing(new HttpRequestException("the request failed",
            new HttpRequestException("model glm-5.3:cloud not found for this account")));
        var (availability, _) = NewAvailability(chat, cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.NotInPlan), answer);
    }

    [Fact]
    public async Task An_unauthorised_status_code_marks_the_model_unavailable()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        // A 401 carries no telling words, only the status: still a refusal the account can do nothing about quickly.
        var chat = Throwing(new HttpRequestException("response status code does not indicate success", null, HttpStatusCode.Unauthorized));
        var (availability, _) = NewAvailability(chat, cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.NotInPlan), answer);
    }

    [Fact]
    public async Task A_refusal_is_logged_and_names_the_model()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var logs = new CapturingLoggerProvider();
        using var loggers = LoggerFactory.Create(b => b.AddProvider(logs));
        var (availability, _) = NewAvailability(Throwing(new HttpRequestException("not included in your plan")), cache,
            logger: loggers.CreateLogger<ModelAvailability>());

        await availability.CheckAsync(Tag, Ct);

        Assert.Contains(logs.Messages, m => m.Contains("refused") && m.Contains(Tag));
    }

    [Fact]
    public async Task A_transient_failure_is_not_an_answer_worth_keeping_for_ten_minutes()
    {
        var cache = Substitute.For<IMemoryCache>();
        var entry = Substitute.For<ICacheEntry>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var chat = Throwing(new HttpRequestException("connection refused", null, HttpStatusCode.BadGateway));
        var (availability, _) = NewAvailability(chat, cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.CouldNotCheck), answer);
        entry.Received().AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
    }

    [Fact]
    public async Task A_could_not_check_answer_is_still_reused_for_a_short_window()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var (availability, factory) = NewAvailability(
            Throwing(new HttpRequestException("connection refused", null, HttpStatusCode.BadGateway)), cache);

        var first = await availability.CheckAsync(Tag, Ct);
        var second = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.CouldNotCheck), first);
        Assert.Equal(first, second);
        Assert.Single(factory.Asked);
    }

    [Fact]
    public async Task An_unexpected_failure_is_logged_as_a_warning_naming_the_model()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var logs = new CapturingLoggerProvider();
        using var loggers = LoggerFactory.Create(b => b.AddProvider(logs));
        var (availability, _) = NewAvailability(
            Throwing(new HttpRequestException("connection refused", null, HttpStatusCode.BadGateway)), cache,
            logger: loggers.CreateLogger<ModelAvailability>());

        await availability.CheckAsync(Tag, Ct);

        Assert.Contains(logs.Messages, m => m.Contains(Tag) && m.Contains("HttpRequestException"));
    }

    [Fact]
    public async Task A_failure_to_create_the_client_is_reported_as_could_not_check()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var availability = new ModelAvailability(new ExplodingFactory(), cache,
            Options.Create(new TestAgentOptions()), NullLogger<ModelAvailability>.Instance);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.CouldNotCheck), answer);
    }

    [Fact]
    public async Task A_timeout_is_reported_as_could_not_check_and_logged()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var logs = new CapturingLoggerProvider();
        using var loggers = LoggerFactory.Create(b => b.AddProvider(logs));
        var (availability, _) = NewAvailability(Throwing(new OperationCanceledException()), cache,
            logger: loggers.CreateLogger<ModelAvailability>());

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.CouldNotCheck), answer);
        Assert.Contains(logs.Messages, m => m.Contains("timed out") && m.Contains(Tag));
    }

    [Fact]
    public async Task A_timeout_answer_is_kept_only_for_thirty_seconds()
    {
        var cache = Substitute.For<IMemoryCache>();
        var entry = Substitute.For<ICacheEntry>();
        cache.CreateEntry(Arg.Any<object>()).Returns(entry);
        var (availability, _) = NewAvailability(Throwing(new OperationCanceledException()), cache);

        var answer = await availability.CheckAsync(Tag, Ct);

        Assert.Equal(new Availability(false, ModelAvailability.CouldNotCheck), answer);
        entry.Received().AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
    }

    [Fact]
    public async Task A_check_the_caller_cancelled_is_not_answered()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var (availability, _) = NewAvailability(Throwing(new OperationCanceledException()), cache);

        await Assert.ThrowsAsync<OperationCanceledException>(() => availability.CheckAsync(Tag, new CancellationToken(canceled: true)));
    }
}