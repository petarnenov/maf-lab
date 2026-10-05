# Tasks

No Jev call is added or changed (Jev is only held in a test), so the Jev review checklist does not apply.

## 1. Mutations

- [x] 1.1 The `web-ui` requirement is changed by this change's delta; check every `mutationFn` in `web/src` and verify
      the control that sends it is disabled while it is pending (fix any that is not). Verify with a Vitest test that a
      test-generation run's start is not aborted by Esc while pending, and that Esc after it answered cancels the run.

## 2. Esc on the four screens

- [x] 2.1 Telemetry, Jev statistics, evals and A2A admin: `useEscToStop` on the loading query and `<StopHint>` while it
      loads. Verify with one Vitest test per screen: a held request is aborted on Esc, and the screen keeps its last
      data.

## 3. The graph guard

- [x] 3.1 In `GraphStoreTests`, add the test that every driver call in the two graph classes is `TerminateAsync` or a
      lambda whose enclosing method calls `GraphStop.RunAsync`. Verify that it passes, and that it fails (naming the
      method) when a direct `ExecutableQuery` is added to one of the classes (checked once, then reverted).

## 4. Paid calls are cancelled

- [x] 4.1 Add a hold to `ScriptedChatClient` and to `FakeJev` that waits on the call's token and records cancellation.
      Verify that the existing tests still pass.
- [x] 4.2 Add the two chat stop tests to `RunProtocolTests`: stopped while the model streams — the model's token fired,
      nothing more was yielded, no turn was recorded; stopped while a Jev call is out — its request's token fired, and no
      model or further Jev request followed. Verify both pass; if either shows a call not cancelled, fix it here and say
      what was wrong.
      Found: the model and Jev calls were cancelled, but under the full suite the Jev-stopped run was left `running` in
      the shared run state — RunTap marked a run cancelled only when `RequestAborted` was already set as its response
      ended, and wrote nothing for a run stopped before its first frame. Fixed in `RunTap`: a response that ends with no
      terminal event is recorded `cancelled` (a run that ended keeps its outcome), with or without a frame written.

## 5. Checks

- [x] 5.1 Run `make lint` and `make test`, and verify both pass.

## 6. Documentation

- [x] 6.1 No document is made untrue (proposal, Documentation impact). Run `make docs` and verify it changes nothing,
      then `make docs-check` and verify it passes.
- [x] 6.2 Run `npx --yes @fission-ai/openspec@1.13.1 validate close-stop-gaps --strict` and verify it passes.
