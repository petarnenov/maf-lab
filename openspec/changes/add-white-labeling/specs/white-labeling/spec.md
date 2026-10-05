# Spec Delta

## ADDED Requirements

### Requirement: The installation brand is core configuration
The core SHALL take the installation brand from its configuration (name, font, light and dark token values, asset
files) with neutral defaults, and SHALL serve it to anyone, including before sign-in. The shell SHALL contain no
product name of its own; internal storage keys are exempt.

#### Scenario: No branding plugin
- **WHEN** the `branding` plugin is not installed and no `Branding:*` is set
- **THEN** the app shows "Assistant" with today's colours and font and no logo, and no "maf-lab" text appears

#### Scenario: Before sign-in
- **WHEN** an anonymous visitor opens the app
- **THEN** `GET /api/branding` returns the installation brand, and no tenant's brand is revealed

### Requirement: A tenant's brand comes through one core port
A tenant's brand SHALL come only from an `IBrandProvider` contributed through `IContributesBrandProvider`. At most one
contributor SHALL be installed; otherwise the api SHALL refuse to start and name them. Before every call, the
core SHALL check that the contributing plugin is in use for the principal's tenant (installed, allowed and enabled). A
tenant's brand SHALL apply only then, and only when the tenant admin has set one. The core SHALL read tenant brand
data, the tone included, only through that port.

#### Scenario: A tenant without white-labeling
- **WHEN** a user of a tenant the operator has not allowed signs in
- **THEN** the installation brand stays, and the tenant admin sees no branding section

#### Scenario: Installed but disabled for the tenant
- **WHEN** the operator has allowed `branding` for a tenant whose admin set a brand and then disabled the plugin
- **THEN** the core does not call the provider for that tenant, and the installation brand applies

#### Scenario: Two brand providers
- **WHEN** two plugins contributing `IContributesBrandProvider` are installed
- **THEN** the api refuses to start and names both

#### Scenario: A tenant with its own brand
- **WHEN** a user of an allowed and set-up tenant signs in
- **THEN** the app switches to that tenant's name, logo, favicon, colours and font without a reload, in Light, Dark and
  System modes alike

### Requirement: Brand values are bounded, checked and hardened
A brand SHALL set the shell's existing colour tokens, never a parallel set. Their pairs (`--text`, `--muted` and `--link`
on the three backgrounds, and `--on-accent` on `--accent`, at 4.5:1; `--accent` and `--border` on `--bg` and
`--surface`, at 3:1) SHALL meet WCAG 2.2 AA in both themes. Logos and favicons SHALL be PNG or WebP by content, not animated, within size and dimension
caps, and re-encoded before they are stored. They SHALL be served from content-hash URLs with `nosniff`, `inline` and
immutable caching, and a tenant's assets only to its principals. Fonts SHALL come from a closed list served
first-party. A failing value SHALL be named, with nothing saved.

#### Scenario: Poor contrast
- **WHEN** a tenant admin saves a `--text` value with 3:1 contrast against `--bg`
- **THEN** the save is refused, the pair is named, and the previous brand stays

#### Scenario: An SVG or polyglot logo
- **WHEN** a tenant admin uploads an SVG, or a PNG carrying an appended script
- **THEN** the SVG is refused, and the PNG is stored only as its re-encoded image without the payload

### Requirement: No tenant text reaches the model
The assistant's brand name SHALL be shown by the UI and SHALL NOT be sent to the model. The greeting SHALL be the chat's
empty state, never a message. The tone SHALL be one of a closed set of reviewed fragments, and each tone SHALL hold the
generation, selection and injection eval baselines, with the tone recorded in the prompt trace and the eval settings.

#### Scenario: Name and greeting stay out of the model
- **WHEN** a user of a branded tenant starts and continues a conversation
- **THEN** no model request contains the brand name or the greeting

#### Scenario: A new tone
- **WHEN** the `friendly` tone is used
- **THEN** its eval runs hold their baselines, and its turns' traces record `tone: friendly`
