# Spec Delta

## MODIFIED Requirements

### Requirement: Uniform chunk metadata
Every chunk SHALL carry doc_id (stable across runs), chunk_id, tenant_id (a tenant id or "shared"), source_type,
source_path, section_path, updated_at, model_version, and its text.

#### Scenario: Metadata complete
- **WHEN** any chunk is read back from the index
- **THEN** all listed fields are present and non-empty (section_path may be empty only for code files without symbols)

#### Scenario: Stable doc_id
- **WHEN** the same unchanged corpus is indexed twice
- **THEN** each document has the same doc_id and chunk_ids in both runs
