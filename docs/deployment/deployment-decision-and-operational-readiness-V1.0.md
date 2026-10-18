# CivicConnect — Phase 4: Production Deployment Decision & Operational Readiness Specification

> **Document Identifier:** `OPS-DEP-01`  
> **Lifecycle Phase:** Phase 4 / Milestone 4 (Release Readiness & Final Product Engineering Defence)  
> **Status:** Baselined & Approved  
> **Date:** 2026-10-18  
> **Governing Standards:** SEN381 Master Project Brief §17 (*Environments, Deployment and Operations Standard*), §18 (*Cost, Schedule and Resource Accountability*), §20.4 (*Milestone 4 Engineering Defence*); PED v4.0 §11; ADR-005.

---

## 1. Executive Summary & Purpose

The transition of a software engineering system from isolated development and staging environments into live production operation represents a critical phase boundary. Passing local unit, component, and integration tests confirms algorithmic and functional correctness under single-tenant, zero-latency, and zero-concurrency conditions; it provides zero empirical proof of operational viability, resilience against network partitions, cost determinism, or data protection compliance in a live production topology.

Under the **SEN381 Master Project Brief (Section 17 and Section 18)**, the production deployment decision is an assessed software engineering decision, not a matter of informal developer preference. The decision requires a systematic evaluation of:
1. **The Cost Constraint (RSK-01):** Enforcing a free or low-cost operational threshold ($\le \$15\text{--}\$25$ / $\sim\text{R}250\text{--}\text{R}450$ per month) while avoiding catastrophic cloud overages.
2. **Environment Parity (Twelve-Factor App Factor X / ASR-04):** Maintaining complete operational parity between development, automated CI test runners, staging containers, and production infrastructure.
3. **South African Regulatory Compliance (POPIA s14, s19, s21, s22, s24):** Guaranteeing data sovereignty, encryption at rest and in transit, credential isolation, and avoiding unauthorized third-party operators.
4. **Disaster Recovery & Operational Resilience (NFR-02, NFR-03, NFR-09):** Automated database backups with an empirical Recovery Time Objective (RTO) $\le 4\text{ hours}$ (verified at $12.27\text{ seconds}$ in Milestone 3 drill `OPS-BKP-02`) and $\ge 99.0\%$ working-hour availability.
5. **Separation of Privileges (NFR-10, IT-SEC-02):** Enforcing database least privilege by separating DDL migration permissions from runtime DML application connections.

This document synthesizes the complete project analysis, presents the comparative evaluation of candidate deployment platforms, specifies the target production topology, details the continuous delivery and release gating controls, and provides the step-by-step database rollback and operational observability runbooks for CivicConnect.

---

## 2. Comprehensive Project Analysis Baseline

### 2.1 Technical Architecture & Runtime Topology
CivicConnect is engineered as an enterprise-grade municipal incident reporting and resolution management system, built on .NET 10 (LTS) and containerized with Docker:
- **Presentation Layer (`frontend-web`):** Blazor Web App hosted on .NET 10 Kestrel (HTTP port 8085), providing responsive citizen request submission, staff ticket triaging queues, and management analytical dashboards.
- **Service & Domain Layer (`backend-api`):** ASP.NET Core Web API on .NET 10 Kestrel (HTTP port 5060), exposing RESTful endpoints, OpenAPI/Swagger specifications, Argon2id cryptographic password hashing (`Argon2idPasswordHasher`), JWT token issuance, and background retention sweep workers (`RetentionSweepHostedService`).
- **Relational Persistence Layer (`db`):** PostgreSQL 15 Alpine (port 5432) enforcing strict relational integrity, composite foreign key constraints, check constraints, partial indexes, and SSL/TLS encryption (`ssl=on`).
- **Asynchronous Messaging Broker (`rabbitmq`):** RabbitMQ 3 Management Alpine (AMQP port 5672), providing temporal decoupling and queue-based load leveling for citizen email/SMS notifications (ADR-003).
- **Dedicated Migration Pipeline (`db-migration`):** An ephemeral, isolated container running `dotnet CivicConnect.Web.dll --migrate-only` authenticated under the dedicated DDL administrative account `civic_migrate`, executing before any application container boots.
- **Automated Backup & Retention Worker (`db-backup`):** An Alpine-based container executing `backup-db.sh` on an automated 24-hour cycle (NFR-02) with 30-day retention rotation and off-site archive synchronization.

