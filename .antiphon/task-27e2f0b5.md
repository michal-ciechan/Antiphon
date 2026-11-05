B-1/B-2/B-3 are repaired and runnable ordinary verification passed. Full qualification still needs the deferred Unit/native work; all separate SourceLanding Mutation controls remain pending.

# CARD-1718 A-D3 Code resume — task 27e2f0b5

Original Code landing owner: `5e05fc80-930f-4f03-8c71-6c323d552d63`.
Branch: `feat/card-task-27e2f0b5`; worktree: `/work/worktrees/task-27e2f0b5`.
Review round 2 diff base: `249d4654d2410c4a3959a492ee0165e8a75b75f9`.
Resume base: `f47be567b5545c0014775ff18be9f423714ee14a`.
Implementation/tested source: `40b176ce1225a40bd8290e77975633f17025c4d2`.
Plans: `docs/superpowers/plans/2026-11-05-card-1718-d3-r2-checkpoints.md` and
`docs/superpowers/plans/2026-11-05-card-1718-d3-r2-baseline-checkpoints.md`.
External evidence root: `/work/evidence/code-27e2f0b5`.

## Harvest and implementation

Read the old worktree `/work/worktrees/task-1edbc845` read-only immediately after the brief.
It had one unstaged modification, no staged changes, and no non-ignored untracked files:
`C1677RetentionEngineTests.Residue_retry_keeps_an_ignored_secret` expanded to Execute and DryRun.
Saved its exact diff to `/tmp/task-27e2f0b5-salvage.patch` and applied it in this task only.
The old task can be cancelled; its worktree was never changed or cleaned. Standing resume
authority was sufficient; no approval was requested.

The inherited pushed repair f47be567b added ignored-name checks at admission, locked recheck,
phase B, and retiring-residue recovery; it shared the mandatory name floor across server and
runner classifiers and added descendants plus the requested conventional names. These items
were initially PARTIAL until fresh witnesses, rather than the former author's claims, ran.

The first real retention transport witness planted three secrets together. Split it into
independent paths so `.env` cannot mask a broken credentials-directory guard. Added real
completed-card dispatcher/recipient tests for initial and locked-final readings plus replay;
attached the deterministic late-write hook to the actual transport-owned removal object and
asserted the write occurred. Added server physical cleanup witnesses for each reading. Every
case checks secret bytes and Git registration; no receipt, move or remove substitutes for them.

Found and repaired one remaining B-1 gap: f47be567b's shared floor omitted the original
`appsettings.*.json` secret rule. Committed the five-boundary regression test first, observed
five genuine verdict failures, then moved that rule and its descendant companion into the
shared floor. Removed duplicate appsettings entries from the two consuming lists. No csproj
or props input changed; no graph digest update, timeout widening, assertion relaxation,
AppHost/runner restart, desktop test, land, rc push or master push occurred.

## Caller inventory

| Consumer | Production path and protection | Witness |
|---|---|---|
| Active runner retention | `PhoneHomeCommandDispatcher.RetainAsync` -> `PhoneHomeRuntimeAdapter` -> real `RunnerWorkspaceRetention`; admission and locked recheck call `CheckAsync`, phase B calls `CompleteAsync`, absent-leaf retry calls `ResidueOrAbsentAsync`; every path reads the shared floor before removal | Full C1677 retention engine/dispatch/inventory/native classes, real WebSocket producer/runner server class, independent lost-reply replay cases, five appsettings boundaries |
| Completed-card runner removal | Explicitly composed `RunnerCompletedCardWorkspaceTransport` -> its real `RunnerCompletedCardWorkspaceRemoval`; `InspectAsync` uses the default runner classifier and `CheckRemovalProofAsync` checks command policy; initial and locked-final inspections precede mutation | Full partial `CompletedCardWorkspaceRemovalTests`, new independent byte/registration tests on both readings and replay. This composition remains fixture-only/default inert, as before |
| Server guarded removal | `WorktreeCleanupSettings.EffectiveProtectedIgnored` -> `WorktreeIgnoredContentClassifier` -> `WorktreeIgnoredContentGate` -> `GuardedWorktreeRemoval`, first and final readings | Full classifier/settings classes, full WorktreeGuardedCleanupTests with independent directory descendants, full C0824 desktop legacy portable policy |
| Host cleanup inventory | `HostCleanupWorktreeInventory` consumes the same server classifier; classification only, with protected-first precedence | Classifier contract rows; complete Unit verification belongs to CARD-1720 |
| Evidence copy | `FileSystemEvidenceRetention` uses path normalization; protected names stop in the gate and are not reclassified as copyable evidence | Classifier tests assert protected only and empty disposable/evidence buckets |

