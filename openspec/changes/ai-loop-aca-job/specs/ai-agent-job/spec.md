# ai-agent-job

## ADDED Requirements

### Requirement: Queue-driven scale-to-zero execution
The `ai-agent` Container Apps Job SHALL use an event trigger with a KEDA `azure-queue` scale rule on the `ai-jobs` queue, scaling from 0 to a configurable maximum, targeting one job execution per queued message, and authenticating the scaler via managed identity (no connection strings).

#### Scenario: Message arrives while scaled to zero
- **WHEN** a message is enqueued to `ai-jobs` and no executions are running
- **THEN** KEDA starts a job execution within the configured polling interval

#### Scenario: Idle queue
- **WHEN** the queue is empty
- **THEN** no executions run and no compute is billed

### Requirement: One message per execution with clean overscale behavior
Each execution SHALL dequeue at most one message. If the queue is empty at dequeue time (KEDA overscale), the execution SHALL exit 0 without contacting GitHub or any LLM provider.

#### Scenario: Overscaled execution finds empty queue
- **WHEN** an execution starts and the dequeue returns no message
- **THEN** it exits 0 immediately, with no GitHub or LLM API calls in its logs

### Requirement: Message lifecycle guarantees
The job SHALL delete a message only after the execution completes its contract (success, clean no-op, or handled failure per `agent-execution`). Messages that exceed a maximum dequeue count SHALL be treated as poison and removed with a logged error, so a crashing entrypoint cannot loop forever.

#### Scenario: Execution killed mid-run before the failure trap arms
- **WHEN** an execution dies without deleting its message
- **THEN** the message reappears after the visibility timeout and is retried, up to the poison threshold

#### Scenario: Poison message
- **WHEN** a message reaches the maximum dequeue count
- **THEN** it is removed from the queue and the failure is logged

### Requirement: Timeout sized for agent work
The job's `replica_timeout_seconds` SHALL be configured independently of the CI runner job and sized for long agent sessions (default on the order of 3600s), and the entrypoint SHALL receive the value so it can arm its failure trap before the platform kills the replica.

#### Scenario: Agent session exceeds the timeout
- **WHEN** an execution approaches `replica_timeout_seconds`
- **THEN** the entrypoint's trap fires first, executing the failure contract (issue → `status:blocked` + comment) before the platform terminates the replica
