# Request visibility: Equivalence Partitioning

Run from the repository root with the **.NET 10 SDK**:

```sh
dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj --filter FullyQualifiedName~RequestVisibilityEquivalencePartitionTests
```

No running API, database, credentials, or environment secrets are needed for these
component tests. Dependencies are restored by `dotnet test` as usual.

`RequestVisibilityEquivalencePartitionTests.cs` uses the existing xUnit framework
and the same ASP.NET Core `HtmlRenderer` approach used by QueueChecks. Each test
renders the actual Blazor page with controlled public ticket data and an
authenticated principal, then checks the HTML shown to the user. It does not
invoke private methods or inspect component fields. The authentication state is
a test substitute; these tests do not verify login, HTTP routing/authorization
middleware, browser interaction, or API security.

The inputs are divided by the relationship between the signed-in user and the
request, with representatives from each expected-behaviour class:

1. **Requester + own request:** their history links to the request and its detail
   page shows the title and description.
2. **Requester + someone else's request:** history omits the other request's link,
   and direct detail access does not disclose its title or description. No exact
   denial message is required. History is tested from both requesters' identities
   to avoid treating a fixed demo account as proof of ownership filtering.
3. **Authorised Staff/Management + requester request:** request details remain
   visible. The Staff representative is the assigned technician in IT Support;
   this does not assume unrestricted cross-team staff access.

This is Equivalence Partitioning because representative inputs are selected from
classes expected to receive the same access decision, rather than enumerating
all possible users and requests. FR-11 documents requester history isolation;
FR-04/FR-14 support the assigned staff example, and the existing Management
All Tickets view supports broader management visibility.

## Existing defects these tests can expose

The implementation was not changed to satisfy the tests. At implementation time,
`MyTickets.razor` filters against the fixed name `Bernard Small`, and
`TicketDetail.razor` loads the selected ticket without an ownership check.
Consequently the second requester's history cases and the cross-requester detail
case are expected to fail until those defects are fixed separately. This is a
source-inspection expectation, not a recorded test result: local execution was
blocked by NETSDK1045 because only SDKs through .NET 9 were installed.

## Test inventory and execution status

Five xUnit theories supply ten cases:

| Test | Cases | Expected observable result |
| --- | --- | --- |
| `EP_RequesterOwnRequest_IsVisibleInHistory` | Both requesters | Own request link appears |
| `EP_RequesterOwnRequest_DetailsAreVisible` | Both requesters | Own title and description appear |
| `EP_RequesterOtherUsersRequest_IsNotVisibleInHistory` | Both requesters | Other request link and title are absent |
| `EP_RequesterOtherUsersRequest_DetailsAreNotDisclosed` | Both requesters | Other title and description are absent |
| `EP_PrivilegedUser_RequesterRequest_IsVisible` | Assigned Staff, Management | Request title and description appear |

Both requester identities exercise the same ownership classes independently of
which demo name is signed in. Using an existing request in the negative cases
prevents a missing record from masquerading as successful access control. The
positive detail cases use the same records to check that they are renderable.

On 16 October 2026, no cases executed. The normal command encountered an MSBuild
named-pipe sandbox error (`SocketException (13): Permission denied`). Retrying
with `--no-restore -m:1` reached the definitive SDK blocker:

```text
error NETSDK1045: The current .NET SDK does not support targeting .NET 10.0.
```

The selected SDK was `9.0.102`; no .NET 10 SDK is installed. Compilation and actual
pass/fail counts therefore remain unverified. Source inspection predicts two
history failures for the second requester and two cross-requester detail
failures. Keep the requirement-based assertions when running with .NET 10;
do not change them to accept data disclosure.

## Execution evidence — 16 October 2026

[Full terminal output](evidence/ep-execution-2026-10-16.txt) records the installed
SDKs, selected SDK, diagnostic command, exact NETSDK1045 error, and exit codes.
.NET 10 is not installed. SDK 9.0.102 was selected. No compiler diagnostics or
assertion results were produced: target-framework validation stopped execution.
No test-code changes were justified by this environment error.

**Counts:** 10 planned cases; 0 run; 0 passed; 0 failed; no runner-reported skips
(the runner never started). All 10 cases are blocked, not skipped or failed.

In the table, A = Bernard Small (`EP-OWN`), B = Other Requester (`EP-OTHER`).
Actual result “Blocked” means NETSDK1045, with no observed page output.

