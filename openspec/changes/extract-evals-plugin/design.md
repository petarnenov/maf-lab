# Design

## Context

See proposal.md and the plugins delta. The CLI currently hosts production agent classes, the API writes labelled
datasets, and domain cases share the persistent reports directory. The code MCP host and graph builder still live
outside their domain folder because the evaluation host constructs them directly.

## Goals / Non-Goals

**Goals:** Remove the evaluation feature as a folder; preserve report/baseline formats and production-path evaluation;
read domain cases only while their owning plugins are installed; accept a completed report without another model run.

**Non-Goals:** Change model prompts, judgment questions, pinned model or thresholds; execute live suites or refresh
indexes during code migration. Existing reports and accepted baselines remain persistent deployment data.

## Decisions

1. Separate the in-process report/dataset module from its CLI tools. The API module uses the abstractions and shared
   domain report types; a neutral runner project in the plugin's service directory hosts the evaluation classes and
   production API services. The API gains no reference to that runner. Its trace/ask command shares this dev harness
   and moves with it. Test references use the existing recursive service glob and extern alias.
2. Introduce a dataset append port with a Null Object for the core's label endpoint. Labels remain stored in SQLite
   when the evaluation module is absent; the optional writer creates evaluation rows only when installed. The report
   controller, writer and web types/page are contributed together.
3. Keep common datasets under the persistent root and move domain-specific rows into their owning plugin's evals
   directory. The loader discovers JSONL files from the installed catalogue, retains row IDs and validation, and
   reports duplicate IDs instead of silently scoring them twice. Existing feedback rows are an additive root dataset.
4. Resolve the code MCP endpoint from the installed catalogue with deployment configuration taking precedence.
   Move the code host, graph builder and graph tests into the code folder; use a graph-build contribution through
   shared data contracts so the indexer keeps generic persistence/progress and no compiled reference to a domain
   builder. Pure builders cannot depend on the indexer executable, which would form a reference cycle.
5. Accept an existing report before initializing model hosts or stores. Validate its run ID, suite, variants and passed
   thresholds, then call the existing BaselineStore.Accept logic. Comparisons remain ineligible for acceptance.
6. Keep BuiltIn until its last reader is removed. The feedback review extraction still reads it, so this migration
   removes the evaluation readers and the next extraction removes the final reader and scanner exemption.

## Jev requests

No request, state field, question type/instruction/criterion, confidence gate, fallback or tuned model changes. The
existing request definitions move with the harness and retain jev-1.13.0. Any private instruction DTO needed by the
runner keeps the existing serialized context/question fields; fixture checks verify that shape without a live call.

## Risks / Trade-offs

- Moving cases can change coverage or duplicate labelled feedback → preserve IDs/rows, discover only installed owners,
  and verify loader counts and duplicate validation with fixtures.
- Moving a runner changes internal assembly identities → keep namespaces/wire formats, use generic test discovery
  and explicit content roots, and remove fixed project references from the core solution.
- A graph builder can create a circular dependency → keep graph DTOs/contribution contracts in shared assemblies and
  load installed contributions without naming a plugin assembly in core code.
- Accepting a malformed or failed report could weaken a baseline → validate before writing and exercise failed,
  comparison, traversal and successful cases in temporary directories without model calls.

## Migration Plan

Move module/tool code, cases, make inputs and documentation together; run builds, fixture tests and strict checks with
the folder present and removed. Keep persistent reports/baselines for rollback. Stack/index-backed validation follows
all planned code moves, per the owner's sequencing decision; archive only after required final gates pass.
