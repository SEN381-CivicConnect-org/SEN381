# FR-12 black-box evidence — issue #116

Run date: 12 October 2026 (Africa/Johannesburg). Source: current GitHub `dev`, verified via GitHub at commit `9870ae370f66183d23b848ff598e5c08b593c74f`, matching this branch's base.

Test basis: [source issue #33](https://github.com/SEN381-CivicConnect-org/SEN381/issues/33) requires due thresholds on management views; [test issue #116](https://github.com/SEN381-CivicConnect-org/SEN381/issues/116) verifies #33 / merged PR #90. The repository's [FR-12 requirement](../requirements/Functional%20Requirements/functional-requirements-V1.0.html) specifies listing incidents still open past their due time, with the due time and elapsed overrun. Resolved and closed incidents therefore belong outside that overdue list.

## Approach and preconditions

xUnit HTTP black-box tests exercise `GET /api/incidents/overdue` over an actual loopback Kestrel listener. The current management ticket table has no due-date/overdue output, so Playwright would not exercise FR-12. The harness loads production controller routes and uses real PostgreSQL with production migrations in a uniquely named, automatically deleted database. SQL is used only to arrange input records, preserving the normal due-date triggers. Assertions use HTTP status codes and JSON only; no controller action or service method is called directly, and no database query supplies a verdict.

Every visibility case uses an authenticated Management identity. Current dev has no usable HTTP login scheme; a test-only authentication handler establishes identity while the endpoint's production authorization policy runs. This verifies role-gated endpoint behavior, not real login or the complete production startup pipeline. Negative visibility cases include an active overdue positive control to avoid passing on an empty response.

Past inputs use fixed `2020-01-01T12:00:00Z`; future input uses fixed `2099-01-01T12:00:00Z`. Boundary inputs use a microsecond-aligned UTC anchor A taken immediately before fixture adjustment and HTTP evaluation. The endpoint is warmed first. Both tests explicitly require completion in `[A, A+1 second)`; BB-04 uses due=A−1 second and BB-05 uses due=A+1 second. Thus BB-04 is 1–2 seconds past due at evaluation, and BB-05 is 0–1 second before due. No sleeps or retries conceal a behavior failure. A exceeded timing window fails explicitly as an infrastructure/precondition failure. Because current dev reads `DateTimeOffset.UtcNow` directly, these are guarded real-clock boundary checks, not frozen-time/tick-exact tests; a long process pause or system clock adjustment remains a limitation. Exact equality and microsecond-adjacent evaluation cannot be established deterministically without an external clock-controlled runtime or a production clock seam. No production code was changed for testing.

## Case evidence

| Test ID | Test basis / why selected | Black-box technique | Preconditions / input | Expected result | Actual automated result | Pass/fail | Traceability | Interpretation |
|---|---|---|---|---|---|---|---|---|
| FR12-BB-01 | Still-open request past due; representative overdue partition | Equivalence Partitioning | New incident, fixed 2020 due time; Management GET | Present once, `isOverdue=true`, correct due time/reference and positive overrun | HTTP 200; matching record and all output assertions passed | PASS | FR-12 / #33 / #116 | Management can identify an overdue open request and its due/overrun information |
| FR12-BB-02 | Request not yet due; representative non-overdue partition | Equivalence Partitioning | New incident, fixed 2099 due time; overdue positive control | Future incident absent; control present | HTTP 200; future ID absent, overdue control correctly surfaced | PASS | FR-12 / #33 / #116 | Future due dates do not enter the overdue list |
| FR12-BB-03 | Missing due time; requested missing-value partition | Equivalence Partitioning | Incident with no due time | Must not be incorrectly marked overdue | Explicit xUnit skip: `due_at` is mandatory and derived on insert; no public endpoint supplies this precondition | SKIPPED, no verdict | FR-12 / #33 / #116 | Unreachable input in current dev; changing schema or mapping null to a sentinel would test a different system |
| FR12-BB-04 | Past side of due threshold | Boundary Value Analysis | New incident; due=A−1 second, evaluation within guarded window | Present and overdue, due time unchanged, positive overrun within 1–2 seconds | HTTP 200; ID present, all fields and overrun bounds passed; timing guard passed | PASS | FR-12 / #33 / #116 | Past boundary behavior verified at the documented second-scale resolution |
| FR12-BB-05 | Future side of due threshold | Boundary Value Analysis | New incident; due=A+1 second, evaluation within guarded window; overdue control | Boundary incident absent; control present | HTTP 200; boundary ID absent, control present; timing guard passed | PASS | FR-12 / #33 / #116 | Future boundary behavior verified within the documented window |
| FR12-BB-06 (Resolved) | Requirement restricts overdue list to still-open incidents | Equivalence Partitioning | Resolved incident with fixed 2020 due time; open overdue control | Resolved incident absent; control present | HTTP 200; resolved ID absent, control correctly overdue | PASS | FR-12 / #33 / #116 | Resolved incidents excluded even with elapsed due dates |
| FR12-BB-06 (Closed) | Closed lifecycle partition | Equivalence Partitioning | Closed incident with fixed 2020 due time; open overdue control | Closed incident absent; control present | HTTP 200; closed ID absent, control correctly overdue | PASS | FR-12 / #33 / #116 | Closed incidents excluded even with elapsed due dates |
| FR12-BB-07 | Management-only visibility / negative role partition | Equivalence Partitioning | Authenticated Reporter GET | Access denied | HTTP 403 | PASS | FR-12 / #33 / #116 | Production endpoint authorization denies a non-management role under the fixture identity provider |

## Commands and results

The machine's default SDK is .NET 9; validation used the existing .NET 10.0.100 SDK at `/private/tmp/civicconnect-fr15-dotnet10`, with `DOTNET_CLI_HOME=/private/tmp/fr12-dotnet-home` and `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`. NuGet audit was disabled for these runs to avoid unrelated audit network calls. PostgreSQL 15 was available locally through Docker.

- `dotnet build -p:NuGetAudit=false`: **PASS**, 0 warnings, 0 errors.
- `dotnet test -p:NuGetAudit=false`: **PASS**, 44 passed, 1 skipped, 0 failed across both test projects (11 Data tests; 33 passed and 1 skipped in CivicConnect.Tests). This final run includes the final FR-12 changes.
- Dedicated FR-12 HTTP suite: **7 passed, 1 skipped, 0 failed**. The full solution run confirmed the same FR-12 results after adding positive controls.
- Playwright: not used; no FR-12 UI output exists in current dev.

## Reproduction

Use .NET 10 and a local PostgreSQL account with CREATE DATABASE permission. Set `FR12_CONNECTION_STRING` to its connection string. The fixture ignores the supplied database name, connects to `postgres` for provisioning, and creates/drops only its unique `fr12_bb_<guid>` database. Set `CONNECTION_STRING` separately to a disposable database for the repository's existing Data tests; those existing tests migrate the supplied database.

```sh
dotnet build
dotnet test
dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj \
  --filter FullyQualifiedName~OverdueVisibilityTests
```

Changes are limited to the HTTP tests, their fixture, one framework reference in the existing test project, and this evidence document. Due-time calculation after priority override is another FR-12 acceptance clause, outside the five requested visibility partitions and not claimed as covered here.
