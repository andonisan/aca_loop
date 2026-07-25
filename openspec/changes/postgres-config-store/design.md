# Design — Postgres Config Store

## Context

Two artifacts hold this product's configuration, and both are versioned files by explicit decision:

- `stacks.yaml` — the portal's list of client stacks. `orchestrator-portal` D2 chose a file over Azure-tag auto-discovery because "implicit membership in a surface that concentrates read access across all clients is the wrong default; an explicit versioned list is itself an authorization artifact."
- `mapping.yaml` — per stack, the state→action mapping. `agentic-backlog-orchestrator` D2 put it in the orchestrator's deployment rather than the target repo because "it decides which states spend agent money," so it "must not be writable by whoever can push to the target repo."

Both decisions were about *who can change what*, not about file formats. At 50–500 stacks the file stops being a workable answer — but the property it was protecting does not stop mattering. This document is mostly about that gap.

The chosen shape is: Postgres authoritative, edited directly through the portal, no approval workflow. That is recorded here as a decision with its costs, not presented as free.

## Goals / Non-Goals

**Goals**
- Stack registry and per-stack mappings in Postgres, authoritative, at 50–500 stacks.
- Onboarding a client is a portal form, not a pull request against two repos.
- The portal and a stack's receiver resolve actions from the same rows, so drift between them becomes structurally impossible rather than prevented by a copy step.
- A store outage degrades a receiver to its last-known-good mapping, never to no service.
- Every existing mapping rule and the fail-fast startup contract survive unchanged.

**Non-Goals**
- Run history in Postgres. It stays in Log Analytics (`orchestrator-portal` D3); this change touches configuration only.
- Secret values in Postgres. The store holds secret *names*; Key Vault still holds secrets.
- An approval workflow or change-review gate on config writes — explicitly out, see D3.
- Multi-region or HA Postgres. Single server; D4's cache is what covers its availability.
- Changing the mapping's content schema. Same fields, same validation, different source.

## Decisions

### D1 — Postgres is the source of truth, not a projection of files

The alternative considered seriously was Postgres as a queryable *projection*: files stay authoritative in git, a sync job materializes them into tables, and the portal reads the tables. That keeps pull-request review over the authorization surface and keeps git history.

Rejected, because it solves only the third of the three problems in the proposal. Editing is still a hand-edit of a many-thousand-line file, and onboarding is still a PR. The projection would buy queryability while leaving the two costs that actually motivated the change. Half a migration with two sources of truth is worse than either end state.

Consequence: files stop being read at all after cutover. They remain in the repo as the import source and then as historical record; nothing loads them.

### D2 — One store spans every client, and that is a real reversal

`agentic-backlog-orchestrator` resolved multi-tenancy as "one stack per client, independent blast radius." That holds for compute, queues, secrets, and log data — none of which this change touches. It stops holding for configuration: one database now serves every client's receiver.

Stated plainly so it is not discovered later: a corrupt migration, an exhausted connection pool, or a bad manual `UPDATE` can now affect every client at once, where previously each client's config was a file in that client's own deployment. D4 bounds the availability half of this. The correctness half — a bad write hitting everyone — is bounded only by D6.

Rejected alternative: a database per client. It preserves isolation and destroys the entire point, since the portal's cross-client queries are half the motivation and 500 databases is a worse operational problem than the one being solved.

### D3 — No approval workflow, and what that actually costs

As chosen: writes are direct CRUD behind the `Portal.Operator` role. No approval step, no change history, no diff to review.

What is given up, precisely — because "we lose git review" is too vague to act on:

- **Before**: changing which state dispatches an agent required a commit, a reviewer, and a deploy. Three humans-or-gates, all leaving a durable record.
- **After**: it requires the Operator role, or write access to the database by any other path (a connection string in an app setting, an admin console, a restore from backup, a migration script).

The product's security thesis is that "the ability to change the tracked state is the authorization gate for spending agent time." That thesis is unchanged for *items* — moving a work item still requires vendor permission. What changes is the layer beneath it: whoever can write `mapping_states` decides what "the gate state" even means, and can now do so without leaving a reviewable trace.

This is a deliberate trade for velocity at 500 stacks. D6 records the floor that keeps it operable, and Open Questions records the two things most likely to be regretted.

### D4 — Every receiver caches last-known-good configuration to disk

Not optional, and not ceremony. Without it, D2's single store becomes a single point of failure for every client's loop simultaneously — a strictly worse availability posture than the files it replaces, which could not be unavailable at all.

