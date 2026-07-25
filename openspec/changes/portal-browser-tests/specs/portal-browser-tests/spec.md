# portal-browser-tests — browser coverage of the portal's interactive behaviour

## ADDED Requirements

### Requirement: Browser tests drive the portal against stubbed read-model sources
The suite SHALL host the portal with its read-model sources replaced by stubs it controls, so any state — populated, empty, degraded, unreadable — is reachable deterministically without deployed infrastructure.

#### Scenario: Any panel state is reachable on demand
- **WHEN** a test needs a stack whose queue source is failing
- **THEN** it configures that stub to fail and the page renders the degraded panel, with no Azure resource involved

#### Scenario: Stubs share the production contract
- **WHEN** a read-model contract changes
- **THEN** the stubs fail to compile rather than silently continuing to return a shape the real source no longer produces

### Requirement: An unreadable value is visibly distinct from a zero value
The rollup SHALL render a cell whose source could not be read differently from a cell whose source returned zero, and the unreadable cell MUST NOT display a number.

This is the requirement the harness exists for. Only one of those two states is safe to act on, the difference is expressed entirely in markup, and no HTTP-level assertion can see it.

#### Scenario: Zero and unreadable do not look alike
- **WHEN** one stack's queue reads zero and another stack's queue source is failing
- **THEN** the first cell shows a zero depth and the second shows an unreadable marker, and the two are distinguishable by more than colour

#### Scenario: A stack with unreadable sources keeps its row
- **WHEN** every source for one stack is failing
- **THEN** that stack still appears as a row with its cells marked unreadable, rather than being omitted from the rollup

### Requirement: A failing source degrades its own panel only
The stack view SHALL render the remaining panels normally when one source fails, and the failing panel SHALL state what could not be read.

#### Scenario: One failure does not cost the page
- **WHEN** the executions source fails and the queue and history sources succeed
- **THEN** the queue and history panels render their data and only the executions panel reports itself unavailable

#### Scenario: Empty is not the same as unavailable
- **WHEN** one panel's source returns no items and another panel's source fails
- **THEN** the first states that there is nothing to show and the second states that it could not be read

### Requirement: Interactive controls are exercised through the browser
The suite SHALL cover the controls that only function over the interactive circuit, which the HTTP-level tests cannot reach.

#### Scenario: Changing the history window re-reads
- **WHEN** a user changes the history window selector
- **THEN** the view re-renders against the newly selected window rather than continuing to show the previous one

#### Scenario: A verb reports each of its outcomes
- **WHEN** a verb is performed, refused, and fails across three runs
- **THEN** each renders its own distinct result, and the refusal shows the live state that caused it

### Requirement: Status is never communicated by colour alone
Every status indicator the suite encounters SHALL carry a text label, not only a colour or a shape.

The design system states this as a rule and a unit test guards the stylesheet's tokens; nothing has verified what a browser actually renders.

#### Scenario: A status badge carries text
- **WHEN** any status badge is rendered
- **THEN** it contains a text label describing the status

### Requirement: Elements are addressed by stable test identifiers
Tests SHALL select elements by a dedicated test identifier attribute, and MUST NOT select by user-facing text.

Copy changes for product reasons, and the portal renders vendor-supplied strings — item titles and client-authored gate names — that are not this product's to depend on.

#### Scenario: Rewording does not break a test
- **WHEN** a user-facing label is reworded with no behavioural change
- **THEN** the suite still passes

### Requirement: The suite is excluded from the default run but never silently skipped
The suite SHALL be excluded from the repository's default test command and SHALL run in its own continuous-integration lane, which MUST fail on a failing test rather than being skipped or reported as passing.

A browser suite that quietly never runs reads as coverage while providing none, which is worse than not having it.

#### Scenario: Default run stays fast
- **WHEN** the repository's default test command runs
- **THEN** the browser suite is not part of it

#### Scenario: A failure in the lane is visible
- **WHEN** a browser test fails in its lane
- **THEN** the lane fails, rather than the result being skipped or swallowed
