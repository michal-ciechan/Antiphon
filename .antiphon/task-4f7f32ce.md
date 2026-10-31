# Review 4f7f32ce: rc-qualification skill and watchers, Final round 2

Subject tip: `cbbe61ddc0684c1d21f245a7a8926ffbabc9e61b` on `origin/feat/card-task-487823b0` (fix task 487823b0-c874-4276-8a8d-a7a9f1099c55, now Canceled), on top of `feat/card-task-f6654e98` 7310cc389 (original owner f6654e98-5bb2-4004-a03d-7d121899c19b, Succeeded). Base cbe3e6b42. Host: server2 runner mirror (Linux, pwsh 7). Read-only: the subject was not changed. Each red-probe mutation was restored with `git checkout`, and the tree was clean afterwards.

Verdict: **clean, no defects.** D-1..D-4 from Review 7f2e8c10 are fixed at the tip, and I re-proved each one independently. Land `cbbe61ddc0684c1d21f245a7a8926ffbabc9e61b`.

## Prior defects

| ID | Status | Evidence |
|---|---|---|
| D-1 compile error | fixed (`using Antiphon.Tests.TestHelpers`) | Tip build: 0 errors. The class passes from the real tree, not a scratch copy. |
| D-2 only `Failed` counted | fixed in both scripts (`trx-failures.ps1:27`, `watch-qual.ps1:45`) | My own probe via `pwsh -File`: TRX files whose only failure is Timeout, Aborted or Error each print `FAILURES chunk-<o>.trx: 1 failed :: WindowsInputTests 1`, and trx-failures prints `TEST Synthetic.WindowsInputFails` (exit 0). A re-arm does not repeat the line. Mixed Failed+Timeout gives `2 failed` and 2 TEST blocks. The green control prints no FAILURES line, and the extractor prints `no failures`. |
| D-3 id-and-status pairs kept forever | fixed (per-id `lastStatus`, `watch-tasks.ps1:46-50`) | Probe against a real node HTTP stub, one `pwsh -File ... -Once` per poll. Sequence Working, Blocked, Working, Blocked, Blocked, Working, Failed, Working, Failed, Failed, Succeeded, Succeeded, Canceled gives exactly BLOCKED, BLOCKED, FAILED, FAILED, SUCCEEDED, CANCELED. Each unchanged repeat and each Working poll is silent. A task already Succeeded at baseline is never reported. All 13 requests carried `?projectId=` and the `X-Antiphon-Task-Token` from env (A-4 also fixed). |
| D-4 `persistent: false` | fixed; SKILL:51 says `timeout_ms: 1800000` only | Checked against the live Monitor tool schema: `timeout_ms` is valid, its cap is 1800000, and unknown properties are rejected. The watcher default of 28 min is under the cap, and its sleep is now clamped to the deadline (A-3 fixed). |

## Red probe (the class can go red)
- Mutation 1: drop `@outcome="Timeout"` from watch-qual.ps1. Class result: 1/1 **failed**, `FAIL RCQUAL qualification reports Timeout as a failure` (harness 1 passed, 1 failed). Restored.
- Mutation 2: revert watch-tasks to first-seen-only (`if (-not $lastStatus.ContainsKey($id))`). Class result: 1/1 **failed**, `FAIL RCQUAL task rearm emits new failed transition` (21 passed, 1 failed). Restored.
- After restore, the class re-ran green: 1/1.

