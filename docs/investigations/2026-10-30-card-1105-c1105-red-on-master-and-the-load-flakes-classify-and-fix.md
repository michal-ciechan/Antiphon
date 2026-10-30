# C1105 red on master and the load flakes: classify and fix

Investigation in progress, 2026-10-30. Task `1ada4d4c`; assigned source `ade54222543381e2e1b31ad79d8ae33a4035f70b` on `feat/card-task-1ada4d4c`.

This Investigate stage records evidence and classification only. No repair, approval regeneration, deployment, or safety-deadline change is permitted by its stage contract.

The brief reports repeatable C1105 resume failure on master and load-sensitive C1364, C1008 and C1385 failures. The retained review reports are available through `origin/feat/card-task-b5b016a6:.antiphon/task-b5b016a6.md` and `origin/feat/card-task-8ef7bb9e:.antiphon/task-8ef7bb9e.md`; their original worktrees are absent on this runner. New generated evidence goes in gitignored `.antiphon/investigation-1ada4d4c/`.

Classification, exact witnesses, uncertainties and next stage will follow the local reproduction.

## Confirmed C1105 finding

**TEST DEFECT, inherited on both named branches.** The assigned rc compiled witness executes 1 test and fails 1 (test 806 ms, run 1.891 s; build 152 s, slot wait 0 s). Its outer assertion is missing `PASS resume`, at `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:2852`. The underlying Node assertion at `scripts/fixtures/c1105-git-audit.cjs:789` expects `check=ls-remote status=128`, but receives `audit check=unclassified status= repo=`.

The fixture extracts three production functions at `scripts/fixtures/c1105-git-audit.cjs:785`. Production `c1008_audit` now calls `c1008_forward_host_proof` at `scripts/c590-remote.sh:8369`; its definition is at line 8300 and is absent from that extracted script. Bash returns 127 at the missing command, the explicit `|| return 2` converts it to audit refusal, and `c1008_audit_checked` at lines 8617–8626 stores the fallback detail and refuses. This is not a timeout or a failed preservation assertion: the preceding `saved.audit == initial.audit` assertion on fixture line 789 passes before the error-detail assertion fails. The successful second resume is never reached.

The production failure branch preserves an existing audit through `.auditFailure=$audit | .audit //= $audit` (`scripts/c590-remote.sh:8621`). The production file contains the missing definition; this observation does not certify every live rollout path, Docker behavior or host-proof mode. The broken witness remains a rollout-safety verification gap until repaired and reviewed.

### Reconstructed history

Only unedited historical script/fixture blobs were copied into ignored scratch; no branch switch, bisect checkout, source repair or live rollout occurred. A Node diagnostic preload records the existing child process result and copies its generated journal/script; it does not replace behavior.

| Source | Local fixture outcome | Evidence |
|---|---|---|
| `32f358b40dde0f7c413cc729c9f0f2ae498906e6` (parent) | PASS resume, exit 0, 1.123 s | `history-3/run.log`; fault branch reports ls-remote/128, restored origin resumes successfully |
| `29218e553d15854fca3540f29fd5cb3df2d2b122` | exit 1, 0.345 s, earlier assertion “the audit ran Git” | `history-2/run.log`; the same change added the missing dependency and also had a separately repaired parser error, so it does not expose the later assertion yet |
| `6a58c226242547f3eba5e1dcb438ed68718ca76c` (parser repair) | missing function and unclassified detail | `history-4/run.log:2` |
| master `0e7dd390520a43ab330396a72e9085a8691ac332` | exit 1, 0.556 s, same missing function | `history-1/run.log:1` |
| rc `ade54222543381e2e1b31ad79d8ae33a4035f70b` | exit 1, 0.442 s, same missing function | `history-0/run.log:1`; generated `resume.sh:60` |

Retained transcript excerpt (rc):

```text
DIAGNOSTIC resume status=2 signal=null stderr=/tmp/c1105-audit-jV0GTY/resume.sh: line 60: c1008_forward_host_proof: command not found
 stdout=RecycleGitAuditUnknown audit check=unclassified status= repo=
AssertionError [ERR_ASSERTION]: The input did not match the regular expression /check=ls-remote status=128/.
```

All scratch paths above are relative to `.antiphon/investigation-1ada4d4c/`. `git log -S 'c1008_forward_host_proof mounts' -- scripts/c590-remote.sh` identifies `29218e553d15854fca3540f29fd5cb3df2d2b122`; that commit did not update the extraction fixture. A previously similar omission of `c1008_resolve_audit_cap` was repaired in `2568f2368f2d0ce48894c2676a7e5faaa331cc5d`.

## Not done, noted

A separate Plan/Code task can restore the resume fixture's production-function dependency coverage and diagnostics; no repair is designed or implemented here.

## Load-failure classification and remaining uncertainty

These are distinct observations. Historical labels of “load flake” establish an isolated pass after a failure; they do not identify which process, assertion or deadline failed. Raw historical TRX/stderr is unavailable at the two original worktree paths. In particular, neither named committed review report contains the preview broken-pipe or C1385 copy-timeout text; those observations come from the supplied brief and are not upgraded to independently retrieved transcripts here.

