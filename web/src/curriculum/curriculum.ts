// Where the 5-day Fullstack AI Engineer study plan (revision 7) shows up in this lab.
// Every path, spec and screen here is checked by curriculum.test.ts; a summary changes with the behaviour it describes.

export interface CurriculumEntry {
  concept: string;
  summary: string;
  /** Repository-relative paths that implement the concept. */
  paths: string[];
  /** Capability directory under openspec/specs/. */
  spec: string;
  screen?: { to: string; label: string };
}

export interface CurriculumSection {
  id: string;
  title: string;
  note: string;
  entries: CurriculumEntry[];
}

const CHAT = { to: '/chat', label: 'Chat' };

export const CURRICULUM: CurriculumSection[] = [
  {
    id: 'day-1',
    title: 'Day 1 — Agent basics and context',
    note: 'The agent loop, what the model sees of a tool, and where conversation state lives.',
    entries: [
      {
        concept: 'The agent loop in code',
        summary:
          'Each turn builds a Microsoft Agent Framework ChatClientAgent with a tool middleware and OpenTelemetry. The loop, the pairing of tool calls with results and the stop condition live in the framework and the runner, not in the model. Calls to tools that do not exist never reach the middleware, so the runner also watches function calls in the stream.',
        paths: ['src/Maf.Lab.Api/Agent/ChatTurnRunner.cs'],
        spec: 'chat-agent',
        screen: CHAT,
      },
      {
        concept: 'Tool description as the selection lever; tool_choice',
        summary:
          'Tool descriptions state when to use the tool and when not to, naming the right tool instead. Forcing a named tool uses RequireSpecific; Ollama ignores tool_choice, so a small chat client issues the forced call itself on the first iteration and the trace records it as tool.forced.',
        paths: [
          'src/Maf.Lab.Retrieval/Tools/SearchDocumentsTool.cs',
          'src/Maf.Lab.Api/Agent/RequiredToolModeChatClient.cs',
        ],
        spec: 'retrieval-tool',
        screen: CHAT,
      },
      {
        concept: 'Context budget and history',
        summary:
          'History is persisted outside the process in SQLite and holds only the user text and the final answer — tool calls and tool data are not replayed. A token budget, not a message count, trims what goes back to the model.',
        paths: [
          'src/Maf.Lab.Api/Agent/SqliteChatHistoryProvider.cs',
          'src/Maf.Lab.Api/Agent/TokenCounter.cs',
        ],
        spec: 'chat-history',
        screen: CHAT,
      },
      {
        concept: 'Client data: retention as its own decision',
        summary:
          'Message content has its own store and its own retention window, separate from trace retention. Logs and telemetry carry structure — tool names, latencies, counts — and never message content.',
        paths: ['src/Maf.Lab.Api/Storage/MessageRetentionService.cs'],
        spec: 'chat-history',
      },
      {
        concept: 'Testing an AI service',
        summary:
          'Tools are plain methods with unit tests that need no model; the intent classifier is tested against a fake. Selection and quality are measured by eval suites with thresholds and negative examples, not by unit tests.',
        paths: ['tests/Maf.Lab.Tests', 'src/Maf.Lab.Eval/Suites'],
        spec: 'eval-harness',
        screen: { to: '/evals', label: 'Evals' },
      },
    ],
  },
  {
    id: 'day-2',
    title: 'Day 2 — MCP server and tool design',
    note: 'The protocol after 2026-07-28, tool contracts, confirmation before writes, several servers.',
    entries: [
      {
        concept: 'Stateless MCP core',
        summary:
          'Both MCP servers run the official C# SDK in its stateless mode: no session id, protocol version and capabilities travel with every request. That is what lets them sit behind the load balancer as two replicas each.',
        paths: ['src/Maf.Lab.Retrieval/Program.cs', 'src/Maf.Lab.Portfolio/Program.cs'],
        spec: 'retrieval-tool',
        screen: { to: '/topology', label: 'Topology' },
      },
      {
        concept: 'MRTR confirmation before a write',
        summary:
          'The fee-adjustment tool first answers input_required with a signed proposal; only the second call, carrying that signed state, executes it. The chat shows the proposal as a confirmation card; a declined proposal is an outcome the tool reports, not an error.',
        paths: [
          'src/Maf.Lab.Retrieval/Tools/FeeAdjustmentTools.cs',
          'web/src/chat/ConfirmationCard.tsx',
        ],
        spec: 'fee-adjustment',
        screen: CHAT,
      },
      {
        concept: 'Idempotency',
        summary:
          'The client sends an idempotency key that reaches the tool in MCP _meta; processed keys live in Redis, shared by the replicas. A unique constraint on the ledger is the second guard, turning a repeat into "already applied".',
        paths: ['src/Maf.Lab.Hosting/Stores/RedisIdempotencyStore.cs'],
        spec: 'shared-state',
      },
      {
        concept: 'Errors the model can act on',
        summary:
          'Tool errors come back as isError results without stack traces or hostnames. A transport failure or timeout reads as retryable; anything else asks the model to rephrase. The UI tells unavailable, refused and unexpected apart.',
        paths: ['src/Maf.Lab.Retrieval/Tools/ToolErrors.cs'],
        spec: 'retrieval-tool',
      },
      {
        concept: 'DTOs designed for the model',
        summary:
          'Tool results are purpose-built DTOs in the shared Domain project, never entities. The legacy free-text note field is left out on purpose, so it cannot carry an instruction to the model.',
        paths: [
          'src/Maf.Lab.Domain/Billing/BillingRunDtos.cs',
          'src/Maf.Lab.Domain/Portfolio/PortfolioDtos.cs',
        ],
        spec: 'retrieval-tool',
      },
      {
        concept: 'One MCP server per domain',
        summary:
          'The api connects to the billing server and to every server listed in its configuration — today portfolio — and merges their tools. A server that is down loses only its own tools for that turn; a duplicated name is kept by the first server.',
        paths: ['src/Maf.Lab.Api/Agent/ToolSource.cs', 'src/Maf.Lab.Portfolio'],
        spec: 'chat-agent',
        screen: CHAT,
      },
    ],
  },
  {
    id: 'day-3',
    title: 'Day 3 — RAG as a tool of the MCP server',
    note: 'Chunking, hybrid retrieval, tenant isolation in Qdrant, injection, evals and feedback.',
    entries: [
      {
        concept: 'Chunking follows the source',
        summary:
          'Documentation is split by heading path, code by function and class, procedures by numbered step. Provenance and tenant are attached before chunking; the domain is a collection of its own rather than a payload field.',
        paths: [
          'src/Maf.Lab.Indexing/Chunking/MarkdownChunker.cs',
          'src/Maf.Lab.Indexing/Chunking/CodeChunker.cs',
          'src/Maf.Lab.Indexing/Chunking/ProcedureChunker.cs',
        ],
        spec: 'document-indexing',
      },
      {
        concept: 'Hybrid retrieval through the Query API',
        summary:
          'BM25 is implemented in C# with a persisted vocabulary. One Qdrant query carries a dense and a sparse prefetch, each several times the final limit and each tenant-filtered, fused server-side with RRF (DBSF by configuration).',
        paths: [
          'src/Maf.Lab.Retrieval/Sparse/Bm25Encoder.cs',
          'src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs',
        ],
        spec: 'hybrid-retrieval',
        screen: CHAT,
      },
      {
        concept: 'Tenant filter inside the query',
        summary:
          "firm_id comes from the token's claims, never from an argument. One method takes the principal and builds every query, applying the tenant filter in each prefetch and in the outer query; a test scans the IL to prove there is no other path.",
        paths: [
          'src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs',
          'src/Maf.Lab.Retrieval/Auth/PrincipalAccessor.cs',
        ],
        spec: 'tenant-isolation',
      },
      {
        concept: 'Payload-based multi-tenancy',
        summary:
          'One collection per domain with tenant_id indexed as is_tenant, HNSW m=0 and payload_m=16, so each tenant gets its own sub-graph. This is the "many small tenants" mode of the plan.',
        paths: ['src/Maf.Lab.Retrieval/Store/CollectionBootstrapper.cs'],
        spec: 'tenant-isolation',
      },
      {
        concept: 'Embedding model change and index drift',
        summary:
          'Embedding models are profiles, each point records the model per vector, and a change of model means a new profile and a rebuild. make drift measures stale and missing documents between the source and the index.',
        paths: [
          'src/Maf.Lab.Retrieval/Configuration/Options.cs',
          'src/Maf.Lab.Indexing/Pipeline/DriftService.cs',
        ],
        spec: 'document-indexing',
      },
      {
        concept: 'Prompt injection in layers',
        summary:
          'Tool results are wrapped as data, not instructions; a guardrail screens the prompt and tool content; writes need a human confirmation. An injection suite with canaries measures whether a persuaded model can still do harm.',
        paths: [
          'src/Maf.Lab.Api/Agent/ToolDataEnvelope.cs',
          'src/Maf.Lab.Api/Agent/Guardrail.cs',
          'src/Maf.Lab.Eval/Suites/InjectionSuite.cs',
        ],
        spec: 'injection-defense',
        screen: { to: '/evals', label: 'Evals' },
      },
      {
        concept: 'Eval plan: selection, retrieval, generation',
        summary:
          'Selection measures recall, precision and negative accuracy; retrieval measures recall@k and MRR; generation is scored for faithfulness by a model judge. Each run is compared with an accepted baseline and regressions are named.',
        paths: ['src/Maf.Lab.Eval/Suites', 'src/Maf.Lab.Eval/Reports/RegressionGate.cs'],
        spec: 'eval-harness',
        screen: { to: '/evals', label: 'Evals' },
      },
      {
        concept: 'Learning from production',
        summary:
          'Turns raise review signals such as a how/why answer without a tool or a search with no results. Structured feedback — wrong tool, document, answer or confirmation — lands in a review queue, where a human labels it into the eval datasets.',
        paths: [
          'src/Maf.Lab.Api/Agent/TurnSignals.cs',
          'web/src/chat/TurnFeedback.tsx',
          'web/src/admin/LabelForm.tsx',
        ],
        spec: 'web-ui',
        screen: { to: '/admin/feedback', label: 'Feedback review' },
      },
    ],
  },
  {
    id: 'day-4',
    title: 'Day 4 — A2A, multi-agent and AG-UI',
    note: 'Agent to agent, the router decision, and the stream between agent and user.',
    entries: [
      {
        concept: 'A2A hosting',
        summary:
          'The lab publishes an Agent Card and hosts a billing agent whose tasks move from submitted through working to completed or input-required. Tasks are stored so a run continues after the stream drops, with push notifications for long tasks.',
        paths: ['src/Maf.Lab.A2A/AgentCardFactory.cs', 'src/Maf.Lab.A2A/A2AEndpoints.cs'],
        spec: 'a2a-hosting',
        screen: { to: '/admin/a2a', label: 'Agent to agent' },
      },
      {
        concept: 'A2A client',
        summary:
          "Before a fee adjustment, the assistant consults a separate compliance agent found by its Agent Card, over a streaming A2A message. It authenticates as itself — the user's token is not forwarded — and every way the consultation can end, including a question back or a timeout, is a typed result.",
        paths: ['src/Maf.Lab.Plugins.Abstractions/ReviewerConsultation.cs', 'src/Maf.Lab.A2A'],
        spec: 'a2a-client',
        screen: { to: '/admin/a2a', label: 'Agent to agent' },
      },
      {
        concept: 'One assistant with a router',
        summary:
          "The plan's first-phase answer: one assistant, and a classifier that picks the domain before the first model call. Jev decides the intent and which domains are in scope, and only the searches of those domains are forced.",
        paths: ['src/Maf.Lab.Api/Agent/Jev/JevIntentClassifier.cs'],
        spec: 'intent-classification',
      },
      {
        concept: 'AG-UI event stream',
        summary:
          "The chat agent is an Agent Framework AIAgent behind the official AG-UI server (MapAGUIServer); only the protocol's own events travel — run lifecycle, steps, text, tool calls, state, activities, interrupts — never a custom one. The browser reaches it through CopilotKit and its runtime, and renders every tool call as a card, not a spinner.",
        paths: [
          'src/Maf.Lab.Api/Agent/ChatAgent.cs',
          'src/Maf.Lab.Api/Agent/AGUI/AGUIMappings.cs',
          'web/src/agents/AgentsProvider.tsx',
        ],
        spec: 'agui-stream',
        screen: CHAT,
      },
      {
        concept: 'React model: reducer over events',
        summary:
          "The chat state is a useReducer over the protocol's own events, as CopilotKit's agent delivers them, with idempotent steps, so a repeated tool-call start changes nothing. TanStack Query serves request–response screens; the run's trace is read from the trace API while it lasts.",
        paths: ['web/src/chat/chatReducer.ts', 'web/src/chat/useChatStream.ts'],
        spec: 'chat-stream',
        screen: CHAT,
      },
    ],
  },
  {
    id: 'day-5',
    title: 'Day 5 — System design',
    note: 'The whole system on one sheet: deployment, identity along the chain, observability.',
    entries: [
      {
        concept: 'Stateless replicas behind a load balancer',
        summary:
          'nginx fronts two replicas each of the api and the MCP servers. Conversation turns, pending proposals and A2A tasks live in shared SQLite; run state and idempotency keys live in Redis, so any replica can serve the next request.',
        paths: ['compose/lb/nginx.conf', 'src/Maf.Lab.Hosting/SharedState.cs'],
        spec: 'load-balancing',
        screen: { to: '/topology', label: 'Topology' },
      },
      {
        concept: 'Observability: a span for every step',
        summary:
          'OpenTelemetry spans cover HTTP, tool calls and MCP calls and go to Jaeger and Prometheus while the observability plugin is in use. Each chat turn also keeps its own trace, which the monitor next to the chat replays step by step.',
        paths: [
          'src/Maf.Lab.Hosting/LabTelemetry.cs',
          'src/Maf.Lab.Api/Agent/Tracing/TurnTrace.cs',
        ],
        spec: 'telemetry',
      },
    ],
  },
  {
    id: 'rules',
    title: "The plan's rules, as enforced here",
    note: 'Rules the plan states as non-negotiable, and the place in the code that holds each one.',
    entries: [
      {
        concept: 'The tenant comes from the token',
        summary:
          'No tool, endpoint or query builder takes a tenant parameter; the principal is read from JWT claims. A tenant the model writes into an argument has nowhere to go.',
        paths: ['src/Maf.Lab.Domain/Tenancy/PrincipalClaims.cs'],
        spec: 'tenant-isolation',
      },
      {
        concept: 'A token never sits in a prompt, result, state or log',
        summary:
          'The Jev key is read in one place and sent only as a bearer header; the chat key is read when the client is created. Neither appears in a prompt, a trace or a log line.',
        paths: ['src/Maf.Lab.Retrieval/Jev/JevCredential.cs'],
        spec: 'intent-classification',
      },
      {
        concept: 'No write without a human',
        summary:
          'Every write behind the agent goes through the MRTR confirmation described on day 2. A persuaded model can propose a fee adjustment but cannot apply one.',
        paths: ['src/Maf.Lab.Retrieval/Tools/FeeAdjustmentTools.cs'],
        spec: 'write-confirmation-ui',
        screen: CHAT,
      },
      {
        concept: 'Evals on change, not on every commit',
        summary:
          'Evals are a separate console app, run on demand or when the prompt, a tool description, the model or the tool set changes. Running make eval compares one suite with its accepted baseline.',
        paths: ['src/Maf.Lab.Eval', 'evals/baseline.json'],
        spec: 'eval-harness',
        screen: { to: '/evals', label: 'Evals' },
      },
    ],
  },
];

