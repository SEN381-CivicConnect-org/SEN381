# 0005: Production deployment target — Render Cloud Application Platform

**Status:** Accepted  
**Date:** 2026-10-18  
**Phase:** Phase 4 (Milestone 4 Release Readiness & Deployment Decision)  
**Decision Maker:** CivicConnect Team  
**Closes:** RSK-01 (Platform Limitations & Operational Cost), RSK-15 (POPIA Operator Compliance), ASR-04 (Cost-Constrained Portable Container Deployment), FEC-02 (Observability & Health Monitoring)

---

## 1. Context and Problem Statement

CivicConnect requires a definitive production deployment strategy to transition from Milestone 3 staging into live production operation. Under SEN381 Master Project Brief Section 17 (*Environments, Deployment and Operations Standard*) and Section 18 (*Cost, Schedule and Resource Accountability*), the deployment decision is an assessed software engineering decision that balances hosting cost, operational complexity, regulatory compliance (POPIA), and environment parity.

The project requires a production platform that:
1. **Satisfies the Free/Low-Cost Constraint (RSK-01):** Operates on free or predictable low-cost tiers ($\le \$15\text{--}\$25\text{/month}$), preventing unanticipated cloud infrastructure invoices.
2. **Maintains Docker Container Parity (ASR-04):** Packages and executes the verified multi-container .NET 10 Kestrel and PostgreSQL runtime.
3. **Minimizes Operational Overhead (RSK-05):** Avoids burdensome manual VM/OS patching or complex Kubernetes cluster management for a 3-person team with academic deadlines.
4. **Protects Personal Information (POPIA s19/s21):** Guarantees in-transit TLS encryption (`SslMode=Require`) and secure credential decoupling.

---

## 2. Decision

CivicConnect selects **Render** as the official production deployment platform, automated via Infrastructure-as-Code through **`render.yaml`** (Render Blueprint).

### Production Topology on Render:
1. **Managed PostgreSQL (`civicconnect-db`):** PostgreSQL 15 managed instance in Frankfurt (EU) region (providing GDPR / POPIA adequacy and automated at-rest encryption). Connections strictly enforce TLS (`SslMode=Require`).
2. **Core Service Layer (`civicconnect-api`):** Docker Web Service built from `src/CivicConnect.Api/Dockerfile`, running on .NET 10 Kestrel (HTTP port 5060) under non-root user `appuser`. Monitored via health probe `/health/live`.
3. **Presentation Layer (`civicconnect-web`):** Docker Web Service built from `src/CivicConnect.Web/Dockerfile`, running Blazor Web App on .NET 10 Kestrel (HTTP port 8085). Monitored via health probe `/`.
4. **Asynchronous Messaging Seam (ADR-003):** Configured with `RabbitMQ__Host=none` / `RabbitMQ__Enabled=false` to utilize the resilient in-process queue fallback, with optional binding to a CloudAMQP instance when distributed message streaming is enabled.
5. **Continuous Deployment Pipeline:** Render connects directly to the GitHub repository (`SEN381-CivicConnect-org/SEN381`). Pushes to the production branch trigger automated multi-stage Docker builds, health checks, and zero-downtime rolling deploys.

---

## 3. Why Render Was Selected Over Alternatives

| Evaluated Alternative | Trade-offs & Limitations | Outcome |
| :--- | :--- | :--- |
| **AWS / Azure Hyperscaler Cloud** | ECS Fargate + RDS Postgres + Amazon MQ costs $\ge \$100\text{--}\$140\text{/month}$, breaching RSK-01. Excessive configuration complexity. | **Rejected** (Cost and cognitive overload). |
| **Unmanaged VPS / Bare VM** | Requires manual Linux OS patching, systemd management, Caddy reverse-proxy configuration, and manual certificate renewals. | **Rejected** (High operational maintenance overhead). |
| **Managed Kubernetes (AKS / EKS)** | Base cluster management fees ($\ge \$70\text{/mo}$) and complex YAML manifests violate team capacity (RSK-05). | **Rejected** (Over-engineering anti-pattern). |
| **Render Cloud Platform [SELECTED]** | Free tier available ($0); Starter tier ($7/mo per service) provides persistent disk, automated SSL, zero-downtime rolling deploys, and native Dockerfile support via `render.yaml`. | **Accepted** (Optimal balance of cost, parity, and simplicity). |

---

## 4. Consequences and Compliance Verification

- **RSK-01 (Cost):** Deployable on Render Free Tier ($0/mo) for demonstration and staging, scalable to Starter ($7–$14/mo) for persistent SLAs.
- **ASR-04 (Parity):** Uses the exact same multi-stage Dockerfiles (`src/CivicConnect.Api/Dockerfile` and `src/CivicConnect.Web/Dockerfile`) verified in staging.
- **NFR-01 (Secrets):** Managed environment variables injected dynamically into containers; zero credentials committed to Git.
- **NFR-04 (Security & TLS):** Enforces TLS encryption for database connections (`SslMode=Require`) and automated HTTPS at the Render edge proxy.
- **NFR-09 (Availability):** Health probes `/health/live` and `/health/ready` actively sampled by Render's internal orchestrator to automatically detect and replace degraded instances.
