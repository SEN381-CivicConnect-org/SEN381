# Quality Gate — What Was Done

Scope: what in the CI pipeline (`docs/ci/ci-workflow-V1.0.md`) actually blocks progression
today, with real examples of it doing so, and an honest statement of what is not yet
enforced. This is the project's quality-gate evidence for SEN381 Milestone 3, Assessment
Area C ("at least one meaningful failure must block progression").

## What blocks a workflow run

Both workflows are a single GitHub Actions job with sequential steps and no
`continue-on-error`, so GitHub's default behaviour applies: the first failing step stops
the job and marks the run failed.

- **`ci-quality-gate.yml`:** a Gitleaks finding, or a build error/warning (`-warnaserror`),
  fails the `code-quality-and-security` job.
- **`db-migrations.yml`:** a failed build, a migration that doesn't apply or doesn't
  reverse cleanly, or any failing test in `dotnet test` fails the `migrate-up-down-up`
  job.

## Real examples of a failure blocking progression

These are actual runs from this repository's Actions history (`gh run list`), not
constructed for this document:

| Run | Workflow | Branch | What failed | Why it matters |
| --- | --- | --- | --- | --- |
| [38249597242](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38249597242) | Database migrations | `task/FR13` | `CS0103: The name 'actorId' does not exist in the current context` (×3) in `IncidentsController.cs` | A plain compile error — the simplest possible gate, and it worked: the broken commit never reached a state where its tests could even run. |
| [38412856754](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38412856754), [38447375306](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38447375306) | CI Quality & Security Gate | `Workflow/CI_QualityGate` | `🛑 missing gitleaks license` | The secret-scanning step itself failing closed (refusing to proceed without a license) rather than silently skipping the scan. |
| [38287862621](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38287862621), [38446260430](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38446260430), [38451126489](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38451126489), [38455796962](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38455796962) | Database migrations | `task/NFR01` | `SystemSecretsConfigurationTests.RepositoryConfigFiles_DoNotContainHardcodedCredentials` — `[FAIL]` | A real, meaningful test failure (not a flaky rerun) blocking the pipeline four times while the NFR-01 secrets-loading implementation was being corrected, each time with a genuine subsequent fix commit rather than a retry. |

Each of the above was followed by an actual code or configuration fix and a subsequent
green run on the same branch — the gate did its job: a broken or insecure change did not
silently proceed.

## What does *not* currently block a merge

This is stated plainly because it is a real limitation, not because it was asked for:

- **The repository's branch rulesets do not include a required-status-check rule.**
  `gh api repos/SEN381-CivicConnect-org/SEN381/rulesets` (checked 2026-10-16) returns two
  active rulesets — "No auto merge" (non-fast-forward + 2 required approving reviews on
  `dev`/`staging`/`main`) and "No essential branch deletion" — and neither has a
  `required_status_checks` rule. In practice: a PR shows a red ✗ next to a failed CI run,
  and the project's two-approver review rule (PED §7.3) still applies, but GitHub itself
  would not refuse the merge button on a failing check alone. The gate blocks the
  **workflow run** and makes the failure visible on the PR; it is not wired as a hard
  **branch-protection** gate.
- **`dotnet list package --vulnerable` does not fail its own step.** It reports to the
  job log regardless of findings (see `docs/security/security-dependency-secrets-V1.0.md`)
  — a vulnerable package would be visible in the log but would not, by itself, turn the
  job red.
- **No coverage threshold is enforced**, by design — the M3 brief explicitly does not
  prescribe one, and this project does not invent one to report against.

## Interpretation

The gate that exists is real and has genuinely caught real problems (above), which is
what the M3 brief asks to be demonstrated. What it is not — a branch-protection-enforced,
un-bypassable merge block — is recorded here rather than implied, so the team's own
release-readiness judgement (PED, Release Evidence Summary) is not overstating the
control in place. Turning the existing required-status-check gap into an enforced rule is
a configuration change (adding a rule to the existing "No auto merge" ruleset), not new
engineering, and is left as a known action for the team rather than performed as part of
this documentation pass.

## Traceability

PED v2.1 §7.4 (Current Governance Status and Compliance Gap — the same document already
records the live ruleset's approving-review count as below the project-brief standard
until it was raised to two), §7.6. Pipeline detail: `docs/ci/ci-workflow-V1.0.md`. Check
detail: `docs/security/security-dependency-secrets-V1.0.md`.
