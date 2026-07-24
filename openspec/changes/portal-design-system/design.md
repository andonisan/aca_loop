# Design — Portal Design System

## Context

`orchestrator-portal` (D1) fixes the portal as `Orchestrator.Portal`, a Blazor Server host rendering server-side HTML with interactive server components, polling per-source read models. It specifies the data and the auth model and is deliberately silent on presentation. The host as implemented has no stylesheet: `MainLayout.razor` is a bare `<header>`/`<main>` wrapping an `AuthorizeView`, and `Home.razor` renders an unstyled table of registered stacks.

Everything the portal must display is dense operational data: tables of items in gate states, numeric queue depths, run durations, outcome distributions, log tails. That is the same shape of problem — and for the same operators, over some of the same Azure DevOps projects — that **Atlas** already solves internally. Atlas's stylesheet is publicly served, unminified in structure, and built without a framework: a `:root` token block (122 custom properties), a `[data-theme=dark]` override, and ~280 hand-authored component classes over an OKLCH palette derived from a single indigo hue (258). Inspected 2026-07-24 from `atlas.plainconcepts.com/assets/index-DiydWycu.css`.

Blazor Server emits plain semantic HTML. A class-based plain-CSS system therefore attaches to it with no adapter, no wrapper components, and no JS interop — which is what makes adoption cheap enough to be worth doing at all.

## Goals / Non-Goals

**Goals:**
- The portal is visually continuous with Atlas: an operator moving between them sees one toolchain, not two products.
- One source of colour, type, spacing, and elevation — the token layer. No raw hex or px values in page markup or page-specific CSS.
- Every orchestrator domain state has exactly one visual treatment, defined once, so "failed" looks identical on the dashboard, the stack view, and the run history.
- Zero build step and zero new package dependencies; the system is a static asset served by the pipeline already in `Program.cs`.
- A dark theme that works, because operators watching queues at 02:00 are the audience.

**Non-Goals:**
- Pixel-identity with Atlas. Continuity of language, not a clone; the portal has components Atlas does not and vice versa.
- Carrying over all ~280 Atlas classes. Only what the portal uses.
- A Blazor component library wrapping the CSS (premature — the classes are the interface until a pattern has three callers).
- Shared-package extraction across both apps (see D3 and Open Questions).
- Visual regression testing.

## Decisions

### D1 — Adopt Atlas's plain-CSS token system, not a Blazor component framework
The UI layer is a hand-authored stylesheet over CSS custom properties — no MudBlazor, no Fluent UI Blazor, no Tailwind.
*Rationale*: the three candidates each impose a component model on markup, and none of them look like Atlas, so adopting one means either living with a foreign look or fighting the framework's theming to approximate the tokens anyway. Atlas has already paid the design cost, and its output happens to be the one format Blazor consumes for free. A framework also adds a dependency the MOD/ArchTest conventions must tolerate and a version treadmill for an internal tool with four pages.
*Alternatives considered*: **Fluent UI Blazor** — closest to "looks like Azure", and the portal is an Azure operations tool, so this is the genuine runner-up; rejected because the operators' reference point is Atlas, not the Azure portal, and it would still need theming to match. **Tailwind** — needs a Node build step in a .NET-only repo for a system that is already written as plain CSS. **From scratch** — reinvents a solved palette worse.

### D2 — Copy the token layer verbatim; re-author the component layer selectively
The `:root` and `[data-theme=dark]` blocks are transcribed exactly, token names included. The component layer is re-authored: expanded from Atlas's minified/property-shuffled output into readable rules, commented where a rule encodes a non-obvious decision, and filtered to the ~60 primitives the portal needs (shell, sidebar/nav, tabs, card, stat card, `.dtable`, badges/tags, buttons, inputs, filter chips, bar rows, utilities).
*Rationale*: the tokens are the part that must match exactly — they are what makes it recognisably Atlas, and they are also the stable part of any design system. The component CSS is where Atlas's own app-specific choices live (App Hub tiles, document preview, chat panel, risk matrices), most of which the portal will never render. Transcribing all of it would import dead CSS and imply a fidelity commitment the next Atlas release would break.
*Consequence*: identical token names mean markup is portable in both directions — a snippet lifted from Atlas works here, and vice versa.

