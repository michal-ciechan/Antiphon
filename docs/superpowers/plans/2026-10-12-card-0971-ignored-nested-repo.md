# CARD-0971: ignored nested repository recovery proof

Owner Code task: bc957ed9; base 02275c3ba8ec6969a6c7bbec8dbe48609367d045.
CARD-0954 is landed; CARD-0955/0972 diagnostics and Windows qualification are outside this slice.
Current LandingGit still rejects the empty final segment of Git's ignored `dir/` record.
No existing CARD-0971 implementation was found in the current tree/history.

## Decision

Normalize exactly one terminal slash only after validating the remaining relative path.
Accept that directory record only with positive outer-repository evidence: `check-ignore`
confirms a rule, literal `ls-files --cached` finds no tracked path at/below it, and the
directory has its own real `.git` directory. Reject symlink/reparse components including
`.git`, `.git` files, malformed records, failed commands, and ambiguous evidence.
Then apply unchanged reviewed exact/parent/child collision guards to the normalized path.
All existing index, byte, identity, submodule and sparse/filter checks remain mandatory.
Do not run Git inside ignored repositories: their private configuration/filters must not execute.
Their private dirt cannot affect the outer tracked state; non-ignored repositories remain refused.
Normal ignored files/directories retain CARD-0954 behavior.

Files: `server/Infrastructure/Git/LandingGit.cs`, new
`tests/Antiphon.Tests/Infrastructure/LandRecoveryIgnoredNestedRepositoryTests.cs`.
Use the existing real scratch Git fixture and process-spawn limiter.

## Slices

S1: this short plan, committed/pushed first.
S2: table-driven tests only, committed/pushed; CP-1 proves the production defect.
S3: narrow implementation, committed/pushed; CP-2..CP-7 ordinary verification.
Source stays frozen during each run. No assertions/timeouts loosened. No repeats after green.

## Verification design

Hand-written matrix expectations (independent of production calculations):

| ID | Case / expected proof | Guarded defect -> red |
|---|---|---|
| V-1 | ignored independent nested `.git` directory: true | original terminal-slash refusal -> false instead of true on base |
| V-2 | non-ignored nested repo: false | remove outer untracked guard -> unexpected acceptance |
| V-3 | tracked gitlink: false | remove gitlink/index refusal -> unexpected acceptance |
| V-4 | ignored nested `.git` file: false | allow file-based administrative pointers -> unexpected acceptance |
| V-5 | non-ignored symlink to repository: false | remove outer untracked guard -> unexpected acceptance |
| V-6 | dirty non-ignored nested repository: false | remove outer untracked guard -> unexpected acceptance |
| V-7 | non-ignored directory with fake `.git`: false | infer authority from repo-looking name alone -> unexpected acceptance |
| V-8 | ignored nested repo obstructing reviewed exact/parent/child path: false | skip normalized collision checks -> unexpected acceptance |
| V-9 | ignored nested repo with failed ignore/tracked evidence or symlinked `.git`: false | accept unknown evidence -> unexpected acceptance |
| R-1 | complete existing checkout proof class | weakening byte/index/ignored safeguards -> existing assertion red |
| R-2 | complete ignored obstruction class | weaken parent/child collisions -> existing assertion red |
| R-3 | complete registration freshness class | bypass identity resampling -> existing assertion red |
| R-4 | complete half-reset integration class | permit unsafe reset/publication -> existing assertion red |
| R-5 | whole Unit lane | shared regressions, no full-assembly run |

Only V-1 introduces newly accepted behavior; all other matrix rows must remain green on
base production and after repair. V-8/V-9 controls keep their expectations across the change.
Per-row deliberate mutants/red/restore/green belong to post-land Mutation, not Code.
Manual acceptance: inspect fresh TRX rosters/counts; qualify green receipts with exact tested SHA;
merge-tree against fetched origin/master; full task-range evidence guard; remote ref equals HEAD;
tracked-file count and clean status. Preserve original checkpoint lines in the evidence report.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c971-base/` | nested-baseline | `/*/*/LandRecoveryIgnoredNestedRepositoryTests/*` | V-1..V-9 | V-1 intentional assertion failure; every refusal control green | 7 | 4 | true |
| CP-2 | S3 | `tests/Antiphon.Tests -> bin-c971-final/` | nested-final | `/*/*/LandRecoveryIgnoredNestedRepositoryTests/*` | V-1..V-9 | all listed, 0 failed/skipped | 7 | 4 | true |
| CP-3 | S3 | CP-2 | checkout-proof | `/*/*/LandRecoveryCheckoutTests/*` | R-1 | all listed, 0 failed | 12 | 2 | true |
| CP-4 | S3 | CP-2 | ignored-obstructions | `/*/*/LandRecoveryIgnoredObstructionTests/*` | R-2 | all listed, 0 failed; Windows alias rows skip on Linux | 6 | 2 | true |
| CP-5 | S3 | CP-2 | registration-proof | `/*/*/LandingRegistrationFreshnessTests/*` | R-3 | all listed, 0 failed/skipped | 4 | 2 | true |
| CP-6 | S3 | CP-2 | half-reset-publication | `/*/*/AgentTaskLandHalfResetTests/*` | R-4 | all listed, 0 failed/skipped | 20 | 6 | true |
| CP-7 | S3 | CP-2 | final-unit | `/*/*/*/*[Category=Unit]` | R-5 | >= 1 executed, 0 failed | 1 | 8 | true |

Higher-priority Final stage contract requires R-5 despite the brief's narrower Unit exclusion.
Named integration classes bound this Git-only impact. No unbounded integration class or assembly
run is proposed. Unit cost is estimated eight minutes; total ordinary checkpoint floor 28 minutes.
Checkpoint tool bootstrap uses the build-slot wrapper and isolated `bin-c971-tool/` output;
this infrastructure build is reported separately from the test manifest builds.

### Pending positive controls

PC-1: exact method `C971_NestedRepositoryMatrix`, variant ignored -> restore trailing-slash refusal.
PC-2: same method, nonignored/dirty/symlink/lookalike -> remove outer untracked refusal.
PC-3: same method, gitlink -> remove gitlink mode refusal.
PC-4: same method, git-file -> permit `.git` file.
PC-5: `C971_IgnoredNestedRepositoryCollision`, exact/parent/child -> skip collision checks.
PC-6: `C971_IgnoredNestedRepositoryEvidenceRefused`, ignore/tracked/git-symlink -> accept unknown evidence.
All PCs/variants pending method-scoped SourceLanding Mutation; no Code green discharges them.
Restart: none. Next stage: Review; caller lands original Code task then commissions Mutation.
