# C1105 red on master and the load flakes: classify and fix

Investigation evidence, 2026-10-30. Task `1ada4d4c`; assigned source `ade54222543381e2e1b31ad79d8ae33a4035f70b` on `feat/card-task-1ada4d4c`.

This Investigate stage records evidence and classification only. No repair, approval regeneration, deployment, or safety-deadline change is permitted by its stage contract.

The brief reports repeatable C1105 resume failure on master and load-sensitive C1364, C1008 and C1385 failures. The retained review reports are available through `origin/feat/card-task-b5b016a6:.antiphon/task-b5b016a6.md` and `origin/feat/card-task-8ef7bb9e:.antiphon/task-8ef7bb9e.md`; their original worktrees are absent on this runner. New generated evidence goes in gitignored `.antiphon/investigation-1ada4d4c/`.

C1105 is confirmed as a test-fixture defect; a separate fresh preview failure is confirmed as a child-PATH prerequisite problem. The original load incidents remain partly unconfirmed, as detailed below. No source repair was implemented.

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

These are distinct observations. Historical labels of “load flake” establish an isolated pass after a failure; they do not identify which process, assertion or deadline failed. Raw historical TRX/stderr is unavailable at the two original worktree paths. The risk remains open where the cause cannot be reconstructed. In particular, neither named committed review report contains the preview broken-pipe or C1385 copy-timeout text; those observations come from the supplied brief and are not upgraded to independently retrieved transcripts here.

| Test | Cause class | Evidence | Fix | Owner / next evidence | Risk |
|---|---|---|---|---|---|
| C1105 resume preservation | **TEST DEFECT, confirmed** | Missing extracted function; named master and rc both reproduce; pre-change fixture passes; details above | None in this Investigate stage | C1105 fixture / CARD-1607 dependency integration; Plan | Rollout-safety witness fails before the successful resume arm; a test defect is not approval to recycle |
| C1364 wrapper retire volumes | **Unresolved; environment/native-child failure is a live hypothesis** | Prior review `.antiphon/task-8ef7bb9e.md:154` reports exit 139, isolated master/tip passes; current exact method passes 1/1 and full class 6/6 | None | Script-test/PowerShell runtime owner; retain the first child stderr, failing vector and crash evidence if it recurs | Exit 139 is not evidence of a wait timeout; the root of the native exit is unknown |
| C1008 legacy jq roster, failing-shell | **Unresolved; environment or test timing/roster failure** | Prior review `.antiphon/task-8ef7bb9e.md:155` reports failure under load and 4/4 isolated passes; no original assertion or deadline available; current isolated modes 4/4 and full Rolling class 77/77 pass | None | Rolling test harness owner; preserve failed T-number, child exit, elapsed time and `harness.log` | Raising a timeout would obscure an unclassified failure; every mode executes 62/66 child invocations |
| C1008 dry-run cleanup receipt | **ENVIRONMENT confirmed for this runner's fresh status-127 failure; historical broken pipe unresolved** | Missing `chroot` on inherited child PATH; 127 reproduced twice, then 1/1 passed with a child-only PATH adjustment; trace below | No source change | Runner image/child prerequisite owner; preserve raw stderr for the historical pipe incident | This environment witness must not be mislabeled as reproducing the earlier broken pipe |
| C1385 narrow graph registry | **ENVIRONMENT/elapsed-copy refusal mechanism reconstructed; underlying delay unconfirmed** | Brief's exact `evaluating Antiphon.sln copy exceeded 5 s` message is emitted only by the copy-phase safety refusal described below | None; **do not widen the 5-second bound** | LandGraphAudit owner; run read-only `-Check` at recorded load and retain copy timing and refusal output | A timed-out copy cannot approve the graph; apparent load correlation does not prove CPU vs IO vs scheduling |

### Actual deadline boundaries at the assigned rc

