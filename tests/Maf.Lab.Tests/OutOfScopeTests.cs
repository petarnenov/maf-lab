using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

/// <summary>
/// A conversation that opens with a question outside every domain gets a fixed reply and no model call
/// (refuse-off-domain-questions); what is still the model's to judge — a follow-up, small talk, a failed classification,
/// the refusal switched off — reaches it as before.
/// </summary>
public class OutOfScopeTests : IDisposable
{
    // The stand-in billing and portfolio domains the shared fakes speak, for the static readers.
    private readonly IDisposable _domains = DomainCatalogue.Use(StandInDomains.WithBilling);

    public void Dispose() => _domains.Dispose();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Frogs = "What do frogs eat?";

    private static List<TraceEvent> Trace(IEnumerable<SseEvent> events) =>
        ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();

    private static List<string> Signals(IEnumerable<SseEvent> events) =>
        Trace(events).Single(t => t.Kind == TraceKinds.Signals).Data.GetProperty("signals").EnumerateArray().Select(s => s.GetString()!).ToList();

    private static ScriptedChatClient Answering() => new((_, _, _) => ScriptedChatClient.Text("Frogs eat insects."));

    [Fact]
    public async Task A_first_question_outside_every_domain_gets_the_fixed_reply_without_a_model_call()
    {
        using var api = new ApiFactory(Answering(), jev: new FakeJev { InDomain = 0.02 });
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, Frogs);

        Assert.Equal(OutOfScope.ReplyEnglish, ApiFactory.AnswerOf(events));
        Assert.Empty(api.Chat.Requests);
        Assert.Empty(api.Tools.Invocations);
        Assert.Single(api.Jev.Requests);
        Assert.Contains(TurnSignal.OutOfScope, Signals(events));
        // No system prompt or tool schemas are built for it, as for a refused prompt.
        Assert.DoesNotContain(Trace(events), t => t.Kind == TraceKinds.Prompt);
        // Logged by structure only.
        Assert.DoesNotContain(api.Logs.Messages, m => m.Contains("frogs"));
    }

    [Fact]
    public async Task A_Bulgarian_question_gets_the_Bulgarian_reply()
    {
        using var api = new ApiFactory(Answering(), jev: new FakeJev { InDomain = 0.02 });

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), "Какво яде жабата?");

        Assert.Equal(OutOfScope.ReplyBulgarian, ApiFactory.AnswerOf(events));
        Assert.DoesNotContain("жаба", ApiFactory.AnswerOf(events));
    }

    [Fact]
    public async Task A_follow_up_outside_every_domain_is_left_to_the_model()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.USER);
        var first = await ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing");
        var calls = api.Chat.Requests.Count;

        api.Jev.InDomain = 0.02;
        var second = await ApiFactory.ChatAsync(client, "why?", ApiFactory.ThreadOf(first));

        Assert.True(api.Chat.Requests.Count > calls);
        Assert.NotEqual(OutOfScope.ReplyEnglish, ApiFactory.AnswerOf(second));
        Assert.DoesNotContain(TurnSignal.OutOfScope, Signals(second));
    }

    [Fact]
    public async Task Small_talk_reaches_the_model()
    {
        using var api = new ApiFactory(Answering(), jev: new FakeJev { InDomain = 0.0 });

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), "hello");

        Assert.NotEmpty(api.Chat.Requests);
        Assert.DoesNotContain(TurnSignal.OutOfScope, Signals(events));
    }

    [Fact]
    public async Task With_the_refusal_off_the_model_answers()
    {
        using var api = new ApiFactory(Answering(), jev: new FakeJev { InDomain = 0.02 })
        {
            ExtraSettings = new Dictionary<string, string?> { ["Jev:RefuseOutsideDomains"] = "false" },
        };

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), Frogs);

        Assert.NotEmpty(api.Chat.Requests);
        Assert.DoesNotContain(TurnSignal.OutOfScope, Signals(events));
    }

    [Fact]
    public async Task A_failed_classification_never_refuses()
    {
        using var api = new ApiFactory(Answering(), jev: new FakeJev { InDomain = 0.02, Status = System.Net.HttpStatusCode.InternalServerError });

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), Frogs);

        Assert.NotEmpty(api.Chat.Requests);
        Assert.DoesNotContain(TurnSignal.OutOfScope, Signals(events));
    }
}
