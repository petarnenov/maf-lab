using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Maf.Lab.Tests;

/// <summary>
/// Stands in for TypeSafe's Jev at the HTTP boundary: answers <c>POST /v1/systemone</c> with one Choice answer in the
/// documented shape, and records what it was sent (the authorization header and the body) so tests can check both.
/// It classifies with keyword rules — English and the Bulgarian words the tests use — because a test double has to be
/// deterministic; the real classifier has no rules.
/// </summary>
public sealed partial class FakeJev : HttpMessageHandler
{
    /// <summary>A test value, not a secret: what the fake expects as the bearer token.</summary>
    public const string TestKey = "test-jev-key-0f6c2d";

    public ConcurrentQueue<(string? Authorization, string Body)> Requests { get; } = new();

    /// <summary>The confidence every answer reports.</summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>When set, every request is answered with this status and no body.</summary>
    public HttpStatusCode? Status { get; set; }

    /// <summary>When set, replaces the keyword rules.</summary>
    public Func<string, string>? Choose { get; set; }

    /// <summary>When set, the fake waits this long and ignores cancellation, like a transport that never answers.</summary>
    public TimeSpan? Hang { get; set; }

    public string Model { get; set; } = "jev-1.13.0";

    /// <summary>The Content-Length the last request declared before its body was read; null means it was streamed.</summary>
    public long? LastContentLength { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastContentLength = request.Content?.Headers.ContentLength;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request.Headers.Authorization?.ToString(), body));
        if (Hang is { } hang)
        {
            await Task.Delay(hang, CancellationToken.None);
        }
        if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: TestKey })
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
        if (Status is { } status)
        {
            return new HttpResponseMessage(status);
        }

        var question = JsonNode.Parse(body)!["state"]!["user_question"]!.GetValue<string>();
        var choice = (Choose ?? Classify)(question);
        var answer = new
        {
            model = Model,
            answers = new Dictionary<string, object>
            {
                ["intent"] = new
                {
                    type = "choice",
                    choice,
                    confidence = Confidence,
                    probabilities = new[] { "procedural", "mixed", "data", "chitchat", "other" }
                        .ToDictionary(o => o, o => o == choice ? Confidence : Math.Round((1 - Confidence) / 4, 4)),
                },
            },
            usage = new { input_tokens = 400, output_tokens = 50 },
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(answer), Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Keyword classification into Jev's option names.</summary>
    public static string Classify(string question)
    {
        var q = question.Trim().ToLowerInvariant();
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var procedural = Procedural().IsMatch(q);
        var run = RunReference().IsMatch(q);
        if (words <= 8 && ChitChat().IsMatch(q) && !procedural)
        {
            return "chitchat";
        }
        if (procedural)
        {
            return run ? "mixed" : "procedural";
        }
        return run || DataWords().IsMatch(q) ? "data" : "other";
    }

    [GeneratedRegex(@"^(thanks|thank you|thx|ok|okay|great|perfect|bye|goodbye|hello|hi|hey|cheers|got it|здравей|здрасти|благодаря|мерси|чао)(?!\p{L})")]
    private static partial Regex ChitChat();

    [GeneratedRegex(@"\b(how|why|procedure|process|steps?|explain|what is|what's|what are|what does|define|definition|meaning|policy|what should|what to do|when should|guide)\b|(?<!\p{L})(как|защо|процедура|процедурата|обясни|политика)(?!\p{L})")]
    private static partial Regex Procedural();

    [GeneratedRegex(@"\brun\s*#?\s*\d{3,}\b|#\d{3,}\b|(?<!\p{L})рън\s*#?\s*\d{3,}")]
    private static partial Regex RunReference();

    [GeneratedRegex(@"\b(status|state of|which runs|list (the )?runs|show (me )?(the )?runs|failed runs|pending runs|latest runs?|runs? (that )?failed)\b|(?<!\p{L})(статус|списък|колко)(?!\p{L})")]
    private static partial Regex DataWords();
}
