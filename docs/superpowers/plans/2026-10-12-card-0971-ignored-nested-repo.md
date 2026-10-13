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
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c971-base/` | nested-baseline | `/*/*/LandRecoveryIgnoredNestedRepositoryTests/*` | V-1..V-9 | V-1 intentional assertion failure; every refusal control green | 15 | 4 | true |
| CP-2 | S3 | `tests/Antiphon.Tests -> bin-c971-final/` | nested-final | `/*/*/LandRecoveryIgnoredNestedRepositoryTests/*` | V-1..V-9 | all listed, 0 failed/skipped | 15 | 4 | true |
| CP-3 | S3 | CP-2 | checkout-proof | `/*/*/LandRecoveryCheckoutTests/*` | R-1 | all listed, 0 failed | 12 | 2 | true |
| CP-4 | S3 | CP-2 | ignored-obstructions | `/*/*/LandRecoveryIgnoredObstructionTests/*` | R-2 | all 5 Linux cases, 0 failed; 2 Windows alias rows skip on Linux | 5 | 2 | true |
| CP-5 | S3 | CP-2 | registration-proof | `/*/*/LandingRegistrationFreshnessTests/*` | R-3 | all listed, 0 failed/skipped | 4 | 2 | true |
| CP-6 | S3 | CP-2 | half-reset-publication | `/*/*/AgentTaskLandHalfResetTests/*` | R-4 | all listed, 0 failed/skipped | 20 | 6 | true |
| CP-7 | S3 | CP-2 | final-unit | `/*/*/*/*[Category=Unit]` | R-5 | >= 1 executed, 0 failed | 1 | 8 | true |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c971-inherited/` | base-channel-contract | `/*/*/ChannelOutboundContractTests/Server_and_gateway_keep_source_and_transport_boundaries` | R-5 finding 1 | 1 executed; confirm observed assertion failure at original base | 1 | 4 | true |
| CP-9 | S4 | CP-8 | base-grok-pin | `/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` | R-5 finding 2 | 1 executed; confirm observed assertion failure at original base | 1 | 1 | true |

Higher-priority Final stage contract requires R-5 despite the brief's narrower Unit exclusion.
Named integration classes bound this Git-only impact. No unbounded integration class or assembly
run is proposed. Unit cost is estimated eight minutes; total ordinary checkpoint floor 28 minutes.
Checkpoint tool bootstrap uses the build-slot wrapper and isolated `bin-c971-tool/` output;
this infrastructure build is reported separately from the test manifest builds.

### Pending positive controls

PC-1: exact method `C971_NestedRepositoryMatrix`, variant ignored -> restore trailing-slash refusal.
PC-2: same method, nonignored/dirty/symlink/lookalike -> remove outer untracked refusal.
PC-3: same method, gitlink -> accept gitlink before unsupported index/tree/blob checks.
PC-4: same method, git-file -> permit `.git` file.
PC-5: `C971_IgnoredNestedRepositoryCollision`, exact/parent/child -> skip collision checks.
PC-6: `C971_IgnoredNestedRepositoryEvidenceRefused`, ignore/tracked/git-symlink -> accept unknown evidence.
All PCs/variants pending method-scoped SourceLanding Mutation; no Code green discharges them.
Restart: none. Next stage: Review after ordinary verification completes; while R-5 is
incomplete, next stage is Code. Caller lands original Code task then commissions Mutation.

## Verification findings / S4

CP-1 corrected baseline: 15 executed, 14 passed, only `N.ignored.ExpectedProof`
failed (True expected, False observed); every refusal control green. Production
`LandingGit.cs` was identical to original master at that test-only commit.
CP-2..CP-6: 116 passed, 2 Windows-only skips at implementation SHA
`8af6e504024f88fe05c70652264b3e8819bfe13d`.
CP-4's original Min=6 mistakenly counted a Windows-only method as Linux-executed.
Fresh TRX proves exactly 5 passes + 2 Windows skips across the whole class. S4
corrects the arithmetic to 5; selection and source assertions are unchanged.
Rerun CP-4 at S4 committed HEAD. This is a manifest repair, not an assertion relaxation.

CP-7 timed out at its unchanged 24-minute deadline without TRX. R-5 remains incomplete;
no Unit counts or full ordinary-scope pass may be claimed. Console observed two failures:
`ChannelOutboundContractTests.Server_and_gateway_keep_source_and_transport_boundaries`
(missing "configured channel step"), and
`GrokRunnerImageContractTests.Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers`
(installed.Success False). CP-8/CP-9 run only those exact methods in a detached checkout
at original base `02275c3ba8ec6969a6c7bbec8dbe48609367d045`, using this committed
plan by absolute path and expected source SHA equal to that base. This adds no repairs
outside CARD-0971. Retain base-confirmation receipts externally in the task's ignored
checkpoint root; never commit in the detached baseline checkout.

Async dump identifies a live wait in
`RemoteScriptContractTests.C983_Required_jq_driver_rejects_successful_false_probe`
via `C983Fixture.Run -> Process.WaitForExitAsync`, with tests queued behind a semaphore.
This identifies the sampled wait, not a proven root cause or inherited hang.
Stack/dump tooling was installed through the slot gate for diagnosis only; no tests or
assertions were altered. Required next stage stays Code while R-5 is incomplete.
Continuation must complete the original Unit scope and confirm/triage the sampled script
case using bounded committed checkpoint selections. Never widen the existing deadline.
All PCs remain pending for SourceLanding Mutation after ordinary Code, Review and land.

S4 outcomes: CP-4 rerun at `c451672030617f5f19ad6eef8bc2ae60cc37e01c`
passed all 5 executed cases, 2 Windows-only skips, zero failures. CP-8 and CP-9
each executed exactly 1 method at the original base and reproduced the same assertion
failure, confirming those two failures are inherited. R-5 is still incomplete because
the full Unit selection timed out without TRX; the sampled script wait is not yet
confirmed inherited. No production or test source changed after the green feature runs.
