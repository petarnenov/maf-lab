## ADDED Requirements

### Requirement: A plugin confirms its writes through the core's seam
A plugin whose tool writes SHALL contribute a write-confirmation flow keyed by that tool's name, and MAY contribute a
web renderer for that tool's summary. The flow SHALL own its summary's schema, and SHALL reach the audit chain, the screening of a reviewer's words,
the turn's trace and a reviewer only through the core's ports in `Maf.Lab.Plugins.Abstractions`. The core SHALL NOT name a
plugin's write, its summary's fields or its statuses.

#### Scenario: A new plugin with a write
- **WHEN** a plugin adds a write tool, its flow and, optionally, a renderer
- **THEN** a person is asked before that write, the card shows its summary, and no file outside the plugin's folder changes

#### Scenario: No web part
- **WHEN** a plugin contributes a flow but no web renderer
- **THEN** the card shows the summary from the flow's schema

#### Scenario: The core names no write
- **WHEN** the core's code is searched for a plugin's write tool, summary type or status
- **THEN** none is found