## Source history

- `a74ff9fd0d7cbe6c52a64e51c8c0092c2c4e8a2c`: inherited round-1 Review report.
- `4693ba553ce3cf555b5b2158080c22bbf0e1d364`: inherited tests committed before fix.
- `0b7f9070e95e57b7d265663436478339530f3121`: inherited fixture tracking-ref correction, historical red source.
- `f47be567b5545c0014775ff18be9f423714ee14a`: inherited B-1/B-2/B-3 production repair.
- `fd1dd9ec0e0f80a92ab7e30a0edf959c9b78d9ea`: salvage, independent real preservation tests and checkpoint manifests.
- `64e9c7cf9918848d0bc899292d7da67c6ef25ea3`: appsettings regression test committed before its fix.
- `40b176ce1225a40bd8290e77975633f17025c4d2`: appsettings shared-floor fix and corrected transport test setup.

## Historical evidence and execution notes

Historical run `20261105-044522-956f` uses untouched committed 0b7f9070e in the task-owned
detached worktree `/tmp/task-27e2f0b5-baseline`. The three affected production files have
zero diff from reviewed 249d4654d (verified with `git diff 249d4654d 0b7f9070e --` the
retention engine, runner classifier and server cleanup settings). Thus the red SHA has the
reviewed production behavior and the committed red-first tests, not a build-breaking mutant.
Its CP-1 executed 23, passed 0, failed 23, skipped 0: 20 real classifier verdict failures
and 3 real engine admission/removal/recovery verdict failures. All errors were inspected.

Appsettings run `20261105-044749-28df`, exact source 64e9c7cf9, CP-3 executed 5, passed 0,
failed 5, skipped 0. All five errors are expected `Kept` versus actual `Removed` (four Execute
boundaries) or `WouldRemove` (DryRun recovery), not fixture/build errors. Wall 628.378 s,
charged execution 283.262 s, admission-only 345.115 s. Build slot waited 120 s; test slot
waited 225 s. Build and source receipts are clean/verified. The report's `builds: 3` counts
manifest build definitions; only the one selected appsettings build actually ran.

One necessary unlisted build bootstrapped the absent checkpoint tool in native output:
`TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1718-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:UseAppHost=false --nologo`.
Exit 0, 0 errors, 9 existing warnings; build 26.37 s, slot=granted waited=120s,
lease `2c7b26d4-b749-4269-b98f-49903071d6e7`. Every actual build and test run had its own lease.

A mistyped expected SHA was refused before any executor/build/test. Corrected launch
`20261105-044544-2c6b` was stopped and joined while its first build waited for admission,
before changing source to add the appsettings regression. It ran no build or test; there is
no passing receipt for it. No green proof selection was repeated to repair report prose.

## Remaining gates

The brief explicitly assigns the whole Unit lane to CARD-1720. It is not run or marked passed
here. Round-1 Review's whole-Unit timeout (75 minutes, no final TRX, run wall 81m51s) and its
performance follow-up remain historical facts; this task does not relabel them.
Windows native acceptance cannot run while the live desktop qualification is protected.
All such skipped cases remain pending, not passed. Deliberate mutants, red/restore/green
cycles and missing-control discovery belong to method-scoped SourceLanding Mutation.

OS branch inventory/lint passed for 249d4654d..40b176ce1: all changed helpers/callers were
inspected; existing path/native-test branches are unchanged, no OS-selected production
factory/startup behavior changed. Runner defaults and catalogue were read from live GETs;
no fleet location or runner/platform pin was embedded. Restart: none.

## Final affected-class results and blocker disposition

Both full affected rows ran at committed source 40b176ce1. Server CP-2: **443 passed,
0 failed, 10 skipped**. Fresh-runner CP-1: **235 passed, 0 failed, 1 skipped**.
Full-class union: **678 passed, 0 failed, 11 skipped**. Exact appsettings CP-3 rerun:
**5 passed, 0 failed, 0 skipped**, with strict SHA-bound receipt validation exit 0.
Do not add its five repeated cases to the distinct full-class union.

