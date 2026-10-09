using System.Net;
using System.Net.Sockets;
using Maf.Lab.Domain.Services;

namespace Maf.Lab.Hosting.Services;

public sealed class DnsServiceResolver : IServiceResolver
{
    public async Task<IReadOnlyList<string>> ResolveAsync(string service, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(service)) return [];
        try { return [.. (await Dns.GetHostAddressesAsync(service, ct)).Select(a => a.ToString()).Distinct().Order()]; }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            ct.ThrowIfCancellationRequested();
            return [];
        }
    }
}