### 2.2 Relational Persistence, Migrations & Indexing Performance
The relational database schema encompasses 21 tables across four core domains (Identity & Access, Configuration, Incidents & Tickets, Activity & Audit) plus the approved `staff_directory` read-only security view (NFR-04, IT-SEC-01):
- **13 EF Core Migrations:** Fully version-controlled, forward-only migrations managed via `__ef_migrations_history` with snake_case naming conventions (`0001-implementation-stack.md`, `0003-schema-conventions.md`).
- **Concurrency Control:** Optimistic concurrency tokens (`version` integer bumped on update via DB triggers) preventing lost updates on incident claiming and status transitions (RSK-12).
- **Sub-Millisecond Query Performance (NFR-07, NFR-08):** In Milestone 3 benchmark `PERF-Q-01`, execution of the service team queue query against a seeded 20,000-incident dataset demonstrated a 95th-percentile database execution time of **1.681 ms** (against the 500 ms SLA threshold), with zero sequential table scans due to the dedicated partial composite index `incident_team_queue_idx`.

### 2.3 Disaster Recovery and Availability Baseline
- **Milestone 3 Restore Drill Record (`OPS-BKP-02`):** On 2026-10-16, the disaster recovery runbook (`scripts/run-restore-drill.ps1` / `.sh`) was executed against an isolated `postgres:15-alpine` container. The backup archive, custom roles, and 22 tables were restored, and 32/32 integration tests passed in **12.268 seconds**—substantially exceeding the NFR-03 4-hour RTO ceiling.
- **Availability Telemetry Sampling (`OPS-AVL-01`):** Dual-probe health check architecture (`/health/live` and `/health/ready`) mathematically verified by the `AvailabilityCalculator` engine to ensure $\ge 99.0\%$ availability during client working hours (08:00–17:00 M–F, NFR-09).

---

## 3. Formal Deployment Evaluation Matrix

To determine the production hosting environment, four candidate deployment architectures were evaluated against the project's engineering constraints:

