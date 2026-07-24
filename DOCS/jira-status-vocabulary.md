# Jira status vocabulary observed on real Plain Concepts instances

**This is a catalog for authoring `mapping.yaml`, not normalization code.** Nothing in this
orchestrator reads it. It exists because Jira status names are per-workflow, per-client, and
frequently not in English, and because guessing them wrong is the single easiest way to configure a
Jira target that silently never fires.

## Where these come from

`PlainConcepts.Bilbao.Burndowns` — an internal Blazor app that has read live client Jira boards since
2022, alongside GitLab and Azure DevOps. Its `JiraService` holds four keyword arrays mapping status
names onto a four-column board. Each "add keywords" commit in its history is a bug report: a client
board whose issues fell through to `Unknown` and silently vanished from the UI. So the list below is
not documentation-derived — it is the accumulated result of four years of real boards breaking.

The union of those arrays, grouped as that app grouped them:

| Rough phase | Status names seen in the wild |
|---|---|
| To do | `To Do` · `TODO` · `Backlog` · `Reopened` · `Tareas por hacer` · `Open` |
| In progress | `In Progress` · `Coding` · `En curso` · `DEVELOPING` |
| Review / QA | `Test` · `Ready for QA` · `VALIDATION PROD` · `Deployed: QA Ready` · `In Review / Deployment` · `Ready for review` · `Code review` · `QA` |
| Done | `Testing` · `Test report pending` · `Release` · `Done` · `Finalizada` · `Resolved` · `Ready to deploy` · `Cancelled` |

Three things worth reading off that table:

- **Three languages.** `Tareas por hacer`, `En curso`, `Finalizada` are Spanish; the rest English.
  Casing varies (`TODO`, `DEVELOPING`, `VALIDATION PROD`) because those are transcriptions of what
  was actually on the board.
- **`Open` / `Resolved`** is the Jira **Server/Data Center** default workflow vocabulary, added in a
  commit named for a specific client. That is the evidence behind this product supporting Data Center
  at all, and the reason `jira_deployment` exists.
- **`Testing` is "done" while `Test` is "review".** Adjacent words, opposite phases, on different
  clients' boards. Do not infer a status's meaning from its name.

## Why this orchestrator needs no keyword arrays

The Burndowns app has *one* global four-column model, so every client's vocabulary has to be squeezed
into it — hence four years of whack-a-mole, and hence per-customer status names flattened into shared
global arrays where one client's vocabulary applies to every client's board.

This product's mapping is **per target**. A client's status names go straight into that client's
`mapping.yaml` as `native_state` keys:

```yaml
  - vendor: Jira
    project: "VY"
    codeHost: GitHub
    codeRepo: "acme/widgets"
    states:
      "Open":        { action: /ds:propose,   runtime: Claude, onFailureState: "Blocked" }
      "Code review": { action: /ds:implement, runtime: Claude, onFailureState: "Blocked" }
```

No canonical set, no keyword matching, no per-customer branching in code. The variance that forced
those arrays into existence is *designed for* here rather than solved — which is why this file is a
reference and not a source file.

## Practical notes when authoring a Jira target

- **Read the names off the workflow, do not guess them.** `GET /rest/api/3/status` for the project,
  or the workflow editor, is authoritative. A `native_state` that does not exactly match a status name
  produces a target that validates fine and never dispatches.
- **Match is case-insensitive but otherwise exact.** No trimming, no substring. A trailing space in
  the Jira status name will not match.
- **The `onFailureState` must be reachable by a transition from the gate status.** Jira status moves
  only through transitions valid from where the item already is, so an `onFailureState` that no
  transition reaches means the failure comment is posted and the status is left unchanged. Check the
  workflow, not just the status list.
- **Renaming a status in Jira breaks that target's mapping**, the same exposure a renamed gate label
  creates on GitHub or GitLab. The status *id* is stable where the name is not; the mapping keys on
  the name because the file is human-authored and every other vendor keys on a readable string.
