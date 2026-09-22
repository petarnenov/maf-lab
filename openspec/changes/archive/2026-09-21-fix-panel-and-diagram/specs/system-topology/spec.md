# Spec Delta

## MODIFIED Requirements

### Requirement: The diagram is the drawing, the report is the truth
The repository SHALL contain the topology diagram in an editable, text-diffable draw.io file, and that file SHALL be
the only place the layout, labels and shapes of the picture are defined. Every node in the diagram SHALL correspond
to a service in the report and every service in the report SHALL appear in the diagram, matched by the stable id. A
mismatch SHALL fail the test suite, so adding a service to the stack cannot leave the picture behind.

The drawing SHALL be legible: no two boxes SHALL overlap, and a mismatch SHALL fail the test suite the way a
missing node does. A connection between two services SHALL be drawn so that it is visible along its whole length
— it SHALL stop at the boxes it joins rather than running under their text, and where a direct line would pass
through a third box it SHALL go around it.

The served diagram SHALL be the one currently drawn: the response SHALL NOT permit a client to use a stored copy
without first asking whether it is still current.

#### Scenario: A service is added without being drawn
- **WHEN** the report gains a service that the diagram does not contain
- **THEN** the test suite fails, naming the missing node

#### Scenario: The diagram stays readable in version control
- **WHEN** the diagram file is committed
- **THEN** its content is uncompressed XML, so a change to it is reviewable as a diff

#### Scenario: One box drawn over another
- **WHEN** two boxes in the committed diagram occupy any of the same space
- **THEN** the test suite fails, naming both

#### Scenario: A connection past a third service
- **WHEN** a straight line between two services would pass through a third
- **THEN** the connection is drawn around that service instead, and its label sits on the line

#### Scenario: The picture is redrawn
- **WHEN** the diagram file changes and a user opens the topology screen again
- **THEN** the screen shows the new drawing rather than the one the browser already had