| Evaluation Criterion | Candidate A: Managed Hyperscaler Cloud (AWS ECS / RDS / Amazon MQ) | Candidate B: Multi-Platform Serverless / PaaS (Render / Railway / Neon) | Candidate C: Single-Tenant Containerized VPS (Azure SA North / Hetzner CPX21) [SELECTED] | Candidate D: Managed Kubernetes Cluster (Azure AKS / AWS EKS) |
| :--- | :--- | :--- | :--- | :--- |
| **Monthly Cost (RSK-01)** | **$\ge \$100\text{--}\$140\text{/mo}$** ($\sim\text{R}1,800\text{--}\text{R}2,500$): RDS micro ($\$15$), ECS tasks ($\$30$), MQ ($\$25$), NAT Gateway ($\$32$). Violates budget. | **$\$0\text{--}\$25\text{/mo}$**, but free tiers have severe storage limits (Neon caps at 0.5 GB) and paid tiers escalate unpredictably. | **$\sim\$10\text{--}\$18\text{/mo}$** ($\sim\text{R}180\text{--}\text{R}320\text{/mo}$): Fixed, predictable flat pricing. | **$\ge \$75\text{--}\$120\text{/mo}$**: Base cluster management fee alone is $\$70\text{/mo}$ before node compute costs. |
| **Dev/Staging Parity (ASR-04 / Factor X)** | Moderate: Uses AWS-proprietary abstractions, task definitions, and cloud IAM roles that differ from local Docker Compose. | Low: Divergent container orchestration, proprietary environment hooks, and externalized connection pooling. | **100% Exact Parity:** Runs the identical multi-container `docker-compose.yml` topology verified in staging. | Moderate: Requires Helm charts, Ingress controllers, and K8s manifests not used in local development. |
| **POPIA & Data Sovereignty (s19/s21 / RSK-15)** | High: AWS Cape Town region available, but requires complex IAM enterprise configuration and s21 operator agreements. | Low: PaaS databases typically hosted in US-East or EU-Central. Incurs cross-border data transfer hurdles (POPIA s72). | **Complete Sovereign Control:** Single-tenant VM hosted in South Africa (Azure SA North) or sovereign private VPS; zero unvetted sub-processors. | High, but excessive operational overhead for data protection auditing. |
| **Latency & Cold Starts (NFR-09)** | Low latency: Dedicated instances provide continuous responsiveness. | **Severe Cold Starts (5–15 s):** Serverless compute and Neon scale-to-zero trigger false-positive availability probe failures. | **Zero Cold Starts:** Always-on Kestrel runtime with dedicated memory allocation ensures consistent $<100\text{ ms}$ response. | Zero cold starts once pods are scheduled. |
| **Storage & WAL Archiving (RSK-08, RSK-13)** | High: Automated EBS snapshots and RDS point-in-time recovery. | **Constrained:** Neon free tier 0.5 GB ceiling breached by projected audit log volume; no direct WAL disk volume access. | **Unconstrained:** Dedicated 80–160 GB NVMe SSD volume, native WAL archiving to dedicated volume, and automated off-site S3 backup push. | High, requires persistent volume claim (PVC) CSI provisioning. |
| **Operational Complexity (RSK-05)** | High: Steep learning curve for cloud IAM, VPC peering, security groups, and CloudWatch. | Low initial setup, but high debugging complexity when rate limits or platform quirks occur. | **Low / Optimal:** Maintained directly via shell scripts, standard Docker CLI, and automated systemd service units. | **Excessive:** High cognitive burden managing control plane, ingress, and node pool upgrades for a 3-person team. |
| **Evaluation Verdict** | **REJECTED** (Cost constraint breached) | **REJECTED** (Cold starts, storage limits, and cross-border risks) | **ACCEPTED (Optimal Production Strategy)** | **REJECTED** (Over-engineering anti-pattern) |

---

## 4. Production Topology & Architecture

### 4.1 Production Infrastructure Specification
- **Host Target:** Single-Tenant Cloud Virtual Machine (2–4 vCPU, 4–8 GB RAM, 80 GB NVMe SSD, Ubuntu 24.04 LTS).
  - *Primary Municipal In-Country Target:* Microsoft Azure South Africa North (Johannesburg) B2s instance ($\sim\$18.25\text{/month}$).
  - *Secondary Low-Cost Sovereign Target:* Hetzner Cloud CPX21 Falkenstein/Helsinki ($\sim€7.05\text{/month}$ / $\sim\text{R}140\text{/month}$).
- **Edge Reverse Proxy:** **Caddy 2** running in a lightweight Alpine container or host systemd unit, managing automated Let's Encrypt TLS (HTTPS) certificates, enforcing HTTP/2 and modern cipher suites, and proxying ingress traffic.
- **Port Exposure Policy:** Only ports `80` (HTTP redirect) and `443` (HTTPS) are exposed to the public internet. Ports `5060` (API), `8085` (Blazor Web), `5432` (PostgreSQL), and `5672/15672` (RabbitMQ) are bound strictly to the private Docker bridge network (`civicconnect-net`) and `127.0.0.1`.

### 4.2 Architecture Topology Diagram

