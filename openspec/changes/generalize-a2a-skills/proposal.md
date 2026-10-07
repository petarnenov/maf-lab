# Proposal

## Why

The a2a plugin's agent card offers billing's skills (the run status, the simulated start-run) and its handler names
billing to reach them, fenced since extract-a2a-plugin (`// names a domain until generalize-a2a-skills`). A partner can
reach a domain's work over A2A only if that domain is billing, and the partner scope is still `a2a.billing.read`
(recorded as debt in DECISIONS §81 part K). This stub opens the change that lets every domain contribute its own A2A
skills.

## What Changes

- A skills seam: a domain plugin contributes its A2A skills (id, description, tags, the scope it needs, public or
  extended) and how each is served, through `Maf.Lab.Plugins.Abstractions`; the a2a plugin builds the card from the
  installed set and dispatches a skill to the domain that contributed it, as the chat reaches a domain's tools.
- Billing's two skills move from the a2a plugin into the billing plugin; the a2a plugin names no domain.
- The partner scope: `a2a.billing.read` stays accepted (partner compatibility); a per-skill scope is the proposal's
  question to settle.
- To be designed: whether a long-running skill's lifecycle (submitted → working → completed, input-required) is the
  a2a plugin's, with the domain supplying steps, or the domain's own handler.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `a2a-hosting`: the card's skills come from the installed domains.

## Principles

- SOLID: the a2a plugin depends on a skills abstraction, never on a domain (dependency inversion); a new domain's skills
  need no change to it (open/closed); the seam is one small interface per role (interface segregation).
- Standards: the official A2A protocol's agent card, skills and task lifecycle; the plugin contract of
  introduce-plugins (spec `plugins`), `IContributes*` (Orchard Core's module shape).

## Progress

None — to be settled by the design: a skill's work reports its own progress through the A2A task's status updates, as
the simulated run does today.

## Stopping

None — to be settled by the design: a skill's task stops by A2A `tasks/cancel`, through the a2a plugin's task store, as
today.

## Documentation impact

- `docs/http-api.md` (the a2a plugin's routes), the a2a plugin's README section, DECISIONS.
