# neo4j-browser

[Neo4j Browser](https://neo4j.com/docs/browser/) on http://localhost:7175, a developer tool allowed in dev and qa (`make plugin-on NAME=neo4j-browser`, `make plugin-off NAME=neo4j-browser`).
It is linked from the web UI's navigation and published on `127.0.0.1` only.

The graph store; connect to `bolt://localhost:7687` as `neo4j` with `NEO4J_PASSWORD` (dev default
`maf-lab-dev-graph`). It runs any Cypher you type, writes included, while the lab itself reads the graph only through
its fixed, tenant-scoped queries. `make graph` rebuilds whatever you change.
