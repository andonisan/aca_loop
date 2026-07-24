# ADR-0001: Dispatch agents via webhook→queue→Container Apps Job, not GitHub Actions workflows

- **Status:** Accepted
- **Date:** 2026-07-24
- **Deciders:** Andoni Santamaria
- **Tags:** infra, github, azure

## Context

When an issue reaches a machine-gate label, something must launch a headless coding agent against that issue. GitHub offers a native-looking path — an `on: issues: types: [labeled]` Actions workflow — and Azure offers an event-driven path mirroring the existing ds3d `github-runner` stack: a webhook receiver enqueues work to a Storage Queue, and KEDA scales an ephemeral Container Apps Job execution per message.

Actions workflows would have to exist in every target repo, would run on the CI runner image (coupling the agent toolchain into it), and inherit Actions-shaped execution limits. Agent sessions are long (an hour is normal) and their toolchain (two agent CLIs, `gh`, git) is unrelated to the CI toolchain.

## Decision

We will dispatch agent runs through a webhook receiver → Storage Queue → KEDA-scaled Container Apps Job, reusing the event→queue→ephemeral-container pattern proven by the `github-runner` stack. One GitHub App install covers the org; target repos need no workflow files; the agent image evolves independently of CI.

## Consequences

- **Positive:** org-wide with zero per-repo setup; scale-to-zero, pay per agent run; agent toolchain and timeouts sized for agent work, not CI; proven operational idioms carried over (poison guard, overscale-is-normal).
- **Negative:** more moving parts than a workflow file (receiver, queue, job, identities); one always-on receiver replica as fixed cost.
- **Neutral:** a throwaway Actions-workflow spike remains a valid cheap experiment for other orgs; nothing here precludes it.

## Alternatives considered

- **`on: issues` GitHub Actions workflow on the self-hosted runners** — rejected: per-repo duplication, agent toolchain baked into the CI runner image, Actions-shaped timeouts for hour-long agent sessions.
- **Always-on poller (no webhooks)** — rejected: pays for idle, adds polling latency, and needs broader API permissions than webhook push.

## References

- Source decision: D1 in `openspec/changes/ai-loop-aca-job/design.md`
- Related: [ADR-0004](./0004-standalone-stack-with-sibling-receiver.md)
