# Tasks

## 1. Foundation

- [ ] 1.1 Install .NET LTS SDK, create `maf-lab.sln` with the five `src/` projects and two test projects; verify `dotnet build` succeeds
- [ ] 1.2 Create `DECISIONS.md` pinning every package and image version (.NET, Agent Framework, MCP SDK, Qdrant + client, Ollama + models, Node/React stack); verify every referenced package has an exact version
- [ ] 1.3 Scaffold `web/` (Vite + React + TS, React Router, TanStack Query, Vitest, ESLint, Prettier); verify `npm test` and `npm run build` pass
- [ ] 1.4 Add `compose/docker-compose.yml` (qdrant, ollama, api, mcp-retrieval, web) and an Ollama model-pull script; verify `docker compose up -d` brings all services healthy
- [ ] 1.5 Add `.vscode/launch.json` (api + web compound) and `.vscode/tasks.json` (compose up, index, eval); verify tasks run from VS Code

## 2. Domain and tenancy

- [ ] 2.1 Define `Principal`, `TenantId`, `Role`, and result DTOs in `Maf.Lab.Domain`; verify the project has no infrastructure references
- [ ] 2.2 Implement the local dev JWT issuer endpoint and JWT bearer validation shared by api and mcp-retrieval; verify tests for valid, expired, and bad-signature tokens
- [ ] 2.3 Implement `IPrincipalAccessor` deriving the principal from claims only; verify a test that request parameters cannot change firm id

## 3. Sample corpus and seed data

- [ ] 3.1 Create `data/{firm-a,firm-b,firm-c,shared}/{docs,procedures,code}` with firm-b ~10x larger and firm-c ~50 documents; verify counts with a script
- [ ] 3.2 Add injection documents (embedded "ignore previous instructions…", "send this to external@…") in firm and shared corpora; verify they are present in the corpus manifest
- [ ] 3.3 Add `compose/seed/billing-runs.json` per firm including run 4417 (failed) and a record whose note contains an instruction; verify it loads in a unit test

## 4. Qdrant and BM25

- [ ] 4.1 Implement collection bootstrap (named dense + sparse vectors, `tenant_id` is_tenant index, HNSW m=0/payload_m, payload indexes); verify with a Testcontainers integration test reading collection info
- [ ] 4.2 Implement the BM25 tokenizer, vocabulary, IDF computation, and persistence in `maf_meta`; verify unit tests for tokenization, IDF values, and round-trip load
- [ ] 4.3 Implement `TenantScopedSearch.QueryAsync` (tenant filter on each prefetch and outer query, RRF default, DBSF by config, prefetch ≥5x limit, hybrid/dense/sparse modes); verify integration tests per mode
- [ ] 4.4 Implement `TenantScopedMaintenance` (delete by doc_id, scroll, conditional vector update) with the same tenant rule; verify integration tests
- [ ] 4.5 Implement `IReranker` with no-op default and Ollama implementation falling back to no-op on failure; verify a test with the reranker endpoint unavailable
- [ ] 4.6 Write the query-path enumeration test (IL scan of all `Maf.Lab.*` assemblies for Qdrant query calls outside the two scoped classes); verify it fails when a rogue call is added in a test fixture

## 5. Indexing pipeline

- [ ] 5.1 Implement source loaders with tenant-from-path and rejection of unowned documents; verify a test that an out-of-tree document is rejected and reported
- [ ] 5.2 Implement the Markdown heading chunker with section_path; verify tests on nested headings
- [ ] 5.3 Implement the procedure step/section chunker; verify tests on numbered steps and oversize steps
- [ ] 5.4 Implement the code chunker (function/class, file path, symbol name, fallback windows); verify tests on C#, TS and Python samples
- [ ] 5.5 Implement chunk metadata assembly with stable doc_id/chunk_id and deterministic point ids; verify two runs over an unchanged corpus produce identical ids
- [ ] 5.6 Implement switchable contextual enrichment with chunk-hash cache; verify on/off tests on stored text
- [ ] 5.7 Implement embedding (Microsoft.Extensions.AI) + BM25 encoding and upsert with delete-by-doc_id re-indexing; verify the acceptance test "re-indexing a changed document leaves exactly one version"
- [ ] 5.8 Implement the drift admin endpoint; verify acceptance tests for 0% after indexing and >0% after bumping a file timestamp
- [ ] 5.9 Implement the restartable `migrate --to <model>` command and `Retrieval:DenseVector` switch; verify acceptance test that kills the migration midway, reruns it, finds no duplicates, and queries succeed throughout

## 6. MCP retrieval server

