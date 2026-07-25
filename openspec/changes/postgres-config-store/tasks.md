# Tasks — Postgres Config Store

> Groups 1-4 are implementable now: schema, read path, cache and portal CRUD are all code, and the read
> path can be tested against a real Postgres via Testcontainers the same way `QueueDepthSource` is tested
> against Azurite. Group 5 needs Azure (server, networking, backup/restore) and a cutover window.
>
> `per-project-ai-pods` depends on this change and is blocked until at least groups 1-4 land.

## 1. Schema, module, and migrations

- [x] 1.0 Mirror ds-connect's module-with-persistence layout (design D9), which this repo has never had an example of: `Infrastructure/Persistence/{ConfigStoreDbContext.cs, Migrations/}`, entity configurations beside their feature rather than under Persistence, and test projects under `src/tests/modules/ConfigStore/`. Packages match ds-connect's pins: `Microsoft.EntityFrameworkCore` 10.0.9, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.2, with `Microsoft.EntityFrameworkCore.Design` referenced **only** by the startup host, never by the module
- [x] 1.1 Create `src/modules/ConfigStore/Orchestrator.Modules.ConfigStore.Contracts` with the read contract both hosts consume (`IConfigStore`: load the registry, load one stack's mapping) and the stored shapes, kept distinct from `Dispatch.Contracts`' validated `MappingConfig` so the store's row model and the resolver's model can differ without one dragging the other
- [x] 1.2 Create `src/modules/ConfigStore/Orchestrator.Modules.ConfigStore` with the Postgres implementation. **Decided: EF Core** with `Npgsql.EntityFrameworkCore.PostgreSQL` (design D8). `ConfigStoreDbContext` is `internal sealed`, declares `HasDefaultSchema("configstore")` plus `ApplyConfigurationsFromAssembly`, and is registered from the module's own `Add(...)` using `configuration.GetConnectionString(...)` — not an Aspire client integration, which `IModule.Add` cannot reach anyway since it receives only `IServiceCollection`/`IConfiguration`. No repository pattern; handlers take the context directly, which is safe precisely because it is internal. Check advisories on restore — this repo treats audit findings as build errors and `Microsoft.Identity.Web` already had to be pinned forward once for exactly that
- [x] 1.3 Schema: `stacks` (id, display name, azure coordinates, vendor coordinates, read-credential secret **name**) and `mapping_states` (stack id FK, native state, action, runtime, on-success state, on-failure state), with `ON DELETE CASCADE` so an orphan state entry is unrepresentable (design D7) and a unique constraint on (stack, native state)
- [x] 1.4 Add `last_written_by` and `last_written_at` to both tables (design D6). Not a history table — the minimum needed to answer "why does this client dispatch differently now" without guessing
- [x] 1.5 Migrations run from the **portal's** `InitializeAsync` via `Database.MigrateAsync()`, exactly as every ds-connect module does — the house convention, and safe because there is one portal and it owns this schema. **Receivers must never migrate**: they read a schema they do not own, and hundreds of them calling `MigrateAsync` is the race this rule exists to prevent. A receiver that finds an unexpected schema version fails fast rather than fixing it. Add an assertion or review note that `Database.Migrate`/`MigrateAsync` appears in no host but the portal (design D8, rewritten after mapping ds-connect)
- [x] 1.7 Author the first migration with the portal as the startup project (`dotnet ef migrations add InitialCreate`) and verify it emits `EnsureSchema("configstore")`, plus `MigrationsHistoryTable("__EFMigrationsHistory", "configstore")` at registration — the three-part schema declaration ds-connect uses in concert (design D9)
- [x] 1.8 Port the **MOD003 and MOD005** analyzers, skipped when this repo adopted ds-connect's analyzer set with the note that they "key off EF `Aggregate`/`BaseEntity`, which don't exist in this stateless product". They are about to exist, and those two rules — entities must be `internal`, entities must never appear in a public signature — are exactly what stops a config schema leaking across module boundaries (design D9)
- [x] 1.6 Enforce at the schema and code level that no credential *value* can be stored (design D6) — the column is a secret name, and a review note explaining why is worth more than a comment nobody reads

## 2. Read path and validation

- [x] 2.1 Implement loading a stack's mapping from the store into `Dispatch.Contracts`' `MappingConfig`, reusing `MappingLoader`'s existing validation wholesale — every rule survives, only the source changes (design D5)
- [ ] 2.2 Implement loading the full registry for the portal, with the same fail-fast contract `StackRegistryLoader` has today: invalid or unreachable → refuse to serve, listing every violation
- [x] 2.3 Validate on read even though the portal validates on write (design D5). The store is writable by paths that do not go through the portal, so the receiver cannot assume rows are well-formed
- [x] 2.4 Unit-test the read path against a real Postgres via Testcontainers, not a fake — the same reasoning that made the Azurite test worth writing: the uncertain part is whether the storage layer behaves as assumed, and a fake asserts the assumption rather than testing it. **Note `TESTCONTAINERS_RYUK_DISABLED=true` and `DOCKER_HOST` are needed on this machine** (see `AzuriteFixture`)

## 3. Last-known-good cache (design D4)

- [ ] 3.1 Persist the validated mapping to local disk on every successful load, and load from it when the store is unreachable at startup — serving traffic and reporting degraded rather than failing
- [ ] 3.2 Refuse to start when the store is unreachable **and** no cache exists: that is a genuinely unconfigured process, not a degraded one
- [ ] 3.3 Validate cached configuration on load too — a corrupt cache must not become a licence to serve a partial mapping
- [ ] 3.4 Surface degraded state on the health endpoint and in the portal, so "running on possibly-stale configuration" is visible rather than inferred. A silently-stale receiver is the failure mode this whole decision risks
- [ ] 3.5 Test the outage path explicitly: start against a live store, stop it, restart the receiver, assert it serves from cache and reports degraded; then restore and assert it re-adopts and clears degraded. This is the requirement that bounds D2's blast radius, so it earns a real test rather than a unit-level fake

## 4. Portal CRUD

- [ ] 4.1 Replace `StackRegistryLoader` with the store read path; delete the `stacks.yaml` load once both hosts are switched (migration plan step 6, not before)
- [ ] 4.2 Stack create/edit/remove behind `Portal.Operator`, validating on write and rejecting in full with every violation named
- [ ] 4.3 Mapping editing per stack, written as one transaction (design D7) so a half-applied mapping is never observable
- [ ] 4.4 Test that a reader-role user cannot write, including by direct request rather than through the UI — this is the deferred half of `orchestrator-portal` task 2.3, which could not be tested until write verbs existed
- [ ] 4.5 Attribute every write to the acting operator (design D6)

## 5. Infrastructure and cutover

- [ ] 5.1 Terraform: Postgres Flexible Server, networking, and per-stack plus portal credentials. Prefer managed identity over passwords where Postgres Flexible Server supports it — this product holds no database passwords today and should not start
- [ ] 5.2 Backup and restore: configure retention **and test a restore**. This database now holds every client's configuration, so restore is the recovery path for the product's whole config surface, not a checkbox (design Risks)
- [ ] 5.3 Write the importer for `stacks.yaml` + mapping files, idempotent so a second run does not duplicate stacks
- [ ] 5.4 Run the importer and verify by diffing imported rows against the parsed files — a mismatch is an importer bug, and it is far cheaper to find before the files stop being read
- [ ] 5.5 Cut over the portal first (read-mostly, fails visibly, stops no loop), then receivers one stack at a time with the cache already in place
- [ ] 5.6 Stop shipping `mapping.yaml` in receiver images and remove `stacks.yaml` from the portal deployment; keep both in the repo as historical record
- [ ] 5.7 Drop `orchestrator-portal` task 5.3 (bundle mappings from GitOps) as obsoleted — there is nothing left to bundle, and note why in that change's tasks so the removal is not mistaken for an oversight

## 6. Close-out

- [ ] 6.1 Document the store in `README.md` and the product brief: schema, who can write, how a stack is onboarded now, and the degraded-mode behaviour operators will eventually see
- [ ] 6.2 Record in `orchestrator-portal` and `agentic-backlog-orchestrator` that their D2 decisions are superseded here, with a pointer — a future reader hitting "the mapping is a deployed file" must find out immediately that it no longer is
- [ ] 6.3 Resolve design.md's open questions and record the decisions: whether attribution without history survived contact with a real incident, whether `Portal.Operator` should split now that it also means "can change any client's dispatch configuration", where the portal's database credential lives, and the RPO/RTO expectation for configuration
