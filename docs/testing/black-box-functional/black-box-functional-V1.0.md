# Black-Box Functional Tests — What Was Done

Scope: tests that exercise CivicConnect through its public service/API/component
surface — asserting on inputs and observable outputs only, with no inspection of private
state or internal implementation — using a named black-box design technique
(Equivalence Partitioning, Boundary Value Analysis, Decision Table, State Transition, or
Negative testing). This is the project's black-box functional evidence for SEN381
Milestone 3, Assessment Area D.

Two kinds of black-box test exist in this repository: six standalone "Checks" console
executables (`tests/CivicConnect.*Checks/Program.cs`, run with `dotnet run --project`,
predating and separate from the xUnit suites), and four classes inside
`tests/CivicConnect.Tests/` that were added specifically to apply a named black-box
technique against a public service method. Both are documented here together because
they are the same kind of evidence; neither overlaps with
`docs/testing/unit-component/` or `docs/testing/api-integration/`, which cover the
xUnit suites' other classes.

## The six "Checks" executables

**Important finding, stated plainly:** none of these six projects are referenced by
`CivicConnect.sln`. Both CI workflows (`docs/ci/ci-workflow-V1.0.md`) run `dotnet build`
and `dotnet test` against the solution, which never touches these projects — they are
run only by a developer typing `dotnet run --project tests/CivicConnect.<Name>Checks`
(as `docs/decisions/0004-status-transitions.md` and the E2E README's "Unit checks"
section both instruct). Running all six live today (2026-10-16) found that **two of them
no longer build**:

| Project | Technique | Result when run today | Notes |
| --- | --- | --- | --- |
| `StatusChecks` | State Transition, Negative testing | ✅ Passed — "All FR-07 status transition checks passed." | |
| `NoteChecks` | Equivalence Partitioning, Negative testing | ✅ Passed — "All FR-16 note checks passed." | |
| `AuditChecks` | Equivalence Partitioning, Negative testing | ✅ Passed — "All FR-08 audit checks passed." | |
| `QueueChecks` | Decision Table, Boundary Value Analysis | ✅ Passed — "All Staff Queue search, filter, sort, visibility, and rendering checks passed." | |
| `NavigationChecks` | Negative testing (open-redirect/role recovery) | ❌ **Build error** — `CS0246: The type or namespace name 'DemoAuthenticationStateProvider' could not be found` | The class was renamed to `AppAuthenticationStateProvider` (`src/CivicConnect.Web/Services/AppAuthenticationStateProvider.cs`) during the FR-11 work; this check was never updated and has not compiled since. |
| `ConfigurationChecks` | Equivalence Partitioning | ❌ **Build error** — `CS0117: 'Category' does not contain a definition for 'PriorityTargetId'` | `Category` carries `UrgencyLevel`, not `PriorityTargetId`; this check references a field the entity has never had in its current form. |

Because neither workflow builds or runs these projects, nothing in CI would have caught
either regression — they were only found by actually trying to run them for this
documentation. This is recorded as a real, current limitation (technical debt), not
silently worked around: fixing either check, or adding all six projects to
`CivicConnect.sln` and a CI step, is left as a known follow-up for the team.

### What the four passing checks actually verify

- **`StatusChecks` (FR-07, State Transition).** Exhaustively tries all 5×5 = 25 ordered
  pairs of `{NEW, ASSIGNED, IN_PROGRESS, RESOLVED, CLOSED}` through the public
  `TicketService.ChangeStatus`/`.Find`, asserting only the 4 forward-adjacent pairs
  succeed and every other pair is rejected with the ticket's status and history
  unchanged. Also covers legacy status aliases ("Open" → `ASSIGNED`), `null`/empty/unknown
  target status, unauthorised/unnamed actors, and a 20-way `Parallel.For` race on the
  same transition that must produce exactly one success and one history entry — a
  negative/concurrency case on top of the state-transition matrix.
- **`NoteChecks` (FR-16).** Internal vs. public note visibility by role (Staff full
  access, Management full access, Requester public-only, anonymous none), note isolation
  between tickets, and four negative cases (blank/whitespace/newline-only text, an
  unauthorised requester, an anonymous author, a missing ticket) — confirming each
  rejection appends nothing.
- **`AuditChecks` (FR-08).** The redaction contract: an extensive equivalence set of
  sensitive key names (`pAsSwOrD_HaSh`, `EMAILADDRESS`, `PhoneNumber`, `Mobile`,
  `AccessToken`, `Credentials`, nested arrays, free-text containing an email/phone number)
  are all scrubbed from stored audit payloads regardless of casing or nesting, while
  non-sensitive values (`Role`, nested `Enabled`) survive; actor identity uses the stable
  database ID rather than an email address; a caller mutating its own dictionary or a
  returned snapshot after recording cannot alter the stored entry; duplicate-creation and
  missing-actor are rejected with no audit row written for the rejected attempt.
