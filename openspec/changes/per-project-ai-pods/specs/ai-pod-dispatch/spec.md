# ai-pod-dispatch — dispatch scoped to a project

## ADDED Requirements

### Requirement: A message is enqueued to its project's own queue
Dispatch SHALL resolve the target project from the canonical event and enqueue to that project's queue. A message MUST NOT be visible to any other project's job.

The dispatch invariants are unchanged by this: gate re-validation is still the lock, one message still yields one execution, and the failure contract still applies. Only the queue changes.

#### Scenario: Projects do not see each other's work
- **WHEN** a message is enqueued for one project
- **THEN** only that project's job scales for it, and no other project's job dequeues it

#### Scenario: A busy project does not starve another
- **WHEN** one project has more gated items than its concurrency cap allows
- **THEN** other projects continue dispatching at their own capacity, because their capacity is their own

### Requirement: Concurrency is capped per project
The maximum concurrent executions SHALL be a property of the project, not of a client or of the environment.

#### Scenario: A project's cap bounds only itself
- **WHEN** a project reaches its concurrency cap
- **THEN** further messages for that project wait, and no other project's dispatch is affected

### Requirement: Dispatch to a project with no provisioned path fails visibly
When a project's queue does not exist, dispatch SHALL fail loudly and the project SHALL be reported as not ready. It MUST NOT silently discard the event.

A project whose row exists but whose infrastructure does not is the failure mode this split introduces, and it produces no error of its own: items would simply sit in a gate state forever.

#### Scenario: Missing queue is surfaced, not swallowed
- **WHEN** an event resolves to a project whose queue does not exist
- **THEN** the failure is recorded and the project is reported as not ready, rather than the event being accepted and dropped

### Requirement: Deleting a project does not strand a running execution
Removing a project SHALL account for an execution already in flight, either by waiting for it to reach a terminal state or by recording that it was terminated. An execution MUST NOT disappear without an outcome.

Every terminal path emits exactly one outcome event, and the read model depends on it. "The job was deleted underneath it" must not become the one path that emits nothing.

#### Scenario: In-flight execution still produces an outcome
- **WHEN** a project is deleted while one of its executions is running
- **THEN** that execution reaches a terminal state with an outcome recorded, rather than vanishing from the read model
