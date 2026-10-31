# CARD-1545 S1b Final Review round 2 (task 3ef5e873)

Subject: origin/feat/card-task-a848e56c at f41be02bd4fd8828303c6df65cfa2b2b8012f43c. This is the fix round on top of 45c24215e, answering Final review 228e67c0. Code landing owner: a848e56c.
Merge base with master: cbe3e6b42401db766f4cee38bc9b34f5b9d314af. origin/master: 96715b56623d57a16c6788838bd18281a40e768d.

Verdict: **Clean.** Land feat/card-task-a848e56c at f41be02bd4fd8828303c6df65cfa2b2b8012f43c. D-1, D-2 and D-3 are closed. My own probes and mutants confirmed each one; I did not rely on the worker's evidence. None of my attacks found a new defect, and every A-advisory from 228e67c0 has been taken.

## Prior defects

| Prior | Status | My evidence |
|---|---|---|
| D-1: Failed row with unknown Setup came out 20 | CLOSED | `live-lane-outcome.ps1:152-160`: an unrecognised Setup gives 2 `row-invalid` for every state. A Failed row with no proof gives 1 before the setup table is consulted. My probes on Failed rows: Setup missing, `''`, `LegacySkip`, `ready`, `READY`, `'Ready '`, `arbitrary`, `$null`, int 0, or a lowercase `setup` key all gave 2. AuthMissing, AuthExpired, QuotaExceeded, DependencyUnavailable, StartupDefaultAbsent, ReadyTimeout, TrustNotCleared, BridgeNotEstablished and SetupContractViolation without proof all gave 1. AuthMissing with own-row transcript proof or own-UID probe proof gave 20. AuthMissing with a probe bound to another UID gave 1. |
| D-2: one probe relabelled every failed row | CLOSED | The probe now carries `Uid` from the identity it was issued for (`:73`, `:83`). `Test-LiveLaneProbeOutage` requires `Probe.Uid -ceq Row.Uid` (`:40`). Two Failed rows plus a probe bound to A, in both orders: lane 1, A=20/Probe, B=1/None. A probe bound to a foreign UID C, or to a foreign account: lane 1, no row relabelled. Precedence checks: an invalid row plus a red row gives 2; a red row plus a skipped row gives 1. |
| D-3: no test of the raw-exit guard | CLOSED | A direct classifier call with a fully passing terminal and raw exit 1, 2, 3, 8, 4, 20, 21, 75, -1 or 255 gives 2 `checkpoint-inconsistent`; raw 0 gives 0. Raw 0 with terminal Outcome Failed or Invalid over passed rows gives 2. The new `raw-passing-{1,2,3,8}` cases catch M7 (below). |

## Attacks (66 direct classifier/probe calls against the tip's lib; 66 pass, 0 fail)

- Proof from another UID, run or account does not count. Covered: transcript ApiError from another session or request; a prompt body that is not the whole prompt; a string Sequence; an empty session; a probe that is unbounded, has a null UID, a different-case UID, a Ready status, or an empty account. All of these stay 1. A probe carries no run field. It is issued inside the same invocation, and its UID comes from the issuing identity, never from the adapter: an adapter returning `Uid=fixture.A.0` for a probe issued for B is recorded as B.
- A probe result is not reused across rows (D-2 cases above, plus mutants M9, M10 and M15).
- A Passed row with outage proof gives 20, never 0, for both transcript and probe proof. A Passed row with `Setup=AuthMissing` also gives 20 (A-2 below).
- Skipped rows and zero rows are never green. Skipped+Ready gives 20; Skipped+Ready+probe gives 20; zero rows give 2 with or without zero UIDs; a null terminal gives 20; a lowercase State gives 2.
- No exit-code collision. Child exits 20, 21, 4 and 75 with a passing terminal give 2, never green or busy. Exits 20 and 21 appear nowhere else in `scripts/` or `tools/` except `scripts/fixtures/c1192-workspace-contract.sh:4836-4837`, where they are an inner sandbox subshell's mount-failure codes inside an unrelated test fixture, a separate namespace. Checkpoint 75/4/5/6/7 are separate as before. Native mode still gives 20 `native-unqualified`.
- Sanitiser and secrets untouched. The diff modifies, deletes or renames **no** existing file (`git diff --diff-filter=MDR` is empty). The new scripts are ASCII-only.

## Mutants (scratch detached worktree at f41be02bd, harness scripts run directly under one `build-slot.ps1` lease, restored with `git checkout` and checked with `git diff --quiet`)

The baseline over all 10 methods was green first (outcome 22/14/7/14/4/4 rows, runner 7/18/3/9).

| ID | Mutation | Red (failing cases) | Restored |
|---|---|---|---|
| M7 | delete `$RawChildExit -ne 0 -or` (outcome:193) | ProviderOutcomeIsDistinct: raw-passing-1/2/3/8 | 22/0 |
| M8 | delete the context owner pid/runId/lane compare (run:46-48) | WrapperOnlyPaidManifest: context-tuple-pid/runId/lane | 18/0 |
| M8b | delete the context processStartedAt compare (run:49) | WrapperOnlyPaidManifest: context-tuple-start | 18/0 |
| M9 | ignore the probe's UID (delete outcome:40) | HealthyProviderAssertionStaysRed: probe-two-failed-False/True | 7/0 |
| M10 | relabel all rows: once any row has proof, later Failed rows inherit it | probe-two-failed-False/True, mixed-red | 7/0 |
| M11 | unknown Setup gives 20 `unclassified-skip` instead of 2 | TypedSetupOutcomes: unknown-skip, failed-setup-missing/empty/LegacySkip/ready/wrong-case-field | 14/0 |
| M12 | original D-1: a Failed row without proof goes to the setup table unless Ready | TypedSetupOutcomes: failed-setup-AuthMissing | 14/0 |
| M13 | transcript proof ignores Request identity | MidRun...: request-foreign | 14/0 |
| M14 | pending receipt not deleted on a failed publish (old A-3) | ProviderOutcomeIsDistinct: evidence | 22/0 |
| M15 | probe the LAST failed row and drop the UID binding (probe reuse) | probe-two-failed-False/True | 7/0 |

