// Reconciles the GitHub Project board to the state the team's workflow expects.
// Modes: 'pr' (one pull request), 'issue-opened' (one new issue), 'reconcile' (every open PR).

const STATUS_FIELD = 'Status';
const COL = {
  TODO: 'Todo',
  IN_PROGRESS: 'In Progress',
  REVIEW: 'Review',
  TESTING: 'Testing',
  DONE: 'Done',
};
const TEST_LABEL = 'Test';
const DONE_ONLY_LABELS = ['documentation', 'task'];
const IMPLEMENTATION_LABEL = 'Implementation';

// Looks up the project node id and the Status field's option ids by name, failing loudly on a rename.
async function loadProject(github, core, owner, number) {
  const q = `
    query($login:String!, $num:Int!) {
      organization(login:$login) {
        projectV2(number:$num) {
          id
          title
          field(name:"${STATUS_FIELD}") {
            ... on ProjectV2SingleSelectField { id options { id name } }
          }
        }
      }
    }`;
  const res = await github.graphql(q, { login: owner, num: Number(number) });
  const project = res.organization && res.organization.projectV2;
  if (!project) throw new Error(`No project #${number} on org ${owner} (or PROJECT_TOKEN lacks project scope)`);
  if (!project.field) throw new Error(`Project "${project.title}" has no single-select field named "${STATUS_FIELD}"`);

  const options = {};
  for (const key of Object.keys(COL)) {
    const wanted = COL[key];
    const match = project.field.options.find((o) => o.name === wanted);
    if (!match) {
      const have = project.field.options.map((o) => o.name).join(', ');
      throw new Error(`Status option "${wanted}" not found. Board has: ${have}`);
    }
    options[wanted] = match.id;
  }
  core.info(`Project "${project.title}" (${project.id}) resolved; Status field ${project.field.id}`);
  return { id: project.id, fieldId: project.field.id, options };
}

// Returns the project item for a content node, adding it to the project if absent.
async function ensureItem(github, project, contentId, existingItems) {
  const existing = (existingItems || []).find((i) => i.project.id === project.id);
  if (existing) return existing;
  const res = await github.graphql(
    `mutation($projectId:ID!, $contentId:ID!) {
       addProjectV2ItemById(input:{projectId:$projectId, contentId:$contentId}) { item { id isArchived } }
     }`,
    { projectId: project.id, contentId }
  );
  return Object.assign({}, res.addProjectV2ItemById.item, { project: { id: project.id } });
}

// Moves an item into a named column, unarchiving it first if it is hidden.
async function setStatus(github, core, project, item, columnName) {
  if (item.isArchived) {
    await github.graphql(
      `mutation($projectId:ID!, $itemId:ID!) {
         unarchiveProjectV2Item(input:{projectId:$projectId, itemId:$itemId}) { item { id } }
       }`,
      { projectId: project.id, itemId: item.id }
    );
    item.isArchived = false;
  }
  await github.graphql(
    `mutation($projectId:ID!, $itemId:ID!, $fieldId:ID!, $optionId:String!) {
       updateProjectV2ItemFieldValue(input:{
         projectId:$projectId, itemId:$itemId, fieldId:$fieldId,
         value:{ singleSelectOptionId:$optionId }
       }) { projectV2Item { id } }
     }`,
    { projectId: project.id, itemId: item.id, fieldId: project.fieldId, optionId: project.options[columnName] }
  );
  core.info(`  item ${item.id} -> ${columnName}`);
}

// Hides an item from the board without losing its field values.
async function archive(github, core, project, item) {
  if (item.isArchived) return;
  await github.graphql(
    `mutation($projectId:ID!, $itemId:ID!) {
       archiveProjectV2Item(input:{projectId:$projectId, itemId:$itemId}) { item { id } }
     }`,
    { projectId: project.id, itemId: item.id }
  );
  item.isArchived = true;
  core.info(`  item ${item.id} archived`);
}

