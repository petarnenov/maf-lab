using Microsoft.ML.Tokenizers;

namespace Maf.Lab.Api.Agent;

/// <summary>Token estimate for the conversation window (o200k encoding; close enough for local models).</summary>
public sealed class TokenCounter
{
    private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

    public int Count(string text) => _tokenizer.CountTokens(text);
}
