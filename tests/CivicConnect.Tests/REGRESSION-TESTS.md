# Cross-Workflow & Post-Fix Verification: Regression Testing

## Why Regression Testing is Needed Alongside Existing E2E Tests

While the Playwright suite (`tests/CivicConnect.E2E/`) covers critical browser user flows (FR-11 basic requester ticket list/detail access, FR-15 queue filtering, and a single status step), dedicated automated regression tests in `tests/CivicConnect.Tests/TicketWorkflowRegressionTests.cs` are required for three reasons:

1. **Cross-feature regression boundaries not exercised by E2E:**
   When `MyTickets.razor`, `TicketDetail.razor`, and `TicketService.cs` were modified to fix the FR-11 requester ownership defects (`D1` and `D2`), `TicketDetail.razor` changed how it loads tickets (`Tickets.FindVisible(Id, user)`) and conditionally loads notes (`if (ticket is not null) await LoadNotes()`). The browser E2E suite never adds or inspects ticket notes (FR-16) or audit records (FR-08), and the standalone `CivicConnect.NoteChecks` / `CivicConnect.AuditChecks` console utilities are outside `CivicConnect.sln` and never render `TicketDetail.razor`.
2. **Post-fix confirmation (defect re-testing) of Equivalence Partitioning (`D1` & `D2`):**
   When `RequestVisibilityEquivalencePartitionTests` was originally authored on the Equivalence Partitioning branch prior to merging with `dev`, execution was blocked by `.NET 9` (`NETSDK1045`) and source inspection identified defects **D1** (hardcoded `Bernard Small` filter in `MyTickets.razor`) and **D2** (`Tickets.Find(Id)` without an ownership check in `TicketDetail.razor`). Re-running the 10 Equivalence Partitioning cases on `test/Regression_Testing` with `.NET 10.0.401` after the FR-11 fix serves as formal confirmation testing that D1 and D2 are resolved, while the 10 new `TicketWorkflowRegressionTests` verify that the fix introduced no regressions in adjacent features.
3. **Deterministic, zero-dependency CI execution:**
   Playwright E2E tests require a running Docker stack and manual `.env` credentials (`E2E_REQUESTER_A_PASSWORD`, etc.), so they do not run inside `.github/workflows/db-migrations.yml`. `CivicConnect.Tests` runs in-process on every `dotnet test` invocation in under 2 seconds.

---

## Regression Scope and Test Basis

- **FR-11 × FR-16 × FR-07 (`TicketDetail.razor` rendered component boundary):**
  - Owning requesters viewing `TicketDetail` continue to see their public notes and status transition history (`NEW → ASSIGNED`), while internal staff notes and staff-only mutation controls (`Add a note`, `Update status`) remain withheld (`REG-01`, `REG-02`).
  - Foreign requesters attempting to view another requester's ticket see `"Ticket not found"` and neither public notes, internal notes, nor status history are loaded or leaked in the rendered HTML (`REG-03`, `REG-04`).
  - Authorised `Staff` and `Management` users rendering `TicketDetail` after the FR-11 ownership refactoring retain full visibility of both public and internal notes as well as the note creation form (`REG-05`, `REG-06`).
- **FR-11 × FR-05 × FR-07 × FR-16 × FR-08 (Full lifecycle invariant preservation):**
  - Creating a request via `CreateForRequester` (ignoring spoofed form ownership), adding internal and public notes, and advancing through all four forward transitions (`Open → Assigned → In Progress → Resolved → Closed`) preserves the immutable `FR-05` priority calculation snapshot (`ImpactLevel`, `UrgencyLevel`, `ComputedPriorityId`, `PriorityId`, `Priority`), `FR-11` stable `RequesterId` ownership, `FR-16` note visibility filtering, and `FR-08` structured audit entries (`CREATE`, `ADD_NOTE`, `CHANGE_STATUS`) without leaking free-text PII (`REG-07`).
- **FR-07 × FR-16 × FR-08 (Negative mutation & audit integrity):**
  - Unauthorized actors (`Requester` and `Anonymous`) attempting to append notes or transition status are rejected without mutating ticket state, status history, notes, or the audit log (`REG-08`, `REG-09`).
- **FR-11 × FR-14 × FR-15 (Staff queue active vs. terminal lifecycle filtering):**
  - A requester-submitted ticket remains visible in `GetStaffQueue` while `Open`, `Assigned`, and `In Progress`, exits the active staff queue once `Resolved` or `Closed`, and remains visible to its owning requester throughout (`REG-10`).

---

## Execution and Results

Execution date: **16 October 2026**, Africa/Johannesburg. SDK: **10.0.401**; runner runtime: .NET 10.0.12.
- **Focused Regression + Equivalence Partitioning Confirmation run:** **20 run, 20 passed, 0 failed, 0 skipped.**
- **Full `CivicConnect.Tests` regression suite run:** **98 run, 98 passed, 0 failed, 0 skipped.**

### 1. New Regression Test Cases (`TicketWorkflowRegressionTests`)