| ID | Actual outcome | Red SHA / fix SHA / tested SHA |
|---|---|---|
| B-1, V-B1 | CLOSED on the executed Linux paths: engine 75/75, active producer/recipient server 70/70 including 9 independent secret/lost-reply cases; appsettings 5/5 across all boundaries in the full engine row; ignored enumeration failure refuses | Red 0b7f9070e (reviewed production 249d4654d), plus appsettings red 64e9c7cf9; fixes f47be567b + 40b176ce1; tested 40b176ce1 |
| B-2, V-B2 | CLOSED: shared source floor, runner 22 secret arguments and 3 disposable controls; server/runner contract 41/41; real completed-card transport 8/8 initial/final/replay cases | Red 0b7f9070e, fix f47be567b (appsettings completion 40b176ce1), tested 40b176ce1 |
| B-3, V-B3 | CLOSED: directory descendants independently protected; physical server cleanup 4/4 on both readings; real runner transport credentials/PEM descendants pass alone | Red 0b7f9070e, fix f47be567b, tested 40b176ce1 |
| V-B1-appsettings | Full engine row 5/5 passed; exact red-row CP-3 rerun also 5/5, strict receipt qualified | Red 64e9c7cf9; fix/tested 40b176ce1 |
| R-1 | Passed full selected classifier/settings, legacy portable release, retention and removal classes; native skips below are not passed | tested 40b176ce1 |
| R-2 | Passed all selected guards: server classification policy 39, classification guard 1, slow registry 5, slow tripwire 2; runner classification guard 1 | tested 40b176ce1 |
| Original S4a V-1..V-27 / R-3 | All selected portable legacy settings/policy cases passed: 32 + 123. This is the brief's full named CP-23 family; it does not credit unselected original S4a CP-24 R-1/R-2 work or any Mutation control | tested 40b176ce1 |
| V-Native / Review V-1 | OPEN: the 11 exact skipped variants below need native qualification; whole Unit remains CARD-1720's, not passed here | no Windows run authorized |
| Review V-2 / performance R1 | OPEN caller Debug obligation. Exact historical run exceeded 30 minutes wall (32m20s); repaired first run also exceeded it (34m28s). Separate charged execution/admission below; see performance handoff, not a suite retry | historical 0b7f9070e and repaired 40b176ce1 |
| A-1 | PARTIAL: all conventional shapes explicitly requested by this brief are added. `*.token` stays intentionally excluded; `bin/design.token` remains disposable. This finite-floor choice is documented and tested | f47be567b |
| A-2 | Conservative false holds for public fixtures/credentials helper names remain intentional; no assertion or floor was weakened | unchanged policy |
| A-3 | OPEN native Windows alias/trailing-dot/space assessment; Linux string matching is not native evidence | deferred |
| A-4 | Fresh essential unedited receipts/provenance are retained below. Historical round-1 receipt omission is not retroactively relabelled or rerun for prose | this report |

Per-class fresh TRX counts (passed / failed / skipped):

| Assembly / class | Counts |
|---|---|
| Runner C1677RetentionEngineTests | 75 / 0 / 0 |
| Runner C1677RetentionDispatchTests | 11 / 0 / 0 |
| Runner C1677RetentionInventoryTests | 3 / 0 / 0 |
| Runner C1677RetentionNativeProcessTests | 14 / 0 / 0 |
| Runner CompletedCardWorkspaceRemovalTests (all partials) | 106 / 0 / 1 |
| Runner RunnerWorkspaceIgnoredContentFloorTests | 25 / 0 / 0 |
| Runner TestClassificationGuardTests | 1 / 0 / 0 |
| Server WorktreeIgnoredContentClassifierTests | 117 / 0 / 0 |
| Server WorktreeCleanupSettingsValidatorTests | 3 / 0 / 0 |
| Server C0824DesktopLegacyReleaseSettingsTests | 32 / 0 / 0 |
| Server C0824DesktopLegacyReleasePolicyTests | 123 / 0 / 0 |
| Server C0824DesktopLegacyReleaseNativeTests | 0 / 0 / 8 |
| Server C1677RetentionServerTests | 70 / 0 / 0 |
| Server WorktreeGuardedCleanupTests | 51 / 0 / 2 |
| Server TestClassificationPolicyTests | 39 / 0 / 0 |
| Server TestClassificationGuardTests | 1 / 0 / 0 |
| Server SlowTestRegistryMergeTests | 5 / 0 / 0 |
| Server SlowTestTripwireTests | 2 / 0 / 0 |

