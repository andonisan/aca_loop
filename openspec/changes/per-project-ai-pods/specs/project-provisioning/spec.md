# project-provisioning — a project's dispatch path, created from the portal

## ADDED Requirements

### Requirement: A project's queue and job are provisioned together
Creating a project SHALL provision both its Storage Queue and its Container Apps Job, and removing a project SHALL remove both. Neither may exist without the other in a settled state: a queue with no job accumulates undispatched work, and a job with no queue can never scale.

#### Scenario: Creating a project yields a working dispatch path
- **WHEN** an operator creates a project with valid configuration
- **THEN** its queue and its job both exist, the job is scaled from that queue, and a message enqueued for that project causes an execution

#### Scenario: Removing a project removes both resources
- **WHEN** a project is removed
- **THEN** its job and its queue are both deleted, leaving no orphan that continues to cost money or appear in inventory

### Requirement: Jobs are created inside a Terraform-owned environment
The portal SHALL create jobs only within a Container Apps environment it did not create and does not manage. The portal's Azure identity MUST NOT hold permission to create environments, networking, or role assignments.

#### Scenario: Portal cannot exceed its provisioning scope
- **WHEN** the portal's identity attempts an Azure operation outside creating and deleting jobs and queues in its designated resource group
- **THEN** the operation is denied by role assignment, not by the portal declining to attempt it

### Requirement: Terraform must not manage per-project jobs
The environment's infrastructure-as-code SHALL NOT declare per-project jobs or queues. Applying it MUST NOT delete resources the portal created.

#### Scenario: Applying environment infrastructure leaves projects untouched
- **WHEN** the environment's Terraform is applied while projects exist
- **THEN** every portal-created job and queue survives, because they are not in its state and it does not attempt to reconcile them away

### Requirement: Reconciliation is idempotent and reports drift
The system SHALL reconcile the configuration store against Azure, creating what is missing, and SHALL report drift in both directions rather than only correcting it. Reconciling twice MUST leave the same result as reconciling once.

#### Scenario: A missing job is detected, not silently tolerated
- **WHEN** a project exists in the configuration store but its job does not exist in Azure
- **THEN** reconciliation reports it, because a project whose job is missing is a loop that never runs and shows no error of its own

#### Scenario: An orphaned job is detected
- **WHEN** a job exists in Azure with no corresponding project in the configuration store
- **THEN** reconciliation reports it rather than assuming it is safe to delete

#### Scenario: Repeated reconciliation is safe
- **WHEN** reconciliation runs twice with no configuration change between runs
- **THEN** the second run creates and deletes nothing

### Requirement: Provisioning failure leaves a project visibly incomplete
When provisioning partially fails, the project SHALL be reported as not ready rather than presented as operational. A project whose dispatch path is incomplete MUST NOT appear healthy.

#### Scenario: Half-provisioned project is not shown as ready
- **WHEN** a project's queue is created but its job creation fails
- **THEN** the project is shown as not ready with the failure surfaced, and reconciliation can complete it on a later run
