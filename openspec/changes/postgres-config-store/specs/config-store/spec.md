# config-store — Postgres-backed configuration for stacks and mappings

## ADDED Requirements

### Requirement: Configuration is stored relationally, not as opaque documents
The store SHALL model a stack and each of its mapped states as rows, with a state entry referencing the stack it belongs to, so that configuration is queryable across stacks and a state entry cannot reference a stack that does not exist.

#### Scenario: Cross-stack query answerable
- **WHEN** an operator asks which stacks declare a state dispatching a given action
- **THEN** the store answers from configuration alone, without loading and parsing every stack's mapping

#### Scenario: Orphan state entries are unrepresentable
- **WHEN** a stack is removed
- **THEN** its state entries are removed with it, and no state entry referencing a missing stack can exist

### Requirement: A mapping is written atomically
Writing a stack's mapping SHALL apply as a single transaction. A partially applied mapping MUST NOT be observable by any reader.

#### Scenario: Failed write leaves the previous mapping intact
- **WHEN** a mapping write fails partway through
- **THEN** readers continue to see the previous complete mapping, and no state entry from the failed write is present

#### Scenario: Concurrent readers never see a half-applied mapping
- **WHEN** a reader loads a stack's mapping while a write of that mapping is in progress
- **THEN** the reader observes either the complete previous mapping or the complete new one

### Requirement: Stored configuration is validated on write
The store SHALL apply the same structural validation to a mapping on write that the receiver applies on load — required fields, known vendor, known runtime, at least one of repo/project, a non-empty resolved code repository — and MUST reject a write that violates any of them, reporting every violation rather than the first.

#### Scenario: Invalid mapping is rejected at the point of entry
- **WHEN** an operator submits a mapping whose state entry has no action
- **THEN** the write is rejected, nothing is stored, and the response names the offending state and every other violation in the submission

#### Scenario: Rejection does not disturb existing configuration
- **WHEN** a write is rejected as invalid
- **THEN** the stack's previously stored mapping is unchanged

### Requirement: Readers validate what they load
A host loading configuration from the store SHALL validate it before use rather than trusting that it was validated on write. Configuration that fails validation on load MUST be treated exactly as an invalid file was: the host serves no traffic.

#### Scenario: Row written outside the portal is still caught
- **WHEN** a mapping row is written directly to the database, bypassing the portal's validation, and is structurally invalid
- **THEN** the receiver that loads it refuses to serve traffic and reports the violations

### Requirement: Writes are attributed
Every stack and state entry SHALL record the identity that last wrote it and when. Attribution MUST be recorded for writes through any path the system controls.

#### Scenario: A configuration change is attributable
- **WHEN** an operator changes a stack's mapping through the portal
- **THEN** the affected rows record that operator's identity and the time of the write

### Requirement: The store holds secret names, never secret values
Configuration SHALL reference credentials by their Key Vault secret name. The store MUST NOT contain tokens, passwords, private keys, connection strings, or any other credential value.

#### Scenario: Credential reference is a name
- **WHEN** a stack's read-scoped vendor credential is configured
- **THEN** the stored value is the Key Vault secret's name, and resolving it to a value happens at read time against Key Vault

### Requirement: Existing files import without retyping
The system SHALL provide a one-time import of an existing `stacks.yaml` and its referenced mapping files into the store, producing configuration equivalent to what those files parse to today.

#### Scenario: Imported configuration matches the files
- **WHEN** the importer runs against the current registry and mapping files
- **THEN** the resulting rows parse to the same validated configuration the files do, and any discrepancy is reported rather than silently accepted

#### Scenario: Import is repeatable without duplication
- **WHEN** the importer runs twice against the same files
- **THEN** the second run leaves the store in the same state as the first, rather than creating duplicate stacks