### D3 — Vendored snapshot with recorded provenance, not a shared package
`atlas.css` lives in the portal's `wwwroot` with a header naming its source asset and retrieval date. Re-sync is a deliberate, reviewed act: diff the current Atlas token block against ours, apply, eyeball the primitives.
*Rationale*: Atlas is another team's app in another repo with no published design artifact. The correct end state is a package both consume, but creating one means owning Atlas's release process to serve exactly one external consumer — cart before horse. A vendored snapshot with honest provenance is the cheap version that does not pretend to be more coupled than it is.
*Accepted cost*: the snapshot drifts silently. Tokens drift slowly (that is their nature); the failure mode is "slightly stale", not "broken". Escalate to a package if a second consumer appears.
*Alternative considered*: `@import` Atlas's stylesheet URL at runtime — rejected outright: it makes the portal's rendering depend on another app's deploy, inherits every unrelated class, and breaks the moment their asset hash changes.

### D4 — Domain state → visual treatment is a fixed mapping in code, not a per-page choice
A small helper in the portal maps orchestrator states to badge classes, and pages call it rather than writing `class="badge danger"` inline:
- **Run outcome** (the `dispatch-outcome` event of `orchestrator-portal` D3): `succeeded` → `ok`, `failed` → `danger`, `stale-gate` → `warn`, `poison` → `violet`.
- **Queue depth**: `ok` under the healthy threshold, `warn` approaching KEDA's scale ceiling, `danger` above it.
- **Pod execution**: `Running` → `info`, `Succeeded` → `ok`, `Stopped`/cancelled → `neutral`, `Failed` → `danger`.
*Rationale*: colour is the fastest read on an operations dashboard and worthless if inconsistent. `stale-gate` deserving `warn` rather than `danger` — the run correctly declined to act — is exactly the judgement that must be made once, centrally, instead of re-litigated by whoever writes the next page. `poison` gets `violet` because it is categorically different from a failed run: it means redelivery gave up, and it must not blend into the failure count.
*Consequence*: adding a domain state means touching the mapping, which is the point — a new outcome cannot render as an unstyled string.

### D5 — Theme follows the OS until the operator overrides it (deviation from Atlas)
Atlas switches on `[data-theme=dark]` only, which means a first visit is always light. The portal keeps that attribute as the override and adds a `prefers-color-scheme: dark` block guarded by `:root:not([data-theme="light"])`, so the OS preference applies until an explicit choice exists.
*Rationale*: the audience watches this thing at night; defaulting a dark-mode user to a white screen is a bad first impression for no gain. The attribute still wins in both directions, so an explicit toggle (whenever it ships) behaves as Atlas's does.
*Cost*: the dark token block is duplicated between the attribute selector and the media query. Plain CSS has no way to alias one selector's declarations to another without a preprocessor, and adding a build step (D1) to save 45 duplicated lines is the worse trade. The duplication is adjacent and commented.

### D6 — Geist from the Google Fonts CDN, with the fallback treated as a supported state
Atlas loads Geist/Geist Mono via `@import` from `fonts.googleapis.com`. The portal uses `<link>` with `preconnect` (same source, better for the critical path) and treats the `system-ui` fallback in `--font-sans` as a first-class outcome rather than a bug.
*Rationale*: type is the one token group that degrades gracefully — a blocked font changes the letterforms, not the layout, because every size/weight/spacing value is a token independent of the family. That makes the CDN acceptable for an internal tool without knowing the final CSP.
*Escape hatch*: if the portal's CSP forbids third-party origins, self-host the two families under `wwwroot/fonts` and drop the links; nothing else changes. Documented in the stylesheet header, not left as folklore.
*Note*: `.mono` sets `font-feature-settings: "tnum"`. Tabular figures are load-bearing here — it is why columns of queue depths and durations align — and the fallback mono stack supports it.

