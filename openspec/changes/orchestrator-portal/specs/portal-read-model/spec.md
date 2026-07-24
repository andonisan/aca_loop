# portal-read-model — cross-stack aggregation without portal-owned persistence

## ADDED Requirements

### Requirement: Stack registry is a validated, fail-fast configuration file
The portal SHALL load its registry of client stacks (`stacks.yaml`: stack id, display name, Azure coordinates — subscription, resource group, queue endpoint and name, ACA job name, Log Analytics workspace id —, vendor coordinates, and the bundled mapping file reference) at startup, validate it structurally, and MUST refuse to serve traffic (unhealthy, process fails to start) if the registry is missing, unparsable, or invalid. The registry SHALL be the only source of stack membership — no auto-discovery.

#### Scenario: Valid registry starts serving
- **WHEN** the portal starts with a structurally valid `stacks.yaml` referencing at least one stack
- **THEN** the portal becomes healthy and every registered stack appears in the UI

#### Scenario: Invalid registry fails fast
- **WHEN** the portal starts with a `stacks.yaml` that is missing a required field (e.g. a stack without a Log Analytics workspace id) or references a mapping file that does not exist in the deployment
- **THEN** startup fails before the portal serves any request, and the error lists every violation

### Requirement: Live dispatch state per stack from Azure sources
For each registered stack the portal SHALL display the current queue depth (approximate message count) and the AI-pod executions (running and recent ACA job executions with status, start time, and duration), read via the portal's managed identity, with per-source cache TTLs so that repeated page views do not translate 1:1 into Azure API calls.

#### Scenario: Queue and executions view
- **WHEN** an authenticated user opens a registered stack's dashboard
- **THEN** the portal shows that stack's queue depth and its running and recent job executions, each value no staler than its configured TTL

#### Scenario: Source outage degrades, does not break
- **WHEN** one Azure source (e.g. the ACA API) errors or times out for a stack
- **THEN** the portal renders the remaining panels for that stack and marks the failing panel as unavailable with the error summarized, without failing the whole page

### Requirement: Gated work items via read-scoped vendor credentials
For each registered stack the portal SHALL list the work items currently carrying a mapped gate state, read through the existing vendor adapters using dedicated read-scoped credentials (GitHub: read-only installation token; GitLab: `read_api` token; Azure DevOps: read-scoped PAT or service principal). The portal MUST NOT read or use the agent's write-capable credentials.

#### Scenario: Gate view lists mapped items only
- **WHEN** a user opens the gate view for a stack whose mapping declares states A and B
- **THEN** the portal lists the items currently in state A or B on the target vendor, and items in unmapped states do not appear

#### Scenario: Write-capable secrets are never requested
- **WHEN** the portal authenticates against any vendor for any read
- **THEN** the credential used is the stack's dedicated read-scoped secret, and no access to the agent's write-capable Key Vault secrets occurs

### Requirement: Run history from Log Analytics, no portal datastore
The portal SHALL answer run-history questions (recent runs per stack with outcome ∈ {succeeded, failed, stale-gate, poison, wip-refused}, duration, and failure log-tail pointer; success rate over a selectable window) by querying the registered stacks' Log Analytics workspaces for the normalized dispatch-outcome events. The portal MUST NOT persist run data in any store of its own; history depth SHALL equal workspace retention and be labeled as such in the UI.

#### Scenario: History for one stack
- **WHEN** a user opens a stack's history view with a 7-day window
- **THEN** the portal shows that stack's dispatch outcomes from its Log Analytics workspace for the last 7 days, including duration and outcome per run

#### Scenario: History source of truth is the workspace
- **WHEN** a dispatch-outcome event exists in a stack's workspace but the portal has never seen it before (e.g. portal was down when it happened)
- **THEN** the event still appears in the history view, because no portal-side ingestion or storage is involved

### Requirement: Normalized dispatch lifecycle events emitted by the stacks
The orchestrator stacks SHALL emit two normalized structured events consumable by the portal: an enqueue event at dispatch time (vendor, project/repo, item id, action, runtime) and a dispatch-outcome event at run end (same coordinates plus outcome ∈ {succeeded, failed, stale-gate, poison, wip-refused}, duration, and a log-tail pointer). Both SHALL be defined as `[LoggerMessage]` contracts with stable event ids and a schema-version field. Exactly one outcome event SHALL be emitted per execution that dequeued a message — every terminal path maps to one of the outcome values, so no run is invisible to the read model.

#### Scenario: Outcome event on failure
- **WHEN** an agent run fails and the failure contract executes
- **THEN** a dispatch-outcome event with outcome `failed`, the run duration, and a log-tail pointer is exported to the stack's Log Analytics workspace

#### Scenario: Outcome event on stale gate
- **WHEN** the agent's gate re-validation finds the item no longer in the mapped state and exits successfully without running a command
- **THEN** a dispatch-outcome event with outcome `stale-gate` is exported

#### Scenario: Outcome event on a WIP-limit refusal
- **WHEN** the mapped command runs and declines because the target repo's WIP limit is reached, leaving the gate state in place
- **THEN** a dispatch-outcome event with outcome `wip-refused` is exported, so the run is neither invisible nor counted as a failure

#### Scenario: No outcome event without a run
- **WHEN** an agent replica wakes to an empty queue and dequeues nothing
- **THEN** no dispatch-outcome event is emitted

### Requirement: Enqueue events follow the handoff they report
The enqueue event SHALL be emitted only after the canonical message has been accepted by the queue, so that a row in the read model always means work was really handed off.

#### Scenario: Failing enqueue emits nothing
- **WHEN** the queue is unavailable and the enqueue call throws
- **THEN** no enqueue event is emitted for that webhook