On successful load, the receiver writes the validated mapping to local disk. On startup with the store unreachable, it loads that cache, serves traffic, and reports degraded on its health endpoint. It refuses to start only when the store is unreachable **and** no cache exists — a genuinely unconfigured process.

The consequence to accept: a receiver running on cache is running on possibly-stale configuration, and a mapping change will not reach it until the store returns. That is the correct trade — a stale gate mapping dispatches slightly wrong; an unstartable receiver dispatches nothing for anyone.

The residual gap: a stack's *first* deploy has no cache. A new client cannot come up during a store outage. Accepted; it is a scheduling inconvenience, not an incident.

### D5 — Validation stays where it is; only the source moves

`MappingLoader`'s parser and its every rule (required fields, known vendor, known runtime, at-least-one-of-repo/project, non-empty `codeRepo`, code-host registration) survive intact. They run in two new places:

- **On write**, in the portal, so an invalid mapping cannot be stored at all. Rejecting at the point of entry is the whole advantage of a database over a file — the file could only be validated by whoever loaded it, always too late.
- **On read**, at receiver startup, unchanged from today. Belt and braces deliberately: the store is writable by paths that do not go through the portal (D3), so the receiver cannot assume rows are well-formed just because the portal would not have written them badly.

`MappingFile` (the YAML shape) stays too — it is the importer's input format and remains the natural way to express a mapping in a text field.

### D6 — The floor: writes are attributed, and the store is not a secret store

Two things that survive even in "no ceremony" mode, because without them the system is not operable rather than merely less safe:

- **Every row carries who last wrote it and when.** Two columns, no workflow, no approval. This is not the change history D3 gave up; it is the minimum needed to answer "why does this client suddenly dispatch on a different state" without guessing. Attribution is cheap; reconstructing intent from nothing is not.
- **The store holds secret names, never secret values.** `readCredentialSecretName` already works this way and it stays that way. A config database that accumulates tokens becomes a credential store with none of Key Vault's properties, and that happens gradually unless it is ruled out at the schema level.

### D7 — Modelled as tables, not a JSON blob per stack

A `mapping_states` row per state, not a `jsonb` column holding the whole mapping.

The queries that motivated the change are cross-cutting: which stacks dispatch a given action, which have no implementation gate, which target a vendor being deprecated. Those are trivial over rows and awkward over blobs. Foreign keys also make "a state entry for a stack that no longer exists" unrepresentable, where a blob makes it merely unlikely.

Cost: writing a mapping is a multi-row transaction rather than one upsert. Acceptable, and the transaction boundary is itself useful — a half-applied mapping is exactly the state the fail-fast contract exists to prevent.

### D8 — EF Core, and the portal is the single schema owner that migrates

EF Core with `Npgsql.EntityFrameworkCore.PostgreSQL`, mirroring ds-connect's stack exactly (`Microsoft.EntityFrameworkCore` 10.0.9, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.2).

**This decision was rewritten.** The first version said migrations must never run at application startup, reasoning that hundreds of receivers plus the portal all start against this database and would race. That was half right, and the half that was wrong matters.

ds-connect applies migrations at startup — every module does `await dbContext.Database.MigrateAsync()` in its `InitializeAsync`, driven from `app.InitializeAppModulesAsync()` before `app.Run()`, recorded in an archived ADR. No runner, no job, no CLI step. It works there because ds-connect is **one server** owning every module and every schema: exactly one process ever migrates.

That is the actual rule, and it is about ownership rather than timing: **the component that owns a schema migrates it, and there must be exactly one of it.**

Applied here: the **portal** owns the `configstore` schema and migrates at startup, following ds-connect's convention verbatim. There is one portal. **Receivers never migrate** — they are readers of a schema they do not own, and hundreds of them calling `MigrateAsync` would be the race the first version of this decision feared. A reader that finds a schema older than it expects fails fast rather than fixing it, which is the correct behaviour for something that does not own the thing.

So the guard that matters is not "no migration at startup" but "no `Database.Migrate()` in any host except the portal". That is narrower, matches the house convention, and is enforceable by inspection.

The Burndowns note from the first draft still stands as a caution, but for a different reason than I first gave: it migrates on startup *and* is a single instance, so it is an example of this rule being satisfied by accident rather than a counter-example.

### D9 — The config store is a module with its own schema, following ds-connect's persistence conventions

