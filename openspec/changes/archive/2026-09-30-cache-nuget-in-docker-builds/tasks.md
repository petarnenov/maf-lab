# Tasks

## 1. Build stages share the package cache

- [x] 1.1 In `src/Maf.Lab.Api/Dockerfile`, add `# syntax=docker/dockerfile:1`. Replace the single publish with a
  restore step under `--mount=type=cache,id=maf-lab-nuget,target=/root/.nuget/packages,sharing=locked`. That step
  fills the cache, then restores again with `--force --source /root/.nuget/packages` into `NUGET_PACKAGES=/nuget`.
  After it comes a `dotnet publish ... --no-restore` step with no mount (design D2). Verify:
  `docker compose -p maf-lab -f compose/docker-compose.yml build api` succeeds.
- [x] 1.2 Apply the same change to the Retrieval, Portfolio, CodeSearch, ComplianceAgent and TestAgent Dockerfiles,
  and to the build stage of the CoverageRunner Dockerfile. Verify: `grep -c 'id=maf-lab-nuget' src/*/Dockerfile`
  shows the mount in all seven, and `docker compose ... build` builds every .NET image.

## 2. Coverage-runner image stays offline-capable

- [x] 2.1 In the CoverageRunner runtime stage, fill `/opt/nuget` from the cache (design D4). First restore
  `tests/Maf.Lab.Tests` with `NUGET_PACKAGES=/root/.nuget/packages`, then again with `--force`,
  `NUGET_PACKAGES=/opt/nuget` and `--source /root/.nuget/packages` only, under a `sharing=locked` mount. Keep
  `rm -rf /seed` and `chmod`. Verify: the image builds, and `du -sh /opt/nuget` in it matches the image built from
  `main` to within a few MB.
- [x] 2.2 Verify the spec's "Runner stays offline-capable" scenario. Start the new runner image with
  `--network none` and run a workspace's .NET and web tests, as DECISIONS.md §57 did. Both suites run and Cobertura
  is produced.

## 3. Behavior check

- [x] 3.1 Cold cache, parallel. Remove only the `maf-lab-nuget` cache records (`docker buildx prune --filter id=<ID>`), invalidate `COPY src/`, then build all seven images in parallel. Every image builds and the stack becomes healthy. The restore logs show no `Failed to download` for a
  package that another image already fetched.
- [x] 3.2 Warm cache, source edit, no network. Touch a `.cs` file under `src/`, build each image with `--network none`, or with `--add-host api.nuget.org=0.0.0.0` where
  another step (`apt-get`, `npm ci`) needs the network.
  Every .NET image rebuilds without download attempts and the stack becomes healthy. Record the restore time
  before and after the change.
- [x] 3.3 Run `make test`, and `make ci` if the e2e stack can be built. Nothing regresses, because application code
  did not change.

## 4. Documentation

- [x] 4.1 Add a DECISIONS.md section, "NuGet packages cached across image builds (cache-nuget-in-docker-builds)".
  It covers the `maf-lab-nuget` cache id, `sharing=locked` on restore only and why, the runner's two-step fill of
  `/opt/nuget`, the measured before/after restore time from 3.2, and `docker builder prune` as the way to clear it
  (`make clean` does not). No package version moves.
- [x] 4.2 Run `make docs` (no `generated:` block is edited by hand) and then `make docs-check`. Both pass.
