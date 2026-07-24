# ADR-0006: One agent image; the runtime is selected per work item

- **Status:** Accepted
- **Date:** 2026-07-24
- **Deciders:** Andoni Santamaria
- **Tags:** agent, infra

## Context

We want to compare agent CLIs (Claude Code vs opencode) on cost and quality without duplicating infrastructure. Both CLIs read the same command files from the target repo (`.claude/commands`, symlinked as `.opencode/commands`), so which one runs is pure configuration, not an environment difference. The toolchain around them (git, `gh`, Node) dwarfs the CLIs themselves.

## Decision

We will build one agent image containing git, `gh`, Claude Code, and opencode. The work item's `agent_runtime` field picks the CLI per execution; the receiver currently stamps it from the `DEFAULT_AGENT_RUNTIME` Terraform variable (`claude` or `opencode`). The entrypoint honours whatever the message says, so richer selection (e.g. an `agent:*` label resolved by the receiver) needs no image or job changes.

## Consequences

- **Positive:** one image to build, scan, and pin; switching runtimes is a variable change or a message field, not a deploy; A/B comparison is trivial.
- **Negative:** the image is fat (~two CLIs plus toolchain); either CLI's supply-chain surface is present even when unused.
- **Neutral:** per-issue runtime selection via label is designed-for but not yet wired — the receiver always uses the default today.

## Alternatives considered

- **Image-per-runtime with separate jobs** — rejected: doubles the infrastructure for no isolation benefit; the CLIs are small next to the shared toolchain.
- **Runtime baked in at deploy time only** — rejected: forecloses per-run comparison, which is half the point of supporting two CLIs.

## References

- Source decision: D6 in `openspec/changes/ai-loop-aca-job/design.md`
- Implementation: `agent/Dockerfile`, `agent/src/entrypoint.js`
