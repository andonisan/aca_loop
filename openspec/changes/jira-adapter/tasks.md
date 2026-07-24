# Tasks — Jira Adapter

## 1. Tracker/code-host separation (all vendors, mechanical)

- [ ] 1.1 Split `IVendorAdapter` in `Orchestrator.Modules.Backlog.Contracts` into `IBacklogAdapter` (`Vendor`, `VerifyWebhook`, `ParseEvent`, `IsAutomationActor`, `ReadItemStateAsync`, `TransitionStateAsync`, `PostCommentAsync`) and `ICodeHostAdapter` (`Vendor`, `GetCloneCredentialsAsync`, plus the CLI-token environment contribution) — see design.md D6
- [ ] 1.2 Add `CodeHost` + `CodeRepo` to `CanonicalEvent` and `CanonicalMessage`, documenting on both records that the tracker coordinates identify the item and the code-host coordinates identify the repository
- [ ] 1.3 Re-declare the GitHub, GitLab, and Azure DevOps modules as implementing both interfaces; their behavior must not change. Verify `ArchTests` and the MOD analyzers stay green — cross-module access remains contracts-only
- [ ] 1.4 Move `ExecutionService`'s vendor `switch` for CLI token env vars onto `ICodeHostAdapter`, and **add the missing Azure DevOps case** found while tracing the seam (ADO targets currently get git credentials via `CloneUrl` and no CLI token at all)
- [ ] 1.5 Update `ExecutionService` to resolve two adapters per message — tracker for gate re-validation and the failure contract, code host for clone credentials and environment — and confirm the existing Execution unit tests still pass against the two-adapter resolution

## 2. Mapping schema: code-host coordinates

- [ ] 2.1 Extend `MappingFile`/`MappingFileTarget` with `codeHost` and `codeRepo`, and `MappingConfig`/`MappingLoader` to default `codeHost` to `vendor` and `codeRepo` to `repo` when absent
- [ ] 2.2 Add startup validation that each target's resolved `codeHost` maps to a registered `ICodeHostAdapter`, failing fast with the offending target named — same fail-fast path as every other mapping violation
- [ ] 2.3 Extend `MappingProvider.TryResolve` so a non-repo-anchored tracker matches on its project coordinate rather than requiring a repo match, without changing resolution for the three repo-anchored vendors
- [ ] 2.4 Prove no migration is needed: load the existing `src/root/Orchestrator.Receiver/mapping.yaml` and the portal's bundled `mappings/dsconnect-dev.mapping.yaml` unchanged and assert identical resolution behavior (design.md Migration Plan step 4 — a task, not an assumption)

## 3. Jira adapter

- [ ] 3.1 Create `src/modules/Jira/Orchestrator.Modules.Jira` (self-registering `IModule`, `Vendor.Jira`) implementing **only** `IBacklogAdapter`; add `"jira"` to `WebhookUseCase.TryParseVendor`
- [ ] 3.2 Implement `VerifyWebhook`: HMAC over the raw body against the configured secret, fixed-time compare, `X-Hub-Signature` parsed in its `method=signature` form with unsupported methods rejected, and a missing header rejected rather than treated as unconfigured (design.md D2)
- [ ] 3.3 Implement `ParseEvent`: accept only `jira:issue_updated`, require a `changelog.items[]` entry with `field == "status"`, take `toString` as `native_state`, the issue **key** as `ItemId` (design.md D7), and the Jira project key as the project coordinate
- [ ] 3.4 Implement `IsAutomationActor` comparing the payload actor's account identifier (not display name) against the configured orchestrator account
- [ ] 3.5 Implement `ReadItemStateAsync` reading the live status by issue key, for gate re-validation
- [ ] 3.6 Implement `TransitionStateAsync` via transition discovery: GET the transitions available from the current status, match the target status name, POST the transition id. Never attempt a status field edit (design.md D5)
- [ ] 3.7 Implement the unreachable-failure-state path: when no transition to the configured `onFailureState` exists, still post the failure-context comment, still record a failed outcome, leave status unchanged, and report the original run failure as the cause rather than the transition failure
- [ ] 3.8 Implement `PostCommentAsync` against the comment endpoint for the configured deployment: Cloud → v3 with a minimal ADF wrapper (`doc → paragraph → text`); Data Center → v2 with a plain-text/wiki-markup body (design.md D11 — ADF does not exist on Data Center)
- [ ] 3.9 Add `JiraOptions` with a single `Deployment: Cloud | DataCenter` setting driving API version, comment format, and auth scheme as one coupled choice (design.md D11), plus base URL, account/email, API or personal access token, webhook secret, and bot account id. The three coupled values MUST NOT be independently settable
- [ ] 3.10 Implement outbound resilience per design.md D12: explicit timeout and `CancellationToken` on every call, `Retry-After`-aware retry with exponential backoff and jitter, bounded attempts, and — critically — exhausted retries on the failure path still recording an outcome
- [ ] 3.11 Add `Orchestrator.Modules.Jira.UnitTests` covering: signature accept/reject/missing-header/unsupported-method; status-change parse; non-status changelog, non-`issue_updated`, and missing-changelog drops; automation-actor filtering; transition selection; the unreachable-failure-transition path; both deployment variants of comment posting; and retry-exhaustion during the failure path. Note that the GitHub adapter still has no unit test project — do not repeat that gap here