Each new method and every argument execution was checked in fresh TRX: appsettings boundaries
5/5, runner floor arguments 22/22, residue modes 2/2, completed-card transport timing/replay
8/8, cross-classifier floor 41/41, active server/runner lost-reply paths 9/9, server physical
descendant/reading paths 4/4. Full-class records were not replaced with method-only smokes.

Deferred native variants, never marked passed:

- C0824: Held_directory_defers_never_forces(cwd, open-handle); Long_path_untracked_file_holds_the_tree;
  Long_path_ignored_tree_is_held_not_partial; Long_path_never_leaves_an_unregistered_orphan;
  Windows_aliases_and_junctions_are_refused(junction-leaf, junction-inside, short-name): 8 total.
- WorktreeGuardedCleanup: C665_LockedFileMidDeleteResumesOnLaterPass (Windows sharing lock),
  C665_EmptyUnlistedJunctionTargetSurvivesRemoval (this Git lists the empty directory link): 2 total.
- CompletedCardWorkspaceRemoval: C1032_RemovalLateFileContentRefuses, existing PC-587 held-handle
  writer/rename variants require Windows delete-disposition sharing: 1 skipped execution.

Ordinary verification is complete for the brief's runnable Linux classes, including CP-3.
The overall Full/zero-skip qualification gate remains incomplete: no Unit/CARD-1720, native
Windows, or deliberate Mutation credit is claimed. The first repaired run's overall exit is
2 because its original CP-1/CP-3 build was refused; retain that RED outcome. Its server CP-2
has clean, verified per-row provenance; fresh-runner receipts replace only the refused rows.

## Provenance refusal and corrected runner run

Run 20261105-045917-ce44 first built runner source 40b176ce1 after the appsettings red run
at 64e9c7cf9. The inherited Linux `CopyOwnedPtyHostAppHostForHerdr` target invokes a nested
apphost build that writes native obj caches outside its isolated entry output. It changed
pre-existing generated files in exactly four roots: src/Antiphon.Agents.Pty/obj,
src/Antiphon.PtyHost.Protocol/obj, src/Antiphon.PtyHost/obj and
src/Antiphon.SessionRunner.Contracts/obj. The confinement guard correctly refused; no
runner test executed and no receipt from that attempt is counted as passing. Tracked source
was clean. No restore, bypass, csproj edit, timeout change or assertion change was used.

The server build continued and its full CP-2 passed. Reran exactly the refused CP-1/CP-3
rows once in fresh detached `/tmp/task-27e2f0b5-runner-verify` at the same committed SHA,
run 20261105-051349-41b0. That fresh build has verified source provenance. Every driver
leased independently; there was at most one build in each worktree. The native producer is
an inherited nested target inside its leased top-level build, not an extra unleased command
issued by this delegate. This clean-worktree rerun is reported explicitly, not hidden as a
passing build from the refused run.

Historical CP-2 inspected 40 results: 37 genuine expected assertion failures and 3 existing
`.env` controls passed, with zero skips/fixture failures. Its live replay failure was the
incorrectly cleared RemoteWorktreePath after physical removal. Across historical CP-1/CP-2
and the separate appsettings red row: 68 executions, 65 expected assertion failures, 3 passes.
No unchanged green selection was repeated for report prose. The appsettings method executes
inside the full class and once as the required exact red-row rerun; no loaded repetition or
deliberate mutation was commissioned.

## Receipt qualification, costs and remaining ownership

`validate-checkpoint-receipt.ps1` on fresh-runner report, expected source 40b176ce1,
`-Rows CP-3`: exit 0, `CHECKPOINT SOURCE VALID source=40b176ce1225a40bd8290e77975633f17025c4d2 rows=1`.
The same strict zero-skip qualification on CP-1 returns exit 2, `row_failed`, because its
one Windows variant is skipped; this is not relabelled Full. Server CP-2's independent
`Test-CheckpointSourceEvidence` source-only check returned True for the exact source SHA.
Its full report stays RED due to the earlier runner build refusal, and its ten native skips
still prevent zero-skip qualification. Counts and intended methods/classes were audited
directly from both fresh TRX files; no receipt or TRX was edited.

