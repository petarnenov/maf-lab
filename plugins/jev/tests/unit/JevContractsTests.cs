using System.Text.Json;
using Maf.Lab.Plugins.Jev;

namespace Maf.Lab.Tests;

/// <summary>Jev's documented response shapes are read as documented (https://docs.typesafe.ai/api).</summary>
public class JevContractsTests
{
    [Fact]
    public void The_documented_noul_answer_is_read()
    {
        // The Noul example response from https://docs.typesafe.ai/api, verbatim.
        const string json = """
            {"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}
            """;

        var response = JsonSerializer.Deserialize<JevResponse>(json, JevRequest.Json)!;

        Assert.Equal(0.95, response.Answers!["is_urgent"].Noul);
    }

    [Fact]
    public void The_documented_response_shape_is_read()
    {
        // The example response from https://docs.typesafe.ai/api, verbatim.
        const string json = """
            {"model":"jev-1.13.0","answers":{"department":{"type":"choice","choice":"billing",
             "probabilities":{"billing":0.88,"technical":0.12,"sales":0.0},"confidence":0.81}},
             "usage":{"input_tokens":318,"output_tokens":34}}
            """;

        var response = JsonSerializer.Deserialize<JevResponse>(json, JevRequest.Json)!;

        Assert.Equal("jev-1.13.0", response.Model);
        var answer = response.Answers!["department"];
        Assert.Equal("billing", answer.Choice);
        Assert.Equal(0.81, answer.Confidence);
        Assert.Equal(0.88, answer.Probabilities!["billing"]);
    }
}
