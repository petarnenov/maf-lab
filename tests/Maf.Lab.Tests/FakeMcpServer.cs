using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.Tests;

/// <summary>A server the tool never talks to: it only needs a request context to read its parameters.</summary>
#pragma warning disable MCPEXP002
internal sealed class FakeMcpServer : McpServer
{
    public override string? SessionId => null;

    public override string NegotiatedProtocolVersion => "2026-07-28";

    public override ClientCapabilities? ClientCapabilities => null;

    public override Implementation? ClientInfo => null;

    public override McpServerOptions ServerOptions { get; } = new();

    public override IServiceProvider? Services => null;

#pragma warning disable MCP9005 // The fake must match the base signature, deprecated or not.
    [Obsolete("Logging is deprecated in 2026-07-28; overridden only to satisfy the base class.")]
    public override LoggingLevel? LoggingLevel => null;
#pragma warning restore MCP9005

    public override Task RunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public override Task SendMessageAsync(ModelContextProtocol.Protocol.JsonRpcMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public override IAsyncDisposable RegisterNotificationHandler(string method, Func<ModelContextProtocol.Protocol.JsonRpcNotification, CancellationToken, ValueTask> handler) =>
        throw new NotSupportedException();

    public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
#pragma warning restore MCPEXP002
