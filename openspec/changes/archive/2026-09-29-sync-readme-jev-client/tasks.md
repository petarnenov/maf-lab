# Tasks

## 1. README

- [x] 1.1 Add a **Reaching Jev** paragraph to README's **Behind the scenes** section, covering the shared kept-alive client, the warm-up, retries and the per-attempt log lines, with every setting's name and default taken from `JevOptions`. Verify: each `Jev:*` name and default in the paragraph matches `src/Maf.Lab.Retrieval/Jev/JevOptions.cs`, and the example log lines match the templates in `JevRetryHandler.cs` / `JevWarmup.cs`

## 2. Verification

- [x] 2.1 Run `openspec validate sync-readme-jev-client --strict` and `openspec validate --all --strict`; both pass
