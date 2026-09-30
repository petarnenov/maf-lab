# Proposal

## Why

`make` can take more than five minutes. Almost all of that time goes to NuGet downloads inside the image builds, not
to compilation. Each of the seven .NET Dockerfiles copies `src/` and then runs `dotnet publish`. Any source edit
therefore invalidates that layer, and every image restores all of its packages again from an empty cache. All seven
images do this in parallel against `api.nuget.org`. On a 2026-09-30 run, single projects spent 3.6–4.4 minutes in
restore, and several downloads failed with `Received an unexpected EOF or 0 bytes from the transport stream` before
retries got them through.

## What Changes

- The .NET image builds share one persistent NuGet package cache, kept by the local Docker builder. A package
  downloaded by one image build becomes available to every later build of every image.
- Restore and publish become separate steps in each build stage. Only restore touches the shared cache, and it
  holds the cache exclusively, so parallel builds cannot corrupt a package folder. It then copies the project's
  packages from the cache into the build stage. Publish needs neither the cache nor the network, and still runs in
  parallel.
- The coverage-runner image restores the test project's packages into its own `/opt/nuget`, because the runner has
  no network at run time. That restore now reads only from the shared cache. The step before it fills the cache from
  nuget.org with any packages still missing. The image contents do not change.
- No package versions change. Images still contain exactly what they contain today.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `make-workflow`: adds a requirement that rebuilding the stack after a source-only change does not download NuGet
  packages again.

## Impact

- `src/Maf.Lab.{Api,Retrieval,Portfolio,CodeSearch,ComplianceAgent,TestAgent,CoverageRunner}/Dockerfile`.
- The local Docker builder's cache grows by the package set, about the size of `~/.nuget/packages` for this repo.
  `docker builder prune` removes it. `make clean` leaves it alone, because it is a download cache, not a build
  output.
- CI: the e2e job builds on a fresh runner, so the cache starts empty there and gives no speed-up, but also no
  change in behavior. The workflows' own NuGet caching (`actions/cache`) stays as it is.
- No application code, API, route, make target or package version changes.

## Documentation impact

- `DECISIONS.md`: a new section records the shared build cache, why restore takes it exclusively, how the
  coverage-runner image is fed from it, and how to clear it.
- README.md, CLAUDE.md, docs/*.md, openspec/project.md and .github/copilot-instructions.md: none. They describe
  `make` and the stack, not how an image restores packages, and no command or target changes.
