# Tasks — Per-Project AI Pods

## 0. Answer the question that can invalidate the rest

- [ ] 0.1 Verify the **jobs-per-Container-Apps-environment quota** against the target project count, and whether it is raisable by support request. This is first because the whole shape depends on it: if hundreds of projects do not fit one environment, the environment stops being a single Terraform-owned resource and becomes a pool with a placement rule, which is a materially different change from the one proposed
- [ ] 0.2 Decide Storage Queues vs Service Bus before writing provisioning code (design Open Questions). Sessions or filtered subscriptions would let projects share a transport and remove queue sprawl entirely; it is the most expensive decision here to retrofit, so it is cheaper to settle now than when the sprawl hurts

## 1. OTel on the agent (independent, land first)

- [ ] 1.1 Add spans around the phases `ExecutionService` already distinguishes: gate re-validation, credential minting, clone, mapped command, write-back. Record each phase's outcome so a trace answers "which phase, how long" mid-run
- [ ] 1.2 Put project, vendor coordinates, item id, action and runtime on **every** span, not just the root — attribution must be queryable without a join, and a run that fails before its first phase completes must still be attributable
- [ ] 1.3 Carry the trace id on the existing `dispatch.outcome` event, additively. Every existing field and `LifecycleEvents.SchemaVersion`'s meaning stay as they are — the portal's whole history depends on that contract
- [ ] 1.4 Verify traces reach the collector and are queryable alongside `AppTraces`, the same way task 1.3 of `orchestrator-portal` verified the lifecycle events. **This needs a deployed stack**; until then it is unverified like everything else in that category
- [ ] 1.5 Do **not** parse agent-runtime output into spans. Worth a note in code, because it will look like an obvious improvement to someone later and it would couple this product to Claude Code's and opencode's output formats

## 2. Reconciliation, read-only first

- [ ] 2.1 Model a project's expected Azure resources from the configuration store — queue name, job name, environment, concurrency cap
- [ ] 2.2 Implement reconciliation as a **reporter only**: given the store and Azure, list projects missing a job or queue, and jobs or queues with no project. Prove the inventory is right before anything is allowed to act on it
- [ ] 2.3 Surface both drift directions in the portal. A project whose job is missing is a loop that never runs and raises no error of its own — it is invisible unless something looks for it
- [ ] 2.4 Make reconciliation idempotent and test that a second run with no configuration change creates and deletes nothing

## 3. Provisioning

- [ ] 3.1 Portal identity and role assignment scoped to creating and deleting jobs and queues in the designated resource group — not environments, networking, or role assignments (design D2). Verify the boundary by asserting a denied operation, not by trusting the portal not to try
- [ ] 3.2 Create a project's queue and job together, in a Terraform-owned environment
- [ ] 3.3 Report a partially provisioned project as **not ready** rather than healthy, and let a later reconciliation complete it
- [ ] 3.4 Deletion, after creation is proven. Second deliberately: an orphaned job costs money, a wrongly deleted one removes a project's dispatch path
- [ ] 3.5 Remove per-project jobs and queues from the environment's Terraform, and assert that applying it leaves portal-created resources untouched. A `terraform apply` that reconciles away every project's job would take out all of them at once (design D3)

## 4. Per-project dispatch

- [ ] 4.1 Resolve the target queue per project in the receiver instead of a single `QueueOptions`
- [ ] 4.2 Move the concurrency cap from a stack-wide setting to a per-project one
- [ ] 4.3 Fail visibly when a project's queue does not exist, rather than accepting and dropping the event — this is the failure mode the split introduces and it is silent by nature
- [ ] 4.4 Ensure an execution in flight when its project is deleted still reaches a terminal state with an outcome. "The job was deleted underneath it" must not become the one path that emits nothing, since the read model assumes exactly one outcome per dequeuing execution
- [ ] 4.5 Migrate existing stacks to one project each, draining the shared queue before removing the old job

## 5. Live run view

- [ ] 5.1 Trace timeline first — it is this product's own structured output, so it is both safer to show and more reliable than the console
- [ ] 5.2 Console stream second, rendered as text and never as markup, with known credential shapes redacted before display
- [ ] 5.3 State in the UI that output is raw agent output with known secret shapes removed, so redaction is not mistaken for a guarantee
- [ ] 5.4 Make the two independent: if the console is unavailable the timeline still renders, and vice versa
- [ ] 5.5 Gate access server-side on the project's authorization, and test refusal by direct request rather than by the UI not offering it
- [ ] 5.6 Provide no exec path of any kind, and add a test asserting no endpoint reaches the container other than reading output (design D5)

## 6. Cost attribution

- [ ] 6.1 Per-project consumption from spans: run counts, wall-clock compute, duration distributions, and model token counts where the runtime reports them
- [ ] 6.2 Label it as **apportionment, not billing**, with its basis stated. Compute bills at the environment level and model costs come from a provider bill this system never sees — someone will quote this number as an invoice unless the UI makes that wrong
- [ ] 6.3 Reconcile the span-derived share against per-project Azure resource metrics, which the per-project job makes available independently. Two coarse sources that agree are worth more than one precise-looking one

## 7. Close-out

- [ ] 7.1 Document the new topology: what Terraform owns, what the portal owns, what reconciliation does, and how to diagnose a project whose job is missing
- [ ] 7.2 **Security review of the portal's combined surface.** This change gives it Azure write; `postgres-config-store` gives it authority over dispatch configuration. They were proposed separately and neither alone triggers a review, but together the portal becomes the highest-value target in the system
- [ ] 7.3 Resolve design.md's open questions: environment topology and placement, Service Bus, what happens to a running execution when its project is deleted, whether the live view needs its own role, and who maintains the redaction list
