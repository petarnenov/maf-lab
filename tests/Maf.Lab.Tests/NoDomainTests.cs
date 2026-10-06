using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

/// <summary>
/// No domain in use (introduce-plugins decision 5h, task 4.11): the assistant answers nothing without a domain, so a
/// turn ends with the fixed reply before any model, decision-engine or tool call.
/// </summary>
public sealed class NoDomainTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ApiFactory CoreOnly() => new(ApiFactory.ProceduralModel())
    {
        // No built-in domain kept and no plugin installed: `make core`.
        ExtraSettings = new Dictionary<string, string?> { ["Agent:BuiltInDomains"] = "" },
    };

    [Theory]
    [InlineData("What is the procedure when a fee schedule is missing?", NoDomain.ReplyEnglish)]
    [InlineData("Каква е процедурата при липсваща тарифа?", NoDomain.ReplyBulgarian)]
    public async Task A_turn_declines_before_any_model_or_Jev_call(string question, string reply)
    {
        using var api = CoreOnly();

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), question);

        Assert.Equal(reply, ApiFactory.AnswerOf(events));
        Assert.Empty(api.Chat.Requests);
        Assert.Empty(api.Jev.Requests);
        Assert.Empty(api.Tools.Invocations);
        Assert.Empty(api.Tools.RequestedDomains);
        var trace = ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();
        Assert.True(trace.Single(t => t.Kind == TraceKinds.Intent).Data.GetProperty("noDomain").GetBoolean());
        Assert.DoesNotContain(trace, t => t.Kind is TraceKinds.ToolCall or TraceKinds.ModelRequest);
    }

    [Fact]
    public async Task The_plugins_endpoint_lists_no_domain_so_the_chat_page_can_say_so_up_front()
    {
        using var api = CoreOnly();
        using var full = new ApiFactory(ApiFactory.ProceduralModel());

        var none = await api.ClientFor("adam", "firm-a", Role.USER).GetFromJsonAsync<JsonElement>("/api/plugins", TestContext.Current.CancellationToken);
        var all = await full.ClientFor("adam", "firm-a", Role.USER).GetFromJsonAsync<JsonElement>("/api/plugins", TestContext.Current.CancellationToken);

        Assert.Empty(none.GetProperty("domains").EnumerateArray());
        Assert.Equal(["billing", "portfolio"], all.GetProperty("domains").EnumerateArray().Select(d => d.GetProperty("id").GetString()));
    }
}
