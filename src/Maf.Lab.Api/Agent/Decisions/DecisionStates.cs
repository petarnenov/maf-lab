using System.Text.Json.Serialization;

namespace Maf.Lab.Api.Agent.Decisions;

// The states the core's decision requests are asked over. The questions and answers are the port's
// (Maf.Lab.Plugins.Abstractions); the wire shape is the installed engine's.

/// <summary>The question travels as a named field of the state: data to classify, never part of the instructions.</summary>
internal sealed record QuestionState([property: JsonPropertyName("user_question")] string UserQuestion);

/// <summary>
/// Instructions with the data they refer to beside the question, which names it in backticks
/// (https://docs.typesafe.ai/primitives/advanced).
/// </summary>
internal sealed record DomainInstructions(string Domain, string Languages, string Question);
