using System.Net.Http;
using System.Net.Sockets;
using Grpc.Core;
using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Configuration;

namespace Maf.Lab.Tests;

public class UnreachableServiceTests
{
    private static readonly QdrantOptions Qdrant = new() { Host = "localhost", GrpcPort = 6334 };
    private static readonly ModelOptions Models = new() { OllamaEndpoint = "http://localhost:11435" };

    [Fact]
    public void Unavailable_qdrant_names_qdrant_and_its_address()
    {
        var ex = new RpcException(new Status(StatusCode.Unavailable, "Error connecting to subchannel."));

        var line = UnreachableService.Describe(ex, Qdrant, Models);

        Assert.Equal("✗ Qdrant is not reachable at localhost:6334 — run 'make infra' (or 'make') and try again", line);
    }

    [Fact]
    public void Refused_embedding_request_names_the_embedding_endpoint()
    {
        var ex = new HttpRequestException("Connection refused (localhost:11435)", new SocketException((int)SocketError.ConnectionRefused));

        var line = UnreachableService.Describe(ex, Qdrant, Models);

        Assert.Equal("✗ The embedding endpoint is not reachable at localhost:11435 — run 'make infra' (or 'make') and try again", line);
    }

    [Fact]
    public void Wrapped_failure_is_found_and_its_message_is_not_repeated()
    {
        var inner = new RpcException(new Status(StatusCode.Unavailable, "secret-detail"));

        var line = UnreachableService.Describe(new InvalidOperationException("outer-detail", inner), Qdrant, Models);

        Assert.NotNull(line);
        Assert.DoesNotContain("secret-detail", line);
        Assert.DoesNotContain("outer-detail", line);
    }

    [Fact]
    public void Other_failures_are_not_classified()
    {
        Assert.Null(UnreachableService.Describe(new InvalidOperationException("boom"), Qdrant, Models));
        Assert.Null(UnreachableService.Describe(new RpcException(new Status(StatusCode.NotFound, "x")), Qdrant, Models));
        Assert.Null(UnreachableService.Describe(new HttpRequestException("500"), Qdrant, Models));
    }
}
