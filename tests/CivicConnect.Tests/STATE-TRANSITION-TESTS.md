# Request status: State Transition Testing

## Workflow inspected before implementation

The test basis is [FR-07](../../docs/requirements/Functional%20Requirements/functional-requirements-V1.0.html)
and the explicit [minimal controlled lifecycle decision](../../docs/decisions/0004-status-transitions.md).
The actual Blazor detail page calls the public `TicketService.ChangeStatus`
action; its status control is restricted to Staff. Tests use this public action
and `Find` to observe the resulting ticket status and history, rather than private
methods, reflection, or inspection of the internal transition table.

The Blazor workflow has five states:

| Canonical state | UI label | Permitted next state |
| --- | --- | --- |
| NEW | Open | ASSIGNED |
| ASSIGNED | Assigned | IN_PROGRESS |
| IN_PROGRESS | In Progress | RESOLVED |
| RESOLVED | Resolved | CLOSED |
| CLOSED | Closed | None |

A new ticket created through `TicketService.Create` starts Open. Tests arrange an
existing request at the indicated initial state, then change state exclusively
through the public action. The event is Staff selecting the next status and
requesting Update status; the test calls the same public application action,
without simulating the browser click. Accepted changes return success and expose
the new status with one previous/next/actor/UTC-time history entry. The decision
permits only authenticated, named Staff actors (technician/supervisor portal
role). Requester, Management, and anonymous actors cannot use this particular
status action. Management's separate API assignment permissions are not denied
by this test expectation.

FR-07 explicitly prohibits NEW → RESOLVED: rejection must explain the failure,
preserve the initial state, and create no history. This is the negative transition
selected for the tests. The lifecycle decision also rejects skips, backwards,
repeated statuses, and transitions out of CLOSED; this suite does not enumerate
all those pairs.

Relevant implementation sources inspected:

- `src/CivicConnect.Web/Services/TicketService.cs`: creation and public status action.
- `src/CivicConnect.Web/Components/Pages/Shared/TicketDetail.razor`: status display,
  Staff-only control, and call to the public action.
- `src/CivicConnect.Data/Entities/IncidentStatus.cs`: the database additionally
  defines ON_HOLD, REJECTED, and MERGED. These have no transition action in this
  minimal Blazor workflow and are not fabricated as UI transitions here.
- `src/CivicConnect.Api/Controllers/IncidentsController.cs`: separate claim/assign
  operations can move NEW to ASSIGNED. Claim accepts staff/technician/supervisor
  roles; assignment also permits Management/operations manager/administrator.
- `src/CivicConnect.Api/Controllers/ResolutionsController.cs` and
  `Services/ResolutionService.cs`: a separate database resolution workflow permits
  resolution from IN_PROGRESS or ON_HOLD (and configured transitions), subject to
  resolution-code/asset validation (FR-10). This suite does not test those actions
  or conflate that workflow with `TicketService.ChangeStatus`.

FR-13's notification example mentions ASSIGNED → RESOLVED, whereas the explicit
Blazor lifecycle decision requires IN_PROGRESS in between. This suite follows
the documented status action matrix and does not infer a direct resolution or
notification rule from that example. It makes no claim of exhaustive coverage
of all application workflows.

## Why this is State Transition Testing

Each case identifies **initial state → action → expected next state**. The result
of the same status action depends on the current state and actor. Assertions
check public observable results, including rejection and unchanged state for the
explicitly prohibited transition, rather than testing how the lifecycle is
implemented. State/history setup occurs only before the behaviour under test.

The existing xUnit project and production `TicketService` are reused. The small
claims helper follows the existing `CivicConnect.StatusChecks` approach. No new
framework or packages are introduced. Each case gets a fresh service and ticket.

## Execution and results

Execution date: **16 October 2026**, Africa/Johannesburg. SDK: **10.0.402**;
runner runtime: .NET 10.0.13. **12 run, 12 passed, 0 failed, 0 skipped.**
No failing test names, assertion failures, or failure stack traces were produced
by the successful run. No application defects were exposed by this suite.

