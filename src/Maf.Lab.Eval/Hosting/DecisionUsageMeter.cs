using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Eval.Hosting;

/// <summary>
/// The input tokens the decision engine charged the eval for (extract-billing): the suites that ask it through the
/// production classes (the guard, the answer check, the intent classifier) never see a response's usage, so the eval
/// counts it at the port, in <see cref="MeteredDecisionEngine"/>.
/// </summary>
public sealed class DecisionUsageMeter
{
    private int _inputTokens;

    public int InputTokens => Volatile.Read(ref _inputTokens);

    internal void Add(int tokens) => Interlocked.Add(ref _inputTokens, tokens);

    /// <summary>Starts a count from zero, for one suite.</summary>
    public void Reset() => Interlocked.Exchange(ref _inputTokens, 0);
}

/// <summary>A Decorator over the installed engine: adds each outcome's input tokens to the meter and changes nothing else.</summary>
public sealed class MeteredDecisionEngine(IDecisionEngine inner, DecisionUsageMeter meter) : IDecisionEngine
{
    public string Engine => inner.Engine;

    public bool IsConfigured => inner.IsConfigured;

    public async Task<DecisionOutcome> DecideAsync(object state, IReadOnlyDictionary<string, DecisionQuestion> questions, TimeSpan budget,
        CancellationToken ct)
    {
        var outcome = await inner.DecideAsync(state, questions, budget, ct);
        meter.Add(outcome.Usage?.InputTokens ?? 0);
        return outcome;
    }

    /// <summary>Wraps the engine the providers registered, as it was registered (instance, factory or type).</summary>
    public static IServiceCollection DecorateEngine(IServiceCollection services)
    {
        var registered = services.LastOrDefault(d => d.ServiceType == typeof(IDecisionEngine) && !d.IsKeyedService);
        if (registered is null)
        {
            return services;
        }
        services.Remove(registered);
        services.AddSingleton<IDecisionEngine>(sp => new MeteredDecisionEngine(
            registered.ImplementationInstance as IDecisionEngine
            ?? (IDecisionEngine?)registered.ImplementationFactory?.Invoke(sp)
            ?? (IDecisionEngine)ActivatorUtilities.CreateInstance(sp, registered.ImplementationType!),
            sp.GetRequiredService<DecisionUsageMeter>()));
        return services;
    }
}
