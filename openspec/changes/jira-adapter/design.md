# Design — Jira Adapter

## Context

`agentic-backlog-orchestrator` shipped three vendor adapters behind one `IVendorAdapter`, and the seam holds only because GitHub, GitLab, and Azure DevOps are each simultaneously a work-item tracker and a git host. Jira is a tracker and nothing else. Adding it therefore does two things at once: it fills the largest vendor gap, and it forces the tracker/code-host conflation apart.

The evidence base is Plain Concepts' internal Burndowns app (`/Users/andoniplain/projects/plain/PlainConcepts.Bilbao.Burndowns`), which has read live client Jira instances since 2022. It is *read-only*, *polling*, and uses only `rest/agile/1.0` + `rest/greenhopper/1.0` — there is no `rest/api/2` or `rest/api/3` call anywhere in its working tree or its history. So it constrains this design more than it de-risks it (D1).

Two facts about Jira drive most decisions below: an issue has exactly **one** workflow status by construction (a stronger single-state invariant than the label conventions the other three vendors rely on), and that status **cannot be written directly** — it moves only through workflow transitions (D5).

## Goals / Non-Goals

**Goals**
- Jira as a first-class tracker: full loop (status change → dispatch → agent action → write-back), gate re-validation, and the failure contract.
- A compile-time-enforced separation between tracker and code host, so a tracker-only vendor cannot accidentally be asked for clone credentials.
- Zero migration for existing GitHub / GitLab / Azure DevOps mappings.
- Support both Jira Cloud and Jira Data Center, since the client base appears to include at least one on-prem instance (D3).

**Non-Goals**
- Bitbucket as a code host. A Jira target pairs with GitHub, GitLab, or Azure DevOps for now; Bitbucket is a separate change.
- Story points / `customfield_*` estimation. Burndowns reads `fields.timetracking.originalEstimate` as an opaque string and no custom field at all; nothing in this product needs effort yet.
- Sprint, board, or iteration integration. `agentic-backlog-orchestrator` explicitly excludes board-field integration and that stands.
- Jira Service Management request types.
- Any normalization of Jira status vocabularies into a canonical set (D3).

## Decisions

### D1 — Treat Burndowns as a constraint source, not a proven implementation

Mapped against the seven adapter members, Burndowns supplies evidence for two: the auth pattern, and `ReadItemStateAsync` (it reads `fields.status.name` live, though via the agile API rather than the core API). It has **no webhook code at all** — it polls — and it never writes to Jira, so `VerifyWebhook`, `ParseEvent`, `IsAutomationActor`, `TransitionStateAsync`, and `PostCommentAsync` are greenfield. The proposal's "live production evidence" claim is therefore about the *vocabulary and auth*, not the adapter surface.

Explicitly **not** ported: its `catch (InvalidOperationException)` Kanban probe (a 400-vs-401-vs-404 ambiguity used as a feature detector), the `__invalid_name__48x48` codegen fossils that can never bind, the one-member `enum State { Active }`, and its total absence of timeouts, retries, 429 handling, and `CancellationToken`.

### D2 — Webhook authenticity is HMAC-SHA256 over `X-Hub-Signature`, and the secret is mandatory