- C1364 clears vectors set a 60,000 ms test land-wait budget, expiry vectors use `-LandWaitMinutes 0` (`tests/Antiphon.Tests/Scripts/C1364BoundLandWaitProofTests.cs:74`). The wrapper fixture kills its child after 30 seconds and throws on cancellation (`tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:3001`). The production wait uses wall time, waits only for the bound land, and records poll count (`scripts/deploy-server2.ps1:893`, `:912`). A timeout exception is distinguishable from a returned exit 139; the historical report describes the latter.
- Legacy roster has a **400,000 ms per-mode TUnit timeout**, and its child launcher also has a 12-minute linked deadline (`RollingVolumeRecycleScriptTests.cs:680`, `:774`). `scripts/test-deploy-server2-jq.ps1:28` waits for the full child; `:39` checks its exit before roster checks. This is not the RemoteScriptContractTests LinuxShell wrapper.
- Preview host execution has a 120-second child deadline (`RollingVolumeRecycleScriptTests.cs:3260`); setup helpers have 30 seconds (`tests/Antiphon.Tests/Scripts/C994ScriptProcess.cs:12`). The fake-Docker read-only helper calls `spawnSync('unshare', ..., timeout: 60000)`, records failure stderr, and returns 2 if the status is null (`scripts/fixtures/c1008-fake-docker.sh:244`, `:249`). That 60-second limit exists, but there is no retained evidence that it caused the reported broken pipe.
- RemoteScriptContractTests' generic LinuxShell limit is already **120 seconds on Linux / 180 on Windows** (`RemoteScriptContractTests.cs:9002`) on both named master and rc. Commit `01c32a59a852a425964d2da70f05696efe638486` changed the earlier 60-second limit for a different cleanup-probe witness. The C1105 failure took 600 ms inside that shell, so this ceiling is unrelated.

### C1385 safety mechanism

`tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:345` verifies the definition digest, then invokes the production auditor with `-Check` (line 390). `tools/Antiphon.LandGraphAudit/EvaluationBudget.cs:29` stores 2,253 ms measured copy p95; `CopySeconds` at line 48 doubles and rounds that to **5 seconds on Linux**. `SolutionAudit.cs:54` runs `CanaryRepository.Create` as that timed phase. The copy enumerates tracked/untracked inputs and Markdown, creates directories, checks resolved destinations and copies/rewrites bytes (`CanaryRepository.cs:32`). `WallClock.cs:12` queues the work and waits with a wall clock, so scheduling delay also consumes the bound. `PhaseClock.cs:32` emits the exact refusal message and returns 2; `:53` prevents pass 2 and `approved` after copy failure. This reconstructs what the reported message means without asserting an unmeasured IO or scheduling cause. No bound or approval input was changed, and no `-Update` was invoked.

## Fresh execution findings (first pass)

The C1105 audit family completed 165 cases: 164 passed, only the resume case failed, zero skipped (182.078 s wall). C1364 completed 6/6 green (391.411 s); the affected method also passed alone (32.375 s). The legacy jq method completed all four modes green (819.716 s). These are same-build executions under the host slot gate, not sealed checkpoint receipts.

The first preview rerun failed at `RollingVolumeRecycleScriptTests.cs:839`: `RecycleGitAuditUnknown audit check=preview-helper status=127 repo=. detail=`, test duration 19.773 s. This is a new observed signature, distinct from the brief's broken pipe; diagnosis continues without changing the test.

The first C1385 rerun failed in 1.006 s with `audit refused: source differs from HEAD; commit source and pins before auditing`. The uncommitted change was this report's two citation-line corrections. This is an **investigator-created setup refusal**, not an inherited graph failure and not a copy-timeout reproduction. The refusal is retained at `c1385-alone/run.log:23`; commit the documentation correction before the clean rerun.

## Confirmed fresh preview environment failure

The unmodified method failed twice: first 19.773 s test time, then 11.665 s with a Node diagnostic preload that only records the existing `spawnSync` result. The underlying read-only helper returned **127 in 0.105 s**, well below its 60-second limit. Unedited `preview-child-trace.jsonl:1`:

