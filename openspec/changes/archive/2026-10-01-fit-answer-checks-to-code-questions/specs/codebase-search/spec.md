## MODIFIED Requirements

### Requirement: Codebase search returns places in files
The codebase server SHALL offer a read-only tool `search_codebase(query, kind?, pathPrefix?, maxResults?)`. It SHALL
return at most 10 snippets (5 by default). Each snippet SHALL carry:
- the path from the repository root;
- the 1-based first and last line of the text it returns, when known;
- the symbol it belongs to, when there is one;
- its section path;
- its kind (`code` or `docs`);
- its language;
- its score;
- its text.

A snippet's text SHALL be at most a configured number of characters. When a chunk is longer, the text SHALL be the
window of whole lines around the lines that match the query's terms, as the codebase's lexical tokenizer splits them
(the densest run of matching lines). When no line matches, the text SHALL be the chunk's first lines. The first and
last line SHALL be those of the returned window, so every line range a snippet carries is a range its text holds.

`kind` SHALL restrict results to source or to Markdown. `pathPrefix` SHALL restrict results to files under that path.
The tool SHALL never return a synthesized answer. When nothing matches, it SHALL return no snippets and a hint on how
to rephrase.

#### Scenario: Find a type by a phrase
- **WHEN** a user searches "tenant scoped search query"
- **THEN** a snippet from `src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs` is returned with its line range and symbol

#### Scenario: Restrict to a folder
- **WHEN** a user searches with pathPrefix `web/src/`
- **THEN** every returned snippet's path starts with `web/src/`

#### Scenario: Nothing relevant
- **WHEN** a user searches for something the repository does not contain
- **THEN** the result has no snippets and a refine hint

#### Scenario: The matching line lies past the limit
- **WHEN** a chunk longer than the snippet limit holds the line that matches the query's identifier after its first 1,200 characters
- **THEN** the returned text contains that line, and the snippet's first and last line are the window's, not the chunk's

#### Scenario: No line matches
- **WHEN** a long chunk is returned for a query none of whose terms appear in it, for example a Bulgarian phrase matched only by the dense branch
- **THEN** the text is the chunk's first lines up to the limit, and the line range is theirs
