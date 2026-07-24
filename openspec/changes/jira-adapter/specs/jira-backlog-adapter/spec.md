# jira-backlog-adapter — Jira as a tracker-only backlog vendor

## ADDED Requirements

### Requirement: Jira webhook authenticity via mandatory HMAC signature
The Jira adapter SHALL verify every incoming webhook by computing an HMAC of the raw request body using the configured shared secret and comparing it, in fixed time, against the `X-Hub-Signature` header. The header value SHALL be parsed in its `method=signature` form and the method rejected if it is not a supported hash algorithm. A configured secret is mandatory: the adapter MUST reject requests carrying no `X-Hub-Signature` header rather than treating an absent signature as "verification not configured". Verification SHALL happen before any payload parsing.

#### Scenario: Valid signature accepted
- **WHEN** a Jira webhook arrives whose `X-Hub-Signature` is `sha256=<correct HMAC of the raw body>` for the configured secret
- **THEN** the adapter accepts the request and proceeds to parsing

#### Scenario: Invalid signature rejected
- **WHEN** a Jira webhook arrives whose `X-Hub-Signature` does not match the computed HMAC
- **THEN** the receiver responds with an error status, parses nothing, and enqueues nothing

#### Scenario: Missing signature header rejected
- **WHEN** a Jira webhook arrives with no `X-Hub-Signature` header
- **THEN** the receiver rejects the request, regardless of whether a secret is configured

#### Scenario: Unsupported HMAC method rejected
- **WHEN** a Jira webhook arrives whose `X-Hub-Signature` declares a method the adapter does not support
- **THEN** the receiver rejects the request rather than attempting verification with a default algorithm

### Requirement: Status-change normalization from the issue changelog
The Jira adapter SHALL emit a canonical event only for `jira:issue_updated` payloads whose `changelog.items[]` contains an entry with `field == "status"`, taking that entry's `toString` as the `native_state`. Payloads for other event types, and `jira:issue_updated` payloads whose changelog does not touch `status`, SHALL be dropped without emitting anything.

#### Scenario: Status transition emits a canonical event
- **WHEN** a `jira:issue_updated` webhook arrives whose `changelog.items[]` contains `{field: "status", toString: "Ready for Proposal"}`
- **THEN** the adapter emits a canonical event with `vendor: jira`, the Jira project key as the project coordinate, the issue key as `item_id`, and `Ready for Proposal` as `native_state`

#### Scenario: Non-status field change is dropped
- **WHEN** a `jira:issue_updated` webhook arrives whose `changelog.items[]` contains only a `summary` or `assignee` change
- **THEN** the adapter drops the event without emitting anything

#### Scenario: Non-update event is dropped
- **WHEN** a webhook arrives for an event type other than `jira:issue_updated` (for example `jira:issue_created`)
- **THEN** the adapter drops the event without emitting anything

#### Scenario: Update with no changelog is dropped
- **WHEN** a `jira:issue_updated` webhook arrives with no `changelog` object
- **THEN** the adapter drops the event without emitting anything

### Requirement: Issue key as the canonical item identifier
The Jira adapter SHALL use the Jira issue key (for example `VY-1234`) as `item_id`, never the numeric issue id. All adapter operations SHALL address the issue by that key.

#### Scenario: Issue key travels end to end
- **WHEN** a canonical event is emitted for a Jira issue whose key is `DSC-42` and numeric id is `10731`
- **THEN** `item_id` is `DSC-42`, and the same value appears on the queue message and in the run's lifecycle records

### Requirement: Jira automation-actor self-filtering
The Jira adapter SHALL identify its own automation identity from the webhook payload's actor and drop those events, so the adapter's own transitions and comments never re-dispatch an agent. Identity comparison SHALL use the account identifier rather than a display name.

#### Scenario: Adapter's own transition arrives as an event
- **WHEN** the adapter transitions an issue and Jira delivers the resulting `jira:issue_updated` webhook whose actor is the orchestrator's Jira account
- **THEN** the adapter identifies the actor as its automation identity and drops the event

#### Scenario: Human transition is not filtered
- **WHEN** a `jira:issue_updated` status-change webhook arrives whose actor is any other account
- **THEN** the adapter emits the canonical event normally

### Requirement: Live status read for gate re-validation
The Jira adapter SHALL read the item's current status from Jira's live REST API, addressed by issue key, for use in gate re-validation — never from the webhook payload.