| Test | Cause class | Evidence | Fix | Owner / next evidence | Risk |
|---|---|---|---|---|---|
| C1105 resume preservation | **TEST DEFECT, confirmed** | Missing extracted function; named master and rc both reproduce; pre-change fixture passes; details above | None in this Investigate stage | C1105 fixture / CARD-1607 dependency integration; Plan | Rollout-safety witness fails before the successful resume arm; a test defect is not approval to recycle |
| C1364 wrapper retire volumes | **Unresolved; environment/native-child failure is a live hypothesis** | Prior review `.antiphon/task-8ef7bb9e.md:154` reports exit 139, isolated master/tip passes; current exact method passes 1/1 | None | Script-test/PowerShell runtime owner; retain the first child stderr, failing vector and crash evidence if it recurs | Exit 139 is not evidence of a wait timeout; the root of the native exit is unknown |
| C1008 legacy jq roster, failing-shell | **Unresolved; environment or test timing/roster failure** | Prior review `.antiphon/task-8ef7bb9e.md:155` reports failure under load and 4/4 isolated passes; no original assertion or deadline available | None | Rolling test harness owner; preserve failed T-number, child exit, elapsed time and `harness.log` | Raising a timeout would obscure an unclassified failure; every mode executes 62/66 child invocations |
| C1008 dry-run cleanup receipt | **Unresolved; pipe peer exit, cause unknown** | Brief reports one broken pipe; source has several nested process boundaries and a 60-second preview child ceiling; fresh witness pending below | None | Preview fixture owner; capture exact exception stack, producer/consumer PIDs, child exit and `preview-helper-stderr` | Broken pipe alone does not distinguish a crash, early exit, timeout or program defect |
| C1385 narrow graph registry | **ENVIRONMENT/elapsed-copy refusal mechanism reconstructed; underlying delay unconfirmed** | Brief's exact `evaluating Antiphon.sln copy exceeded 5 s` message is emitted only by the copy-phase safety refusal described below | None; **do not widen the 5-second bound** | LandGraphAudit owner; run read-only `-Check` at recorded load and retain copy timing and refusal output | A timed-out copy cannot approve the graph; apparent load correlation does not prove CPU vs IO vs scheduling |

### Actual deadline boundaries at the assigned rc

- C1364 clears vectors set a 60,000 ms test land-wait budget, expiry vectors use `-LandWaitMinutes 0` (`tests/Antiphon.Tests/Scripts/C1364BoundLandWaitProofTests.cs:74`). The wrapper fixture kills its child after 30 seconds and throws on cancellation (`tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:3001`). The production wait uses wall time, waits only for the bound land, and records poll count (`scripts/deploy-server2.ps1:893`, `:912`). A timeout exception is distinguishable from a returned exit 139; the historical report describes the latter.
- Legacy roster has a **400,000 ms per-mode TUnit timeout**, and its child launcher also has a 12-minute linked deadline (`RollingVolumeRecycleScriptTests.cs:680`, `:774`). `scripts/test-deploy-server2-jq.ps1:28` waits for the full child; `:36` checks its exit before roster checks. This is not the RemoteScriptContractTests LinuxShell wrapper.
- Preview host execution has a 120-second child deadline (`RollingVolumeRecycleScriptTests.cs:3260`); setup helpers have 30 seconds (`tests/Antiphon.Tests/Scripts/C994ScriptProcess.cs:12`). The fake-Docker read-only helper calls `spawnSync('unshare', ..., timeout: 60000)`, records failure stderr, and returns 2 if the status is null (`scripts/fixtures/c1008-fake-docker.sh:244`, `:249`). That 60-second limit exists, but there is no retained evidence that it caused the reported broken pipe.
- RemoteScriptContractTests' generic LinuxShell limit is already **120 seconds on Linux / 180 on Windows** (`RemoteScriptContractTests.cs:9002`) on both named master and rc. Commit `01c32a59a852a425964d2da70f05696efe638486` changed the earlier 60-second limit for a different cleanup-probe witness. The C1105 failure took 600 ms inside that shell, so this ceiling is unrelated.

### C1385 safety mechanism

`tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:345` verifies the definition digest, then invokes the production auditor with `-Check` (line 390). `tools/Antiphon.LandGraphAudit/EvaluationBudget.cs:29` stores 2,253 ms measured copy p95; `CopySeconds` at line 48 doubles and rounds that to **5 seconds on Linux**. `SolutionAudit.cs:54` runs `CanaryRepository.Create` as that timed phase. The copy enumerates tracked/untracked inputs and Markdown, creates directories, checks resolved destinations and copies/rewrites bytes (`CanaryRepository.cs:32`). `WallClock.cs:12` queues the work and waits with a wall clock, so scheduling delay also consumes the bound. `PhaseClock.cs:32` emits the exact refusal message and returns 2; `:53` prevents pass 2 and `approved` after copy failure. This reconstructs what the reported message means without asserting an unmeasured IO or scheduling cause. No bound or approval input was changed, and no `-Update` was invoked.

## Fresh execution findings (first pass)

The C1105 audit family completed 165 cases: 164 passed, only the resume case failed, zero skipped (182.078 s wall). C1364 completed 6/6 green (391.411 s); the affected method also passed alone (32.375 s). The legacy jq method completed all four modes green (819.716 s). These are same-build executions under the host slot gate, not sealed checkpoint receipts.

The first preview rerun failed at `RollingVolumeRecycleScriptTests.cs:839`: `RecycleGitAuditUnknown audit check=preview-helper status=127 repo=. detail=`, test duration 19.773 s. This is a new observed signature, distinct from the brief's broken pipe; diagnosis continues without changing the test.

The first C1385 rerun failed in 1.006 s with `audit refused: source differs from HEAD; commit source and pins before auditing`. The uncommitted change was this report's two citation-line corrections. This is an **investigator-created setup refusal**, not an inherited graph failure and not a copy-timeout reproduction. The refusal is retained at `c1385-alone/run.log:23`; commit the documentation correction before the clean rerun.