```json
{"exe":"unshare","status":127,"signal":null,"seconds":0.105,"stderr":"unshare: failed to execute chroot: No such file or directory\n","stdout":""}
```

`scripts/fixtures/c1008-fake-docker.sh:237` invokes bare `chroot` via `unshare`. `command -v chroot` fails on the inherited child PATH, although `/usr/sbin/chroot` exists, root:root, mode 0755, 48,112 bytes, SHA-256 `dc44f0ec5a5f7d2a881061b047b7bbd8e9de3b41db5daadbd672ebdbf24190b6`. The fake helper saves stderr in `preview-helper-stderr` (line 251), then exits 127 without forwarding that stderr to the production wrapper's error file (line 253). That accounts for the empty `detail=` diagnostic.

Appending `/usr/sbin:/sbin` to **only the test child's** inherited PATH made the exact method pass 1/1, 71.127 s test-run duration / 73.425 s wall. No executable was installed or changed; no assertion, safety bound or production setting was altered. The subsequent C1008 class-family run uses that same explicitly qualified child PATH. The earlier reported broken pipe remains unconfirmed: the new failure is a different, now explained signature.

C1385 clean-tree execution passed 1/1 (31.616 s wall), then the two C1385 methods in its class passed 2/2 (83.895 s wall). No 5-second copy refusal was reproduced. The supplemental graph-safety run initially selected zero tests (exit 8): its OR operands lacked the wildcard suffix required by pinned TUnit discovery, as documented in `docs/testing-and-build.md:1121`. That invocation is preserved as a selection error, not a passing check; a canonical-filter rerun follows.

The corrected graph-safety filter executed 9/9 passed (3.602 s wall). The remaining RollingVolumeRecycleScriptTests methods are being checked in a separate bounded group to complete that class without one long serial invocation. Its first launch omitted the known child PATH prerequisite; the exact owned test host (PID 1412) and descendants were stopped deliberately after 81.393 s, the still-owned build-slot wrapper exited 137 and released its lease. `rolling-other-methods/intentional-stop.json` records that setup correction. No TRX or passing credit is claimed for that interrupted attempt; the qualified restart is `rolling-other-qualified`.

At 02:53:43 UTC the naturally occurring host load average reached 24.68 (5-minute 15.85; 15-minute 12.08). One additional exact C1385 run is justified by the still-unreproduced historical copy refusal at about load 21; the tree is committed first and the five-second ceiling is retained. This is a diagnostic replay, not a repeated qualification gate.

## Final execution ledger

One build served every row. Wall time below includes driver startup and slot admission; every listed executed row was granted a lease with 0 s recorded slot wait. Raw row names are directories under the evidence root; each completed row retains its exact filter, run source SHA, counts and per-case durations.

| Row | Executed | Passed | Failed | Skipped | Driver exit | Wall seconds |
|---|---:|---:|---:|---:|---:|---:|
| `c1105-alone` | 1 | 0 | 1 | 0 | 2 | 4.352 |
| `c1364-alone` | 1 | 1 | 0 | 0 | 0 | 32.375 |
| `c1008-legacy-alone` | 4 | 4 | 0 | 0 | 0 | 819.716 |
| `c1364-class` | 6 | 6 | 0 | 0 | 0 | 391.411 |
| `c1105-audit-family` | 165 | 164 | 1 | 0 | 2 | 182.078 |
| `c1008-preview-alone` | 1 | 0 | 1 | 0 | 2 | 22.519 |
| `c1385-alone` | 1 | 0 | 1 | 0 | 2 | 3.874 |
| `c1385-clean` | 1 | 1 | 0 | 0 | 0 | 31.616 |
| `c1008-preview-trace` | 1 | 0 | 1 | 0 | 2 | 13.754 |
| `c1008-preview-qualified-path` | 1 | 1 | 0 | 0 | 0 | 73.425 |
| `c1385-class-family` | 2 | 2 | 0 | 0 | 0 | 83.895 |
| `graph-copy-safety` | 0 | 0 | 0 | 0 | 8 | 3.738 |
| `c1008-class-family` | 19 | 19 | 0 | 0 | 0 | 1230.445 |
| `graph-copy-safety-corrected` | 9 | 9 | 0 | 0 | 0 | 3.602 |
| `rolling-other-methods` | no TRX | not credited | not credited | not credited | 137 | 81.393 |
| `rolling-other-qualified` | 58 | 58 | 0 | 0 | 0 | 736.517 |
| `c1385-natural-load` | 1 | 1 | 0 | 0 | 0 | 31.134 |

