# Architecture Decision Review

## 2. Review methodology

Compared the original Milestone 2 PED v2.0 and the team's Milestone 2 Architecture, Technology & Initial Design Baseline (sections 5.2–5.6), alongside ADR-ARCH-001, ADR-004, DD-01, DD-02, records 0001–0003, and staging project configuration and code. Historical rationale is distinguished from source-code observation; decisions that need team approval remain provisional.

## 3. Decision review matrix

| ID | Original / accepted choice | Finding | Action |
| --- | --- | --- | --- |
| ADR-ARCH-001 | Layered monolith (examiner-confirmed classification) | PARTIAL: layered modules; separate API and Web deployables | Clarify deployment topology and status |
| ADR-004 | C#/.NET 10, Blazor WASM, PostgreSQL 16, Docker | PARTIAL: .NET 10 and Docker confirmed; interactive server and PostgreSQL 15 Compose image | Amend rendering and version claims |
| DD-01 | Explicit status orchestration; optional Observer reactions | PARTIAL: transaction/rollback/audit on submission shown; event dispatch not established | Trace status transitions and any broker publisher |
| DD-02 | Data-driven configuration; defer algorithmic Strategy until needed | PARTIAL: database reference data and IPriorityMatrix present | Distinguish algorithm interface from configurable rules engine |
| 0001 | EF Core migrations, Npgsql, .NET 10 | CONFIRMED IN CONFIGURATION | Attach migration execution output |
| 0002 | PostgreSQL relational engine | CONFIRMED IN CONFIGURATION | Validate staging DB version and service |
| 0003 | Snake_case, timestamptz, migration rules | CONFIRMED IN CONFIGURATION | Check applied migrations and actual schema |

## 4. Findings and controlled amendments

### 4.1 ADR-ARCH-001 — Architecture topology

Original justification: a layered application prevents the overhead of microservices, while retaining testability and clear separation of roles for a small team. Evidence of current solution: separation of CivicConnect.Web, CivicConnect.Api and CivicConnect.Data projects; docker-compose.yml distinguishes frontend-web and backend-api services. This allows a layered monolithic approach with distinct Web and API deployment containers; the initial phrase “single deployable application” needs deployment topology clarification.

Proposed final wording: “CivicConnect uses a layered monolithic approach with separately containerised Web and API hosts that use a relational data layer. The application does not use independently managed business microservices and comprises several coordinated containers.” Layered-monolith categorization is based on examiner’s remarks; ADR approval by the team is yet to come.

### 4.2 ADR-004 — Technology stack

Confirmed: both Web and API project files target net10.0; the database context configures EF Core/Npgsql; Docker Compose defines the operational topology. Difference A: original record describes Blazor WebAssembly, but Web/Program.cs uses AddInteractiveServerComponents and AddInteractiveServerRenderMode. Difference B: the decision cites PostgreSQL 16, whereas docker-compose.yml specifies postgres:15-alpine.

Proposed amendment: “ASP.NET Core / Blazor Web App with interactive server rendering, EF Core/Npgsql, PostgreSQL as the persistence platform, and Docker Compose deployment.” State an exact PostgreSQL version only after comparing the release environment to the compose configuration. Do not claim the broker publishes notifications without tracing the producer/consumer code and a working test.

### 4.3 DD-01 — Status-transition reactions

Original decision: mandatory transition validation, persistence and history remain in explicit orchestration; optional secondary reactions may use Observer/events if justified. Inspected SubmissionService contains a database transaction, rollback, idempotency controls, and initial status-history creation. This is evidence for transaction-sensitive intake, not a complete audit of every status-change path or an Observer implementation.

Proposed wording: “Explicit orchestration is supported by the reviewed submission path; event-based secondary reactions remain unverified for final-release purposes.” Link actual transition handlers and test evidence before claiming full compliance.

### 4.4 DD-02 — Controlled configuration

Original decision favoured persisted, role-controlled configuration over office-specific code; Strategy was deferred unless real algorithmic variation emerged. Current API reads active categories and locations from database tables; both Web and API implement IPriorityMatrix / PriorityMatrix for deterministic impact-and-urgency calculation. An interface encapsulating one algorithm is not proof of runtime-swappable strategies or a general-purpose rules engine.

