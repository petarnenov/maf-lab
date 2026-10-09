using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Retrieval.Models;

/// <summary>Creates chat clients; the seam tests and evals use to substitute a scripted model.</summary>
public interface IChatClientFactory
{
    IChatClient CreateChatClient(string? model = null);
    ChatOptions BaseChatOptions();
}

/// <summary>A named chat provider's factory. The clients it creates speak only Microsoft.Extensions.AI.</summary>
public interface IChatModelProvider : IChatClientFactory
{
    string Name { get; }
}

/// <summary>A provider creates generators for a profile and a purpose; wire options stay inside it.</summary>
public interface IEmbeddingsProvider
{
    IEmbeddingGenerator<string, Embedding<float>> CreateGenerator(EmbeddingProfile profile, EmbeddingRole role);
}

/// <summary>Embeds text for one named dense vector, applying the model's document/query prefixes.</summary>
public interface IDenseEncoder
{
    Task<float[]> EmbedQueryAsync(string vectorName, string text, CancellationToken ct);
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(string vectorName, IReadOnlyList<string> texts, CancellationToken ct);
    string ModelVersion(string vectorName);
}

/// <summary>Which Ollama instance an embedding belongs on: search queries, or documents (indexing and other batches).</summary>
public enum EmbeddingRole
{
    Query,
    Documents,
}

/// <summary>A document longer than the embedding model's context window, which the provider refused to cut.</summary>
public sealed class InputTooLongException(string model, int maxInputTokens, Exception inner)
    : Exception($"A document exceeds {model}'s context window of {maxInputTokens} tokens; the chunker must split it smaller.", inner)
{
    public int MaxInputTokens { get; } = maxInputTokens;
}