The five failing test executions are two C1105 resume witnesses, two preview executions with the unqualified inherited PATH, and one investigator-created dirty-report C1385 refusal. The zero-test filter and deliberately stopped residual shard are separate, uncredited setup attempts. No all-green claim is made. The definitive qualified Rolling partition executed **77/77 passed, 0 skipped, 35 distinct methods, 0 overlapping case names**, matching all 35 declared test methods in its source file. Its two drivers took 1,230.445 s and 736.517 s; their union wall interval was 22m22.540s. The legacy failing-shell arm passed alone in 179.869 s; all four modes in class passed in 190.992–205.927 s.

The natural-load C1385 replay passed 1/1 in 31.134 s wall. Its 31 one-second samples were load-average 13.89–17.96: the host had already fallen below the earlier 24.68 observation and the brief's approximately 21. It does not reproduce the historical copy timeout or prove why that copy was slow. The unchanged copy refusal and non-overridable clock guard tests passed 9/9.

## Scope, provenance and next stage

All production, definition, test and fixture files are unchanged from assigned rc `ade54222543381e2e1b31ad79d8ae33a4035f70b`. One isolated `UseAppHost=false` build at documentation commit `904fc29506647556599617d7498032dd9428d1f3` produced the DLL used by every .NET run; SHA-256 `f2f50859c145c594c739a63ee46007a6e1e269044d006249c89fcadc16b26d9a`. Build: 152 s, 1,380 warnings, zero errors, lease wait 0 s. Runtime: Debian 12 x64, .NET 9.0.22, SDK 10.0.403, Node 22.23.4, Git 2.47.3, TUnit 1.44 / Testing Platform 2.2.2.

Historical review provenance is `c82ec737bcbd3e510aac09677963786eacd34ecf:.antiphon/task-b5b016a6.md` and `421151ebee78b30bdc969521b5c5a92b2e42b75f:.antiphon/task-8ef7bb9e.md`. Their raw worktrees were absent, so only the committed reports were retrieved. New evidence is under `/work/worktrees/task-1ada4d4c/.antiphon/investigation-1ada4d4c/`; generated TRX/JSON/logs remain ignored. Essential failure text is embedded above.

The brief's full-class follow-up is bounded here to the relevant families in RemoteScriptContractTests (C1105 Git-audit methods), LandNonCodeSkipTests (C1385 methods); C1364 ran its full class. RollingVolumeRecycleScriptTests ran its full class split into the C1008 family and the remaining named method families. These are investigation witnesses, not full-suite qualification. The previous Remote class ran in five shards totaling about 48 minutes, and the previous Rolling class took 33m16s in round 2; a serial broad replay was avoided within this 75-minute investigation, with Rolling partitioned into two concurrent bounded groups. No new full-lane run or graph approval regeneration was attempted. The supplemental graph safety witnesses prove the refusal decision and rejection of clock overrides; they do not reproduce host load or measure native copy p95.

Per-test membership, durations, start/end host load and build-slot output are retained in each run's `run.trx`, `meta.json`, `run.log` and timing JSONL. At most three of this task's test drivers overlapped (the two bounded Rolling groups plus the short natural-load C1385 replay), each with its own host lease. No artificial host load was introduced. Script child-process and IO subphases are not instrumented by these test helpers: their elapsed time remains unattributed remainder, not measured zero. Samples are provisional observations, not a measured p95. No future timeout recommendation is derived from them.

