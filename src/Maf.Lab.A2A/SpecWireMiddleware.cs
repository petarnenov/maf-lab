using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Maf.Lab.A2A;

/// <summary>
/// Puts <see cref="SpecWire"/> on the wire: every A2A request is translated into the dialect the preview SDK
/// understands, and everything the SDK answers — a JSON document or a stream of events — is translated back into
/// the 1.0 format before it leaves the process.
/// </summary>
public static class SpecWireMiddleware
{
    public static IApplicationBuilder UseA2ASpecWire(this WebApplication app) =>
        app.UseWhen(context => Translated(context.Request), branch => branch.Use(TranslateAsync));

    /// <summary>The protocol endpoints, and only those: the token endpoint and the public card are ours.</summary>
    private static bool Translated(HttpRequest request) =>
        request.Path.StartsWithSegments(AgentCardFactory.A2APath)
        && !request.Path.StartsWithSegments("/a2a/token");

    private static async Task TranslateAsync(HttpContext context, RequestDelegate next)
    {
        var (method, spoken) = await RewriteRequestAsync(context);

        // A caller that used the SDK's own spelling expects the SDK's own answer — clients built on the preview
        // SDK keep working while it lags 1.0 — but every answer, in either dialect, still has to be a well-formed
        // JSON-RPC response. So that one repair applies to both, and only the translation depends on the dialect.
        Func<JsonNode?, JsonNode?> translate = spoken ? SpecWire.ResponseToSpec : SpecWire.EnsureEnvelope;

        var original = context.Response.Body;
        await using var translating = new SpecWireStream(original, SpecWire.IsStreaming(method), translate);
        context.Response.Body = translating;
        // The translation changes the length of everything it touches.
        context.Response.OnStarting(state =>
        {
            ((HttpResponse)state).ContentLength = null;
            return Task.CompletedTask;
        }, context.Response);

        try
        {
            await next(context);
            await translating.FinishAsync(context.RequestAborted);
        }
        finally
        {
            context.Response.Body = original;
        }
    }

    /// <summary>
    /// Rewrites the request into the SDK's dialect and says which method it was — in whichever spelling the
    /// caller used — and whether the caller spoke the specification, which is also how it expects to be answered.
    /// </summary>
    private static async Task<(string? SpecMethod, bool SpokeSpec)> RewriteRequestAsync(HttpContext context)
    {
        var request = context.Request;
        var fromPath = RestMethod(request);
        if (!HttpMethods.IsPost(request.Method) || request.ContentLength is 0)
        {
            // Nothing to read: the HTTP+JSON transport is the specification's surface, so it is answered as one.
            return (fromPath, request.Path.StartsWithSegments("/a2a/tasks") || fromPath is not null);
        }

        request.EnableBuffering();
        JsonNode? body;
        try
        {
            body = await JsonNode.ParseAsync(request.Body, cancellationToken: context.RequestAborted);
        }
        catch (JsonException)
        {
            // Malformed: the SDK answers with the parse error the specification asks for.
            request.Body.Position = 0;
            return (fromPath, false);
        }
        request.Body.Position = 0;

        var specMethod = fromPath is null ? SpecWire.RequestToSdk(body) : fromPath;
        if (specMethod is null)
        {
            // A JSON-RPC request in the SDK's own spelling: left alone. Its method still comes back, because a
            // streamed answer must be let through event by event whichever dialect asked for it.
            return ((body as JsonObject)?["method"]?.GetValue<string>(), false);
        }
        if (fromPath is not null)
        {
            // Over HTTP+JSON the method is in the path, so the body itself says which dialect the caller speaks.
            if (!SpecWire.LooksLikeSpec(body))
            {
                return (fromPath, false);
            }
            body = SpecWire.BodyToSdk(fromPath, body);
        }

        var translated = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(body));
        request.Body = translated;
        request.ContentLength = translated.Length;
        return (specMethod, true);
    }

    /// <summary>The HTTP+JSON transport says in its path what the JSON-RPC transport says in its method field.</summary>
    private static string? RestMethod(HttpRequest request)
    {
        var path = request.Path.Value ?? "";
        if (path.EndsWith("/message:send", StringComparison.Ordinal))
        {
            return "message/send";
        }
        if (path.EndsWith("/message:stream", StringComparison.Ordinal))
        {
            return "message/stream";
        }
        if (path.EndsWith(":subscribe", StringComparison.Ordinal))
        {
            return "tasks/resubscribe";
        }
        if (path.EndsWith(":cancel", StringComparison.Ordinal))
        {
            return "tasks/cancel";
        }
        if (path.Contains("/pushNotificationConfigs", StringComparison.Ordinal))
        {
            return HttpMethods.IsPost(request.Method) ? "tasks/pushNotificationConfig/set" : null;
        }
        return null;
    }
}

