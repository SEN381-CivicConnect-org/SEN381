# FR-07: minimal controlled status lifecycle

FR-07 in `docs/requirements/Functional Requirements/functional-requirements-V1.0.html`
explicitly allows NEW → ASSIGNED and rejects NEW → RESOLVED. The repository does
not specify a complete matrix. The minimal forward lifecycle derived from that
requirement and existing UI statuses is:

| Previous | Allowed next |
| --- | --- |
| NEW (Open in the UI) | ASSIGNED |
| ASSIGNED | IN_PROGRESS |
| IN_PROGRESS | RESOLVED |
| RESOLVED | CLOSED |
| CLOSED | None |

Every other transition, including repeated status, skip, backward, hold, and
reopen transitions, is rejected. Hold/reopen rules require a separately specified
policy. Existing display names remain intact; history uses canonical names and
UTC timestamps. Only authenticated Staff users with a name may perform transitions,
matching the current authentication provider's technician/supervisor portal role.

History shares the existing in-memory TicketService lifetime and is lost on restart.
No database persistence, generic audit, assignment ownership, or notification
feature is introduced. The Assigned transition represents the FR-07 status aspect
of claiming; team/assignment workflow remains outside this change.

Focused checks follow the existing executable-check project convention:

```sh
dotnet run --project tests/CivicConnect.StatusChecks
```

They cover every pair in the matrix, legacy display aliases, invalid input,
authorization, UTC actor history, and concurrent duplicate attempts.