The confirmed C1105 fixture mechanism is ready for **Plan**. Original C1364 exit 139, legacy failing-shell failure and preview broken pipe still need their first failing child output/trace (and crash evidence for 139); the C1385 copy refusal needs contemporaneous copy/scheduling/IO timing to distinguish its underlying delay. Those facts cannot be recovered from a successful rerun. The current runner's separate missing-chroot failure is confirmed and reproducible through child PATH qualification. No claim that rollout safety is fully certified follows from this investigation.

The task branch is **not cleanly mergeable to current master**. After `git fetch origin master`, `git merge-tree --write-tree` returned 1 against master `0cb96a0efcdad7848b431bb294e028e745b5dc10`, with the same four conflicts at both the assigned base and report tip `316dd883cb1e644f13aa3cd6cde1f29feef8e969`: `.antiphon/task-af2b78c8.md`, `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`, `server/Application/Services/CapacityRecoveryService.cs`, and `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`. These are inherited rc/master divergence; no merge, rebase or resolution was performed. The caller owns target selection and landing.

## Reproduction commands

These are diagnostic runs under the required lease gate; `dotnet exec` uses the single prebuilt DLL and does not build. The output directories from this investigation have been removed, so rebuild before rerunning.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1105-investigate-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-i1ada4d4c/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1105-resume -- dotnet exec tests/Antiphon.Tests/bin-i1ada4d4c/Antiphon.Tests.dll --treenode-filter '/*/*/RemoteScriptContractTests/C1105_Git_audit_resume_preserves_proof'
PATH="$PATH:/usr/sbin:/sbin" pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1008-preview -- dotnet exec tests/Antiphon.Tests/bin-i1ada4d4c/Antiphon.Tests.dll --treenode-filter '/*/*/RollingVolumeRecycleScriptTests/C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt'
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1385-check -- dotnet exec tests/Antiphon.Tests/bin-i1ada4d4c/Antiphon.Tests.dll --treenode-filter '/*/*/LandNonCodeSkipTests/C1385_RegistryMatchesNarrowGraph'
```

C1385 requires a clean committed tree; even an uncommitted report edit refuses before evaluation. The checked-in source and all safety deadlines remain unchanged.

## Evidence retention and cleanup

All owned foreground runs were joined before cleanup. Exactly **34** alternate-output directories named `bin-i1ada4d4c` were removed using the recorded literal paths; the pre-build inventory contained zero and the final remaining count is zero. No shared output directory or unrelated scratch was deleted. Generated evidence stays ignored; `cleanup.json` and `created-output-inventory.json` retain ownership and exact paths. The deliberately stopped fixture's unknown temporary residue was not guessed at or deleted.

| Retained row | TRX SHA-256 | Log SHA-256 |
|---|---|---|
| `c1105-alone` | `f5b5189e0c2541cf99a41a83c419dd6e787315d7dc7c2d6021664f9b74a61295` | `b6b6ef797cf127387c891cb0abf1d9c1f4483edefd1f7ea446a61fbdd96c5fc5` |
| `c1364-alone` | `0ccaa8e85aa83d9e0e1323d4c6e86f893c0b5364bbdb75380b92e3550d7acb14` | `da4382b3fc70cd6f54c9621c078604b4cbefbf0851f179afd7084fe10990d9e4` |
| `c1008-legacy-alone` | `a07deb61c355235a5f3808b33c177cfada7e9cb55a978dedc660fb01079c890b` | `f2519c86ac611256873f0e38b95c8c55ab75de77b3236c9ffc9006018fa0e098` |
| `c1364-class` | `3628da3569b3efb5bc398c362c09fbc4b024c115e70e7fd60564cfbf32fcc5e4` | `16ba45f1d8f61df886c83c0fe81e08cf43452eff584794005d2ebd3502d3c8a9` |
| `c1105-audit-family` | `9ce144bbf2028e2d6129cd076f5facdb920c160c25a52e19ae4772ff517d871b` | `758c50ab730c0bb414e3a91efdc1e4b1751daf3025427d585ef8ebe912e564a3` |
| `c1008-preview-alone` | `b7b8b186350402e39443393603dccaa49e9e95c4677b987a5aee1bff756358d8` | `4170ed42643c6caf8cb4fd7f11b2c136ecfd4139e76f90f8146841212ca905d4` |
| `c1385-alone` | `8f17a512b944d137e28cff715f5197c59471b819bd3bb4632dcc95dd4a7e88cb` | `20c1f0590ebb62dc2303bfccfc3e0321c899d8fcbca1d09a02b60589eeff36a3` |
| `c1385-clean` | `b342a9908a10ec96947425c564ea561fe336330028adcfa9c34cb909624c5d9c` | `60863e4b1c217ddf12f2d7235a1f50f6748653be24f0403bfd72226aa763828e` |
| `c1008-preview-trace` | `93093cc9e4317c85425f486911239809a573a4a8296bfaaf258dccb021013caf` | `8f1afb03745d628482635d8bd721b599c93d1cf136efa4feb0d7b4dc80d857a3` |
| `c1008-preview-qualified-path` | `b695150d70b521d162d7577dbb9e39e6c505e3dd3eb2cc6417f7bedeaeb0be7a` | `3b2aafd1e988cda30fa0503554fc067bce3e1a93ac5b8242231de9aa27f81b83` |
| `c1385-class-family` | `7f3f8bd8760e1fb1b705080da239b0eb99650801b3d19a4c651e0938d50c9301` | `8073b27c440a9dcd9fdf1e150eaf0585ad25dd77aa2465ef206b59baa83af23a` |
| `graph-copy-safety` | `20bb48f1eb9854f33ff29946784a4ed9d295d415f750af5e594dcc37b070236f` | `86bfb2205fb15355f1c62ae254bd346b3c85ed7e5d8f68bdd08a894c00a2d222` |
| `c1008-class-family` | `ce0d18dc00731d0576b15a1e522502f930987c6d172b9a89c6c6c7b20accd6f8` | `29c7ac9edf5539def9fce09bd5b81e77c4c0b0cb44e4aca8b672609244789301` |
| `graph-copy-safety-corrected` | `9e1d05dfe54c20f71f28e280c2dfd833abb982205109b649f7fcbc366b1b04e5` | `2931323031226e5f0ce57c0aa83af315cb09e5961089905ed97578602d87bf44` |
| `rolling-other-qualified` | `d01a8f1d1865d9211e791fc8a0bdf41bed2022113eb54b71861903bb7fdc22ec` | `315055dda537e2076954ba4f93e969dcf98c7f8e7a7fb370413be088b51e7af8` |
| `c1385-natural-load` | `5ab9be56916fc4036fe5f83d86d29b947a7ca8fab536e7b032d8ed22387f982e` | `87096bf005798b7b789169e0c51a00a880758c641d685850c3ca2bf40235ce83` |

Pushed evidence slices before this final ledger commit:

```text
904fc29506647556599617d7498032dd9428d1f3 docs: record C1105 and load-flake investigation provenance
fecd88d02eef6e48d88c0f3db55b2e3660e92360 docs: confirm C1105 resume fixture dependency regression
e4828931e7ad4e7f40bc4777bf6c65bc2ec49cd5 docs: separate load-failure evidence from timeout hypotheses
ba78297990228b965b89e4e6a9553b7d36e34ec2 docs: record scoped test outcomes and fresh diagnostic signatures
316dd883cb1e644f13aa3cd6cde1f29feef8e969 docs: confirm preview child prerequisite failure and clean graph result
923f09591d1de164c0ceddc61dc97c3369290dbc docs: retain graph guard results and bounded follow-up provenance
```

--- next stage ---
next: plan
handoff: Plan the confirmed C1105 resume fixture dependency repair; retain the proof-preservation assertion and five-second graph-copy ceiling. The separate preview status-127 failure is child PATH/chroot. Original exit-139, legacy and broken-pipe incidents still need raw child evidence.
artifact: docs/investigations/2026-10-30-card-1105-c1105-red-on-master-and-the-load-flakes-classify-and-fix.md