### D7 — Shared primitives are global CSS; Blazor CSS isolation is for component internals only
These classes live in a global stylesheet, not in `.razor.css` files.
*Rationale*: scoped styles rewrite selectors per component, which is the precise opposite of a shared vocabulary — `.badge` must mean one thing everywhere. Isolation stays available and appropriate for a component's genuinely private internals (a chart's own positioning), and that boundary is worth stating before someone helpfully "fixes" the global file by scoping it.

### D8 — Accessibility floor: colour is never the only signal
Badges pair their fill with a text label, and `.badge .dot` exists so a status reads as shape-plus-text, not hue alone. Every interactive primitive keeps a visible `:focus-visible` treatment (`--sh-focus` ring or a brand outline); none of the transcribed rules removes an outline without replacing it.
*Rationale*: the palette is 8 hues at similar lightness — legible to most, ambiguous under colour-vision deficiency, and the states it encodes (failed vs. stale-gate) drive operator action.
*Not claimed*: Atlas's own contrast ratios are not independently verified, particularly `--fg-subtle` and `--fg-muted` on soft fills. Inherited as-is; a contrast audit is listed as a task, not asserted as done.

## Risks / Trade-offs

- [Vendored snapshot drifts from Atlas; the portal slowly stops matching] → provenance header records source asset + date; re-sync is a documented, small diff (tokens are the stable layer); worst case is cosmetic, never functional.
- [Atlas's team is unaware their CSS is being reused, and may restructure or object] → same organisation and an internal tool, so no licensing question, but this is a courtesy and a coupling to declare rather than assume; raised as an open question with a named owner-to-find. If they decline, the tokens stay as an internal-precedent palette and the provenance note changes.
- [Atlas has no component for the portal's dense views (run timeline, log tail, gate strip)] → `portal.css` extends the system using only tokens; the constraint is "no new raw values", not "no new components".
- [Only ~60 of 280 classes carried over — the next page needs one that was skipped] → transcribing another primitive from the recorded source asset is a mechanical, low-risk addition; the filter is not a wall.
- [Duplicated dark-theme block (D5) can drift between the two selectors] → they sit adjacent in one file with a comment saying why; a re-sync touches both or neither.
- [Google Fonts as an outbound dependency in a portal that concentrates cross-client read access] → fonts only, no cookies of consequence, graceful degradation, and a self-host path that requires no other change (D6).
- [Inherited contrast ratios unverified (D8)] → audit task before the portal reaches operators; the tokens are OKLCH, so lightness is directly adjustable without disturbing hue relationships if a value fails.

## Migration Plan

Additive and reversible in one commit. The stylesheet is a new file; `App.razor` gains two link elements; `MainLayout.razor` is rewritten but its `AuthorizeView` structure and `PortalPolicies.Read` gate are carried over unchanged — the diff must show no authorization change, and that is the review's first check. `Home.razor` is restyled as the reference consumer. Reverting means deleting `wwwroot/css` and restoring the two Razor files; no data, no infra, no config participates.

Sequencing against `orchestrator-portal`: this lands before its tasks 3.x (the read-model pages), so those pages are written against the shell and primitives instead of being retrofitted.

## Open Questions

- Who owns Atlas's frontend, and do they want to (a) know, (b) co-own a shared token package, or (c) be left alone? Answer changes D3's end state, nothing in this change's implementation.
- Does the portal ship an explicit light/dark toggle in v1, or is OS-follow (D5) enough until someone asks? Leaning: no toggle until asked — the sidebar user card is where it goes, and Atlas's own placement is the precedent to copy.
- Is `--brand` right for the portal, or should it carry a distinct hue so operators can tell the two tabs apart at a glance? Leaning: keep it identical; continuity is the entire point, and the page title plus sidebar brand disambiguate.
- Queue-depth `warn`/`danger` thresholds (D4) are stated as "approaching KEDA's scale ceiling" but the actual numbers come from the stack's KEDA configuration — resolve when the queue read model lands (`orchestrator-portal` task 3.1).
