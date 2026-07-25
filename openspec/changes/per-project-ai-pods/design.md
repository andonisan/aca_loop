# Design — Per-Project AI Pods

## Context

The current unit of isolation is the client: one stack, one queue, one Container Apps Job, every target in that client's mapping sharing them. That was right when a stack was a Terraform change. `postgres-config-store` makes a stack a form submission, which makes a much smaller unit affordable — and the three problems in the proposal (fairness, cost attribution, opacity of a run in flight) are all consequences of the unit being too large.

One fact constrains the shape more than any preference: **KEDA scales a job from exactly one queue, and Azure Storage Queues have no selective consumption.** There is no filter, no subscription, no session. Several jobs pointed at one queue compete for every message, and a project's job would happily run another project's work. So the queue and the job move together or not at all.

A second fact reframes the security analysis: **the infrastructure is ours, not each client's.** The portal creating compute in our own subscription is a different proposition from the portal creating compute in fifty clients' subscriptions, and it is what makes D2 viable.

## Goals / Non-Goals

**Goals**
- A project's concurrency, failures, and cost are its own.
- Creating a project provisions its dispatch path without a deploy.
- Every execution is traceable while it runs, not only after it ends.
- Cost is attributable to a project from telemetry, not reconstructed from Azure billing exports.
- The portal's Azure permission stays as small as the job it has to do.

**Non-Goals**
- Interactive shell into a running pod. Explicitly rejected — see D5.
- Replacing Log Analytics as the run-history store. `orchestrator-portal` D3 stands.
- Changing the dispatch invariants. Gate re-validation, one message per execution, and the failure contract are untouched; only the queue they operate over changes.
- Per-client Azure subscriptions or per-project resource groups.
- Exact chargeback accounting. D4 is explicit about what the telemetry can and cannot support.

## Decisions

### D1 — A project's queue and job are one unit, because KEDA makes them one

Not a design preference. A job scales from a single queue; Storage Queues cannot be consumed selectively. Pointing several projects' jobs at one queue means every job races for every message — and the gate re-validation invariant would *hide* the problem rather than surface it, since the wrong job would clone the wrong repo and then exit cleanly on a stale gate. Silent, wasteful, and hard to notice.

Alternative considered: move to Service Bus with sessions or filtered subscriptions, keeping one transport for many projects. Rejected for now, but honestly: it is the better answer if project count makes queue sprawl painful, and it is the one change here that would be expensive to retrofit. Recorded in Open Questions rather than dismissed.

### D2 — Terraform owns the environment; the portal owns jobs and queues

The split follows the lifecycle, not the technology. The Container Apps environment is slow to create, carries networking and Log Analytics wiring, and changes rarely — declarative infrastructure, reviewed. A job and a queue are lightweight child resources whose lifecycle now matches "someone filled in a form".

The security consequence is the point of splitting rather than handing the portal everything: because the environment already exists, the portal's identity needs permission to create jobs *within a known resource group*, not to create environments, networks, or role assignments. That is a bounded escalation with a name, rather than Contributor.

It is still an escalation. Today the portal reads Azure and sends queue messages. After this it creates compute. Combined with `postgres-config-store` making it authoritative for dispatch configuration, the portal becomes the highest-value target in the system — worth stating plainly because the two changes were proposed separately and their union is larger than either.

Rejected alternative: a separate reconciling controller (operator pattern) watching the config store. It keeps the portal read-only and is the more conventional shape, but it is a new component with its own lifecycle, deployment, and failure mode, to avoid one scoped role assignment. Revisit if the portal's permission surface grows further.

### D3 — Terraform stops being the record of what exists

Once jobs and queues are created by the portal, `terraform plan` no longer describes reality, and this is worth naming because it silently invalidates an operational habit. The configuration store becomes the inventory: a project row is the claim that a job should exist, and reconciliation is what makes it so.

Consequences to accept:
- **Drift is possible in both directions.** A job deleted in Azure, or one whose project row was removed while the portal was down. Reconciliation must be idempotent and re-runnable, and must be able to report drift rather than only fix it silently.
- **Terraform must not fight the portal.** The environment's Terraform must not manage jobs at all — a `terraform apply` that destroys portal-created jobs because they are not in state would take out every project at once.

### D4 — Telemetry carries project identity, and what that buys is attribution, not accounting

Every span from an agent execution carries the project, the item, and the run's coordinates. That is what makes "what did this project consume" answerable without an Azure cost export keyed on resources nobody tagged.

Being precise about what this is, because "cost attribution" invites over-claiming:

- **What it supports**: relative attribution. Wall-clock compute per project, run counts, duration distributions, and — if the runtime reports them — model token counts per run. Enough to say a project is expensive, to compare projects, and to notice a regression.
- **What it does not support**: an invoice. Container Apps bills on vCPU-seconds and memory-seconds at the environment level, and model costs come from a provider bill this product never sees. Telemetry gives the numerator for a fair split, not the total.

