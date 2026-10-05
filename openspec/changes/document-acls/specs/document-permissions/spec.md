# Spec Delta

## ADDED Requirements

### Requirement: The assistant respects document permissions
Every indexed chunk and graph node SHALL carry the ids of the groups and users allowed to read it at its source, or an
explicit marker for the whole tenant. The indexing path SHALL refuse a chunk without it. Every search and graph read
SHALL filter by the principal's tenant, user id and group ids in the one method that builds the query, so no plugin
can retrieve, cite or summarise a chunk the user may not open. For the graph, the condition SHALL hold for every node
a query matches or returns, not only its starting node. Items with no permissioned source (built from code, such as
terms, runs or code symbols) SHALL get `tenant:all` by construction. Group ids SHALL come from the token only.

#### Scenario: A document only HR may read
- **WHEN** a user outside the HR group asks a question whose best source is an HR-only document
- **THEN** that document is not retrieved, cited or summarised, and the answer uses only sources the user may read

#### Scenario: A path through a restricted node
- **WHEN** a user outside HR traces relationships, and a path from an account reaches an HR-only document node
- **THEN** that path is not returned, and neither is any node beyond it

#### Scenario: Restricted chunk text is not republished by the graph
- **WHEN** an HR-only chunk is copied into the graph as a retrieval chunk, and a user outside HR runs a graph
  retrieval
- **THEN** the copied node carries the HR-only acl, and its text is not returned

#### Scenario: make graph sets permissions by construction
- **WHEN** `make graph` builds billing runs and code symbols
- **THEN** every such node carries `acl = ["tenant:all"]`, every retrieval chunk carries its point's acl, and the run
  writes no node without an acl

#### Scenario: A plugin indexes without permissions
- **WHEN** a plugin's loader upserts a chunk with no permission list
- **THEN** the upsert is refused and names the source
