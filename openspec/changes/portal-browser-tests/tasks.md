# Tasks — Portal Browser Tests

## 1. Harness

- [ ] 1.1 Add `Orchestrator.Portal.BrowserTests` under `src/tests/root/`, with Playwright. Excluded from the default solution test run by the same filter mechanism ds-connect uses for its E2E project
- [ ] 1.2 Host fixture: the portal composed with stubbed read-model sources, signed in through the existing stand-in authentication scheme. Entra must not become a network dependency of a test suite — the same reason the functional tests use that scheme
- [ ] 1.3 One host for the suite with stub state reset per test (design D5). Reset means assigning fresh stub state, not Respawn, since there is no database here. ds-connect's retro records what per-class fixtures cost them: seventeen stack spin-ups in one module
- [ ] 1.4 Prove the harness before anything depends on it: one trivial navigation asserting the page loads. A harness whose first failure is ambiguous between "the test is wrong" and "the harness is wrong" wastes the time it was meant to save
- [ ] 1.5 Verify the browser download works in this environment. It comes from a CDN rather than a container registry, which is why this suite is viable here at all when Docker Hub is not — but confirm rather than assume

## 2. Rendered distinctions (no interaction needed, highest value)

- [ ] 2.1 `data-testid` on the rollup's cells and the stack view's panels — only the elements a test addresses (design D3)
- [ ] 2.2 **Unreadable is not zero.** One stack reading zero beside one whose source fails, asserting the two are distinguishable and that the unreadable cell shows no number. This is the requirement the harness exists for: only one of those states is safe to act on and the difference lives entirely in markup
- [ ] 2.3 A stack whose every source fails still appears as a row. Silently dropping it is the worst failure mode for a rollup, and it is invisible to every other kind of test
- [ ] 2.4 **Degraded is not empty.** A panel whose source returned nothing beside one whose source failed, asserting each says which it is
- [ ] 2.5 One failing source leaves the other panels rendering normally
- [ ] 2.6 Every status badge carries a text label, not only colour — the design system's floor, asserted against what a browser actually renders rather than against the stylesheet

## 3. Interactions

- [ ] 3.1 The history window selector re-reads and re-renders against the new window. Untestable over HTTP: it only works over the interactive circuit
- [ ] 3.2 Manual dispatch reports each of its three outcomes distinctly, and the refusal shows the live state that caused it — the detail that makes a refusal actionable rather than merely negative
- [ ] 3.3 Cancel on an already-terminal execution reports a no-op rather than success
- [ ] 3.4 A reader-role user cannot reach the verbs from the UI. The server-side refusal is covered elsewhere; this covers that the affordance is absent, which is a different claim

## 4. CI and close-out

- [ ] 4.1 Its own lane, failing on a failing test rather than skipping (design D2). Confirm by making a test fail deliberately once and checking the lane goes red — an excluded suite that silently never runs reads as coverage while providing none
- [ ] 4.2 Check which runner the lane needs. ds-connect had to route its container lane to a Docker-capable runner; a browser lane's requirements differ and may fit the default fleet, but that needs checking (design Open Questions)
- [ ] 4.3 Document how to run it locally, including the browser install step, next to the existing test instructions
- [ ] 4.4 Resolve design.md's open questions: which runner, whether a live twin against a deployed portal is worth it once one exists, and whether the design system's unverified keyboard and contrast passes belong in this suite or stay in `portal-design-system`
