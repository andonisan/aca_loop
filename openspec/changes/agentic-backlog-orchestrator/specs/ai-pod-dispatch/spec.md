# ai-pod-dispatch — vendor-agnostic queue and ACA Job execution

## ADDED Requirements

### Requirement: Canonical queue message
The receiver SHALL enqueue exactly one message per dispatchable event with the canonical schema `{vendor, project, repo, item_id, state, action, runtime}`. The message SHALL carry everything the agent needs to select adapter, credentials, command, and runtime without consulting vendor-specific receiver logic.

#### Scenario: Mapped event enqueued
- **WHEN** a verified canonical event matches a mapping entry
- **THEN** one message with the canonical schema is enqueued on the `ai-jobs` queue

### Requirement: Scale-to-zero job execution
Agent executions SHALL run as Azure Container Apps Job executions ("AI pods") scaled 0..N from queue depth via KEDA. Each execution SHALL dequeue at most one message; an execution that finds the queue empty SHALL exit 0 (overscale is expected, not an error).

#### Scenario: One item, one pod
- **WHEN** three mapped events are enqueued
- **THEN** up to three job executions run, each processing exactly one message

#### Scenario: Empty dequeue
- **WHEN** a job execution starts after the queue has been drained by concurrent executions
- **THEN** it exits 0 without error

### Requirement: Message lifecycle semantics
Messages SHALL become invisible during processing (visibility timeout sized to the execution ceiling), be deleted only on execution completion (success, clean stale-gate exit, or handled failure with write-back), and be quarantined by a poison-message guard after a bounded number of deliveries.

#### Scenario: Crash before completion retries
- **WHEN** an execution crashes before completing the failure contract
- **THEN** the message reappears after the visibility timeout and is retried, up to the poison threshold

#### Scenario: Poison message quarantined
- **WHEN** a message exceeds its delivery-count threshold
- **THEN** it is removed from the work queue without further agent executions

### Requirement: Bounded execution
Each execution SHALL have a hard platform timeout, and the entrypoint SHALL run a watchdog that fires before the platform ceiling so the failure contract (write-back to `on_failure_state` + context comment) executes before the replica is killed. Agent LLM spend per run SHALL be bounded by a configurable turn cap.

#### Scenario: Watchdog preserves the failure contract
- **WHEN** an agent run reaches the watchdog deadline
- **THEN** the run is aborted, the item is moved to its `on_failure_state` with a context comment, and the message is deleted before the platform timeout
