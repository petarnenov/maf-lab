using Grpc.Core;
using Grpc.Core.Interceptors;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Maf.Lab.Retrieval.Store;

public static class QdrantFactory
{
    public static QdrantClient Create(IOptions<QdrantOptions> options)
    {
        var o = options.Value;
        var address = new Uri($"{(o.Https ? "https" : "http")}://{o.Host}:{o.GrpcPort}");
        var channel = QdrantChannel.ForAddress(address, new ClientConfiguration { ApiKey = o.ApiKey });
        return new QdrantClient(new QdrantGrpcClient(channel.Intercept(new StoppedCalls())));
    }

    /// <summary>
    /// A call its caller stopped is cancelled on its gRPC channel, and gRPC reports that as <see cref="RpcException"/>
    /// with status Cancelled. Callers ask "was this stopped?" the .NET way (stop-anything), so here it becomes the
    /// <see cref="OperationCanceledException"/> it is — and only when the caller's own token says so.
    /// </summary>
    internal sealed class StoppedCalls : Interceptor
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(request, context);
            var token = context.Options.CancellationToken;
            return new AsyncUnaryCall<TResponse>(Stopped(call.ResponseAsync, token), call.ResponseHeadersAsync,
                call.GetStatus, call.GetTrailers, call.Dispose);
        }

        private static async Task<TResponse> Stopped<TResponse>(Task<TResponse> response, CancellationToken token)
        {
            try
            {
                return await response;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && token.IsCancellationRequested)
            {
                throw new OperationCanceledException("The vector store call was stopped.", ex, token);
            }
        }
    }
}
