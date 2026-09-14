# CivicConnect Documentation & Requirement Portal (SEN381)

A custom web portal and automation engine for managing requirements, specifications, risks, assumptions, and traceability tables for the **SEN381 CivicConnect** project.

---

## Features

- **Workspace Auto-Discovery**: Automatically inspects `.html` files in `SEN381/docs/` to discover table column schemas, row styling, and current numbering.
- **Dynamic Web Form**: Generates tailored input fields strictly conforming to each table's schema (Functional Requirements, Non-Functional Requirements, Assumptions, Risk Register, Stakeholder Register, Conflicts, RTM, Scope Constraints, and AI Usage).
- **Auto-Sequenced ID Determination**: Automatically scans existing entries and calculates the next zero-padded ID (e.g., `FR-01` &rarr; `FR-02`, `RSK-01` &rarr; `RSK-02`, `ASM-06` &rarr; `ASM-07`).
- **Indentation-Preserving File Engine**: Safely inserts newly generated `<tr>` rows directly into the target `.html` file's `<tbody>` while preserving exact tabs/spaces, HTML entities, and formatting without corrupting documents.
- **Dark/Light Mode Aesthetic**: Matches the exact color palette (`#16161a`, `#1f1f24`, `#d9a07a`), typography, and styles of the CivicConnect documentation.
- **Dual Operating Modes**:
  1. **Full-Stack Mode**: Running via the lightweight built-in Node runner (`npm start`) writes directly to files on disk and optionally creates GitHub tracking issues.
  2. **Standalone Browser Mode**: Can also run purely in the browser with the interactive "Preview & Copy HTML" drawer.
- **Optional GitHub Issue Integration**: Integrates with the GitHub CLI (`gh issue create`) to file linked tracking issues automatically.

---

## Discovered Schemas & Target Files

| Category | Prefix | Next ID | Target Document File | Columns |
| :--- | :---: | :---: | :--- | :--- |
| **Functional Requirements** | `FR-` | `FR-02` | `docs/requirements/Functional Requirements/functional-requirements-V1.0.html` | ID, Requirement, Source, Statement, Acceptance Criteria |
| **Non-Functional Requirements** | `NFR-` | `NFR-02` | `docs/requirements/Non-Functional Requirements/non-functional-requirements-V1.0.html` | ID, Requirement, Source, Statement, Acceptance Criteria |
| **Assumptions** | `ASM-` | `ASM-07` | `docs/assumptions/assumptions-V1.0.html` | Assumption ID, Assumption, Potential Impact if Incorrect |
| **Risk Register** | `RSK-` | `RSK-02` | `docs/risk/Risk Register.html` | ID, Description, Cause, Probability, Impact, Priority, Mitigation, Contingency, Status |
| **Stakeholder Register** | `STK-` | `STK-08` | `docs/requirements/Stakeholder/register/stakeholder-register-V1.0.html` | ID, Stakeholder, Role / Interest in the System, Influence, Interest |
| **Stakeholder Conflicts** | `STK-CFL-` | `STK-CFL-05` | `docs/requirements/Stakeholder/conflicts/stakeholder-conflicts-V1.0.html` | ID, Conflict, Parties, Resolution |
| **Traceability Matrix (RTM)** | `—` | Manual | `docs/requirements/Requirements Traceability Matrix/requirements-traceability-matrix-V1.0.html` | Source, Requirement ID, Description, Architecture, Issue/PR, Test, Release |
| **AI Usage Register** | `DATE` | Today | `docs/AI Useage/Useage Register/AI Usage.html` | Date, Student, Tool, Engineering task, AI contribution, Verification, Decision, Issues |
| **Scope Constraints** | `—` | Manual | `docs/requirements/scope/constraints/scope-constraints-V1.0.html` | Constraint, Scope Implication |

---

## Quick Start Guide

### 1. Launch the Local Portal

From the repository root or inside `tools/doc-portal/`, run:

```bash
# From workspace root:
npm --prefix tools/doc-portal start

# Or directly with node:
node tools/doc-portal/server.js
```

The portal will launch on **http://localhost:3381** (zero external dependencies required!).

### 2. Enter Requirement / Ticket Data
1. Select the specification category from the dropdown (e.g. **Functional Requirements**).
2. The portal will automatically display the computed **Next Auto ID** (e.g. `FR-02`) and load the live table preview of existing entries.
3. Fill in the form fields (e.g., Requirement Name, Source, Statement, Acceptance Criteria).
4. Optionally provide a **Ticket Reference** (e.g., `#15` or `CIVIC-15`).
5. Click **⚡ Produce & Save to Document**.

### 3. Verify the Update
- The new row is immediately appended to the target `.html` file inside `SEN381/docs/`.
- The Next Auto ID instantly increments for the subsequent entry (e.g. `FR-03`).
- You can inspect the updated document directly in your browser or code editor.
