/**
 * Schema definitions and metadata for SEN381 documentation tables.
 * Discovered from SEN381/docs/ specifications.
 */
export const SCHEMAS = {
  functional_requirements: {
    id: "functional_requirements",
    title: "Functional Requirements",
    badge: "FR",
    docFile: "requirements/Functional Requirements/functional-requirements-V1.0.html",
    prefix: "FR-",
    padLength: 2,
    idField: "id",
    description: "System functional capabilities, user interactions, and formal acceptance criteria.",
    fields: [
      {
        key: "id",
        label: "Requirement ID",
        type: "text",
        readOnly: true,
        auto: true,
        placeholder: "FR-02",
        hint: "Automatically sequenced from the highest existing ID."
      },
      {
        key: "requirement",
        label: "Requirement Name",
        type: "text",
        required: true,
        placeholder: "e.g., Export Incident Reports to PDF",
        hint: "Short, action-oriented title of the requirement."
      },
      {
        key: "source",
        label: "Source",
        type: "combobox",
        required: true,
        placeholder: "Select or enter source...",
        options: [
          "Requested Capabilities",
          "Minimum Business Capabilities",
          "Stakeholder Need",
          "CivicConnect Master Project Brief",
          "Regulatory Requirement"
        ]
      },
      {
        key: "statement",
        label: "Statement",
        type: "textarea",
        required: true,
        rows: 3,
        placeholder: "The system will allow authorised users to...",
        hint: "Precise specification statement describing what the system shall do."
      },
      {
        key: "acceptanceCriteria",
        label: "Acceptance Criteria",
        type: "textarea",
        required: true,
        rows: 4,
        placeholder: "Given..., when..., then...",
        hint: "Gherkin-style BDD criteria (Given/When/Then) for verifiable completion."
      }
    ],
    renderRow(data) {
      return `<tr>\n  <td class="id">${escapeHtml(data.id)}</td>\n  <td class="name">${escapeHtml(data.requirement)}</td>\n  <td>${escapeHtml(data.source)}</td>\n  <td>${escapeHtml(data.statement)}</td>\n  <td>${escapeHtml(data.acceptanceCriteria)}</td>\n</tr>`;
    }
  },

  non_functional_requirements: {
    id: "non_functional_requirements",
    title: "Non-Functional Requirements",
    badge: "NFR",
    docFile: "requirements/Non-Functional Requirements/non-functional-requirements-V1.0.html",
    prefix: "NFR-",
    padLength: 2,
    idField: "id",
    description: "Architectural qualities, security standards, performance bounds, and system constraints.",
    fields: [
      {
        key: "id",
        label: "Requirement ID",
        type: "text",
        readOnly: true,
        auto: true,
        placeholder: "NFR-02",
        hint: "Automatically sequenced from the highest existing ID."
      },
      {
        key: "requirement",
        label: "Requirement Name",
        type: "text",
        required: true,
        placeholder: "e.g., Protection of System Secrets",
        hint: "Quality attribute or constraint title."
      },
      {
        key: "source",
        label: "Source",
        type: "combobox",
        required: true,
        placeholder: "Select or enter source...",
        options: [
          "Security Constraints",
          "Security Engineering Standard",
          "Performance Standards",
          "Architecture Baseline",
          "Availability Requirements"
        ]
      },
      {
        key: "statement",
        label: "Statement",
        type: "textarea",
        required: true,
        rows: 3,
        placeholder: "The system will ensure that...",
        hint: "Measurable architectural constraint or quality benchmark."
      },
      {
        key: "acceptanceCriteria",
        label: "Acceptance Criteria",
        type: "textarea",
        required: true,
        rows: 4,
        placeholder: "Given..., when..., then...",
        hint: "Verification criteria including specific quantifiable metrics."
      }
    ],
    renderRow(data) {
      return `<tr>\n  <td class="id">${escapeHtml(data.id)}</td>\n  <td class="name">${escapeHtml(data.requirement)}</td>\n  <td>${escapeHtml(data.source)}</td>\n  <td>${escapeHtml(data.statement)}</td>\n  <td>${escapeHtml(data.acceptanceCriteria)}</td>\n</tr>`;
    }
  },

  assumptions: {
    id: "assumptions",
    title: "Assumptions",
    badge: "ASM",
    docFile: "assumptions/assumptions-V1.0.html",
    prefix: "ASM-",
    padLength: 2,
    idField: "id",
    description: "Project and technical working assumptions and impact evaluation if falsified.",
    fields: [
      {
        key: "id",
        label: "Assumption ID",
        type: "text",
        readOnly: true,
        auto: true,
        placeholder: "ASM-07",
        hint: "Automatically sequenced from highest existing ASM ID."
      },
      {
        key: "assumption",
        label: "Assumption",
        type: "textarea",
        required: true,
        rows: 3,
        placeholder: "State the working assumption clearly..."
      },
      {
        key: "impact",
        label: "Potential Impact if Incorrect",
        type: "textarea",
        required: true,
        rows: 3,
        placeholder: "Risk or delivery consequences if this assumption is invalid..."
      }
    ],
    renderRow(data) {
      return `<tr>\n  <td class="id">${escapeHtml(data.id)}</td>\n  <td>${escapeHtml(data.assumption)}</td>\n  <td>${escapeHtml(data.impact)}</td>\n</tr>`;
    }
  },

  risk_register: {
    id: "risk_register",
    title: "Risk Register",
    badge: "RSK",
    docFile: "risk/Risk Register.html",
    prefix: "RSK-",
    padLength: 2,
    idField: "id",
    description: "Operational, technical, and scheduling risks with rating matrix and contingencies.",
    fields: [
      {
        key: "id",
        label: "Risk ID",
        type: "text",
        readOnly: true,
        auto: true,
        placeholder: "RSK-02"
      },
      {
        key: "description",
        label: "Description",
        type: "textarea",
        required: true,
        rows: 2,
        placeholder: "Describe the specific event or condition that may occur..."
      },
      {
        key: "cause",
        label: "Root Cause",
        type: "textarea",
        required: true,
        rows: 2,
        placeholder: "Underlying vulnerabilities, triggers, or circumstances..."
      },
      {
        key: "probability",
        label: "Probability",
        type: "select",
        required: true,
        options: ["Low", "Medium", "High", "Critical"],
        default: "Medium"
      },
      {
        key: "impact",
        label: "Impact",
        type: "select",
        required: true,
        options: ["Low", "Medium", "High", "Critical"],
        default: "High"
      },
      {
        key: "priority",
        label: "Priority",
        type: "select",
        required: true,
        options: ["Low", "Medium", "High", "Critical"],
        default: "High",
        hint: "Calculated from probability and impact severity."
      },
      {
        key: "mitigation",
        label: "Mitigation",
        type: "textarea",
        required: true,
        rows: 2,
        placeholder: "Preventative actions to reduce likelihood or exposure..."
      },
      {
        key: "contingency",
        label: "Contingency",
        type: "textarea",
        required: true,
        rows: 2,
        placeholder: "Response plan if the risk materialises..."
      },
      {
        key: "status",
        label: "Status",
        type: "select",
        required: true,
        options: ["Open", "In Progress", "Mitigated", "Closed"],
        default: "Open"
      }
    ],
    renderRow(data) {
      return `<tr>\n  <td class="name">${escapeHtml(data.id)}</td>\n  <td>${escapeHtml(data.description)}</td>\n  <td>${escapeHtml(data.cause)}</td>\n  <td class="rating">${escapeHtml(data.probability)}</td>\n  <td class="rating">${escapeHtml(data.impact)}</td>\n  <td class="rating">${escapeHtml(data.priority)}</td>\n  <td>${escapeHtml(data.mitigation)}</td>\n  <td>${escapeHtml(data.contingency)}</td>\n  <td class="rating">${escapeHtml(data.status)}</td>\n</tr>`;
    }
  },

  stakeholder_register: {
    id: "stakeholder_register",
    title: "Stakeholder Register",
    badge: "STK",
    docFile: "requirements/Stakeholder/register/stakeholder-register-V1.0.html",
    prefix: "STK-",
    padLength: 2,
    idField: "id",
    description: "Identified stakeholders, personas, system responsibilities, and power-interest ratings.",
    fields: [
      {
        key: "id",
        label: "Stakeholder ID",
        type: "text",
        readOnly: true,
        auto: true,
        placeholder: "STK-08"
      },
      {
        key: "stakeholder",
        label: "Stakeholder / Persona",
        type: "text",
        required: true,
        placeholder: "e.g., System Administrator, Municipal Field Officer"
      },
      {
        key: "role",
        label: "Role / Interest in the System",
        type: "textarea",
        required: true,
        rows: 3,
        placeholder: "Primary tasks, responsibilities, and key expectations..."
      },
      {
        key: "influence",
        label: "Influence",
        type: "select",
        required: true,
        options: ["High", "Medium", "Low"],
        default: "Medium"
      },
      {
        key: "interest",
        label: "Interest",
        type: "select",
        required: true,
        options: ["High", "Medium", "Low"],
        default: "High"
      }
    ],
    renderRow(data) {
      return `<tr>\n  <td class="id">${escapeHtml(data.id)}</td>\n  <td class="name">${escapeHtml(data.stakeholder)}</td>\n  <td>${escapeHtml(data.role)}</td>\n  <td>${escapeHtml(data.influence)}</td>\n  <td>${escapeHtml(data.interest)}</td>\n</tr>`;
    }
  },

  stakeholder_conflicts: {
    id: "stakeholder_conflicts",
    title: "Stakeholder Conflicts",
    badge: "STK-CFL",
    docFile: "requirements/Stakeholder/conflicts/stakeholder-conflicts-V1.0.html",
    prefix: "STK-CFL-",
    padLength: 2,
    idField: "id",
    description: "Documented conflicts between stakeholder groups and their approved resolutions.",
    fields: [
      {
        key: "id",
        label: "Conflict ID",
        type: "text",
        readOnly: true,
        auto: true,
        placeholder: "STK-CFL-05"
      },
      {
        key: "conflict",
        label: "Conflict Summary",
        type: "text",
        required: true,
        placeholder: "e.g., Self-declared urgency vs. objective severity rating"
      },
      {
        key: "parties",
        label: "Parties Involved",
        type: "text",
        required: true,
        placeholder: "e.g., Incident reporters (STK-01) vs. technicians (STK-02)"
      },
      {
        key: "resolution",
        label: "Resolution Mechanism",
        type: "textarea",
        required: true,
        rows: 3,
        placeholder: "Agreed system rule or governance procedure reconciling the conflict..."
      }
    ],
    renderRow(data) {
      return `<tr>\n  <td class="id">${escapeHtml(data.id)}</td>\n  <td class="name">${escapeHtml(data.conflict)}</td>\n  <td>${escapeHtml(data.parties)}</td>\n  <td>${escapeHtml(data.resolution)}</td>\n</tr>`;
    }
  },

  rtm: {
    id: "rtm",
    title: "Traceability Matrix (RTM)",
    badge: "RTM",
    docFile: "requirements/Requirements Traceability Matrix/requirements-traceability-matrix-V1.0.html",
    prefix: "",
    padLength: 0,
    description: "End-to-end traceability mapping requirements to architectural artifacts, tests, and milestones.",
    fields: [
      {
        key: "source",
        label: "Source / Stakeholder",
        type: "text",
        required: true,
        placeholder: "e.g., Minimum Business Capabilities"
      },
      {
        key: "reqId",
        label: "Requirement ID",
        type: "text",
        required: true,
        placeholder: "e.g., FR-02 or NFR-02"
      },
      {
        key: "description",
        label: "Description",
        type: "text",
        required: true,
        placeholder: "Concise requirement summary"
      },
      {
        key: "architecture",
        label: "Design / Architecture Target",
        type: "text",
        required: true,
        default: "Milestone 2",
        placeholder: "e.g., Milestone 2"
      },
      {
        key: "issue",
        label: "Issue / PR",
        type: "text",
        required: true,
        default: "Milestone 3",
        placeholder: "e.g., Milestone 3 or #42"
      },
      {
        key: "test",
        label: "Test Verification",
        type: "text",
        required: true,
        default: "Milestone 3",
        placeholder: "e.g., Milestone 3"
      },
      {
        key: "release",
        label: "Release Evidence",
        type: "text",
        required: true,
        default: "Milestone 4",
        placeholder: "e.g., Milestone 4"
      }
    ],
    renderRow(data) {
      return `<tr>\n  <td class="name">${escapeHtml(data.source)}</td>\n  <td class="id">${escapeHtml(data.reqId)}</td>\n  <td>${escapeHtml(data.description)}</td>\n  <td class="todo">${escapeHtml(data.architecture)}</td>\n  <td class="todo">${escapeHtml(data.issue)}</td>\n  <td class="todo">${escapeHtml(data.test)}</td>\n  <td class="todo">${escapeHtml(data.release)}</td>\n</tr>`;
    }
  },

  ai_usage: {
    id: "ai_usage",
    title: "AI Usage Register",
    badge: "AI-USE",
    docFile: "AI Useage/Useage Register/AI Usage.html",
    prefix: "DATE",
    description: "Academic AI governance register recording prompts, verification, and engineering decisions.",
    fields: [
      {
        key: "date",
        label: "Date",
        type: "text",
        required: true,
        auto: true,
        placeholder: "DD/MM/YYYY",
        hint: "Defaults to current local date."
      },
      {
        key: "student",
        label: "Student Name",
        type: "combobox",
        required: true,
        options: ["Shaun", "Fourie", "Bernard Small"],
        placeholder: "Select or enter student..."
      },
      {
        key: "tool",
        label: "AI Tool",
        type: "combobox",
        required: true,
        options: ["Gemini", "Claude", "ChatGPT", "GitHub Copilot", "Antigravity"],
        placeholder: "e.g., Gemini"
      },
      {
        key: "task",
        label: "Engineering Task",
        type: "text",
        required: true,
        placeholder: "e.g., Form schema design & file update engine"
      },
      {
        key: "contribution",
        label: "AI Contribution",
        type: "textarea",
        required: true,
        rows: 3,
        placeholder: "Specific prompts used and output received..."
      },
      {
        key: "verification",
        label: "Verification Method",
        type: "textarea",
        required: true,
        rows: 3,
        placeholder: "How the student verified and validated the generated result..."
      },
      {
        key: "decision",
        label: "Decision",
        type: "textarea",
        required: true,
        rows: 2,
        placeholder: "e.g., Accepted result with modifications..."
      },
      {
        key: "issues",
        label: "Issues Found",
        type: "textarea",
        required: true,
        rows: 2,
        placeholder: "Discrepancies, scope mismatches, or refactoring required..."
      }
    ],
    renderRow(data) {
      return `<tr>\n  <td class="name">${escapeHtml(data.date)}</td>\n  <td class="name">${escapeHtml(data.student)}</td>\n  <td>${escapeHtml(data.tool)}</td>\n  <td>${escapeHtml(data.task)}</td>\n  <td>${escapeHtml(data.contribution)}</td>\n  <td>${escapeHtml(data.verification)}</td>\n  <td>${escapeHtml(data.decision)}</td>\n  <td>${escapeHtml(data.issues)}</td>\n</tr>`;
    }
  },

  scope_constraints: {
    id: "scope_constraints",
    title: "Scope Constraints",
    badge: "CON",
    docFile: "requirements/scope/constraints/scope-constraints-V1.0.html",
    prefix: "",
    description: "Binding engineering constraints limiting project and delivery scope.",
    fields: [
      {
        key: "constraint",
        label: "Constraint Name",
        type: "text",
        required: true,
        placeholder: "e.g., Team Size, Deployment Budget, Milestone Schedule"
      },
      {
        key: "implication",
        label: "Scope Implication",
        type: "textarea",
        required: true,
        rows: 4,
        placeholder: "Detailed explanation of how this constraint shapes system engineering..."
      }
    ],
    renderRow(data) {
      return `<tr>\n  <td class="name">${escapeHtml(data.constraint)}</td>\n  <td>${escapeHtml(data.implication)}</td>\n</tr>`;
    }
  }
};

/**
 * Basic HTML escaping utility
 */
export function escapeHtml(str) {
  if (str === undefined || str === null) return "";
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}
