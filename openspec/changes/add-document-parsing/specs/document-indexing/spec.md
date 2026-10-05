# Spec Delta

## MODIFIED Requirements

### Requirement: Uniform chunk metadata
Every chunk SHALL carry doc_id (stable across runs), chunk_id, tenant_id (a tenant id or "shared"), source_type,
source_path, section_path, updated_at, model_version, acl (the ids allowed to read it, or `tenant:all`), and its text.
A chunk of a parsed document SHALL also carry pages: the pages of the source it came from.

#### Scenario: Metadata complete
- **WHEN** any chunk is read back from the index
- **THEN** all listed fields are present and non-empty (section_path may be empty only for code files without symbols)

#### Scenario: Stable doc_id
- **WHEN** the same unchanged corpus is indexed twice
- **THEN** each document has the same doc_id and chunk_ids in both runs

#### Scenario: Pages of a parsed document
- **WHEN** a chunk from page 3 of a parsed PDF is read back
- **THEN** it carries pages [3]