| Run | Actual tested source | Wall / charged execution / admission-only |
|---|---|---|
| Historical negatives 20261105-044522-956f | 0b7f9070e95e57b7d265663436478339530f3121 | 1940.221s / 889.788s / 1050.433s |
| Appsettings negative 20261105-044749-28df | 64e9c7cf9918848d0bc899292d7da67c6ef25ea3 | 628.378s / 283.262s / 345.115s |
| First repaired run 20261105-045917-ce44 | 40b176ce1225a40bd8290e77975633f17025c4d2 | 2068.456s / 748.216s / 1320.240s |
| Fresh runner 20261105-051349-41b0 | 40b176ce1225a40bd8290e77975633f17025c4d2 | 1408.680s / 223.466s / 1185.214s |

Runs overlap across independent worktrees; do not add their wall times as task wall time.
The checkpoint tool's `builds` field sometimes counts manifest definitions (e.g. appsettings
selected one of three definitions; fresh runner selected one build while its full plan defines
two). Actual top-level builds: historical 2, appsettings 1, first repaired 2 (one provenance
refusal), fresh runner 1, plus the separately declared bootstrap 1. No other build/test driver
was issued. Every scheduled build and row acquired its own host lease; exact `slot=` and
`waited=` plus phase costs are preserved unedited below. No loaded repetitions ran.

Required performance handoff: `docs/investigations/2026-11-05-card-1718-d3-r2-performance-handoff.md`.
Caller commissions Debug; Code was forbidden to sub-delegate. This is a genuine open
performance obligation, separate from the passed assertions and CARD-1720's historical
whole-Unit timeout. Do not retry passing classes solely to correct report prose.

Every historical M1/M2, PEM removal and protected-after-disposable control remains PENDING.
Every candidate PC-D3-1..PC-D3-11 and its recovery-DryRun/recovery-Execute and
initial-transport/final-transport variants remains PENDING. Mutation owns deliberate edits,
red/restore/green and missing-control discovery. See the unbound candidate at
`docs/superpowers/mutations/2026-11-05-card-1718-d3-r2-candidate.md`; it is not a bound pack.
No existing S4a or other card's controls are newly credited by these ordinary class runs.

## Evidence preservation and cleanup

Raw JSON/TRX/logs/checkpoint outputs stayed untracked and ignored. Archives were compared
against the original files with `tar -d` before removing outputs. The original absolute TRX
paths in the following unedited receipts resolve inside the corresponding archive's relative
`.antiphon/checkpoints/<run>/` tree; archive mapping does not rewrite a receipt.

- Historical archive `/work/evidence/code-27e2f0b5/historical-negative-evidence.tar.gz`, SHA-256
  `18d380648566639baa05854c2c63c82cc539439d474acd4fff27e2e44c5717e1`.
- Primary canceled/appsettings/server archive `/work/evidence/code-27e2f0b5/primary-checkpoint-evidence.tar.gz`, SHA-256
  `3f7707c92e4384b182ad9c99010ceae5da0f3faf5b209a22d99a641e5e17a504`.
- Fresh runner archive `/work/evidence/code-27e2f0b5/fresh-runner-evidence.tar.gz`, SHA-256
  `1650e3324d7dba642d0e26933c3bd18c66cf927a122bdd95b2e382a912d3007c`.

The external evidence root also retains plain checkpoint reports, per-class/method TRX audit
JSON and the OS inventory. Essential unedited receipt lines are retained below. Compiled
outputs are deliberately not copied into the archives; the unchanged build-provenance
records are retained. All owned runs joined before cleanup. Removed only exact literal
task-owned paths: the two detached verification worktrees, the three isolated primary test
output roots, four generated native obj roots, and the bootstrap/checkpoint output roots.
No cleanup targeted the old task worktree, any other session, Docker volume, or canonical
checkout. No source file was restored or mutated after its proof run.

The final report/evidence/candidate publication commit changes Markdown only. The exact
implementation/test bytes remain those verified at 40b176ce1; its receipts are not relabelled
to that later documentation commit. Final remote tip, complete-range evidence guard,
OS inventory and merge-tree result are in the final caller summary/closing checks.

## Essential unedited checkpoint lines

