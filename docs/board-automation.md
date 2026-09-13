# Project board automation

The board (**Todo → In Progress → Review → Testing → Done**) is kept in sync by
`.github/workflows/project-board-sync.yml`, which calls `board-sync-reusable.yml`,
which runs `.github/scripts/board-sync.js`.

## What the team has to do

- Move a card from **Todo** to **In Progress** by hand when you start work.
- Put `Closes #123` (or `Fixes` / `Resolves`) in the PR description, **or** link the
  issue through the PR's **Development** sidebar. Both work — the sidebar route just
  takes up to 15 minutes to show on the board, because GitHub fires no webhook for it.
- Label issues correctly. Labels decide whether merged work gets a QA card.

Everything else moves on its own.

## What the automation does

| Event | Board effect |
|---|---|
| Issue opened | added to **Todo** |
| PR opened / linked to an issue | linked issue cards are **archived** off the board; PR card goes to **Review** |
| PR merged | PR card to **Done**; a `Test` issue is opened in **Testing** if QA is needed |
| PR closed without merging | linked issues return to **In Progress**; PR card archived |

### When is a QA card created?

Based on the labels of the issues the PR closes:

| Linked issue labels | Test issue |
|---|---|
| any issue labelled `Implementation` | yes |
| all issues labelled `documentation` and/or `task` | no |
| unlabelled or other labels | yes (safe default) |

The `Test` issue is assigned to a **reviewer of the PR** — the earliest approver,
never the PR author, and never anyone in the `EXCLUDED_ASSIGNEES` variable. If no
eligible reviewer exists it is created unassigned and says so in the body.

Archived cards are not deleted. They're under the project's **Archived items** view
and can be restored.

## Setup (one time)

1. **Turn off the project's built-in "auto-add item" workflow.** The `issue-opened`
   job replaces it; leaving both on races the Test-issue placement and drops those
   cards in Todo.

2. **Create a token.** `GITHUB_TOKEN` cannot touch Projects v2.
   - Classic PAT with `repo` + `project`, or
   - Fine-grained PAT with org **Projects: Read and write** plus repo **Issues: Read
     and write** and **Pull requests: Read**.

   ```
   gh secret set PROJECT_TOKEN
   ```

3. **Point the workflow at the board.**

   ```
   gh variable set PROJECT_OWNER  --body "<org-login>"
   gh variable set PROJECT_NUMBER --body "<project-number>"
   gh variable set EXCLUDED_ASSIGNEES --body "mcom314"
   ```

   The project number is the last part of the project URL:
   `https://github.com/orgs/<org-login>/projects/<number>`.

4. **Check the column names resolve.** The script matches `Todo`, `In Progress`,
   `Review`, `Testing`, `Done` by name and fails loudly if one is renamed. Trigger a
   run with `gh workflow run "Project board sync"` and read the log.

## Changing the rules

| Change | Where |
|---|---|
| Column names | `COL` in `.github/scripts/board-sync.js` |
| Which labels skip QA | `DONE_ONLY_LABELS` / `IMPLEMENTATION_LABEL` in the same file |
| Who can't be assigned | `EXCLUDED_ASSIGNEES` repository variable |
| Reconcile frequency | the `schedule` cron in `project-board-sync.yml` |

## Troubleshooting

- **Nothing happened on a PR.** The PR has no closing reference. Add `Closes #N` or
  link it in the Development sidebar, then push a commit or run
  `gh workflow run "Project board sync" -f pr_number=<n>`.
- **Card went to the wrong column.** Check the run log — it prints the resolved
  project, the linked issues, and each item move.
- **Scheduled runs stopped.** GitHub disables scheduled workflows after 60 days of
  repository inactivity. Re-enable in the Actions tab.
