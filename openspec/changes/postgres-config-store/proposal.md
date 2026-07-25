# Postgres Config Store — stack registry and mappings out of files, into a database

## Why

Configuration lives in versioned files today: `stacks.yaml` in the portal lists every client stack, and each client's `mapping.yaml` ships with that client's orchestrator deployment. That was the right shape at one stack. It does not survive **50–500**.

Three things break at that scale, and only the first is cosmetic:

- **Editing.** A 500-stack `stacks.yaml` is several thousand lines. Onboarding a client becomes a hand-edit of a file nobody can review meaningfully, and a merge conflict magnet across everyone doing the same.
- **Bundling.** The portal needs each stack's mapping to resolve actions identically to that stack's receiver. Today that is a deploy-time copy from the same GitOps source (`orchestrator-portal` task 5.3, unimplemented). At 500 stacks that pipeline is a second system, and every copy is a chance to drift from the mapping its receiver actually runs — the exact failure the bundling was invented to prevent.
- **Querying.** "Which stacks target GitLab?", "which mappings still dispatch `/ds:propose`?", "which clients have no gate for implementation?" are unanswerable without grepping YAML across repos.

So: one Postgres holding the stack registry and every stack's mapping, authoritative, edited through the portal.

This reverses recorded decisions rather than extending them, and the reversal is the substance of the change — see design.md D1–D3. `orchestrator-portal` D2 chose a versioned file *because* membership is an authorization artifact; `agentic-backlog-orchestrator` D2 chose a deployed file *because* the mapping decides which states spend agent money. Those arguments do not disappear at scale; the file just stops being a workable answer to them. design.md records what is lost, and D4–D6 cover what must be built so the loss is bounded rather than discovered in an incident.

## What Changes

- **A `config-store` capability**: a Postgres schema for stacks and their state→action mappings, with migrations, plus the read and write paths over it. Postgres is the source of truth — not a cache of files, not a projection.
- **The receiver loads its mapping from the store, not from `mapping.yaml`.** Startup validation and the fail-fast contract are preserved unchanged: an invalid or unreachable configuration must still mean "serve no traffic", never "serve with a partial mapping".
- **Every receiver caches its last-known-good mapping to local disk** and starts from it when the store is unreachable, reporting degraded. Without this, one Postgres outage stops **every client's** loop at once — see design.md D4. This is not ceremony; it is the difference between a shared database and a shared single point of failure.
- **The portal reads the registry and all mappings from the store** instead of `stacks.yaml` and bundled copies. `orchestrator-portal` task 5.3 (GitOps bundling) is dropped: there is nothing left to bundle, and the portal and receiver now read the same rows rather than two copies that can drift.
- **Portal CRUD for stacks and mappings**, behind the existing `Portal.Operator` role. Creating a client stack becomes a form, not a pull request.
- **Terraform**: a Postgres Flexible Server, its network access, and per-stack credentials. Every client stack gains a dependency it did not have.
- **`stacks.yaml` and `mapping.yaml` become import sources, then stop being read.** A one-time importer moves existing files into the store so the cutover is not a retype.

## Capabilities

### New Capabilities
- `config-store`: the Postgres-backed configuration store — schema and migrations for stacks and mappings, the read path both hosts use, the write path the portal uses, the last-known-good cache that bounds a store outage, and the file importer for cutover.

### Modified Capabilities
- `state-action-config`: the mapping is sourced from the config store rather than a file deployed with the orchestrator. The mapping's *content* contract and every validation rule are unchanged; what changes is where it is read from, and the requirement that a store outage degrades to cached configuration rather than to no service.
- `portal-read-model`: the stack registry is read from the config store rather than `stacks.yaml`. Fail-fast on an invalid registry is preserved; auto-discovery is still rejected — membership remains explicit, it is simply now a table.

## Impact

- **Code**: new `Orchestrator.Modules.ConfigStore` (+ `.Contracts`); `Dispatch` loads mappings through the store contract instead of `MappingLoader.LoadFromFile`; `Orchestrator.Portal` swaps `StackRegistryLoader` for the store and gains CRUD pages; `MappingLoader`'s parser survives — it is still needed to validate mapping content on write and to import files.
- **Infra**: Postgres Flexible Server, private networking or firewall rules, a managed-identity or secret credential per client stack plus one for the portal, and backup/retention decisions this product has never had to make.
- **Deployment**: every client receiver gains a startup dependency on a shared component. The mitigation is D4's cache; the residual risk is that a *first* deploy of a new stack has no cache to fall back on.
- **Security**: this is the change's real cost. The mapping stops being protected by pull-request review and starts being protected by database access control. Anyone who can write to the `mapping_states` table can make any state spend agent money, and — as chosen — there is no approval step or change history to notice it afterwards. design.md D3 states this plainly and D6 records the minimum that keeps it operable.
- **Isolation**: "one stack per client, independent blast radius" (`agentic-backlog-orchestrator` open questions) no longer holds for configuration. One database now spans every client.
- **Dropped**: `orchestrator-portal` task 5.3 (bundle mappings from GitOps) is obsoleted by this change.
- **Not in scope**: moving run history or audit into Postgres — history stays in Log Analytics (`orchestrator-portal` D3 is untouched); secrets stay in Key Vault, and the store holds secret *names*, never values.