```mermaid
flowchart TD
    subgraph PublicInternet["Public Internet (Encrypted Ingress)"]
        Citizen["Citizen Mobile / Web Browser"]
        Staff["Municipal Staff / Management Workstation"]
    end

    subgraph HostPerimeter["Production Host Perimeter (Ubuntu 24.04 LTS / Azure SA North)"]
        subgraph IngressLayer["Edge Ingress & TLS Termination"]
            Caddy["Caddy Reverse Proxy\n(Automated Let's Encrypt TLS 1.3 / Ports 80 & 443)"]
        end

        subgraph DockerBridge["Private Bridge Network (civicconnect-net)"]
            Frontend["frontend-web:8085\nBlazor Web App (.NET 10 Kestrel)"]
            Backend["backend-api:5060\nASP.NET Core Web API (.NET 10 Kestrel)"]
            Migration["db-migration\nEphemeral DDL Runner (civic_migrate)"]
            Database[("db:5432\nPostgreSQL 15 Alpine\n(SSL Required / DML civic_app)")]
            BackupWorker["db-backup\nAutomated Daily pg_dump Cron Worker"]
            MessageBroker["rabbitmq:5672\nRabbitMQ 3 Management Alpine"]
        end

        subgraph StorageVolumes["Persistent Host Mounts"]
            DataVol[("civicconnect-db-data\n(NVMe SSD)")]
            WalVol[("civicconnect-db-wal-archive")]
            BackupVol[("backups/ Archive Volume\n(Gzip Encrypted)")]
            RabbitVol[("civicconnect-rabbitmq-data")]
        end
    end

    subgraph OffsiteStorage["Encrypted Off-Site Backup"]
        S3Storage[("Encrypted Off-Site Storage\n(AWS S3 / Backblaze B2 / Azure Blob)")]
    end

    Citizen -->|HTTPS / Port 443| Caddy
    Staff -->|HTTPS / Port 443| Caddy
    Caddy -->|Reverse Proxy / HTTP| Frontend
    Caddy -->|Reverse Proxy /api/*| Backend
    Frontend -->|Internal HTTP| Backend
    Backend -->|Asynchronous Event Dispatches| MessageBroker
    Backend -->|Least-Privilege DML Connection| Database
    Migration -.->|Ephemeral DDL Execution| Database
    BackupWorker -->|Daily Logical Backup / pg_dump| Database
    Database --- DataVol
    Database --- WalVol
    BackupWorker --- BackupVol
    MessageBroker --- RabbitVol
    BackupWorker -.->|Nightly Push (AES-256-GCM)| S3Storage
```

---

## 5. Continuous Delivery Pipeline & Release Controls

### 5.1 Continuous Delivery Standard: "Only Build Your Binaries Once"
In accordance with Jez Humble and David Farley (*Continuous Delivery: Reliable Software Releases through Build, Test, and Deployment Automation*), CivicConnect enforces the core deployment rule: **build binaries only once**.
- The CI pipeline (`.github/workflows/ci.yml`) compiles, tests, and packages container images exactly once per commit tag.
- The resulting immutable container images (`civicconnect-web:vX.Y.Z` and `civicconnect-api:vX.Y.Z`) are digitally tagged and pushed to a secure container registry.
- Environment promotion from Staging to Production occurs by deploying the identical, verified image digest; only runtime environment variables (`.env.production`) differ between stages.

### 5.2 Decoupling Technical Deployment from Business Release
As formulated by Martin Fowler, **Deployment** and **Release** are treated as separate architectural concerns:
1. **Technical Deployment:** The automated installation, execution, and health validation of new container images on the production host. Deployments occur safely during low-traffic windows without exposing incomplete features.
2. **Business Release:** The controlled activation of new user-facing functionality to citizens or municipal staff. Release activation is managed via:
   - **Role-Based Gating (ASR-01):** New capabilities are bound to specific seeded roles (`administrator`, `operations manager`, `supervisor`, `technician`) before general availability.
   - **Database Feature Toggles:** Low-risk configuration flags in database configuration tables allowing instant feature disablement without code redeployment.

### 5.3 Release Gating Sign-Off Checklist
Before any production deployment is authorized, the release candidate must pass all seven mandatory release gates:

