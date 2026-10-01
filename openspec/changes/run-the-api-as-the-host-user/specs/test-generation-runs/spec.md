## ADDED Requirements

### Requirement: Repository writes keep the developer's ownership
Committing a run's candidate branch, accepting it (whether `main` is merged in the checkout that has it or in a
temporary working tree) and discarding it SHALL leave every path they create or change in the repository — objects,
refs, reflogs, the index and working-tree files — owned by the user who runs the stack, never by root or by a service
account. After any of them, that user SHALL be able to commit, branch and merge in the repository as before.

#### Scenario: Accept on a Linux host
- **WHEN** an administrator accepts a candidate and `main` is checked out in the developer's clean checkout on Linux
- **THEN** the merged test files, the new objects, `main`'s ref and its reflog are owned by the developer, and the developer's next `git commit` succeeds

#### Scenario: A run's candidate branch
- **WHEN** a verified run is committed onto `test-agent/<file-slug>-<runId>`
- **THEN** the branch's ref, its reflog and the objects written for it are owned by the developer
