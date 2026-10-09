using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests;

/// <summary>Jev keeps the decision engine's contract, over its own transport, against the HTTP stand-in for Jev.</summary>
public class JevDecisionEngineContractTests : DecisionEngineContract
{
    protected override IDecisionEngine Engine(FakeJev rules) => JevSupport.Engine(rules);
}