| Gate ID | Quality Control Gate | Required Evidence / Metric | Verification Tool |
| :--- | :--- | :--- | :--- |
| **GATE-01** | **Automated Test Suite** | 100% pass rate across unit, component, and integration test suites ($>100$ tests). | `dotnet test CivicConnect.sln` |
| **GATE-02** | **Static Security & Dependency Scan** | Zero High or Critical CVE vulnerabilities in dependencies and container base images. | `dotnet list package --vulnerable` / Trivy |
| **GATE-03** | **Zero Hardcoded Secrets (NFR-01)** | Codebase scan confirms zero committed credentials, tokens, or private keys. | GitGuardian / Trufflehog |
| **GATE-04** | **Schema Migration Verification** | Ephemeral test migration applied up and rolled back cleanly against clean Postgres instance. | CI Migration Runner |
| **GATE-05** | **Verified Disaster Recovery (NFR-03)** | Successful restore drill completed within milestone period with RTO $\le 4\text{ hours}$. | `scripts/run-restore-drill.sh` (`OPS-BKP-02`) |
| **GATE-06** | **Performance Margin (NFR-07/08)** | Query execution plans verify zero sequential scans and staff queue p95 $\le 500\text{ ms}$. | `tools/seed-benchmark` (`PERF-Q-01`) |
| **GATE-07** | **Least Privilege Role Binding (NFR-10)** | Application connection string verified to use `civic_app`, blocking DDL operations. | `SystemSecretsConfiguration.EnforceLeastPrivilegeRoleAsync` |

---

## 6. Database Migration & Rollback Strategy

### 6.1 Ephemeral DDL Execution (`db-migration`)
In compliance with NFR-10 (IT-SEC-02), the runtime application container possesses only `SELECT`, `INSERT`, `UPDATE`, and `DELETE` permissions on database tables. It has zero schema modification (`CREATE`, `ALTER`, `DROP`) privileges.
- When an update is deployed, the deployment script executes the `db-migration` service before restarting the API or Web containers:
  ```bash
  docker compose run --rm db-migration
  ```
- This container connects using the administrative user `civic_migrate`, applies pending EF Core migrations up to the target version, records the state in `__ef_migrations_history`, and terminates immediately.

### 6.2 Pre-Deployment Snapshot & Rollback Runbook
To guarantee zero unrecoverable data loss in the event of a faulty migration, every deployment automatically executes a pre-migration snapshot:
1. **Pre-Migration Backup:** The deployment runner invokes:
   ```bash
   bash scripts/backup-db.sh --backup-dir /backups --label "pre-deploy-$(date +%Y%m%d%H%M%S)"
   ```
2. **Backward-Compatible Schema Discipline:** All migrations follow the *Expand and Contract* pattern (additive changes first; column renames or drops deferred to subsequent releases).
3. **Emergency Rollback Procedure:** If a newly deployed version fails post-deployment health probes:
   - **Step 1: Container Reversion:** Redeploy previous container image tags via Docker Compose.
   - **Step 2: Database Reversion:** If the schema change cannot run with the previous binary, invoke the automated restore script:
     ```bash
     bash scripts/restore-db.sh --archive /backups/civicconnect_pre-deploy_backup.dump
     ```
   - *RTO Compliance:* The system restores the full relational state within **12–15 seconds**, well beneath the 4-hour RTO threshold.

---

## 7. Observability, Health Probes & Incident Response

### 7.1 Dual-Probe Health Architecture (FEC-02)
CivicConnect exposes two distinct, standardized health check endpoints (implemented in `src/CivicConnect.Api/Program.cs`):
- **/health/live (Liveness Probe):** Verifies that the Kestrel web server process is responsive and thread pools are unblocked. Used by container orchestrators to trigger automatic container restarts if the process hangs.
- **/health/ready (Readiness Probe):** Evaluates critical backing service connectivity:
  1. *PostgreSQL Relational Persistence:* Executes `CanConnectAsync()` against the database cluster.
  2. *RabbitMQ Message Broker:* Establishes a TCP socket handshake to port `5672` with a 2-second timeout.
  Returns HTTP `200 OK` with detailed JSON diagnostics when all components are operational, or HTTP `503 Service Unavailable` if any dependency is degraded.

### 7.2 Working-Hours Availability Sampling (NFR-09)
In compliance with NFR-09, an automated cron sampling agent (`scripts/sample-health.ps1` / Linux equivalent) probes `/health/ready` at 5-minute intervals. The samples are logged to `logs/health-samples.jsonl` and processed by the `AvailabilityCalculator` domain engine to verify:
$$\text{Availability } \% = \frac{\text{Successful Working-Hour Samples}}{\text{Total Working-Hour Samples}} \times 100 \ge 99.0\%$$
- Maximum allowable working-hour downtime: **108 minutes/month** (21.6 samples).
- Maximum consecutive outage window: **48 samples** (4.0 hours, NFR-03 RTO).

