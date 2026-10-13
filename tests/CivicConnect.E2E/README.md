# CivicConnect browser E2E

Playwright exercises the real running Blazor application with fresh browser contexts, one worker, and no retries. There are no mocked components or intercepted APIs. Failure screenshots/traces are retained in `test-results/`; the HTML report is in `playwright-report/`. Both folders are ignored. Keep CI artifacts private because they may contain account/ticket information.

## Install and run

Use Node.js 22+ and a running PostgreSQL/backend/frontend stack. The frontend defaults to http://localhost:8085. The requested backend address is http://localhost:5001; compose defaults to 5060, so use `BACKEND_PORT=5001` when starting the entire stack if needed. Browser tests only access the frontend.

From the repository root, rebuild/restart the frontend to apply account/fixture changes and reset in-memory tickets:

```sh
docker compose build frontend-web
docker compose up -d --no-deps frontend-web
cd tests/CivicConnect.E2E
npm ci
npx playwright install chromium
cp .env.example .env
# Fill in account settings in the ignored .env file.
set -a
. ./.env
set +a
npm test
```

For Linux CI, install browser OS dependencies with `npx playwright install --with-deps chromium`. Inject credentials from the CI secret store instead of committing a filled .env. Values containing spaces/special characters must be shell-quoted; .env is not auto-loaded. Wait for the frontend to be ready before starting tests.

```sh
npm test -- --list
npm test -- specs/queue.spec.js
npm test -- specs/requester.spec.js
npm test -- specs/workflow.spec.js
npm test -- specs/auth.spec.js
npm run test:ui
npm run report
npx playwright show-trace test-results/<test-folder>/trace.zip
```

## Deterministic demo fixtures

The existing `DemoDataSeeder` uses its existing Argon2id password-seeding/verification mechanism for all four demo accounts. Existing account names/roles/passwords are preserved. Obtain the configured demo password from your local setup; no password is provided in this README or .env.example. Existing valid custom account passwords are not reset to force a test login.

| Account | Identity | Portal/database role | Environment variables |
| --- | --- | --- | --- |
| Requester A | requester@civicconnect.demo / Bernard Small | Requester / reporter | `E2E_REQUESTER_A_EMAIL`, `E2E_REQUESTER_A_PASSWORD` |
| Requester B | requester.b@civicconnect.demo / Ayesha Khan | Requester / reporter | `E2E_REQUESTER_B_EMAIL`, `E2E_REQUESTER_B_PASSWORD` |
| Staff | staff@civicconnect.demo / Daniel Jacobs | Staff / technician | `E2E_STAFF_EMAIL`, `E2E_STAFF_PASSWORD` |
| Management | manager@civicconnect.demo / Michael Adams | Management / operations manager | `E2E_MANAGEMENT_EMAIL`, `E2E_MANAGEMENT_PASSWORD` |

Requester B is added through the same idempotent account-seeding loop as the original accounts. Its name matches the existing CC-010 requester. Staff remains in IT Support only. The existing Facilities ticket CC-013 is assigned to Daniel Jacobs, making it visible through the unchanged team OR assignee rule. No multi-team rule, authorization rule, or ownership rule is changed.

| Ticket | Category | Priority | Status after frontend reset | Requester | Staff visibility |
| --- | --- | --- | --- | --- | --- |
| CC-014 — Unable to connect to office Wi-Fi | IT Support | High | In Progress | Bernard Small | Team and assignment |
| CC-013 — Air conditioner not working | Facilities | Medium | Open | Bernard Small | Assignment to Daniel Jacobs |
| CC-010 — New starter system access | IT Support | High | Open | Ayesha Khan | Team |

CC-012 remains resolved and excluded from the active queue. No new tickets, random accounts, or database incidents are needed to establish FR-15 variation. Tests assert that both included and excluded tickets exist for configured category/status filters, and priority sorting requires two distinct visible priorities. A fresh frontend now supplies both conditions without preparatory browser mutations.

## Environment settings

`.env.example` contains deterministic ticket/filter values and blank account credentials. Supply all eight account email/password settings above. Other settings:

| Variable | Meaning/default |
| --- | --- |
| `E2E_BASE_URL` | Optional, http://localhost:8085 |
| `E2E_SEARCH_KEYWORD` | Wi-Fi |
| `E2E_SEARCH_MATCH_TITLE` | Unable to connect to office Wi-Fi |
| `E2E_SEARCH_NONMATCH_TITLE` | New starter system access |
| `E2E_QUEUE_CATEGORY` | Facilities; requires included and excluded queue records |
| `E2E_QUEUE_STATUS` | Open; requires included and excluded queue records |
| `E2E_REQUESTER_A_TICKET_ID`, `E2E_REQUESTER_A_TICKET_TITLE` | CC-014 and its exact title above |
| `E2E_REQUESTER_B_TICKET_ID`, `E2E_REQUESTER_B_TICKET_TITLE` | CC-010 and its exact title above |
| `E2E_WORKFLOW_TICKET_TITLE`, `E2E_WORKFLOW_NEXT_STATUS` | New starter system access / Assigned after reset |
| `E2E_CREATE_CATEGORY`, `E2E_CREATE_LOCATION` | Optional; Facilities / Ground Floor |

Missing settings fail explicitly instead of silently skipping ownership tests. A and B must be distinct accounts. No E2E test source contains account/password literals.

## Isolation and ownership behavior

Authentication is circuit-scoped. Tests reuse the working auth helper and UI links; role-denial checks use same-document navigation, and direct ticket tests sign in with a local returnUrl. A full page reload creates a new anonymous circuit. The helper waits for the initial Blazor render batch over the default WebSocket transport before filling prerendered inputs.

