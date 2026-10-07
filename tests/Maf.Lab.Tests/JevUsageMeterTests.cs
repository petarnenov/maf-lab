using System.Net;
using System.Text;
using Maf.Lab.Eval.Hosting;

namespace Maf.Lab.Tests;

/// <summary>The eval's Jev token meter counts each response's input tokens and leaves the response readable.</summary>
public class JevUsageMeterTests
{
    private sealed class Answering(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    [Fact]
    public async Task It_counts_the_input_tokens_and_the_caller_still_reads_the_answer()
    {
        const string json = """{"model":"jev-1.13.0","answers":{},"usage":{"input_tokens":1234,"output_tokens":5}}""";
        var meter = new JevUsageMeter();
        using var client = new HttpClient(new JevUsageHandler(meter) { InnerHandler = new Answering(json) });

        using var response = await client.PostAsync("https://jev.test/v1/systemone", new StringContent("{}"), TestContext.Current.CancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        var read = await new StreamReader(stream).ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal(json, read);
        Assert.Equal(1234, meter.InputTokens);
    }
}
