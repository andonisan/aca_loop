# Per-Project AI Pods — isolated dispatch, per-run telemetry, and a live run view

## Why

Today a client's stack has one queue and one Container Apps Job, and every target in that client's mapping shares them. Three consequences follow, and each is a different kind of problem:

- **Noisy neighbours.** `max_concurrent_agents` is a per-stack cap. One project with a burst of gated items consumes the whole client's capacity, and a poison-message storm starves every other project behind it. The gate re-validation invariant makes this safe but not fair.
- **Costs are not attributable.** Runs from every project land in one job, so "what did this project cost us this month" has no answer beyond a guess. With the infrastructure being ours rather than each client's, that question is a billing question, not a curiosity.
- **A run in flight is opaque.** An operator watching a stack sees a pod is `Running` and nothing else until it finishes. "How is the implementation going" is currently answered by waiting.

`postgres-config-store` makes stacks cheap to create — hundreds of them, from a form. That reframes what a stack should be. If creating one is a form submission rather than a Terraform change, a stack can be **one project** rather than one client, and each project can have its own queue and its own job. The isolation problems above stop being trade-offs and become configuration.

## What Changes

- **A stack becomes a project.** One project, one queue, one Container Apps Job. Concurrency, failure blast radius, and cost are all scoped to a project rather than pooled across a client's.
- **Split provisioning, deliberately.** Terraform owns the Container Apps **environment** — the expensive, slow, networked, long-lived resource. The **portal creates the job and queue** per project, because those are lightweight child resources whose lifecycle now matches a form submission. This keeps the portal's Azure permission scoped to "create jobs and queues inside this resource group" instead of broad subscription rights (design D2).
- **Per-project queues follow from per-project jobs, not by choice.** KEDA scales a job from one queue, and Azure Storage Queues have no selective consumption — several jobs over one queue steal each other's messages. So the queue is per project too (design D1).
- **OTel on every AI pod execution**, exported to the collector, with the project identity and the run's coordinates on every span. This serves three purposes at once: seeing what a run is doing, attributing cost to a project, and correlating a run's spans with the `dispatch.outcome` event that already exists.
- **A live run view in the portal**: the running execution's console stream alongside its correlated trace timeline — which step it is on and how long each has taken. Read-only. No shell, no exec into the container (design D5).
- **Cost attribution as a first-class output**, not a report someone assembles later. Spans carry enough to answer "what did this project consume" — see design D4 for what that does and does not include.

## Capabilities

### New Capabilities
- `project-provisioning`: creating, updating, and removing a project's queue and Container Apps Job from the portal, reconciled against the configuration store, inside a Terraform-owned environment.
- `pod-observability`: OpenTelemetry on every agent execution — spans for the run's phases, project and item identity on every span, export to the collector, and correlation with the existing lifecycle events.
- `live-run-view`: the portal's read-only view of an execution in flight — console stream plus correlated trace timeline, with the redaction rules that make showing it defensible.

### Modified Capabilities
- `ai-pod-dispatch`: dispatch resolves to a project's own queue and job rather than a stack-wide pair. The gate re-validation invariant, the one-message-per-execution rule, and the failure contract are unchanged — what changes is which queue a message goes to and which job drains it.

## Impact

- **Code**: the receiver resolves the target queue per project rather than from a single `QueueOptions`; the agent adds OTel instrumentation around its execution phases; the portal gains provisioning and a live run view; a new module owns Azure resource reconciliation.
- **Infra**: Terraform's per-client stack shrinks to the shared environment, Log Analytics, Key Vault and identities. Jobs and queues leave Terraform and become portal-managed, which means Terraform stops being the record of what exists — the configuration store becomes that record (design D3).
- **Azure limits**: there is a cap on jobs per Container Apps environment, and hundreds of projects will meet it. How many environments are needed, and what decides which project lands in which, is an open question rather than a detail.
- **Security**: the portal gains Azure write permission for the first time. Scoped to job and queue creation in a known resource group, but it is a genuine escalation from today's read-only-plus-queue-send, and it compounds with `postgres-config-store` giving the portal authority over dispatch configuration. The two together make the portal the highest-value target in the system.
- **Live run output is the sharpest new exposure.** A running agent's console can contain secrets, client code, and content injected through the issue body — the README already names prompt injection as a real surface. Showing that stream in a cross-client portal needs rules, not just a permission check (design D5).
- **Not in scope**: interactive shell into a running pod; moving run history out of Log Analytics; changing the agent runtimes; per-project Azure subscriptions.
