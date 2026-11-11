# CARD-1224 Final Review — Unix delivery ceilings (S2+S3)

Round 1, Full ordinary scope. Verdict: clean. No product change in this review.

Reviewed source: `bb4f2811c2516d3754db5e34487ce4f6a4497532` on `feat/card-task-9d9adb37`.
Code owner: `9d9adb37-70fa-4261-b935-4d7e706e0a79` (deliverable is that SHA).
Recorded base: `188ac942166884f060fdcc7cbf3b15f670a27b43`.
S2 red commit: `a7c2a97d8516222195e7575fd0db3a741b43a45c`.

## Behavior

`CeilingsFor(PtyBackend.UnixPty)` returns `DeliveryBackend.UnixPty` with brief 43200, reply 14400, single write 86400, and appends `card-1224-unixpty` to the reason. The inbox arm stays 900 / 3000 / 1024. The modern arm stays 43200 / 14400 / 86400. Herdr numbers are untouched.

`RunnerBoundDelivery.ForRunner` selects the Unix arm only when the cached report equals `UnixPty` by ordinal comparison. Null, empty, `InboxConhost`, `ModernConPty`, and every other string stay on the inbox arm and do not append the measurement id. A missing directory or runner id stays null, then inbox.

`PtyBackendPolicy.Resolve` returns `UnixPty` only when the host is not Windows, and it does that before the Windows selectors. A Windows phone-home is `ModernConPty` or `InboxConhost`, so `ForRunner` keeps the inbox triple. A Linux phone-home is `UnixPty`, and that consumer is the Porta pty. An oversized Unix body is still sent and raises `OversizedTerminalDelivery`. Specialist full-inline still requires `DeliveryBackend.ModernConPty` (`SpecialistInputPolicy.cs`).

`Unix_backend_keeps_conservative_limits` is absent at the tip. The inbox and modern pins remain and passed in CP-3.

## Checkpoints

Run `20261111-122546-8617`, host Debian 12, branch `feat/card-task-b6ebea9c`. `dotnet run --project tools/Antiphon.Checkpoints -- validate --evidence .antiphon/checkpoints/20261111-122546-8617/report.json --expected-source-sha bb4f2811c2516d3754db5e34487ce4f6a4497532 --rows CP-1,CP-2,CP-3,CP-4` printed `CHECKPOINT SOURCE VALID` and exited 0.

