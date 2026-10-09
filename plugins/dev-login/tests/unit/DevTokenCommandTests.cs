using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

public sealed class DevTokenCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_terminal_command_requests_persona_organization_and_audience_and_prints_only_the_token()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.MapPost("/dev/token", async (HttpContext context) =>
        {
            received.TrySetResult((await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)).RootElement.Clone());
            return Results.Json(new { token = "fixture-token" });
        });
        await app.StartAsync(Ct);
        var start = new ProcessStartInfo("python3") { WorkingDirectory = CorpusLoaderTests.RepoRoot(), RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine("plugins", "dev-login", "files", "dev_token.py"), "--base-url", app.Urls.Single(), "--persona", "operator", "--tenant", "firm-b", "--audience", "api" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("fixture-token", output.Trim());
        var body = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("operator", body.GetProperty("persona").GetString());
        Assert.Equal("firm-b", body.GetProperty("tenantId").GetString());
        Assert.Equal("api", body.GetProperty("audience").GetString());
    }
}