// Fetches everything the routing rules need about one PR in a single round trip.
async function loadPullRequest(github, owner, repo, number) {
  const q = `
    query($owner:String!, $repo:String!, $pr:Int!) {
      repository(owner:$owner, name:$repo) {
        pullRequest(number:$pr) {
          id number title state merged isDraft
          author { login }
          projectItems(first:20, includeArchived:true) { nodes { id isArchived project { id } } }
          closingIssuesReferences(first:20) {
            nodes {
              id number title
              labels(first:30) { nodes { name } }
              projectItems(first:20, includeArchived:true) { nodes { id isArchived project { id } } }
            }
          }
          reviews(first:100) { nodes { author { login } state submittedAt } }
        }
      }
    }`;
  const res = await github.graphql(q, { owner, repo, pr: Number(number) });
  return res.repository.pullRequest;
}

// Decides whether merged work still needs a QA pass, based on the linked issues' labels.
function needsTesting(linkedIssues) {
  const names = linkedIssues.reduce((acc, i) => acc.concat(i.labels.nodes.map((l) => l.name)), []);
  if (names.includes(IMPLEMENTATION_LABEL)) return true;
  const allDoneOnly = names.length > 0 && names.every((n) => DONE_ONLY_LABELS.includes(n));
  return !allDoneOnly;
}

// Picks the reviewer who should verify the work: earliest approver, never the author or an excluded account.
function pickAssignee(pr, excluded) {
  const blocked = new Set(
    excluded.concat(pr.author ? [pr.author.login] : []).filter(Boolean).map((s) => s.toLowerCase())
  );
  const reviews = pr.reviews.nodes
    .filter((r) => r.author && !blocked.has(r.author.login.toLowerCase()))
    .sort((a, b) => new Date(a.submittedAt) - new Date(b.submittedAt));
  const approved = reviews.find((r) => r.state === 'APPROVED');
  const chosen = approved || reviews[0];
  return chosen ? chosen.author.login : null;
}

// Guards against opening a second QA issue if the merged path runs twice for the same PR.
async function findExistingTestIssue(github, owner, repo, marker) {
  const issues = await github.paginate(github.rest.issues.listForRepo, {
    owner, repo, labels: TEST_LABEL, state: 'all', per_page: 100,
  });
  return issues.find((i) => !i.pull_request && (i.body || '').includes(marker));
}

// Opens the QA follow-up issue and drops it straight into Testing.
async function createTestIssue(github, core, project, owner, repo, pr, linkedIssues, excluded) {
  const marker = `<!-- board-sync:pr-${pr.number} -->`;
  const existing = await findExistingTestIssue(github, owner, repo, marker);
  if (existing) {
    core.info(`  test issue #${existing.number} already exists, skipping`);
    return;
  }

  const assignee = pickAssignee(pr, excluded);
  const sourceTitle = linkedIssues.length ? linkedIssues[0].title : pr.title;
  const refs = linkedIssues.map((i) => `#${i.number}`).join(', ') || '(none)';
  const body = [
    `Verify the work from ${refs}, merged in #${pr.number}.`,
    '',
    `- Source issue(s): ${refs}`,
    `- Pull request: #${pr.number}`,
    assignee
      ? `- Assigned to @${assignee}, who reviewed #${pr.number}.`
      : '- No eligible reviewer found; please pick this up.',
    '',
    marker,
  ].join('\n');

  const created = await github.rest.issues.create({
    owner, repo,
    title: `Test: ${sourceTitle}`,
    body,
    labels: [TEST_LABEL],
  });
  core.info(`  created test issue #${created.data.number}`);

  if (assignee) {
    try {
      await github.rest.issues.addAssignees({
        owner, repo, issue_number: created.data.number, assignees: [assignee],
      });
    } catch (e) {
      core.warning(`Could not assign ${assignee}: ${e.message}`);
    }
  }

  const item = await ensureItem(github, project, created.data.node_id, []);
  await setStatus(github, core, project, item, COL.TESTING);
}

