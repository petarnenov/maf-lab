## Topology (any authenticated role)

| Method | Path | Body / query | Response |
|---|---|---|---|
| GET | `/api/topology` | — | `TopologyReport`: `{ generatedAt, cacheSeconds, discoveryAvailable, reportedBy, nodes }` |
| GET | `/api/topology/diagram` | — | The drawn diagram (embedded draw.io resource) as `application/xml` |

A node is `{ id, name, health, instances, facts, reason }`; `health` is `Healthy`, `Degraded`,
`Unreachable` or `NotProbed` (sent by name). `instances` are the replicas found by resolving the compose service
name, each asked its own `/health`; `facts` are display strings (chunk count, models, tool list) and never carry a
secret — the chat provider reports only *whether* its key is configured. The report is probed concurrently with a
2 s budget per service and reused for `cacheSeconds`.
