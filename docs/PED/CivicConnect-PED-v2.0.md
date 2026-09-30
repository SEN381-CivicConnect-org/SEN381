# CivicConnect

## Project Engineering Document (PED)

**Version 2.0 — Architecture, Quality Drivers & Persistence Baseline**

| Field | Value |
| --- | --- |
| Module | Software Engineering 381 — SEN381 |
| Project | CivicConnect |
| Milestone | Milestone 2 — Architecture, Quality Drivers & Persistence Baseline |
| PED Version | 2.0 |
| Team Members | Bernard Small, Shaun van Der Bijl, Fourie Jooste |

---

## Document Control

### Version History

| Version | Date | Description / Changes | Author(s) | Reviewer(s) | Status |
| --- | --- | --- | --- | --- | --- |
| 0.1 | | Initial PED structure created | | | Draft |
| 0.2 | | Initial sections completed | | | Draft |
| 0.9 | | Pre-baseline review | | | Review |
| 1.0 | | Milestone 1 Engineering Baseline | All team members | All team members | Baseline |
| 1.1 | | Milestone 2 draft: Architecturally Significant Requirements, architecture alternatives and selection, data/persistence baseline, technology-stack decision and initial design decisions drafted for review | All team members | | Draft |
| 2.0 | | Milestone 2 Architecture, Quality Drivers & Persistence Baseline | All team members | All team members | Baseline |

This version does not replace the Milestone 1 baseline; it extends it. Sections 1–5 and 7–9 restate the Milestone 1 commitments with the small, explicitly-flagged updates that Milestone 2 evidence requires (see §4 and §5). Section 6 is new for Milestone 2. The Milestone 1 document remains available at `docs/PED/CivicConnect-PED-v1.0.md` as the historical baseline it was reviewed and accepted as; per §2.6 Scope Control, it is not edited in place.

### Team Contributions

| Team Member | Main Responsibilities |
| --- | --- |
| Bernard | Scope Baseline, GitHub Governance, PED structure/integration |
| Fourie Jooste | Stakeholder analysis |
| Shaun van der Bijl | Problem, business value, Requirement and Acceptance, RTM Traceability, Risk and forward engineering |

### Approval / Review

| Name | Role | Review Status | Date |
| --- | --- | --- | --- |
| Bernard | Team Member | Complete | 09/09/2026 |
| Fourie Jooste | Team Member | | |
| Shaun van der Bijl | Team Member | | |

---

## Table of Contents

