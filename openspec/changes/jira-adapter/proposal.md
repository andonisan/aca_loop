# Jira Adapter — fourth backlog vendor, and separating tracker from code host

## Why

Vendor coverage is the product's whole value proposition ("each team/client lives on a different vendor"), and Jira is the largest remaining gap. It is also the gap with the most evidence behind it: Plain Concepts' internal [`PlainConcepts.Bilbao.Burndowns`](https://burndowns.azurewebsites.net/) app has read live Jira boards for real client teams since 2022, alongside GitLab and Azure DevOps. Coverage across the two codebases is nearly complementary:

| | GitHub | GitLab | Jira | Azure DevOps |
|---|---|---|---|---|
| **Burndowns** (live, read-only) | — | ✅ live | ✅ live | ✅ live |
| **this orchestrator** | ✅ shipped | ⚠️ unpiloted | ✗ absent | ⚠️ unpiloted |

Jira is the only vendor where we have production evidence and no code at all. That evidence is narrower than it first appears — Burndowns *polls*, never writes, and uses only `rest/agile/1.0` + `rest/greenhopper/1.0` — so it de-risks roughly two of the seven adapter members (see design.md D1). What it does supply is the thing docs cannot: the **observed status vocabulary** across real client instances, in three languages, with per-entry git provenance (design.md D3).

Adding Jira also forces a latent design flaw into the open. `IVendorAdapter` bundles tracker concerns (`VerifyWebhook`, `ParseEvent`, `IsAutomationActor`, `ReadItemStateAsync`, `TransitionStateAsync`, `PostCommentAsync`) with one code-host concern (`GetCloneCredentialsAsync`), and `ExecutionService` drives both from a single `message.Vendor`. That holds only because GitHub, GitLab, and Azure DevOps each happen to be *both* a tracker and a git host. **Jira is not a git host** — a Jira issue does not belong to a repository, so `CanonicalEvent.Repo` (non-nullable, documented as "the repository the item belongs to") has nothing to bind to. The conflation must be separated for Jira to exist at all, and separating it is cheapest now: nothing has ever been deployed, `agentic-backlog-orchestrator` already declares one breaking internal queue-schema change, and `orchestrator-portal` task 2.5 is already reshaping the contracts layer.

## What Changes

- **Separate tracker from code host** in the adapter seam: split `IVendorAdapter` into `IBacklogAdapter` (the six tracker members) and `ICodeHostAdapter` (`GetCloneCredentialsAsync` plus the CLI-token environment contribution). GitHub, GitLab, and Azure DevOps implement both; Jira implements only `IBacklogAdapter`. The boundary becomes compile-time rather than a runtime `NotSupportedException`.
- **Carry code-host coordinates on the canonical model**: `CanonicalEvent` and `CanonicalMessage` gain `CodeHost` + `CodeRepo`, so `ExecutionService` resolves two adapters (tracker for gate re-validation and failure write-back, code host for clone credentials and CLI tokens) instead of one.
- **Extend the mapping schema** with per-target `codeHost` / `codeRepo`, defaulting `codeHost` to `vendor` and `codeRepo` to `repo`. Every existing GitHub/GitLab/Azure DevOps mapping stays valid byte-for-byte — no migration.
- **Add the Jira adapter** (`Vendor.Jira`, `/webhooks/jira`): webhook authenticity per design.md D2, `jira:issue_updated` parsing with a `changelog`-based status-change filter, workflow **status** as the state carrier (not labels — design.md D4), transition-id discovery for write-back, comment posting, and live status read for gate re-validation.
- **Support Jira Cloud and Jira Data Center behind one deployment-type setting** that selects REST API version, comment body format, and auth scheme together — they diverge in lockstep, and Atlassian Document Format does not exist on Data Center (design.md D11).
- **Specify outbound resilience**: `Retry-After`-aware retry with exponential backoff and jitter, explicit timeouts and cancellation, and the guarantee that exhausted retries on the failure path still record a run outcome (design.md D12).
- **Use the Jira issue key** (`VY-1234`) as `ItemId`, not the numeric id — legible in logs and the portal, and accepted everywhere Jira's REST takes `issueIdOrKey`.
- **Ship a seed mapping catalog** recording the status vocabularies observed on real client Jira instances, as authoring input for per-target mappings — deliberately *not* as normalization code (design.md D3).
- **Terraform**: an `enable_jira` flag mirroring `enable_gitlab` / `enable_azure_devops` — conditional Key Vault secrets and receiver/agent env vars.
- **Fill the Azure DevOps CLI-token gap** found while tracing the seam: `ExecutionService`'s vendor `switch` has no `AzureDevOps` case, so ADO targets get git credentials via `CloneUrl` but no CLI token env var. Moving that switch onto `ICodeHostAdapter` closes it as a side effect.
- **BREAKING** (internal, twice over): the queue message schema gains code-host fields, and `IVendorAdapter` ceases to exist as a single interface. Receiver, Agent, and all three existing adapters change together. Nothing is deployed, so there is no live migration.

## Capabilities

### New Capabilities
- `jira-backlog-adapter`: the Jira tracker adapter — webhook authenticity and `changelog`-filtered status-change parsing inbound; transition-id-discovered status write-back, comment posting, and live status read outbound; issue-key identity; automation-actor self-filtering by `accountId`.

### Modified Capabilities
- `backlog-abstraction`: adds the tracker/code-host separation — the split interfaces, the code-host coordinates on the canonical event and message, and the requirement that a tracker-only vendor is expressible without a code-host implementation.
- `state-action-config`: adds `codeHost` / `codeRepo` to the target schema, with defaults preserving existing mappings, and startup validation that a target's `codeHost` resolves to a registered `ICodeHostAdapter`.

> **Baseline caveat**: `openspec/specs/` is empty — `backlog-abstraction` and `state-action-config` currently exist only inside the unarchived `agentic-backlog-orchestrator` change. The two "modified" deltas above are written against that change's spec files, so **`agentic-backlog-orchestrator` must archive before this change** for the deltas to apply against a real baseline. If it does not, these become additions to the same capability files instead.

## Impact

- **Code**: new `src/modules/Jira/Orchestrator.Modules.Jira` (+ `Orchestrator.Modules.Jira.UnitTests`); `Backlog.Contracts` splits the interface and extends both canonical records; `Dispatch` extends `MappingFile`/`MappingConfig`/`MappingLoader`/`MappingProvider` and `WebhookUseCase`'s vendor parse; `Execution`'s `ExecutionService` resolves two adapters and moves its CLI-token `switch` behind `ICodeHostAdapter`; the GitHub, GitLab, and Azure DevOps adapters re-declare which interfaces they implement.
- **Infra (`terraform/`)**: `enable_jira` flag; Key Vault entries for the Jira API token and whatever webhook-authenticity material design.md D2 settles on; `Vendors__Jira__*` env vars on Receiver and Agent.
- **Config surface**: the mapping file gains two optional fields. Jira targets key on **project key + status name**, where the other three key on repo + label/state — the first target shape that is not repo-anchored.
- **Security surface**: Jira's webhook authenticity story is materially weaker than the other three vendors' (design.md D2 and Risks) — this is the change's principal risk, not an implementation detail. Jira also introduces the first split-trust target: a tracker credential and a code-host credential from two different vendors, for one loop.
- **Target repo contract**: unchanged. Repos still own their agent commands. The single-state invariant holds *more* strongly on Jira than on the label-based vendors, since a Jira issue has exactly one workflow status by construction.
- **Not in scope**: Bitbucket as a code host (a Jira target must pair with GitHub, GitLab, or Azure DevOps for now); Jira story points / `customfield_*` estimation; Jira Service Management; sprint or board-field integration, which remains an explicit non-goal of `agentic-backlog-orchestrator`.
