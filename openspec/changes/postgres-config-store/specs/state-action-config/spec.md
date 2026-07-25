# state-action-config — mapping sourced from the config store

## ADDED Requirements

### Requirement: A store outage degrades to cached configuration, never to no service
A receiver SHALL persist the validated mapping it loaded and, when the config store is unreachable at startup, SHALL start from that cached copy, serve traffic, and report itself degraded. It SHALL refuse to start only when the store is unreachable and no cached configuration exists.

The point is availability blast radius: one store now serves every client, so without this a single outage stops every client's loop at once — a worse posture than the per-deployment files this replaces, which could not be unavailable.

#### Scenario: Receiver survives a store outage
- **WHEN** a receiver starts, the config store is unreachable, and a cached mapping from a previous successful load exists
- **THEN** the receiver serves traffic using the cached mapping and reports degraded on its health endpoint

#### Scenario: Recovery refreshes the cache
- **WHEN** a receiver running on cached configuration reloads and the store is reachable again
- **THEN** it adopts the stored configuration, refreshes its cache, and stops reporting degraded

#### Scenario: A genuinely unconfigured receiver still refuses to start
- **WHEN** a receiver starts, the store is unreachable, and no cached configuration exists
- **THEN** startup fails and no traffic is served

#### Scenario: Degraded means possibly stale, and says so
- **WHEN** a mapping is changed in the store while a receiver is running on cached configuration
- **THEN** that receiver continues on the cached mapping until the store is reachable, and its degraded report is what distinguishes this from being up to date

## MODIFIED Requirements

### Requirement: Mapping deployed with the orchestrator, not the target repo
The mapping SHALL be read from the configuration store that the orchestrator's operators control. The receiver MUST NOT fetch mapping configuration from target repositories at event time; target repositories own only the agent commands themselves.

The property this protects is unchanged from when the mapping was a deployed file: whoever can push to a target repository must not thereby be able to change which of its states dispatch agents. What changed is where that authority now lives — write access to the store rather than commit access to the orchestrator's deployment.

#### Scenario: Target repo cannot grant itself dispatches
- **WHEN** a target repository adds or edits a mapping-like file in its own tree
- **THEN** the orchestrator's dispatch behavior is unaffected

#### Scenario: Mapping source is the store, not the image
- **WHEN** a receiver starts
- **THEN** it resolves its mapping from the configuration store, and a mapping file present in its image is not read

### Requirement: Fail-fast schema validation
The receiver SHALL validate the mapping it loads and refuse to serve traffic on validation failure, logging the specific violations. This applies identically whether the mapping came from the store or from the local cache.

#### Scenario: Invalid mapping blocks startup
- **WHEN** the receiver starts with a mapping entry missing a required field (e.g. no `action`)
- **THEN** the receiver exits/reports unhealthy and processes no webhooks

#### Scenario: Invalid cached mapping is not a licence to serve
- **WHEN** the store is unreachable and the cached configuration fails validation
- **THEN** the receiver refuses to serve traffic rather than serving a partial mapping
