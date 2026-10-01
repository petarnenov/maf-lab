using System.Net.Http;
using System.Net.Sockets;
using Grpc.Core;
using Maf.Lab.Retrieval.Configuration;

namespace Maf.Lab.Indexing;

/// <summary>
/// Turns a failure to connect to the indexer's infrastructure into one actionable line: which service, which address,
/// and what to run. Built from configuration and the exception's type only, never its message.
/// </summary>
internal static class UnreachableService
{
    public static string? Describe(Exception ex, QdrantOptions qdrant, ModelOptions models)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is RpcException { StatusCode: StatusCode.Unavailable })
            {
                return Line("Qdrant", $"{qdrant.Host}:{qdrant.GrpcPort}");
            }
            if (e is HttpRequestException { InnerException: SocketException })
            {
                return Line("The embedding endpoint", Authority(models));
            }
        }
        return null;
    }

    private static string Line(string service, string address) =>
        $"✗ {service} is not reachable at {address} — run 'make infra' (or 'make') and try again";

    private static string Authority(ModelOptions models)
    {
        var endpoint = string.Equals(models.Provider, "ollama", StringComparison.OrdinalIgnoreCase) ? models.OllamaEndpoint : models.OpenAIEndpoint;
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Authority : "its configured address";
    }
}