Proposed wording: “Office values are data-driven, while a dedicated priority-calculation abstraction contains the shared deterministic algorithm. This does not introduce user-defined workflows or arbitrary business rules.” Confirm administrative authorisation and configuration audit behaviour separately.

## 5. Alternatives and consequences

| Decision | Alternatives already considered | Accepted trade-off / consequence |
| --- | --- | --- |
| Architecture | Tightly coupled monolith; distributed microservices | Layering improves separation but requires disciplined boundaries; multi-container deployment adds coordination. |
| Frontend | React/Angular JS toolchain; Blazor WASM in original ADR | Shared C# skillset; interactive-server hosting introduces server-side circuit and connectivity considerations. |
| Persistence | NoSQL/document store, SQLite, MySQL/MariaDB, SQL Server | Relational constraints and migrations; database-specific operations and operational backup obligations. |
| Deployment | Bare metal / VM | Repeatable container configuration; image/version drift and secrets management must be controlled. |
| Ticket reactions | Everything inside a service; Observer | Explicit mandatory operations are easier to trace; optional asynchronous dispatch requires failure/retry semantics. |
| Configuration | Hard-coded values; generic Strategy/rules engine | Managed data reduces branching but requires authorization, validation and audit controls. |

## 6. Evidence register — reviewed source

E01 — Architecture ADR and alternatives: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/docs/architecture/architecture-V1.0.html

E02 — Technology ADR: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/docs/decisions/technology-stack-V1.0.html

E03 — DD-01 and DD-02: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/docs/decisions/design-decisions-V1.0.html

E04 — Accepted .NET/EF stack: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/docs/decisions/0001-implementation-stack.md

E05 — Accepted PostgreSQL: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/docs/decisions/0002-database-postgresql.md

E06 — Accepted schema rules: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/docs/decisions/0003-schema-conventions.md

E07 — Interactive server rendering and auth DI: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/src/CivicConnect.Web/Program.cs

E08 — API registration and reference-data endpoints: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/src/CivicConnect.Api/Program.cs

E09 — Npgsql and naming conventions: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/src/CivicConnect.Data/CivicConnectDbContext.cs

E10 — Transactional intake: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/src/CivicConnect.Api/Services/SubmissionService.cs

E11 — Priority algorithm: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/src/CivicConnect.Web/Services/PriorityMatrix.cs

E12 — DB login and claims: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/src/CivicConnect.Web/Services/AppAuthenticationStateProvider.cs

E13 — Container topology and database image: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/docker-compose.yml

E14 — net10.0 target: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/src/CivicConnect.Web/CivicConnect.Web.csproj

E15 — net10.0 target: https://github.com/SEN381-CivicConnect-org/SEN381/blob/staging/src/CivicConnect.Api/CivicConnect.Api.csproj

## 8. Proposed decision disposition

Retain original documents unchanged as historical records. Create amended versions or a controlled addendum for ADR-ARCH-001 and ADR-004 once team approval is recorded. Update DD-01 and DD-02 implementation-evidence fields with verified links rather than replacing the original design rationale. Retain 0001–0003 as Accepted, subject to release evidence checks. Final release approval remains PENDING.

## 11. Source-derived original decisions and alternatives

### 11.1 ADR-ARCH-001 — architecture selection

The authors were able to compare the following architectural approaches: (a) the approach that was labeled “Layered Modular Monolith” initially, (b) the tightly coupled monolith and (c) microservices/distributed services. It was later revealed by the examiner that the chosen architecture was the layered monolith since there was a need for separating concerns for a three-person team in order to have a maintainable, testable and lightweight system. The phrase used is “single deployable”, and it is significant due to the fact that the Docker Compose file introduces Web and API services.

### 11.2 ADR-004 — technology choice

The historical choice was C#/.NET 10 LTS, Blazor WebAssembly, ASP.NET Core, PostgreSQL 16 and Docker. Reasons included single-language development and peer review, relational integrity and transactions, and portable deployment. Alternatives discussed included React/Angular or mixed-language stacks, NoSQL/document stores and bare-metal/VM-style deployment. The later code review found Interactive Server rendering and a postgres:15-alpine image in the reviewed Compose configuration; these are documented divergences, not retroactive changes to M2.