/// <summary>
/// The response body, translated as it is written. A single document is held until the end because it is one
/// object; a stream of events is translated event by event, so a caller watching a task keeps seeing it live.
/// </summary>
internal sealed class SpecWireStream(Stream inner, bool expectStream, Func<JsonNode?, JsonNode?> translate) : Stream
{
    private readonly MemoryStream buffer = new();
    private readonly bool streaming = expectStream;
    private readonly Func<JsonNode?, JsonNode?> translate = translate;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => buffer.Length;
    public override long Position { get => buffer.Position; set => throw new NotSupportedException(); }

    public override void Write(byte[] source, int offset, int count) => buffer.Write(source, offset, count);

    public override void Write(ReadOnlySpan<byte> source) => buffer.Write(source);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        await buffer.WriteAsync(source, cancellationToken);
        if (streaming)
        {
            await DrainEventsAsync(cancellationToken);
        }
    }

    public override Task WriteAsync(byte[] source, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(source.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        streaming ? DrainEventsAsync(cancellationToken).ContinueWith(_ => inner.FlushAsync(cancellationToken),
            cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap()
        : Task.CompletedTask;

    /// <summary>Writes whatever is left: the whole document, or the tail of a stream.</summary>
    public async Task FinishAsync(CancellationToken cancellationToken)
    {
        if (streaming)
        {
            await DrainEventsAsync(cancellationToken);
            await WriteRemainderAsync(cancellationToken);
            await inner.FlushAsync(cancellationToken);
            return;
        }

        var body = buffer.ToArray();
        buffer.SetLength(0);
        if (body.Length == 0)
        {
            return;
        }

        var translated = Translate(body, translate) ?? body;
        await inner.WriteAsync(translated, cancellationToken);
        await inner.FlushAsync(cancellationToken);
    }

    /// <summary>Server-sent events arrive as <c>data: {…}</c> blocks ended by a blank line.</summary>
    private async Task DrainEventsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pending = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
            var end = pending.Span.IndexOf("\n\n"u8);
            if (end < 0)
            {
                return;
            }

            var frame = pending[..(end + 2)];
            await inner.WriteAsync(TranslateFrame(frame.Span), cancellationToken);
            await inner.FlushAsync(cancellationToken);
            Consume(end + 2);
        }
    }

    private async Task WriteRemainderAsync(CancellationToken cancellationToken)
    {
        if (buffer.Length == 0)
        {
            return;
        }
        var rest = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
        await inner.WriteAsync(TranslateFrame(rest.Span), cancellationToken);
        buffer.SetLength(0);
    }

    private byte[] TranslateFrame(ReadOnlySpan<byte> frame)
    {
        var text = Encoding.UTF8.GetString(frame);
        var translated = new StringBuilder(text.Length);
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal)
                && Translate(Encoding.UTF8.GetBytes(line[6..]), translate) is { } payload)
            {
                translated.Append("data: ").Append(Encoding.UTF8.GetString(payload)).Append('\n');
            }
            else
            {
                translated.Append(line).Append('\n');
            }
        }
        // Split on '\n' produced one empty piece for the final newline; putting it back would add a line.
        translated.Length -= 1;
        return Encoding.UTF8.GetBytes(translated.ToString());
    }

    private static byte[]? Translate(ReadOnlySpan<byte> json, Func<JsonNode?, JsonNode?> translate)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return JsonSerializer.SerializeToUtf8Bytes(translate(node));
        }
        catch (JsonException)
        {
            return null; // Not JSON after all: whatever it is, it leaves untouched.
        }
    }

    private void Consume(int count)
    {
        var rest = buffer.GetBuffer().AsSpan(count, (int)buffer.Length - count).ToArray();
        buffer.SetLength(0);
        buffer.Write(rest);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            buffer.Dispose();
        }
        base.Dispose(disposing);
    }

    public override int Read(byte[] destination, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
