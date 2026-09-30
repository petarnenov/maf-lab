# Spec Delta

## ADDED Requirements

### Requirement: Image builds reuse downloaded packages
Building the stack's .NET images SHALL fetch each NuGet package from the network at most once per machine. Later
builds of any image SHALL take packages they already have from a persistent local cache. This SHALL hold when
several images are built in parallel. A rebuild after a source-only change MUST NOT download packages again. The
images SHALL contain the same content they would contain without the cache. An image that needs packages at run
time SHALL still carry them inside the image. Clearing the Docker build cache SHALL be the only way to drop the
package cache, and after it the next build MUST succeed by downloading again.

#### Scenario: Rebuild after a source edit
- **WHEN** a developer edits a `.cs` file under `src/` and runs `make` again with the machine offline from nuget.org
- **THEN** every .NET image rebuilds and the stack becomes healthy, with no package download attempted

#### Scenario: Parallel builds share one download
- **WHEN** `make` builds all .NET images in parallel on a machine whose package cache is empty
- **THEN** every build succeeds, and a package several images reference is downloaded once

#### Scenario: Runner stays offline-capable
- **WHEN** the coverage-runner image is built from the shared cache and then started with `--network none`
- **THEN** it restores, builds and tests a workspace as before, because the packages it needs are inside the image

#### Scenario: Cache cleared
- **WHEN** a developer runs `docker builder prune` and then `make`
- **THEN** the build downloads the packages again and succeeds
