using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Server;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent.AGUI;

/// <summary>
/// The only place an AG-UI event is built (agui-protocol-only, enforced by AGUIProtocolOnlyTests): the mapping hooks
/// registered with the official server. Each turns one of <see cref="TurnContents"/>' contents into the protocol's own
/// event; everything else — the run's start and end, text, reasoning, tool calls, interrupts — the server maps itself.
/// </summary>
public static class AGUIMappings
{
    /// <summary>The mappings every agent of this system uses.</summary>
    public static AGUIStreamOptions Agents()
    {
        var options = new AGUIStreamOptions();
        options.MapContent(Map);
        return options;
    }

    private static IEnumerable<BaseEvent>? Map(AIContent content)
    {
        if (content is not DataContent data)
        {
            return null;
        }
        switch (data.MediaType)
        {
            case TurnContents.StateType:
                return [new StateSnapshotEvent { Snapshot = Parse(data) }];
            case TurnContents.ActivityType:
                var activity = Parse(data);
                return [new ActivitySnapshotEvent
                {
                    MessageId = activity.GetProperty("messageId").GetString()!,
                    ActivityType = activity.GetProperty("activityType").GetString()!,
                    Content = activity.GetProperty("content").Clone(),
                }];
            case TurnContents.StepStartedType:
                return [new StepStartedEvent { StepName = Encoding.UTF8.GetString(data.Data.Span) }];
            case TurnContents.StepFinishedType:
                return [new StepFinishedEvent { StepName = Encoding.UTF8.GetString(data.Data.Span) }];
            default:
                return null;
        }
    }

    private static JsonElement Parse(DataContent data)
    {
        using var document = JsonDocument.Parse(data.Data);
        return document.RootElement.Clone();
    }
}