Both [Jira Cloud](https://developer.atlassian.com/cloud/jira/platform/webhooks/) and [Jira Data Center](https://confluence.atlassian.com/spaces/ADMINJIRASERVER/pages/938846912/Managing+webhooks) sign webhook bodies with HMAC (default SHA-256) and deliver it in an `X-Hub-Signature` header formatted `method=signature` per WebSub — the same shape as GitHub. This was the open risk that could have blocked the change; it does not. Parity is achievable on both deployment types.

Two hardening requirements follow from the details:
- Atlassian makes the secret **optional** at registration. The adapter MUST fail closed: a missing or empty `X-Hub-Signature` is a rejection, never "no secret configured." Fixed-time comparison, as in `GitLabAdapter`.
- The docs warn that "Jira might start using another method for the HMAC in the future." Parse the `method=signature` form and reject unknown methods explicitly, rather than hardcoding a `sha256=` prefix.

**Register webhooks as admin webhooks** (the Jira admin UI, or `POST /rest/webhooks/1.0/webhook`), *not* through `/rest/api/3/webhook`. The latter is restricted to Connect/OAuth 2.0 apps and expires **30 days** from creation or last refresh, requiring a refresh endpoint call to stay alive. Admin webhooks have no stated expiry. Trading a small amount of registration automation for the elimination of an expiry treadmill is the right call for a per-client stack that should run unattended.

### D3 — The observed status vocabulary is a config catalog, not normalization code

Burndowns carries four keyword arrays matching `fields.status.name` case-insensitively and exactly, mapping every client's statuses into one global four-column model. Each "add keywords" commit is a bug report: a client board whose issues fell through to `Unknown` and silently vanished from the UI. The union across three languages:

```
ToDo        To Do · TODO · Backlog · Reopened · Tareas por hacer · Open
InProgress  In Progress · Coding · En curso · DEVELOPING
Test        Test · Ready for QA · VALIDATION PROD · Deployed: QA Ready
            In Review / Deployment · Ready for review · Code review · QA
Done        Testing · Test report pending · Release · Done · Finalizada
            Resolved · Ready to deploy · Cancelled
```

**The reason those arrays exist does not apply here.** This product's mapping is already per-target, so a client's status names go directly into that client's `mapping.yaml` as `native_state` values — no canonical set, no keyword matching, no per-customer branching in code. The vocabulary ships as a documented seed catalog for *authoring* mappings.

One inference worth recording: `Open` / `Resolved` is the Jira **Server/DC** default workflow vocabulary, added in a commit titled for a specific client. That is the basis for the Data Center goal above.

### D4 — Workflow status carries the state, not labels

Jira offers both, and labels would be far easier to write (`update.labels` add/remove, no workflow coupling). Status wins anyway:

| | `status` | `labels` |
|---|---|---|
| single-state invariant | native, enforced by Jira | convention only |
| write-back | transition discovery required | trivial |
| visible on the board | yes | no |

The product's security thesis is that *"the ability to change the tracked state is the authorization gate for spending agent time."* A label the team never looks at is not a gate: humans would drive status, the loop would never fire, and the authorization surface would become invisible. Burndowns corroborates that teams manage status — its entire state derivation reads `fields.status.name`. Accepting a harder write path to keep the gate where humans actually work is the correct trade.

### D5 — Write-back is transition-id discovery, and the failure contract can legitimately fail

Jira does not permit setting status as a field; `PUT /rest/api/3/issue/{issueIdOrKey}` cannot move it. `TransitionStateAsync` must `GET /rest/api/3/issue/{issueIdOrKey}/transitions` (which returns only transitions valid *from the current status*), match the target status name to a transition, then `POST` the same path with `{"transition": {"id": "<id>"}}`.

This makes the write path workflow-dependent in a way no other vendor's is, with a consequence that must be specified rather than discovered in production: **the configured `onFailureState` may not be reachable from the item's current status.** The failure contract cannot be assumed to succeed. When the failure transition is unavailable, the adapter MUST still post the failure-context comment and the run MUST still record its outcome — a story that stays in place with an explanatory comment is a far better outcome than a swallowed error. This is a new failure mode relative to label-based vendors, where the write always succeeds.

Comments go to the deployment's comment endpoint, whose API version and body format are selected by D11 rather than fixed here.

### D6 — Split `IVendorAdapter` into `IBacklogAdapter` + `ICodeHostAdapter`

`ExecutionService` currently drives four different concerns from one `message.Vendor`: gate re-validation and failure write-back (tracker), clone credentials and CLI token env vars (code host). Jira can satisfy the first pair and nothing in the second.

The alternative — keep one interface and have Jira throw `NotSupportedException` from `GetCloneCredentialsAsync` — was rejected: an interface that lies is exactly what the MOD analyzers and `ArchTests` exist to prevent, and the runtime throw would surface as a failed run rather than a configuration error. Splitting makes it a compile-time fact.

Timing is the strongest argument for doing it in this change rather than deferring: nothing has ever been deployed, `agentic-backlog-orchestrator` already declares a breaking internal queue-schema change, and `orchestrator-portal` task 2.5 is already extracting `Dispatch.Contracts`. This seam is as cheap to fix now as it will ever be.

Moving the CLI-token `switch` behind `ICodeHostAdapter` also closes a pre-existing gap found while tracing this: `ExecutionService`'s switch has `GitHub` and `GitLab` cases but **no `AzureDevOps` case**, so ADO targets today get git credentials via `CloneUrl` and no CLI token at all.

### D7 — `ItemId` is the Jira issue key, not the numeric id

`CanonicalEvent.ItemId` is already `string`, and Jira's REST accepts `issueIdOrKey` on every relevant endpoint. The key (`VY-1234`) is legible in logs, in `dispatch.outcome` records, and in the portal, and it is what humans say out loud. Burndowns uses the numeric `id` and models `issue.key` without ever reading it — which is why its UI cannot deep-link a story. That is a gap to avoid, not a precedent.

### D8 — Mapping targets gain `codeHost` / `codeRepo`, defaulted

```yaml
targets:
  - vendor: Jira            # tracker
    project: "DSC"          # Jira project key
    codeHost: GitHub        # where the git repo lives
    codeRepo: "your-org/ds-connect"
    states:
      "Ready for Proposal":
        action: /ds:propose
        runtime: Claude
        onFailureState: "Blocked"
```

`codeHost` defaults to `vendor` and `codeRepo` defaults to `repo`, so every existing GitHub, GitLab, and Azure DevOps mapping remains valid unchanged. Startup validation gains one rule: a target's `codeHost` must resolve to a registered `ICodeHostAdapter`, failing fast like every other mapping violation.

Jira targets are the first that are **not repo-anchored** — they key on project key + status name. `MappingProvider`'s resolution must treat `project` as the primary coordinate for Jira rather than assuming `repo` identifies the target.

### D9 — Status matching is by name, with the id available as an escape hatch

The `jira:issue_updated` changelog gives both: `changelog.items[]` where `field == "status"` carries `from`/`to` (status ids) and `fromString`/`toString` (display names). Names go in the mapping because the file is human-authored and every other vendor keys on a human-readable string; renaming a status in Jira breaks that target's mapping, which is the same exposure a renamed label already creates on GitHub and GitLab. Recording the risk and keeping YAML legible beats opaque numeric ids.

### D10 — Auth is an API token over Basic, per deployment type

Jira Cloud: account email as username, API token as password. Jira DC: a personal access token. Burndowns' one useful auth artifact is its selection rule — username present → Basic, username absent → JWT — which encodes exactly this Cloud-vs-other split. Like GitLab and Azure DevOps (D6 of `agentic-backlog-orchestrator`), Jira has no JIT-credential story, so this is a longer-lived secret in Key Vault, consistent with the documented asymmetry rather than a new exception to it. Which scheme applies is selected by D11, not configured independently.

### D11 — One deployment-type option selects API version, comment format, and auth together

Cloud and Data Center are not the same API with a different hostname, and the differences are correlated rather than independent:

| | Jira Cloud | Jira Data Center |
|---|---|---|
| REST version | `/rest/api/3` | `/rest/api/2` (no v3 exists) |
| comment body | Atlassian Document Format | plain text / wiki markup |
| auth | email + API token, Basic | personal access token |
| webhook HMAC | ✅ `X-Hub-Signature` | ✅ `X-Hub-Signature` |

ADF is a **Cloud-v3-only** feature; Data Center's v2 endpoints take wiki markup. An earlier draft of D5 mandated v3 + ADF unconditionally, which silently contradicted the Data Center goal — worth recording as a caught error, because a Data Center pilot would have failed at the first comment post, inside the *failure* path, where it is hardest to notice.

So the adapter takes a single `Deployment: Cloud | DataCenter` option that selects all three together, rather than three independently-settable options that can be combined into states that do not exist in the world. Only the webhook verification path (D2) is genuinely deployment-independent.

### D12 — Outbound calls are resilient, and the failure contract still completes when they are not

The Jira adapter makes four kinds of outbound call (read status, discover transitions, execute a transition, post a comment), and Jira Cloud rate-limits on a points-based budget: over quota returns `429` with `Retry-After` plus `X-RateLimit-*` headers, and Atlassian's guidance is exponential backoff with jitter, bounded at roughly 4 retries.

The adapter SHALL honor `Retry-After`, back off exponentially with jitter, bound its attempts, and carry an explicit timeout and `CancellationToken` on every call. This is stated as a decision rather than left implicit because D1 lists exactly this absence as an anti-pattern in Burndowns — flagging it there and not specifying it here would have been the same defect one layer up.

The interaction with the failure contract matters more than the retry policy itself: rate limiting is *most* likely during an incident, which is precisely when the failure path runs. If retries are exhausted while posting the failure comment or executing the failure transition, the execution MUST still record its outcome. Resilience must never become a new way to lose a run.

## Risks / Trade-offs

- **Jira retries deliveries up to five times with 5–15 minute randomized backoff.** Gate re-validation already makes duplicate delivery safe, but the retry window is orders of magnitude longer than the other vendors'. A retry landing 15 minutes later can find the item legitimately back in the gate state (a human re-triaged it), and will dispatch again — correctly, but surprisingly. Named because it is the one place Jira's delivery semantics can produce a duplicate the invariant treats as valid work.
- **The failure contract can fail** (D5). New failure mode; specified rather than left to discovery.
- **Status renames silently break a target's mapping** (D9), the same class of exposure as a renamed gate label.
- **Split-trust targets**: a Jira loop needs a tracker credential and a code-host credential from two different vendors, doubling the credential surface per target and the blast radius of a leak.
- **Concurrency and rate limits**: Jira Cloud limits webhook delivery to ~20 concurrent requests per tenant + webhook-URL-host pair (10 for secondary/bulk flows) and drops payloads over 25 MB; outbound REST calls are separately governed by a points-based quota returning `429`. Both are documented as changeable without notice, so D12's backoff is a requirement rather than a tuning exercise.
- **Deployment type is a correctness variable, not a configuration detail** (D11). Cloud and Data Center diverge on API version, comment format, and auth simultaneously, and the divergence is only exercised on the failure path — a wrong setting fails where it is least visible.
- **The authorization gate is now Jira's workflow permissions and conditions**, not a label permission. Who may execute the transition into a gate status *is* who may spend agent money — a different and generally more restrictive model than GitHub triage, which is favourable but must be verified per client.
- **Two breaking internal changes in one change** (queue schema, adapter interface). Acceptable only because nothing is deployed; this would be two changes if anything were live.

## Migration Plan

Nothing is deployed, so there is no data or live-traffic migration. Ordering constraints:

1. `agentic-backlog-orchestrator` should archive **before** this change, so the `backlog-abstraction` and `state-action-config` deltas apply against a real `openspec/specs/` baseline rather than against a sibling change's files.
2. The D6 interface split lands before the Jira adapter, as one mechanical refactor across the three existing adapters with `ArchTests` green, so that Jira is purely additive on top of a correct seam.
3. `orchestrator-portal` task 2.5 (`Dispatch.Contracts` extraction) touches the same contracts layer. Whichever lands second rebases; they should not run concurrently.
4. Existing mappings need no edit. Verifying that is a task, not an assumption.

## Open Questions

- **Which client's Jira gets piloted first, Cloud or Data Center?** D2's HMAC story covers both, but D11 shows the rest of the surface — API version, comment format, auth — forks entirely, and the `Open`/`Resolved` signal suggests the on-prem instance is the more realistic pilot. Whichever is not piloted stays theoretical, which is the same trap that left the Azure DevOps adapter unpiloted. If only one can be piloted, pilot Data Center: it is the half with no evidence behind it and the half an ADF assumption would silently break.
- **Is `codeRepo` always configured, or ever discovered?** D8 assumes configured. Jira's development-information panel can associate repos with issues, but relying on it would make dispatch depend on a separate integration being healthy. Configured is the safer default; worth confirming nobody expects auto-discovery.
- **Does any existing client actually pair Jira with Bitbucket?** If most Jira clients are Bitbucket clients, the D6 split is necessary but insufficient and the Bitbucket code-host change becomes the real blocker for a Jira pilot. This should be answered before scheduling work, not after.