- [ ] 6.1 Host the MCP server (Streamable HTTP, stateless, bearer auth) in `Maf.Lab.Retrieval`; verify tool listing and two sessionless calls in an integration test
- [ ] 6.2 Implement `search_documents` with input/output schemas, cap of 10, `truncated`, `refineHint`, and use/do-not-use description; verify contract tests including identifier-only queries
- [ ] 6.3 Implement `get_billing_run_status` and `search_billing_runs` over seed data with firm scoping and note-free DTOs; verify tests for own run, foreign run, and absence of the note field
- [ ] 6.4 Set tool annotations and implement sanitized error results; verify tests that annotations match and that a Qdrant outage yields short text without hostnames or stack traces
- [ ] 6.5 Record in DECISIONS.md every place the MCP SDK lags spec 2026-07-28; verify the section exists (even if "none")

## 7. Agent host

- [ ] 7.1 Build the Agent Framework agent in `Maf.Lab.Api` over `IChatClient` (Ollama; OpenAI/Azure by config) with MCP tools and per-user bearer forwarding; verify an integration test lists the three tools through the agent
- [ ] 7.2 Write the system prompt with 3–4 question→tool examples and one no-tool example; verify it is loaded from a versioned file
- [ ] 7.3 Implement the intent classifier and per-turn `search_documents` forcing; verify unit tests for procedural vs data vs chit-chat questions and that the next turn is not forced
- [ ] 7.4 Implement tool middleware: audit log (ids only, outcome, duration), data-block wrapping, and refusal + audit of unknown tools; verify tests including a hallucinated `send_email` call
- [ ] 7.5 Implement SQLite conversation memory bound to the principal with token windowing; verify tests for restart persistence and foreign conversation id rejection
- [ ] 7.6 Implement the SSE chat endpoint emitting `text_delta`, `tool_call_started`, `tool_call_finished`, `sources`, `done`; verify an integration test asserting `tool_call_started` precedes tool execution and `sources` precedes `done`
- [ ] 7.7 Implement feedback endpoints and production-signal detection (thumbs down, rephrase, no tool on how/why, zero results, long answer without sources); verify tests per signal
- [ ] 7.8 Add a logging test asserting no message content appears in structured logs for a full chat turn

## 8. Web

- [ ] 8.1 Implement routes `/chat`, `/evals`, `/admin/index`, `/admin/feedback` with a dev token picker; verify navigation and FIRM_ADMIN-only admin access in tests
- [ ] 8.2 Implement `chatReducer` and `useChatStream` (fetch + ReadableStream SSE); verify Vitest reducer tests for event ordering and partial chunks
- [ ] 8.3 Implement tool-call cards and the sources panel; verify Testing Library rendering tests for running and finished states
- [ ] 8.4 Implement the three feedback buttons with `feedbackReducer`; verify reducer tests and that clicking posts the structured event
- [ ] 8.5 Implement `/evals` reading report files via TanStack Query; verify a rendering test with a fixture report
- [ ] 8.6 Implement `/admin/index` (trigger indexing, drift %, model_version distribution, run migration); verify with mocked API tests
- [ ] 8.7 Implement `/admin/feedback` review queue and labeling form; verify a test that submission calls the append-to-dataset endpoint

## 9. Evaluation harness

- [ ] 9.1 Create `evals/selection.jsonl`, `retrieval.jsonl`, `generation.jsonl`, `injection.jsonl` including the four acceptance selection cases; verify a schema-validation test over each file
- [ ] 9.2 Implement the selection runner with recall/precision; verify on the acceptance cases
- [ ] 9.3 Implement the retrieval runner with recall@5, recall@20, MRR across hybrid/dense/sparse and rerank/contextual toggles; verify the report contains all three modes
- [ ] 9.4 Implement the generation runner with fixed-rubric LLM judge (faithfulness, answer relevance); verify a run produces scores for every row
- [ ] 9.5 Implement the injection runner (forbidden strings and tenant ids); verify it fails on a planted leaking answer fixture and passes on the real agent
- [ ] 9.6 Implement JSON + Markdown reports, config thresholds, and non-zero exit on failure; verify with a threshold set above achievable
- [ ] 9.7 Implement feedback import into datasets; verify the acceptance flow "click wrong document → label → next retrieval run includes the row"

## 10. Acceptance and documentation

- [ ] 10.1 Implement the multi-tenant acceptance tests (firm A only sees A + shared; no post-filter shrinkage; small tenant gets top-10; firm B absent from logs and reranker input); verify all pass against Testcontainers Qdrant
- [ ] 10.2 Document in DECISIONS.md the sparse-encoder choice, multitenancy mode, tiered-multitenancy trigger, and Agent Framework gaps; verify all four sections exist
- [ ] 10.3 Document in README when evals must be run (prompt, tool description, model, tool set, chunking changes); verify the section exists
- [ ] 10.4 Run the definition of done end to end: `docker compose up`, index the corpus, ask a procedural question in the UI with visible tool calls and sources, run `--suite all`; verify every step succeeds and all acceptance scenarios pass
