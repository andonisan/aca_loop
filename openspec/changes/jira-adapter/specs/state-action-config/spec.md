# state-action-config — code-host coordinates in the target mapping

## ADDED Requirements

### Requirement: Code-host resolution is validated at startup
The receiver SHALL validate at startup that every target's resolved code host corresponds to a registered code-host implementation, and refuse to serve traffic otherwise. A target naming a tracker-only vendor as its code host MUST fail this validation with a message identifying the target and the vendor.

#### Scenario: Tracker-only vendor named as code host blocks startup
- **WHEN** the receiver starts with a target declaring a code host that provides no code-host implementation
- **THEN** the receiver reports the violation naming that target and processes no webhooks

#### Scenario: Valid code host starts cleanly
- **WHEN** every target's code host resolves to a registered code-host implementation
- **THEN** the receiver starts and serves traffic

### Requirement: Non-repo-anchored targets resolve by tracker coordinate
Target resolution SHALL match an incoming canonical event on the coordinates that identify the item for that tracker. For a tracker whose items are not repository-scoped, the tracker's project coordinate SHALL be the matching key, and the target's code-host repository coordinate MUST NOT be required to match anything in the event.

#### Scenario: Jira event resolves by project key
- **WHEN** a canonical event arrives with `vendor: jira` and a project key declared by a target
- **THEN** the target resolves and the state lookup proceeds against that target's states

#### Scenario: Jira event for an undeclared project key is dropped
- **WHEN** a canonical event arrives with `vendor: jira` and a project key no target declares
- **THEN** the receiver drops the event and enqueues nothing

## MODIFIED Requirements

### Requirement: Declarative per-target mapping
The system SHALL be configured by a declarative mapping file listing targets, where each target declares `{vendor, project/repo coordinates, codeHost, codeRepo, states}` and each state entry declares `{native_state, action, runtime, on_success_state, on_failure_state}`. `codeHost` SHALL default to the target's `vendor` and `codeRepo` SHALL default to the target's `repo`, so a target whose tracker and code host are the same vendor declares neither. The mapping SHALL be the only source of "which state dispatches which agent action" — no state or command names hardcoded in receiver or agent code.

#### Scenario: v1-equivalent configuration expressible
- **WHEN** the mapping declares a GitHub target with `status:ready-for-proposal → {action: "/ds:propose", runtime: claude, on_failure_state: "status:blocked"}` and `status:ready-for-implementation → {action: "/ds:implement", runtime: claude, on_failure_state: "status:blocked"}`
- **THEN** the system reproduces the v1 behavior for that repo without code changes

#### Scenario: Existing single-vendor mappings need no edit
- **WHEN** a mapping file written before code-host coordinates existed is loaded, declaring only `vendor` and `repo` per target
- **THEN** it validates and behaves identically, with `codeHost` resolved to `vendor` and `codeRepo` to `repo`

#### Scenario: Split tracker and code host expressible
- **WHEN** a target declares `vendor: Jira` with a Jira project key, `codeHost: GitHub`, and a GitHub `codeRepo`
- **THEN** dispatch resolves states against Jira and the resulting message carries the GitHub repository as its code-host coordinate

#### Scenario: Unmapped state produces no dispatch
- **WHEN** a canonical event arrives whose `native_state` has no entry for that target
- **THEN** the receiver drops the event and enqueues nothing

#### Scenario: Unknown target produces no dispatch
- **WHEN** a canonical event arrives from a repo/project not declared as a target
- **THEN** the receiver drops the event and enqueues nothing
