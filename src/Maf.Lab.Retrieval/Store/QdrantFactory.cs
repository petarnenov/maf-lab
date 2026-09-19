using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.Options;
using Qdrant.Client;

namespace Maf.Lab.Retrieval.Store;

public static class QdrantFactory
{
    public static QdrantClient Create(IOptions<QdrantOptions> options)
    {
        var o = options.Value;
        return new QdrantClient(o.Host, o.GrpcPort, o.Https, o.ApiKey);
    }
}
