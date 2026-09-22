# Tasks

## 1. The store, and what every service does without it

- [x] 1.1 Pin `StackExchange.Redis` 3.3.0 and `redis:8.8.3-alpine`, and record both and why in `DECISIONS.md`;
      verify `make lint-dotnet` builds clean.
- [x] 1.2 Add Redis to `compose/docker-compose.yml` with its own volume and a healthcheck, and give api,
      mcp-retrieval and compliance its address; verify `make` brings the stack up healthy.
- [x] 1.3 Add `AddSharedState(...)` to `src/Maf.Lab.Hosting/` beside `AddLabTelemetry`: one multiplexer for the
      process, the store registrations, and a refusal to start when the store cannot be reached; verify a test
      asserts a service with an unreachable store does not begin serving and names the store.
- [x] 1.4 Report the store's reachability on `/health` so an unreachable store takes the replica out of the pool,
      and recovers without a restart; verify tests cover unhealthy while it is away and healthy once it is back.
- [x] 1.5 Report Redis in the topology and add it to `docs/topology.drawio` with its edges; verify the diagram
      test passes and the report lists it.

## 2. Message content has one retention

- [x] 2.1 Conversations stay in SQLite: the list is one query over the turns beside them, and splitting the two
      would make it a cross-store join and put the same text in two places. Recorded in proposal.md and
      DECISIONS.md; no store interface is needed for them.
- [x] 2.2 Add the compliance retention for message content to `SharedStateOptions`' neighbours in the api's
      configuration, stated separately from `Tracing:RetentionDays`; verify a test asserts changing one leaves
      the other where it was.
- [x] 2.3 Sweep conversations, their messages and their turns once the retention has passed, beside the trace
      retention job; verify a test ages a conversation past it and finds the conversation, its messages and its
      turns gone, and a fresh one untouched.
- [x] 2.4 Confirm deleting a conversation still takes effect at once, whatever the retention says; verify the
      existing delete tests pass unchanged and one asserts a deleted conversation is gone before its retention.

## 3. A run that can be rejoined

- [x] 3.1 Define `IRunStateStore` and a Redis implementation holding a run's answer so far, its tool calls and
      their outcomes, whether it is waiting for a person, and its outcome once it ends, with a grace period after
      the run; verify unit tests cover a running, a paused and a finished run, and one whose state has expired.
- [x] 3.2 Update the snapshot from `ChatEndpoints.Stream` as each event is yielded — the one place every event of
      a run passes; verify a test asserts the snapshot matches what the stream produced at several points.
- [x] 3.3 Add `GET /api/chat/{runId}`, subject to the thread's ownership; verify tests cover rejoining a running
      run, one that finished, one that stopped for a person, another principal's run (not found) and an expired
      one (not found).
- [x] 3.4 Confirm `RunRegistry` and `RunStopper` still do their own job — cancelling a request this replica serves
      — and say in the code why that is allowed to be per-replica; verify the stop tests pass unchanged.

## 4. The reviewer's tasks and webhooks stop living in its memory

- [x] 4.1 The api's A2A tasks and webhooks stay in SQLite: `/admin/a2a` reads them in one query together with the
      audit rows, which do not move, and they are already shared between its replicas. Only the reviewer, which
      has neither a page nor a database, moves. Recorded in proposal.md and DECISIONS.md.
- [x] 4.2 Add a Redis `ITaskStore` and `IPushConfigStore` in the reviewer, in place of `InMemoryTaskStore` and
      `InMemoryPushConfigStore`; verify unit tests cover saving and reading a task, and a webhook per task.
- [x] 4.3 Declare that the reviewer needs them, so it refuses to start without the store; verify a test asserts it
      does not begin serving with no store registered.
- [x] 4.4 Remove `InMemoryPushConfigStore`; verify a test starts a review through one host, reads the task through
      a second host over the same store, and sees a webhook registered on one honoured by the other.

## 5. An idempotency key the caller owns

- [x] 5.1 Define `IIdempotencyStore` and a Redis implementation that records the answer given under a key with a
      digest of the request, replays it for a repeat, and refuses a different request under a used key; verify
      unit tests cover all three, and expiry.
- [x] 5.2 Take an idempotency key on the write tool in `src/Maf.Lab.Retrieval/Tools/FeeAdjustmentTools.cs` and
      answer a repeat from the store; verify a test applies once, sends the same call again and gets the first
      answer with nothing applied twice.
- [x] 5.3 Generate the key in the browser when a person confirms, and carry it through the resume request to the
      tool; verify a web test asserts the key is sent and is the same on a retry of the same confirmation.
- [x] 5.4 Confirm the ledger's `UNIQUE (firm, adjustmentId)` still stands as the guarantee about the money;
      verify the existing at-most-once tests pass unchanged.

## 6. Handles

- [x] 6.1 Confirm by test that the history cursor and the proposal state are understood by any replica and refused
      when altered; verify a test parses a cursor minted elsewhere and rejects a tampered proposal state.
- [x] 6.2 Check that nothing mints a handle that is a key into process memory, and say so where a handle is made;
      verify the check is a test rather than a comment.

## 7. Documentation and verification

- [x] 7.1 Document the store, the keys, the two retentions, the rejoin endpoint and the idempotency key in
      `docs/http-api.md` and a `docs/shared-state.md`; verify the documented shapes match what is served.
- [x] 7.2 Run `make lint` and `make test` and confirm they pass.
- [x] 7.3 Run the stack (`make`), hold a conversation, close the stream mid-turn and rejoin it through the
      balancer; confirm the snapshot is right and that `make verify` passes.
- [x] 7.4 Stop Redis and confirm the replicas report unhealthy and refuse work plainly, then start it and confirm
      they serve again without a restart.
