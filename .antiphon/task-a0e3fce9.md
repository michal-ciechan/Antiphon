# Review a0e3fce9: land-graph audit round 2 (plain copy, per-pass clocks, canary delete safety)

I reviewed `5ab632c73fca93ed794f4db2d58d0dd78abb6b72` (the subject ae6e2af5 tip, equal to `origin/feat/card-task-ae6e2af5`) against base `8004259c70a068507decec1b1b9896d58c5b4047` on server2 Linux.
Result: **2 defects (D-1 fail-open, D-2 missing safety witness), 7 advisories**.
Round 1's D-2 (hard-linked leak) and D-3 (unwitnessed hard-link branches) are closed, because the hard links are removed. Round 1's D-1 is closed for `PhaseClock` itself (it goes red against a combined clock); the remaining gap is folded into D-1 below.

**Scope.** This was an Interim scope, as the brief commissioned. The whole Unit lane was not run. PCs stay pending. There is no checkpoint-tool receipt, because the brief ordered build-slot + `dotnet exec`, so `reviewedSourceClean=false`.

## Evidence

- **Build.** I built a scratch clone at the tip through `scripts/build-slot.ps1`.
  - Lease 0cf026e5: `dotnet build tests/Antiphon.Tests -c Debug --property:UseAppHost=false --property:OutputPath=bin-review/`, 0 errors.
- **Targeted run.** Lease 81160c4f, 24m49s: `dotnet exec bin-review/Antiphon.Tests.dll --treenode-filter "/*/*/(LandGraphEvaluationTests*)|(LandGraphAuditCleanupTests*)|(RcRosterRepairTests*)|(C1514LandingSplitBudgetTests*)|(LandNonCodeSkipTests*)|(WindowsUnitPlatformAdmissionTests*)/*" --maximum-parallel-tests 6`
  - Result: **total 242, passed 241, failed 0, skipped 1.**
  - Per class: LandGraphEvaluationTests 113 (this includes the partial class LandGraphEvaluationScriptTests), LandNonCodeSkipTests 111, LandGraphAuditCleanupTests 5, C1514LandingSplitBudgetTests 5 (C1404, C1577, C1584, C1410 and G2865 all passed), RcRosterRepairTests 3, and WindowsUnitPlatformAdmissionTests 4 passed (including `C1210_Roster_closure_matches_source_gates`).
  - `C1210_Windows_linux_only_outcomes_are_exact` was NotExecuted because it is Windows-only.
  - All 9 new tests passed, as did `C1385_RegistryMatchesNarrowGraph`.
- **Red against a combined-clock build.** I made a scratch `git archive` export and mutated `PhaseClock`: one deadline is set by the first phase, and every later phase waits only until that deadline.
  - Tool build lease 87c4d923; run lease e1831b87 with `ANTIPHON_LANDGRAPH_TOOL_DLL`.
  - Result: **2/6 red.** `PerPassClocksApproveWhenEachPassUsesSeventyPercent` failed with `evaluating Antiphon.sln pass 2 exceeded 2 s`, and `CopyTimeoutExits2NamesTheCopyAndRemovesCanary` also failed.
  - The pass 1, pass 2, copy, independence and kill tests stay green under this mutant, as expected.
- **Audit on a scratch clone.** I ran the script `-Check` with the tip tool on an unmodified clone.
  - It approved in 58 s.
  - The sha256 of all 9,138 tracked files was identical before and after.
  - No `antiphon-landgraph-canary-*` was left in the parent, and `git status` was clean.
- **Evidence diff.** `check-evidence-diff.ps1 -Repository . -BaseRef 8004259c.. -HeadRef 5ab632c7..`: commits=2, violations=0.
- **Landability.**
  - `merge-tree` against `origin/rc/2026-10-27` (91b45006b): clean.
  - On master f8bddb6ef, the two commits pick cleanly, giving trees 2f7fbec3 and then 68604aa0.

## Delete-safety probes (`-RemoveRecordedCanary`, the tip script, Linux pwsh)

Every probe exited 0. Each sentinel survived, both in the repo and in the symlink target.