After all restores, all 10 methods were green again. PCs remain pending for SourceLanding Mutation; these mutants do not discharge them.

## Sweep (checkpoint tool, one Antiphon.Tests build)

- Tool: `tools/Antiphon.Checkpoints`, built to `bin-rv3eftool/` through build-slot.
- Plan: `.antiphon/rv3ef/review-plan.md`, imported to `.antiphon/rv3ef/manifest.yaml`. Environment `TEMP=/tmp`; the tool confines `UseAppHost=false`.
- Run **20261031-090431-4a9f** at f41be02bd, `--serial`: verdict GREEN exit 0, 11 green / 0 red, one build `bin-rv3ef/` (fresh, then deleted), `unlisted: none`, wall 16m56s.
- `validate --rows CP-2..CP-37` (10 rows): **CHECKPOINT SOURCE VALID source=f41be02bd... rows=10**. Every row is `sourceState=clean buildSource=verified`.

| Row | Filter | executed/passed/failed/skipped |
|---|---|---|
| CP-2 | LiveLaneRunnerTests/* (own build) | 4/4/0/0 |
| CP-16 | LiveLaneOutcomeTests/* | 6/6/0/0 |
| CP-23 | LiveLaneRunnerTests/* (reuse) | 4/4/0/0 |
| CP-31 | ProcessSpawnLimitTests/* | 21/21/0/0 |
| CP-32 | ScriptHarnessProcessContractTests/* | 29/29/0/0 |
| CP-33 | SlowTestTripwireTests/* | 2/2/0/0 |
| CP-34 | TestLaneCategoryGuardTests/* | 1/1/0/0 |
| CP-35 | TestClassificationPolicyTests/* | 39/39/0/0 |
| CP-36 | ShellFixtureLineEndingContractTests/* | 2/2/0/0 |
| CP-37 | RefreshGithubTokenScriptTests/* (scans all docs/**/*.md, including the two new plan docs) | 3/3/0/0 |
| CP-38 | WindowsUnitPlatformAdmissionTests/* (Roslyn scan of all tests/Antiphon.Tests/**/*.cs) | 4/4/0/1 |

CP-38 is green but unqualified. Its one skip is `C1210_Windows_linux_only_outcomes_are_exact` ("requires Windows placement"), so the validator refuses that row (`row_failed`) and it is not in the validated set.

**Repo tree unchanged:** `git status --short` was empty after the run. No `.antiphon/live-lane-outcome-traces` exists, because traces now sit under the harness results root and are deleted in `finally` (old A-1 closed).

Unit lane: not run. The plan's closed ordinary scope is CP-2/CP-16/CP-23 ("the union is the entire ordinary scope"), and the brief names the neighbours and scanners above. The diff adds only Integration-category classes and touches no existing file. The Unit-category scanners it could affect (TestClassificationPolicy, TestLaneCategoryGuard, WindowsUnitPlatformAdmission, RefreshGithubToken, ShellFixture, SlowTestTripwire) were all run.

## Other gates

- Land graph `scripts/audit-land-verification-graph.ps1 -Check`: **approved**, exit 0, digest 7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99, **count=68** (unchanged). Evaluator built to `bin-land-audit` in the scratch worktree through build-slot. `-Update` was not run. The canary was removed.
- `check-evidence-diff.ps1 -Repository . -BaseRef cbe3e6b42 -HeadRef f41be02bd`: commits=26, entries=2, violations=0, exit 0.
- `git merge-tree --write-tree origin/master(96715b566) HEAD`: clean, tree 54fbb24a050ae5b571bcbabd235e1457ed10a502.
- Diff scope: 12 added files and nothing else: 5 scripts, 2 test classes, 1 yaml, 2 plan docs and 2 `.antiphon` reports. No gate, supervisor, cut/publish script, release doc, policy or existing test is touched.

## Advisories (D-2 policy: none blocks the land)

- A-1: the fix worker's report `.antiphon/task-a848e56c.md` is **not** on the branch. Only task-917262f2 and task-a62610b7 are committed, likely because `.antiphon/` is gitignored and needs `git add -f`. The orchestrator should collect it from that task's settlement record.
- A-2: a **Passed** row with a non-Ready Setup (e.g. AuthMissing) and no proof gives 20 `auth-required`. These are contradictory facts, which the exit table would class as 2. It is safe (never green), but S1c could make it `row-invalid`.
- A-3: transcript proof is per row, but nothing requires Session/Request to be distinct across rows. A future S1c reader that attached one shared outage transcript to rows with identical session, request and whole-prompt bytes would relabel each of them. The identical-prompt requirement makes this unlikely. S1c should refuse duplicate (Session, Request) pairs across rows.
- A-4: a Failed AuthMissing row whose proof is an availability outage reports reason `auth-required`, not `provider-outage`. Only the label differs; the exit is 20 either way.
- A-5 (carried from 228e67c0): the classes keep `[Category("Integration")]`. The test-execution policy requires Unit XOR Integration, so the plan amendment's "Unit metadata" line needs an explicit S1b Integration exception. The orchestrator should reconcile this before S1a/S1c.
- A-6 and A-7 (carried): native `Wait` must be bounded itself in S1c. Probe proof has no time link to the failure, and transcript proof should be preferred.

Waiting/parking: this slice adds no input wait. Parking is disabled, so no automatic release deadline applies (CARD-1083).
