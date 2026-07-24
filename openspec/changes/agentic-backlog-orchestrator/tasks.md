# Tasks — Agentic Backlog Orchestrator

## 1. .NET Aspire solution, canonical model, and GitHub adapter (v1 parity)

- [ ] 1.1 Scaffold the solution mirroring ds-connect's organization: `src/` root (`.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`), `src/root/{Orchestrator.Aspire, Orchestrator.Receiver, Orchestrator.Agent}`, `src/shared/{BuildingBlocks, Constants, ServiceDefaults, ArchitectureAnalyzers}`, `src/modules/`, `src/tests/` (module tests + `ArchTests`), applying the adopted ds-connect spec baseline listed in design.md D7 (repo-structure, commit hooks/commitlint/CSharpier, editorconfig, CPM)
- [ ] 1.2 Create `src/modules/Backlog/Orchestrator.Modules.Backlog.Contracts` with the canonical event `{vendor, project, repo, item_id, native_state, actor}`, the canonical queue message `{vendor, project, repo, item_id, state, action, runtime}`, and `IVendorAdapter`: `VerifyWebhook`, `ParseEvent`, `IsAutomationActor`, `ReadItemState`, `TransitionState`, `PostComment`, `GetCloneCredentials`
- [ ] 1.3 Implement `src/modules/GitHub/Orchestrator.Modules.GitHub` (self-registering `IModule`, keyed by vendor) porting the logic from `webhook-receiver/src/github.js` and `agent/src/github.js` (HMAC verify, `labeled` parsing, bot self-filter, label swap with single-label invariant, installation-token minting)
- [ ] 1.4 Implement the `Dispatch` module (webhook endpoints per vendor, filter, enqueue — composed by Receiver) and the `Execution` module (gate re-validation, runtime run with `Process` + `CancellationTokenSource` watchdog, failure contract — composed by Agent) for GitHub-only behavior identical to v1, with single-file vertical-slice use cases
- [ ] 1.5 Port/enable the MOD architecture analyzers and `ArchTests` so cross-module access is contracts-only from the first commit
- [ ] 1.6 Update both Dockerfiles (.NET images; agent image keeps Claude Code + opencode + `gh` alongside the .NET runtime) and verify against the v1 rollout checklist in `README.md`

## 2. Mapping configuration (state-action-config)

- [ ] 2.1 Define the mapping YAML schema: `targets[] → {vendor, project/repo, states[] → {native_state, action, runtime, on_success_state, on_failure_state}}` with JSON-schema validation
- [ ] 2.2 Load and validate the mapping at receiver startup; fail fast (unhealthy, no traffic) on schema violations
- [ ] 2.3 Replace the receiver's hardcoded gate-label filter with mapping lookup (unknown target or unmapped state → drop)
- [ ] 2.4 Replace the agent's hardcoded label→command logic with mapping-driven action/runtime resolution and mapping-aware gate re-validation (stale gate → exit 0, delete message)
- [ ] 2.5 Ship a mapping file reproducing v1's ds-connect configuration (two gates, `/ds:propose` + `/ds:implement`, blocked-on-failure) and verify end to end

## 3. Runtime selection hardening (agent-runtime-selection)

- [ ] 3.1 Make the entrypoint honor `runtime` from the message for every action; treat unknown runtime values as failed runs (failure contract, no command executed)
- [ ] 3.2 Verify the same repo-owned command runs under both `claude` and `opencode` from the single image (symlinked commands dir contract)
- [ ] 3.3 Document the runtime knob in the mapping schema docs and `README.md`

## 4. GitLab adapter

- [ ] 4.1 Implement the GitLab adapter: secret-token webhook verification, issue label-event parsing, automation-actor filtering, label swap write-back with single-label invariant, note posting, live state read
- [ ] 4.2 Add GitLab credentials: project/group access token in Key Vault, read via managed identity at execution time; document scope and rotation runbook
- [ ] 4.3 Terraform: new Key Vault secret entries and receiver env/vars for GitLab webhook secret
- [ ] 4.4 Pilot on a scratch GitLab project: full loop (state → dispatch → action → write-back), stale-gate, duplicate delivery, failure→blocked

## 5. Azure DevOps adapter

- [ ] 5.1 Spike: service principal (Entra ID) vs PAT against Work Items + Repos REST APIs; record decision as ADR/design update
- [ ] 5.2 Implement the Azure DevOps adapter: service-hook verification, `workitem.updated` `System.State` parsing, automation-actor filtering, work-item state write-back (configurable target values per process template), discussion comment, live state read
- [ ] 5.3 Add deploy-time validation that configured `native_state`/`on_*_state` values exist on the target project's process template
- [ ] 5.4 Terraform: Key Vault secrets and receiver config for Azure DevOps
- [ ] 5.5 Pilot on a scratch Azure DevOps project: full loop plus the same edge cases as 4.4

## 6. Close-out

- [ ] 6.1 Update `README.md` and `DOCS/products/orquestador-agentico-backlog.md` to reflect the implemented product (vendors supported, mapping config reference, deployment steps per vendor)
- [ ] 6.2 Re-run the full v1 verification checklist on the GitHub path to confirm no regression after generalization
- [ ] 6.3 Resolve open questions in `design.md` (multi-tenancy stance, orchestrator-side WIP guard) and record decisions