- **`QueueChecks` (FR-14/FR-15, Decision Table + Boundary Value Analysis).** Search ×
  category × status × sort combinations against `TicketQueueFilter.Apply`, including a
  combined-criteria case and the boundary around relative/unknown "Updated" date text
  (`"Just now"`, `"Today"`, `"invalid"`) feeding the date-sort comparator. It then also
  renders the real Blazor `Queue` component via `HtmlRenderer` (no running app/database)
  and asserts on the emitted HTML — active-only visibility, the empty-state message, and
  that the component still carries its `[Authorize(Roles = "Staff")]` attribute.

## The four black-box-technique xUnit classes

Added in two recent, explicitly technique-named pull requests — "State Transition
Testing" (PR #139) and "test: add equivalence partitioning tests for request visibility"
(PR #138) — these live in `tests/CivicConnect.Tests/` but are documented here, not in
`docs/testing/unit-component/`, because their defining feature is the black-box
technique applied, not the test level. Verified by running them directly just now:

```
dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj \
  --filter "FullyQualifiedName~RequestStatusStateTransitionTests|FullyQualifiedName~AuthorizationRecoveryTests|FullyQualifiedName~RequesterOwnershipTests|FullyQualifiedName~DemoQueueFixtureTests"
```
→ **Passed! Failed: 0, Passed: 35, Skipped: 0, Total: 35** (2026-10-16, this checkout).

- **`RequestStatusStateTransitionTests` — State Transition (FR-07).** Exercises only
  `TicketService.ChangeStatus`/`.Find` (the class's own header states the rule: "No
  private methods, lifecycle-table introspection, or state mutation during actions").
  Covers the four forward transitions with exact history-entry assertions, state
  retention on re-read, one direct-skip negative case (`Open → RESOLVED`), and three
  restricted-actor negative cases (Requester, Management, unauthenticated).
- **`AuthorizationRecoveryTests` — Decision Table + Equivalence Partitioning.** A 2×2
  decision table of HTTP method (`GET`/`POST`) × outcome (challenge/forbid) against the
  real ASP.NET Core authorization pipeline, asserting the correct status code (`302`,
  `401`, `403`) and redirect location per cell; plus the `Queue` page's role policy
  evaluated across the equivalence classes `null` (anonymous), `Requester`, `Staff`,
  `Management`, confirming only `Staff` is allowed.
- **`RequesterOwnershipTests` — Equivalence Partitioning (FR-11).** The test class that
  verifies the real bug found and fixed during the E2E work (see
  `docs/testing/e2e-system/e2e-system-V1.0.md`): equivalence classes of requester identity
  (account A/B, matching/non-matching name, renamed account, missing `NameIdentifier`
  claim, broader roles, anonymous/unknown role) against
  `GetVisibleTickets`/`FindVisible`/`CreateForRequester`, including that creation ignores
  a spoofed `RequesterId`/`Requester` supplied in the input model and that a stable ID
  survives a display-name change rather than falling back to name matching.
- **`DemoQueueFixtureTests` — Equivalence Partitioning.** Confirms the deterministic demo
  fixture dataset (also used by the E2E suite) actually has the category/priority/status
  variation its tests assume, and that the team-OR-assignee staff-queue visibility rule
  shows/hides a cross-team ticket correctly for different technicians.

## Traceability

| Technique | Where it's applied |
| --- | --- |
| State Transition | `StatusChecks`, `RequestStatusStateTransitionTests` (FR-07) |
| Decision Table | `QueueChecks` (FR-14/15), `AuthorizationRecoveryTests` |
| Equivalence Partitioning | `NoteChecks`, `AuditChecks`, `ConfigurationChecks` (FR-09), `RequesterOwnershipTests` (FR-11), `DemoQueueFixtureTests` |
| Boundary Value Analysis | `QueueChecks` (relative/unknown date text) |
| Negative testing | All six "Checks" projects, `RequestStatusStateTransitionTests`, `AuthorizationRecoveryTests` |

At least two distinct black-box design techniques are represented, per the M3 minimum
baseline, with several covering three or more. See `docs/testing/unit-component/` and
`docs/testing/api-integration/` for the project's other test-level evidence, and
`docs/testing/e2e-system/` for the browser-level evidence these fixtures also support.
