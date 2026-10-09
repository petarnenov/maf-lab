extern alias service;
using Metrics = global::Maf.Lab.Eval.Suites.Metrics;
namespace Maf.Lab.Tests;
public sealed partial class CliCancelTests
{
    [Theory]
    [InlineData("TERM")]
    [InlineData("INT")]
    public async Task The_a2a_probe_stops_when_told(string signal)
    {
        using var hole = new BlackHole();

        var (exit, stderr) = await InterruptAsync(signal, hole, ToolPath("plugins/evals/service/probe", "Maf.Lab.ProtocolProbe.dll"),
            [$"http://127.0.0.1:{hole.Port}"], new());

        Assert.Equal(130, exit);
        Assert.Contains("Cancelled", LastLine(stderr), StringComparison.Ordinal);
        Assert.Contains("make eval-a2a", LastLine(stderr), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TERM")]
    [InlineData("INT")]
    public async Task The_eval_tool_stops_when_told_and_keeps_what_finished(string signal)
    {
        using var hole = new BlackHole();
        using var providers = new CliProviders();

        var (exit, stderr) = await InterruptAsync(signal, hole, ToolPath("plugins/evals/service/runner", "Maf.Lab.Evaluator.dll"),
            ["--ask", "what is the procedure when a fee schedule is missing"], new()
        {
            ["Plugins__Root"] = providers.Root,
            ["MAF_CHAT_MODEL"] = providers.Chat,
            ["Qdrant__Host"] = "127.0.0.1",
            ["Qdrant__GrpcPort"] = hole.Port.ToString(),
            ["SharedState__ConnectionString"] = $"127.0.0.1:{hole.Port},abortConnect=false",
        });

        Assert.Equal(130, exit);
        Assert.Equal(service::Maf.Lab.Eval.Program.AfterCancel, LastLine(stderr));
    }

}