## Tests (server2, all through build-slot, TEMP=/tmp, one build at the tip)
| Run | Result |
|---|---|
| Build `tests/Antiphon.Tests` Debug, UseAppHost=false, OutputPath=bin-r4f7f/ | OK, 0 errors, 4m41s |
| RcQualificationWatcherScriptTests + ProcessSpawnLimitTests + ScriptHarnessProcessContractTests + SlowTestTripwireTests + TestLaneCategoryGuardTests | **54/54**. TRX class identities: 1 + 21 + 29 + 2 + 1. |
| Doc-contract classes that read `.claude/skills` (and AGENTS.md): InstructionBundle 78, DockerStackContract 115, PostLandMutationContract 30, InstructionFileStamp 15, StandingPipelinePolicyDocumentation 12, MechanicalPcContract 11, CheckpointManifestDocumentation 8, RunnerBranchContractDocumentation 8, OrchestratorInstructionsGuidance 6, PostLandRetrospectiveContract 4, ReleaseBranchModelDocumentation 3, RepairSourceDocumentation 3, CommitOnSettleDocumentation 2, BlockedTaskParkProjection 2 | **297/297** (14 classes). This differs from the earlier 142 because Review 7f2e8c10 never listed its 14 classes. Mine is every class that references the skills directory. |
| `pwsh -NoProfile -File scripts/test-orchestration-watchers.ps1`, direct, pwsh 7 | **38 passed, 0 failed**, exit 0, 3.7 s, no owned /tmp residue |
| `audit-land-verification-graph.ps1 -Check` (real tool, Release build via build-slot) | **approved**: digest 7BDABBA075B6..., count 68, unchanged. The new class adds no build definition, and the canary was removed. `-Update` was not run. |
| `check-evidence-diff.ps1 -Repository . -BaseRef cbe3e6b42 -HeadRef HEAD` | 7 commits, 0 violations |
| `git merge-tree --write-tree origin/master HEAD` (origin/master 256b86146) | clean, tree 28f8c89a6 |
| `check-agent-context.ps1` | PASS (within 24576 bytes) |
| Byte scan of all 9 changed files | No BOM, no CR. ASCII-only except AGENTS.md, whose 4 non-ASCII lines all predate the base; the diff adds none. |

## SKILL.md (item 4)
- **Operator-direction citations.** Checked against `docs/orchestration-loop.md:612-626`:
  - Rule 9 is the "reviewed cherry-pick" rule, rules 1 and 2 raise the cap through `dispatch-concurrency.ps1 set`, and rule 10 gives the abandoned-qualification stop autonomy. All three override citations name the right rule.
  - "rc fixes direct to rc, no review" is dated 2026-10-23, which matches the brief.
  - The Debug cap of 4 as a hard create refusal, with Custom as the workaround, matches the brief.
  - Stopping the live run is the operator's decision, which matches the brief. It is also restated at SKILL:187.
  - The SKILL keeps Review for product, dispatcher and watchdog changes. That is narrower than the operator quote, and safe.
- **Human-only list (step 7)** is unchanged from the version Review 7f2e8c10 verified as complete: tags, publish and promote; killing sessions; the live supervisor, lock and files; `C:\Antiphon`; `-AllowWorktree`, `-KillSessions` and `-Hard`; state volumes; private-notes; secrets; no recursive deletion.
- **Named commands exist:** `delegate.ps1` (Role `Custom` is in its ValidateSet), `rc-qualification-status.ps1`, `audit-land-verification-graph.ps1`, `build-slot.ps1`, `dispatch-concurrency.ps1`, the four `scripts/orchestration/*.ps1` scripts, and the referenced docs and skills.

## Advisories (not blocking)
- A-1: The SKILL dates "RC is the quick-merge line: keep testing minimal" to **2026-10-27**. The brief gives that direction no date, and nothing in the repo records one, so I could not verify the date. The quote also uses a colon where the brief has a comma. The caller should confirm the date against its memory and fix it in a later docs touch if it is wrong.
- A-2: Windows PowerShell 5.1 is still owed on the desktop. That covers the smoke run, the PS 5.1-only `git.cmd` stderr-shim row (it runs only when `$env:OS -eq 'Windows_NT'`; Linux covers it with a `Write-Error` stub), and watch-master's local `ErrorActionPreference=Continue` around `2>&1`.
- A-3: The fix worker's report `.antiphon/task-487823b0.md` is not on `origin/feat/card-task-487823b0`, so I could not read its claims. Everything above was verified independently.
- A-4: The smoke harness is fail-fast because `Assert-Smoke` throws, so one red hides the later rows (seen in mutation 1, where 1 row passed). This is pre-existing and acceptable.
- A-8 from the earlier review is retained: the harness ignores `-Case` and `-ResultsDirectory`.
- Landing identity: the reviewed tip is on the branch of fix task 487823b0, which is **Canceled**. The original owner f6654e98 is Succeeded, but its branch tip is 7310cc389. The caller needs to land `cbbe61ddc0684c1d21f245a7a8926ffbabc9e61b` through whichever owner or branch its land mechanism accepts.

## Not run
- Windows PowerShell 5.1: not available on this host (A-2).
- The watchers against the live server, the remote, and a live qualification run: I exercised them only with offline stubs and a local HTTP stub.
- No checkpoint plan table exists for this task, so there are no checkpoint-tool receipts. The evidence is build-slot TRX files with class identities, built at the tip SHA.

Cleanup: all 37 `bin-r4f7f`/`bin-land-audit` directories were deleted by exact literal paths. The probe files are in the session scratchpad only.
