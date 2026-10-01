## ADDED Requirements

### Requirement: The stack writes to the host as the user who runs make
Every service that writes into a directory of the host — the repository, `data/` and `evals/` — SHALL run as the
user and group that run `make`. `make` SHALL pass that user's numeric id and group id to compose as `MAF_LAB_UID`
and `MAF_LAB_GID`, overridable like any other variable; compose used without `make` SHALL default them to `1000`.
Named volumes such a service writes SHALL be made writable for that user before it starts, including volumes an
earlier version created as root. The behaviour on Docker Desktop for macOS, which already maps bind mounts to the
host user, SHALL not change.

#### Scenario: A Linux host
- **WHEN** a developer with uid 1000 runs `make` on Linux and the stack writes into the repository, `data/` or `evals/`
- **THEN** every file and directory it creates or changes there is owned by uid 1000 and the developer's group, none by root

#### Scenario: A volume from an earlier version
- **WHEN** the api's named data volume holds files owned by root from before this change
- **THEN** `make up` hands them to the user before the api starts, and the api starts healthy and can write its data

#### Scenario: Continuous integration
- **WHEN** `make ci-e2e` runs on a CI runner whose user is uid 1001
- **THEN** the api runs as uid 1001 and the end-to-end run, including accepting a test-generation run, passes

### Requirement: make up repairs root-owned leftovers
Before starting the stack, `make up` SHALL look for paths owned by root (uid 0) under the checkout and under the
mounted repository. Only when it finds any SHALL it change the owner of exactly those paths to the user and group
that run `make`, and it SHALL print how many it repaired. It MUST NOT change any path owned by another user, MUST NOT
follow symbolic links, and MUST do nothing when `make` itself runs as root. It SHALL end with one line saying whether
anything was repaired, per the `progress-feedback` rule.

#### Scenario: Leftovers from a root api
- **WHEN** earlier runs left root-owned refs under `.git/refs/heads/test-agent/` and a root-owned test file in the working tree
- **THEN** `make up` reports how many paths it repaired, they are owned by the user afterwards, and `git commit` works again

#### Scenario: Nothing to repair
- **WHEN** nothing under the checkout or the repository is owned by root
- **THEN** `make up` changes no owner, starts no extra container for it, and says there was nothing to repair

#### Scenario: Another user's files
- **WHEN** a path under the checkout is owned by a user other than root and other than the one running `make`
- **THEN** its owner is left unchanged
