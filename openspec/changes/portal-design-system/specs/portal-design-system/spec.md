# portal-design-system — one visual language, tokenised, with domain state pinned to treatment

## ADDED Requirements

### Requirement: All visual values come from the token layer
The portal's UI SHALL derive every colour, font family, font size, radius, spacing step, shadow, and transition duration from CSS custom properties defined in a single token layer. Page markup, component markup, and page-specific stylesheets MUST NOT contain raw colour literals (hex, `rgb()`, `oklch()`), nor hard-coded font families or shadow values. New components SHALL be built by composing existing tokens; a component needing a value the tokens do not express requires a token addition, reviewed as such.

#### Scenario: A new page is styled without new raw values
- **WHEN** a page or component is added to the portal
- **THEN** its styles reference only token custom properties for colour, type, radius, spacing, elevation, and motion, and a search of the portal's CSS and Razor markup for colour literals outside the token layer returns nothing

#### Scenario: A component needs a colour the tokens lack
- **WHEN** a required visual treatment cannot be expressed with existing tokens
- **THEN** the token is added to the token layer for both light and dark themes, and the component references the new token rather than an inline value

### Requirement: Domain state maps to visual treatment through one definition
Each orchestrator domain state that is rendered as a status SHALL resolve to its visual treatment through a single shared definition in code, not by markup choosing a style class directly. The mapping MUST cover run outcomes (`succeeded`, `failed`, `stale-gate`, `poison`, `wip-refused`), pod execution states, and queue-depth health. Two pages rendering the same state MUST produce the same treatment. Adding a domain state without extending the mapping MUST be a visible failure, not a silently unstyled or default-styled value.

#### Scenario: The same outcome renders identically across pages
- **WHEN** a run with outcome `failed` appears in both a cross-stack dashboard and a per-stack run history
- **THEN** both render the same badge treatment, obtained from the shared mapping

#### Scenario: stale-gate is distinguished from failure
- **WHEN** a run ends with outcome `stale-gate` and another ends with `failed`
- **THEN** the two render with visibly different treatments, and `stale-gate` is not presented as an error

#### Scenario: An unmapped state is caught
- **WHEN** a domain state with no entry in the mapping is rendered
- **THEN** the failure is surfaced (exhaustiveness enforced at compile time where the state is a closed type, otherwise a failing unit test), rather than falling back to a neutral or default treatment

### Requirement: Both themes are supported, with an explicit choice overriding OS preference
The portal SHALL render legibly in light and dark themes. Every token defined for light MUST have a dark counterpart. In the absence of an explicit theme selection the portal SHALL follow the operating system's `prefers-color-scheme`; an explicit selection, expressed as a `data-theme` attribute on the document root, MUST override the OS preference in both directions.

#### Scenario: OS preference applies on first visit
- **WHEN** an operator whose OS is set to dark opens the portal with no theme previously chosen
- **THEN** the portal renders in the dark theme

#### Scenario: Explicit light choice survives a dark OS
- **WHEN** the document root carries `data-theme="light"` on a system set to dark
- **THEN** the portal renders in the light theme

#### Scenario: No token is theme-incomplete
- **WHEN** the token layer is inspected
- **THEN** every colour token defined for the light theme has a corresponding dark-theme value, and no view renders foreground and background from mismatched themes

### Requirement: Status is never communicated by colour alone
Any status rendering SHALL carry a non-colour signal — a text label, and where space allows a shape indicator — in addition to its colour. Interactive elements SHALL present a visible focus indicator when focused via keyboard; a rule that removes a default outline MUST supply a replacement.

#### Scenario: A status badge is readable without colour perception
- **WHEN** a status badge is rendered and viewed without colour discrimination
- **THEN** the state remains identifiable from its text label

#### Scenario: Keyboard focus is visible on every control
- **WHEN** an operator tabs through a page's buttons, links, inputs, and navigation items
- **THEN** each focused element shows a visible focus indicator

### Requirement: Numeric data uses tabular figures
Columns of numbers — queue depths, durations, counts, costs, timestamps — SHALL render in the monospace token family with tabular figures enabled, so that digits align vertically across rows.

#### Scenario: A column of durations aligns
- **WHEN** a table renders run durations across many rows
- **THEN** the digits align in fixed columns rather than shifting per row

### Requirement: The vendored design system records its provenance
The stylesheet adopted from Atlas SHALL carry a header identifying its upstream source and the date it was retrieved, and the change SHALL document which layers were transcribed verbatim, which were re-authored, and the procedure for re-syncing when upstream moves. A deliberate divergence from upstream MUST be marked at the point of divergence.

#### Scenario: A maintainer can trace and re-sync the system
- **WHEN** a maintainer needs to reconcile the portal's stylesheet with a newer upstream Atlas release
- **THEN** the stylesheet header names the upstream asset and retrieval date, and the documented procedure identifies which layers to diff

#### Scenario: A divergence is not mistaken for drift
- **WHEN** the portal's stylesheet differs from upstream deliberately
- **THEN** a comment at that rule states the divergence and its reason, so a re-sync does not silently revert it

### Requirement: Shared primitives are globally scoped
Shared UI primitives (layout shell, navigation, cards, tables, badges, buttons, form controls, utilities) SHALL be defined in global stylesheets so that a class name carries one meaning across the whole portal. Blazor CSS isolation MAY be used for a component's private internals but MUST NOT be used to redefine a shared primitive.

#### Scenario: A primitive means the same thing everywhere
- **WHEN** the same primitive class is used in two different components
- **THEN** both render identically, with no component-scoped redefinition of that class
