# Design

## Context

There are seven .NET Dockerfiles under `src/*/Dockerfile`. All of them have the same build stage: copy
`global.json`, `Directory.Build.props`, `Directory.Packages.props` and all of `src/`, then run a single
`dotnet publish`, which restores implicitly. The `.dockerignore` keeps `bin/` and `obj/` out, so each build restores
into an empty `/root/.nuget/packages`. Compose builds the images in parallel. The layer is invalidated by any change
under `src/`, so in practice every `make` after an edit downloads every package, seven times over.

`src/Maf.Lab.CoverageRunner/Dockerfile` is the one exception. Besides its build stage, its runtime stage restores
`tests/Maf.Lab.Tests` into `/opt/nuget` (`NUGET_PACKAGES`), and those packages must stay in the image: the runner has
no network at run time (DECISIONS.md §57, "Offline by construction").

The local builder is Docker Desktop with BuildKit (buildx 0.34, engine 29.5). Cache mounts need the Dockerfile
frontend 1.2 or newer. `# syntax=docker/dockerfile:1` selects it. The runner's Dockerfile already declares it.

## Goals / Non-Goals

**Goals:**
- One package cache per machine, shared by every .NET image build, surviving between builds.
- No corruption when several builds restore at the same time.
- Byte-for-byte the same image contents as today, including the runner's `/opt/nuget`.

**Non-Goals:**
- Speeding up CI. Runners start with an empty builder, and remote BuildKit cache export is not worth it for this
  lab.
- The web image's `npm ci`. It is one image and is not the bottleneck.
- Layer-caching restore by copying `*.csproj` first. See D3.

## Decisions

**D1. A BuildKit cache mount with one fixed id, `maf-lab-nuget`, at `/root/.nuget/packages`.** The fixed id lets all
seven Dockerfiles share it. The default id would be derived from the target path, which here happens to be the same,
but a named id says the sharing is intended and survives a path change. Alternative: a named Docker volume or a
host bind mount. `docker build` cannot mount volumes, and a bind mount would need the host's own NuGet folder,
coupling images to the developer's machine. Rejected.

**D2. The cache is mounted only in a restore step that holds it with `sharing=locked`. That step also copies the
project's packages into the stage. Publish runs with `--no-restore` and no mount.**
```dockerfile
ENV NUGET_PACKAGES=/nuget
RUN --mount=type=cache,id=maf-lab-nuget,target=/root/.nuget/packages,sharing=locked \
    NUGET_PACKAGES=/root/.nuget/packages dotnet restore src/Maf.Lab.Api/Maf.Lab.Api.csproj \
    && dotnet restore src/Maf.Lab.Api/Maf.Lab.Api.csproj --force --source /root/.nuget/packages
RUN dotnet publish src/Maf.Lab.Api/Maf.Lab.Api.csproj -c Release -o /app /p:UseAppHost=false --no-restore
```
NuGet's cross-process locks live in each container's own temp folder, so two containers extracting the same package
into the same shared folder are not protected from each other. `locked` serializes only the restores. When the cache
is warm, a restore takes seconds, so serializing it costs little. Compilation, the expensive part, stays parallel.
The second restore uses the D4 mechanism: the cache is its only source, and it extracts into `/nuget`, which exists
only in the build stage. The final image does not change.

The first implementation mounted the cache in publish too, in the default `shared` mode. On the cold parallel build
that failed with `NETSDK1064: Package ModelContextProtocol.Core, version 2.2.0 was not found`, because a `shared`
mount taken while other builds hold the same id `locked` does not see the locked content. Mixing modes on one id is
not reliable, so publish no longer touches the cache. Alternatives:
- `sharing=locked` on publish as well would serialize compilation of all seven images. Rejected.
- `sharing=private` gives each concurrent build its own copy, and would still download seven times on a cold
  cache. Rejected.

**D3. Keep `COPY src/ src/` and do not introduce a csproj-only restore layer.** A restore-only layer would need each
Dockerfile to list every `.csproj` in its project-reference closure. That list goes stale silently when a reference
is added. With the cache mount, re-running restore after an edit costs seconds and no network, so the layer adds
little.

**D4. The runner's `/opt/nuget` is filled from the cache in two steps inside one `RUN`.** First, restore with
`NUGET_PACKAGES=/root/.nuget/packages`. This fills the shared cache, downloading only what is missing. Then restore
again with `--force`, `NUGET_PACKAGES=/opt/nuget` and `--source /root/.nuget/packages` as the only source. The
global packages folder has the v3 hierarchical feed layout (`<id>/<version>/<id>.<version>.nupkg`), so it works as
a local feed. With a single local source, the second step makes no network call, and it fails loudly if a package
were missing. The mount is `sharing=locked`, like D2. `/opt/nuget` is a normal image path, so its content stays in
the image, as it must. Alternative: restore straight into the mount and `cp -r` it into `/opt/nuget`. That would copy
every package any image ever cached, not just the test project's. Rejected.

**D5. `make clean` does not prune the cache.** It is a download cache, not a build output, and dropping it would
bring back the slow first build for no gain. `docker builder prune` clears it, as for any BuildKit cache. DECISIONS.md
records this.

## Risks / Trade-offs

- [BuildKit may evict the cache mount under its GC policy when the builder cache is large] → The next build
  downloads again. That is still correct, only slower, and no worse than today.
- [The global packages folder does not work as a local feed for some package] → The runner's second restore fails
  the build loudly. Verified by building the runner image and running its tests with `--network none`. The fallback
  is to leave the runner's `/opt/nuget` restore uncached, as it is today.
- [An older builder without frontend 1.2] → `# syntax=docker/dockerfile:1` pulls the frontend. Docker Desktop and
  GitHub's `ubuntu-24.04` both ship BuildKit by default.
- [The id is ever mounted in another sharing mode] → BuildKit creates a second record under the same id. Locked
  restores then pick either record, run in parallel and download again. The cold test hit this (see D2). Every
  mount of `maf-lab-nuget` is `sharing=locked`, and DECISIONS.md §58 shows how to find and drop a stray record.
- [A restore interrupted half-way leaves a partial package folder in the cache] → NuGet writes `.nupkg.metadata`
  last and treats a folder without it as absent, then re-extracts.

## Migration Plan

Nothing to migrate. The first build after the change downloads once and fills the cache. Rollback means reverting
the Dockerfiles. The leftover cache is inert until `docker builder prune`.