| Test name | EP | Scenario | Expected result | Actual result | Pass/Fail | Defect found |
| --- | --- | --- | --- | --- | --- | --- |
| `EP_RequesterOwnRequest_IsVisibleInHistory` | 1: owns | A lists own request | EP-OWN link visible | Blocked | Not evaluated | None confirmed by execution |
| `EP_RequesterOwnRequest_IsVisibleInHistory` | 1: owns | B lists own request | EP-OTHER link visible | Blocked | Not evaluated | D1 suspected |
| `EP_RequesterOwnRequest_DetailsAreVisible` | 1: owns | A opens EP-OWN | Own title/description visible | Blocked | Not evaluated | None confirmed by execution |
| `EP_RequesterOwnRequest_DetailsAreVisible` | 1: owns | B opens EP-OTHER | Own title/description visible | Blocked | Not evaluated | None confirmed by execution |
| `EP_RequesterOtherUsersRequest_IsNotVisibleInHistory` | 2: does not own | A lists requests | B's link/title absent | Blocked | Not evaluated | None confirmed by execution |
| `EP_RequesterOtherUsersRequest_IsNotVisibleInHistory` | 2: does not own | B lists requests | A's link/title absent | Blocked | Not evaluated | D1 suspected |
| `EP_RequesterOtherUsersRequest_DetailsAreNotDisclosed` | 2: does not own | A opens EP-OTHER | B's title/description absent | Blocked | Not evaluated | D2 suspected |
| `EP_RequesterOtherUsersRequest_DetailsAreNotDisclosed` | 2: does not own | B opens EP-OWN | A's title/description absent | Blocked | Not evaluated | D2 suspected |
| `EP_PrivilegedUser_RequesterRequest_IsVisible` | 3: privileged | Assigned IT Support Staff opens EP-OTHER | Title/description visible | Blocked | Not evaluated | None confirmed by execution |
| `EP_PrivilegedUser_RequesterRequest_IsVisible` | 3: privileged | Management opens EP-OTHER | Title/description visible | Blocked | Not evaluated | None confirmed by execution |

### Suspected defects investigated

- **D1 — fixed requester history:** source review of `MyTickets.razor` confirms
  that its list filters by `Bernard Small`. Predicted observable behaviour for B:
  their own request is missing and A's request appears. This would violate both
  own-request visibility and exclusion of other requesters' requests. The two
  history theories above contain the affected B cases.
- **D2 — detail ownership:** source review of `TicketDetail.razor` confirms that it
  loads `Tickets.Find(Id)` without an ownership check. Predicted observable
  behaviour: either requester can read the other request's title/description.
  Both cases in `EP_RequesterOtherUsersRequest_DetailsAreNotDisclosed` would
  violate the non-disclosure expectation.

These are source findings and predicted failures, not executed black-box results.
No production files or test assertions were changed during this evidence step.

### Lecturer submission evidence

For the current blocked execution, open `evidence/ep-execution-2026-10-16.txt`
and screenshot (1) the SDK list plus selected version and (2) the diagnostic
command, full NETSDK1045 error, and exit code. Include this table to show that no
pass/fail result has been claimed. A page screenshot would not demonstrate an
automated test result; none was generated.

Once .NET 10 is available, run the exact command at the top of this README. For
additional machine-readable and verbose evidence, run:

```sh
dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj --filter FullyQualifiedName~RequestVisibilityEquivalencePartitionTests --logger "console;verbosity=detailed" --logger "trx;LogFileName=ep-results.trx" --results-directory tests/CivicConnect.Tests/evidence
```

Screenshot the runner's final totals and each failing case's name, input values,
assertion message, and stack trace. Preserve the TRX file and terminal output.
Report the observed totals and failures even if they differ from the source
predictions; keep requirement-based failing assertions intact.

## State Transition Testing

See [State Transition tests and execution results](STATE-TRANSITION-TESTS.md) for
the inspected workflow, test basis, twelve-case result table, commands, evidence,
and limitations. The focused State Transition suite ran with .NET 10.0.402:
**12 passed, 0 failed, 0 skipped**. This result does not rerun or supersede the EP
execution records above.

## Regression Testing

See [Regression tests and post-fix confirmation results](REGRESSION-TESTS.md) for
the cross-feature regression suite (`TicketWorkflowRegressionTests`), post-fix
confirmation of `RequestVisibilityEquivalencePartitionTests` (defects D1 and D2
verified resolved after merging the FR-11 ownership fix), ten-case regression
result table, commands, and [execution evidence](evidence/reg-execution-latest.txt).
The combined regression and EP confirmation run executed with .NET 10.0.401:
**20 passed, 0 failed, 0 skipped** (**98 passed** across the full `CivicConnect.Tests`
suite). Original pre-fix EP and State Transition evidence files are preserved intact.

