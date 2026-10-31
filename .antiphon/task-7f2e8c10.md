# Review 7f2e8c10: rc-qualification skill, watchers, TRX helper (Final)

Subject: f6654e98-5bb2-4004-a03d-7d121899c19b, branch feat/card-task-f6654e98,
tip 7310cc389bf4dfc532db43f0859130aecde16f08 (base cbe3e6b42401db766f4cee38bc9b34f5b9d314af).
Host: server2 runner mirror (Linux, PowerShell 7). Read-only: the subject is unchanged.

Verdict: **found**. The new TUnit class does not compile, so the whole Antiphon.Tests
project fails to build at the tip. There are also two watcher fail-opens and one SKILL
command that cannot run.

## Defects

**D-1 (blocker): the test class breaks the Antiphon.Tests build.**
- Where: `tests/Antiphon.Tests/Scripts/RcQualificationWatcherScriptTests.cs:1`
- Failure: `using Antiphon.Tests.Application;` should be `using Antiphon.Tests.TestHelpers;`. The tip build gives `RcQualificationWatcherScriptTests.cs(7,18): error CS0246: The type or namespace name 'ProcessSpawnLimit' could not be found` (build-slot run, 1 error).
- Why: every Antiphon.Tests run, including nightly and rc, fails to build. The author never ran the test.
- Fix: swap the `using`. In a scratch copy with only that change, the build succeeds and the class passes (see Tests).

**D-2: Timeout, Error and Aborted outcomes are silently treated as green.**
- Where: `scripts/orchestration/watch-qual.ps1:41` and `scripts/orchestration/trx-failures.ps1:22` match only `outcome="Failed"`. The repo's failure set is Failed/Error/Timeout/Aborted (`tools/Antiphon.Checkpoints/Trx/TrxReport.cs:55`, `scripts/run-checkpoint.ps1:1395`).
- Failure: I ran watch-qual against a log directory holding the synthetic TRX with its failed result changed to `outcome="Timeout"`. It printed no FAILURES line. It also wrote the file to the state file (`:56`), so no later re-arm ever reports it. `trx-failures.ps1` on the same file prints `no failures`, exit 0.
- Why: a hung or timed-out test, a core rc failure family, never triggers the procedure's "dispatch immediately on every FAILURES line".
- Fix: match all four outcomes in both scripts, and add smoke rows with a Timeout fixture.

**D-3: watch-tasks drops a task that blocks or fails a second time.**
- Where: `scripts/orchestration/watch-tasks.ps1:39-43`. The dedupe key `id|status` is remembered forever.
- Failure: an offline stub probe with the sequence Working, Blocked, Working, Blocked, Failed emits TASK-BLOCKED only once. The second Blocked is suppressed. That second Blocked is exactly what happens after SKILL step 6 tells the orchestrator to `-Reply` and the task blocks again. A retried task that fails twice is dropped the same way.
- Fix: keep the last observed status per id and emit when it changes into Succeeded, Failed, Blocked or Canceled. Add a smoke row for re-block.

**D-4: the SKILL's Monitor arguments are not runnable.**
- Where: `.claude/skills/antiphon-rc-qualification/SKILL.md:33`, which says `persistent: false`.
- Failure: the Monitor tool has no `persistent` parameter, and its schema rejects unknown properties (`additionalProperties: false`). A literal call fails input validation.
- Fix: drop it, and keep `timeout_ms: 1800000`, which is the cap.

## Advisories (not blocking)
- A-1 `trx-failures.ps1:44` prints `no failures` after a TRX-READ-ERROR, and also when no path is given. Say "no failures in readable input" or suppress the line in those cases. Under `pwsh -File` only one path can be passed: a comma string is read as one path, and a second positional argument exits 1. The header promises "one or more".
- A-2 `watch-qual.ps1:76` prints HEARTBEAT-STALE on every poll, about 28 lines per window. The Monitor tool auto-stops noisy monitors, so emit on the transition instead.
- A-3 The sleep is not clamped to the deadline. A 1-minute window with 25 s polls measured 77 s. `-MaxMinutes 29 -PollSeconds 300` can run about 34 minutes, which exceeds the 30-minute Monitor cap. The defaults fit.
- A-4 `watch-tasks.ps1:14` hard-defaults to localhost. It ignores `$env:ANTIPHON_API` and the `X-Antiphon-Task-Token` header that `docs/ops-http.md` uses. Run on server2 without `-Api`, every poll errors; the error is loud, not silent.
- A-5 Heartbeat schema: the in-repo writer (`scripts/lib/qualification-supervisor-heartbeat.ps1`) writes `observedAtUtc`, not `utc`, `runId` or `supervisorPid`. The `utc` file comes from the private supervisor outside the repo, which I could not check here. If that file lacks `utc`, every poll prints HEARTBEAT-ERROR (loud). The writer also deletes before it moves the new file, so a transient HEARTBEAT-MISSING is possible.
- A-6 `watch-master.ps1:21` captures git stderr with `2>&1` while `$ErrorActionPreference` is Stop. On Windows PowerShell 5.1 that turns any git stderr warning into MASTER-WATCH-ERROR. The smoke test stubs git, so this path is not covered.
- A-7 Policy divergences from the standing rules in `docs/orchestration-loop.md`, presented as the operator's 2026-10-31 workflow. Recommend reconciling the owner doc or citing the override:
  - SKILL:95 "Test-only and other Debug repairs go directly to rc" versus Rule 9 "reviewed cherry-pick".
  - SKILL:72-73 "use Custom when Debug is full" versus Rules 1 and 2, which say to raise the cap with `dispatch-concurrency.ps1 set`.
  - SKILL:165 is stricter than Rule 10, which allows stopping an abandoned qualification.
