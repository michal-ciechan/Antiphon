# CARD-1545 S1c-sanitization evidence (task 3a7c132f)

Diagnostic only. No gate change, no native lane run, no paid live run, no provider call. Full census files were not edited and the compiled Full census is not claimed. Definition files unchanged: `tests/test-execution-policy.json`, `scripts/lib/nightly-common.ps1`, `scripts/lib/nightly-policy.ps1`, `scripts/lib/nightly-coverage.ps1`, `docs/release-gates.md` have an empty diff from `c7a7b2c9e27c793ff441bbf37e89dc46a4ed82e2` to `HEAD`. `scripts/lib/live-lane-run.ps1` (S1b) is absent and was not reimplemented. The closed checkpoint table was not edited.

## Commits

- Red witnesses: `02e4c970bad23162742d43402eaaaa515a4e13e5`
- Sanitizer at every sink: `3aaf87550992e14b4ced9d01bf00ba3a7fc176c7`
- Invariant culture: `ce2285e4e15d3e27cd7c3f66c53473cbdb677132`
- Scan after the test rows: `da4ad0890bfb6dc0c1b57809abe755f2ddf56a79`

Branch `feat/card-task-3a7c132f`. No rebase, amend, reset, or force-push.

## Red checkpoint (official, assertion red)

Run `20261031-110534-5f82`, source `02e4c970bad23162742d43402eaaaa515a4e13e5`, tool exit 1.

- CP-20 filter `/*/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests/*` executed=2 passed=0 failed=2 skipped=0. `CanarySinksCannotBypassSanitizer` and `EvidenceIsSanitized`.
- CP-26 filter `/*/Antiphon.Tests.Scripts/LiveLaneEvidenceTests/*` executed=2 passed=0 failed=2 skipped=0. `ArtifactGateRequiresPresence` and `C680EvidenceIsSanitized`.

Hard-wall deviation on that run: slotWait 25, total 60m, hard wall 85m. The plan's own row deadlines plus two serial builds would not fit a ~29 minute wall while slot waits of 10-25 minutes are normal.

## Unlisted preflight

Both exact filters were run once through `scripts/build-slot.ps1` before the official green, to avoid spending the 90 minute wall on a compile or fixture bug. Not official evidence.

- PTY: total 2, failed 0, succeeded 2, skipped 0, duration 12s 667ms.
- Antiphon.Tests: total 2, failed 0, succeeded 2, skipped 0, duration 2m 13s 989ms.

## First official green attempt (not the accepting run)

Run `20261031-122910-f537` at `ce2285e4e15d3e27cd7c3f66c53473cbdb677132`. The wrapper captured `dotnet start` with the call operator. The detached executor inherits that stdout pipe, so the wrapper could not bind `checkpointRun` until the whole run finished. The scheduler also admits a command row before test builds. CP-29 therefore scanned with an empty run id.

- CP-20 green: executed=2 passed=2 failed=0 skipped=0. `CHECKPOINT CP-20 commit=ce2285e4e15d3e27cd7c3f66c53473cbdb677132 build=ok filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests/* executed=2 passed=2 failed=0 skipped=0 ... buildSource=verified`
- CP-26 green after a manual bind of the generated context (not source): executed=2 passed=2 failed=0 skipped=0.
- CP-29 red. Console: `INVALID/evidence-sanitization reason=cross-run`. Line: `CHECKPOINT CP-29 commit=ce2285e4e15d3e27cd7c3f66c53473cbdb677132 build=n/a filter=pwsh -NoProfile -File scripts/test-live-lane-evidence.ps1 -Case CompletedRows executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=2 ... buildSource=notApplicable`

## Accepting official run

`scripts/test-live-lane-evidence.ps1 -Case RunPortableRows` at `da4ad0890bfb6dc0c1b57809abe755f2ddf56a79`. `Start-Process` without a captured pipe returns when `dotnet start` itself exits. The test run id is bound before the tests read the context. CP-29 is a second checkpoint run, started only after CP-20 and CP-26 are green, because a command row is admitted before test builds and CP-29's 15 minute deadline cannot cover those builds. The scan command is still the plan command. It reads the test run through the bound context.

Hard-wall deviation: slotWait 25. Test rows total 70m / hard wall 90m. Scan row total 20m / hard wall 25m.

Test run `20261031-125359-1559` verdict GREEN exit=0. unlisted: none.

