using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maf.Lab.Api.Agent.Jev;

// The wire shape of POST /v1/systemone for one Choice question (https://docs.typesafe.ai/api). None of these types has
// a field that could carry a credential: the key travels in a header set by JevAuthHandler and nowhere else.

internal sealed record JevRequest(string Model, JevState State, IReadOnlyDictionary<string, JevChoiceQuestion> Questions)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

/// <summary>The question travels as a named field of the state: data to classify, never part of the instructions.</summary>
internal sealed record JevState([property: JsonPropertyName("user_question")] string UserQuestion);

internal sealed record JevChoiceQuestion(string Instructions, IReadOnlyDictionary<string, string> Criteria)
{
    public string Type => "choice";
}

internal sealed record JevResponse(string? Model, Dictionary<string, JevChoiceAnswer>? Answers);

internal sealed record JevChoiceAnswer(string? Type, string? Choice, Dictionary<string, double>? Probabilities, double? Confidence);