This product's `design.md` D7 (in `agentic-backlog-orchestrator`) already commits to mirroring ds-connect's organization. It has had no persistence until now, so the persistence half of those conventions has never been exercised. Adopted wholesale rather than reinvented:

- **One Postgres schema per module.** `configstore`, declared three ways in concert the way ds-connect does it: `modelBuilder.HasDefaultSchema("configstore")` in `OnModelCreating`, `npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "configstore")` at registration, and `EnsureSchema` in the first migration. All modules share one physical database; isolation is by schema, and there are no cross-schema queries.
- **`DbContext` is `internal sealed`**, never public, never referenced by a host. That is what makes "no repository pattern" safe there: another module physically cannot reach it. Test access via `InternalsVisibleTo`.
- **No repository pattern, no `IUnitOfWork`.** Handlers take the `DbContext` directly. ds-connect has none and neither will this.
- **Entity configuration** as `IEntityTypeConfiguration<T>` classes picked up by `ApplyConfigurationsFromAssembly`, with `ToTable("snake_case")`, lengths from a shared constants type, and enums stored as strings via `HasConversion<string>()`.
- **Connection by `IConfiguration.GetConnectionString(...)`**, constructing the client inside the module's own `Add(...)` — not an Aspire client integration. ds-connect documents the mechanical reason: `IModule.Add` receives only `IServiceCollection`/`IConfiguration`, so builder-based Aspire client extensions are structurally unavailable to a module.

One consequence worth flagging rather than burying: **this repo skipped MOD003 and MOD005 when porting ds-connect's analyzers**, with the recorded note that they "key off EF `Aggregate`/`BaseEntity`, which don't exist in this stateless product". They are about to exist. MOD003 (domain entities must be `internal`) and MOD005 (entities must never appear in a public signature) are precisely the rules that keep a config schema from leaking across module boundaries, so porting them becomes part of this change rather than a nice-to-have.

## Risks / Trade-offs

- **The authorization surface loses its review gate** (D3). The single largest cost of this change, chosen deliberately.
- **Configuration becomes cross-client** (D2). Availability is bounded by D4; a bad write reaching every client is bounded by nothing but D6's attribution, after the fact.
- **A new startup dependency for every stack.** Mitigated by D4, except on first deploy.
- **Operational surface this product has never had**: migrations, backups, restore testing, connection pooling, and a database whose loss now means reconstructing every client's configuration. Backup/restore is not a checkbox here — it is the recovery path for the entire product's configuration.
- **Files and rows will disagree during cutover.** Bounded by making the switch per-host and one-way, with the importer run once — see the migration plan.
- **Postgres for ~500 rows is oversized**, and that is fine. The choice is driven by queryability and transactional writes, not volume; noting it so nobody later mistakes the row count for the justification.

## Migration Plan

1. Stand up Postgres and apply migrations. Nothing reads it yet.
2. Import `stacks.yaml` and every `mapping.yaml` through the importer. Verify by diffing imported rows against the parsed files — a mismatch here is a bug in the importer, and it is far cheaper to find now than after the files stop being read.
3. Switch the **portal** to the store first. It is read-mostly and its failure is visible without stopping any loop, which makes it the right place to discover schema problems.
4. Switch **receivers**, one stack at a time, with D4's cache in place before the first one. A receiver that will not start is a client's loop stopped, so this step is per-stack and reversible by redeploying the previous image.
5. Enable portal CRUD once reads are proven on both hosts.
6. Stop shipping `mapping.yaml` in receiver images and delete `stacks.yaml` from the portal deployment. Keep both in the repo as historical record.

Rollback: until step 6, redeploying the previous image restores file-based loading for that host, because the files are still present and still valid.

## Open Questions

- **Does "no ceremony" survive the first incident?** If someone changes a mapping and a client's loop starts dispatching wrongly, D6's attribution says who and when but not what the value was before. Whether that is tolerable is a judgement this design cannot make in advance. Adding a history table later is cheap; recovering the history you did not record is not.
- **Who holds `Portal.Operator` once it also means "can change any client's dispatch configuration"?** The role was defined for operational verbs (dispatch, cancel) whose blast radius was one item. It now also gates configuration for every client. That may warrant a third role rather than widening the second.
- **Where does the portal's own database credential live**, and does a compromised portal now imply compromised configuration for every client? Previously the portal held read-scoped vendor credentials and nothing that could change dispatch behaviour.
- **Backup and restore expectations** — RPO/RTO for the configuration of every client. Unanswered, and it is the question whose wrong answer is least recoverable.