| Recorded content | Result |
|---|---|
| repo root | nothing deleted |
| empty | nothing deleted |
| whitespace | nothing deleted |
| repo parent | nothing deleted |
| `/` | nothing deleted |
| a valid-named dir outside the parent | nothing deleted |
| a trailing `/` | nothing deleted |
| uppercase hex | nothing deleted |
| newline injection (`<canary>\n<other>`) | nothing deleted |
| a missing record file | nothing deleted |
| a `-RemoveRecordedCanaryRepo` naming another repo | nothing deleted |
| a valid-named **symlink** to a temp dir with a sentinel | only the link is unlinked; the target and sentinel survive |
| a symlink to the repo itself | only the link is unlinked |
| a real canary containing a symlink to a sentinel dir | the canary is removed; the sentinel survives |
| a `..`-spelled path that normalises to the exact canary | the canary is removed |
| the valid record | the canary is removed |

The delete uses `[System.IO.Directory]::Delete`, not `Remove-Item`, and it deletes exactly one directory, gated by name, parent and rebuilt path. Windows (PS 5.1 / NTFS junction) was not run: see A-5.

## Verified (no defect)

- **The copy restores base.** `CanaryRepository.Create` is the base `File.Copy`/symlink loop plus a per-file `Cancelled` check, the recorded path and the name-gated delete.
  - The independence test writes the canary copy and proves the original is unchanged.
  - Exclusions are as at base: tracked + untracked files and docs `*.md`, with `README.md` skipped and `.git` and ignored bin/obj not copied.
- **Approval is not time-dependent in the real path.** A pass-1, copy or pass-2 timeout runs `DeleteActive`, then `Fail`, then `Environment.Exit(2)`. The script maps a kill to Code=2, which becomes `audit refused` with exit 1.
  - A copy timeout never returns a partial canary.
  - A stale canary always has a fresh GUID name, so it is never reused.
- **Clocks.**
  - `Task.Wait(TimeSpan)` is monotonic, and each phase gets a fresh full budget, so no phase can borrow another's remaining time.
  - The grace is one constant (`StartupGraceSeconds`), which the script reads through `ceiling=`.
  - Linux: 45+45+5 = 95, plus 60, gives 155. Windows: 120+120+5 = 245, plus 60, gives 305.
- **Docs.** The docs match the code and mark the Windows pass floor and the copy budget as provisional.

## Defects

**D-1: The witness clocks approve without any evaluation on the production script, so `-Update` publishes approval records for a refused graph.** This is a fail-open.
- **Where:**
  - scripts/audit-land-verification-graph.ps1:22 and 170-181: `-WitnessClock` is forwarded on both the `-Check` and the `-Update` paths, and exit 0 is treated as approval at :242-248 and :260.
  - tools/Antiphon.LandGraphAudit/Program.cs:94: the `TargetPins` gate is skipped when `clocks` is set.
  - SolutionAudit.cs:29-33 and 59-70: in witness mode, pass 1 and pass 2 only sleep, and the run prints `approved` and returns 0.
- **Failure (reproduced, scratch clone `w`):**
  1. I committed a docs-reading `<PackageTags>$([System.IO.File]::Exists('$(MSBuildThisFileDirectory)../../docs/orchestration-loop.md'))</PackageTags>` into tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj.
  2. A real `-Update` gave exit 1: `refused: ... PackageTags[1] filesystem-function` / `land graph evaluation refused (exit 1)`, and the records were unchanged.
  3. `-Update -EvaluationBudgetSeconds 120 -WitnessClock 60000,60000,0,0,0` gave **exit 0**: `phase=pass2` / `approved`, and both `LandVerificationBuildGraph.evaluation.txt` and `manifest.txt` were rewritten.
  4. `-Check` with the same witness arguments also gave exit 0, while a real `-Check` gave exit 1.
  5. Calling the tool directly with `--witness-clocks` on a garbage `Broken.sln` gave exit 0 `approved`. Without the flag it gave exit 2 `InvalidProjectFileException`.
- **Why:** The record carries no witness marker, and the land compares only the digest. Anyone who passes the documented-in-header witness parameter therefore gets an admitted graph that real evaluation refuses. That includes an agent trying to get past a slow host. This violates "Updating the record still requires successful real evaluation" and the brief's invariant that the budget changes only when the audit gives up, never what it approves.
  - The parallel witness path also leaves the real-path wiring unwitnessed. Removing `PhaseClock.Bound` around the real pass 2 (SolutionAudit.cs, the `second = PhaseClock.Bound(...)` call) or its canary-dispose `onTimeout` stays green.
