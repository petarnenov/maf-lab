using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Coverage;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestGen.Coverage;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Tests;

/// <summary>Per-file thresholds, the allowlist and what a run is expected to cost (coverage-threshold, model-selection).</summary>
// Heavy (git, several hosts): run one after another rather than beside the timing-sensitive tests.
[Collection("TestGeneration")]
public sealed class CoverageThresholdTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string File = "src/Lab/Beta.cs";

    /// <summary>The api with one file measured at 62% and the default threshold of 80%.</summary>
    private static async Task<ApiFactory> ApiAsync(Action<IServiceCollection>? services = null)
    {
        var repo = await TempGitRepo.CreateAsync(new Dictionary<string, string> { [File] = CoverageApi.Lines(100) }, Ct);
        var api = CoverageApi.Create(repo);
        if (services is not null)
        {
            var previous = api.ConfigureTestServices;
            api.ConfigureTestServices = s =>
            {
                previous?.Invoke(s);
                services(s);
            };
        }
        await CoverageApi.IngestAsync(api, await repo.HeadAsync(Ct), Toolchains.Dotnet, SnapshotKind.Official, null, (File, 62, 100));
        return api;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, int? pct) =>
        client.PutAsJsonAsync($"/api/coverage/thresholds?path={Uri.EscapeDataString(File)}", new CoverageEndpoints.ThresholdRequest(pct), Ct);

    private static async Task<CoverageTreeFile> FileAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CoverageTreeDto>("/api/coverage/tree", Json, Ct))!.Files.Single(f => f.Path == File);

    private sealed record CoverageTreeFile(string Path, int Threshold, bool ThresholdIsOverride, bool BelowThreshold);

    private sealed record CoverageTreeDto(List<CoverageTreeFile> Files);

    [Fact]
    public async Task Lowering_a_threshold_saves_at_once()
    {
        using var api = await ApiAsync();
        var admin = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);

        var response = await PutAsync(admin, 50);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var file = await FileAsync(admin);
        Assert.Equal((50, true, false), (file.Threshold, file.ThresholdIsOverride, file.BelowThreshold));
    }

    [Fact]
    public async Task Raising_above_coverage_asks_for_a_run_and_saves_nothing()
    {
        using var api = await ApiAsync();
        var admin = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);

        var response = await PutAsync(admin, 85);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CoverageEndpoints.RunRequired>(Json, Ct);
        Assert.Equal(("run_required", 62.0, 85), (body!.Type, body.CurrentPct, body.TargetPct));
        Assert.Equal(80, (await FileAsync(admin)).Threshold);
    }

    [Fact]
    public async Task Raising_to_a_value_coverage_already_meets_saves_at_once()
    {
        using var api = await ApiAsync();
        var admin = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, 40)).StatusCode);

        var response = await PutAsync(admin, 60);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(60, (await FileAsync(admin)).Threshold);
    }

    [Fact]
    public async Task Keeping_the_same_value_saves()
    {
        using var api = await ApiAsync();

        var response = await PutAsync(api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN), 80);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Clearing_an_override_returns_the_file_to_the_default()
    {
        using var api = await ApiAsync();
        var admin = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        await PutAsync(admin, 50);

        var response = await PutAsync(admin, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var file = await FileAsync(admin);
        Assert.Equal((80, false), (file.Threshold, file.ThresholdIsOverride));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(-1)]
    public async Task An_out_of_range_threshold_is_rejected(int pct)
    {
        using var api = await ApiAsync();
        var admin = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);

        var response = await PutAsync(admin, pct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(80, (await FileAsync(admin)).Threshold);
    }

    [Fact]
    public async Task A_threshold_cannot_change_while_a_run_is_active()
    {
        using var api = await ApiAsync();
        await using (var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct))
        {
            db.TestGenRuns.Add(CoverageStorageTests.Run("r_1", File, TestGenRunState.Working));
            await db.SaveChangesAsync(Ct);
        }

        var response = await PutAsync(api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN), 50);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("run_active", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Only_an_admin_changes_a_threshold()
    {
        using var api = await ApiAsync();

        var response = await PutAsync(api.ClientFor("bob", "firm-a", Role.ADVISOR), 50);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Answers for every model except those it was told the account cannot use.</summary>
    private sealed class PlanLimitedModels(params string[] refused) : IChatClientFactory
    {
        public List<string> Asked { get; } = [];

        public IChatClient CreateChatClient(string? model = null)
        {
            lock (Asked)
            {
                Asked.Add(model ?? "");
            }
            return refused.Contains(model)
                ? new ThrowingChat(new HttpRequestException("this model is not included in your free usage", null, HttpStatusCode.Forbidden))
                : new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text("OK"));
        }

        public ChatOptions BaseChatOptions() => new();
    }

    private sealed class ThrowingChat(Exception error) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(error);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw error;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task The_picker_lists_the_allowlist_with_availability_and_an_estimate()
    {
        var models = new PlanLimitedModels("kimi-k3:cloud");
        using var api = await ApiAsync(s =>
        {
            s.RemoveAll<IChatClientFactory>();
            s.AddSingleton<IChatClientFactory>(models);
        });

        var picker = await api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN)
            .GetFromJsonAsync<CoverageEndpoints.ModelsDto>($"/api/coverage/models?path={Uri.EscapeDataString(File)}", Json, Ct);

        Assert.Equal(["glm-5.3:cloud", "kimi-k3:cloud", "glm-5.3-flash:cloud", "deepseek-v4-pro:cloud", "deepseek-v4.1-flash:cloud"],
            picker!.Models.Select(m => m.Tag));
        Assert.Equal("glm-5.3:cloud", picker.Models.Single(m => m.IsDefault).Tag);
        var kimi = picker.Models.Single(m => m.Tag == "kimi-k3:cloud");
        Assert.Equal((false, ModelAvailability.NotInPlan), (kimi.Available, kimi.UnavailableReason));
        Assert.All(picker.Models.Where(m => m.Tag != "kimi-k3:cloud"), m => Assert.True(m.Available));
        Assert.True(picker.Estimate!.FixedInputTokens > 0);
        Assert.Equal(new Maf.Lab.TestGen.LimitBounds(1, 10, 10), picker.Limits.MaxAttempts);
        Assert.Equal(new Maf.Lab.TestGen.LimitBounds(1, 40, 40), picker.Limits.ToolRoundsPerAttempt);
        Assert.Equal(new Maf.Lab.TestGen.LimitBounds(0, 2, 2), picker.Limits.TestRunsPerAttempt);
        Assert.Equal((120, 3), (picker.Limits.DeadlineMinutes, picker.Limits.MaxSuspectedBugs));

        // The answer is reused: a second look does not ask the provider again.
        var asked = models.Asked.Count;
        await api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN).GetAsync("/api/coverage/models", Ct);
        Assert.Equal(asked, models.Asked.Count);
    }

    [Fact]
    public void A_refusal_is_told_apart_from_a_hiccup()
    {
        Assert.True(ModelAvailability.IsRefusal(new HttpRequestException("x", null, HttpStatusCode.Forbidden)));
        Assert.True(ModelAvailability.IsRefusal(new InvalidOperationException("model \"kimi\" is not included in your free usage")));
        Assert.False(ModelAvailability.IsRefusal(new HttpRequestException("x", null, HttpStatusCode.BadGateway)));
        Assert.False(ModelAvailability.IsRefusal(new TaskCanceledException()));
    }

    [Fact]
    public void The_estimate_follows_the_model_and_the_attempts()
    {
        var glm = new AgentModelOption { Tag = "glm-5.3:cloud", InputPerMTok = 0.6, OutputPerMTok = 2.2 };
        var flash = new AgentModelOption { Tag = "glm-5.3-flash:cloud", InputPerMTok = 0.1, OutputPerMTok = 0.4 };

        var five = CostEstimator.Estimate(8_000, 5, 40, glm);
        var two = CostEstimator.Estimate(8_000, 2, 40, glm);

        Assert.True(CostEstimator.Estimate(8_000, 5, 40, flash).CostUsd < five.CostUsd);
        Assert.Equal(five.InputTokens, two.InputTokens / 2 * 5);
        // 43 000 + 4 × 2 000 file tokens + 4 000 × 20 typical rounds = 131 000 input and 6 000 output tokens per attempt.
        Assert.Equal((655_000L, 30_000L), (five.InputTokens, five.OutputTokens));
        Assert.Equal(Math.Round(0.655 * 0.6 + 0.03 * 2.2, 4), five.CostUsd);
        Assert.Equal(new EstimateParts(51_000, 4_000, 20, 6_000), CostEstimator.Parts(8_000));
    }
}
