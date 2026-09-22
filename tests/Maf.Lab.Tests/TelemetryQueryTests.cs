using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Maf.Lab.Api.Telemetry;
using Maf.Lab.Domain.Telemetry;
using Maf.Lab.Domain.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>The stack's own numbers: read for the caller, from queries the caller does not get to write.</summary>
public class TelemetryQueryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static TelemetryQueries Queries(Func<HttpRequestMessage, HttpResponseMessage> respond, string? url = "http://prometheus:9090") =>
        new(new StubFactory(respond),
            Options.Create(new TelemetryQueryOptions { PrometheusUrl = url ?? "", JaegerUrl = "http://localhost:7171/jaeger" }),
            TimeProvider.System, NullLogger<TelemetryQueries>.Instance);

    private static HttpResponseMessage Vector(params (string Label, string Value, double Number)[] rows)
    {
        var results = rows.Select(r =>
            "{\"metric\":{\"" + r.Label + "\":\"" + r.Value + "\"},\"value\":[1758500000,\"" + r.Number + "\"]}");
        var body = "{\"status\":\"success\",\"data\":{\"resultType\":\"vector\",\"result\":["
            + string.Join(",", results) + "]}}";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task The_numbers_come_back_under_the_panel_and_the_labels_that_name_them()
    {
        var asked = new List<string>();
        var report = await Queries(request =>
        {
            asked.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return Vector(("outcome", "answered", 12));
        }).ReadAsync("1h", Ct);

        Assert.True(report.Available);
        Assert.Equal("1h", report.Window);
        var turns = report.Panels.Single(p => p.Id == "turns");
        Assert.Equal(new TelemetrySeries("answered", 12), turns.Series.Single());

        // The window the caller chose is the only thing of theirs in the query.
        Assert.All(asked, q => Assert.Contains("[1h]", q));
        Assert.Contains(asked, q => q.Contains("maf_turns_total"));
        Assert.Equal("http://localhost:7171/jaeger", report.TraceUrl);
    }

    [Fact]
    public async Task A_window_the_server_does_not_know_is_refused_rather_than_passed_on()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var refused = await adam.GetAsync("/api/telemetry?window=1h)%20or%20drop", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // And a known one is served, even with no metrics store behind it.
        var ok = await adam.GetAsync("/api/telemetry?window=15m", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var report = await ok.Content.ReadFromJsonAsync<TelemetryReport>(Json, Ct);
        Assert.False(report!.Available);
        Assert.Equal("15m", report.Window);
    }

    [Fact]
    public async Task A_row_that_measured_nothing_is_left_out_rather_than_shown_as_zero()
    {
        var report = await Queries(_ => Vector(("service_instance_id", "a-replica-that-is-gone", 0))).ReadAsync("1h", Ct);

        Assert.True(report.Available);
        // Every panel had a row with nothing in it, so every panel reads as no data.
        Assert.All(report.Panels, p => Assert.Empty(p.Series));
    }

    [Fact]
    public async Task A_store_that_cannot_be_reached_says_so_and_reports_no_numbers()
    {
        var report = await Queries(_ => throw new HttpRequestException("no route to host")).ReadAsync("1h", Ct);

        Assert.False(report.Available);
        Assert.Contains("could not be reached", report.Reason);
        // Not zeros: nothing was measured that this can speak for.
        Assert.Empty(report.Panels);
    }

    [Fact]
    public async Task Signing_in_is_required()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/telemetry", Ct)).StatusCode);
    }

    private sealed class StubFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(respond));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
