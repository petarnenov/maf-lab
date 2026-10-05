namespace Maf.Lab.Tests;

/// <summary>What the indexer says when it is stopped (stop-anything): what it left, and the command that finishes it.</summary>
public class IndexerCancelTests
{
    [Theory]
    [InlineData("index", "make index")]
    [InlineData("rebuild", "make rebuild-index FORCE=1")]
    [InlineData("graph", "make graph")]
    [InlineData("neo4j-chunks", "make neo4j-chunks")]
    [InlineData("migrate", "continue where it stopped")]
    public void A_stopped_command_says_what_to_run_again(string command, string next)
    {
        Assert.Contains(next, Maf.Lab.Indexing.Program.AfterCancel(command), StringComparison.Ordinal);
    }
}
