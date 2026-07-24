# Portal Design System — adopt Atlas's visual language as the portal's UI layer

## Why

`orchestrator-portal` specifies *what* the portal shows (gate views, queue/pod status, run history) and *who* may see it, but says nothing about how any of it looks. The host as built reflects that: `MainLayout.razor` is a bare `<header>`/`<main>`, `Home.razor` renders an unstyled `<table>`, and there is no stylesheet in the project at all. Every page from tasks 3.x onward would otherwise invent its own layout, its own idea of "this run failed", and its own spacing — and the first four pages would already disagree.

Plain Concepts already runs an internal operator portal with a solved answer: **Atlas** (`atlas.plainconcepts.com`). It is the tool these same operators use to look at these same Azure DevOps projects, and it is built exactly the way this portal can consume — hand-authored plain CSS over a token layer, no framework, no build step (verified 2026-07-24 against its published stylesheet: 122 custom properties, ~280 component classes, OKLCH palette on a single indigo hue, Geist/Geist Mono). Adopting it makes the portal look like the rest of the operators' toolchain instead of like a different company's product, and costs one vendored file rather than a component-library dependency.

Direction chosen: **vendor a snapshot of Atlas's token + primitive layers into the portal**, extend it with the handful of components the orchestrator domain needs, and pin a fixed mapping from domain state (gate state, run outcome, queue health) to visual treatment so page authors do not choose colours.

## What Changes

- Add `src/root/Orchestrator.Portal/wwwroot/css/atlas.css` — the vendored design system: token layer (light + dark), reset, app shell, navigation, surfaces, data display, controls, utilities. Plain CSS, served through the existing `MapStaticAssets()` pipeline with `@Assets[]` fingerprinting.
- Rebuild `MainLayout.razor` as the Atlas **app shell**: sidebar (brand, stack navigation, user card) + sticky topbar (breadcrumbs, page title, actions) + `.page` content well. The existing role-gating logic is preserved exactly — this change is visual, not authorization.
- Add a **portal-specific layer** (`portal.css`) for what Atlas has no equivalent of: run timeline, log-tail viewer, gate-state strip. Built from the same tokens, never from raw colour values.
- Pin **domain-state semantics** in one place (a small C# helper plus the badge classes it returns): run outcome `succeeded|failed|stale-gate|poison` → `ok|danger|warn|violet`, queue-depth thresholds → `ok|warn|danger`, pod state → `info|ok|neutral`. Pages ask for the state's badge; they do not pick a colour.
- Restyle the existing `Home.razor` stack list onto `.dtable` as the first consumer and the reference example.
- Document the extraction: provenance (source asset + retrieval date), what was carried over vs. re-authored, and the re-sync procedure when Atlas moves.

## Capabilities

### New Capabilities
- `portal-design-system`: the visual contract — the token layer as the only source of colour/type/spacing, the fixed domain-state-to-treatment mapping, theme behaviour (explicit choice over OS preference), the accessibility floor (never colour alone, visible focus), and the provenance/re-sync obligation that keeps the snapshot honest.

### Modified Capabilities
<!-- none. `portal-read-model`, `portal-operations`, and `portal-auth` are untouched: this change adds a
     presentation layer beneath them and does not alter what is read, what verbs exist, or who may act. -->

## Impact

- **Code**: `Orchestrator.Portal` gains `wwwroot/css/`; `App.razor` gains the stylesheet + font links; `MainLayout.razor` is rewritten (auth logic unchanged); `Home.razor` restyled. No new packages, no NPM, no build step — the MOD analyzers and ArchTests are unaffected because nothing crosses a module boundary.
- **Infra**: none. No new resources; static assets ship in the existing container image.
- **Security surface**: one new outbound dependency — `fonts.googleapis.com` for Geist. The font stack falls back to `system-ui`, so a CSP that blocks it degrades type only. Self-hosting under `wwwroot/fonts` is the documented alternative if the portal's CSP forbids third-party origins.
- **Cross-team dependency**: Atlas is owned by another team. This change vendors a snapshot of their CSS, which needs their awareness and an agreed re-sync expectation — see the design's open questions. Nothing breaks if they never engage, but the snapshot goes stale silently.

## Out of Scope

- Any change to what the portal reads, which verbs exist, or the auth model.
- Publishing Atlas's design system as a shared package (NuGet/NPM) for both apps to consume — the right end state, but it means owning another team's release process for one consumer; revisit once a second consumer exists.
- Visual regression testing / screenshot diffing.
- A component library of reusable Blazor components wrapping these classes — the classes are the interface for now; extract components when a pattern has three real callers.
- Client-facing theming or per-tenant branding (the portal is internal-only by `portal-auth`).