export const NOT_COVERED: { topic: string; reason: string }[] = [
  {
    topic: 'LangChain4j and the Java stack',
    reason:
      "The lab is written on the plan's other candidate, Microsoft Agent Framework in .NET; AI Services, @Tool and who produces sparse vectors in Java stay study material.",
  },
  {
    topic: 'MapAGUIServer, STATE_SNAPSHOT and STATE_DELTA',
    reason:
      'The api streams AG-UI events from its own endpoint rather than MapAGUIServer, and sends no state snapshot or delta events.',
  },
  {
    topic: 'Re-attaching the browser to a live run',
    reason:
      'The server keeps run state and exposes it, but the web client does not call it; after a reload it recovers through the pending proposal instead.',
  },
  {
    topic: 'Tool filtering per conversation and domain prefixes',
    reason:
      'Every turn offers all tools; a duplicated name is kept by the first server rather than prefixed by its domain.',
  },
  {
    topic: 'A tools/list snapshot test per server',
    reason:
      'Only tool names are asserted; there is no verified snapshot of descriptions and schemas.',
  },
  {
    topic: 'Trace context in MCP _meta',
    reason:
      'Traces cross service boundaries through HTTP instrumentation; _meta carries diagnostics, not trace context.',
  },
  {
    topic: 'Shard-based and tiered multi-tenancy',
    reason:
      'The lab uses the payload-based mode only; tiered with tenant promotion is a documented follow-up.',
  },
  {
    topic: 'Token exchange and the legacy REST bridge',
    reason:
      "There is no legacy system to call, so there is no on-behalf-of token exchange (RFC 8693); the api forwards the caller's bearer token to its own MCP servers.",
  },
];
