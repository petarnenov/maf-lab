using System.Text.Json.Nodes;

namespace Maf.Lab.A2A;

/// <summary>
/// Translates between the A2A 1.0 wire format and the dialect this preview SDK speaks. A partner writes its client
/// against the specification, so the specification is what this service accepts and emits; the SDK's spelling never
/// reaches the wire.
///
/// The divergences, all verified against A2A 1.0.0-preview2 and recorded in DECISIONS.md:
/// <list type="bullet">
/// <item>methods are dispatched as <c>SendMessage</c>, not <c>message/send</c>;</item>
/// <item>roles are <c>ROLE_USER</c> / <c>ROLE_AGENT</c>, not <c>user</c> / <c>agent</c>;</item>
/// <item>task states are <c>TASK_STATE_INPUT_REQUIRED</c>, not <c>input-required</c>;</item>
/// <item>a part carries no <c>kind</c> and a file part is flattened to <c>raw</c>/<c>url</c>/<c>mediaType</c>;</item>
/// <item>a message, task and artifact update carry no <c>kind</c>, and a status update carries no <c>final</c>;</item>
/// <item>a result is wrapped (<c>{"task": …}</c>, <c>{"statusUpdate": …}</c>) rather than being the object itself;</item>
/// <item>push-notification configuration is <c>{taskId, configId, config}</c>, not
/// <c>{taskId, pushNotificationConfig}</c>, and <c>configId</c> is mandatory.</item>
/// </list>
/// This class is the one place that knows any of it, and it disappears when the SDK speaks 1.0 itself.
/// </summary>
public static class SpecWire
{
    /// <summary>Specification method name → the identifier this SDK version dispatches on.</summary>
    public static readonly IReadOnlyDictionary<string, string> Methods = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["message/send"] = "SendMessage",
        ["message/stream"] = "SendStreamingMessage",
        ["tasks/get"] = "GetTask",
        ["tasks/list"] = "ListTasks",
        ["tasks/cancel"] = "CancelTask",
        ["tasks/resubscribe"] = "SubscribeToTask",
        ["tasks/pushNotificationConfig/set"] = "CreateTaskPushNotificationConfig",
        ["tasks/pushNotificationConfig/get"] = "GetTaskPushNotificationConfig",
        ["tasks/pushNotificationConfig/list"] = "ListTaskPushNotificationConfig",
        ["tasks/pushNotificationConfig/delete"] = "DeleteTaskPushNotificationConfig",
        ["agent/getAuthenticatedExtendedCard"] = "GetExtendedAgentCard",
    };

    /// <summary>
    /// Whether a body over the HTTP+JSON transport is written the way the specification writes it. That transport
    /// carries its method in the path, so the body is the only thing that says which dialect the caller speaks.
    /// </summary>
    public static bool LooksLikeSpec(JsonNode? body)
    {
        if (body is not JsonObject request)
        {
            return true;
        }
        if (request["message"] is JsonObject message)
        {
            return message["kind"] is not null
                || message["role"]?.GetValue<string>() is "user" or "agent"
                || (message["parts"] as JsonArray)?.FirstOrDefault()?["kind"] is not null;
        }
        return request["pushNotificationConfig"] is not null || request["configId"] is null;
    }

    /// <summary>The methods whose answer is a stream of events rather than a single JSON document.</summary>
    public static bool IsStreaming(string? specMethod) => specMethod is "message/stream" or "tasks/resubscribe";

    private static readonly Dictionary<string, string> RolesToSdk = new(StringComparer.Ordinal)
    {
        ["user"] = "ROLE_USER",
        ["agent"] = "ROLE_AGENT",
    };

    private static readonly Dictionary<string, string> RolesToSpec =
        RolesToSdk.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> StatesToSpec = new(StringComparer.Ordinal)
    {
        ["TASK_STATE_UNSPECIFIED"] = "unknown",
        ["TASK_STATE_SUBMITTED"] = "submitted",
        ["TASK_STATE_WORKING"] = "working",
        ["TASK_STATE_INPUT_REQUIRED"] = "input-required",
        ["TASK_STATE_COMPLETED"] = "completed",
        ["TASK_STATE_CANCELED"] = "canceled",
        ["TASK_STATE_FAILED"] = "failed",
        ["TASK_STATE_REJECTED"] = "rejected",
        ["TASK_STATE_AUTH_REQUIRED"] = "auth-required",
    };

    /// <summary>States after which no further update follows, so its event is the stream's last.</summary>
    private static readonly HashSet<string> Terminal =
        ["completed", "canceled", "failed", "rejected", "input-required", "auth-required"];

    // ---------- request: specification → SDK ----------

    /// <summary>
    /// Rewrites a JSON-RPC request in place. Returns the specification method name — so the caller knows whether the
    /// answer streams — or null when the request is not one we translate.
    /// </summary>
    public static string? RequestToSdk(JsonNode? envelope)
    {
        if (envelope is not JsonObject request
            || request["method"]?.GetValue<string>() is not { } method
            || !Methods.TryGetValue(method, out var dispatched))
        {
            // Already the SDK's spelling, or nothing we recognise: the SDK answers for itself.
            return null;
        }

        request["method"] = dispatched;
        if (Take(request, "params") is JsonObject parameters)
        {
            request["params"] = ParamsToSdk(method, parameters);
        }
        return method;
    }

    /// <summary>Rewrites an HTTP+JSON request body, which carries the parameters without an envelope.</summary>
    public static JsonNode? BodyToSdk(string specMethod, JsonNode? body) =>
        body is JsonObject parameters ? ParamsToSdk(specMethod, parameters) : body;

    private static JsonNode ParamsToSdk(string specMethod, JsonObject parameters)
    {
        switch (specMethod)
        {
            case "message/send" or "message/stream":
                if (Take(parameters, "message") is JsonObject message)
                {
                    parameters["message"] = MessageToSdk(message);
                }
                return parameters;

            case "tasks/pushNotificationConfig/set":
            {
                // The specification names the whole object; the SDK wants it split, with an id it will not invent.
                var config = Take(parameters, "pushNotificationConfig") ?? Take(parameters, "config");
                return new JsonObject
                {
                    ["taskId"] = Text(parameters, "taskId") ?? Text(parameters, "id"),
                    ["configId"] = (config as JsonObject)?["id"]?.GetValue<string>() ?? Text(parameters, "configId") ?? "",
                    ["config"] = config,
                };
            }

            case "tasks/pushNotificationConfig/get" or "tasks/pushNotificationConfig/delete":
                return new JsonObject
                {
                    ["taskId"] = Text(parameters, "taskId") ?? Text(parameters, "id"),
                    ["id"] = Text(parameters, "pushNotificationConfigId") ?? Text(parameters, "configId") ?? Text(parameters, "id"),
                };

            case "tasks/pushNotificationConfig/list":
                return new JsonObject { ["taskId"] = Text(parameters, "taskId") ?? Text(parameters, "id") };

            default:
                return parameters;
        }
    }

    private static JsonObject MessageToSdk(JsonObject message)
    {
        message.Remove("kind");
        if (message["role"]?.GetValue<string>() is { } role && RolesToSdk.TryGetValue(role, out var sdkRole))
        {
            message["role"] = sdkRole;
        }
        Rebuild(message, "parts", PartToSdk);
        return message;
    }

    private static JsonNode PartToSdk(JsonObject part)
    {
        var kind = part["kind"]?.GetValue<string>();
        part.Remove("kind");
        if (kind != "file" || Take(part, "file") is not JsonObject file)
        {
            return part;
        }

        // A file part is one object in the specification and three flat fields in the SDK.
        Move(file, "bytes", part, "raw");
        Move(file, "uri", part, "url");
        Move(file, "mimeType", part, "mediaType");
        Move(file, "name", part, "filename");
        return part;
    }

    // ---------- response: SDK → specification ----------

    /// <summary>
    /// Rewrites a response: a JSON-RPC envelope, one streamed event (the same envelope), or — over the HTTP+JSON
    /// transport — the result on its own.
    /// </summary>
    public static JsonNode? ResponseToSpec(JsonNode? body)
    {
        if (body is not JsonObject response)
        {
            return body;
        }
        if (Take(response, "result") is { } result)
        {
            response["result"] = ResultToSpec(result);
            return response;
        }
        // An error, or a request id echoed with nothing else: there is no payload to translate.
        return response.ContainsKey("error") || response.ContainsKey("jsonrpc") ? response : ResultToSpec(response);
    }

    private static JsonNode? ResultToSpec(JsonNode? result)
    {
        if (result is not JsonObject wrapper)
        {
            return result;
        }

        // The SDK wraps a result in the name of its payload; the specification returns the object itself.
        if (Take(wrapper, "task") is JsonObject task)
        {
            return TaskToSpec(task);
        }
        if (Take(wrapper, "message") is JsonObject message)
        {
            return MessageToSpec(message);
        }
        if (Take(wrapper, "statusUpdate") is JsonObject status)
        {
            return StatusUpdateToSpec(status);
        }
        if (Take(wrapper, "artifactUpdate") is JsonObject artifact)
        {
            artifact["kind"] = "artifact-update";
            if (Take(artifact, "artifact") is JsonObject inner)
            {
                artifact["artifact"] = ArtifactToSpec(inner);
            }
            return artifact;
        }
        if (wrapper["tasks"] is JsonArray)
        {
            Rebuild(wrapper, "tasks", TaskToSpec);
            return wrapper;
        }
        if (wrapper["configs"] is JsonArray)
        {
            Rebuild(wrapper, "configs", PushConfigToSpec);
            return wrapper;
        }
        if (wrapper["taskId"] is not null && (wrapper["config"] is JsonObject || wrapper["pushNotificationConfig"] is JsonObject))
        {
            return PushConfigToSpec(wrapper);
        }

        // Some results are the object itself rather than a wrapper around it — a fetched task, a plain message.
        if (wrapper["id"] is not null && wrapper["status"] is JsonObject)
        {
            return TaskToSpec(wrapper);
        }
        if (wrapper["messageId"] is not null && wrapper["parts"] is JsonArray)
        {
            return MessageToSpec(wrapper);
        }

        // An agent card, or anything else the SDK already shapes the way the specification does.
        return wrapper;
    }

    private static JsonObject TaskToSpec(JsonObject task)
    {
        task["kind"] = "task";
        if (Take(task, "status") is JsonObject status)
        {
            task["status"] = StatusToSpec(status);
        }
        Rebuild(task, "history", MessageToSpec);
        Rebuild(task, "artifacts", ArtifactToSpec);
        return task;
    }

    private static JsonObject StatusUpdateToSpec(JsonObject update)
    {
        update["kind"] = "status-update";
        if (Take(update, "status") is JsonObject status)
        {
            var translated = StatusToSpec(status);
            update["status"] = translated;
            // The specification marks the last event of a stream; the SDK leaves the caller to work it out.
            update["final"] = Terminal.Contains(translated["state"]?.GetValue<string>() ?? "");
        }
        else
        {
            update["final"] = false;
        }
        return update;
    }

    private static JsonObject StatusToSpec(JsonObject status)
    {
        if (status["state"]?.GetValue<string>() is { } state)
        {
            status["state"] = StatesToSpec.TryGetValue(state, out var specState) ? specState : state;
        }
        if (Take(status, "message") is JsonObject message)
        {
            status["message"] = MessageToSpec(message);
        }
        return status;
    }

    private static JsonObject MessageToSpec(JsonObject message)
    {
        message["kind"] = "message";
        if (message["role"]?.GetValue<string>() is { } role && RolesToSpec.TryGetValue(role, out var specRole))
        {
            message["role"] = specRole;
        }
        Rebuild(message, "parts", PartToSpec);
        return message;
    }

    private static JsonObject ArtifactToSpec(JsonObject artifact)
    {
        Rebuild(artifact, "parts", PartToSpec);
        return artifact;
    }

    private static JsonObject PartToSpec(JsonObject part)
    {
        if (part["text"] is not null)
        {
            part["kind"] = "text";
            return part;
        }
        if (part["data"] is not null)
        {
            part["kind"] = "data";
            return part;
        }
        if (part["raw"] is null && part["url"] is null)
        {
            return part;
        }

        var file = new JsonObject();
        Move(part, "raw", file, "bytes");
        Move(part, "url", file, "uri");
        Move(part, "mediaType", file, "mimeType");
        Move(part, "filename", file, "name");
        part["kind"] = "file";
        part["file"] = file;
        return part;
    }

    private static JsonObject PushConfigToSpec(JsonObject config)
    {
        if (Take(config, "config") is { } inner)
        {
            config["pushNotificationConfig"] = inner;
        }
        // The specification identifies a configuration inside the object, not beside it.
        config.Remove("id");
        return config;
    }

    // ---------- small helpers: a node belongs to one parent, so it is taken out before it is put anywhere else ----------

    private static JsonNode? Take(JsonObject parent, string key)
    {
        if (!parent.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }
        parent.Remove(key);
        return node;
    }

    private static string? Text(JsonObject parent, string key) =>
        parent[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static void Move(JsonObject from, string fromKey, JsonObject to, string toKey)
    {
        if (Take(from, fromKey) is { } node)
        {
            to[toKey] = node;
        }
    }

    /// <summary>Translates every object in an array, leaving anything else as it is.</summary>
    private static void Rebuild(JsonObject parent, string key, Func<JsonObject, JsonNode> translate)
    {
        if (Take(parent, key) is not JsonArray items)
        {
            return;
        }
        var translated = new JsonArray();
        foreach (var item in items.ToList())
        {
            items.Remove(item);
            translated.Add(item is JsonObject obj ? translate(obj) : item);
        }
        parent[key] = translated;
    }
}
