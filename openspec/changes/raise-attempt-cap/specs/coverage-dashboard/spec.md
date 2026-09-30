## MODIFIED Requirements

### Requirement: Screen follows run progress live
While a run is active for a file, the screen SHALL show its state and progress (attempt n of N, where N is the run's attempt cap, and the latest coverage) as
updates arrive, without a page reload. This SHALL hold for the file's row in the tree as well as for the file view.
The row of a file with an active run SHALL show a live marker, a moving dot with the state, the attempt n/N and the
phase, and the marker SHALL follow the run's AG-UI stream. When a run ends, the tree and the file's detail SHALL
refresh, so the new state is shown without a manual refresh. After a run ends without a candidate, the file's row
SHALL show a short label of that outcome ("no change", "failed", "canceled", "verification failed"), with the reason
in its tooltip, until the file is measured again after the run.

#### Scenario: Progress arrives
- **WHEN** the agent finishes attempt 2 of 10 at 71%
- **THEN** the file's run status shows attempt 2/10 and 71% within a few seconds

#### Scenario: Row shows a live run
- **WHEN** a run on a file enters attempt 2 building
- **THEN** that file's row in the tree shows the live marker with "working 2/10 · building" within a few seconds, without a reload

#### Scenario: Row shows the last outcome
- **WHEN** a run ends `completed_no_change` with reason `budget`
- **THEN** the live marker is gone, and the row shows "no change" with a tooltip saying the budget would have been exceeded

#### Scenario: Run ends
- **WHEN** a run is verified and its candidate is recorded
- **THEN** the tree and file view show the candidate without the user reloading
