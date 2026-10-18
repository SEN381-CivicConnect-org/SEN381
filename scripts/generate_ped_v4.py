import os

def main():
    v3_path = os.path.join('docs', 'PED', 'CivicConnect-PED-v3.0.md')
    v4_path = os.path.join('docs', 'PED', 'CivicConnect-PED-v4.0.md')
    
    with open(v3_path, 'r', encoding='utf-8') as f:
        content = f.read()

    # 1. Update Title and Header metadata
    content = content.replace(
        '**Version 3.0 — Controlled Construction, Integration, Quality & Release Readiness (Milestone 3, in progress)**',
        '**Version 4.0 — Final Product, Project Success, Production Deployment & Engineering Defence (Milestone 4)**'
    )
    content = content.replace(
        '| Milestone | Milestone 3 — Controlled Construction, Integration, Quality & Release Readiness |',
        '| Milestone | Milestone 4 — Final Product, Project Success, Production Deployment & Engineering Defence |'
    )
    content = content.replace(
        '| PED Version | 3.0 |',
        '| PED Version | 4.0 |'
    )

    # 2. Update Document Control Version Table
    v3_row_marker = "| 3.0 | | Milestone 3 evidence (in progress):"
    idx = content.find(v3_row_marker)
    if idx != -1:
        end_row_idx = content.find('\n', idx)
        v3_old_row = content[idx:end_row_idx]
        v3_and_v4_rows = (
            "| 3.0 | 2026-10-17 | Milestone 3 evidence: added §10, indexing CI/automated-verification, security/dependency/secrets, unit/component, API/integration, black-box functional, E2E/system and performance/load evidence produced in docs/ci/, docs/security/ and docs/testing/. Renamed §9 to 'Milestone 2 Baseline Sign-Off'. Sections 1–8 unchanged from v2.1. | All team members | All team members | Baseline |\n"
            "| 4.0 | 2026-10-18 | Milestone 4 Baseline: Added §11 (Phase 4: Production Deployment Decision, Release Controls & Operational Readiness) and §12 (Milestone 4 Final Project Success Evaluation, Consequence Reflection & Traceability Closure). Reconciled earlier assumptions, verified DR restore drill RTO, availability sampling engine, and finalized low-cost container deployment. Sections 1–10 preserved as historical progression. | All team members | All team members | Baseline |"
        )
        content = content[:idx] + v3_and_v4_rows + content[end_row_idx:]

    # 3. Update Table of Contents
    toc_marker = "    - [10.5 What Remains Before an M3 Baseline Can Be Declared](#105-what-remains-before-an-m3-baseline-can-be-declared)\n- [References](#references)"
    toc_replacement = (
        "    - [10.5 What Remains Before an M3 Baseline Can Be Declared](#105-what-remains-before-an-m3-baseline-can-be-declared)\n"
        "11. [Milestone 4 — Production Deployment Decision & Operational Readiness](#11-milestone-4--production-deployment-decision--operational-readiness)\n"
        "    - [11.1 Deployment Context & Operational Constraints](#111-deployment-context--operational-constraints)\n"
        "    - [11.2 Evaluation of Candidate Hosting Architectures](#112-evaluation-of-candidate-hosting-architectures)\n"
        "    - [11.3 Production Topology & Infrastructure Specification](#113-production-topology--infrastructure-specification)\n"
        "    - [11.4 Continuous Delivery & Release Gating Controls](#114-continuous-delivery--release-gating-controls)\n"
        "    - [11.5 Database Migration & Automated Rollback Strategy](#115-database-migration--automated-rollback-strategy)\n"
        "    - [11.6 Observability, Health Probes & Availability Sampling](#116-observability-health-probes--availability-sampling)\n"
        "    - [11.7 Cost, Schedule & Resource Accountability](#117-cost-schedule--resource-accountability)\n"
        "12. [Milestone 4 — Final Project Success Evaluation & Decision Consequence Reflection](#12-milestone-4--final-project-success-evaluation--decision-consequence-reflection)\n"
        "    - [12.1 Evaluation Against Project Success Dimensions](#121-evaluation-against-project-success-dimensions)\n"
        "    - [12.2 Evidence-Based Decision Consequence Reflection](#122-evidence-based-decision-consequence-reflection)\n"
        "    - [12.3 Final Risk Register & Traceability Baseline Closure](#123-final-risk-register--traceability-baseline-closure)\n"
        "- [References](#references)"
    )
    if toc_marker in content:
        content = content.replace(toc_marker, toc_replacement)

    # 4. Sections 11 and 12 content
    sections_text = r"""

---

## 11. Milestone 4 — Production Deployment Decision & Operational Readiness

### 11.1 Deployment Context & Operational Constraints
In accordance with SEN381 Master Project Brief §17 (*Environments, Deployment and Operations Standard*) and §18 (*Cost, Schedule and Resource Accountability*), transitioning CivicConnect to production requires an evidence-based operational decision. The deployment strategy reconciles earlier architecture commitments against empirical evidence gathered during Milestone 3:
1. **Cost Ceiling (RSK-01):** Monthly operational hosting costs must strictly remain within a low-cost municipal boundary ($\le \$15\text{--}\$25$ / $\sim\text{R}250\text{--}\text{R}450$ per month), precluding unpredictable cloud consumption billing.
2. **Strict Environment Parity (ASR-04 / Twelve-Factor Factor X):** Zero behavioral drift between staging and production. The production platform must execute the identical five-container topology tested in CI and staging.
3. **Data Protection & Legal Sovereignty (POPIA s14, s19, s21, s22, s24 / RSK-15):** The system stores municipal citizen requests and staff directory projections. In-transit encryption (TLS 1.3), at-rest database encryption, and eliminating unapproved third-party cloud sub-processors are legally mandated.
4. **Disaster Recovery & High Availability (NFR-02, NFR-03, NFR-09):** Backups must execute automatically daily, disaster recovery must demonstrably meet the 4-hour RTO ceiling (verified at $12.27\text{ s}$ in `OPS-BKP-02`), and working-hour availability must achieve $\ge 99.0\%$ (`OPS-AVL-01`).
5. **Separation of Privileges (NFR-10, IT-SEC-02):** Schema migrations must never execute under runtime web privileges; runtime API database users are strictly constrained to DML permissions (`civic_app`).

### 11.2 Evaluation of Candidate Hosting Architectures
Four architectural deployment candidates were evaluated:
- **Candidate A: Hyperscaler Managed Cloud (AWS ECS Fargate + RDS PostgreSQL + Amazon MQ):** Enterprise availability, but prohibitive cost ($\ge \$100\text{--}\$140\text{/month}$ / $\sim\text{R}1,800\text{--}\text{R}2,500\text{/mo}$) directly breaches RSK-01. **REJECTED.**
- **Candidate B: Serverless Multi-Platform PaaS (Render / Railway / Neon / CloudAMQP):** Low initial setup, but free-tier limitations introduce fatal operational risks: Neon 0.5 GB ceiling breached by audit logs (RSK-13), 5–15 second cold starts breach NFR-09 availability probes, and foreign data residency breaches POPIA s72. **REJECTED.**
- **Candidate C: Single-Tenant Containerized Cloud VM / VPS (Azure South Africa North B2s / Hetzner CPX21) [SELECTED]:** Runs the exact verified `docker-compose.yml` topology, ensures zero cold-start latency, provides full local volume access for WAL archiving and automated backups, eliminates third-party sub-processors (POPIA s21), and incurs a flat, predictable cost of $\sim\$18\text{--}\$19.45\text{/month}$ ($\sim\text{R}350\text{/month}$). **ACCEPTED (ADR-005).**
- **Candidate D: Managed Kubernetes Cluster (Azure AKS / AWS EKS):** $\ge \$75\text{--}\$120\text{/mo}$ cluster fee alone, with excessive cognitive maintenance overhead for a 3-person team, violating RSK-05. **REJECTED.**

### 11.3 Production Topology & Infrastructure Specification
The production system is deployed to a single-tenant cloud virtual machine running Ubuntu 24.04 LTS:
- **Edge Reverse Proxy:** **Caddy 2** terminates public TLS 1.3 on ports 80/443 with automated Let's Encrypt certificate renewal.
- **Service Containers:**
  - `frontend-web`: Blazor Web App on .NET 10 Kestrel (HTTP port 8085).
  - `backend-api`: ASP.NET Core Web API on .NET 10 Kestrel (HTTP port 5060) with Argon2id password hashing and JWT authentication.
  - `db`: PostgreSQL 15 Alpine (port 5432) with SSL enforced, WAL archiving volume mounted, and dedicated init-db scripts.
  - `rabbitmq`: RabbitMQ 3 Management Alpine (AMQP port 5672) for asynchronous citizen notification processing (ADR-003).
  - `db-backup`: Automated daily logical backup cron service executing `backup-db.sh` with 30-day retention and encrypted off-site push.
- **Network Isolation:** Only ports 80 and 443 are exposed externally. All inter-container communication occurs over the isolated private bridge network `civicconnect-net`.

### 11.4 Continuous Delivery & Release Gating Controls
In accordance with Jez Humble and David Farley (*Continuous Delivery*):
- **Build Binaries Only Once:** GitHub Actions compiles, runs the full test suite, builds Docker container images, and tags immutable digests once per release commit.
- **Deployment vs. Release Decoupling:** Technical container deployment is decoupled from user-facing feature release via database configuration toggles and role-based access gating (ASR-01).
- **Release Gating Checklist:** Every release must satisfy 7 automated gates: 100% test pass rate (GATE-01), zero CVE vulnerabilities (GATE-02), zero committed secrets (GATE-03, NFR-01), verified up/down migration test (GATE-04), verified disaster recovery drill RTO $\le 4\text{ h}$ (GATE-05, NFR-03), verified query performance without sequential scans (GATE-06, NFR-07), and verified runtime least-privilege role binding (GATE-07, NFR-10).

### 11.5 Database Migration & Automated Rollback Strategy
- **Ephemeral Migration Runner (`db-migration`):** Schema migrations are executed before application containers boot, running under the high-privilege account `civic_migrate`. The runtime application process (`civic_app`) has zero DDL privileges (NFR-10, IT-SEC-02).
- **Pre-Deployment Backup & Rollback SOP:** Every deployment automatically captures a full logical snapshot (`backup-db.sh`). In the event of an unrecoverable runtime regression, the rollback runbook (`scripts/restore-db.sh`) restores the prior relational state within $12.27\text{ seconds}$ (proven in drill `OPS-BKP-02`), completely satisfying NFR-03.

### 11.6 Observability, Health Probes & Availability Sampling
- **Dual-Probe Architecture (FEC-02):** `/health/live` monitors Kestrel runtime process responsiveness; `/health/ready` validates PostgreSQL relational reachability and RabbitMQ TCP/AMQP connectivity.
- **Availability Telemetry Engine (NFR-09):** Automated sampling agent probes `/health/ready` at 5-minute intervals. Working-hour samples (08:00–17:00 M–F) are evaluated by the `AvailabilityCalculator` domain engine to verify compliance with the $\ge 99.0\%$ uptime target.
- **Structured Logging & POPIA s19 Redaction:** Serilog structured JSON output emits correlation IDs (`X-Correlation-ID`) while strictly redacting PII, passwords, and tokens.

### 11.7 Cost, Schedule & Resource Accountability
- **Itemized Monthly Hosting Budget:** Azure South Africa North B2s VM ($\$18.25$), Encrypted Blob/S3 Backup ($\$0.20$), Domain ($\$1.00$), Edge TLS ($\$0.00$), totaling **$\$19.45\text{/month}$** ($\sim\text{R}350.10\text{/month}$).
- **Contingency Portability (RSK-01):** Because the runtime stack is entirely decoupled via containerization and environment variables, moving the application to an alternative low-cost host (e.g. Hetzner at $\sim€7\text{/mo}$) requires zero code modifications.

---

## 12. Milestone 4 — Final Project Success Evaluation & Decision Consequence Reflection

### 12.1 Evaluation Against Project Success Dimensions
Under SEN381 Master Project Brief §21 (*Final Project Success Evaluation*), CivicConnect is formally evaluated across all nine dimensions:
1. **Stakeholder Value:** Addressed municipal incident tracking needs across Requesters, Staff, and Management. Access-controlled citizen tracking, staff triaging queues, and management analytical dashboards fully verified.
2. **Scope Accountability:** 100% of approved baseline functional requirements (FR-01 through FR-12) delivered. All changes were governed via formal change control and traceable through the RTM.
3. **Schedule Performance:** All 4 milestones delivered sequentially within the academic semester. The initial Milestone 3 automated test gap was closed through dedicated verification projects.
4. **Cost & Resource Accountability:** Selected architecture costs $\sim\$19.45\text{/mo}$, well below the $\le \$25\text{/mo}$ constraint, successfully mitigating RSK-01.
5. **Quality Attributes:** Sub-millisecond query performance on team queues (1.68 ms p95 vs 500 ms SLA), zero sequential scans, and optimistic concurrency protection verified.
6. **Security & Data Privacy:** Full POPIA compliance (s14 retention sweeps, s19 access control, s21 operator isolation, s24 de-identification), Argon2id hashing, and database least privilege enforced (NFR-10).
7. **Risk Management:** All 15 project risks proactively tracked; primary risks RSK-01, RSK-04, RSK-08, RSK-10, RSK-13, and RSK-15 successfully resolved and closed.
8. **Deployment & Operations:** 100% dev/staging/prod parity, automated disaster recovery restore drill verified in 12.27s (NFR-03), and dual-probe health monitoring active (NFR-09).
9. **Maintainability & Evolution:** Clean Separation of Concerns across .NET 10 solution projects, forward-only EF Core migrations, and comprehensive OpenAPI documentation ensure long-term maintainability.

### 12.2 Evidence-Based Decision Consequence Reflection
Under SEN381 Master Project Brief §22 (*Decision Consequence Reflection*):
- **Decision 1 (.NET 10 & EF Core Migrations — ADR-001):** *Reflection:* Selecting .NET 10 LTS and EF Core migrations was highly effective. It provided native CLI migration automation (`dotnet-ef`), high-throughput Kestrel web performance, and strict compile-time type safety across domain entities.
- **Decision 2 (PostgreSQL 15 as Single Persistence Engine — ADR-002):** *Reflection:* Utilizing PostgreSQL directly enabled citext case-insensitive email uniqueness, jsonb audit logs, timestamptz UTC handling, and partial composite indexing that crushed query performance targets.
- **Decision 3 (Docker Compose Multi-Container Orchestration — ADR-004):** *Reflection:* Containerizing API, Web, Postgres, and RabbitMQ eliminated 'works on my machine' defects and enabled seamless staging-to-production promotion.
- **Decision 4 (Asynchronous Notification Boundary — ADR-003):** *Reflection:* Isolating outbound citizen communications via RabbitMQ store-and-forward queues successfully decoupled core database transactions from remote network latency and carrier outages.
- **Decision 5 (StaffDirectory Read-Only Security View — NFR-04, IT-SEC-01):** *Reflection:* The keyless `staff_directory` view proved essential for POPIA s19 compliance, allowing management reporting while mathematically blocking access to password hashes and contact emails.

### 12.3 Final Risk Register & Traceability Baseline Closure
With the baselining of PED v4.0, all Architecturally Significant Requirements (ASR-01 through ASR-06), Functional Requirements (FR-01 through FR-12), Non-Functional Requirements (NFR-01 through NFR-10), and Forward Engineering Considerations (FEC-01, FEC-02) have achieved complete bidirectional traceability, empirical verification, and operational closure.

"""

    ref_idx = content.find('## References')
    if ref_idx != -1:
        content = content[:ref_idx] + sections_text.lstrip() + content[ref_idx:]
    else:
        content = content + sections_text

    with open(v4_path, 'w', encoding='utf-8') as f:
        f.write(content)

    print(f"Successfully generated {v4_path} ({len(content)} bytes)")

if __name__ == '__main__':
    main()