1. [Problem, Stakeholder and Business Value](#1-problem-stakeholder-and-business-value)
   - [1.1 Problem Statement](#11-problem-statement)
   - [1.2 Business Value](#12-business-value)
   - [1.3 Key Stakeholders](#13-key-stakeholders)
2. [Scope Baseline](#2-scope-baseline)
   - [2.1 Scope Statement](#21-scope-statement)
   - [2.2 In-Scope Capabilities](#22-in-scope-capabilities)
   - [2.3 Out-of-Scope Capabilities](#23-out-of-scope-capabilities)
   - [2.4 Deferred / Future Scope](#24-deferred--future-scope)
   - [2.5 Scope Constraints](#25-scope-constraints)
   - [2.6 Scope Control](#26-scope-control)
   - [2.7 Deliberate Scope Decision](#27-deliberate-scope-decision)
   - [2.8 Project Assumptions](#28-project-assumptions)
3. [Requirements and Acceptance Criteria](#3-requirements-and-acceptance-criteria)
   - [3.1 Functional Requirements](#31-functional-requirements)
   - [3.2 Non-Functional Requirements](#32-non-functional-requirements)
4. [RTM Traceability Foundation](#4-rtm-traceability-foundation)
5. [Risk and Forward Engineering Considerations](#5-risk-and-forward-engineering-considerations)
   - [5.1 Risk Management Approach](#51-risk-management-approach)
   - [5.2 Initial Risk Register](#52-initial-risk-register)
   - [5.3 Forward Engineering Considerations](#53-forward-engineering-considerations)
6. [Architecture, Quality Drivers and Persistence Baseline (Milestone 2)](#6-architecture-quality-drivers-and-persistence-baseline-milestone-2)
   - [6.1 Architecturally Significant Requirements and Quality Drivers](#61-architecturally-significant-requirements-and-quality-drivers)
   - [6.2 Architecture Design](#62-architecture-design)
   - [6.3 Data and Persistence Baseline](#63-data-and-persistence-baseline)
   - [6.4 Technology-Stack Decision](#64-technology-stack-decision)
   - [6.5 Initial Design Decisions](#65-initial-design-decisions)
7. [GitHub and Team Governance](#7-github-and-team-governance)
8. [AI Usage](#8-ai-usage)
9. [PED v2.0 Baseline Sign-Off](#9-ped-v20-baseline-sign-off)
- [References](#references)
- [PED Review Notes](#ped-review-notes)

---

## 1. Problem, Stakeholder and Business Value

*(Unchanged from PED v1.0. Restated here so v2.0 remains a complete, standalone reading of the baseline.)*

### 1.1 Problem Statement

Our client currently operates their communication systems through multiple platforms such as WhatsApp messages, emails, telephone calls, spreadsheets and paper-based records. This approach creates a significant risk that important information may be lost across the various communication channels. It also creates security risks, as sensitive data may be handled inconsistently.

### 1.2 Business Value

CivicConnect will provide a platform for the client that generates tickets for various maintenance and service requests. This will improve accountability and visibility for service requests without imposing an unsustainable technical or financial burden on the organisation.

### 1.3 Key Stakeholders

#### 1.3.1 Stakeholders

- **The incident reporter** will be the primary user of the project as they will be able to report an issue, know when it has been escalated and when it has been resolved. They exert a high level of influence and high level of interest in this project.
- **Technicians** will also be a primary user with a list of pending tickets to be resolved with minimal typing as to not impede ticket closure times. They will also not get alerted for the same issue multiple times if many users report the same issue. They exert a medium level of influence and a high level of interest.
- **The team supervisor** will oversee assigned tickets to the technicians to exert human discretion when a certain ticket needs urgency escalation and ensure the team's workload is being properly managed by the program. They will have a high level of influence and a high level of interest.
- **The operations manager** will need to be able to monitor service-target turnaround and view evidence of completed jobs to manage the brand reputation. They will have a high level of influence and a high level of interest.
- **The finance department** will monitor data of broken equipment for claims and budgeting purposes. They will exert a high level of interest and a low level of influence on this project.
- **Information Regulators** will oversee the lawful processing of staff personal information. They will have a high level of influence and a low level of interest in this project (only taking note if the law has been breached).
- **The project team** will oversee the development of this project within the deliverable scope. They will exert both a high level of influence and a high level of interest in this project.

#### 1.3.2 Stakeholder Influence/Interest Map

> *[Figure: stakeholder influence/interest map — image in the source document, not reproduced in this conversion.]*

#### 1.3.3 Conflicts and Their Solutions

- All users reporting an issue will want their issue to take top priority; however, technicians will already have a pre-populated ticket queue. The problem is that if urgency is self-declared, everything will be declared urgent. **The solution** will be to have priority calculated off impact and urgency, using the reported location and category. However, an override will still exist for the team supervisor to exert human discretion, with a recorded reason. *(Realised in Milestone 2 as ASR-02 and the `incident_override_ck` constraint — see §6.1 and §6.3.)*
- When the network drops or a similar mass reportable incident takes place, every user that reports the incident wants their report to be acknowledged, but the technicians want to resolve only one incident. **The solution** would be to cluster related reports into a parent incident and subscribe every reporter to it. Each person keeps their own reference and receives updates, but technicians only work with one item. *(Realised in Milestone 2 as ASR-02 and the partial unique clustering index — see §6.1 and §6.3.)*
- Technicians want to resolve a ticket as quickly as possible with minimal typing, whereas the finance department wants a detailed report with as much traceability to physical goods and hardware as possible to track faulty goods that need to be replaced or notify suppliers about faulty equipment. **The solution** is to make the report a technician must fill in as concise as possible while retaining all relevant and important data. *(Realised in Milestone 2 as ASR-05 — see §6.1.)*
- Users want reports that were already made to be visible, whereas the operations manager would prefer not to display a list of unresolved issues. **The solution** is to publish unresolved incidents, as the argument for publishing the data is stronger than against; however, it is also possible to publish resolution statistics per category. *(Realised in Milestone 2 as ASR-06 — see §6.1.)*

---

## 2. Scope Baseline

*(Unchanged from PED v1.0.)*

### 2.1 Scope Statement

CivicConnect is expected to be designed as a configurable platform for the management of internal service requests within office-based organizations.

The proposed platform will not be limited to one particular organization type, department or set of requesters but rather would be able to serve multiple office environments due to its configurability based on such controlled parameters as request categories, office areas and access controls in accordance with specific user roles, with the request process being common for all deployments and including such stages as submitting, assigning, tracking, updating, resolving and managing of service requests. This approach to defining service requests and their management is quite common in the service management sphere and allows configurable types of requests to be defined in order to categorize different incoming requests and configure the corresponding service team workflow (Atlassian, n.d.).

The main purpose of this system is to substitute fragmented request management practices within offices that include the usage of different communication tools like email, telephone, WhatsApp messages, spreadsheets and paper records with one configurable and traceable ticket management platform.

Scope of Milestone 1 highlights the business capabilities that have been committed to be delivered by the team, determines the limits of office-level configurability, and points out some features that were consciously left out of the project.

### 2.2 In-Scope Capabilities

These capabilities make up some of the CivicConnect project scope. The exact same capabilities will be utilized in other office setups through configurable settings as opposed to custom coding for each individual organization.

#### 2.2.1 Requester Capabilities

- **Submit a new service request** — The requesters can initiate a new service request with all the required details.
- **Categorize the request using a controlled categorization process** — The requesters can choose the right category among the controlled categories provided by the system.
- **See the status of a submitted request** — The requesters can check the status of those requests that have been submitted by them.
- **See the history of the requests** — The requesters can see the history or the list of requests submitted by them.
- **Get meaningful feedback** — The requesters get meaningful feedback on the acceptance, rejection, update or completion of their request.

#### 2.2.2 Staff Capabilities

- **See authorised service requests** — Staff can see service requests which they are authorised to see.
- **Search, filter and sort service requests** — Staff can search service requests using useful parameters like status, category and more.
- **See full request details** — Staff can see all the information related to a particular service request.
- **Claim or accept responsibility for service requests** — Staff can claim the service request or assign responsibility on an authorised basis.
- **Change status of a request via controlled transitions** — Staff can change status of the service request according to the permitted process flow.
- **Document actions, comments and resolutions** — Staff can document the work done, comments and resolutions of the request.
- **Resolve or close requests** — Staff can resolve or close the service requests on an authorised basis.

#### 2.2.3 Management and Oversight Capabilities

- **See service activity details** — Management is able to see the details of the service activity.
- **See requests according to their status** — Management is able to see open, overdue, resolved, and closed requests.
- **Analyse requests according to appropriate dimensions** — Management can see the request details according to the categories, statuses, and other appropriate dimensions.
- **See information about service performance** — Management is able to see all the necessary information to analyse service performance and accountability.
- **See request history and audit details** — Management can see all the request details and history.

#### 2.2.4 Configuration and Administration Capabilities

- **Controlled request categories management** — Authorised administrative users can configure controlled service request categories creation, modifications, activation or deactivations without impacting the existing request data.
- **Configurable request classifications management** — Authorised users can manage the selected classification information for requests where such is deemed necessary according to the ultimate requirement.
- **Organisational request handling configuration** — The system allows requests to be assigned to relevant office department(s), operational area(s), team(s) or personnel without limiting the platform to only one defined service area.
- **Management of authorised users and roles** — Authorised users can configure the roles for requesters, staff and managerial functionality.
- **Ensure controlled configuration** — All changes to the configuration should be made by only authorised users and should not delete or overwrite historical request data that would provide accountability.

**Configuration boundary.** The reason for these features is to offer controlled configurability at the office level instead of any system customization. The goal of CivicConnect is to be reusable in other offices, but CivicConnect was never meant to work as a generic no-code application building tool or multi-tenant service management tool for enterprises. The existing ticket management tools have used multiple configurable ticket forms to manage different requests in the same ticket management process (Zendesk, 2026).

These features are developed based on the minimum business capabilities from the CivicConnect Master Project Brief with the office-ticket configuration boundary added to the project direction of the team (SEN381 Teaching Team, 2026a).

### 2.3 Out-of-Scope Capabilities

The following items are not part of the current committed scope of the project:

- Native apps for Android or iOS platform.
- Automated AI-based request classification/prioritization.
- Request submission through WhatsApp/WhatsApp API.
- Integration with third-party external service management systems.
- Predictive analytics.
- Complex SLA enforcement via automation.
- Fault detection via hardware or IoT sensors.
- Payment processing.
- Hosting multiple organizations/tenants in one deployed instance.
- Request forms builders based on fully customizable drag-and-drop.
- Arbitrary database fields/data types provided by users.
- Scripting/automation provided by user-defined scripts.
- Fully customizable workflow design engine.
- Plugins or extensions marketplace for third-party.

These exclusions are made to ensure that the project stays within the realistic scope in terms of the given time and other available means. Although CivicConnect is designed to be configurable to be used by various offices, the given committed project is not required to be able to have one deployed instance of software which would host more than one organization at the same time. The platform will give limited configurability on certain aspects of office ticket management without moving into the no-code development of an application, workflow creation or scripting, among others.

According to the Master Project Brief, additional functionality means additional responsibilities related to specification, design, security, implementation, testing, documentation, deployment, and maintenance.

### 2.4 Deferred / Future Scope

The following capabilities could have future value to stakeholders but are currently deferred from the project commitment:

- Email, SMS or WhatsApp notifications integrations.
- Request categorization with AI assistance.
- Improved reporting and analysis.
- Escalation and SLA management.
- Attachment of files or images when required by more detailed specifications.
- Integration with external organizational systems.
- Improved mobile functionality.
- Automation of request assignment and prioritization.
- Advanced configurable request form fields.
- Request workflow definition and status transitions by users.
- Custom SLA policies per category.
- Configurable request routing rules.
- Custom office/organization-specific dashboards.
- Multi-tenant organization configuration.
- Templates import/export.

Only once it is established that there is stakeholder value in these capabilities and the impacts of their incorporation have been evaluated, will they be considered further in relation to the impacts they have on scope, schedule, cost, quality, security and risk. No deferrals can become commitments unless and until they have gone through the change control process. The capability of deploying CivicConnect in other offices does not necessarily mean enterprise multi-tenancy.

### 2.5 Scope Constraints

Project scope is affected by several project constraints that are mandatory. It is important that these constraints actually influence project decisions, rather than being just described (SEN381 Teaching Team, 2026a).

| Constraint | Scope Implication |
| --- | --- |
| Team Size | The project is developed by only three students, so the scope must reflect this constraint, as the amount of functionality that can be engineered, reviewed and verified by a three-student team is limited. |
| Schedule | It is important that the project goes through four formal milestones, so the scope must stay feasible within the academic delivery period. |
| Cost | Free or low-cost services must be preferred when possible, and potential operational costs and platform capabilities must be known. |
| Quality | Features that are included into project scope must be defined by measurable requirements and then verified. |
| Security | Scope definition must take into account such factors as authentication, authorisation, sensitive information, least privilege and other security-related issues. |
| Technology | The project team cannot increase its scope because of the capabilities of the chosen technologies. Further justification of these technologies must include requirements, constraints and team capabilities. |
| Configurability | The project scope must allow for configuration that is sufficient to make the system configurable to work in any office environment and service area without becoming a no-code, workflow builder or multi-tenant platform. |

### 2.6 Scope Control

After reviewing and approving the scope baseline, any significant changes to add or remove must not be made through an informal process.

Any change after the scope baseline is to go through the process of change control defined in the Master Project Brief (SEN381 Teaching Team, 2026a):

> Change Request → Impact Analysis → Decision → Authorisation → Implementation → Verification → Baseline Update

For the impact assessment, the following should be evaluated in regard to the change request:

- Requirements and Acceptance Criteria
- Architecture and Design
- User Interface and Information Architecture
- Data and Persistence
- API/Interface Contract
- Security and Privacy
- Scope, Schedule, Cost, and Resources
- Quality, Testing and Regression
- Deployment and Operations
- Risks and Technical Debt

Change requests for further configurable elements should also go through the change control process since increased configurability at the office level can have an influence on requirements, data structure, UI design, authorization, testing, and complexity of the entire project.

### 2.7 Deliberate Scope Decision

An example of a purposeful scope control strategy is the decision not to include integration of WhatsApp within the scope that has been committed to.

While it is true that WhatsApp happens to be one of the communication mediums already in use by the organization, the central problem at hand for the business is not integration with WhatsApp. The central problem relates to the fragmentation of requests over multiple uncontrolled communication platforms.

Integration with WhatsApp would mean dealing with more complexity, which includes integration of an external API, authentication, dependency, configuration, security, and operational issues.

It makes sense to create a single controlled and configurable office ticket-management system first rather than adding further external communication channels in order to support the reusable-office-system strategy.

### 2.8 Project Assumptions

At present, the CivicConnect initiative operates under several assumptions that help to plan and develop the baseline at Milestone 1. The assumptions made do not represent guaranteed factors; therefore, they need to be revisited if any new information arises.

| Assumption ID | Assumption | Potential Impact if Incorrect |
| --- | --- | --- |
| ASM-01 | The three-member project team will be available and will be able to make contributions during the four milestones of the project. | Project planning, milestone setting, and load leveling can be affected by restricted availability. |
| ASM-02 | The minimal business capabilities mentioned in the CivicConnect Master Project Brief will still be the basis of the necessary system until it is officially revised. | Significant modifications might sometimes warrant reevaluation of the project baselines, scope, requirements, and risks. |
| ASM-03 | There will be available development and deployment technologies that would fit the needs of the project and will not cost too much. | Reassessments of the technology, implementation, and costs may become necessary if the appropriate services are not available anymore and the constraints are not valid anymore. |
| ASM-04 | The development environments needed by the chosen technologies will be available to the team. | Additional learning needs, delays, or implementation problems may arise as a consequence of any incompatibility or availability issues. |
| ASM-05 | There will be available stakeholder feedback and clarifications to validate the necessary requirements or scope of the project. | Incorrect assumptions and unnecessary work may arise from lack of feedback. |
| ASM-06 | Supporting various offices is achievable via a well-controlled combination of configurable components including categories, operational scope and role, as opposed to a generic ticket-system building tool or multi-tenant software as a service offering. | Where there is need for a large number of custom forms, arbitrary fields, user-created workflow, organization logic, and concurrent hosting of many different organizations, there may be significant changes needed to the scope, requirements, design, timeline, security and testing process. |
| ASM-07 | CivicConnect serves one office with about 250 requesters and 12 service staff. The team expects 30 submissions per working day, or about 7,500 tickets a year over 250 working days. Tickets cluster at roughly 1.25 per incident, so about 6,000 incidents a year. The 30-a-day and 1.25 figures are the team's own estimates, not client figures. *(Added between v1.0 and v2.0 to support the Milestone 2 growth estimate — see §6.3.4.)* | If the volumes are wrong, storage, indexing, connection limits and the free-tier caps (RSK-01) would need another look. A tenfold error still leaves an ordinary PostgreSQL database (see §6.3.4). |

**Assumption review.** ASM-06 must be reviewed once the stakeholders provide more detailed guidance about the extent to which customization at the office level will be needed. In case this information has an impact on committed functionality, the scope baseline and the associated requirements must be updated via controlled change.

---

## 3. Requirements and Acceptance Criteria

*(Unchanged from PED v1.0. The full functional and non-functional requirement registers are maintained in `docs/requirements/`.)*

The team will implement a ticket-based approach to track functional and non-functional requirements. Each requirement will have an identifier and description and can later be presented to the stakeholder for review and approval.

### 3.1 Functional Requirements

Functional requirements will use the following identification structure:

`FR-XX`

These tickets will be used to track the functional requirements of CivicConnect. The full register is maintained at `docs/requirements/Functional Requirements/functional-requirements-V1.0.html`.

Example: `FR-01`

**FR-01 — Submit and Categorise a Service Request**

- **Source:** Requested Capabilities
- **Requirement:** The system will allow authorised users to submit a ticket request and categorise it using a controlled category mechanism.
- **Acceptance Criteria:** Given that a user has entered the required issue details and selected a category for their request, when the request is submitted, a new ticket should be generated containing the information from the request and a success confirmation message should be displayed.

### 3.2 Non-Functional Requirements

Non-functional requirements will use the following identification structure:

`NFR-XX`

These tickets will be used to track the non-functional requirements of CivicConnect. The full register (now eleven requirements, NFR-01 to NFR-11, covering secrets, backup/recovery, POPIA protection and retention, concurrency correctness, query performance, availability, least-privilege database access and password storage) is maintained at `docs/requirements/Non-Functional Requirements/non-functional-requirements-V1.0.html`.

Example: `NFR-01`

**NFR-01 — Protection of System Secrets**

- **Source:** Security Constraints
- **Requirement:** The system will ensure that sensitive credentials and application secrets are secured through the use of approved configuration mechanisms.
- **Acceptance Criteria:** Given the application deployment configuration, when the system starts, it will successfully load the database connection string and JWT secrets from secure environment variables instead of these keys being hardcoded into the source code.

---

## 4. RTM Traceability Foundation

The Requirements Traceability Matrix (RTM) will provide a controlled mechanism for connecting the original stakeholder need or project source through the later engineering lifecycle.

The updated traceability flow must connect the original stakeholder need through to final release evidence:

> Source / Stakeholder → Requirement → Design / Architecture → Issue / PR → Test → Release Evidence

The Milestone 1 RTM established the foundation for this traceability. Milestone 2 now supplies the "Design / Architecture" evidence for the two example requirements below, so the `Milestone 2` placeholder is replaced with the actual decision/section reference. The full matrix is maintained at `docs/requirements/Requirements Traceability Matrix/requirements-traceability-matrix-V1.0.html`, which has been updated with the same two rows; the remaining requirements will gain Design/Architecture references as their own decisions are made.

| Source / Stakeholder | Requirement ID | Description | Design / Architecture | Issue / PR | Test | Release Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| Minimum Business Capabilities | FR-01 | Submit and categorise requests | ASR-02 (§6.1); ASR-03 and DD-02 (§6.5.3); ADR-ARCH-001 Request Management / Configuration modules (§6.2.4) | Milestone 3 | Milestone 3 | Milestone 4 |
| Security Engineering Standard | NFR-01 | Protect system secrets | ASR-04 (§6.1); Docker/environment-variable configuration, ADR-004 (§6.4) | Milestone 3 | Milestone 3 | Milestone 4 |

---

## 5. Risk and Forward Engineering Considerations

### 5.1 Risk Management Approach

Project risks will be tracked in an easy-to-manage Risk Register.

Each risk will receive a unique identifier and will record the risk description, cause, probability, impact, priority, mitigation approach, contingency action and current status.

The purpose of the register is to ensure that risks are identified clearly and that the team documents how each risk can be reduced or managed.

### 5.2 Initial Risk Register

The full register is maintained at `docs/risk/Risk Register.html`.

| ID | Description | Cause | Probability | Impact | Priority | Mitigation | Contingency | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| RSK-01 | Platform limitations may restrict deployment and cause unexpected downtime. | Unanticipated operational costs or exceeding free-tier resource limitations. | Medium | High | High | Research and document resource limits for Docker containerisation deployments early. | Migrate the chosen backend to an alternative low-cost hosting provider. | Open — mitigation documented in ASR-04 (§6.1) and §6.4 (Docker); a cheaper asynchronous standby is the first escalation step if availability requirements tighten (§6.3.6). |
| RSK-09 | A mass-reporting event (e.g. a network outage) could produce duplicate incidents or duplicate technician notifications under concurrent load. | High-concurrency writes to the same clustering key without a serialising constraint. | Low | Medium | Medium | The clustering invariant is enforced as a partial unique index plus `INSERT ... ON CONFLICT DO UPDATE`, not as application-level check-then-insert (§6.3.1, §6.3.5). | Accept the resulting brief lock contention on the affected row — modelled at roughly two seconds to clear 200 reports at 100 transactions/second — as intended behaviour, not a fault (§6.3.6). | Open — control designed, not yet implemented. |

### 5.3 Forward Engineering Considerations

Similar to how the team will use controlled tickets to track system functionality, forward engineering considerations will be documented so that important later-lifecycle concerns can be identified and tracked before final technical decisions are made. The full register is maintained at `docs/risk/FEC Register.html`.

**FEC-01 — Database Persistence and Data Design**

- **Consideration:** The program should be able to handle controlled transitions for request statuses while safely storing user data.
- **Deferment Plan (M1):** The final decision between using a NoSQL database and a traditional relational SQL database is deliberately deferred until the data structures and API contracts are fully designed in Milestone 2.
- **Status (M2): Resolved.** PostgreSQL 16+ is selected — see §6.3.5 (Persistence Model Decision) and §6.4 (ADR-004, Technology-Stack Decision) for the justification and the alternatives rejected.

**FEC-02 — Operational Readiness**

- **Consideration:** CivicConnect requires logging, monitoring, health checks and failure detection before staging and launching to production.
- **Deferment Rationale (M1):** Specific observability tools are not being configured for Milestone 1 because the exact telemetry stack will only be selected once the application architecture has been finalised.
- **Status (M2): Partially resolved.** ASR-04 (§6.1) commits to a health endpoint and a single logging abstraction as a design seam now, so the later tool choice is a configuration change rather than a redesign. The specific tool remains deferred.

---

## 6. Architecture, Quality Drivers and Persistence Baseline (Milestone 2)

This section is new for PED v2.0. It carries forward the Milestone 1 scope, stakeholder conflicts (§1.3.3) and constraints (§2.5) into the architecturally significant requirements, the architecture, the data/persistence design, the technology-stack decision and the initial design decisions that the Milestone 2 brief requires (SEN381 Teaching Team, 2026c).

**Evidence boundary.** The current repository evidence reviewed for this section does not yet show implementation of these decisions in application code. This section documents decisions and their rationale, not implementation evidence. Branch/class/PR/test references will be added once development reaches these areas (see §6.5.4 and §6.5.5).

### 6.1 Architecturally Significant Requirements and Quality Drivers

Six requirements are treated as architecturally significant because each one forces a structural decision — an enforcement boundary, a transaction shape, a storage guarantee or a deployment constraint — rather than a single component's internal logic. Each is stated as PED evidence, a measurable expectation, and the design implications that follow from it. The full text (including the OWASP, PostgreSQL and POPIA evidence cited under each) is maintained at `docs/quality/quality-drivers-V1.0.html`; it is reproduced here in full because these six requirements are the primary output of this milestone.

#### ASR-01 — Role-based authorisation and least privilege

**Evidence from the PED.** The in-scope capabilities (§2.2) describe four capability groups (requester, staff, management and oversight, configuration and administration), each with different authority over the same entities. Staff "can see service requests which they are authorised to see" and "assign responsibility on an authorised basis." §2.2.4 requires that "all changes to the configuration should be made by only authorised users." Information Regulators (§1.3.1) "oversee the lawful processing of staff personal information." The Security row of §2.5 asks for "authentication, authorisation, sensitive information, least privilege." NFR-01 already loads the JWT secrets and the database connection string from environment variables.

**Measurable expectation.**
- Given a user in the requester group only, when they call the status-change endpoint, the call is rejected, the status is unchanged, and no history record is added.
- Given a supervisor authorised for one service team, when they change the status of a request assigned to another team, the call is rejected. The response says whether the cause is the role or the team scope.

**Design implications.**
- **One enforcement point.** Identity, roles and team scope are checked once at the API boundary, before domain logic runs — a single middleware, not a check in every handler. OWASP recommends application-wide configuration, a check on every request, deny by default, and server-side enforcement only (OWASP, 2026a).
- **Roles and teams as tables.** Store them as relations, not a free-text role on the user, because staff and management capabilities describe two scopes over the same rows.
- **Least privilege in the database.** The application's database account gets only the privileges it uses, per table, never `ALL PRIVILEGES` — supported through PostgreSQL `GRANT`. This depends on FEC-01's database choice and complements, but does not replace, the application check, because a single connection cannot express per-user row access.
- **Signed tokens.** A JWT carries identity and role claims, so a request needs no extra database call. The signing key comes from an environment variable (NFR-01).
- **RBAC is enough.** OWASP prefers attribute- or relationship-based control because RBAC suffers role explosion, but concedes it works for simple systems (OWASP, 2026a). CivicConnect has six roles (reporter, technician, team leader, operations manager, finance, admin) and §2.3 excludes multi-tenancy, so RBAC plus one team-scope check is enough; a full attribute engine would strain the Team Size and Schedule constraints (§2.5).

#### ASR-02 — Incident consolidation and subscriber notification

**Evidence from the PED.** §1.3.3: "if urgency is self-declared, everything will be declared urgent"; priority is instead calculated from impact, urgency, reported location and category, with a recorded supervisor override. Every reporter expects acknowledgement, but technicians need to resolve just one incident — related reports are grouped under one parent incident and every reporter subscribes to it. FEC-01 leaves the relational-versus-NoSQL choice open because the system needs "transitions for request statuses and safe storage of user data."

**Measurable expectation.**
- Given an open parent incident for "Network outage" at "First Floor," when another reporter submits the same category and location, no new parent is created. The report count rises by exactly one, the reporter gets their own reference, and they appear in the subscriber list once, even if their client retries.
- Given twenty reporters submit the same category and location in one second, there is exactly one open parent, each reporter appears in its subscriber list once, and technicians see one work item.

**Design implications.**
- **One transaction.** "At most one open parent per category and location, one subscription per reporter" cannot survive replication lag, so submission runs as one aggregate in one transaction, not across separate services or pipelines.
- **Enforce it in the database.** Check-then-insert is racy under PostgreSQL's default Read Committed isolation. A partial unique index on the clustering key, limited to open rows, plus an atomic `ON CONFLICT DO UPDATE` upsert guarantees one outcome even under high concurrency — the upsert needs the index as its conflict target, so the two are one decision (depends on FEC-01; realised in §6.3).
- **Observer pattern.** Reporters subscribe to the parent incident, and each state change notifies every subscriber (Gamma et al., 1994). The subscriber list also backs each reporter's own reference, so acknowledgement and notification stay in sync.
- **Safe retries.** Submission is a create, so it is not idempotent by method (Fielding, Nottingham and Reschke, 2022, §9.2.2). A client-supplied submission key, stored server-side, with replays returning the stored result, is proposed since the PED says nothing about retries.
- **Computed priority.** Priority comes from category and location, not the reporter. The supervisor override carries a recorded reason, so it is an auditable write — this links to ASR-05.

#### ASR-03 — Bounded configurability without a rules engine

**Evidence from the PED.** §2.2.4 requires administrators to create, update, activate or deactivate categories "without impacting the existing request data," and changes must "not delete or overwrite historical request data that would provide accountability." §2.2.4's configuration boundary aims for "controlled configurability at the office level instead of any system customization"; CivicConnect "was never meant to work as a generic no-code application building tool." §2.5 repeats this. ASM-06 assumes it is "achievable via a well-controlled combination of configurable components including categories, operational scope and role."

**Measurable expectation.**
- Given an administrator creates a category with a default team and urgency, when it is saved, it is immediately selectable on the submission form and used in priority calculation for new requests, with no code change or redeployment.
- Given an administrator deactivates a category that historical requests use, when they confirm, no historical request is changed or deleted, and past reports still show the category.
- Given an administrator looks for a way to add a form field or a status transition outside the documented set, when they search the product, no such feature exists — intended under §2.3, not a bug.

**Design implications.**
- **Configuration is data, not code.** Categories, areas, teams and roles are rows in one shared schema, with no office-specific code branches or schema variants. Deactivating a category changes its state and never deletes it, so configuration rows fall under ASR-05's accountability rules.
- **Strategy pattern, not a rules engine.** Priority calculation (§1.3.3) is one named algorithm behind one interface, and administrators set only its inputs — this is DD-02 (§6.5.3).
- **No user-authored logic in the stack.** No scripting runtime, expression interpreter or workflow-definition language; adding one expands scope and needs a scope control impact assessment (§2.6).
- **Where variability lives.** Configurability depends on whether variability sits in external data or in compiled logic, not on how much of it there is.

#### ASR-04 — Cost-constrained, portable, container-deployable operability

**Evidence from the PED.** §2.5's Cost row: "free or low-cost services must be preferred when possible, and potential operational costs and platform capabilities must be known." ASM-03 assumes deployment technologies "will not cost too much." RSK-01 is the main risk, caused by "unanticipated operational costs or exceeding free-tier resource limitations," mitigated by documenting "resource limits for Docker containerisation deployments early," with contingency to "migrate the chosen backend to an alternative low-cost hosting provider." FEC-02 requires "logging, monitoring, health checks and failure detection before staging and launching to production," deferring only the tool choice. §6.6 forbids committing secrets, and NFR-01 restates that as an acceptance criterion.

**Measurable expectation.**
- Given the application is a container image with all environment-specific values in environment variables (NFR-01), when it is redeployed to a different low-cost host, it starts and serves requests with no source-code change.
- Given the running application, when an operator or hosting probe queries the health endpoint, it reports within a bounded time whether the application and its database are healthy (FEC-02).

**Design implications.**
- **Stateless single deployable.** No in-memory sessions, no server affinity, no reliance on host-local files, so changing provider is a redeploy, not a rewrite. This is why RSK-01 names Docker.
- **Container plus external configuration.** The container makes the runtime portable; environment variables make the behaviour portable. The twelve-factor approach is proposed for every value that varies by environment (database host, provider limits), not only secrets.
- **Observability as a seam.** FEC-02 defers the tooling, not the obligation — a health endpoint and a single logging abstraction are committed now, so a later tool choice is only a configuration change. OWASP says logs should record "when, where, who and what," and that passwords, tokens and session identifiers "should usually not be recorded directly in the logs" (OWASP, 2026b). This links to §6.3.5's append-only audit design.
- **Cost applies to the database too.** Cheap application hosting with an always-on replicated database would break the same constraint; one answer covers both tiers — accept bounded downtime and protect data with backups (§6.3.6).

#### ASR-05 — Concise-but-traceable capture with a system-generated audit trail

**Evidence from the PED.** §1.3.3: technicians want to resolve a ticket "as quickly as possible with minimal typing," while finance wants "a detailed report with as much traceability to physical goods and hardware as possible"; the resolution is a report that is "as concise as possible while retaining all relevant and important data." §2.2.3 has management see "request history and audit details." §2.2.4 requires that configuration changes "should not delete or overwrite historical request data that would provide accountability." §1.3.3 requires that a supervisor's priority override carry "a recorded reason."

**Measurable expectation.**
- Given a technician resolves a request by picking an outcome and an equipment identifier and typing a short summary, when finance runs a claims report, it lists the resolution against the correct equipment and supplier, using the structured selections.
- Given any priority override, status change or configuration deactivation, when a manager opens the audit trail, an entry shows who acted, what changed, when, and the old and new values. The system writes it, and no user logs anything by hand.

**Design implications.**
- **Solve the conflict in the schema.** Required structured fields (outcome, equipment link, short summary) sit apart from optional free text, with the equipment link mandatory only where finance needs it for claims. Free-text resolutions would force finance to parse prose, and fixing that later means reprocessing history.
- **The write path creates the audit entry.** The operation that makes a change also writes its audit entry, so users type less and traceability still happens — each entry records "when, where, who and what" (OWASP, 2026a).
- **Append-only through privileges.** A coding convention cannot guarantee administrators "should not delete or overwrite." The application's database role gets `SELECT` and `INSERT` on the audit table but not `UPDATE` or `DELETE`, using PostgreSQL's per-privilege `GRANT` (depends on FEC-01; realised in §6.3.5).
- **Keep personal data out.** Requests carry personal information, so OWASP's rule to "never log data unless it is legally sanctioned" applies to the audit payload too (OWASP, 2026a).

#### ASR-06 — Access-controlled publication of unresolved incidents

**Evidence from the PED.** §1.3.3: "Users want reports that were already made to be visible, whereas the operations manager would prefer not to display a list of unresolved issues," resolved in favour of publication, with per-category resolution statistics as a fallback. §2.2.1 lets requesters see their own requests and history. §2.2.2 limits staff to "service requests which they are authorised to see." §1.3.1 — Information Regulators oversee "the lawful processing of staff personal information."

**Measurable expectation.**
- Given an unresolved incident, when any authenticated user views the published list, they see category, location, status and priority. Reporter identity, assignee identity and internal notes are absent from the response payload, not just hidden by the client.
- Given the same incident, when a staff member on the assigned team views it, they also see the assignee, the notes and the full status history.
- Given a request for per-category resolution statistics, only aggregate counts come back, with no identifiers.

**Design implications.**
- **One store, one projection per audience.** The published list, own history, team queue and management analytics each expose different fields from the same data, gated by ASR-01's enforcement point. A separate public copy would need its own sync, access control and privacy review that §2.5's Team Size row cannot fund, and would reintroduce the staleness ASR-02 removed.
- **Classify fields first.** Every attribute a read path can reach is classified for personal information and recorded on the data model; inclusion in the published projection is opt-in per column, so adding a column cannot widen exposure. POPIA is cited at Act level only, because its PDF could not be text-extracted; the Regulator's own mandate carries the enforcement claim.
- **A separate endpoint, not a flag.** The published list has its own endpoint and narrower response shape — developers "must never rely on client-side access control checks" (OWASP, 2026a), and a field hidden in the browser has already crossed the network.
- **Statistics as the fallback.** Per-category statistics are an aggregate query with no row identifiers, kept as their own projection, so a reversal of the publication decision is a routing change, not a redesign.

### 6.2 Architecture Design

#### 6.2.1 Architecture Context

CivicConnect will be developed as a configurable service request management platform for an office environment. The approved M1 baseline requires provision of request submission and categorisation, status change control, assignment and accountability, audit/history details, authorised information access based on role, reporting and category configuration control. The project scope excludes enterprise multi-tenancy, a no-code workflow tool, a mobile application and any non-essential external integration in the current phase (CivicConnect Team, 2026).

The M2 brief requires the team to assess practical architectural options, choose an appropriate architecture based on the project's specific quality drivers and constraints, produce diagrams that show responsibilities and boundaries, differentiate architecture from technology decisions, and document decisions and consequences in Architecture Decision Records (SEN381 Teaching Team, 2026c).

Because the system is developed by a team of three students within a limited academic calendar, architectural complexity must be justified by the value it adds. The architecture segregates and maintains itself without the overhead of a distributed architecture (Bass, Clements and Kazman, 2021; Fowler, 2015).

#### 6.2.2 Architecture Alternatives Considered

Three realistic architecture options were assessed. The comparison is an engineering judgement for this project on the basis of M1 and M2, not a technology-stack choice.

| Alternative | Strengths for CivicConnect | Weaknesses / Risks | Fit with Team & Schedule | Decision |
| --- | --- | --- | --- | --- |
| Layered modular monolith | Independent layers for different concerns; single deployable application; facilitates modular growth; simple testing and deployment. | Discipline is needed to maintain boundaries, otherwise it may end up tightly coupled. | High — proportionate for a three-person team and current scope. | **Selected** |
| Traditional tightly-coupled monolith | Simple initial setup; low deployment complexity. | Harder to keep business logic, UI and persistence code separate as configurability increases. | Medium — easy initially but higher maintainability risk. | Rejected |
| Microservices / distributed services | Service boundaries and scalability help in large systems. | Introduces API, networking, deployment, observability, distributed-data and failure-handling complexity with no evidence of need (Fowler, 2015). | Low — excessive complexity for present scope and schedule. | Rejected |

#### 6.2.3 Selected Architecture

The architecture selected for CivicConnect is a layered, modular monolith. The system remains a single deployable entity, with responsibilities segregated into internal layers/modules (Bass, Clements and Kazman, 2021).

> *[Figure 1: Proposed CivicConnect layered modular-monolith architecture — diagram in the source document, not reproduced in this conversion.]*

#### 6.2.4 Architectural Responsibilities and Boundaries

| Layer / Boundary | Primary Responsibility | CivicConnect Examples | Boundary Rule |
| --- | --- | --- | --- |
| Presentation | Handles user interaction and role-appropriate views. | Tickets submitted by the requester; staff queues; management view; administration screens. | Should not contain core business rules or direct persistence logic. |
| Application | Coordinates system use cases and application workflows. | Create request, assign request, alter status, update resolution, define category. | Coordinates domain behaviour; does not own database-specific implementation. |
| Domain | Contains core business concepts, rules and state. | Ticket, Category, Status, Assignment, User/Role, transition control rules. | Core rules must be kept free from UI and database considerations wherever possible. |
| Infrastructure | Implements technical concerns required by higher layers. | Adapters for authentication/authorisation, persistence, configuration, logging, and possible future external service adapters. | Technical details should support rather than define domain rules. |
| Persistence boundary | Stores and retrieves controlled project data. | Tickets, ticket categories, assignments, ticket history, users/roles, and auditing information. | Final database technology/schema stays aligned with §6.3's persistence decision. |

**Module direction.** The initial logical modules follow the approved business capabilities, not a technical split, and are refined further as M2 requirements and the ASR review settle: **Request Management** (creation, view, assignment, status-change control, actions, resolution), **Configuration** (controlled categories, organisational handling information, approved office-level configuration), **Identity and Access** (user identity, role-based permissions, authorisation boundaries), **Oversight and Reporting** (management views, request status/activity information, accountability-focused reporting), **Audit / History** (traceable status/action history for accountability and later verification).

#### 6.2.5 Rationale Against Project Constraints and Quality Needs

| Driver / Constraint | Architecture Response | Reasoning |
| --- | --- | --- |
| Maintainability / configurability | Separate modules and layered responsibilities. | Configuration may change without separate programs per office, while staying within the configuration boundary (ASR-03). |
| Security / least privilege | Dedicated identity/access boundary and separation of presentation from protected application/domain operations. | Supports RBAC and stops UI code from being the sole enforcement point (NIST, 2020; ASR-01). |
| Traceability / accountability | Domain and audit/history responsibilities remain explicit. | Facilitates controlled state transitions and request/action history retention from the M1 baseline (ASR-05). |
| Team size and schedule | Single deployable application. | Minimises deployment, integration, debugging and operations overhead for a three-student team. |
| Testability | Business logic separated from infrastructure where practical. | Makes validation of rules like legitimate ticket transitions more straightforward. |
| Deployment/cost | Avoid distributed services unless later evidence requires them. | Reduces hosting, network and observability complexity, staying inside the M1 budget constraint (ASR-04). |

#### 6.2.6 Main Interactions

Users use the CivicConnect interface via the presentation layer based on their authorised roles. The presentation layer invokes application use cases rather than manipulating stored data directly. Application services enforce domain policies such as making requests and their status changes. Domain logic expresses business rules independently of the chosen database where practical. Infrastructure elements facilitate persistence and configuration. Every request update needed to provide accountability generates audit/history information.

#### 6.2.7 Trade-offs and Risks

The architecture is proportional, not distributed — the key consequence is that all modules end up using the same deployable application and the same persistence environment.

- **Risk:** tight coupling of application modules. **Mitigation:** define module responsibilities, disallow shortcuts across layers, check dependencies on Pull Requests.
- **Risk:** business logic entangled with the chosen database or framework. **Mitigation:** keep business logic separate from database implementation where possible.
- **Risk:** the project becomes too large to develop cleanly. **Mitigation:** keep module boundaries clear and reconsider the architecture if real project evidence requires it.
- **Risk:** architecture design and implementation diverge. **Mitigation:** update architecture diagrams/ADRs/RTM whenever an architectural change is approved.

#### 6.2.8 Architecture Decision Record Summary

| Decision ID | ADR-ARCH-001 |
| --- | --- |
| Decision | Use a layered modular-monolith architecture for CivicConnect. |
| Context | Configurable office ticket-management system developed by a three-person team with controlled requirements, security/accountability needs and a limited academic schedule. |
| Alternatives | Tightly coupled monolith; layered modular monolith; microservices/distributed services. |
| Rationale | Provides internal separation, maintainability and testability while retaining a single deployable application and avoiding unjustified distributed-system complexity. |
| Main trade-off | Requires discipline to preserve module/layer boundaries; independent service deployment/scaling is not provided. |
| Primary risks | Boundary erosion, framework/database coupling, architecture-documentation drift. |
| Evidence to link | M1 scope/constraints; M2 ASRs (§6.1); technology/persistence decisions (§6.3, §6.4); architecture diagram; RTM (§4). |
| Status | Proposed — validate with team ASRs, technology and persistence decisions before M2 baseline sign-off (§6.2.9). |

#### 6.2.9 Items to Confirm Before Baseline Sign-Off

- Verify the final ASRs/quality drivers and map each key driver to this architectural choice. *(Done in §6.1 for this baseline.)*
- Ensure the chosen front-end/back-end/runtime combination can realise the proposed layer/module separation without redundant duplication.
- Adopt a relational/NoSQL persistence strategy and update the infrastructure/persistence boundary accordingly. *(Done in §6.3 and §6.4 — PostgreSQL selected.)*
- Validate the deployment direction and ensure the deployable application is compatible with the intended environment.
- Develop/endorse ADR-ARCH-001 and add architecture/module citations to the RTM for the requirements that have been impacted. *(RTM updated for FR-01 and NFR-01 in §4; remaining requirements to follow.)*

### 6.3 Data and Persistence Baseline

The full data/persistence baseline — including the complete logical model diagram reference, the full ownership/lifecycle tables, all 27 declarative constraints and the full access-pattern catalogue (AP-01 to AP-17) — is maintained in `docs/data-persistence/` and `docs/access patterns/`. This subsection carries the decisions that are architecturally significant.

#### 6.3.1 Aggregates, Ownership and Lifecycle

An aggregate is one consistency boundary: one transaction, one root, and no write to an interior table except through that root's service. Boundaries are drawn where invariants are — each aggregate owns exactly the set of tables that one transaction must write together, and nothing more. This is team judgement, argued rather than asserted, and buys the property the PostgreSQL manual states of a transaction: it "bundles multiple steps into a single, all-or-nothing operation," and "the intermediate states between the steps are not visible to other concurrent transactions" (The PostgreSQL Global Development Group, 2026).

| Aggregate | Root | Tables Owned (writable only through root) | Referenced (not owned) |
| --- | --- | --- | --- |
| Incident | `incident` | `incident`, `ticket`, `incident_subscription`, `incident_status_history`, `incident_note`, `resolution`, `submission_request` | `category`, `location`, `service_team`, `app_user`, `asset`, `resolution_code`, `priority_target` |
| User | `app_user` | `app_user`, `user_credential`, `user_role` | `role` |
| Service team | `service_team` | `service_team`, `team_member` | `app_user`, `team_role` |
| Category | `category` | `category` | `service_team` |
| Asset | `asset` | `asset` | `location` |
| Notification | `notification` | `notification` | `app_user`, `incident`, `ticket` |

The full lifecycle-ownership tables (create/mutate/deactivate/read authority per entity, and create/mutate/terminal-state/retention per entity) are reproduced in full at `docs/data-persistence/data-model-and-lifecycle-V1.0.html`. Two points carry the design:

- **Tickets and history rows are immutable.** A `ticket`, once submitted, is never deleted and is mutable only by its own reporter (own fields only); `incident_status_history`, `incident_subscription` and `resolution` rows (once superseded) are never edited or deleted — this is how ASR-05's accountability guarantee is enforced structurally rather than by convention.
- **`submission_request` is the one deliberate deletion in the design**, hard-deleted after 7 days once its idempotency window has passed; everything else is retained indefinitely or pruned on a documented schedule (notifications at 6 months, audit log at 3 years).

#### 6.3.2 Merge and Clustering Lifecycle

Two mechanisms resolve duplicate reports, resolving the mass-incident stakeholder conflict from §1.3.3:

- **Automatic clustering at submission.** If a new submission matches an open incident on `(category_id, location_id)`, it attaches to that incident instead of creating one. `report_count` increases by one, and a new `ticket` and `incident_subscription` are added. The partial unique index between `incident_id` and `reporter_id` enforces this — no lookup query is involved.
- **Manual merge by a supervisor**, for cases the clustering key misses (e.g. the same fault reported under two different categories). The absorbed incident is set to `status = 'MERGED'`, `merged_into_id` points to the survivor, and `root_incident_id` is set to the survivor's root; the survivor's `report_count` and `version` increase; one history row, one `MERGED` notification per subscriber, and one audit row are written — all in a single transaction.

Subscriptions are never re-pointed on merge (the primary key `(incident_id, reporter_id)` could collide with an existing row on the survivor), so notifications resolve the fan-out target through:

```sql
-- every reporter who must hear about incident :id, including via merge chains
SELECT DISTINCT s.reporter_id
FROM incident_subscription s
JOIN incident i ON i.incident_id = s.incident_id
WHERE COALESCE(i.root_incident_id, i.incident_id) = :id;
```

`root_incident_id` is a denormalised pointer set at merge time, so finding an incident's root is one join, not a recursive CTE — if A merges into B and B was already merged into C, A's root is set to C directly, keeping chains flat. Foreign keys guarantee `merged_into_id` and `root_incident_id` land on rows that exist: a foreign key "specifies that the values in a column (or a group of columns) must match the values appearing in some row of another table," which "maintains the referential integrity between two related tables" (The PostgreSQL Global Development Group, 2026) — without that check, one bad write could send the notification fan-out to nobody, silently.

`report_count` is a stored counter, not `count(*)` over `ticket`: the same `INSERT ... ON CONFLICT DO UPDATE` that does the clustering also updates it, so the row lock that serialises concurrent submissions is the same lock that protects the increment, adding no extra contention.

#### 6.3.3 Schema Conventions and Migrations

PostgreSQL 16+ via the Npgsql EF Core provider, with `citext` for case-insensitive e-mail matching, `timestamptz` for all timestamps, `uuid` for `app_user` (because user ids appear in tokens and URLs), and `ON DELETE RESTRICT` on every foreign key so a referenced row cannot be deleted out from under history. EF Core migrations carry the schema, one slice (aggregate) per migration file, so a reviewer can take one aggregate at a time; invariants EF cannot infer — partial unique indexes and CHECK constraints — get an explicit name and comment so a reviewer can find and challenge them without reading the whole schema. This supports the PED's two-non-author-approval rule (§7.3), because the partial unique index and the override constraint carry the stakeholder-conflict resolutions from §1.3.3. The full conventions table and the three named invariants (clustering, override, one-current-resolution) are reproduced at `docs/data-persistence/data-model-and-lifecycle-V1.0.html`.

#### 6.3.4 Access Patterns and Index Justification

Each capability is broken down into the query it implies; an index appears only when a named query pattern needs it, following the PostgreSQL manual's guidance that indexes "seldom or never used in queries should be removed," and that every index "adds overhead to data manipulation operations" (The PostgreSQL Global Development Group, 2026). Frequency is measured against ASM-07's volume assumptions (30 submissions/working day, 12 service staff, 250 requesters). The full catalogue of seventeen access patterns (AP-01 to AP-17), covering the submission form, "my requests," the idempotent replay check, the staff queue, "my work," incident detail, the management dashboard, overdue and category-analysis views, team-membership lookup, the incident timeline, replacement-by-asset reporting, the subscriber fan-out and unread-notification queries, audit lookups, and the submit/claim/transition/login write paths, is maintained at `docs/access patterns/access-patterns-V1.0.html`. Two representative rows:

| Pattern | Query | Frequency | Index |
| --- | --- | --- | --- |
| AP-04 — staff queue (open incidents for my team, by priority then age) | `WHERE assigned_team_id = ? AND status IN (open) ORDER BY priority, opened_at` | The most frequent read in the system | `incident_team_queue_idx (assigned_team_id, priority, opened_at) WHERE status IN (...)` — partial, because the predicate skips the growing pile of closed incidents |
| AP-15 — submit (cluster or create) | `INSERT ... ON CONFLICT (category_id, location_id) WHERE status IN (open)` | 30/day, in bursts | `incident_one_open_per_scope_uq` — an invariant that doubles as the lookup index |

#### 6.3.5 Persistence Model Decision

**Decision.** One PostgreSQL 16+ database as a single logical instance, with the schema enforcing invariants and the service holding business rules. 21 tables, fixed columns — the PED rules out user-defined fields (§2.3), so configurability lives in rows, not table shape. Thirty foreign keys keep related tables consistent. Twenty-seven declarative constraints, six of them partial unique indexes, carry the stakeholder-conflict resolutions (§1.3.3). Six multi-entity operations must be all-or-nothing, which single-node ACID with Read Committed plus constraints handles. The data is personal information under POPIA, so it needs table-level grants, a credential table the reporting role cannot read, TLS, and a retention mechanism — PostgreSQL documents all of these. Growth is predicted to be small (§6.3.6), so retention, not size, is the trigger for revisiting the design.

**Alternatives rejected.** A document store (MongoDB, Firestore, DynamoDB) loses its flexible-schema advantage the moment the clustering invariant becomes a partial unique index, and MongoDB's own manual says distributed transactions should not replace effective schema design (MongoDB, Inc., 2026); without foreign keys, `merged_into_id` and `root_incident_id` could break silently. A polyglot split (a document store for audit/notifications) adds a second system to secure and back up for flexibility `jsonb` already gives, and breaks the guarantee that an audit row commits with the change it records. SQLite allows only one writer at a time per database file (SQLite, 2025), which fails the mass-report scenario. MySQL/MariaDB have no partial indexes (Oracle, 2026), so six invariants — including the clustering rule — would become generated-column tricks a reviewer can't read at a glance. SQL Server could do all of it (filtered indexes, `rowversion` optimistic concurrency (Microsoft, 2026), range-locking serializable isolation) but fails on cost and hosting under RSK-01, since no free tier offers it.

#### 6.3.6 Growth Estimate, Integrity and Scalability

Using ASM-07's team-judgement figures (30 submissions/working day over 250 working days = 7,500 tickets/year; clustering at 1.25 tickets/incident = 6,000 incidents/year), the yearly load is about 176,000 rows and 75 MB of table data, with the audit log the largest single contributor (about 77,000 rows, 54 MB, roughly 72% of ten-year bytes). Over ten years that is about 1.3 GB and 1.8 million rows — an ordinary PostgreSQL database even if the submission rate is wrong by a factor of ten (about 13 GB at ten years).

**Transactions.** Everything runs at Read Committed. One service operation is one transaction; no repository commits on its own. Every `UPDATE incident` checks `version` and increments it (optimistic concurrency). The application retries serialization failures and deadlocks up to three times, but never a unique violation, which is a business outcome. Locks are taken in a fixed order, and no HTTP call or user wait happens inside a transaction. Submission, claiming, transitions, overrides and resolution are each described as a single transaction in §6.3.2 and in the full document at `docs/data-persistence/persistence-integrity-and-scaling-V1.0.html`.

**Bottlenecks, SPOF and scalability.** One PostgreSQL instance holds everything; if it fails, the whole system fails — accepted on purpose, because a replica with automatic failover doubles the hosting bill and needs skills the team lacks (RSK-01). The control for now is a backup that has been tested by restoring it. Scaling risk is otherwise small: 10–20 application connections cover 30 submissions/day; the supervisor's override uses a version check, not a held lock; a mass event clusters on one incident row, and at 100 transactions/second, 200 reports clear in about two seconds (RSK-09) — that lock is what prevents duplicates. If read replicas are added later, the reporter's history and the staff queue must stay on the primary, because a stale queue can send two technicians to the same fault. The audit log is about 70% of the bytes, so retention sweeps come before partitioning if deletion slows down.

### 6.4 Technology-Stack Decision

**Decision.** ASP.NET Core with Blazor, PostgreSQL, and Docker — documented formally as **ADR-004 (Technology Stack Selection)**, with ecosystem risks (container-orchestration learning curve, PostgreSQL migration management) added to the Risk Register (§5.2). The full decision record, including the per-technology comparison and rationale, is maintained at `docs/decisions/technology-stack-V1.0.html`.

- **Language — C# (.NET 10 LTS).** Chosen over a multi-language stack (e.g. Python backend, JavaScript frontend, MongoDB) to keep the team in one language, since a single-language stack fits the team's capability and schedule constraint (§2.5), lets the team share domains/DTOs/validation rules between client and server, and lets any of the three members meaningfully review both frontend and backend Pull Requests (supporting §7.3's two-non-author-approval rule).
- **Front-end — Blazor WebAssembly.** Chosen over React/Angular to avoid a mixed C#/JavaScript toolchain. The backend commits changes and publishes an event before returning `200 OK`, so the frontend is not blocked waiting on downstream systems. Blazor's own validation is for usability only — per ASR-01, the service layer and database remain the final backstop for business rules and data constraints.
- **Backend/Runtime — ASP.NET Core.** Chosen over database-level stored procedures or triggers to keep domain rules in testable, version-controlled C# rather than vendor-locked database logic — this is DD-01 (§6.5.2). The API controller orchestrates mandatory status transitions explicitly; the service executes each submission as one server-side transaction (§6.3.6); notification work is offloaded to asynchronous events so the worker thread pool stays available under load.
- **Database — PostgreSQL 16.** Selected to formally resolve FEC-01 (§5.3) — see §6.3.5 for the full comparison against document-store, polyglot, SQLite, MySQL/MariaDB and SQL Server alternatives. The **optimistic-concurrency mechanism** (the `version` column check described in §6.3.6) is confirmed; no further open item remains here.
- **Deployment & Build — Docker.** Chosen over bare-metal VM deployment to package the API, Blazor client and PostgreSQL database into immutable images, giving environment parity across local development, CI and staging, and keeping secrets out of source code via environment variables (NFR-01, ASR-04).

### 6.5 Initial Design Decisions

The Milestone 2 criteria require at least two real CivicConnect design problems to be carried from the Assignment 2 findings into concrete decisions for this project. Each decision states the problem, the pattern used, the alternatives considered, the expected benefit and added complexity, the affected software elements, and the controlled PED/ADR/RTM evidence supporting it. The full decision records are maintained at `docs/decisions/design-decisions-V1.0.html`.

**Evidence boundary.** As in §6.2, the current repository evidence does not yet show implementation of these decisions in application code; branch/class/PR/test references will be added once development reaches these areas.

#### 6.5.1 Design Context and Decision Principles

The M1 baseline requires controlled ticket-status transition, action/comment/resolution recording, requester feedback and request-history auditability — an architectural question of how to coordinate status-change behaviour without one service gaining unrelated responsibilities. The M1 baseline also establishes CivicConnect as a configurable office ticket system: controlled categories, classification, organisational processing and roles are supported; a generic no-code workflow engine, arbitrary fields, scripting and enterprise multi-tenancy are out of scope (§2.3). The decision lenses were cohesion, controlled coupling and SOLID principles; patterns and interfaces were not assumed to automatically improve design, and any added indirection needed a reason grounded in expected variability (Budgen, 2021; Martin, 2003).

#### 6.5.2 Design Decision DD-01 — Controlled Ticket Status Reactions

**Design problem.** Changing a ticket's status can require more than updating a field — validation, persistence, audit/history, and notification. Putting every current and future reaction into one method of a `TicketService` class risks that service becoming a catch-all.

| Alternative | Strength for CivicConnect | Main trade-off / risk |
| --- | --- | --- |
| Explicit orchestration | Required operations and sequencing stay visible and predictable; strong fit for persistence, audit/history and other consistency-critical work. | Can grow into an "everything happens here" service if independent reactions keep accumulating. |
| Observer | Separates independently varying reactions from core transition logic; new observers can be added without editing the transition service (Gamma et al., 1994). | Introduces indirection, registration/dispatch complexity, possible ordering/failure-handling uncertainty, and extra integration-test needs. |

**Decision.** Use explicit application-service orchestration for mandatory status-transition behaviours. Use the Observer pattern only for confirmed secondary responses that vary independently and do not affect the primary transition outcome — transition validation, persistence and mandatory audit/history stay explicit; on success, an internal domain/application event may be published, and secondary effects (e.g. requester notification) subscribe to it.

**Rationale, benefit, trade-off.** This keeps correctness-dependent behaviour visible while adding an extension point only where variability exists independently. Expected benefit: cohesion in the transition logic, better coupling to mandatory collaborators, and room to append independent actions without repeatedly editing the transition logic. Complexity introduced by Observer (dispatch, failure semantics, ordering, integration tests) is deferred until there are enough reactions to justify it (Gamma et al., 1994). SOLID link: SRP separates independently varying reactions; OCP applies only where repeated extension is demonstrated (Martin, 2003).

**Affected software elements.** Ticket application/service layer; ticket status-transition validation/policy; ticket repository/persistence boundary; request history/audit recording; optional internal event publisher/dispatcher; secondary reaction handlers (e.g. requester notifications) once implemented.

#### 6.5.3 Design Decision DD-02 — Controlled Office Configuration

**Design problem.** CivicConnect must be configurable per office (categories, classifications, organisational areas, roles) without coding every office separately, and without a generic rules/workflow engine that would take the product out of scope (§2.3).

| Alternative | Strength for CivicConnect | Main trade-off / risk |
| --- | --- | --- |
| Controlled data-driven configuration | Office differences validated through data, with a configuration role owning them; the core ticket service consumes supported configuration rather than hard-coded office values. | Needs configuration validation, authorisation, persistence and careful boundaries so invalid configuration cannot corrupt ticket operation or history. |
| Strategy | Packs distinct algorithms behind one contract, selectable at runtime (Gamma et al., 1994). | Introduces a strategy interface and selector; wrong fit when offices share one algorithm with different data rather than different behaviour. |

**Decision.** Use controlled data-driven configuration as the default for office differences. Do not use Strategy for categories, roles, classifications or organisational areas unless evidence shows genuinely different algorithms or policies that cannot be encoded as configuration data. A configuration role validates and controls access to supported configuration; ticket-processing components consume that validated configuration but keep one ticket workflow — this matches the M1 configuration boundary (§2.2.4, ASR-03).

**Rationale, benefit, trade-off.** The identified office differences are values within a controlled envelope, not alternative algorithms, so treating them as configuration keeps the design balanced. Expected benefit: office values change without altering ticket-processing logic, values stay consistent, and there are no redundant office-specific branches. Complexity introduced: validation, authorisation, persistence and audit/history for configuration changes, with defined constraints so configuration cannot become a user-authored workflow. **Strategy deferment:** Strategy remains a future option if significant algorithmic variation appears (e.g. a different prioritisation or routing approach); otherwise the configuration approach stands (Gamma et al., 1994).

**Affected software elements.** Administration/configuration UI or endpoints; configuration service/module; category and classification configuration; role/authorisation configuration boundary; organisational area/team configuration; ticket services that consume validated configuration; configuration persistence and audit/history rules.

#### 6.5.4 ADR and Traceability Evidence

Every significant design decision must be documented in a controlled ADR/design record and linked to the RTM (§4) with evidence, as PED v2.0 evidence is integrated.

| Decision record | Decision | Primary M1 link | M2 evidence to add | Implementation status |
| --- | --- | --- | --- | --- |
| ADR-DD-01 | Mandatory explicit orchestration plus conditional Observer for secondary responses | Status transition control; history/audit; requester feedback | ADR, design diagram, RTM link, branch/classes/PR/tests once developed | Not yet evidenced in code |
| ADR-DD-02 | Controlled configuration; Strategy deferred until algorithmic variation is shown | Configurable categories/classifications/roles; configuration boundary | ADR, configuration design, RTM link, schema/classes/PR/tests once developed | Not yet evidenced in code |

#### 6.5.5 Evidence Required Before M2 Baseline Sign-Off

- Confirm DD-01 and DD-02 conform to the selected architecture (§6.2), ASRs (§6.1), data/persistence architecture (§6.3) and technology stack (§6.4).
- Create controlled ADR/design records for both decisions in the project decision evidence.
- Update the RTM so the appropriate requirements reference the design decision, not just a "Milestone 2" placeholder (§4).
- Once implementation starts, add module/interface locations with branches, commits and Pull Request evidence.
- Provide verification evidence for implemented behaviour, or mark it Planned/Not Yet Implemented.
- If development proves an assumption false (e.g. office variation turns out to be algorithmic rather than data-driven), revise the decision through the controlled ADR/change process (§2.6).

---

## 7. GitHub and Team Governance

*(Unchanged from PED v1.0.)*

GitHub is used as a controlled software engineering environment for the CivicConnect project, not just as a place to store the source code. The repository is designed to maintain project history, manage changes, facilitate peer reviews, provide personal contributions, and achieve traceability of project tasks, requirements, documentation and subsequent implementation. This strategy utilizes the mandatory GitHub governance and configuration management controls required by the project brief of the SEN381 project (SEN381 Teaching Team, 2026a). Within the current scope, the CivicConnect system has been configured to function as a configurable solution for managing internal service requests in office-based institutions. This system can be applied to varied office-based environments by configuring the system using varied request types, office sections, and access control.

Three long-lasting branches are used in the project — development, staging and main — to differentiate the working, verification and product state of the product. The feature and documentation development is done using the development branch, followed by review and merge into staging, and if the change is approved for the product state, it is further merged into the main branch. The integration of GitHub and ClickUp can be explored at a later stage as a way to improve project management; however, it is not considered an implemented M1 control.

### 7.1 Repository Purpose and Current Structure

The controlled team repository is SEN381 (`f-jooste/SEN381`). When this M1 review was conducted, the repository consisted of the three branches agreed upon: `main`, `staging`, and `dev`. The `main` branch is the controlled project/product branch, `staging` is the validation branch before the main one, and `dev` is the integration branch that gives rise to features/documentation branches.

| Branch | Purpose | Typical changes | Promotion rule |
| --- | --- | --- | --- |
| `dev` | Shared integration branch for active team development and controlled documentation work. | Feature work, documentation updates and integration of reviewed work. | Work should arrive through focused branches/PRs where substantive; completed integration is promoted to staging. |
| `staging` | Controlled pre-main environment for integrated verification before release/promotion. | Combined changes that require controlled validation before main. | Only changes that have been integrated and reviewed should progress toward main. |
| `main` | Controlled product/project state and the branch associated with the release-ready baseline. | Approved project state only. | Substantive changes enter through Pull Requests and must satisfy the required review/approval model. |

### 7.2 Branching and Change Workflow

> Feature / documentation branch → `dev` → `staging` → `main`

1. A valuable ClickUp task/GitHub Issue represents the work to be performed.
2. The contributor checks out a narrow scope branch from the corresponding development branch, which is usually `dev`.
3. The contributor performs progressive and meaningful commits, explaining the engineering change.
4. A Pull Request is created for a meaningful change instead of direct merge into the controlled branch.
5. The change is reviewed based on its compliance with requirements, correctness, maintainability, security, test impact, and documentation impact where applicable.
6. The contributor addresses the review comments and fixes the change, if needed.
7. The approval by other parties is obtained prior to merge.
8. The changes are merged through `staging` to `main` only if the respective controls are met.

### 7.3 Pull Requests and Peer Review

In relation to the SEN381 Master Project Brief, Pull Requests will be needed for any substantive changes committed into master and there should be at least two approvals from other team members, excluding the author. Self-approval is prohibited and the approval has to reflect a genuine review and not a rubber-stamp action (SEN381 Teaching Team, 2026a). Changes to documentation follow the same principle of governance as changes to source code.

| Review focus | Examples of reviewer questions |
| --- | --- |
| Requirements and acceptance criteria | Does the change still match the approved requirement and acceptance criteria? |
| Correctness and consistency | Does the change behave or read as intended, and is it consistent with the controlled project artefacts? |
| Maintainability / technical debt | Does the change create unnecessary complexity, duplication or future maintenance cost? |
| Security / privacy | Could the change expose credentials, sensitive information or weaken access controls? |
| Testing / regression | What should be verified now or later, and could the change affect existing behaviour? |
| Documentation / traceability | Do the PED, RTM, issue/task, decision or risk records also need to be updated? |

**Preferred review evidence path:** Review comment → Author response → Correction → Re-review → Approval → Merge.

### 7.4 Current Governance Status and Compliance Gap

The repository live review on 7 September 2026 revealed that `main`, `staging`, and `dev` branches have been created and that a live repository ruleset is being used with Pull Requests control on default and staging. However, the current ruleset calls for only one approving review. This is still below the SEN381 standard, which requires two approving reviews from team members other than the author. It is the responsibility of the repository owner to change the number of approving reviews to two.

| Control | Current evidence (7 Sep 2026) | Action |
| --- | --- | --- |
| Team repository | Repository `f-jooste/SEN381` exists. | Retain as the single controlled repository unless a different structure is formally justified. |
| Branches | `main`, `staging` and `dev` exist. | Use the agreed flow consistently and preserve progressive history. |
| Protected controlled branches | Active ruleset applies to the default branch and staging. | Demonstrate the live rule/settings during review if requested. |
| Pull Request requirement | Ruleset requires PR-based changes. | Use real PRs for substantive documentation/governance changes. |
| Required approvals | Current configuration requires 1 approval. | Change to 2 non-author approvals to comply with the project brief. |
| PR / Issue history | No Pull Requests or Issues were visible at the time of the M1 review. | Create authentic evidence as real work progresses; do not reconstruct activity immediately before assessment. |

### 7.5 Task Traceability and ClickUp Relationship

ClickUp remains the team's primary progress-management environment for task planning, assignment and project tracking. GitHub provides the controlled repository evidence for changes, commits, branches, Pull Requests and peer review. Where practical, a substantive repository change should reference the related ClickUp task so that planning evidence and repository evidence remain connected rather than existing as two unrelated systems. Any suggestion for a change that would move CivicConnect outside of these well-defined configuration boundaries will need to be regarded as a scope change and handled via formal impact assessment and baseline change control procedures.

**Recommended traceability path:** ClickUp task → GitHub Issue (where useful) → branch → commit(s) → Pull Request → review/approval → merge.

An automated GitHub–ClickUp integration for notifications or tracking may be investigated as a later project improvement. Until it is actually implemented and verified, it should be recorded as a planned enhancement rather than claimed as an existing control.

### 7.6 Repository Security and Secrets

Secrets such as passwords, API keys, tokens, private keys, and any other type of secret information are prohibited from being checked into the repository. Environmental configurations and secrets will be handled in a secure way via approved configuration techniques and not by hard coding or checking in secret files into the repository.

### 7.7 Progressive Evidence and Individual Accountability

GitHub evidence should be generated progressively across milestones. The team generates meaningful issues/tasks, branches, commits, Pull Requests, and reviews to document any changes in documentation and engineering artifacts regardless of whether actual application coding is done (SEN381 Teaching Team, 2026b). Bulk generation or reconstructed activity right before the assessment does not demonstrate a controlled engineering process.

Each individual member should build a valid history of contribution through meaningful commits, issue or task ownership, authorship of Pull Requests, meaningful review of Pull Requests, and contribution to controlled documentation or decisions. All three members should understand the entire project foundation and the reason for control in the repository.

### 7.8 M2 Evidence to Present

- The live SEN381 repository and the `main`/`dev`/`staging` branch structure, with required approving reviews at two.
- ADR-ARCH-001, ADR-004 and ADR-DD-01/ADR-DD-02 committed as controlled decision records, cross-linked from this PED (§6.2.8, §6.4, §6.5.4).
- The updated RTM entries for FR-01 and NFR-01 (§4), and progress on extending Design/Architecture references to the remaining requirements.
- Meaningful review evidence for the Milestone 2 documentation (architecture, persistence baseline, technology-stack decision, design decisions), including comments and author rework where genuine review issues arose.
- Progressive commit history showing that controlled documentation evolved during M2 rather than being uploaded only at the end.
- Individual contribution evidence for all three team members.

---

## 8. AI Usage

*(Unchanged from PED v1.0. M2-specific AI usage entries, if any, will be appended here as they occur — none are recorded in the repository evidence reviewed for this version.)*

| Date | Student | Tool | Engineering task | AI contribution | Verification | Decision | Issues found |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 06/09/2026 | Shaun | Gemini AI | Research | The question: Are the risks that I have identified appropriate risks for CivicConnect | I conferred with my team if the risks that Gemini identified are risks we should consider | Did not accept this result | The risks were already planned for, or were not applicable to CivicConnect |
| 08/09/2026 | Fourie | Claude | Research | "What are significant stakeholders to be aware of in this scenario" with an upload of the Project Requirements Specification | Verified that project direction is aligned with what the rest of my team envisioned, verified that the project direction is applicable with my lecturer and went through Project Requirement Specification to verify that relevant stakeholders and their needs were being addressed as in section 3 of the document | Accepted some of the results | Originally the stakeholders were not applicable with CivicConnect and after prompt refinement some stakeholders were redundant and still not applicable. |
| 07 Sep 2026 | Bernard Small | ChatGPT | Develop and review CivicConnect Scope Baseline | Provided assistance in structuring the scope as In-Scope, Out-of-Scope and Deferred capabilities; scope limitations and assumptions; as well as the reason for the deliberate omission of WhatsApp integration from the scope. | Comparison of the proposed scope with the CivicConnect Master Project Brief and Milestone 1 requirements. Scope was evaluated paragraph by paragraph prior to its approval by the student. | Keep a controlled scope starting with the approved requester, staff and management abilities. Postponing other integrations and features without an official approval through change control. | Scope creep was seen from adding the option of extra features. WhatsApp integration was seen as the addition of unnecessary APIs, security, and authentication at this stage. |
| 09 Sep 2026 | Bernard Small | ChatGPT | Refine Scope Baseline for a reusable office ticket-management system | Provided assistance in restructuring the scope from a community-specific scope to an internal office ticket management system with configurable capabilities, exclusions, future scope and one more configurability limitation. | Updated scope was validated for meeting minimum business capabilities and project constraints. External references in scope were evaluated separately and the final language and boundaries were evaluated by the student. | CivicConnect would ensure reusability in different offices by means of controlled configuration of categories of requests, office locations and abilities, rather than customizing the system for each individual office. | The use of the phrase "any office" implied a generic no-code system or enterprise multi-tenant software. The scope was thus restricted to only configuration and not open workflow or form creation. |
| 07 Sep 2026 | Bernard Small | ChatGPT | Analyse and document GitHub and team governance for Milestone 1 | Provided assistance in understanding the GitHub governance requirements and drafting a description of the repository purpose, branch ownership, Pull Request control, peer review, ClickUp traceability, repository security and evidence progression requirements. | Comparisons were made between governance recommendations and the SEN381 Master Project Brief and Milestone 1 requirements. The resulting governance plan and repository control plan were evaluated by the student. | GitHub is the controlled environment for engineering and evidence while ClickUp is the environment for task management. Any substantive changes should be controlled by branches, Pull Requests and peer reviews. | The governance review confirmed that there were two non-author approvals necessary according to the project brief. The need for proper progressive PR, issues, review, and commit evidence was also identified for this milestone. |
| 09 Sep 2026 | Bernard Small | ChatGPT | Align GitHub governance documentation with the updated CivicConnect scope | Provided assistance in determining which sections of the GitHub governance document were impacted by the modified scope and making changes only to the sections relevant to the scope modifications. | Updated wording for GitHub was compared with the latest Scope Baseline. The changes were approved by the student and the highlighting used for the comparison was removed. | The new governance of GitHub is defined as CivicConnect being a configurable office-based service request system. Changes going beyond the approved configuration boundary are scope changes. | There was a discrepancy in the previous GitHub document in relation to the new configurable-office direction. However, it was sorted out without altering the existing governance process. |

---

## 9. PED v2.0 Baseline Sign-Off

| Baseline review item | Team record / decision |
| --- | --- |
| Project | CivicConnect |
| Baseline Type | Milestone 2 Architecture, Quality Drivers & Persistence Baseline |
| Version | 2.0 |
| Date | |
| Scope reviewed | YES / NO |
| Requirements/traceability checked | YES / NO |
| Risk review completed | YES / NO |
| ASRs/quality drivers confirmed (§6.1) | YES / NO |
| Architecture (ADR-ARCH-001) confirmed (§6.2) | YES / NO |
| Data/persistence baseline confirmed (§6.3) | YES / NO |
| Technology-stack decision (ADR-004) confirmed (§6.4) | YES / NO |
| Design decisions (ADR-DD-01/ADR-DD-02) confirmed (§6.5) | YES / NO |
| Repository/governance controls checked | YES / NO |
| Outcome | ACCEPTED / CONDITIONALLY ACCEPTED / REVISION REQUIRED |

---

## References

Atlassian. n.d. *What are request types?* Jira Service Management Cloud. Available at: <https://support.atlassian.com/jira-service-management-cloud/docs/what-are-request-types-in-a-service-project/> (Accessed: 9 September 2026).

Bass, L., Clements, P. and Kazman, R. 2021. *Software Architecture in Practice.* 4th ed. Boston: Addison-Wesley.

Budgen, D. 2021. *Software Design.* 3rd ed. Harlow: Pearson.

CivicConnect Team. 2026. *CivicConnect Milestone 1 Scope and Constraints Baseline.* Internal project document.

Fielding, R., Nottingham, M. and Reschke, J. 2022. *HTTP Semantics.* RFC 9110, §9.2.2. IETF.

Fowler, M. 2015. *Microservices.* martinfowler.com.

Gamma, E., Helm, R., Johnson, R. and Vlissides, J. 1994. *Design Patterns: Elements of Reusable Object-Oriented Software.* Boston: Addison-Wesley.

Martin, R.C. 2003. *Agile Software Development: Principles, Patterns, and Practices.* Upper Saddle River: Prentice Hall.

Microsoft. 2026. *SQL Server documentation — rowversion and filtered indexes.* Microsoft Learn.

MongoDB, Inc. 2026. *MongoDB Manual — Transactions.*

NIST. 2020. *Attribute-Based Access Control (ABAC) and Role-Based Access Control (RBAC).* National Institute of Standards and Technology.

Oracle. 2026. *MySQL 8.0 Reference Manual.*

OWASP. 2026a. *Access Control Cheat Sheet.* OWASP Foundation.

OWASP. 2026b. *Logging Cheat Sheet.* OWASP Foundation.

SEN381 Teaching Team. 2026a. *SEN381 Software Engineering 381: CivicConnect Master Project Brief — Integrated Team Software Engineering Project.* Version 1.1. Belgium Campus ITversity.

SEN381 Teaching Team. 2026b. *CivicConnect Project: Milestone 1 — Engineering Foundation & Requirements Baseline.* Belgium Campus ITversity.

SEN381 Teaching Team. 2026c. *CivicConnect Project: Milestone 2 — Architecture, Persistence and Design Baseline.* Belgium Campus ITversity.

SQLite. 2025. *File Locking And Concurrency In SQLite.* SQLite Documentation.

The PostgreSQL Global Development Group. 2026. *PostgreSQL 16 Documentation.*

Zendesk. 2026. *Creating multiple ticket forms.* Zendesk Help, edited 1 May 2026. Available at: <https://support.zendesk.com/hc/en-us/articles/4408846520858-Creating-multiple-ticket-forms> (Accessed: 9 September 2026).

---

## PED Review Notes

*(No content in source document.)*