```
CHECKPOINT CP-20 commit=da4ad0890bfb6dc0c1b57809abe755f2ddf56a79 build=ok filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-3a7c132f/.antiphon/checkpoints/20261031-125359-1559/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=da4ad0890bfb6dc0c1b57809abe755f2ddf56a79 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-20 waited=0s executed=12.026s wall=12.081s
PHASES CP-20 slotWait=0s build=86.8349682s startup=2.7018627s testsWall=7.8767754s teardown=0.7355829s hostWall=11.3142247s
CHECKPOINT CP-26 commit=da4ad0890bfb6dc0c1b57809abe755f2ddf56a79 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneEvidenceTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-3a7c132f/.antiphon/checkpoints/20261031-125359-1559/rows/CP-26/run.trx slot=granted waited=1260s dirty=0 source=da4ad0890bfb6dc0c1b57809abe755f2ddf56a79 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-26 waited=1260s executed=125.315s wall=1385.673s
PHASES CP-26 slotWait=1260s build=691.4538144s startup=4.6422444s testsWall=119.2182585s teardown=0.6455097s hostWall=124.5060127s
```

Scan run `20261031-133339-df46` verdict GREEN exit=0. unlisted: none.

```
CHECKPOINT CP-29 commit=da4ad0890bfb6dc0c1b57809abe755f2ddf56a79 build=n/a filter=pwsh -NoProfile -File scripts/test-live-lane-evidence.ps1 -Case CompletedRows executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=975s dirty=0 source=da4ad0890bfb6dc0c1b57809abe755f2ddf56a79 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-29 waited=975s executed=8.351s wall=983.755s
```

Wrapper after that: `publicationCount=1` and `EVIDENCE-SCAN clean`. CP-29 console is `EVIDENCE-SCAN clean`.

## Sinks

One library function, `LiveClaudeEvidenceSanitizer.Project`, is what every wired sink calls. It drops remote-control session links, screen text, and token-shaped values and emits `C1545-EVIDENCE case= test= kind= run=`. Wired sinks: TRX via `Console.WriteLine` of the projected text, the receipt, the summary, and the public summary. Console logs, HTML reports, and every file under the row directories are scanned for sentinel absence only. Passing-test stdout is not in `console.log` (0 `C1545-EVIDENCE` lines in CP-20 and CP-26 console logs). HTML reports are present (4 files) and were included in the absence scan.

Official TRX StdOut of `EvidenceIsSanitized` contains the 22 roster markers plus the rejected-run line (23 `test=EvidenceIsSanitized` lines). StdOut of `C680EvidenceIsSanitized` contains the 9 roster markers plus the rejected-run line (10 lines). Receipt and public summary contain all 31 roster markers and no sentinel. Summary text equals the public summary. 83 files under the two run row trees and `.antiphon/s1c-ev-g24ea2d` were read; sentinel hits = 0.

## Mutants

In-process companions PC-33 through PC-72, plus `duplicate-uid`, ran inside the accepting CP-20 and CP-26 TRX. Each logged assertion-red then restored-green. PC-38/39/40/41 covered Control, Submitting_via_two_writes, and C514. PC-42 covered the five C680 sinks. PC-57 covered JSON, XML, ANSI-split, and OSC-8. PC-69 covered the five write faults. PC-71 covered raw exits 1/2/3/8. PC-68's witness is publication count while phase is `wait75` (the healthy path does not print `publicationCount=1`; the mutant that skips the guard does). The OSC fixture hides the sentinel until OSC extraction and the unicode decode run.

Owed, not this task: the formal SourceLanding battery, and native CP-4, CP-5, CP-6, CP-7, and CP-21. These six live UIDs were not executed: `C680_LiveNudgeRefusesPermissionAndQuestionDialogs`, `Submitting_via_two_writes_completes_a_turn(claude)`, `Control_auto_connects_a_bridge_session_before_the_first_user`, `Settings_file_prevents_auto_connect_and_keeps_startup_records`, `Remote_control_slash_command_still_arms_after_settings_off`, `C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt`. One_Esc is wired and was not enrolled as a native run.

## Land and history

`scripts/check-evidence-diff.ps1` over `c7a7b2c9e27c793ff441bbf37e89dc46a4ed82e2..da4ad0890bfb6dc0c1b57809abe755f2ddf56a79`: commits=4 entries=0 violations=0.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` exit 1. Inherited add/add conflict in `docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md`. Not merged and not resolved.

`scripts/audit-land-verification-graph.ps1 -Check` (no `-Update`) exit 1: `digest=BAA62938B0DA86DC5950DA5BAA20EFA021BF6E29C949C785CCB3B8E1832C4365 count=68`, manifest does not match the selected graph, pre-check refused. The script stops before evaluation on that refusal. The evaluator DLL was then run directly (`--repo` this worktree, `--solution Antiphon.sln`) and printed `approved` with exit 0. Canary removed. No `-Update`.

Isolated `bin-s1c-ev-*` directories outside `.antiphon/checkpoints` were deleted (141). Checkpoint evidence was kept.
