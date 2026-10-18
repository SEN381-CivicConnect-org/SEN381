# CivicConnect Defect Register (Minimal) - V1.0

*Defects found so far, recorded as: defect, severity/impact, status, fix, regression evidence (PED v3.0 section 10.5). Built from PED v1.0, PED v3.0, the repository (dev, pulled 2026-10-18) and the test, CI and deployment docs. No tests were re-run; regression results are those recorded in the repo docs.*

**final.pdf was not available when this was written, so its requirements are not reflected. Check the columns and severity scale against it.**

## Severity scale

- **Critical:** data exposure or loss in normal use, or the system cannot be released.
- **High:** a requirement is violated, or a security control is weakened.
- **Medium:** reduced reliability or verification, with a workaround.
- **Low:** documentation or traceability issue with no functional effect.

## Register

| ID | Defect | Requirement | Severity / impact | Status | Fix | Regression evidence |
|---|---|---|---|---|---|---|
| DEF-01 | A requester's "My Tickets" list used a hardcoded requester-name filter, so Requester B could not see their own ticket and could see Requester A's. | FR-11 | High. Requesters could see each other's tickets. | Fixed | PR #133: MyTickets.razor moved to ownership-aware TicketService.GetVisibleTickets. | RequesterOwnershipTests, RequestVisibilityEquivalencePartitionTests (EP-01 to EP-08), E2E requester.spec.js. Recorded: 19 passed, 0 failed, 2 skipped (E2E README, 14 Oct). |
| DEF-02 | Ticket detail lookup had no ownership check, so a requester could open another requester's ticket by direct URL. | FR-11, FR-16 | High. Disclosure of another user's ticket, notes and history. | Fixed | PR #133: TicketDetail.razor uses FindVisible and denies non-owners before loading notes or history. | TicketWorkflowRegressionTests REG-03 and REG-04, EP-07 and EP-08, E2E direct-route tests. REG and EP run: 20 of 20 passed (2026-10-16). |
| DEF-03 | Ticket creation defaulted to a hardcoded requester instead of the signed-in identity, and trusted a requester ID supplied in the form. | FR-01, FR-11 | High. Tickets could be attributed to the wrong or a forged user. | Fixed | PR #133: CreateForRequester takes identity and stable ID from the authenticated principal. | RequesterOwnershipTests (spoofed-ID and stable-ID cases), ReporterVisibilityChecks. Recorded passing. |
| DEF-04 | SystemSecretsConfigurationTests regression blocked the db-migrations.yml pipeline four times on task/NFR01. | NFR-01 | Medium. Secrets startup guard was failing in CI. | Fixed | PR #121. | SystemSecretsConfigurationTests. Pipeline passing on later runs (docs/ci/quality-gate-V1.0.md). |
| DEF-05 | Role passwords are hardcoded in the roles and report-role migrations and in scripts/provision-roles.sql (civic_app, civic_migrate, civic_report). | NFR-01, NFR-10 | High. Secrets are in source, and the config-scan test only covers appsettings*.json, so it cannot catch them. | Open | None yet. Load role passwords from environment variables, and extend the scan test to cover migrations and scripts. | None. |
| DEF-06 | Web ticket state lives in the in-memory TicketService and resets on restart, so tickets, notes and audit entries are not persisted. | FR-01, FR-07, FR-08, FR-11, FR-14 to FR-16 | High. Data is lost on restart, and DB-level behaviour for these requirements is untested. | Open | None yet. | None. E2E README states the limitation. |
| DEF-07 | NavigationChecks did not build: it referenced DemoAuthenticationStateProvider, renamed to AppAuthenticationStateProvider. | FR-02, FR-11 | Medium. A black-box check silently stopped running. | Fix committed, unverified | Latest pull changed NavigationChecks (new AuthFixture.cs). | None recorded. Needs dotnet run --project tests/CivicConnect.NavigationChecks. |
| DEF-08 | ConfigurationChecks does not build: it references Category.PriorityTargetId, which the entity does not have. | FR-09 | Medium. Check not running. | Open | None yet. Update to UrgencyLevel. | None. |
| DEF-09 | The six console Checks projects are not in CivicConnect.sln, so CI never builds or runs them. This is how DEF-07 and DEF-08 went unnoticed. | FR-07, FR-08, FR-09, FR-14 to FR-16 | Medium. Verification gap. | Open | None yet. Add the projects to the solution and to a CI step. | None. |
| DEF-10 | Neither branch ruleset requires the CI quality gate to pass before merging. | Process (RSK-07) | Medium. A red build can still be merged. | Open | None yet. Add a required-status-check rule. | None. |
| DEF-11 | Availability evidence reports "100% PASS" from zero working-hour samples (2 live samples, none in working hours). | NFR-09 | Medium. Misleading evidence. | Open | None yet. Collect real samples, or report "insufficient data". | None. |
| DEF-12 | RTM test ID IT-CFG-01 is used for two requirements (FR-09 and NFR-01), and several FR-09, FR-11 and FR-15 tests are not tagged with their RTM IDs. | FR-09, NFR-01 | Low. Traceability only. | Open | None yet. Rename one ID and tag the untagged tests. | None. |
| DEF-13 | RTM design column and test docs are stale: they name files that do not exist (NotificationService.cs, IncidentStateMachine.cs, QueueService.cs, ConfigService.cs), and test counts disagree (43, 78, 88, 98; 32 for Data.Tests despite RetentionAndDisposalTests). | All | Low. Documentation only. | Open | None yet. | None. |

## Notes

- **DEF-01 to DEF-03:** the three root causes behind the four failing E2E tests in docs/testing/e2e-system/e2e-system-V1.0.md, and the D1 and D2 defects named in PED v3.0 section 10.3.1.
- **DEF-07 to DEF-10:** the candidate Technical Debt entries listed in PED v3.0 section 10.4, kept here until a Technical Debt Register exists.
- **Missing tests:** gaps such as FR-12, FR-17, NFR-04 and NFR-10 are evidence gaps, not defects. They are recorded in docs/testing/rtm-evidence/rtm-evidence-sheet-V1.0.md.
