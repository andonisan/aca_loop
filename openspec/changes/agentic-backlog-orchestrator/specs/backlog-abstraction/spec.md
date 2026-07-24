# backlog-abstraction — canonical work-item model and vendor adapters

## ADDED Requirements

### Requirement: Canonical work-item event normalization
Each vendor adapter (GitHub, Azure DevOps, GitLab) SHALL normalize its native state-change notification into a canonical event `{vendor, project, repo, item_id, native_state, actor}`. Components downstream of an adapter MUST NOT contain vendor-specific logic or parse vendor payloads.

#### Scenario: GitHub issue labeled
- **WHEN** a GitHub `issues` webhook with `action == "labeled"` arrives
- **THEN** the GitHub adapter emits a canonical event with `vendor: github`, the repo full name, the issue number as `item_id`, and the applied label name as `native_state`

#### Scenario: Azure DevOps work item state changed
- **WHEN** an Azure DevOps `workitem.updated` service hook arrives with a change to the `System.State` field
- **THEN** the Azure DevOps adapter emits a canonical event with `vendor: azure-devops`, the project and repo coordinates, the work item id as `item_id`, and the new `System.State` value as `native_state`

#### Scenario: GitLab issue label event
- **WHEN** a GitLab issue webhook arrives whose label changes include an added label
- **THEN** the GitLab adapter emits a canonical event with `vendor: gitlab`, the project path, the issue iid as `item_id`, and the added label as `native_state`

#### Scenario: Non-state-change events are dropped
- **WHEN** a webhook arrives that does not represent a state change (e.g. GitHub `action == "opened"`, an Azure DevOps update not touching `System.State`)
- **THEN** the adapter drops the event without emitting anything

### Requirement: Webhook authenticity verification per vendor
Each adapter SHALL verify the authenticity of incoming webhooks using the vendor's native mechanism (GitHub: HMAC-SHA256 signature; GitLab: secret token header; Azure DevOps: configured shared secret/basic auth) before any parsing. Requests failing verification MUST be rejected with no side effects.

#### Scenario: Invalid signature rejected
- **WHEN** a webhook arrives whose signature/token does not match the configured secret for that vendor
- **THEN** the receiver responds with an error status and enqueues nothing

### Requirement: Self-triggering prevention
Each adapter SHALL drop events whose `actor` is the orchestrator's own automation identity for that vendor (bot login, service account, or PAT owner), so agent-performed state transitions never re-dispatch agents.

#### Scenario: Agent's own write-back arrives as an event
- **WHEN** the agent moves an item to its `on_success_state` and the vendor delivers the resulting webhook
- **THEN** the adapter identifies the actor as the automation identity and drops the event

### Requirement: State write-back through the adapter
The system SHALL write state transitions back to the origin vendor via its adapter using the vendor's native mechanism: GitHub and GitLab swap lifecycle labels preserving a single-lifecycle-label invariant; Azure DevOps sets the work item `System.State` (or a configured field/value when the process template lacks the target state).

#### Scenario: Success transition on GitHub
- **WHEN** an agent run for a GitHub item completes successfully with `on_success_state` configured
- **THEN** the adapter removes the current lifecycle label and adds the configured success label in one logical operation

#### Scenario: Failure transition with context
- **WHEN** an agent run fails
- **THEN** the adapter moves the item to the configured `on_failure_state` and posts a comment/discussion entry containing the failure context (log tail)

### Requirement: Live item state read
Each adapter SHALL expose a read of the item's current lifecycle state from the vendor's live API (not the webhook payload), for use in gate re-validation.

#### Scenario: State read after dispatch delay
- **WHEN** the agent asks the adapter for the current state of an item that was re-labeled after the event was enqueued
- **THEN** the adapter returns the state currently on the vendor, not the state carried by the queue message
