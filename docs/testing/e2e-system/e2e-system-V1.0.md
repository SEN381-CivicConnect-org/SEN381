# E2E / System Tests — What Was Done

Scope: `tests/CivicConnect.E2E/` — a Playwright suite driving a real Chromium browser
against the actual Dockerized CivicConnect stack (Blazor frontend + API + PostgreSQL),
with no mocked components or intercepted network calls. This is the project's
E2E/system-level evidence for SEN381 Milestone 3, Assessment Area D, added in PR #133
("Test/e2e critical user flows", merged 2026-10-15).

## Why this evidence is here, not elsewhere

Every other test document in `docs/testing/` runs in-process, without a browser and
usually without the real frontend. This suite is the one place the actual rendered
Blazor app — real login form, real server-rendered circuit, real client-side routing —
is driven end-to-end, which is what lets it catch the kind of defect the other suites
structurally cannot (see "A real defect found and fixed" below).

## Environment and setup

- **Stack under test:** the project's own `docker compose` services (`db`,
  `rabbitmq`, `backend-api`, `frontend-web`), built from the real `Dockerfile`s — not a
  dev server or a stub.
- **Browser:** Playwright + Chromium, one worker, zero retries ("mutating flows must not
  silently repeat against shared application state" — `playwright.config.js`), 30 s test
  timeout, screenshots and traces retained only on failure.
- **Demo accounts:** four seeded accounts (`DemoDataSeeder.cs`), covering Requester
  (two distinct accounts, A and B, specifically to prove ownership isolation), Staff, and
  Management, with credentials supplied only via a gitignored local `.env` — never
  committed, and not present in this document either.
- **Fixtures:** three specific demo tickets (CC-010, CC-013, CC-014) with deliberately
  distinct categories, priorities, statuses and team/assignee combinations, so that
  filter/sort/visibility assertions have a genuine positive and negative case to check
  against without the suite mutating the database to manufacture them.

## What was run, and what it found

### Attempted live re-run for this document (2026-10-16)

An attempt was made to bring up the full stack locally (`docker compose up --build`,
then `--env-file .env`) to re-verify the suite fresh. This surfaced a real, separate
local-environment gap: the repository's own `.env` here only defines `CONNECTION_STRING`,
not the `POSTGRES_PASSWORD`/`JWT_SECRET`/RabbitMQ variables `docker-compose.yml` expects,
so both `backend-api` and `frontend-web` fell back to the compose file's placeholder
`Password=changeme` — which `SystemSecretsConfiguration`'s own NFR-01 startup guard then
correctly refused to start with (`'CONNECTION_STRING' uses an insecure default or empty
password`). That guard is working exactly as designed (see
`docs/security/security-dependency-secrets-V1.0.md`); building out a complete local
`.env` to get past it was outside the scope of a documentation pass. The containers were
stopped and removed, and the pre-existing `db` container (and its data) were left exactly
as found. What follows is therefore the project's own real, dated execution record from
`tests/CivicConnect.E2E/README.md`, not a re-run performed today.

### A real defect found and fixed (14 October 2026, PR #133)

**Before the fix — fixture-only validation:** 37 unit tests passed, then the full
Playwright run: **15 passed, 4 failed, 2 skipped (21 tests)**. All FR-15 search/filter/
sort cases passed. The two date-sort tests skipped by design (displayed dates were
relative — `"Today"`, `"Just now"` — which the suite deliberately will not try to parse
against an unexposed server clock/timezone). The four failures were real application
defects, not flaky tests or bad fixtures:

1. Requester B could not see their own ticket (CC-010) in "My Tickets".
2. Requester B could see Requester A's ticket (CC-014) via normal list navigation.
3. Requester B could directly open Requester A's ticket by URL.
4. Requester A could directly open Requester B's ticket by URL.

**Root causes** (per the README): a hardcoded requester-name filter, a detail lookup
with no ownership check at all, and ticket creation defaulting to a hardcoded requester
rather than the authenticated identity.

**The fix:** `MyTickets.razor`, `NewTicket.razor` and `TicketDetail.razor` were moved onto
shared, ownership-aware `TicketService` methods (`GetVisibleTickets`, `FindVisible`,
`CreateForRequester`); creation now takes the requester's identity and stable ID from the
authenticated principal and ignores whatever the submitted form/model claims; detail
lookup denies an anonymous or non-owning requester before any note or ticket data loads.
`RequesterOwnershipTests.cs` (documented in
`docs/testing/black-box-functional/black-box-functional-V1.0.md`) was added as the
corresponding black-box equivalence-partitioning coverage at the service level.

**After the fix — FR-11 fix validation:** 49 unit tests passed (12 of them the new
`RequesterOwnershipTests`), and **the full Playwright run: 19 passed, 0 failed, 2
skipped**. All four ownership failures above now pass unchanged — the README is explicit
that no assertion was weakened and no expected-failure annotation was added to make this
so. The same two date-sort tests remain intentionally skipped for the same stated reason.

No fabricated or reconstructed numbers are used above — both result lines are quoted
directly from the project's own committed `README.md`, dated against the actual PR merge.

## What the suite covers (`specs/`)

- **`auth.spec.js` (3 tests).** Staff login → queue access → sign-out revokes access, with
  a same-document (in-circuit) navigation back to a protected route confirming it now
  redirects to login. Requester login succeeds but direct in-circuit navigation to
  `/staff/queue` is denied with the "Access denied" view, not a crash or a silent empty
  page. Management keeps its own workspace and is denied entry to the Staff queue, with
  "Return to my workspace" recovering correctly.
- **`queue.spec.js` (10 tests).** FR-15: all four queue controls render for Staff;
  anonymous access to `/staff/queue` redirects to login with no table rendered; keyword
  search retains a known match and removes a known non-match; category and status filters
  each tested against a real included/excluded fixture pair (not just "the filter runs");
  "Clear filters" restores every control and the original row set; priority sort proven
  in both directions against two genuinely distinct priorities; date sort in both
  directions, which explicitly `test.skip()`s itself with a stated reason when the
  displayed dates are relative or insufficiently distinct, rather than asserting
  something it cannot actually prove.
- **`requester.spec.js` (7 tests).** Creating a new request and seeing it immediately in
  "My Tickets"; the six FR-11 ownership scenarios described above (own-ticket access,
  foreign-ticket exclusion from the list, and foreign-ticket exclusion by direct URL, each
  checked in both directions between Requester A and Requester B).
- **`workflow.spec.js` (1 test).** A real Staff status transition through the UI (status
  dropdown → "Update status" button), asserting both the displayed current status and the
  appended `previous → new` history line.

21 tests in total, matching the README's own reported count exactly.

## Honest limitations (as the suite's own README states them)

- **In-memory application state.** The frontend's `TicketService` is in-memory and resets
  on restart; the suite can create a ticket or advance a workflow, but that state does not
  survive a frontend restart, and a repeated full run needs the frontend reset first (or
  the workflow fixture's "next status" updated) to avoid colliding with a ticket it already
  advanced.
- **Legacy ownership fallback.** Tickets created before the FR-11 fix only have a display
  name, not a stable ID, so ownership for those specific legacy rows falls back to exact
  name matching and cannot distinguish two accounts that happen to share a display name,
  nor follow a rename. New, UI-created tickets do not have this limitation.
- **FR-08 is explicitly out of scope** for this suite — there is no established
  audit-record UI/API contract for Playwright to assert against yet.
- **Database side effects.** Startup seeding is idempotent, but E2E runs do write real
  demo accounts and can create real requests/status changes — the suite assumes a
  disposable environment, not a protected shared one.

## Traceability

FR-07 (workflow), FR-11 (requester ownership — the specific defect/fix above), FR-14/15
(queue access, search/filter/sort). Black-box service-level coverage supporting these same
fixtures: `docs/testing/black-box-functional/black-box-functional-V1.0.md`. Security guard
encountered while attempting a live re-run: `docs/security/security-dependency-secrets-V1.0.md`.
