using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// Removes the inline citation markers gpt-oss writes into answers — <c>【tool_data†get_billing_run_status】</c>,
/// <c>【sourcePath: procedures/…】</c> — with the whitespace that led into them (strip-citation-markers). The turn's
/// sources are already shown as structured Sources, so the markers only clutter the answer, the history the model reads
/// next turn, the answer check and A2A replies. Sits above the tracing client: the trace's <c>model.response</c> keeps
/// what the model actually wrote. Reasoning, tool calls and usage pass through untouched.
/// </summary>
public sealed class CitationMarkerChatClient(IChatClient inner, Action<int>? onRemoved = null) : DelegatingChatClient(inner)
{
    public const char Open = '【';
    public const char Close = '】';

    /// <summary>An opening bracket not closed within this many characters is ordinary text, released as it was.</summary>
    public const int MaxMarkerLength = 400;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        foreach (var message in response.Messages)
        {
            for (var i = 0; i < message.Contents.Count; i++)
            {
                if (message.Contents[i] is TextContent { Text: { } text } content && text.Contains(Open))
                {
                    var scrubber = new Scrubber();
                    var clean = scrubber.Push(text) + scrubber.Flush();
                    Report(scrubber.Removed);
                    message.Contents[i] = new TextContent(clean) { AdditionalProperties = content.AdditionalProperties };
                }
            }
        }
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var scrubber = new Scrubber();
        ChatResponseUpdate? last = null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            last = update;
            if (!update.Contents.Any(c => c is TextContent))
            {
                yield return update;
                continue;
            }
            var contents = new List<AIContent>(update.Contents.Count);
            foreach (var content in update.Contents)
            {
                if (content is TextContent text)
                {
                    var clean = scrubber.Push(text.Text ?? "");
                    if (clean.Length > 0)
                    {
                        contents.Add(new TextContent(clean) { AdditionalProperties = text.AdditionalProperties });
                    }
                }
                else
                {
                    contents.Add(content);
                }
            }
            // An update that only carried held text still carries its id, finish reason or usage.
            if (contents.Count > 0 || update.FinishReason is not null)
            {
                update.Contents = contents;
                yield return update;
            }
        }
        var rest = scrubber.Flush();
        Report(scrubber.Removed);
        if (rest.Length > 0)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, rest)
            {
                MessageId = last?.MessageId,
                ResponseId = last?.ResponseId,
                ConversationId = last?.ConversationId,
                ModelId = last?.ModelId,
            };
        }
    }

    private void Report(int removed)
    {
        if (removed > 0)
        {
            onRemoved?.Invoke(removed);
        }
    }

    /// <summary>The whole answer at once, for a caller that already has it.</summary>
    public static string Strip(string text, out int removed)
    {
        var scrubber = new Scrubber();
        var clean = scrubber.Push(text) + scrubber.Flush();
        removed = scrubber.Removed;
        return clean;
    }

    /// <summary>
    /// Streams text through, holding back only what might still turn out to belong to a marker: an open marker, and the
    /// whitespace right before a possible one.
    /// </summary>
    private sealed class Scrubber
    {
        private readonly StringBuilder _marker = new();
        private readonly StringBuilder _space = new();
        private bool _inMarker;

        public int Removed { get; private set; }

        public string Push(string text)
        {
            var output = new StringBuilder();
            foreach (var ch in text)
            {
                if (_inMarker)
                {
                    _marker.Append(ch);
                    if (ch == Close)
                    {
                        // The marker and the whitespace that led into it go; what follows starts afresh.
                        _marker.Clear();
                        _space.Clear();
                        _inMarker = false;
                        Removed++;
                    }
                    else if (_marker.Length >= MaxMarkerLength)
                    {
                        output.Append(_space).Append(_marker);
                        _space.Clear();
                        _marker.Clear();
                        _inMarker = false;
                    }
                }
                else if (ch == Open)
                {
                    _inMarker = true;
                    _marker.Append(ch);
                }
                else if (char.IsWhiteSpace(ch))
                {
                    _space.Append(ch);
                }
                else
                {
                    output.Append(_space).Append(ch);
                    _space.Clear();
                }
            }
            return output.ToString();
        }

        /// <summary>The end of the text: whatever is still held was not a marker after all.</summary>
        public string Flush()
        {
            var rest = _space.ToString() + _marker;
            _space.Clear();
            _marker.Clear();
            _inMarker = false;
            return rest;
        }
    }
}