```text
run: 20261105-044522-956f   manifest: /tmp/task-27e2f0b5-baseline/.antiphon/checkpoints/20261105-044522-956f/manifest.resolved.yaml
commit: 0b7f9070e95e57b7d265663436478339530f3121  branch: HEAD  worktree: /tmp/task-27e2f0b5-baseline  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 0b7f9070e95e57b7d265663436478339530f3121 state=clean buildSource=verified
CHECKPOINT CP-1 commit=0b7f9070e95e57b7d265663436478339530f3121 build=ok filter=/*/*/(RunnerWorkspaceIgnoredContentFloorTests*)|(C1677RetentionEngineTests*)/(Disposable_override_cannot_release_a_secret_shaped_name*)|(Ignored_secret_files_survive_admission_on_dry_run_and_execute*)|(Phase_b_keeps_an_ignored_secret_planted_after_the_move*)|(Residue_retry_keeps_an_ignored_secret*) executed=23 passed=0 failed=23 skipped=0 trx=/tmp/task-27e2f0b5-baseline/.antiphon/checkpoints/20261105-044522-956f/rows/CP-1/run.trx slot=granted waited=150s dirty=0 source=0b7f9070e95e57b7d265663436478339530f3121 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=150s executed=9.956s wall=160.183s
PHASES CP-1 slotWait=180s build=292.551457s startup=4.9449936s testsWall=1.5011255s teardown=0.8553652s hostWall=7.3014787s
CHECKPOINT CP-2 commit=0b7f9070e95e57b7d265663436478339530f3121 build=ok filter=/*/*/(WorktreeIgnoredContentClassifierTests*)|(C1677RetentionServerTests*)/(Both_classifiers_protect_the_secret_floor_under_a_disposable_override*)|(Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay*) executed=40 passed=3 failed=37 skipped=0 trx=/tmp/task-27e2f0b5-baseline/.antiphon/checkpoints/20261105-044522-956f/rows/CP-2/run.trx slot=granted waited=645s dirty=0 source=0b7f9070e95e57b7d265663436478339530f3121 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=645s executed=28.863s wall=674.232s
PHASES CP-2 slotWait=870s build=466.0503557s startup=9.1639091s testsWall=15.0109726s teardown=3.8649554s hostWall=28.0398373s
BUDGET wall=1940.221s execution=889.788s admissionOnly=1050.433s
unlisted: none (the tool ran no other build or test command)
wall: 32m20s  sequential-equivalent: 13m54s  builds: 2  max-concurrent-builds: 1  rows: 0 green 2 red 0 skipped
outputs: refused/kept bin-c1718-r2-red-runner/, bin-c1718-r2-red-server/
verdict: RED exit=1
```

```text
run: 20261105-044749-28df   manifest: /work/worktrees/task-27e2f0b5/.antiphon/checkpoints/20261105-044749-28df/manifest.resolved.yaml
commit: 64e9c7cf9918848d0bc899292d7da67c6ef25ea3  branch: feat/card-task-27e2f0b5  worktree: /work/worktrees/task-27e2f0b5  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 64e9c7cf9918848d0bc899292d7da67c6ef25ea3 state=clean buildSource=verified
CHECKPOINT CP-3 commit=64e9c7cf9918848d0bc899292d7da67c6ef25ea3 build=ok filter=/*/*/C1677RetentionEngineTests/Appsettings_secret_survives_every_retention_boundary executed=5 passed=0 failed=5 skipped=0 trx=/work/worktrees/task-27e2f0b5/.antiphon/checkpoints/20261105-044749-28df/rows/CP-3/run.trx slot=granted waited=225s dirty=0 source=64e9c7cf9918848d0bc899292d7da67c6ef25ea3 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=225s executed=8.772s wall=234.645s
PHASES CP-3 slotWait=345s build=229.0794689s startup=4.4526469s testsWall=1.8889554s teardown=0.7452369s hostWall=7.0868395s
BUDGET wall=628.378s execution=283.262s admissionOnly=345.115s
unlisted: none (the tool ran no other build or test command)
wall: 10m28s  sequential-equivalent: 3m54s  builds: 3  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: refused/kept bin-c1718-appsettings-red/
verdict: RED exit=1
```

