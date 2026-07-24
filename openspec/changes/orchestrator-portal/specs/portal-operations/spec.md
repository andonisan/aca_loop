# portal-operations — audited operational verbs against live state

## ADDED Requirements

### Requirement: Manual dispatch re-validates the gate before enqueueing
The portal SHALL offer a manual-dispatch verb that resolves an item's current native state through the stack's bundled mapping (the same resolution logic as the receiver) and enqueues the canonical queue message only if the item currently carries the mapped state on the live vendor. If the item is not in a mapped state, the portal MUST refuse the dispatch and show the live state instead. The portal MUST NOT write vendor state in this change.

#### Scenario: Item in gate state dispatches
- **WHEN** an Operator triggers manual dispatch for an item whose live vendor state matches a mapped state
- **THEN** the portal enqueues the canonical message (vendor, project, repo, item id, state, action, runtime, on_failure_state resolved from the mapping) onto that stack's queue

#### Scenario: Item not in gate state is refused
- **WHEN** an Operator triggers manual dispatch for an item whose live vendor state is not mapped for that target
- **THEN** no message is enqueued and the portal shows the item's actual live state

### Requirement: Cancel stops a specific execution with documented retry semantics
The portal SHALL offer a cancel verb that stops one selected running ACA job execution via the management API. The UI MUST state the semantics: the already-dequeued message may reappear after visibility timeout and retry once unless the item leaves the gate state; the poison guard bounds redelivery.

#### Scenario: Running execution is stopped
- **WHEN** an Operator cancels a running execution listed in a stack's executions view
- **THEN** the portal issues the stop call for exactly that execution and reflects the terminal status on refresh

#### Scenario: Cancel of a finished execution is a no-op with feedback
- **WHEN** an Operator cancels an execution that has already completed between render and click
- **THEN** the portal reports that the execution was already terminal and performs no state change

### Requirement: Every verb is audited with the operator's identity
Each verb invocation SHALL emit a structured audit event to the portal's own Log Analytics workspace containing: actor (Entra object id and UPN), verb, stack id, target (item coordinates or execution id), timestamp, and outcome (performed / refused / failed with reason). Audit emission failures MUST NOT silently drop: if the audit event cannot be written, the verb result surface MUST flag it.

#### Scenario: Successful verb writes an audit event
- **WHEN** an Operator performs any verb successfully
- **THEN** an audit event with the actor identity, verb, target, and outcome `performed` is written

#### Scenario: Refused verb is also audited
- **WHEN** a manual dispatch is refused because the item is not in a mapped state
- **THEN** an audit event with outcome `refused` and the reason is written
