# Tasks

## 1. The chain

- [x] 1.1 Widen `AuditRow` with `Kind`, `Hash`, `PreviousHash` and add the `(PrincipalId, At)` index; verify a test that an existing database gains the columns and the index without losing its rows, and that the 65-row shape still loads
- [x] 1.2 Make `ToolAudit` append a chained row: inside one `BEGIN IMMEDIATE` transaction read the current head by `Id`, compute `sha256(previousHash + "\n" + canonical(row))` over exactly the stored fields, insert, commit; verify tests for a three-row chain, a row altered in place (verification names it), a row deleted (verification fails at its successor), rows predating the chain (reported as before it), and two appenders running in parallel leaving one intact chain
- [x] 1.3 Add the verifier (walk by `Id`, recompute, report `intact`, `checked`, `from`, `to`, `head`, `firstBrokenId`, `reason`); verify it as a unit against a built chain, a broken one and an empty table

## 2. Actions beyond tools

- [x] 2.1 Record `conversation.delete` in `HistoryEndpoints` (principal, conversation, outcome) and keep the 404 for someone else's conversation unrecorded as a deletion; verify tests: deleting appends a row of that kind, the row survives the soft delete, and a refused delete adds nothing
- [x] 2.2 Keep tool calls exactly as they are (`Kind = "tool"`, same fields, still no query text) — verify the existing audit tests pass unchanged and a new test asserts a tool row and a deletion row sit in one ordered chain

## 3. Compliance endpoints

- [x] 3.1 `GET /api/admin/compliance/verify` under the `firm-admin` policy, returning the verification report; verify tests: 403 for a non-admin, intact for a clean chain, and the first broken id after tampering
- [x] 3.2 `GET /api/admin/compliance/export?from=&to=[&userId=]`: firm from the token only, optional subject narrowing, sections for audit/turns/conversations including deleted ones marked as such, and a manifest (`firmId`, `from`, `to`, `generatedAt`, `by`, counts, `sha256` of the content, `auditChainHead`); verify tests: another firm's data never appears even when a firm id is passed as a parameter, subject scoping, a deleted conversation is included and marked, the digest matches a re-hash of the content, and 403 for a non-admin
- [x] 3.3 Append the `compliance.export` row before returning the package (range and counts as identifiers, never content), with the manifest carrying the head from before that row; verify a test that the export appears in the next verification and in a later export

## 4. Verification and docs

- [x] 4.1 In the running stack on :7171 with its existing 65 rows: verify the chain (expect "before the chain" for the old rows), chat once and delete a conversation, verify again, export a period as alice and check the manifest counts and digest by re-hashing; then alter one chained row directly in the database copy and confirm the verifier names it. Run `make test`, `make lint`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`
- [x] 4.2 Update `docs/http-api.md` (both endpoints and the manifest), README (a compliance section: what is recorded, what can be handed over, and what the chain does *not* promise) and `DECISIONS.md` (one record not two, chain over row ids, concurrency, why no HMAC yet, and that tamper-evidence is not immutability); verify the sections exist
