using Maf.Lab.Hosting.Cli;

namespace Maf.Lab.Indexing.Pipeline;

/// <summary>Drives the CLI's progress bar from the pipeline's reports: stage, then documents done of the total.</summary>
public sealed class IndexProgressBar(ConsoleProgress bar) : IProgress<IndexProgress>
{
    private string? _stage;
    private int? _total;
    private int _done;

    public void Report(IndexProgress value)
    {
        if (value.Stage != _stage)
        {
            _stage = value.Stage;
            bar.Step(value.Stage);
        }
        if (value.Total is { } total && total != _total)
        {
            _total = total;
            bar.SetTotal(total);
        }
        if (value.Done > _done)
        {
            bar.Advance(value.Done - _done);
            _done = value.Done;
        }
        if (value.Current is { } current)
        {
            bar.Working(current);
        }
    }
}
