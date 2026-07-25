# portal-read-model — registry from the config store

## ADDED Requirements

### Requirement: Operators manage stacks and mappings through the portal
The portal SHALL allow an operator to create, edit, and remove stacks and their mappings, gated on the operator role. A submission that fails validation SHALL be rejected in full, reporting every violation, and MUST leave existing configuration unchanged.

#### Scenario: Onboarding a stack needs no deployment
- **WHEN** an operator creates a stack with valid coordinates and a valid mapping
- **THEN** the stack is stored, appears in the registry, and its receiver resolves that mapping on its next load — with no file edited and no deploy

#### Scenario: A reader cannot change configuration
- **WHEN** a user holding only the reader role attempts a configuration write, including by direct request rather than through the UI
- **THEN** the write is refused and nothing is stored

#### Scenario: Invalid submission changes nothing
- **WHEN** an operator submits a mapping with an unknown runtime
- **THEN** the submission is rejected naming that violation, and the stack's existing mapping is untouched

## MODIFIED Requirements

### Requirement: Stack registry is a validated, fail-fast configuration file
The portal SHALL load its registry of client stacks (stack id, display name, Azure coordinates — subscription, resource group, queue endpoint and name, ACA job name, Log Analytics workspace id —, vendor coordinates, and that stack's mapping) from the configuration store at startup, validate it structurally, and MUST refuse to serve traffic (unhealthy, process fails to start) if the store is unreachable or the configuration is invalid. The store SHALL be the only source of stack membership — no auto-discovery.

Membership remains explicit and reviewed-by-intent rather than inferred: it is now a table rather than a file, but a stack still appears because someone added it, never because a resource happened to carry a tag.

#### Scenario: Valid registry starts serving
- **WHEN** the portal starts and the store holds at least one structurally valid stack
- **THEN** the portal becomes healthy and every registered stack appears in the UI

#### Scenario: Invalid registry fails fast
- **WHEN** the portal starts and a stored stack is missing a required field (e.g. no Log Analytics workspace id)
- **THEN** startup fails before the portal serves any request, and the error lists every violation

#### Scenario: Unreachable store fails fast
- **WHEN** the portal starts and the configuration store is unreachable
- **THEN** startup fails rather than serving an empty stack list, which would misreport "no stacks" as a healthy state

#### Scenario: Portal and receiver resolve identically
- **WHEN** the portal resolves an item's state against a stack's mapping
- **THEN** it reads the same stored rows that stack's receiver reads, so the two cannot disagree about which states dispatch