#### Scenario: Status read reflects the live value
- **WHEN** the agent asks the adapter for the current state of an issue that was transitioned after its event was enqueued
- **THEN** the adapter returns the status currently on the issue in Jira, not the status carried by the queue message

### Requirement: Status write-back through workflow transition discovery
Because Jira does not permit setting status as a field, the Jira adapter SHALL write a state transition by requesting the transitions available from the issue's current status, selecting the transition whose target status matches the requested state, and executing that transition by its id. The adapter MUST NOT attempt to set status through an issue-edit operation.

#### Scenario: Reachable target status is transitioned
- **WHEN** the adapter is asked to move an issue to `Blocked` and a transition to `Blocked` is available from the issue's current status
- **THEN** the adapter executes that transition and the issue's status becomes `Blocked`

#### Scenario: Issue-edit path is never used for status
- **WHEN** the adapter performs any state write-back
- **THEN** the request is a transition execution, and no issue-edit request carrying a status field is sent

### Requirement: Failure contract survives an unreachable failure status
When a run fails and the configured failure state is not reachable by any transition from the item's current status, the adapter SHALL still post the failure-context comment, and the execution SHALL still record its outcome through the normal failure path. An unreachable failure transition MUST NOT suppress the comment, mask the original failure, or cause the run to report success.

#### Scenario: Failure comment posted despite unreachable transition
- **WHEN** a run fails, the configured `onFailureState` is `Blocked`, and no transition to `Blocked` exists from the issue's current status
- **THEN** the adapter posts the failure-context comment on the issue, the run records a failed outcome, and the issue's status is left unchanged

#### Scenario: Original failure is the reported cause
- **WHEN** both the agent run and the subsequent failure transition fail
- **THEN** the recorded outcome describes the original run failure, with the transition failure reported as additional context rather than replacing it

### Requirement: Deployment type selects API version, comment format, and auth together
The Jira adapter SHALL take a single deployment-type setting distinguishing Jira Cloud from Jira Data Center, and SHALL derive from it the REST API version, the comment body format, and the authentication scheme as one coupled choice. These MUST NOT be independently configurable, because combinations such as Data Center with Atlassian Document Format do not exist.

#### Scenario: Cloud deployment uses v3 and Atlassian Document Format
- **WHEN** the adapter is configured for Jira Cloud and posts a comment
- **THEN** it calls the v3 comment endpoint with a valid Atlassian Document Format body and authenticates with the account email and API token

#### Scenario: Data Center deployment uses v2 and wiki markup
- **WHEN** the adapter is configured for Jira Data Center and posts a comment
- **THEN** it calls the v2 comment endpoint with a plain-text/wiki-markup body and authenticates with a personal access token

#### Scenario: Failure context posted successfully on either deployment
- **WHEN** the adapter posts a failure-context comment containing a multi-line log excerpt
- **THEN** the request body is valid for the configured deployment's comment format and Jira accepts it

### Requirement: Resilient outbound calls that never swallow a run
Every Jira REST call the adapter makes SHALL carry an explicit timeout and honor cancellation. On a rate-limit response the adapter SHALL respect the `Retry-After` header, retry with exponential backoff plus jitter, and bound its total attempts. Exhausting retries MUST NOT prevent the execution from recording an outcome.

#### Scenario: Rate-limited call is retried with backoff
- **WHEN** a Jira REST call returns a rate-limit response carrying `Retry-After`
- **THEN** the adapter waits at least the indicated interval, retries with exponential backoff and jitter, and stops after its bounded attempt count

#### Scenario: Exhausted retries during the failure path still record an outcome
- **WHEN** a run has already failed and the failure comment or failure transition exhausts its retries against a rate-limited Jira
- **THEN** the execution still records a failed outcome describing the original run failure, and the run is not left without a recorded result

#### Scenario: Calls are bounded in time
- **WHEN** Jira does not respond to a request
- **THEN** the call fails on its configured timeout rather than blocking the execution indefinitely

### Requirement: Jira is a tracker only
The Jira module SHALL implement the backlog-tracker contract and MUST NOT implement the code-host contract. A Jira target's repository coordinates SHALL be resolved through its configured code host, never through the Jira adapter.

#### Scenario: Jira cannot be selected as a code host
- **WHEN** a mapping target declares `codeHost: Jira`
- **THEN** startup validation fails, reporting that Jira provides no code-host implementation
