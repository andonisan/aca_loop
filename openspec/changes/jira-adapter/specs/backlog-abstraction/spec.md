# backlog-abstraction — tracker/code-host separation

## ADDED Requirements

### Requirement: Tracker and code host are separate contracts
The vendor seam SHALL expose two contracts: a backlog-tracker contract (webhook verification, event parsing, automation-actor identification, live state read, state write-back, comment posting) and a code-host contract (minting per-run clone credentials and contributing the vendor CLI token environment). A vendor module SHALL implement whichever contracts it can honor. A tracker-only vendor MUST be expressible without any code-host implementation, and the absence MUST be a compile-time fact rather than a runtime failure.

#### Scenario: Tracker-only vendor needs no code-host implementation
- **WHEN** a vendor module implements only the backlog-tracker contract
- **THEN** the solution compiles, the module self-registers as a tracker, and nothing in the system can ask it for clone credentials

#### Scenario: Dual-role vendor implements both
- **WHEN** a vendor is both a tracker and a git host
- **THEN** its module implements both contracts and behaves identically to before this separation

### Requirement: Code-host coordinates on the canonical model
The canonical event and canonical queue message SHALL carry code-host coordinates (the code host vendor and its repository coordinate) distinct from the tracker coordinates. Execution SHALL resolve the tracker contract for gate re-validation and write-back, and the code-host contract for clone credentials and CLI token environment, from those separate coordinates.

#### Scenario: Tracker and code host differ
- **WHEN** a message carries a Jira tracker coordinate and a GitHub code-host coordinate
- **THEN** gate re-validation and failure write-back go to Jira, and the clone credentials and CLI token come from GitHub

#### Scenario: Tracker and code host are the same vendor
- **WHEN** a message carries the same vendor as both tracker and code host
- **THEN** both resolutions select that vendor's module and behavior is unchanged from before the separation

### Requirement: Every code host contributes its CLI token environment
Each code-host implementation SHALL contribute the environment variables its vendor's CLI requires for authentication, so a run never receives another vendor's token and never lacks its own. This contribution SHALL be part of the code-host contract rather than a branch inside execution.

#### Scenario: Azure DevOps run receives its CLI token
- **WHEN** an execution's code host is Azure DevOps
- **THEN** the run's environment carries the Azure DevOps CLI authentication variable in addition to the git credentials

#### Scenario: Tokens are not cross-contaminated
- **WHEN** an execution's code host is GitLab
- **THEN** the run's environment carries the GitLab token and no GitHub token

## MODIFIED Requirements

### Requirement: Canonical work-item event normalization
Each vendor adapter (GitHub, Azure DevOps, GitLab, Jira) SHALL normalize its native state-change notification into a canonical event `{vendor, project, repo, item_id, native_state, actor, code_host, code_repo}`. Components downstream of an adapter MUST NOT contain vendor-specific logic or parse vendor payloads. For a tracker that is not repo-anchored, the tracker's own coordinates SHALL identify the item and the code-host coordinates SHALL identify the repository.

#### Scenario: GitHub issue labeled
- **WHEN** a GitHub `issues` webhook with `action == "labeled"` arrives
- **THEN** the GitHub adapter emits a canonical event with `vendor: github`, the repo full name, the issue number as `item_id`, and the applied label name as `native_state`

#### Scenario: Azure DevOps work item state changed
- **WHEN** an Azure DevOps `workitem.updated` service hook arrives with a change to the `System.State` field
- **THEN** the Azure DevOps adapter emits a canonical event with `vendor: azure-devops`, the project and repo coordinates, the work item id as `item_id`, and the new `System.State` value as `native_state`

#### Scenario: GitLab issue label event
- **WHEN** a GitLab issue webhook arrives whose label changes include an added label
- **THEN** the GitLab adapter emits a canonical event with `vendor: gitlab`, the project path, the issue iid as `item_id`, and the added label as `native_state`

#### Scenario: Jira issue status changed
- **WHEN** a `jira:issue_updated` webhook arrives whose changelog contains a `status` change
- **THEN** the Jira adapter emits a canonical event with `vendor: jira`, the Jira project key as the project coordinate, the issue key as `item_id`, the new status name as `native_state`, and the target's configured code-host coordinates

#### Scenario: Non-state-change events are dropped
- **WHEN** a webhook arrives that does not represent a state change (e.g. GitHub `action == "opened"`, an Azure DevOps update not touching `System.State`, a Jira changelog not touching `status`)
- **THEN** the adapter drops the event without emitting anything

### Requirement: Webhook authenticity verification per vendor
Each adapter SHALL verify the authenticity of incoming webhooks using the vendor's native mechanism (GitHub: HMAC-SHA256 signature; GitLab: secret token header; Azure DevOps: configured shared secret/basic auth; Jira: HMAC signature in `X-Hub-Signature`) before any parsing. Requests failing verification MUST be rejected with no side effects. Where a vendor treats its webhook secret as optional, the adapter MUST still require it and reject unsigned requests.

#### Scenario: Invalid signature rejected
- **WHEN** a webhook arrives whose signature/token does not match the configured secret for that vendor
- **THEN** the receiver responds with an error status and enqueues nothing

#### Scenario: Unsigned request rejected where the vendor allows no secret
- **WHEN** a webhook arrives with no signature material from a vendor whose registration makes the secret optional
- **THEN** the receiver rejects the request rather than skipping verification

### Requirement: Self-triggering prevention
Each adapter SHALL drop events whose `actor` is the orchestrator's own automation identity for that vendor (bot login, service account, PAT owner, or account identifier), so agent-performed state transitions never re-dispatch agents.

#### Scenario: Agent's own write-back arrives as an event
- **WHEN** the agent moves an item to its `on_success_state` and the vendor delivers the resulting webhook
- **THEN** the adapter identifies the actor as the automation identity and drops the event

### Requirement: State write-back through the adapter
The system SHALL write state transitions back to the origin vendor via its adapter using the vendor's native mechanism: GitHub and GitLab swap lifecycle labels preserving a single-lifecycle-label invariant; Azure DevOps sets the work item `System.State` (or a configured field/value when the process template lacks the target state); Jira executes the workflow transition whose target status matches the requested state, discovered from the transitions available at the item's current status. Where a vendor's write-back can be unavailable for workflow reasons, the failure contract's comment and outcome recording MUST still complete.

#### Scenario: Success transition on GitHub
- **WHEN** an agent run for a GitHub item completes successfully with `on_success_state` configured
- **THEN** the adapter removes the current lifecycle label and adds the configured success label in one logical operation

#### Scenario: Failure transition with context
- **WHEN** an agent run fails
- **THEN** the adapter moves the item to the configured `on_failure_state` and posts a comment/discussion entry containing the failure context (log tail)

#### Scenario: Failure transition unavailable on a workflow-constrained vendor
- **WHEN** an agent run fails and the configured `on_failure_state` is unreachable from the item's current status
- **THEN** the adapter posts the failure-context comment, the execution records a failed outcome, and the item's state is left unchanged

### Requirement: Live item state read
Each adapter SHALL expose a read of the item's current lifecycle state from the vendor's live API (not the webhook payload), for use in gate re-validation.

#### Scenario: State read after dispatch delay
- **WHEN** the agent asks the adapter for the current state of an item that was re-labeled after the event was enqueued
- **THEN** the adapter returns the state currently on the vendor, not the state carried by the queue message