### 11.3 DD-01 — status reactions

The original design considered explicit orchestration versus Observer. It deliberately assigned mandatory status validation, persistence and audit/history to explicit orchestration, with Observer applicable only to independently varying, non-critical secondary responses such as optional requester notifications. A database “subscriber” row used to associate reporters with incidents must not be presented as proof of an implemented Observer event-dispatch architecture.

### 11.4 DD-02 — office configuration

The original decision preferred validated, authorised configuration records for categories, roles, classifications, areas and teams rather than per-office code. Strategy was deferred for general configuration unless distinct algorithms or policies emerged. The M2 ASR-03 text also refers to one priority algorithm behind an interface: that narrowly scoped algorithm abstraction is compatible with controlled configuration and does not establish a generic configurable rules engine.

### 11.5 Persistence, integrity and deployment constraints

The M2 baseline established relational integrity, transaction correctness, controlled access, auditability, bounded configurability, portability, backup/recovery and observability as design drivers. These explain the accepted implementation decisions 0001 (EF/Npgsql migration tooling), 0002 (PostgreSQL) and 0003 (schema conventions). Claims of operational achievement require independent restore, monitoring, migration and test evidence.

## 14. Verified pull request and test evidence links

Evidence checked 18 October 2026. The PRs below were verified as merged to dev through GitHub metadata. This is development integration evidence, not proof that the commits are contained in the final staging/release commit. Test-file links prove that a test artifact exists; pass/fail claims require execution records.

| Decision | Verified merged PR (dev) | Relevance |
| --- | --- | --- |
| ADR-ARCH-001 / 0001–0003 | PR #11 – database foundation/migrations | Migration code and schema conventions |
| ADR-004 / 0001 | PR #69 – .NET 10 transition | Framework target |
| ADR-004 / deployment | PR #169 – Docker environments | Development/staging Docker configuration |
| ADR-004 / deployment | PR #173 – deployment decision | Operational deployment design; not ADR sign-off |
| ADR-ARCH-001 / Identity | PR #76 – database-backed authentication | Database-backed login/roles |
| DD-02 | PR #72 – service catalogue | Data-driven categories |
| DD-02 | PR #73 – location hierarchy | Locations and impact fields |
| DD-02 | PR #87 – FR-05 | Priority and related infrastructure |
| DD-01 | PR #99 – status transition persistence | Transition table/history |
| DD-01 | PR #102 – notes and notifications | Notification/outbox data design |
| DD-01 | PR #93 – FR-13 | Status-change notification application logic |
| DD-01 | PR #106 – FR-07 | Controlled workflow transitions |
| DD-01 / tests | PR #139 – state-transition tests | Testing implementation |
| 0002 / concurrency | PR #118 – concurrency | Concurrent submission safeguards |
| DD-02 / audit | PR #131 – priority override audit log | Audit records |
| ADR-ARCH-001 / access | PR #163 – role management | Authorisation delegation |
| ADR-ARCH-001 / runtime | PR #159 – dummy-data removal | Legacy data remediation; release verification required |
| 0003 / permissions | PR #148 – least-privilege DB | Database privilege configuration |

### 14.1 Test source and execution artifacts

| Artifact | Link | Evidence limitation |
| --- | --- | --- |
| Priority-matrix unit tests | PriorityMatrixTests.cs | Test implementation; rerun required for final release |
| State-transition tests | RequestStatusStateTransitionTests.cs | Test implementation |
| State-transition execution record | st-execution-latest.txt | Existing output; check branch/commit and actual results before claiming pass |
| Workflow regression tests | TicketWorkflowRegressionTests.cs | Test implementation |
| Concurrency tests | ConcurrencyTests.cs | Database tests; requires PostgreSQL integration run |
| API integration test evidence | api-integration-tests-V1.0.md | Testing plan/results document; verify against release |
| CI quality gate | quality-gate-V1.0.md | CI control definition; check release run logs |
| Migration script | migrate-up.sh | Migration runner; execution proof still required |