Creation uses a unique title and leaves the request in memory. Workflow advances CC-010 one step and leaves its new status in memory. Reset/recreate the disposable frontend before each full run, or update the workflow next-status setting explicitly. Run only one suite per application instance. Resetting the frontend restores the original in-memory queue, but does not delete seeded database accounts. Startup seeding is idempotent.

FR-11 assertions remain unchanged. The requester list and detail lookup now use the same service visibility check. Newly created tickets receive RequesterId and display name from the authenticated principal, ignoring ownership in form state. ID-backed ownership remains valid after a display-name change, and another account with the same name cannot access that ticket. Staff/Management retain broader access. Denied detail lookups use the existing Ticket not found view and do not load foreign notes/details.

Legacy demo tickets contain display names only, so they use exact, nonblank authenticated display-name matching. This compatibility fallback cannot distinguish legacy owners who share the same display name and will not follow a legacy owner rename. Before using such records with non-demo accounts, migrate/backfill their stable requester IDs from a trusted source. New UI-created requests have no name fallback. The in-memory demo model still resets on frontend restart; no database ownership schema was redesigned.

Both date sorts skip with a reason if displayed dates contain relative/unsupported values (Today, Yesterday, Just now), or lack two distinct absolute dates. The application does not expose the server date/timezone for a reliable comparison. FR-08 is excluded because this suite has no established audit-record UI/API contract.

## Unit checks

With .NET 10 installed, from the repository root:

```sh
dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj
dotnet run --project tests/CivicConnect.QueueChecks/CivicConnect.QueueChecks.csproj
```

The host currently has .NET 9, so validation uses an isolated .NET 10 SDK Docker container. The database integration suite additionally requires its existing CONNECTION_STRING configuration and is separate from these unit/queue checks. Playwright configuration reference: https://playwright.dev/docs/test-configuration.

## Fixture-only validation before the FR-11 fix — 14 October 2026

After rebuilding/resetting the frontend and loading the ignored .env:

- **Unit tests: 37 passed**, including the new deterministic fixture coverage/visibility checks. Dedicated Staff Queue checks also passed. Frontend Docker publish, JavaScript syntax and diff checks passed.
- **Full Playwright: 15 passed, 4 failed, 2 skipped** (21 tests). All FR-15 search/category/status/reset/priority cases passed with seeded fixtures alone. Category exclusion is now mandatory and fully exercised against the Facilities versus IT Support records.
- The two date tests skipped because displayed dates include relative values, as designed.
- Requester B authenticated successfully as Ayesha Khan with the Requester role. Its three former missing-credential failures now execute and expose application defects: B cannot see its own CC-010 in My Tickets; B sees A's CC-014 in normal navigation; B can directly open A's CC-014. The fourth ownership failure is A directly opening B's CC-010. No missing E2E settings remain in the local validation configuration.
- These failures are consistent with the fixed requester-name filter and missing detail ownership check described above. No assertions were weakened, no expected-failure annotations were added, and ownership business logic was not changed.
- Request creation and the Staff Open → Assigned transition passed. Reset the frontend before repeating the full suite with .env.example's initial workflow setting. Failure screenshots/traces and the HTML report are retained in ignored output folders.

Files changed for deterministic fixture support: `DemoDataSeeder.cs`, `TicketService.cs` (demo assignment data only), `DemoQueueFixtureTests.cs`, `specs/queue.spec.js`, `.env.example`, and this README. The ignored local .env was updated with B's credentials and the deterministic fixture values; no credentials were added to committed E2E source or documentation examples.

## FR-11 fix validation — 14 October 2026

The four ownership assertions now pass unchanged. Root causes were the hardcoded requester list filter, the unrestricted detail lookup, and a hardcoded default requester on creation.

The requester pages now call shared `TicketService.GetVisibleTickets`, `FindVisible`, and `CreateForRequester` methods. Creation stores the authenticated NameIdentifier in RequesterId and the authenticated display name in Requester, overriding form values. The creation snapshot copies both ownership fields. The legacy trusted/non-UI Create overloads remain available for existing callers. Detail lookup denies anonymous/foreign users before assigning a ticket or loading notes; Staff/Management retain their broader access. The pages use the existing circuit authentication provider; no auth scheme or controller API was rewritten. These requester pages use the in-memory TicketService, not the incident controllers.

Results:

- **49 unit tests passed**, including **12 dedicated RequesterOwnershipTests** run separately.
- Dedicated Queue, Notes, Status and Audit regression executables all passed.
- **Full unchanged Playwright suite: 19 passed, 0 failed, 2 skipped**. All four previously failing ownership scenarios passed. Date sorts remain intentionally skipped for relative displayed dates.
- The unchanged creation test also passed separately using Requester B's credentials through environment overrides, confirming creation/list visibility for B as well as A.
- Frontend Docker publish and diff checks passed. Validation used .NET 10 containers because the host SDK is .NET 9. The database integration suite was not run.

Files changed for this fix: `Models/Ticket.cs`, `Services/TicketService.cs`, `Pages/Requester/MyTickets.razor`, `Pages/Requester/NewTicket.razor`, `Pages/Shared/TicketDetail.razor`, `RequesterOwnershipTests.cs`, and this README. E2E specs/assertions and authentication implementation were not changed.

Remaining FR-11 limitation: legacy name-only snapshots cannot distinguish equal display names or follow a rename. New ID-backed requests do not have that limitation. Backfill legacy records with trusted requester IDs before relying on them outside the demo dataset; no demo-account special cases were added to authorization. Reset the frontend before another full suite run, since the workflow ticket was advanced to Assigned.