| Test ID | Feature Interaction | Scenario | Expected Observable Result | Actual Result | Pass/Fail | Regression Found |
| --- | --- | --- | --- | --- | --- | --- |
| REG-01 | FR-11 × FR-16 × FR-07 | Requester A (`USER-A`) opens own `REG-A` detail | Public note and `NEW → ASSIGNED` visible; internal note and staff controls absent | Public note & history rendered; internal note & controls absent | Pass | None |
| REG-02 | FR-11 × FR-16 × FR-07 | Requester B (`USER-B`) opens own `REG-B` detail | Public note and `NEW → ASSIGNED` visible; internal note and staff controls absent | Public note & history rendered; internal note & controls absent | Pass | None |
| REG-03 | FR-11 × FR-16 | Requester A (`USER-A`) opens foreign `REG-B` detail | `Ticket not found`; neither public/internal notes nor history disclosed | `Ticket not found`; 0 notes/history rendered | Pass | None |
| REG-04 | FR-11 × FR-16 | Requester B (`USER-B`) opens foreign `REG-A` detail | `Ticket not found`; neither public/internal notes nor history disclosed | `Ticket not found`; 0 notes/history rendered | Pass | None |
| REG-05 | FR-11 × FR-16 | Staff (`STAFF-1`) opens requester ticket `REG-A` | Both public and internal notes + `Add a note` control visible | Both notes and note form rendered | Pass | None |
| REG-06 | FR-11 × FR-16 | Management (`MGR-1`) opens requester ticket `REG-A` | Both public and internal notes + `Add a note` control visible | Both notes and note form rendered | Pass | None |
| REG-07 | FR-05 × FR-07 × FR-08 × FR-11 × FR-16 | `CreateForRequester` → 2 notes → full `Open` to `Closed` lifecycle | Priority snapshot (`Critical`), `USER-A` ownership, note isolation, and 7 PII-free audit records preserved | All snapshots, notes, and 7 audit records intact | Pass | None |
| REG-08 | FR-07 × FR-16 × FR-08 | Requester attempts `AddNote` and `ChangeStatus` | Both rejected; ticket stays `Open`; 0 notes/history added; only `CREATE` audit entry remains | Rejected; state and audit log unchanged | Pass | None |
| REG-09 | FR-07 × FR-16 × FR-08 | Anonymous user attempts `AddNote` and `ChangeStatus` | Both rejected; ticket stays `Open`; 0 notes/history added; only `CREATE` audit entry remains | Rejected; state and audit log unchanged | Pass | None |
| REG-10 | FR-11 × FR-14 × FR-15 | Requester creates ticket; Staff advances `Open → Closed` | In `GetStaffQueue` while `Open`/`Assigned`/`In Progress`; excluded when `Resolved`/`Closed`; requester retains visibility | Active queue includes through `In Progress`, excludes at `Resolved`/`Closed` | Pass | None |

### 2. Post-Fix Confirmation Re-Test of Equivalence Partitioning (`RequestVisibilityEquivalencePartitionTests`)

Running the 10 Equivalence Partitioning cases on `dev` after the FR-11 fix with `.NET 10.0.401` confirms that previously suspected defects **D1** (`MyTickets.razor` fixed-name filter) and **D2** (`TicketDetail.razor` missing ownership check) are resolved:

| Test Method | Cases | Pre-Fix Status (README) | Post-Fix Actual Result (.NET 10.0.401) | Pass/Fail |
| --- | --- | --- | --- | --- |
| `EP_RequesterOwnRequest_IsVisibleInHistory` | 2 (`Bernard Small`, `Other Requester`) | Blocked (`D1` suspected on B) | Both requesters see their own request link | Pass (2/2) |
| `EP_RequesterOwnRequest_DetailsAreVisible` | 2 (`Bernard Small`, `Other Requester`) | Blocked | Both requesters see their own title & description | Pass (2/2) |
| `EP_RequesterOtherUsersRequest_IsNotVisibleInHistory` | 2 (`Bernard Small`, `Other Requester`) | Blocked (`D1` suspected on B) | Neither requester sees the other's link or title | Pass (2/2) |
| `EP_RequesterOtherUsersRequest_DetailsAreNotDisclosed` | 2 (`Bernard Small`, `Other Requester`) | Blocked (`D2` suspected on both) | Neither requester can view the other's details | Pass (2/2) |
| `EP_PrivilegedUser_RequesterRequest_IsVisible` | 2 (`Staff`, `Management`) | Blocked | Both privileged roles view the request details | Pass (2/2) |

---

## Running and Evidence

From the repository root with the .NET 10 SDK:

```sh
dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj --filter "FullyQualifiedName~TicketWorkflowRegressionTests|FullyQualifiedName~RequestVisibilityEquivalencePartitionTests" --no-restore --logger "console;verbosity=detailed" --logger "trx;LogFileName=reg-results.trx" --results-directory tests/CivicConnect.Tests/evidence -m:1
```

To execute the complete `CivicConnect.Tests` unit/component regression suite (all 98 tests):

```sh
dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj --no-restore --verbosity normal -m:1
```

- [Complete regression & EP confirmation terminal output](evidence/reg-execution-latest.txt) (preserves earlier `ep-execution-*` and `st-execution-*` logs untouched).
