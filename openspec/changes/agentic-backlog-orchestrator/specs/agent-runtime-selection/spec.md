# agent-runtime-selection — per-dispatch runtime on one multi-runtime image

## ADDED Requirements

### Requirement: Single multi-runtime agent image
The agent container image SHALL bundle Claude Code, opencode, and `gh` together, so runtime choice is a per-message decision and never requires a different image or Job definition.

#### Scenario: Runtimes coexist
- **WHEN** two queue messages for the same target specify `runtime: claude` and `runtime: opencode`
- **THEN** both executions run from the same image, each invoking its selected runtime

### Requirement: Runtime selected from the mapping
The entrypoint SHALL select the runtime named in the queue message (which the receiver copied from the mapping's state entry). An unknown runtime value SHALL be treated as a failed run (failure contract applies), not silently defaulted.

#### Scenario: Configured runtime honored
- **WHEN** a mapping entry declares `runtime: opencode` for a state
- **THEN** dispatches for that state execute the action via opencode headlessly

#### Scenario: Unknown runtime fails safely
- **WHEN** a message carries a runtime value the image does not provide
- **THEN** the item is moved to its `on_failure_state` with an explanatory comment and no command is executed

### Requirement: Repo-owned, runtime-portable commands
The action named in a mapping entry SHALL resolve to a command owned by the target repository (e.g. `.claude/commands` / `.opencode/commands`), invoked headlessly by the selected runtime. The orchestrator MUST NOT embed target-specific agent logic beyond the entrypoint contract.

#### Scenario: Same action across runtimes
- **WHEN** a target repo defines a command available to both Claude Code and opencode
- **THEN** the same mapping `action` executes correctly under either configured runtime

### Requirement: Per-vendor credentials injected at execution time
The entrypoint SHALL obtain the credentials for the message's vendor at execution time (GitHub: short-lived App installation token minted per run; GitLab and Azure DevOps: scoped tokens read from Key Vault via managed identity) and expose them to the runtime only for the duration of the run. No vendor credential SHALL be baked into the image or Job definition.

#### Scenario: GitHub token minted per run
- **WHEN** an execution processes a `vendor: github` message
- **THEN** it mints a fresh installation token scoped to that repo and uses it for clone and API write-back
