# Security / Dependency / Secrets Check — What Was Done

Scope: the automated secret-scanning and dependency-vulnerability checks that run as part
of `.github/workflows/ci-quality-gate.yml`, plus the application-level secrets test suite
they complement (`SystemSecretsConfigurationTests`, NFR-01). This is the project's
static/security-check evidence for SEN381 Milestone 3, Assessment Area C.

## What runs, and where

`.github/workflows/ci-quality-gate.yml` ("CI Quality & Security Gate"), job
`code-quality-and-security`, on every push to `dev`/`staging`/`main` and every pull
request targeting them:

1. **Secret scanning — Gitleaks.** `gitleaks/gitleaks-action@v2` scans the full checked-out
   history (`fetch-depth: 0`) for committed secrets (API keys, credentials, tokens)
   against its standard detection rules. This is the automated, GitHub-supported
   secret-scanning tool the M3 brief asks for ("a feasible automated tool supported by the
   chosen ecosystem/GitHub").
2. **Dependency vulnerability check.** `dotnet list package --vulnerable` runs after
   `dotnet restore`, reporting any restored NuGet package with a known advisory against
   the exact dependency graph actually used by the solution.
3. **Build / static analysis.** `dotnet build --no-restore -warnaserror` — the .NET/Roslyn
   compiler's own diagnostics (nullability, unused variables, unreachable code, and the
   rest of the default analyzer set) are promoted from warnings to build-breaking errors.
   This is the project's static/code-quality check; there is no separate third-party
   static-analysis product (e.g. SonarCloud) configured, and that is stated here rather
   than implied.

## Secret scanning — real failure and fix history

Gitleaks requires a license key for use on an **organisation** repository
(`SEN381-CivicConnect-org/SEN381`), which the workflow did not have when it was first
added. This produced two genuine, unforced CI failures before the fix:

| Run | Date | Branch | Result |
| --- | --- | --- | --- |
| [38412856754](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38412856754) | 2026-10-13 14:22 | `Workflow/CI_QualityGate` | **Failed** — `🛑 missing gitleaks license` |
| [38447375306](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38447375306) | 2026-10-13 18:30 | `Workflow/CI_QualityGate` | **Failed** — same cause |
| [38447656496](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38447656496) | 2026-10-13 18:33 | `Workflow/CI_QualityGate` | **Passed** |

The fix is visible as its own commit on PR #122 ("Add CI workflow for quality and security
checks", merged 2026-10-13): `Add CI workflow for quality and security checks` →
`Add Gitleaks action to CI workflow` → `Update CI workflow to include Gitleaks license`,
which added the `GITLEAKS_LICENSE` repository secret the action's `env:` block reads.
Since that fix, every run of this workflow has scanned clean (no secret findings have
needed a response).

## Dependency vulnerability check

`dotnet list package --vulnerable` is run against the restored solution on every CI run.
At the time of writing it has not reported any vulnerable package. Its limitation is
stated plainly: the command **reports** findings to the job log but does not itself fail
the step if a vulnerability is found (it always exits `0`), so it does not currently act
as a blocking gate on its own — see `docs/ci/quality-gate-V1.0.md` for what in this
pipeline does and does not block.

## Application-level secrets check (NFR-01)

Separately from the CI tooling above, `SystemSecretsConfigurationTests`
(`tests/CivicConnect.Tests/`, documented in full in
`docs/testing/unit-component/unit-component-tests-V1.0.md`) is the project's own automated
check that the running application cannot start with a hardcoded or missing secret, and
that none of the committed `appsettings*.json` files contain one. This test suite has
itself caught a real regression in CI: on `task/NFR01`, the
`RepositoryConfigFiles_DoNotContainHardcodedCredentials` case failed four times (runs
[38287862621](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38287862621),
[38446260430](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38446260430),
[38451126489](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38451126489),
[38455796962](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38455796962),
2026-10-12 to 2026-10-13) while the connection-string/JWT-secret loading rules were being
built out, blocking `.github/workflows/db-migrations.yml`'s `migrate-up-down-up` job each
time, before passing cleanly once the implementation was corrected and PR #121 ("NFR01
System Secrets configuration") merged on 2026-10-14.

## Traceability

| Check | Tool | Enforced by | Requirement |
| --- | --- | --- | --- |
| Secret scanning | Gitleaks (`gitleaks-action@v2`) | `ci-quality-gate.yml` | NFR-01, §7.6 (Repository Security and Secrets) |
| Dependency vulnerabilities | `dotnet list package --vulnerable` | `ci-quality-gate.yml` | Security Constraints (PED §2.5) |
| Static analysis | .NET/Roslyn compiler, `-warnaserror` | `ci-quality-gate.yml` | Quality Constraints (PED §2.5) |
| Hardcoded/missing secrets at runtime | `SystemSecretsConfigurationTests` | `db-migrations.yml` (`dotnet test`) | NFR-01, IT-CFG-01 |

See `docs/ci/ci-workflow-V1.0.md` for the full pipeline this sits in, and
`docs/ci/quality-gate-V1.0.md` for which of these checks actually block progression today.