// Applies the board rules for a single PR based on whether it is open, merged, or closed unmerged.
async function syncPullRequest(github, core, project, owner, repo, number, excluded) {
  const pr = await loadPullRequest(github, owner, repo, number);
  const linked = pr.closingIssuesReferences.nodes;
  core.info(`PR #${pr.number} state=${pr.state} merged=${pr.merged} linked=[${linked.map((i) => i.number).join(',')}]`);

  if (linked.length === 0) {
    core.info('  no closing issue references, leaving the board alone');
    return;
  }

  if (pr.state === 'OPEN') {
    for (const issue of linked) {
      const item = issue.projectItems.nodes.find((i) => i.project.id === project.id);
      if (item) await archive(github, core, project, item);
    }
    const prItem = await ensureItem(github, project, pr.id, pr.projectItems.nodes);
    await setStatus(github, core, project, prItem, COL.REVIEW);
    return;
  }

  if (pr.merged) {
    const prItem = await ensureItem(github, project, pr.id, pr.projectItems.nodes);
    await setStatus(github, core, project, prItem, COL.DONE);
    if (needsTesting(linked)) {
      await createTestIssue(github, core, project, owner, repo, pr, linked, excluded);
    } else {
      core.info('  documentation/task only, no test issue needed');
    }
    return;
  }

  // Closed without merging: put the work back where the team left it.
  for (const issue of linked) {
    const item = issue.projectItems.nodes.find((i) => i.project.id === project.id);
    if (item) await setStatus(github, core, project, item, COL.IN_PROGRESS);
  }
  const prItem = pr.projectItems.nodes.find((i) => i.project.id === project.id);
  if (prItem) await archive(github, core, project, prItem);
}

// Puts a newly opened issue in Todo, replacing the project's built-in auto-add rule.
async function syncNewIssue(github, core, project, owner, repo, number) {
  const res = await github.graphql(
    `query($owner:String!, $repo:String!, $num:Int!) {
       repository(owner:$owner, name:$repo) {
         issue(number:$num) {
           id
           labels(first:30) { nodes { name } }
           projectItems(first:20, includeArchived:true) { nodes { id isArchived project { id } } }
         }
       }
     }`,
    { owner, repo, num: Number(number) }
  );
  const issue = res.repository.issue;
  if (issue.labels.nodes.some((l) => l.name === TEST_LABEL)) {
    core.info(`Issue #${number} is a ${TEST_LABEL} issue, the merge job owns its column`);
    return;
  }
  const item = await ensureItem(github, project, issue.id, issue.projectItems.nodes);
  await setStatus(github, core, project, item, COL.TODO);
}

// Sweeps every open PR, catching links made through the Development sidebar (which fires no webhook).
async function reconcile(github, core, project, owner, repo, excluded) {
  const prs = await github.paginate(github.rest.pulls.list, { owner, repo, state: 'open', per_page: 100 });
  core.info(`Reconciling ${prs.length} open PR(s)`);
  for (const pr of prs) {
    try {
      await syncPullRequest(github, core, project, owner, repo, pr.number, excluded);
    } catch (e) {
      core.warning(`PR #${pr.number}: ${e.message}`);
    }
  }
}

module.exports = async ({ github, context, core }) => {
  const owner = context.repo.owner;
  const repo = context.repo.repo;
  const projectOwner = process.env.PROJECT_OWNER;
  const projectNumber = process.env.PROJECT_NUMBER;
  const mode = process.env.MODE;
  const excluded = (process.env.EXCLUDED_ASSIGNEES || '')
    .split(',').map((s) => s.trim()).filter(Boolean);

  if (!projectOwner || !projectNumber) {
    throw new Error('Set the PROJECT_OWNER and PROJECT_NUMBER repository variables (see docs/board-automation.md)');
  }

  const project = await loadProject(github, core, projectOwner, projectNumber);

  if (mode === 'reconcile') return reconcile(github, core, project, owner, repo, excluded);
  if (mode === 'issue-opened') return syncNewIssue(github, core, project, owner, repo, process.env.ISSUE_NUMBER);
  if (mode === 'pr') return syncPullRequest(github, core, project, owner, repo, process.env.PR_NUMBER, excluded);
  throw new Error(`Unknown MODE "${mode}"`);
};
