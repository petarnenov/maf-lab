using System.Text.Json;

namespace Maf.Lab.Retrieval.Jev;

// The wire shape of POST /v1/systemone (https://docs.typesafe.ai/api). None of these types has a field that could carry
// a credential: the key travels in a header set by JevAuthHandler and nowhere else.

/// <remarks>
/// The state is whatever named fields the caller asks about — the intent classifier's question, a search's query and
/// passages — serialised as its runtime type. Questions are typed the same way (Choice or Noul).
/// </remarks>
public sealed record JevRequest(string Model, object State, IReadOnlyDictionary<string, object> Questions)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

public sealed record JevChoiceQuestion(object Instructions, IReadOnlyDictionary<string, string> Criteria)
{
    public string Type => "choice";
}

/// <summary>A yes/no question; the answer is the probability of yes.</summary>
public sealed record JevNoulQuestion(object Instructions)
{
    public string Type => "noul";
}

public sealed record JevResponse(string? Model, Dictionary<string, JevAnswer>? Answers, JevUsage? Usage = null);

/// <summary>What a request was charged for (jev-usage §4.6: log it per call). Numbers only.</summary>
public sealed record JevUsage(
    [property: System.Text.Json.Serialization.JsonPropertyName("input_tokens")] int? InputTokens,
    [property: System.Text.Json.Serialization.JsonPropertyName("output_tokens")] int? OutputTokens);

/// <summary>A Choice answer carries choice, probabilities and confidence; a Noul answer carries noul.</summary>
public sealed record JevAnswer(string? Type, string? Choice, Dictionary<string, double>? Probabilities, double? Confidence,
    double? Noul = null);
