# Proposal

## Why

Draft follow-up opened by extract-a2a-plugin task 2.4. The assistant's A2A handler and card still contain billing-specific
skills. Their installed-plugin guards preserve today's behaviour, but another domain cannot contribute a skill without
editing the A2A plugin. This stub records the remaining design work; it does not authorize implementation.

## What Changes

- Plan a small skill-contribution seam so domains own their card descriptors and task behaviour; the A2A plugin owns
  the standard protocol framing, partner authentication, persistence and cancellation.
- Move the existing billing skills behind that seam, preserving their wire format, tenant entitlement and behaviour.
- Keep domain tools on official MCP and agent skills on official A2A 1.0; choose the dispatch and lifetime design in
  the follow-up design artifact before adding implementation tasks.
- Retain `a2a.billing.read` for existing partners until a separately agreed compatibility migration.

## Capabilities

### New Capabilities

None in this draft: the intended move preserves the currently exposed skills.

### Modified Capabilities

None in this draft. The design must establish whether a new public contribution capability requires a spec delta
before implementation; this proposal alone makes no new behavioural requirement.

## Impact

The A2A plugin, contributing domain plugins and `Maf.Lab.Plugins.Abstractions`. No core dependency on a domain or
plugin project. No implementation tasks are opened by this stub.

## Principles

- SOLID: each domain owns its skill; the A2A host depends on a small contribution contract (dependency inversion and
  interface segregation), without branches for individual domains (open/closed).
- Standards: official A2A 1.0 AgentSkill and task lifecycle; official MCP for domain tools; ports and adapters for
  the internal contribution contract. The design must record any additional mechanism in DECISIONS before implementation.

## Progress

None — this draft creates no executable work. Existing task status streams remain the planned progress mechanism.

## Stopping

None — this draft starts no work. The eventual move must preserve official A2A `tasks/cancel` and the owning store's
terminal state protection.

## Documentation impact

When designed and implemented: update docs/plugins.md for the contribution contract, the A2A plugin's HTTP reference
for skill ownership and DECISIONS §81 for the replacement of the temporary domain branches. No runtime documentation
changes are claimed by this draft.
