# agent-execution

## ADDED Requirements

### Requirement: Gate re-validation before any work
After dequeuing a work item, the entrypoint SHALL re-read the issue from GitHub and verify it still carries the gate label from the message. If the label has moved, the execution SHALL delete the message and exit 0 without cloning or invoking any agent.

#### Scenario: Stale gate label
- **WHEN** a work item references an issue whose `status:*` label is no longer the gate label in the message
- **THEN** the execution deletes the message and exits 0 with no clone and no LLM call

#### Scenario: Duplicate work item, second consumer
- **WHEN** two messages exist for the same gate event and one execution has already advanced the issue's label
- **THEN** the second execution observes the moved label and exits 0

### Requirement: Short-lived, narrowly scoped GitHub credentials
Each execution SHALL mint a GitHub App installation token scoped to the target repository at run time, using the App private key read from Key Vault via managed identity. The token SHALL never be persisted beyond the execution.

#### Scenario: Token minting
- **WHEN** an execution passes gate re-validation
- **THEN** it obtains an installation token scoped to the message's repository and uses it for clone and API calls

### Requirement: Headless invocation of the repo's own gate command
The entrypoint SHALL clone the target repository and run the command corresponding to the gate label — `/ds:propose` for `status:ready-for-proposal`, `/ds:implement` for `status:ready-for-implementation` — headlessly (non-interactive, e.g. `claude -p` or `opencode run`), with per-run caps (max turns, allowed tools) applied. All agent behavior (branching, PR creation, label flipping) comes from the repo's command definitions, not from this infrastructure.

#### Scenario: Propose gate
- **WHEN** the work item's gate label is `status:ready-for-proposal`
- **THEN** the agent runs the repo's `/ds:propose` for that issue, resulting in a draft PR and the label advancing per the command's own contract

#### Scenario: Implement gate
- **WHEN** the work item's gate label is `status:ready-for-implementation`
- **THEN** the agent runs the repo's `/ds:implement` for that issue on the existing PR branch

### Requirement: Runtime selection per work item
The entrypoint SHALL select the agent CLI (Claude Code or opencode) from the work item's `agent_runtime` field, defaulting to a configured value when absent. Both runtimes SHALL be invoked against the same repo-provided command files.

#### Scenario: Explicit runtime
- **WHEN** a work item specifies `agent_runtime: opencode`
- **THEN** the execution runs the gate command via `opencode run`

#### Scenario: Default runtime
- **WHEN** a work item omits `agent_runtime`
- **THEN** the execution uses the deployment's configured default

### Requirement: Failure flips the issue to blocked
If the agent command fails, times out, or the entrypoint errs after gate re-validation, the execution SHALL set the issue's `status:*` label to `status:blocked` (preserving the single-label invariant), post an issue comment with the failure context (reason and log tail, linking any partial branch/PR), and delete the message. Agent runs SHALL NOT be automatically retried; a human re-labels to retry.

#### Scenario: Agent command fails
- **WHEN** the gate command exits non-zero or the run times out
- **THEN** the issue carries `status:blocked` and only that `status:*` label, a comment explains the failure, and the queue message is deleted

#### Scenario: WIP-limit refusal from /ds:implement
- **WHEN** `/ds:implement` refuses because four issues are already `status:in-progress`
- **THEN** the issue keeps its gate label (not flipped to blocked), a comment notes the refusal, and the message is deleted
