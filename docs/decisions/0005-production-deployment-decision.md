# 0005: Production deployment target and operational topology

**Status:** Accepted  
**Date:** 2026-10-18  
**Phase:** Phase 4 (Milestone 4 Release Readiness & Deployment Decision)  
**Closes:** RSK-01 (Platform Limitations & Operational Cost), RSK-15 (POPIA Operator Compliance), ASR-04 (Cost-Constrained Portable Container Deployment), FEC-02 (Observability & Health Monitoring)

---

## 1. Context and Problem Statement

CivicConnect requires a definitive production deployment strategy to transition from Milestone 3 staging into live production operation. The selection of hosting infrastructure, deployment orchestration, and operational controls is an assessed Software Engineering decision under SEN381 Master Project Brief Section 17 (*Environments, Deployment and Operations Standard*) and Section 18 (*Cost, Schedule and Resource Accountability*).

The deployment decision must satisfy five hard project constraints:
1. **Cost Ceiling (RSK-01 / Scope Constraints):** The operational hosting cost must strictly remain within a free or low-cost budget ($\le \$15\text{--}\$25$ / $\sim\text{R}250\text{--}\text{R}450$ per month), preventing unpredictable cloud billing escalations.
2. **Strict Environment Parity (ASR-04 / Twelve-Factor Factor X):** The production environment must maintain direct 1:1 runtime parity with the verified Milestone 3 staging topology (.NET 10 Kestrel runtime, PostgreSQL 15 Alpine, RabbitMQ 3 Management, and dedicated DDL migration tasks).
3. **Data Protection & Legal Sovereignty (POPIA s19 & s21 / RSK-15):** The system processes citizen municipal incident reports and staff directory details. Hosting must guarantee encryption at rest and in transit, strict isolation of credentials, and South African legal sovereignty (or explicit compliance with POPIA s72 cross-border data transfer conditions and s21 operator agreements).
4. **Disaster Recovery & High Availability (NFR-02, NFR-03, NFR-09):** The target platform must support automated logical backups, WAL archiving, verified recovery within an RTO $\le 4\text{ hours}$ (verified at $12.27\text{ s}$ in drill `OPS-BKP-02`), and $\ge 99.0\%$ working-hour availability.
5. **Separation of Concerns and Least Privilege (NFR-10, IT-SEC-02):** Runtime API instances must execute under non-privileged DML database accounts (`civic_app`), while schema migrations run strictly as isolated ephemeral pre-deployment tasks (`civic_migrate`).

---

## 2. Decision

CivicConnect selects a **Dedicated Single-Tenant Containerized Cloud VM / VPS Orchestrated via Immutable Docker Compose and Caddy Reverse Proxy** (hosted in South Africa or high-reputation low-cost European infrastructure with EU GDPR / POPIA adequacy, specifically **Azure South Africa North B2s** as primary domestic target, or **Hetzner Cloud CPX21** as cost-optimized sovereign VPS).

### Key Architectural Commitments:
1. **Packaging & Delivery:** The core services are delivered as immutable, multi-arch Docker container images built and signed once during CI/CD (`backend-api`, `frontend-web`, `db-migration`).
2. **Service Topology:** The production host runs the established five-tier container topology via `docker-compose.yml`:
   - `caddy`: Lightweight edge reverse proxy managing automated TLS certificates (Let's Encrypt), enforcing HTTP/2 and HTTP/3, and routing ingress traffic.
   - `frontend-web`: Blazor Web App running on .NET 10 Kestrel.
   - `backend-api`: ASP.NET Core Web API running on .NET 10 Kestrel with least-privilege connection (`civic_app`).
   - `db`: PostgreSQL 15 Alpine with SSL enforced, WAL archiving volume mounted, and initialization scripts.
   - `db-backup`: Automated daily logical backup cron service executing `backup-db.sh` with 30-day retention and off-site rsync/S3 push.
   - `rabbitmq`: RabbitMQ 3 Alpine managing asynchronous citizen notification message queues.
3. **Dedicated Migration Execution (NFR-10):** Schema updates are never executed by the runtime web process. Prior to updating application containers, an ephemeral `db-migration` container runs `dotnet CivicConnect.Web.dll --migrate-only` authenticated as `civic_migrate`.
4. **Secrets Injection (NFR-01):** Production secrets (database passwords, JWT secret keys, RabbitMQ credentials) are stored exclusively in host-level environment files (`.env.production`) with permissions restricted to `chmod 600 root:root`, never committed to source control.
5. **Observability & Health Probes (FEC-02, NFR-09):** The host runs an automated 5-minute sampling daemon querying `/health/live` and `/health/ready` to evaluate live availability against the 99.0% working-hour SLA.

---

## 3. Evaluated Alternatives

### Alternative 1: Hyperscaler Managed Cloud (AWS ECS Fargate + RDS PostgreSQL + Amazon MQ)
- **Evaluation:** Provides managed high availability and multi-AZ failover, but introduces massive cost inflation: RDS db.t4g.micro ($\sim\$15\text{/mo}$), ECS Fargate containers ($\sim\$30\text{/mo}$), Amazon MQ broker ($\sim\$25\text{/mo}$), and NAT Gateway ($\sim\$32\text{/mo}$), totalling over $\$100\text{--}\$120\text{/month}$ ($\sim\text{R}1,800\text{--}\text{R}2,200\text{/mo}$).
- **Outcome:** **Rejected.** Direct breach of RSK-01 (unanticipated operational costs exceeding project budget). Over-engineered for municipal municipality requirements.

### Alternative 2: Serverless Multi-Platform PaaS (Render / Railway / Neon / CloudAMQP)
- **Evaluation:** Easy initial setup, but suffers from severe operational risks: Neon free tier limits databases to 0.5 GB (breached by audit logs per RSK-13), compute spin-down introduces 5–15 second cold starts that trigger false-positive availability probe failures (violating NFR-09), and CloudAMQP free tiers severely throttle concurrent queues. Furthermore, data residency resides outside South Africa, introducing POPIA s72 compliance hurdles.
- **Outcome:** **Rejected.** Unpredictable cold-start latency, memory/storage ceilings, and compliance friction.

### Alternative 3: Kubernetes Orchestration (Managed AKS or EKS)
- **Evaluation:** Over-engineering anti-pattern. Management cluster fee alone is $\$70\text{/mo}$, with excessive cognitive overhead for a 3-person engineering team, violating RSK-05 (team capacity constraint).
- **Outcome:** **Rejected.** Unjustified operational complexity.

---

## 4. Consequences and Compliance Verification

- **RSK-01 Mitigated:** Fixed, predictable hosting cost of $\$10\text{--}\$18\text{/month}$ ($\sim\text{R}180\text{--}\text{R}320\text{/month}$), entirely within the approved low-cost project budget.
- **RSK-15 Mitigated:** Single-tenant server control eliminates unapproved third-party sub-processors. Data stays within controlled boundaries with LUKS at-rest encryption and TLS 1.3 in-transit.
- **ASR-04 & Dev/Prod Parity Achieved:** The production host runs the exact same container configurations, environment variable mappings, and network bridges tested and verified during Milestone 3.
- **NFR-03 & RTO Guaranteed:** The automated restore drill runbook (`scripts/run-restore-drill.sh`) and backup script (`scripts/backup-db.sh`) operate identically on production, ensuring the system can recover from backup archives in under 15 seconds.
- **NFR-10 Enforced:** Zero DDL privileges granted to the live application process. The runtime attack surface is strictly constrained to DML operations on business entities.
