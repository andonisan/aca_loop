# Portal Browser Tests — cover the half of the portal that HTTP tests cannot reach

## Why

The portal's functional tests drive it through `WebApplicationFactory`. For a Blazor Server app that covers the **initial render** and nothing after it: interactivity happens over a SignalR circuit that never opens in those tests. So everything the operator actually does is untested.

Concretely untested today:

- **The history window selector.** Changing it re-reads through a different cache key and re-renders. Nothing exercises it.
- **The verb flows.** Manual dispatch and cancel both have confirmation paths and three distinct outcomes — performed, refused, failed — and the refusal has to show the live state that caused it. None of that is reachable over plain HTTP.
- **The distinction between "0 queued" and "unreadable".** This one is the reason worth writing the harness. In the cross-stack rollup an unreadable cell and a zero cell must not look alike, because **only one of them is safe to act on**. That is a correctness property expressed entirely in markup, and no HTTP assertion can see it.
- **Degraded panels.** One failing source must render its own card as unavailable while the rest of the page stands. The logic is tested; the rendering is not.
- **Both themes and the never-colour-alone floor.** The design system requires status never be communicated by colour alone, and that the dark palette not drift between the `[data-theme]` override and the OS-preference block. A unit test already guards the stylesheet; nothing checks what a browser actually paints.

## What Changes

- **A `portal-browser-tests` capability**: Playwright coverage of the portal's interactive behaviour, run against the app hosted in-process with **stubbed read-model sources**.
- **Stubbed sources, not real Azure or vendors.** The read model is already an injectable seam (`IStackSource<T>`, `IPanelReader`, `BacklogReaderFactory`), so a browser test can drive a stack into any state — empty, populated, degraded, unreadable — deterministically, and can do so today with no deployed infrastructure. Testing against real sources is a different activity that belongs to each change's pilot task.
- **Excluded from the default `dotnet test` run**, mirroring ds-connect's treatment of its own E2E project: browser tests need a browser download and are slower by an order of magnitude, and a suite people skip because it is slow protects nothing.
- **Stable selectors added to the portal's markup** where a test needs to address an element. Tests key on those, never on user-facing text, which changes for copy reasons and would make a test failure indistinguishable from a wording change.

## Capabilities

### New Capabilities
- `portal-browser-tests`: browser-level coverage of the portal's interactive behaviour and rendered semantics — the read views' states, the verb flows and their outcomes, the degraded and unreadable renderings, and the accessibility floor the design system requires.

## Impact

- **Code**: a new test project outside the default run; `data-testid` attributes on the elements tests address; a host fixture that composes the portal with stubbed sources.
- **Tooling**: Playwright and its browser download. Notably this comes from a CDN rather than a container registry, which matters here — Docker Hub is unreachable from at least one development machine while MCR is not, and this harness sidesteps that entirely.
- **CI**: a separate lane. It must not be silently skipped — a browser suite that never runs is worse than none, because it reads as coverage.
- **Not in scope**: real Azure sources, real vendor APIs, authentication against Entra (the existing stand-in scheme stays), visual regression / screenshot diffing, and browsers other than Chromium.
