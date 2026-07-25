# pod-observability — OpenTelemetry on every AI pod execution

## ADDED Requirements

### Requirement: Every execution is traced through its phases
An agent execution SHALL emit a trace whose spans cover the phases it already distinguishes: gate re-validation, credential minting, clone, the mapped command, and write-back. Each span SHALL record its outcome, so a trace answers "which phase is this run in, and how long has it taken" while the run is still in flight.

#### Scenario: A running execution is observable mid-run
- **WHEN** an execution is part-way through the mapped command
- **THEN** its trace shows the earlier phases as complete with their durations and the command phase as still open

#### Scenario: A failed phase is identifiable
- **WHEN** an execution fails during clone
- **THEN** the clone span records the failure, and the phases that never ran have no spans rather than empty successful ones

### Requirement: Every span carries project and item identity
Spans SHALL carry the project, the vendor coordinates, the item id, the action, and the runtime. Identity on the span rather than only on the trace root is what makes telemetry queryable per project without a join to something else.

#### Scenario: Consumption is attributable to a project
- **WHEN** telemetry is queried for a project over a period
- **THEN** that project's executions and their durations are answerable from spans alone, with no correlation against Azure resource inventory

#### Scenario: Identity survives a partial trace
- **WHEN** an execution fails before its first phase completes
- **THEN** whatever spans exist still identify the project and item, so a failing project is not invisible in attribution

### Requirement: Traces correlate with the terminal outcome event
The `dispatch.outcome` event SHALL carry the trace identifier of the execution it terminates. The event's existing fields and schema version are otherwise unchanged.

#### Scenario: History links to a run's trace
- **WHEN** an operator opens a finished run in the portal's history
- **THEN** that run's trace is reachable from it, because the outcome event records which trace it belongs to

#### Scenario: The outcome contract is not otherwise disturbed
- **WHEN** a consumer reads dispatch outcomes as it did before this change
- **THEN** every field it relied on is present and unchanged, and the added trace identifier is additive

### Requirement: Agent-runtime internals are not instrumented as spans
Spans SHALL describe the execution's own phases. The system MUST NOT parse the agent runtime's output to synthesize spans for its internal steps.

#### Scenario: Runtime output shape is not a dependency
- **WHEN** an agent runtime changes how it reports its internal progress
- **THEN** tracing is unaffected, because no span was derived from that output

### Requirement: Cost signals are presented as apportionment, not billing
Where telemetry is used to attribute cost, the system SHALL present it as a relative share and SHALL NOT present it as an amount owed. Compute is billed at the environment level and model usage comes from a provider bill this system does not see.

#### Scenario: Attribution is labelled for what it is
- **WHEN** per-project consumption is shown
- **THEN** it is labelled as apportionment with its basis stated, rather than as a currency total that invites being quoted as an invoice
