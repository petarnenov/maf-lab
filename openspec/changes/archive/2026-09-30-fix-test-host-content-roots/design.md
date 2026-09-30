# Design

## Context

See proposal.md, Why. `WebApplicationFactory<T>` (used for the api, retrieval, portfolio and code search) already sets
the content root to the project's own directory. Only the hosts that tests start with `Program.BuildApp([], …)`
inherit the process's working directory, which is the test output folder.

## Goals / Non-Goals

**Goals:** each in-process host reads exactly the configuration it has in production, whatever the build order.

**Non-Goals:** changing which files projects copy to their output. Removing `appsettings.json` from the referenced
projects' outputs would change their publish layout and the container images.

## Decisions

1. **Pass `--contentRoot` to `BuildApp`.** Add a helper `ProjectDir.Of("Maf.Lab.CoverageRunner")` that walks up from
   `AppContext.BaseDirectory` to the directory holding `maf-lab.sln`, and returns `src/<project>`. Tests call
   `BuildApp(["--contentRoot", ProjectDir.Of(...)], …)`. `WebApplication.CreateBuilder(args)` honors it, so no
   production signature changes.
   *Alternative:* supply every needed key in each test's in-memory configuration. It is brittle: a new key in
   `appsettings.json` silently falls back to whatever file won the copy.
2. **Guard test.** Start each in-process host once and resolve `IOptions<A2AOptions>`: the runner is
   `maf-lab-coverage-runner`, the agent `maf-lab-test-agent`, the compliance agent `maf-lab-compliance`. This fails
   fast, and with a clear message, if a new host is added without the helper.

## Risks / Trade-offs

- [The tests depend on the source layout (`src/<project>`)] → That layout is fixed by the repository layout in
  `openspec/project.md`. The helper fails with the path it looked for if the layout moves.
