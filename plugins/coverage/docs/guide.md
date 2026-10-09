## Coverage, and a third agent that writes tests

**`/coverage`** shows how well the lab's own C# and TypeScript source is covered by tests — folder by folder, file by
file and line by line, each file against its threshold (a configured default, or the file's own override). Any
signed-in user can look; only a TENANT_ADMIN can change a threshold, refresh coverage or start, cancel, accept or
discard a run, and the server enforces that. `make coverage` refreshes the snapshot at `main` through the running
stack.

Raising a file's threshold above its current coverage asks to confirm, then for a model and its cost estimate, and
then starts a **test-generation run**. The api hands the file to the **test-agent**, a third agent reachable only over
A2A and only by the api. It reads the repository at the run's commit, may write only test files, and loops — write
tests, run them with coverage, read the result — until the file reaches the target or it runs out of attempts. The
tests run in the **coverage-runner**, which has no secrets and no route to the internet. A test the agent believes
exposes a bug is skipped and reported, not worked around.

The api then checks the result itself before anyone sees it, commits it to a candidate branch
(`test-agent/<file-slug>-<runId>`), and waits for a TENANT_ADMIN to accept it — a conflict-free merge into `main` — or
discard it. A suspected bug whose test still fails when un-skipped becomes a GitHub issue (`GITHUB_ISSUES_TOKEN`). The page
follows a run live, and the Coverage page shows the agent and its runs. `make testgen-e2e` drives the whole path without a
model; `make ci-e2e` includes it.

The api writes to the repository (those branches and merges) and `evals/` as the user who ran `make`
(`MAF_LAB_UID`/`MAF_LAB_GID`, from `id -u`/`id -g`), so on Linux everything it leaves there is yours; a one-shot
`api-data-init` hands its data volume to that user first. Earlier versions ran it as root: `make up` finds paths owned
by root in the checkout and gives exactly those back to you. On rootless Docker or `userns-remap`, run
`make MAF_LAB_UID=0 MAF_LAB_GID=0`.
