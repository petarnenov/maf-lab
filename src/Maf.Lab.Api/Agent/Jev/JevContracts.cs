using System.Text.Json.Serialization;

namespace Maf.Lab.Api.Agent.Jev;

// The intent classifier's part of the Jev request. The wire shape shared with the relevance judge (request, questions,
// answers) lives in Maf.Lab.Retrieval.Jev, next to the credential.

/// <summary>The question travels as a named field of the state: data to classify, never part of the instructions.</summary>
internal sealed record JevState([property: JsonPropertyName("user_question")] string UserQuestion);

/// <summary>
/// Instructions with the data they refer to beside the question, which names it in backticks
/// (https://docs.typesafe.ai/primitives/advanced).
/// </summary>
internal sealed record JevDomainInstructions(string Domain, string Languages, string Question);
