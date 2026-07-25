# Design — Portal Browser Tests

## Context

`Orchestrator.Portal.FunctionalTests` drives the app through `WebApplicationFactory`. That is the right tool for what it covers — which endpoints are anonymous, what a role-less user sees, whether a probe leaks stack data — and it is structurally incapable of covering anything after the first render, because Blazor Server's interactivity lives on a SignalR circuit those tests never open.

The portal's read views were written with care about *rendered* distinctions: an unreadable cell must not look like a zero, a degraded panel must not look like an empty one, a status must never be communicated by colour alone. Every one of those is a property of markup, and every one is currently unverified.

ds-connect has a Playwright project (`DsConnect.EndToEndTests`) driving a real Aspire AppHost against real containers, excluded from the default test run and split into a per-PR lane and a nightly live lane. Its shape is worth borrowing; its scope is not, and D1 is about why.

## Goals / Non-Goals

**Goals**
- Cover the interactions an operator actually performs: changing the history window, running a verb, reading a rollup.
- Pin the rendered distinctions that are correctness rather than styling — unreadable vs zero, degraded vs empty, refused vs failed.
- Run deterministically, today, with no deployed infrastructure.
- Stay out of the default test run without becoming a suite nobody runs.

**Non-Goals**
- Real Azure sources or real vendor APIs. That is each change's pilot task, and it is a different question: those verify the client, this verifies the page.
- Entra authentication. The existing stand-in scheme is what these tests sign in through, for the same reason the functional tests use it — Entra must not become a network dependency of a test suite.
- Visual regression or screenshot diffing.
- Browsers other than Chromium.

## Decisions

### D1 — Stubbed sources, not a live stack

ds-connect's E2E drives a real AppHost with real Postgres, Redis and Keycloak. That fits their app, where the interesting behaviour is end-to-end through real infrastructure.

Here it would be the wrong target. What is untested is the **page**, not the clients — those have their own tests, and the ones that talk to Azure and vendors are unverifiable on this machine anyway. Driving a real stack would make the slowest, least reliable part of the system a prerequisite for testing the fastest, and would make the states worth testing nearly unreachable: producing a genuinely degraded ACA source on demand is not something a test can arrange.

With the read model stubbed, every state is one line of setup: a source that throws renders the degraded panel, a source returning zero renders the zero cell, and the two can be asserted to look different. The seam already exists — `IStackSource<T>` and `IPanelReader` were built to make failure a value rather than an exception, and that same shape makes failure a fixture.

Consequence to accept: these tests cannot catch a real Azure client returning something the page mishandles. That gap is real and belongs to the pilot tasks, which is where it is already recorded.

### D2 — Out of the default run, and loudly rather than silently

Excluded from `dotnet test` over the solution, mirroring ds-connect's filter. Browser tests need a browser download and are an order of magnitude slower; a default suite people stop running protects nothing.

The failure mode of exclusion is worse than the cost it avoids, though: a browser suite that quietly never runs in CI reads as coverage while providing none. So the exclusion is paired with its own CI lane, and the lane must fail visibly rather than skip. ds-connect's own spec makes the same point about skipped tests — "documented, not hidden", and never simultaneously skipped and run.

### D3 — Address elements by stable test ids, never by visible text

Tests key on `data-testid`. Two reasons, and the second is the one that bites: copy changes for product reasons, so a text-keyed test turns a wording tweak into a red build with a misleading message. And the portal's own content includes vendor-supplied strings — item titles, gate state names authored by a client — so text selectors would couple tests to data that is not ours.

The cost is markup noise. Accepted, and bounded by only adding an id where a test addresses the element.

### D4 — Assert the distinctions, not the appearance

The temptation with a browser harness is to assert on classes and colours. That produces tests that break on every restyle and still miss the thing that matters.

What these tests assert is that two states are **distinguishable** and correctly labelled: an unreadable cell carries its own marker and is not the string "0"; a degraded panel says what could not be read; a status badge has text and not only a dot. That is the design system's own floor — never colour alone — stated as a test rather than as a comment, and it survives a restyle because it is about semantics.

### D5 — One host per suite, reset between tests

A single hosted portal shared by the whole suite, with the stub sources reset per test. ds-connect's retro records what the alternative costs: per-class fixtures were spinning up their stack seventeen times in one module, and moving to one shared collection fixture cut the combined suite from minutes to about a minute.

Since the sources are stubs rather than a database, "reset" is assigning fresh stub state, not Respawn. Cheaper, and it removes the ordering coupling that makes shared-fixture suites fragile.

## Risks / Trade-offs

- **A stub can drift from the real source.** The page could pass against a stub shape the real client never produces. Mitigated by the stubs returning the same contract types the real sources do, so a contract change breaks compilation rather than silently diverging — but the semantic gap is real and is the honest cost of D1.
- **Browser download in CI.** A new external dependency for the lane. It comes from a CDN rather than a container registry, which is a meaningful advantage in this environment, but it is still a network dependency that can fail.
- **Selector churn.** `data-testid` attributes are markup that exists only for tests. They will occasionally be deleted by someone tidying, and the failure will look unrelated to the change that caused it.
- **The suite will be skipped under pressure.** Every excluded suite eventually is. D2 pairs the exclusion with a lane precisely because that is the predictable failure, not a hypothetical one.

## Migration Plan

1. Add the project and the host fixture with stubbed sources; prove one trivial navigation passes, so the harness itself is known good before any assertion depends on it.
2. Add `data-testid` to the elements the first tests address, and only those.
3. Cover the rendered distinctions first — unreadable vs zero, degraded vs empty. They are the highest-value and need no interaction.
4. Then the interactions: the history window selector, then the verb flows and their three outcomes.
5. Add the CI lane, and confirm it fails when a test fails rather than skipping.

Rollback: the project is additive and excluded from the default run, so removing it changes nothing else.

## Open Questions

- **Where does this run in CI, given the runner constraints?** ds-connect had to route its container-needing lane to a Docker-capable runner. A browser lane has different requirements and may fit the default fleet, but that needs checking rather than assuming.
- **Does this suite eventually get a live twin**, pointed at a deployed portal? ds-connect runs one nightly. That only becomes meaningful once a stack is actually deployed, which nothing here is yet.
- **Accessibility beyond the never-colour-alone floor** — keyboard navigation and contrast are in the design-system change's task list and unverified. A browser harness is the natural place to check them, and whether that belongs here or there is unresolved.
