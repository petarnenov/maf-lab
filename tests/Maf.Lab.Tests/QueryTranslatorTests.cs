using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The query is brought into the corpus language before it is embedded or encoded — and only when it has to be.
/// Every failure path keeps the original query, so search degrades instead of breaking.
/// </summary>
public class QueryTranslatorTests
{
    private const string Bulgarian = "каква е процедурата когато липсва фий схема";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (LlmQueryTranslator Translator, ScriptedChatClient Model) Build(
        Func<IReadOnlyList<ChatMessage>, ChatResponseUpdate[]> answer, double timeoutSeconds = 5, int cacheSize = 500)
    {
        var model = new ScriptedChatClient((messages, _, _) => answer(messages));
        return (new LlmQueryTranslator(
            new FixedChatClientFactory(model),
            Options.Create(new ModelOptions { TranslationModel = "translate-model" }),
            Options.Create(new RetrievalOptions { TranslationTimeoutSeconds = timeoutSeconds, TranslationCacheSize = cacheSize }),
            NullLogger<LlmQueryTranslator>.Instance), model);
    }

    [Theory]
    [InlineData("what is the procedure when a fee schedule is missing")]
    [InlineData("how do I resolve a CUSTODIAN-MISMATCH failure")]
    [InlineData("run 4417 status?")]
    public async Task A_query_in_the_corpus_language_is_never_sent_anywhere(string query)
    {
        var (translator, model) = Build(_ => throw new InvalidOperationException("must not be called"));

        var result = await translator.ToCorpusLanguageAsync(query, Ct);

        Assert.Equal(query, result.Searched);
        Assert.False(result.Changed);
        Assert.Null(result.DurationMs);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task A_query_in_another_language_is_translated_and_the_model_is_told_what_to_keep()
    {
        var (translator, model) = Build(_ => ScriptedChatClient.Text("what is the procedure when a fee schedule is missing"));

        var result = await translator.ToCorpusLanguageAsync(Bulgarian, Ct);

        Assert.Equal("what is the procedure when a fee schedule is missing", result.Searched);
        Assert.Equal(Bulgarian, result.Original);
        Assert.True(result.Changed);
        Assert.NotNull(result.DurationMs);
        Assert.Null(result.Reason);

        var (messages, options) = Assert.Single(model.Requests);
        Assert.Contains(LlmQueryTranslator.PromptMarker, messages[0].Text);
        Assert.Contains("English", messages[0].Text);
        Assert.Contains("identifiers", messages[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<query>\n{Bulgarian}\n</query>", messages[1].Text);
        Assert.Null(options?.Tools);
    }

    [Fact]
    public async Task Identifiers_survive_because_they_are_kept_as_written()
    {
        var (translator, _) = Build(messages =>
            // A model that copies the code through, as the prompt asks; the assertion is that nothing mangles it.
            ScriptedChatClient.Text(messages[1].Text!.Contains("FS-REQUIRED")
                ? "how do I resolve an FS-REQUIRED failure for run 4417"
                : "something else"));

        var result = await translator.ToCorpusLanguageAsync("как да разреша грешка FS-REQUIRED за рън 4417", Ct);

        Assert.Contains("FS-REQUIRED", result.Searched);
        Assert.Contains("4417", result.Searched);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Разбира се, ето превода")] // still not in the corpus language
    [InlineData("line one\nline two")]
    public async Task An_unusable_answer_keeps_the_original_query(string answer)
    {
        var (translator, _) = Build(_ => answer.Length == 0 ? [] : ScriptedChatClient.Text(answer));

        var result = await translator.ToCorpusLanguageAsync(Bulgarian, Ct);

        Assert.Equal(Bulgarian, result.Searched);
        Assert.False(result.Changed);
        Assert.Equal("translation was not usable", result.Reason);
    }

    [Fact]
    public async Task A_failing_model_keeps_the_original_query()
    {
        var (translator, _) = Build(_ => throw new HttpRequestException("connection refused"));

        var result = await translator.ToCorpusLanguageAsync(Bulgarian, Ct);

        Assert.Equal(Bulgarian, result.Searched);
        Assert.Equal("HttpRequestException", result.Reason);
    }

    [Fact]
    public async Task A_timeout_keeps_the_original_query()
    {
        var translator = new LlmQueryTranslator(
            new FixedChatClientFactory(new SlowChatClient()),
            Options.Create(new ModelOptions()),
            Options.Create(new RetrievalOptions { TranslationTimeoutSeconds = 0.2 }),
            NullLogger<LlmQueryTranslator>.Instance);

        var result = await translator.ToCorpusLanguageAsync(Bulgarian, Ct);

        Assert.Equal(Bulgarian, result.Searched);
        Assert.Equal("no answer within 0.2s", result.Reason);
    }

    [Fact]
    public async Task A_repeated_query_is_translated_once()
    {
        var (translator, model) = Build(_ => ScriptedChatClient.Text("what is the procedure when a fee schedule is missing"));

        var first = await translator.ToCorpusLanguageAsync(Bulgarian, Ct);
        var second = await translator.ToCorpusLanguageAsync(Bulgarian, Ct);

        Assert.Equal(first.Searched, second.Searched);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task Disabled_normalisation_searches_every_query_as_written()
    {
        var translator = new NoOpQueryTranslator();

        var result = await translator.ToCorpusLanguageAsync(Bulgarian, Ct);

        Assert.Equal(Bulgarian, result.Searched);
        Assert.False(result.Changed);
    }
}

/// <summary>Answers far later than any timeout, ignoring cancellation, like a provider that has stopped responding.</summary>
internal sealed class SlowChatClient : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, "too late"));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "too late");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
