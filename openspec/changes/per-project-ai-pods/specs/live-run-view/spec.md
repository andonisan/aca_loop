# live-run-view — watching an execution in flight, read-only

## ADDED Requirements

### Requirement: A running execution can be followed without waiting for it to end
The portal SHALL show, for an execution in flight, its correlated trace timeline and its console stream, updating as the run proceeds.

#### Scenario: Progress is visible mid-run
- **WHEN** an operator opens a running execution
- **THEN** they see which phase it is in, how long each completed phase took, and console output produced so far

#### Scenario: A finished execution degrades to history
- **WHEN** an execution completes while being watched
- **THEN** the view settles into the completed run's outcome and its retained logs, rather than erroring or appearing stuck

### Requirement: The view is read-only, with no execution path into the container
The portal SHALL provide no means of executing commands inside a running pod — no shell, no exec, no command injection through any parameter of the view.

The agent container holds write-capable credentials for the target repository and is running a model over content authored in an issue. Command execution inside it, from a surface that spans clients, is remote code execution in a credentialed container.

#### Scenario: No interactive path exists
- **WHEN** the live run view is used in any way
- **THEN** no operation reaches the container other than reading its output

### Requirement: Console output is treated as untrusted input
Console output SHALL be rendered as text and never as markup, and known credential shapes SHALL be redacted before display.

Output can contain content authored in the issue body — prompt injection is a named surface for this product — and rendering it as markup would make the portal a delivery vehicle for it.

#### Scenario: Injected markup does not render
- **WHEN** an execution's output contains HTML or script, whether from an issue body or from a tool it ran
- **THEN** it appears as literal text in the portal and nothing is interpreted

#### Scenario: Credential shapes are redacted
- **WHEN** output contains a recognizable credential, such as a clone URL carrying a token
- **THEN** it is redacted before display

#### Scenario: Redaction is not claimed to be complete
- **WHEN** the live view is presented to an operator
- **THEN** it states that output is raw agent output with known secret shapes removed, so redaction is not mistaken for a guarantee

### Requirement: Watching a run requires the same access as the project's other data
Access to a live run SHALL be gated on the same authorization as the rest of that project's data, and unauthorized access MUST be refused by the server rather than by the view not offering it.

#### Scenario: Direct request is refused
- **WHEN** a user without access to a project requests one of its executions' live output by direct request rather than through the UI
- **THEN** the request is refused and no output is returned

### Requirement: The trace timeline is usable when the console stream is not
Trace and console SHALL be independent: if the console stream is unavailable, the trace timeline still renders, and vice versa.

The spans are this product's own structured output, while the console is raw text from a third-party runtime — the safer and more reliable half should not be lost to the failure of the other.

#### Scenario: Console unavailable, progress still visible
- **WHEN** an execution's console stream cannot be read
- **THEN** the trace timeline still shows which phase the run is in, and the console area reports itself unavailable
