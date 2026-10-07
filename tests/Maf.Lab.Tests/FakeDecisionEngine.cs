using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests;

/// <summary>
/// The core's decision engine in tests (introduce-provider-plugins, T2): the core is tested against its port, not against
/// any engine's transport. It keeps <see cref="FakeJev"/>'s whole rule API — the keyword answers, <see cref="FakeJev.Status"/>,
/// <see cref="FakeJev.Hang"/>, <see cref="FakeJev.HoldWhen"/> — by writing each request as the documented JSON shape
/// (state, typed questions) and letting <see cref="FakeJev"/> answer it, so <see cref="FakeJev.Requests"/> still holds every
/// body a test inspects. Failures come back as the port says: an error status as <c>rejected (status)</c>, an exceeded
/// budget as <c>timed out after Ns</c>, a transport error as its type, and, when <see cref="OpenAfterFailures"/> is set,
/// later requests skipped as <see cref="DecisionFailures.CircuitOpen"/>, as a failing engine's breaker would.
/// </summary>
public sealed class FakeDecisionEngine(FakeJev rules) : IDecisionEngine
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly HttpMessageInvoker _invoker = new(rules, disposeHandler: false);
    private int _failures;

    public FakeJev Rules => rules;

    public string Engine => rules.Model;

    /// <summary>False stands for an engine with no credential: callers skip the request and say why.</summary>
    public bool IsConfigured { get; set; } = true;

    /// <summary>Consecutive transient failures after which requests are skipped; 0 sends every request.</summary>
    public int OpenAfterFailures { get; set; }

    public async Task<DecisionOutcome> DecideAsync(object state, IReadOnlyDictionary<string, DecisionQuestion> questions, TimeSpan budget,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return new DecisionOutcome(null, Engine, null, DecisionFailures.NoKey, 0);
        }
        if (OpenAfterFailures > 0 && Volatile.Read(ref _failures) >= OpenAfterFailures)
        {
            return new DecisionOutcome(null, Engine, null, DecisionFailures.CircuitOpen, 0, Skipped: true);
        }
        var sw = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://engine.test/decide")
        {
            Content = new StringContent(Body(Engine, state, questions), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", FakeJev.TestKey);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(budget);
            var call = _invoker.SendAsync(request, cts.Token);
            if (await Task.WhenAny(call, Task.Delay(budget, ct)) != call)
            {
                _ = call.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                return Failed($"timed out after {budget.TotalSeconds}s", sw, transient: true);
            }
            using var response = await call;
            // An answer that came back after the budget is read with the budget's token, as a real transport reads it.
            cts.Token.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                return Failed($"rejected ({code})", sw, transient: code is 408 or 429 or >= 500);
            }
            var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cts.Token))!;
            Interlocked.Exchange(ref _failures, 0);
            var answers = (root["answers"] as JsonObject ?? []).ToDictionary(a => a.Key, a => Answer(a.Value!), StringComparer.Ordinal);
            var usage = root["usage"] is JsonObject u
                ? new DecisionUsage(u["input_tokens"]?.GetValue<int>(), u["output_tokens"]?.GetValue<int>())
                : null;
            return new DecisionOutcome(answers, root["model"]?.GetValue<string>() ?? Engine, usage, null, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed($"timed out after {budget.TotalSeconds}s", sw, transient: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(ex.GetType().Name, sw, transient: ex is HttpRequestException);
        }
    }

    private DecisionOutcome Failed(string reason, Stopwatch sw, bool transient)
    {
        if (transient)
        {
            Interlocked.Increment(ref _failures);
        }
        return new DecisionOutcome(null, Engine, null, reason, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>The serializer options of the documented request shape (web defaults, as the engines send it).</summary>
    public static JsonSerializerOptions Json => Web;

    /// <summary>One question as the documented JSON shape: its instructions, its criteria, its type.</summary>
    public static JsonObject QuestionNode(DecisionQuestion question)
    {
        var node = new JsonObject { ["instructions"] = JsonSerializer.SerializeToNode(question.Instructions, question.Instructions.GetType(), Web) };
        switch (question)
        {
            case ChoiceQuestion c:
                node["criteria"] = JsonSerializer.SerializeToNode(c.Criteria, Web);
                node["type"] = "choice";
                break;
            case NoulQuestion { Criteria: { } criteria }:
                node["criteria"] = new JsonObject { ["true"] = criteria.Yes, ["false"] = criteria.No };
                node["type"] = "noul";
                break;
            case NoulQuestion:
                node["type"] = "noul";
                break;
            case ScoreQuestion s:
                node["criteria"] = JsonSerializer.SerializeToNode(s.Levels, Web);
                node["type"] = "score";
                break;
        }
        return node;
    }

    /// <summary>The questions as the documented JSON object, in their order.</summary>
    public static JsonObject QuestionsNode(IReadOnlyDictionary<string, DecisionQuestion> questions)
    {
        var asked = new JsonObject();
        foreach (var (id, question) in questions)
        {
            asked[id] = QuestionNode(question);
        }
        return asked;
    }

    /// <summary>The questions as the documented JSON text.</summary>
    public static string QuestionsJson(IReadOnlyDictionary<string, DecisionQuestion> questions) => QuestionsNode(questions).ToJsonString(Web);

    /// <summary>The request as the documented JSON shape: the engine, the state, and each question with its type.</summary>
    public static string Body(string engine, object state, IReadOnlyDictionary<string, DecisionQuestion> questions)
    {
        var body = new JsonObject
        {
            ["model"] = engine,
            ["state"] = JsonSerializer.SerializeToNode(state, state.GetType(), Web),
            ["questions"] = QuestionsNode(questions),
        };
        return body.ToJsonString(Web);
    }

    private static DecisionAnswer Answer(JsonNode a) => new(
        a["choice"]?.GetValue<string>(),
        a["confidence"]?.GetValue<double>(),
        a["noul"]?.GetValue<double>(),
        a["probabilities"] is JsonObject p ? p.ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<double>()) : null);
}