| Test ID | Initial State | Event/Action | Expected Next State | Actual Result | Pass/Fail | Defect found |
| --- | --- | --- | --- | --- | --- | --- |
| ST-01 | Open | Staff sets ASSIGNED | Assigned | Assigned; one correct history entry | Pass | None |
| ST-02 | Assigned | Staff sets IN_PROGRESS | In Progress | In Progress; one correct history entry | Pass | None |
| ST-03 | In Progress | Staff sets RESOLVED | Resolved | Resolved; one correct history entry | Pass | None |
| ST-04 | Resolved | Staff sets CLOSED | Closed | Closed; one correct history entry | Pass | None |
| ST-05 | Open | Assign, then read again | Assigned | Assigned retained; one history entry | Pass | None |
| ST-06 | Assigned | Start progress, then read again | In Progress | In Progress retained; one history entry | Pass | None |
| ST-07 | In Progress | Resolve, then read again | Resolved | Resolved retained; one history entry | Pass | None |
| ST-08 | Resolved | Close, then read again | Closed | Closed retained; one history entry | Pass | None |
| ST-09 | Open | Staff attempts direct RESOLVED | Open (rejected) | Rejected with message; Open; no history | Pass | None |
| ST-10 | Open | Requester attempts ASSIGNED | Open (rejected) | Rejected with message; Open; no history | Pass | None |
| ST-11 | Open | Management attempts ASSIGNED | Open (rejected) | Rejected with message; Open; no history | Pass | None |
| ST-12 | Open | Anonymous actor attempts ASSIGNED | Open (rejected) | Rejected with message; Open; no history | Pass | None |

ST-01–04 map to `ST_ForwardLifecycle_StaffAction_TransitionsToDocumentedNextState`;
ST-05–08 to `ST_AcceptedTransition_ReadAgain_RetainsNextState`;
ST-09 to `ST_OpenRequest_DirectResolveAction_IsRejectedAndRemainsOpen`;
ST-10–12 to `ST_OpenRequest_RestrictedActorAssignAction_IsRejectedAndRemainsOpen`.
Full parameters and individual results are recorded in the evidence.

## Running and evidence

From the repository root with .NET 10:

```sh
dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj --filter FullyQualifiedName~RequestStatusStateTransitionTests
```

The successful evidence run used this exact command (after a successful restore):

```sh
dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj --filter FullyQualifiedName~RequestStatusStateTransitionTests --no-restore --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=st-results.trx' --results-directory tests/CivicConnect.Tests/evidence -m:1
```

- [Complete environment, initial attempt, and retry output](evidence/st-execution-latest.txt).
- [Successful retry output](evidence/st-execution-unsandboxed.txt).
- [Machine-readable results](evidence/st-results.trx).

The first run compiled successfully but VSTest could not bind its local socket
inside the sandbox (`SocketException (13): Permission denied`); it aborted before
executing cases. The retry outside the sandbox passed all 12. The initial
infrastructure stack trace is preserved separately from actual test outcomes.
The initial restore also printed a CSSM_ModuleLoad diagnostic; restore completed.

For lecturer screenshots, open the successful output and capture the test names
with input parameters and Passed lines, then the final `Total tests: 12` and
`Passed: 12` summary. Capture `dotnet --version` from the complete log and this
result table as supporting evidence. No browser screenshot is needed for these
public-behaviour tests.

## Limitations

These tests exercise public application behaviour; they do not render Blazor,
verify event wiring, test HTTP routing/authentication, or run the database API.
The action result's explanatory message is asserted nonempty without prescribing
wording. History is observable public ticket data also displayed by the detail
page. Rereads use the same service lifetime: in-memory retention is tested, not
restart or database persistence. The documented service loses history on restart.
No production code was changed and no requirement-based assertions were weakened.
