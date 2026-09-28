## ADDED Requirements

### Requirement: Curriculum map screen
The `/curriculum` screen SHALL explain where the concepts and rules of the 5-day Fullstack AI Engineer study plan
(revision 7) are applied in the lab. It SHALL be reachable from the main navigation.

The screen SHALL group its entries by the plan's days, in the plan's order, followed by a section for the plan's
rules. Each entry SHALL give:
- the concept's name;
- a short explanation, of two or three sentences, of how the lab applies it;
- the repository paths that implement it;
- the spec that states it;
- a link to the screen where it can be seen, when such a screen exists.

The screen SHALL end with a section naming the plan's topics the lab does not implement, each with its reason, so the
page never claims coverage the code does not have.

The screen SHALL render from content shipped with the web app. It SHALL NOT call the api and SHALL NOT require a
session or a dev persona.

#### Scenario: Entries grouped by day
- **WHEN** the user opens `/curriculum`
- **THEN** one labelled section per day of the plan appears in the plan's order, then the rules section, then the not-covered section

#### Scenario: An entry names where to look
- **WHEN** the user reads an entry
- **THEN** it shows the explanation, at least one repository path and the spec name
- **AND** when the concept is visible in the app, a link opens that screen

#### Scenario: Readable without a persona
- **WHEN** no dev persona is picked
- **THEN** the whole page renders and no api request is made

#### Scenario: Honest about gaps
- **WHEN** the user reaches the end of the page
- **THEN** each topic of the plan that the lab does not implement is listed with its reason