- A-8 `test-orchestration-watchers.ps1` ignores `-Case` and `-ResultsDirectory`. It has one Smoke case, so this is harmless.

## Verified sound
- **Human-only list (SKILL:155-161) is complete:** tags, publish, promote; killing sessions; the live run and its lock/files; anything under C:\Antiphon not owned; -AllowWorktree, -KillSessions, -Hard; state volumes; private-notes; secrets.
- **Extraction pitfall** is present at SKILL:65-68.
- **Ordering policy** (failures first, oldest unfinished resume, affected lanes, planner/validator) is present. The open decisions are listed without answers.
- **Activation and release rules:** "master land is not activation" and "tags/publish/promote human-only" are stated, and the SKILL never touches the live run.
- **Commands exist.** delegate.ps1 has `-Refine`(281), `-Reply`(271), `-Land`(303), `-ExpectedSourceSha`(309), `-StartRef`(89), `-Runner`(163), `-Kind`(58: ClaudeCode/Grok/Codex), `-Level`(44: Frontier/High/...), `-Worktree`(142), and Role Debug/Merge.
- **Endpoints exist:** `POST /api/agent-tasks/{id:guid}/cancel` (AgentTaskEndpoints.cs:204), `/pipeline` (:100), `/api/cards/{id}/private-notes` (CardEndpoints.cs:141).
- **Behaviour claims hold.** Reply requires Blocked (`not_blocked`). `rc-qualification-status.ps1` and `audit-land-verification-graph.ps1` exist. build-slot runs a `.ps1` target.
- **Scripts:** ASCII-only, no BOM, no CR (byte scan of all 9 files). No hard-coded session ids, paths or secrets, and no process, lock or kill operations.
- **trx-failures behaviour:** the message is bounded by the same string's length (`Get-Prefix`), it prints 3 frames and the stdout top, and it always exits 0 (also on malformed or missing input).
- **watch-qual behaviour:** one FAILURES line per TRX across re-arms, a partial TRX is retried, stall, missing-heartbeat and hang-suspected lines appear, RUN-ENDED fires, and the window-end message prints.
- **AGENTS.md row** follows the "Change area | Required owner" convention. `check-agent-context.ps1` passes at 18,239 of 24,576 bytes.
- **Repo-wide script rules:** the `scriptCensus` (`tests/test-execution-policy.json`) is not an exhaustive disk inventory, so no repo-wide script rule fails for the new harness.

## Tests (server2, all under build-slot, TEMP=/tmp)
| Run | Tree | Result |
|---|---|---|
| Build Antiphon.Tests, UseAppHost=false | subject tip 7310cc38 | **FAIL**, CS0246 (D-1) |
| `pwsh scripts/test-orchestration-watchers.ps1` direct | tip | 18/18 PASS, 5.8 s, no /tmp residue |
| Build | scratch copy with D-1 one-line fix, not committed | OK, 0 errors |
| `/*/*/RcQualificationWatcherScriptTests/*` | scratch fix | 1/1 pass, 2.6 s |
| Red probe: trx-failures message bound 1500 to 1600 | scratch fix | 1/1 **failed**, `FAIL RCQUAL bounds the same message string and stdout`; restored |
| Guards: ProcessSpawnLimitTests 21, ScriptHarnessProcessContractTests 29, SlowTestTripwireTests 2, TestLaneCategoryGuardTests 1, RcQualificationWatcherScriptTests 1 | scratch fix | 54/54 (TRX class identities) |
| AGENTS.md/skill doc contracts: 14 classes | scratch fix | 142/142 |
| `audit-land-verification-graph.ps1 -Check` (real, Release tool) | tip | approved, digest 7BDABBA0..., count 68. Not a new build definition. `-Update` not run. |
| `check-evidence-diff.ps1` base..HEAD | tip | 3 commits, 0 violations |
| `git merge-tree --write-tree origin/master HEAD` | tip | clean |

Not run:
- watch-tasks and watch-master against the live server or remote. They were exercised only offline, with stubs and my re-block probe.
- Windows PowerShell 5.1, which does not exist on this host.

All build outputs and the scratch worktree were removed by exact path.
