using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maf.Lab.Api.Agent.Jev;

// The wire shape of POST /v1/systemone for the intent Choice and the domain Noul (https://docs.typesafe.ai/api). None of these types has
// a field that could carry a credential: the key travels in a header set by JevAuthHandler and nowhere else.

/// <remarks>Questions are typed by their runtime type (Choice or Noul): the serializer writes each value as what it is.</remarks>
internal sealed record JevRequest(string Model, JevState State, IReadOnlyDictionary<string, object> Questions)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

/// <summary>The question travels as a named field of the state: data to classify, never part of the instructions.</summary>
internal sealed record JevState([property: JsonPropertyName("user_question")] string UserQuestion);

internal sealed record JevChoiceQuestion(string Instructions, IReadOnlyDictionary<string, string> Criteria)
{
    public string Type => "choice";
}

/// <summary>A yes/no question; the answer is the probability of yes.</summary>
internal sealed record JevNoulQuestion(object Instructions)
{
    public string Type => "noul";
}

/// <summary>
/// Instructions with the data they refer to beside the question, which names it in backticks
/// (https://docs.typesafe.ai/primitives/advanced).
/// </summary>
internal sealed record JevDomainInstructions(string Domain, string Languages, string Question);

internal sealed record JevResponse(string? Model, Dictionary<string, JevAnswer>? Answers);

/// <summary>A Choice answer carries choice, probabilities and confidence; a Noul answer carries noul.</summary>
internal sealed record JevAnswer(string? Type, string? Choice, Dictionary<string, double>? Probabilities, double? Confidence,
    double? Noul = null);