The per-project job helps here independently of telemetry: Azure resource-level metrics are now already scoped to a project, which is a second, coarser source that can be reconciled against the spans.

### D5 — Read-only run view, and the output is treated as untrusted

The live view is the console stream plus the correlated trace timeline. No exec, no shell.

Interactive shell was considered and rejected on a specific ground rather than general caution: the agent container holds write-capable credentials for the target repository, and it is running an LLM against content from an issue body. A shell into it, from a portal that spans clients, is remote code execution inside a credentialed container reachable from a shared surface. The diagnostic value is real; it is not worth that.

Even the read-only stream needs rules, because the output is not the product's own text:

- It can contain **secrets** — a clone URL with a token in it, an environment dump, a command echoing a variable.
- It can contain **injected content** from the issue body, which the README already names as a live surface. Rendering it in an operator's browser makes the portal a delivery vehicle for whatever an issue author wrote.
- It can contain **client code and data**, shown in a portal where an operator may hold access to many clients.

So: the stream is rendered as text and never as markup, known credential shapes are redacted before display, and access is gated on the same role as the stack's other data. The trace timeline is the safer half — spans are structured and this product emits them, so they can be shown without the same treatment. Where the two disagree in usefulness, prefer showing the spans.

### D6 — Spans describe the run's phases, and correlate with the outcome event

The existing `dispatch.outcome` event is the terminal record and stays exactly as it is — the portal's history depends on it and `LifecycleEvents.SchemaVersion` pins it. Spans are additive and answer a different question: not "how did it end" but "where is it now".

The phases worth their own span are the ones already distinguishable in `ExecutionService` and that an operator waiting on a run actually cares about: gate re-validation, credential minting, clone, the agent command, and the write-back. The trace id is carried on the outcome event so a finished run in history links to its trace, which is the join that makes both useful.

Deliberately not instrumented as spans: individual tool calls inside the agent runtime. That is the runtime's business, it varies per runtime, and this product does not parse the agent's internals today — starting would couple it to Claude Code's and opencode's output formats.

## Risks / Trade-offs

- **Jobs per environment is a hard Azure limit**, and hundreds of projects will meet it. This is the risk most likely to force a redesign, and it is a quota to verify before committing, not after — see Open Questions.
- **Resource sprawl**: a queue and a job per project, created dynamically. Deleting a project must actually delete both, or the account accumulates orphans that cost money and confuse inventory.
- **The portal becomes the highest-value target** (D2), and this change plus `postgres-config-store` arrived separately. Their union deserves a security review that neither triggers alone.
- **Live output is an exfiltration and injection surface** (D5), mitigated but not eliminated by redaction — redaction catches shapes it knows.
- **Reconciliation is a new failure mode.** A project whose row exists but whose job does not is a loop that silently never runs. Detecting that must be part of the feature, not an afterthought.
- **Cost attribution will be quoted as exact** by someone, eventually, regardless of D4's caveats. Label it in the UI as apportionment, not billing.

## Migration Plan

1. Verify the jobs-per-environment quota against the target project count. If it does not fit, the environment topology question moves from Open Questions to a blocking decision before any code.
2. Add OTel spans to the agent, exporting through the existing collector path. Independently useful and it changes no dispatch behaviour, so it can land and be observed first.
3. Build reconciliation as a read-only reporter: given the config store, report which projects lack a job or queue. Prove the inventory is right before letting anything create resources from it.
4. Enable creation, then deletion. Deletion second and deliberately — an orphaned job costs money, a wrongly deleted one loses a project's dispatch path.
5. Migrate existing stacks: one project per existing target, each with its own queue and job, with the old shared queue drained before the old job is removed.
6. Ship the live run view once spans are flowing, starting with the trace timeline and adding the console stream after the redaction rules have been exercised.

Rollback: through step 4, the shared per-client queue and job still exist and dispatch can be pointed back at them by configuration.

## Open Questions

- **How many jobs fit in one Container Apps environment, and what decides which project goes where?** If the answer is fewer than the project count, the environment stops being a single Terraform-owned resource and becomes a pool with a placement rule — a materially bigger change than this proposal describes.
- **Service Bus instead of Storage Queues?** D1 keeps Storage Queues, but sessions or filtered subscriptions would let many projects share a transport and would remove queue sprawl entirely. It is the most expensive thing here to retrofit later, which argues for deciding it now rather than when the sprawl hurts.
- **What happens to a running execution when its project is deleted?** Deleting the job while a pod is mid-run either strands work or kills it; neither is obviously right, and the failure contract has no story for "the job disappeared".
- **Does the live view need its own role?** It shows raw client output, which is a different sensitivity from queue depth and run counts. `Portal.Reader` currently covers both.
- **Where is the redaction list maintained**, and what is the plan for the first secret shape it misses? A redactor that is never updated is a false assurance.
