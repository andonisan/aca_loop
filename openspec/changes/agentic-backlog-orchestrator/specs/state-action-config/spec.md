# state-action-config — configurable state→action mapping

## ADDED Requirements

### Requirement: Declarative per-target mapping
The system SHALL be configured by a declarative mapping file listing targets, where each target declares `{vendor, project/repo coordinates, states}` and each state entry declares `{native_state, action, runtime, on_success_state, on_failure_state}`. The mapping SHALL be the only source of "which state dispatches which agent action" — no state or command names hardcoded in receiver or agent code.

#### Scenario: v1-equivalent configuration expressible
- **WHEN** the mapping declares a GitHub target with `status:ready-for-proposal → {action: "/ds:propose", runtime: claude, on_failure_state: "status:blocked"}` and `status:ready-for-implementation → {action: "/ds:implement", runtime: claude, on_failure_state: "status:blocked"}`
- **THEN** the system reproduces the v1 behavior for that repo without code changes

#### Scenario: Unmapped state produces no dispatch
- **WHEN** a canonical event arrives whose `native_state` has no entry for that target
- **THEN** the receiver drops the event and enqueues nothing

#### Scenario: Unknown target produces no dispatch
- **WHEN** a canonical event arrives from a repo/project not declared as a target
- **THEN** the receiver drops the event and enqueues nothing

### Requirement: Mapping deployed with the orchestrator, not the target repo
The mapping file SHALL be part of the orchestrator's deployment (baked or mounted configuration). The receiver MUST NOT fetch mapping configuration from target repositories at event time; target repositories own only the agent commands themselves.

#### Scenario: Target repo cannot grant itself dispatches
- **WHEN** a target repository adds or edits a mapping-like file in its own tree
- **THEN** the orchestrator's dispatch behavior is unaffected

### Requirement: Fail-fast schema validation
The receiver SHALL validate the mapping against its schema at startup and refuse to serve traffic on validation failure, logging the specific violations.

#### Scenario: Invalid mapping blocks startup
- **WHEN** the receiver starts with a mapping entry missing a required field (e.g. no `action`)
- **THEN** the receiver exits/reports unhealthy and processes no webhooks

### Requirement: Gate re-validation against the mapping
The agent SHALL re-validate, immediately before executing, that the live item still carries the exact `native_state` that produced its queue message, resolving the check through the same mapping. If the state moved, the agent SHALL exit successfully without executing the action or calling any LLM.

#### Scenario: Stale gate exits cleanly
- **WHEN** an agent execution dequeues a message whose item was moved to a different state after enqueueing
- **THEN** the agent exits 0 with no action executed and the message is deleted

#### Scenario: Duplicate delivery is idempotent
- **WHEN** the same state-change event is delivered and enqueued twice and the first execution already advanced the item's state
- **THEN** the second execution exits 0 at gate re-validation