```text
run: 20261105-045917-ce44   manifest: /work/worktrees/task-27e2f0b5/.antiphon/checkpoints/20261105-045917-ce44/manifest.resolved.yaml
commit: 40b176ce1225a40bd8290e77975633f17025c4d2  branch: feat/card-task-27e2f0b5  worktree: /work/worktrees/task-27e2f0b5  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 40b176ce1225a40bd8290e77975633f17025c4d2 state=clean buildSource=unknown
CHECKPOINT CP-1 commit=40b176ce1225a40bd8290e77975633f17025c4d2 build=failed filter=/*/*/(RunnerWorkspaceIgnoredContentFloorTests*)|(C1677RetentionEngineTests*)|(C1677RetentionDispatchTests*)|(C1677RetentionInventoryTests*)|(C1677RetentionNativeProcessTests*)|(CompletedCardWorkspaceRemovalTests*)|(TestClassificationGuardTests*)/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=390s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-1 waited=390s executed=0.000s wall=0.000s
CHECKPOINT CP-3 commit=40b176ce1225a40bd8290e77975633f17025c4d2 build=failed filter=/*/*/C1677RetentionEngineTests/Appsettings_secret_survives_every_retention_boundary executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=390s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-3 waited=390s executed=0.000s wall=0.000s
CHECKPOINT CP-2 commit=40b176ce1225a40bd8290e77975633f17025c4d2 build=ok filter=/*/*/(WorktreeIgnoredContentClassifierTests*)|(WorktreeCleanupSettingsValidatorTests*)|(C0824DesktopLegacyRelease*)|(C1677RetentionServerTests*)|(WorktreeGuardedCleanupTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)/* executed=443 passed=443 failed=0 skipped=10 trx=/work/worktrees/task-27e2f0b5/.antiphon/checkpoints/20261105-045917-ce44/rows/CP-2/run.trx slot=granted waited=330s dirty=0 source=40b176ce1225a40bd8290e77975633f17025c4d2 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=330s executed=339.933s wall=670.005s
PHASES CP-2 slotWait=930s build=281.7243853s startup=10.2571519s testsWall=324.6724985s teardown=4.1203041s hostWall=339.0501116s
BUDGET wall=2068.456s execution=748.216s admissionOnly=1320.240s
unlisted: none (the tool ran no other build or test command)
wall: 34m28s  sequential-equivalent: 11m10s  builds: 2  max-concurrent-builds: 1  rows: 1 green 2 red 0 skipped
outputs: refused/kept bin-c1718-r2-runner/, bin-c1718-r2-server/
verdict: RED exit=2
```

```text
run: 20261105-051349-41b0   manifest: /tmp/task-27e2f0b5-runner-verify/.antiphon/checkpoints/20261105-051349-41b0/manifest.resolved.yaml
commit: 40b176ce1225a40bd8290e77975633f17025c4d2  branch: HEAD  worktree: /tmp/task-27e2f0b5-runner-verify  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 40b176ce1225a40bd8290e77975633f17025c4d2 state=clean buildSource=verified
CHECKPOINT CP-1 commit=40b176ce1225a40bd8290e77975633f17025c4d2 build=ok filter=/*/*/(RunnerWorkspaceIgnoredContentFloorTests*)|(C1677RetentionEngineTests*)|(C1677RetentionDispatchTests*)|(C1677RetentionInventoryTests*)|(C1677RetentionNativeProcessTests*)|(CompletedCardWorkspaceRemovalTests*)|(TestClassificationGuardTests*)/* executed=235 passed=235 failed=0 skipped=1 trx=/tmp/task-27e2f0b5-runner-verify/.antiphon/checkpoints/20261105-051349-41b0/rows/CP-1/run.trx slot=granted waited=405s dirty=0 source=40b176ce1225a40bd8290e77975633f17025c4d2 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=405s executed=120.867s wall=526.003s
PHASES CP-1 slotWait=900s build=44.8185366s startup=3.4070972s testsWall=115.9162327s teardown=0.6199585s hostWall=119.9432854s
CHECKPOINT CP-3 commit=40b176ce1225a40bd8290e77975633f17025c4d2 build=reused filter=/*/*/C1677RetentionEngineTests/Appsettings_secret_survives_every_retention_boundary executed=5 passed=5 failed=0 skipped=0 trx=/tmp/task-27e2f0b5-runner-verify/.antiphon/checkpoints/20261105-051349-41b0/rows/CP-3/run.trx slot=granted waited=285s dirty=0 source=40b176ce1225a40bd8290e77975633f17025c4d2 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=285s executed=10.800s wall=296.112s
PHASES CP-3 slotWait=285s build=0s startup=4.5730781s testsWall=3.6743454s teardown=0.7503809s hostWall=8.9978047s
BUDGET wall=1408.680s execution=223.466s admissionOnly=1185.214s
unlisted: none (the tool ran no other build or test command)
wall: 23m28s  sequential-equivalent: 13m42s  builds: 2  max-concurrent-builds: 1  rows: 2 green 0 red 0 skipped
outputs: deleted bin-c1718-r2-runner/
verdict: GREEN exit=0
```