CHECKPOINT CP-1 commit=bb4f2811c2516d3754db5e34487ce4f6a4497532 build=ok filter=/*/*/UnixPtyDeliveryEnvelopeTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-b6ebea9c/.antiphon/checkpoints/20261111-122546-8617/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=bb4f2811c2516d3754db5e34487ce4f6a4497532 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=bb4f2811c2516d3754db5e34487ce4f6a4497532 build=ok filter=/*/*/UnixDeliveryCeilingTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-b6ebea9c/.antiphon/checkpoints/20261111-122546-8617/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=bb4f2811c2516d3754db5e34487ce4f6a4497532 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=bb4f2811c2516d3754db5e34487ce4f6a4497532 build=reused filter=/*/*/PtyDeliveryCeilingsTests/(The_inbox_backend_keeps_exactly_the_ceilings_that_shipped*)|(No_modern_ceiling_exceeds_the_measured_envelope*)|(A_runner_bound_brief_is_fitted_to_the_inbox_ceiling_not_the_desktop_pty*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-b6ebea9c/.antiphon/checkpoints/20261111-122546-8617/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=bb4f2811c2516d3754db5e34487ce4f6a4497532 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=bb4f2811c2516d3754db5e34487ce4f6a4497532 build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-b6ebea9c/.antiphon/checkpoints/20261111-122546-8617/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=bb4f2811c2516d3754db5e34487ce4f6a4497532 sourceState=clean buildSource=verified
BUDGET wall=826.593s execution=826.591s admissionOnly=0.002s

Unlisted checkpoint commands: none. Charged execution 826.591s is under 30 minutes. CP-1 executed all seven envelope methods on Linux with zero skips.

## S2 red, tip green

Detached worktree at `a7c2a97d8516222195e7575fd0db3a741b43a45c`, leased `UnixDeliveryCeilingTests`. The review branch HEAD stayed `bb4f2811c2516d3754db5e34487ce4f6a4497532`. TRX `/tmp/c1224-s2-red-results/s2-red.trx`: total 9, executed 9, passed 3, failed 6, error 0, skipped 0.

Failed, and for the planned reason:

- `Unix_arm_names_the_receipt`: reason was `probe`.
- `Unix_numbers_match_the_receipt`: single write was 1024.
- `Claude_brief_follows_the_recorded_inline_ceiling`: the under-ceiling brief still spilled.
- `Runner_unix_report_uses_the_unix_arm`: reason was `runner-bound input`.
- `Docker_stack_records_the_unix_receipt`: `card-1224-unixpty` was absent.
- `Session_runtime_records_the_unix_contract`: `card-1224-unixpty` was absent.

Passed at that commit: `Runner_without_a_report_stays_inbox`, `Non_claude_brief_inline_ceiling_stays_zero`, `Specialist_full_inline_stays_on_modern`. The same nine methods are green at the tip (CP-2, 9 passed, 0 failed).

## Guards, evidence, merge, migrations, OS

Nine-class guard union at the reviewed SHA, one leased `dotnet run` of `tests/Antiphon.Tests`: total 78, succeeded 78, failed 0, skipped 0, duration 2m 27s. Classes: `TestClassificationPolicyTests`, `TestClassificationGuardTests`, `SlowTestRegistryMergeTests`, `SlowTestTripwireTests`, `NativeLinuxPolicyWitnessTests`, `MechanicalPcContractTests`, `TaskPlatformGuidanceTests`, `C1729LayeringGuardTests`, `TestLaneCategoryGuardTests`. The brief named the first five; the Final profile and the documented union add the other four. This run is the documented 78.

`scripts/check-evidence-diff.ps1` over `188ac942166884f060fdcc7cbf3b15f670a27b43..bb4f2811c2516d3754db5e34487ce4f6a4497532`: `EVIDENCE result commits=7 entries=0 violations=0`.

`git merge-tree --write-tree` against `origin/master` `6f8741d872d63507827014f972ccf71196722d4a` wrote tree `02f4a7ccce3b5c9f4c61c11fd91b973af794a863` with exit 0. No merge was made.

Downgrade selector (`scripts/impacted-classes.ps1` migrations row): no `migrations` kind, count 0. No migration path is in the diff. Other selector kinds (`land-notification`, `pty-host-fixture`, `server-git-start-sites`, `spawn-limit-rosters`, `statement-budgets`, `test-lane-category`) are outside the ordinary rows. Statement budgets are CP-4. The lane-category guard is inside the 78.

OS witness lint exit 0 for `/tmp/c1224-os-witnesses.json`. Every changed non-document path is inventoried. Pre-existing `OperatingSystem` lines in the dispatcher, reply service, FakeClaude, and the Windows redistributable skip are non-behavior. The envelope test is test-only, matching the plan. Attaching it as a lexed witness is refused by the checker because the file contains interpolated strings (exit 2). Native execution is CP-1, which ran all seven methods. `restartImpact.required` is false: no startup, AppHost, or build-slot path changed. `DelegationSettings.cs` changes numbers a running process keeps until its ordinary restart; this card restarts nothing. Listing that file under `additionalPaths` would force `required=true` in the current checker, so the census leaves it empty.

`tests/derived-impact-index.json` changes in `a7c2a97d8` (34 insertions, 33 deletions) and names `UnixPtyDeliveryEnvelopeTests` and `UnixDeliveryCeilingTests`. `DerivedImpactIndexTests` is not an ordinary row and was not re-run.

## Positive controls

PC-1 through PC-10 stay pending for method-scoped SourceLanding Mutation.

## Advisories

These do not hold the land.

- `ForRunner` fail-closes empty, `InboxConhost`, and case-different reports. The plan does not require separate pins for those strings.
- R-7 prose says 12 statement-budget arguments. The unchanged method has 14. CP-4 passed all 14, which meets the checkpoint floor of 12.
- The digits 86400 also appear outside Gotcha #41. The contract test additionally requires the measurement id, 43200, 14400, and the oversize sentence. Those failed at S2 and pass at the tip.

No input-wait session was added. Parking stays disabled, so there is no automatic release deadline.
