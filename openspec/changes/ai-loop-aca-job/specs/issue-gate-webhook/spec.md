# issue-gate-webhook

## ADDED Requirements

### Requirement: Machine-gate label events are enqueued as agent work items
The webhook receiver SHALL handle GitHub `issues` events and, when `action == "labeled"` and the applied label is one of the machine-gate labels (`status:ready-for-proposal`, `status:ready-for-implementation`), enqueue a work item to the `ai-jobs` queue containing at minimum: repository full name, issue number, gate label, and agent runtime.

#### Scenario: Gate label applied
- **WHEN** an `issues` webhook arrives with `action == "labeled"` and `label.name == "status:ready-for-proposal"`
- **THEN** a message with `{repo, issue_number, gate_label, agent_runtime}` is enqueued to the `ai-jobs` queue

#### Scenario: Non-gate label applied
- **WHEN** an `issues` webhook arrives with `action == "labeled"` and a label that is not a machine-gate label (e.g. `status:proposal-review`, `bug`)
- **THEN** the receiver acknowledges the delivery with a 2xx and enqueues nothing

#### Scenario: Non-labeled issue action
- **WHEN** an `issues` webhook arrives with any `action` other than `labeled` (e.g. `opened`, `unlabeled`, `closed`)
- **THEN** the receiver acknowledges the delivery with a 2xx and enqueues nothing

### Requirement: Deliveries are authenticated before processing
The receiver SHALL verify the HMAC signature of every `issues` delivery against the AI-loop GitHub App's webhook secret and SHALL reject deliveries with missing or invalid signatures without enqueueing.

#### Scenario: Invalid signature
- **WHEN** an `issues` webhook arrives whose `X-Hub-Signature-256` does not match the configured secret
- **THEN** the receiver responds with a 4xx and enqueues nothing

### Requirement: Self-triggering is prevented at the receiver
The receiver SHALL drop `labeled` events whose sender is the AI-loop automation's own bot identity, even when the label is a machine-gate label.

#### Scenario: Bot-applied gate label
- **WHEN** an `issues` webhook arrives with a machine-gate label and `sender.login` equal to the AI-loop App's bot login
- **THEN** the receiver acknowledges with a 2xx and enqueues nothing

### Requirement: Duplicate deliveries are harmless
The receiver SHALL treat webhook deliveries as at-least-once: a redelivered gate-label event MAY produce a second queue message, and the system SHALL rely on execution-time gate re-validation (see `agent-execution`) to neutralize duplicates rather than receiver-side state.

#### Scenario: GitHub redelivers the same event
- **WHEN** the same `labeled` delivery is received twice
- **THEN** up to two messages exist on the queue and at most one agent run acts on the issue (the other exits cleanly at gate re-validation)
