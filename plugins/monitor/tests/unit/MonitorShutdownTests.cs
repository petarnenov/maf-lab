using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Monitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Maf.Lab.Tests;

/// <summary>The monitor's writes may outlive their request, never the host that disposes the stores they write through.</summary>
public class MonitorShutdownTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RunFrame Frame = new(1, 0, "RUN_STARTED", null, 10, null, null, false);

    [Fact]
    public async Task Disposing_waits_for_the_frames_write_in_hand_and_starts_none_after()
    {
        var opened = new TaskCompletionSource<DbContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var db = Substitute.For<IDbContextFactory<DbContext>>();
        db.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(opened.Task);
        var observer = new MonitorObserver(Substitute.For<IRunTraceStore>(), db, TimeProvider.System, NullLogger<MonitorObserver>.Instance);

        var write = observer.OnFramesAsync("run-1", "turn-1", [Frame], CancellationToken.None);
        var disposing = observer.DisposeAsync().AsTask();
        // The write has not got its context yet: disposing is still waiting on it, so the store is not disposed under it.
        Assert.False(disposing.IsCompleted);

        opened.SetException(new InvalidOperationException("the store is down"));
        await Task.WhenAll(write, disposing).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        await observer.OnFramesAsync("run-2", "turn-2", [Frame], CancellationToken.None);
        await db.Received(1).CreateDbContextAsync(Arg.Any<CancellationToken>());
    }
}
