using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent;

/// <summary>The core's side of <see cref="IIntentSettings"/>: the classifier's options as bound, and the installed engine.</summary>
public sealed class CoreIntentSettings(IOptions<IntentOptions> options, IDecisionEngine engine) : IIntentSettings
{
    public IntentSettings Current =>
        new(engine.Engine, options.Value.MinConfidence, options.Value.MinInDomain, options.Value.TimeoutSeconds);
}