### 7.3 Structured Logging & POPIA Compliance
- All API and Web requests generate structured JSON logs containing timestamp (UTC), log level, trace/correlation ID (`X-Correlation-ID`), HTTP method, path, response code, and duration.
- **POPIA Data Masking:** In strict compliance with POPIA s19(1) and ASR-06, logs never output plaintext passwords, password hashes, JWT tokens, citizen national identity numbers, or raw email addresses.

---

## 8. Cost & Resource Accountability Model (RSK-01)

### 8.1 Monthly Production Cost Breakdown
The operational cost of the selected deployment architecture is itemized below:

| Infrastructure Component | Specification / Provider | Monthly Cost (USD) | Monthly Cost (ZAR @ R18/$1) | Cost Allocation & Notes |
| :--- | :--- | :--- | :--- | :--- |
| **Compute & Memory (All Tiers)** | Azure South Africa North (B2s: 2 vCPU, 4 GB RAM, Ubuntu 24.04) | $\$18.25$ | $\sim\text{R}328.50$ | Hosts Web, API, Postgres, RabbitMQ, and Caddy containers with $100\%$ parity. |
| **Alternative Low-Cost Compute** | Hetzner Cloud CPX21 (3 vCPU, 4 GB RAM, 80 GB NVMe) | $\$7.75$ (€7.05) | $\sim\text{R}139.50$ | Secondary sovereign VPS option if budget requires absolute minimization. |
| **Storage (Primary Database)** | 80 GB Standard SSD / NVMe | Included in VM | Included in VM | Accommodates 3+ years of municipal growth ($<5\text{ GB}$ projected volume). |
| **Bandwidth (Egress / Ingress)** | Up to 20 TB / month | Included in VM | Included in VM | Far exceeds municipal request submission requirements. |
| **Edge TLS Certificates** | Let's Encrypt Automated CA | $\$0.00$ (Free) | $\text{R}0.00$ | Managed automatically by Caddy reverse proxy. |
| **Encrypted Off-Site Backup Storage** | Azure Blob Storage / AWS S3 Standard (10 GB) | $\$0.20$ | $\sim\text{R}3.60$ | Holds 30 daily rotating encrypted database dumps. |
| **Domain Name Registration** | Municipal `.gov.za` or `.co.za` | $\$1.00$ (amortized) | $\sim\text{R}18.00$ | Standard domain registration. |
| **Total Monthly Operational Cost** | **Azure SA North Domestic Baseline** | **$\$19.45\text{/month}$** | **$\sim\text{R}350.10\text{/month}$** | **Fully compliant with low-cost budget ($\le \$25\text{/mo}$).** |
| **Total Monthly Operational Cost** | **Hetzner Cloud VPS Baseline** | **$\$8.95\text{/month}$** | **$\sim\text{R}161.10\text{/month}$** | **Ultra-low-cost contingency baseline.** |

### 8.2 Free-Tier Risk & Contingency Migration Plan (RSK-01)
- **Free-Tier Ceilings Avoided:** CivicConnect avoids fragile multi-cloud free tiers (e.g., Neon 0.5 GB storage cap, Render 15-minute sleep periods, CloudAMQP connection limits).
- **Contingency Migration Pathway:** Because the entire system is defined via an immutable, portable Docker Compose topology with externalized environment variables (Twelve-Factor Factor III & IV), migrating the system to an alternative provider requires zero source-code changes. The migration procedure consists of:
  1. Provisioning a clean Docker-enabled Linux VM on the target host.
  2. Copying `docker-compose.yml`, `scripts/`, and `.env.production`.
  3. Transferring the latest encrypted database backup archive.
  4. Executing `bash scripts/restore-db.sh --archive <backup_file>` and starting containers via `docker compose up -d`.

---

## 9. Risk Register Closure & Reflection on Engineering Decisions