## 4. Credentials and infrastructure

- [ ] 4.1 Add Key Vault secrets for the Jira API/personal access token and the webhook HMAC secret. The webhook secret is Terraform-generated (like GitHub's and GitLab's); the Jira token is pushed out-of-band and never enters Terraform state
- [ ] 4.2 Add an `enable_jira` flag mirroring `enable_gitlab`/`enable_azure_devops`, gating the conditional secrets and `Vendors__Jira__*` env vars on both Receiver and Agent; `terraform validate` must pass
- [ ] 4.3 Document the manual prerequisites in `README.md` alongside the GitHub/GitLab/ADO steps: registering an **admin** webhook (admin UI or `/rest/webhooks/1.0/webhook`, **not** `/rest/api/3/webhook` — design.md D2 explains the 30-day expiry this avoids), scoping it to `jira:issue_updated` for the target project, setting the secret, and the `az keyvault secret set` commands
- [ ] 4.4 Document the authorization gate for Jira: who may execute the transition into a gate status is who may spend agent time, which on Jira means workflow permissions and conditions rather than label permissions

## 5. Seed vocabulary catalog and docs

- [ ] 5.1 Record the status vocabularies observed on real client Jira instances as an authoring reference for `mapping.yaml`, with explicit framing that this is a **catalog, not normalization code**, and the note explaining why per-target mapping makes keyword arrays unnecessary (design.md D3)
- [ ] 5.2 Add a Jira example to `mapping.yaml`'s commented sample showing a split tracker/code-host target (`vendor: Jira` + `codeHost: GitHub`)
- [ ] 5.3 Update the vendor status table in `README.md` and the vendor list in `DOCS/products/orquestador-agentico-backlog.md` to four vendors, keeping the honest piloted/unpiloted distinction

## 6. Pilot and close-out

- [ ] 6.1 Pilot on a scratch Jira project: full loop (status change → dispatch → action → write-back), stale gate, duplicate delivery, failure→blocked, and the unreachable-failure-transition case. **Requires a real Jira instance, an API token, and a paired code-host repo** — until this runs, treat the adapter as unpiloted, exactly as GitLab and Azure DevOps are
- [ ] 6.2 Verify against whichever deployment type the pilot uses, and record which half of design.md D11 (Cloud v3/ADF/API-token vs Data Center v2/wiki/PAT) remains theoretical afterwards — the open question in design.md exists to stop this becoming a third silently-unpiloted vendor. Prefer Data Center if only one is available: it is the half with no prior evidence, and the half an ADF assumption breaks
- [ ] 6.3 Exercise the comment path on the piloted deployment **through the failure route specifically** — D11's divergence only shows up when the failure contract runs, so a green happy path proves nothing about it
- [ ] 6.4 Verify Jira's delivery-retry behavior against gate re-validation: confirm a retried delivery arriving minutes later either exits at the stale gate or dispatches legitimately, and document which was observed (design.md Risks)
- [ ] 6.5 Confirm the three existing vendors still behave identically after the D6 split — re-run the GitHub path end to end, and re-check that the GitLab and Azure DevOps unit suites pass unchanged
- [ ] 6.6 Resolve design.md's open questions and record the decisions: pilot deployment type, whether `codeRepo` is ever discovered rather than configured, and whether any existing client pairs Jira with Bitbucket (which would make the Bitbucket code-host change a prerequisite for a real pilot rather than a follow-up)
