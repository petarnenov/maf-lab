# Spec Delta

## ADDED Requirements

### Requirement: The Jev statistics screen shows skipped calls
The `/admin/jev` overview SHALL show how many calls an open circuit skipped over the period. It SHALL show the count
per site, next to that site's unavailable requests.

The requests and unavailability timeline SHALL draw the skipped calls as a series of their own. The series SHALL be named
in the legend, and its values SHALL be offered on hover. An outage therefore stays visible after the breaker opens.

When no call was skipped in the period, the skipped count SHALL read 0 and the timeline SHALL draw no skipped series.

#### Scenario: Outage with an open circuit
- **WHEN** a FIRM_ADMIN opens `/admin/jev` for a period in which Jev timed out and the circuit then skipped calls
- **THEN** the overview shows the unavailable requests and the skipped calls as separate numbers per site, and the
  timeline shows both series in the buckets where they occurred

#### Scenario: No skipped calls
- **WHEN** no call was skipped in the chosen period
- **THEN** the skipped count reads 0, and the timeline shows only requests and unavailability
