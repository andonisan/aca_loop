# Tasks — ai-loop-aca-job

## 1. De-risking spike (before any infra)

- [ ] 1.1 Run `/ds:propose` headlessly (`claude -p`) in a fresh local container clone of ds-connect against a scratch issue at `status:ready-for-proposal`; record every prompt/stall/permission ask
- [ ] 1.2 Repeat with `opencode run`; note behavioral differences
- [ ] 1.3 Determine `/ds:implement`'s WIP-limit refusal behavior (exit code, output) so the entrypoint can distinguish it from real failure
- [ ] 1.4 Decide go/no-go and record findings + any command fixes needed in ds-connect as design.md updates

## 2. GitHub App (manual prerequisites)

- [ ] 2.1 Create the AI-loop GitHub App: Issues R/W, Contents R/W, Pull Requests R/W; subscribe to Issues events; webhook URL placeholder
- [ ] 2.2 Generate the App private key and note App ID / Installation ID; install on ds-connect only
- [x] 2.3 Document the manual steps in the README (mirroring the runner README's prerequisites section)

## 3. Terraform

- [x] 3.1 Add `ai-jobs` Storage Queue to the shared storage account *(standalone stack per revised D4: own storage account)*
- [x] 3.2 Add user-assigned identity for the agent job + role assignments (queue consume, scaler queue read, Key Vault secrets read, ACR pull)
- [x] 3.3 Add Key Vault secret placeholders/vars for App private key, webhook secret, `ANTHROPIC_API_KEY`, opencode provider keys (values pushed out-of-band)
- [x] 3.4 Add `ai-agent` Container Apps Job: event trigger, KEDA azure-queue rule on `ai-jobs` via managed identity, `replica_retry_limit = 0`, independent `replica_timeout_seconds` (default 3600), env wiring
- [ ] 3.5 Add variables/outputs; `terraform plan` clean against a scratch resource group *(variables/outputs done, `terraform validate` clean; real `plan` needs Azure credentials)*

## 4. Agent image

- [x] 4.1 Dockerfile: base + git + `gh` + Claude Code + opencode
- [x] 4.2 Entrypoint: dequeue one message / exit 0 on empty; gate re-validation against live issue label
- [x] 4.3 Entrypoint: mint installation token from Key Vault-held App key; clone target repo
- [x] 4.4 Entrypoint: runtime selection from message; headless gate-command invocation with max-turns and allowed-tools caps
- [x] 4.5 Entrypoint: failure trap — flip issue to `status:blocked` + comment with failure context; WIP-refusal path keeps gate label; delete message per contract; arm trap before `replica_timeout_seconds`
- [ ] 4.6 Build and push to ACR *(local validation build done; push needs the registry)*

## 5. Webhook receiver (standalone sibling per revised D4)

- [x] 5.1 `issues` event route: HMAC verify against the AI-loop App's secret, filter `action == "labeled"` + gate labels, drop own-bot sender
- [x] 5.2 Enqueue `{repo, issue_number, gate_label, agent_runtime}` to `ai-jobs`
- [ ] 5.3 Build/push receiver image; deploy *(local validation build done; push/deploy needs the registry + Azure)*

## 6. End-to-end verification

- [ ] 6.1 Webhook "Redeliver" of a gate-label event → message appears on `ai-jobs` queue
- [ ] 6.2 Live run: label a scratch issue `status:ready-for-proposal` → execution scales out → draft PR opens → label advances to `status:proposal-review`
- [ ] 6.3 Edge: stale gate label (re-label away before execution starts) → execution exits 0, no LLM calls
- [ ] 6.4 Edge: kill an execution mid-run → issue ends at `status:blocked` with a comment
- [ ] 6.5 Edge: duplicate delivery → second execution exits 0 at gate re-validation
- [ ] 6.6 Edge: WIP-limit refusal → gate label retained, explanatory comment posted
- [ ] 6.7 Review loop: request spec changes on the draft PR, re-label to `status:ready-for-proposal` → agent revises using review comments