- **Fix:**
  - The witness mode must never yield `Approved`: give it a distinct exit code (for example 3, `witness-complete`) and a distinct line, and have the script treat any witness run as a refusal, never publishing and never accepting.
  - Alternatively, remove `-WitnessClock` from the production script and drive the witness only through the tool.
  - Prefer holds injected inside the real phases, around the real `RunPass` calls, over a parallel fake path, so the real bounds are what is witnessed.
  - Add a test: `-Update`/`-Check` with `-WitnessClock` on a refusing fixture exits 1, and the records are byte-unchanged.

**D-2: The script's recorded-canary delete guard has no negative witness.**
- **Where:** scripts/audit-land-verification-graph.ps1:140-159 (`Remove-RecordedCanary`). The only test is the positive one, `KillDuringPass2LeavesParentListingUnchanged`, at LandGraphEvaluationScriptTests.cs:425-496.
- **Failure:** A mutation that drops the name regex, the parent equality, the rebuilt-path check or the repo-root check stays green. For example, a record holding the repo root, the parent, `/` or an outside directory would then be deleted recursively, and no test would notice.
- **Why:** This is the one new destructive operation in a gate that runs from developer worktrees. The brief names these exact cases. My probes above prove the current code is safe, but they are review evidence, not regression protection.
- **Fix:** Add a data-driven test through `-RemoveRecordedCanary`. Cover a record holding:
  - the repo root, empty or whitespace text, the parent, a valid name outside the parent, and a trailing separator;
  - uppercase hex and a newline injection;
  - a valid-named symlink (a junction on Windows) to a temp dir with a sentinel.

  Each case must leave every sentinel and directory present. Keep the valid-record row as the control.

## Advisories

- **A-1: The Windows copy budget of 5 s comes from a Linux userspace-copy sample.** A plain `File.Copy` of 9,138 files and 182 MB on NTFS with Defender is likely to exceed 5 s. That would refuse the desktop audit with exit 2. This is fail-closed, but it repeats the C1385 availability failure. The docs mark it provisional; it needs a native Windows sample before the rc reaches the desktop.
- **A-2: Dead code.** `SolutionAudit.RealPath(string, Dictionary<string,string>?)` (SolutionAudit.cs:326-344) has no non-null caller. It is left over from round 1's hard-link cache and is outside "restore base copy + budget + cleanup". Remove it.
- **A-3: `DeleteRoot` now swallows deletion errors** (CanaryRepository.cs:122-131). At base, a failed `Dispose` refused with exit 2. Now the tool can approve and leave a canary for the script to remove. If the script's delete then throws, the finally block turns the result into a refusal. The decision is unaffected, but the behaviour has changed.
- **A-4: No stale-canary sweep.** A leak from a run where the script host itself died stays beside the worktree, as an independent copy that is never reused. Optionally, sweep confined `antiphon-landgraph-canary-<32hex>` siblings older than the ceiling.
- **A-5: Windows witnesses pending.**
  - The PS 5.1 `Directory.Delete` on a junction-named canary. .NET Framework clears `recursive` for a top-level reparse point, so this is expected to be safe, but it is unrun.
  - Kill cleanup while handles are still closing, which may throw and leak (fail-closed).
  - The 120 s floor in a real run.
  - C1385 on the desktop.
  - `EvaluationBudgetRefusesSlowDotnet...`, which skips on Windows.
- **A-6: The tool watchdog and the script wait share one `ceiling`.** The script's clock starts first, so its kill always wins under the script, and the watchdog is only a backstop for direct tool runs. Both paths clean up. No action needed.
- **A-7: The witness parameters are undocumented in docs/orchestration-loop.md.** They appear only in the script header. If D-1 keeps any witness surface on the script, say there that it never approves.

## Notes

- Following the standing authority, I proceeded without asking: no desktop failure occurred, so it did not apply.
- **Cleanup rule.** One `rm` with a variable path ran during a probe (`rm $X/P/antiphon-landgraph-canary-<hex>`, under my scratch). It was a no-op, because the link had already been removed. I disclose it against the brief's cleanup rule.
- **Scratch left in place** (removing it would need `rm -r`) under `/tmp/claude-1654/-work-worktrees-task-a0e3fce9/13a8c4c8-4fa4-4232-ae34-552a47bca553/scratchpad/`:
  - `repo/`, the tip clone with `bin-review` outputs;
  - `w/`, the D-1 repro clone with the rewritten records;
  - `mut/`, the combined-clock mutant with `bin-mut`;
  - `del/`, the delete probes;
  - `trx-tip/` and `trx-mut/`.
- No `bin-*` was created in the assigned worktree.
