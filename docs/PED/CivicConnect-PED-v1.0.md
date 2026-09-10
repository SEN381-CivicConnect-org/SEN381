# CivicConnect

## Project Engineering Document (PED)

**Version 1.0 — Engineering Foundation & Requirements Baseline**

| Field | Value |
| --- | --- |
| Module | Software Engineering 381 — SEN381 |
| Project | CivicConnect |
| Milestone | Milestone 1 — Engineering Foundation & Requirements Baseline |
| PED Version | 1.0 |
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
6. [GitHub and Team Governance](#6-github-and-team-governance)
   - [6.1 Repository Purpose and Current Structure](#61-repository-purpose-and-current-structure)
   - [6.2 Branching and Change Workflow](#62-branching-and-change-workflow)
   - [6.3 Pull Requests and Peer Review](#63-pull-requests-and-peer-review)
   - [6.4 Current M1 Governance Status and Compliance Gap](#64-current-m1-governance-status-and-compliance-gap)
   - [6.5 Task Traceability and ClickUp Relationship](#65-task-traceability-and-clickup-relationship)
   - [6.6 Repository Security and Secrets](#66-repository-security-and-secrets)
   - [6.7 Progressive Evidence and Individual Accountability](#67-progressive-evidence-and-individual-accountability)
   - [6.8 M1 Evidence to Present](#68-m1-evidence-to-present)
7. [AI Usage](#7-ai-usage)
8. [PED v1.0 Baseline Sign-Off](#8-ped-v10-baseline-sign-off)
- [References](#references)
- [PED Review Notes](#ped-review-notes)

---

## 1. Problem, Stakeholder and Business Value

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

- All users reporting an issue will want their issue to take top priority; however, technicians will already have a pre-populated ticket queue. The problem is that if urgency is self-declared, everything will be declared urgent. **The solution** will be to have priority calculated off impact and urgency, using the reported location and category. However, an override will still exist for the team supervisor to exert human discretion, with a recorded reason.
- When the network drops or a similar mass reportable incident takes place, every user that reports the incident wants their report to be acknowledged, but the technicians want to resolve only one incident. **The solution** would be to cluster related reports into a parent incident and subscribe every reporter to it. Each person keeps their own reference and receives updates, but technicians only work with one item.
- Technicians want to resolve a ticket as quickly as possible with minimal typing, whereas the finance department wants a detailed report with as much traceability to physical goods and hardware as possible to track faulty goods that need to be replaced or notify suppliers about faulty equipment. **The solution** is to make the report a technician must fill in as concise as possible while retaining all relevant and important data.
- Users want reports that were already made to be visible, whereas the operations manager would prefer not to display a list of unresolved issues. **The solution** is to publish unresolved incidents, as the argument for publishing the data is stronger than against; however, it is also possible to publish resolution statistics per category.

---

## 2. Scope Baseline

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

**Assumption review.** ASM-06 must be reviewed once the stakeholders provide more detailed guidance about the extent to which customization at the office level will be needed. In case this information has an impact on committed functionality, the scope baseline and the associated requirements must be updated via controlled change.

---

## 3. Requirements and Acceptance Criteria

The team will implement a ticket-based approach to track functional and non-functional requirements. Each requirement will have an identifier and description and can later be presented to the stakeholder for review and approval.

### 3.1 Functional Requirements

Functional requirements will use the following identification structure:

`FR-XX`

These tickets will be used to track the functional requirements of CivicConnect.

Example: `FR-01`

**FR-01 — Submit and Categorise a Service Request**

- **Source:** Requested Capabilities
- **Requirement:** The system will allow authorised users to submit a ticket request and categorise it using a controlled category mechanism.
- **Acceptance Criteria:** Given that a user has entered the required issue details and selected a category for their request, when the request is submitted, a new ticket should be generated containing the information from the request and a success confirmation message should be displayed.

### 3.2 Non-Functional Requirements

Non-functional requirements will use the following identification structure:

`NFR-XX`

These tickets will be used to track the non-functional requirements of CivicConnect.

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

The initial Milestone 1 RTM establishes the foundation for this traceability. Additional evidence will be added during later milestones as architecture, implementation, testing and deployment activities are completed.

| Source / Stakeholder | Requirement ID | Description | Design / Architecture | Issue / PR | Test | Release Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| Minimum Business Capabilities | FR-01 | Submit and categorise requests | Milestone 2 | Milestone 3 | Milestone 3 | Milestone 4 |
| Security Engineering Standard | NFR-01 | Protect system secrets | Milestone 2 | Milestone 3 | Milestone 3 | Milestone 4 |

---

## 5. Risk and Forward Engineering Considerations

### 5.1 Risk Management Approach

Project risks will be tracked in an easy-to-manage Risk Register.

Each risk will receive a unique identifier and will record the risk description, cause, probability, impact, priority, mitigation approach, contingency action and current status.

The purpose of the register is to ensure that risks are identified clearly and that the team documents how each risk can be reduced or managed.

### 5.2 Initial Risk Register

| ID | Description | Cause | Probability | Impact | Priority | Mitigation | Contingency | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| RSK-01 | Platform limitations may restrict deployment and cause unexpected downtime. | Unanticipated operational costs or exceeding free-tier resource limitations. | Medium | High | High | Research and document resource limits for Docker containerisation deployments early. | Migrate the chosen backend to an alternative low-cost hosting provider. | Open |

### 5.3 Forward Engineering Considerations

Similar to how the team will use controlled tickets to track system functionality, forward engineering considerations will be documented so that important later-lifecycle concerns can be identified and tracked before final technical decisions are made.

**FEC-01 — Database Persistence and Data Design**

- **Consideration:** The program should be able to handle controlled transitions for request statuses while safely storing user data.
- **Deferment Plan:** The final decision between using a NoSQL database and a traditional relational SQL database is deliberately deferred until the data structures and API contracts are fully designed in Milestone 2.

**FEC-02 — Operational Readiness**

- **Consideration:** CivicConnect requires logging, monitoring, health checks and failure detection before staging and launching to production.
- **Deferment Rationale:** Specific observability tools are not being configured for Milestone 1 because the exact telemetry stack will only be selected once the application architecture has been finalised.

---

## 6. GitHub and Team Governance

GitHub is used as a controlled software engineering environment for the CivicConnect project, not just as a place to store the source code. The repository is designed to maintain project history, manage changes, facilitate peer reviews, provide personal contributions, and achieve traceability of project tasks, requirements, documentation and subsequent implementation. This strategy utilizes the mandatory GitHub governance and configuration management controls required by the project brief of the SEN381 project (SEN381 Teaching Team, 2026a). Within the current scope, the CivicConnect system has been configured to function as a configurable solution for managing internal service requests in office-based institutions. This system can be applied to varied office-based environments by configuring the system using varied request types, office sections, and access control.

Three long-lasting branches are used in the project — development, staging and main — to differentiate the working, verification and project state of the product. The feature and documentation development is done using the development branch, followed by review and merge into staging, and if the change is approved for the product state, it is further merged into the main branch. The integration of GitHub and ClickUp can be explored at a later stage as a way to improve project management; however, it is not considered an implemented M1 control.

### 6.1 Repository Purpose and Current Structure

The controlled team repository is SEN381 (`f-jooste/SEN381`). When this M1 review was conducted, the repository consisted of the three branches agreed upon: `main`, `staging`, and `dev`. The `main` branch is the controlled project/product branch, `staging` is the validation branch before the main one, and `dev` is the integration branch that gives rise to features/documentation branches.

| Branch | Purpose | Typical changes | Promotion rule |
| --- | --- | --- | --- |
| `dev` | Shared integration branch for active team development and controlled documentation work. | Feature work, documentation updates and integration of reviewed work. | Work should arrive through focused branches/PRs where substantive; completed integration is promoted to staging. |
| `staging` | Controlled pre-main environment for integrated verification before release/promotion. | Combined changes that require controlled validation before main. | Only changes that have been integrated and reviewed should progress toward main. |
| `main` | Controlled product/project state and the branch associated with the release-ready baseline. | Approved project state only. | Substantive changes enter through Pull Requests and must satisfy the required review/approval model. |

### 6.2 Branching and Change Workflow

> Feature / documentation branch → `dev` → `staging` → `main`

1. A valuable ClickUp task/GitHub Issue represents the work to be performed.
2. The contributor checks out a narrow scope branch from the corresponding development branch, which is usually `dev`.
3. The contributor performs progressive and meaningful commits, explaining the engineering change.
4. A Pull Request is created for a meaningful change instead of direct merge into the controlled branch.
5. The change is reviewed based on its compliance with requirements, correctness, maintainability, security, test impact, and documentation impact where applicable.
6. The contributor addresses the review comments and fixes the change, if needed.
7. The approval by other parties is obtained prior to merge.
8. The changes are merged through `staging` to `main` only if the respective controls are met.

### 6.3 Pull Requests and Peer Review

In relation to the SEN381 Master Project Brief, Pull Requests will be needed for any substantive changes committed into master and there should be at least two approvals from other team members, excluding the author. Self-approval is prohibited and the approval has to reflect a genuine review and not a rubber-stamp action (SEN381 Teaching Team, 2026a). Changes to documentation in M1 follow the same principle of governance as future changes to source code.

| Review focus | Examples of reviewer questions |
| --- | --- |
| Requirements and acceptance criteria | Does the change still match the approved requirement and acceptance criteria? |
| Correctness and consistency | Does the change behave or read as intended, and is it consistent with the controlled project artefacts? |
| Maintainability / technical debt | Does the change create unnecessary complexity, duplication or future maintenance cost? |
| Security / privacy | Could the change expose credentials, sensitive information or weaken access controls? |
| Testing / regression | What should be verified now or later, and could the change affect existing behaviour? |
| Documentation / traceability | Do the PED, RTM, issue/task, decision or risk records also need to be updated? |

**Preferred review evidence path:** Review comment → Author response → Correction → Re-review → Approval → Merge.

### 6.4 Current M1 Governance Status and Compliance Gap

The repository live review on 7 September 2026 revealed that `main`, `staging`, and `dev` branches have been created and that a live repository ruleset is being used with Pull Requests control on default and staging. However, the current ruleset calls for only one approving review. This is still below the SEN381 standard, which requires two approving reviews from team members other than the author. It is the responsibility of the repository owner to change the number of approving reviews to two prior to M1 submission.

| Control | Current evidence (7 Sep 2026) | M1 action |
| --- | --- | --- |
| Team repository | Repository `f-jooste/SEN381` exists. | Retain as the single controlled repository unless a different structure is formally justified. |
| Branches | `main`, `staging` and `dev` exist. | Use the agreed flow consistently and preserve progressive history. |
| Protected controlled branches | Active ruleset applies to the default branch and staging. | Demonstrate the live rule/settings during M1 if requested. |
| Pull Request requirement | Ruleset requires PR-based changes. | Use real PRs for substantive M1 documentation/governance changes. |
| Required approvals | Current configuration requires 1 approval. | Change to 2 non-author approvals to comply with the project brief. |
| PR / Issue history | No Pull Requests or Issues were visible at the time of review. | Create authentic evidence as real M1 work progresses; do not reconstruct activity immediately before assessment. |

### 6.5 Task Traceability and ClickUp Relationship

ClickUp remains the team's primary progress-management environment for task planning, assignment and project tracking. GitHub provides the controlled repository evidence for changes, commits, branches, Pull Requests and peer review. Where practical, a substantive repository change should reference the related ClickUp task so that planning evidence and repository evidence remain connected rather than existing as two unrelated systems. Any suggestion for a change that would move CivicConnect outside of these well-defined configuration boundaries will need to be regarded as a scope change and handled via formal impact assessment and baseline change control procedures.

**Recommended traceability path:** ClickUp task → GitHub Issue (where useful) → branch → commit(s) → Pull Request → review/approval → merge.

An automated GitHub–ClickUp integration for notifications or tracking may be investigated as a later project improvement. Until it is actually implemented and verified, it should be recorded as a planned enhancement rather than claimed as an existing M1 control.

### 6.6 Repository Security and Secrets

Secrets such as passwords, API keys, tokens, private keys, and any other type of secret information are prohibited from being checked into the repository. Environmental configurations and secrets will be handled in a secure way via approved configuration techniques and not by hard coding or checking in secret files into the repository.

### 6.7 Progressive Evidence and Individual Accountability

The first milestone explicitly mandates that the GitHub evidence should be generated progressively. As such, the team should generate meaningful issues/tasks, branches, commits, Pull Requests, and reviews from M1 onward to document any changes in documentation and engineering artifacts regardless of whether actual application coding is done (SEN381 Teaching Team, 2026b). Such bulk generation or reconstructed activity right before the assessment does not demonstrate a controlled engineering process.

Each individual member should build a valid history of contribution through meaningful commits, issue or task ownership, authorship of Pull Requests, meaningful review of Pull Requests, and contribution to controlled documentation or decisions. It is imperative that all three members should have an understanding of the entire project foundation and the reason for control in the repository.

### 6.8 M1 Evidence to Present

- The live SEN381 repository and the `main`/`dev`/`staging` branch structure.
- The live branch/ruleset configuration showing the protected controlled branch and required PR workflow.
- Evidence that the approving-review count has been corrected to two.
- At least one authentic substantive M1 Pull Request with two non-author approvals.
- Meaningful review evidence, including comments and author response/rework where genuine review issues arose.
- A meaningful issue/task and its connection to the relevant ClickUp task or PED/requirement artefact where practical.
- Progressive commit history showing that controlled documentation evolved during M1 rather than being uploaded only at the end.
- Individual contribution evidence for all three team members.

---

## 7. AI Usage

| Date | Student | Tool | Engineering task | AI contribution | Verification | Decision | Issues found |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 06/09/2026 | Shaun | Gemini AI | Research | The question: Are the risks that I have identified appropriate risks for CivicConnect | I conferred with my team if the risks that Gemini identified are risks we should consider | Did not accept this result | The risks were already planned for, or were not applicable to CivicConnect |
| 08/09/2026 | Fourie | Claude | Research | "What are significant stakeholders to be aware of in this scenario" with an upload of the Project Requirements Specification | Verified that project direction is aligned with what the rest of my team envisioned, verified that the project direction is applicable with my lecturer and went through Project Requirement Specification to verify that relevant stakeholders and their needs were being addressed as in section 3 of the document | Accepted some of the results | Originally the stakeholders were not applicable with CivicConnect and after prompt refinement some stakeholders were redundant and still not applicable. |
| 07 Sep 2026 | Bernard Small | ChatGPT | Develop and review CivicConnect Scope Baseline | Provided assistance in structuring the scope as In-Scope, Out-of-Scope and Deferred capabilities; scope limitations and assumptions; as well as the reason for the deliberate omission of WhatsApp integration from the scope. | Comparison of the proposed scope with the CivicConnect Master Project Brief and Milestone 1 requirements. Scope was evaluated paragraph by paragraph prior to its approval by the student. | Keep a controlled scope starting with the approved requester, staff and management abilities. Postponing other integrations and features without an official approval through change control. | Scope creep was seen from adding the option of extra features. WhatsApp integration was seen as the addition of unnecessary APIs, security, and authentication at this stage. |
| 09 Sep 2026 | Bernard Small | ChatGPT | Refine Scope Baseline for a reusable office ticket-management system | Provided assistance in restructuring the scope from a community-specific scope to an internal office ticket management system with configurable capabilities, exclusions, future scope and one more configurability limitation. | Updated scope was validated for meeting minimum business capabilities and project constraints. External references in scope were evaluated separately and the final language and boundaries were evaluated by the student. | CivicConnect would ensure reusability in different offices by means of controlled configuration of categories of requests, office locations and abilities, rather than customizing the system for each individual office. | The use of the phrase "any office" implied a generic no-code system or enterprise multi-tenant software. The scope was thus restricted to only configuration and not open workflow or form creation. |
| 07 Sep 2026 | Bernard Small | ChatGPT | Analyse and document GitHub and team governance for Milestone 1 | Provided assistance in understanding the GitHub governance requirements and drafting a description of the repository purpose, branch ownership, Pull Request control, peer review, ClickUp traceability, repository security and evidence progression requirements. | Comparisons were made between governance recommendations and the SEN381 Master Project Brief and Milestone 1 requirements. The resulting governance plan and repository control plan were evaluated by the student. | GitHub is the controlled environment for engineering and evidence while ClickUp is the environment for task management. Any substantive changes should be controlled by branches, Pull Requests and peer reviews. | The governance review confirmed that there were two non-author approvals necessary according to the project brief. The need for proper progressive PR, issues, review, and commit evidence was also identified for this milestone. |
| 09 Sep 2026 | Bernard Small | ChatGPT | Align GitHub governance documentation with the updated CivicConnect scope | Provided assistance in determining which sections of the GitHub governance document were impacted by the modified scope and making changes only to the sections relevant to the scope modifications. | Updated wording for GitHub was compared with the latest Scope Baseline. The changes were approved by the student and the highlighting used for the comparison was removed. | The new governance of GitHub is defined as CivicConnect being a configurable office-based service request system. Changes going beyond the approved configuration boundary are scope changes. | There was a discrepancy in the previous GitHub document in relation to the new configurable-office direction. However, it was sorted out without altering the existing governance process. |

---

## 8. PED v1.0 Baseline Sign-Off

| Baseline review item | Team record / decision |
| --- | --- |
| Project | CivicConnect |
| Baseline Type | Milestone 1 Engineering Baseline |
| Version | 1.0 |
| Date | |
| Scope reviewed | YES / NO |
| Requirements/traceability checked | YES / NO |
| Risk review completed | YES / NO |
| Repository/governance controls checked | YES / NO |
| Outcome | ACCEPTED / CONDITIONALLY ACCEPTED / REVISION REQUIRED |

---

## References

Atlassian. n.d. *What are request types?* Jira Service Management Cloud. Available at: <https://support.atlassian.com/jira-service-management-cloud/docs/what-are-request-types-in-a-service-project/> (Accessed: 9 September 2026).

SEN381 Teaching Team. 2026a. *SEN381 Software Engineering 381: CivicConnect Master Project Brief — Integrated Team Software Engineering Project.* Version 1.1. Belgium Campus ITversity.

SEN381 Teaching Team. 2026b. *CivicConnect Project: Milestone 1 — Engineering Foundation & Requirements Baseline.* Belgium Campus ITversity.

Zendesk. 2026. *Creating multiple ticket forms.* Zendesk Help, edited 1 May 2026. Available at: <https://support.zendesk.com/hc/en-us/articles/4408846520858-Creating-multiple-ticket-forms> (Accessed: 9 September 2026).

---

## PED Review Notes

*(No content in source document.)*
