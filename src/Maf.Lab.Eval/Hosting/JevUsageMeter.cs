using System.Text.Json;
using Maf.Lab.Retrieval.Jev;

namespace Maf.Lab.Eval.Hosting;

/// <summary>
/// The input tokens Jev charged the eval for (extract-billing): the suites that call Jev through the production classes
/// (the guard, the answer check, the intent classifier) never see a response's usage, so the eval reads it off the wire.
/// </summary>
public sealed class JevUsageMeter
{
    private int _inputTokens;

    public int InputTokens => Volatile.Read(ref _inputTokens);

    internal void Add(int tokens) => Interlocked.Add(ref _inputTokens, tokens);

    /// <summary>Starts a count from zero, for one suite.</summary>
    public void Reset() => Interlocked.Exchange(ref _inputTokens, 0);
}

/// <summary>Reads each Jev response's <c>usage.input_tokens</c> into the <see cref="JevUsageMeter"/>; changes nothing.</summary>
internal sealed class JevUsageHandler(JevUsageMeter meter) : DelegatingHandler
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode && response.Content is { } content)
        {
            // Buffered and read as bytes: reading it as a stream would dispose the one the caller reads next.
            await content.LoadIntoBufferAsync(cancellationToken);
            try
            {
                var body = JsonSerializer.Deserialize<JevResponse>(await content.ReadAsByteArrayAsync(cancellationToken), Web);
                meter.Add(body?.Usage?.InputTokens ?? 0);
            }
            catch (JsonException)
            {
                // Not a Jev answer: nothing to count, and the caller reads it as before.
            }
        }
        return response;
    }
}