The Phase 4 deployment decision resolves and closes multiple major project risks:
- **RSK-01 (Platform Limitations & Cost Overruns):** Closed. Flat-rate hosting cost of $\$19.45\text{/mo}$ eliminates unexpected billing surges while providing guaranteed compute and memory resources.
- **RSK-04 (Database Schema Migration Inconsistency):** Closed. Ephemeral migration runner (`civic_migrate`) separates DDL from runtime DML, backed by automated pre-migration snapshots and forward-only migration discipline.
- **RSK-08 (Unverified Backup & Data Loss):** Closed. Daily automated logical backups paired with empirical restore drill verification (`OPS-BKP-02`) proving RTO $= 12.27\text{ s} \le 4\text{ hours}$.
- **RSK-10 & RSK-11 (POPIA Retention & PII in Descriptions):** Closed. Nightly automated retention sweep hosted service (`RetentionSweepHostedService`), user de-identification service, and access-controlled views protect citizen rights under POPIA s14 and s24.
- **RSK-13 (Audit Log Storage Inflation):** Closed. Dedicated 80 GB SSD volume eliminates the 0.5 GB Neon cap, coupled with scheduled 3-year audit log pruning sweeps.
- **RSK-15 (Unvetted Third-Party POPIA Operator):** Closed. Single-tenant VM hosting with LUKS at-rest encryption and self-managed backups eliminates unvetted cloud sub-processors, ensuring full compliance with POPIA s21(1).

---

## 10. Bidirectional Traceability Matrix

| Requirement / Engineering Standard | Architectural Mechanism & Design Commitment | Verification Evidence / Artefact |
| :--- | :--- | :--- |
| **ASR-04 (Portable Container Deployability)** | Docker Compose multi-container packaging; environment variable injection; zero in-memory state. | `docker-compose.yml`, `docs/deployment/deployment-V1.0.html`, ADR-005. |
| **NFR-01 (Secrets Configuration)** | System secrets loaded from environment variables; validated at boot; zero hardcoded credentials. | `src/CivicConnect.Data/Configuration/SystemSecretsConfiguration.cs`, `.env.example`. |
| **NFR-02 (Backup RPO $\le 24\text{ hours}$)** | Automated daily logical backup cron service executing `backup-db.sh` with 30-day retention. | `docker-compose.yml` (`db-backup` service), `scripts/backup-db.sh`. |
| **NFR-03 (Restore RTO $\le 4\text{ hours}$)** | Verified restore drill restoring into isolated PostgreSQL container and running test suite. | `docs/deployment/restore-drill-record-M3.md` (RTO $= 12.268\text{ s}$), `scripts/run-restore-drill.sh`. |
| **NFR-04 (POPIA Staff Protection)** | Keyless `staff_directory` view omitting passwords and contact details; direct `app_user` read revoked. | `src/CivicConnect.Data/Entities/StaffDirectory.cs`, `scripts/provision-roles.sql`. |
| **NFR-07 (Staff Queue p95 $\le 500\text{ ms}$)** | Dedicated partial composite index `incident_team_queue_idx` across 20,000 incident rows. | `docs/testing/performance/perf-q-01-evidence.md` (p95 $= 1.681\text{ ms}$). |
| **NFR-08 (Dashboard p95 $\le 2.0\text{ s}$)** | Composite indexes on status, category, team, and due dates eliminating sequential scans. | `docs/testing/performance/performance-load-V1.0.md` (p95 $< 5\text{ ms}$). |
| **NFR-09 (Availability $\ge 99.0\%$)** | Dual-probe health endpoints (`/health/live`, `/health/ready`); working-hour sampling engine. | `docs/deployment/availability-evidence-M3.md`, `scripts/sample-health.ps1`. |
| **NFR-10 (Least Privilege Database Role)** | Runtime API restricted to `civic_app` (DML only); migrations run by `civic_migrate` (DDL). | `src/CivicConnect.Api/Program.cs`, `scripts/provision-roles.sql`. |
| **FEC-02 (Observability & Monitoring)** | Standardized health probes, structured JSON logging, and correlation ID tracking. | `src/CivicConnect.Api/Program.cs`, `scripts/sample-health.ps1`. |
| **Master Project Brief §17 & §18** | Revisit earlier assumptions, cost modeling, environment parity, and operational accountability. | `docs/decisions/0005-production-deployment-decision.md`, `OPS-DEP-01`. |
