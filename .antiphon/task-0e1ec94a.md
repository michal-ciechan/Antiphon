# Review 0e1ec94a: land-graph audit round 3 (subject c9fe095a)

I reviewed `52237731c25145abb43a58a4c727e3b7fc0180f8` on server2 Linux. That is the tip of `feat/card-task-c9fe095a`, with code commits `653e01c86` and `f038db25f`. The base is `8004259c7`; the landing target is rc `91b45006b`.

**Result: 1 defect (D-1, fail-open class), 6 advisories.**

- The round-2 fail-open is closed. `-WitnessClock` and every tool witness, hold and touch flag are gone.
- The refreshed approval records reproduce byte-for-byte.
- The delete guard holds in every probe I ran.
- The kill and timeout paths fail closed.
- One production-script mode still exits 0 on `-Check` and `-Update` without any evaluation (D-1).

**Scope is Interim.** The brief commissioned Interim, so the whole Unit lane was not run. PCs stay pending. There is no checkpoint-tool receipt, because the brief ordered build-slot + `dotnet exec`.

## Evidence

**Build.** I built a scratch clone at the tip through `scripts/build-slot.ps1`.
- Lease `590fa182`: `dotnet build tests/Antiphon.Tests -c Debug --property:UseAppHost=false --property:OutputPath=bin-review/`. 0 errors, 1368 pre-existing warnings, 223 s.
- Lease `9b65e7a2`: a Release build of the tool alone on the documented default path, `--property:OutputPath=bin-land-audit/Release/net10.0/`. 0 errors, 0 warnings. `Antiphon.LandGraphAudit.Core.dll` was copied next to the tool.
- A default `-Check` with no `-ToolPath`, using that output, approved.

**Targeted run.** Lease `cf53c5d8`, 22m41s:

```
dotnet exec bin-review/Antiphon.Tests.dll --treenode-filter "/*/*/(LandGraphEvaluationTests*)|(LandNonCodeSkipTests*)|(LandGraphAuditCleanupTests*)|(RcRosterRepairTests*)|(WindowsUnitPlatformAdmissionTests*)|(C1514LandingSplitBudgetTests*)/*" --maximum-parallel-tests 6
```

- **Total 255: 254 passed, 0 failed, 1 NotExecuted.** The NotExecuted test is `C1210_Windows_linux_only_outcomes_are_exact`, which is Windows-only.
- Per class:
  - LandGraphEvaluationTests 126. The partial classes Canary guard, Clock, FailOpen and Script are all inside this count.
  - LandNonCodeSkipTests 111
  - LandGraphAuditCleanupTests 5
  - C1514LandingSplitBudgetTests 5: C1404, C1577, C1584, C1410 and G2865
  - RcRosterRepairTests 3
  - WindowsUnitPlatformAdmissionTests 4 passed, including `C1210_Roster_closure_matches_source_gates`
- `C1385_RegistryMatchesNarrowGraph` passed. Adding the Core project needs no registry or census teaching.
- All 19 new tests passed, counting `[Arguments]` cases. That includes `RefusingFixtureRejectsEveryRemainingFlag`, `EachCanaryGuardIsNecessary`, `RecordedCanaryDeleteRejectsUnsafeRecords` and `ProductionSurfaceHasNoWitnessClock`.

**Approval records (check 2).**
- Clone `r3` is at `f038db25f`. I put both records back to their `653e01c86` bytes and ran a real `-Update -ToolPath <tip tool>`: exit 0, `digest=841DD41B42D127CC768ABEC521683496D2C385ECFE753A4E725FEB5ADD54D052 count=66`, `phase=pass2`, `approved`.
- After that run, `git status` was empty. The regenerated `evaluation.txt` and `manifest.txt` are therefore byte-identical to the committed ones.
- The diff against base changes only three lines: the digest, the count (65 to 66) and the SDK line (10.0.402 to 10.0.403, this host's SDK). The pins SHA `749d6538...` is unchanged.
- Between `5ab632c73` and the tip, the only added `*.csproj` is `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj`.

**Real audit (check 6).**
- Clone `r3` at the tip ran `-Check`: exit 0, 38 s, `approved`.
- The sha256 over the sha256 of all 9,144 tracked files was `45dbb455...` both before and after.
- `git status` was empty, and no `antiphon-landgraph-canary-*` was left in the parent.

**Kill and timeout paths (check 4).** I ran `-Check -EvaluationBudgetSeconds` with 8, 22 and 28 on the approving clone.
- All three exited 1 with `evaluation-failed: evaluating Antiphon.sln exceeded N s` and `land graph evaluation refused (exit 2)`.
- The 22 and 28 runs were killed after `phase=pass2`, with their canary recorded.
- Every canary was removed, and `git status` stayed empty.
- I grepped for `CreateHardLink`, `link(`, `DllImport` and `LibraryImport` in both tool projects and the script: no matches.

**Evidence diff.** `check-evidence-diff.ps1 -Repository . -BaseRef 8004259c.. -HeadRef 52237731c..`: commits=5, entries=1, violations=0.

**Landability.**
- `merge-tree` against `origin/rc/2026-10-27` (`91b45006b`): clean.
- Against `origin/master` (`f8bddb6ef`): it conflicts in the 10 files the author's report lists, because the branch has rc-only ancestry. Master needs its own pick or re-cut.

## Adversarial probes on the refusing fixture (check 1)

Clone `w` is the tip plus a committed docs-reading `<PackageTags>$([System.IO.File]::Exists('$(MSBuildThisFileDirectory)../../docs/orchestration-loop.md'))</PackageTags>` in the tool csproj. I refreshed its manifest and evaluation digest the same way the test does, so that runs reach real evaluation. I took the sha256 of both records before and after every run.

These all exited 1, left both records unchanged, left no canary and printed no `approved` line:

| Arguments | Reason given |
|---|---|
| `-Check` / `-Update` with `-ToolPath` | `PackageTags[1] filesystem-function` |
| `-EvaluationBudgetSeconds 1` | same refusal |
| `-EvaluationBudgetSeconds 100000` | same refusal |
| `-EvaluationBudgetSeconds 2147483647` | `TimeoutMs` Int32 conversion: refused, but a confusing message |
| `-EvaluationBudgetSeconds -1` | budget must be positive |
| `-Check -Update` | not both |
| no switch | same refusal |
| `-Check:$false -Update:$false` | same refusal |
| `-Verbose -Debug -ErrorAction SilentlyContinue` / `Ignore`, `-WarningAction` | same refusal (line 27 forces Stop) |
| `MSBUILDENABLEALLPROPERTYFUNCTIONS=1 MSBuildSDKsPath=/nonexistent` | stripped by the script; same refusal |

PowerShell prefix matching cannot reach a removed flag:
- `-Witness` and `-WitnessClock`: no such parameter, exit 1.
- `-W`: ambiguous between `-WarningAction` and `-WarningVariable`, exit 1.

The tool called directly behaves as follows:
- `--witness-clocks`, `--pass-budget-ms`, `--hold-pass1-ms` and `--touch-canary`: exit 2, `unknown or incomplete argument`.
- A response file (`@file`): exit 2.
- `--print-budget` anywhere except argv[0]: exit 2.
- The only environment read is `NUGET_PACKAGES`.
- The script decides on the exit code only. It never parses tool stdout for approval, so evaluated-project output cannot be echoed into an approval.
- `InternalsVisibleTo` exposes `AuditPhases`/`IPhaseClock` only to `Antiphon.LandGraphAudit` and `Antiphon.Tests`. No CLI path reaches the fake clock or the fake evaluator.

**Not refused: `-RemoveRecordedCanary <any> -RemoveRecordedCanaryRepo <any>`.** This is D-1.

## Delete-guard probes (check 3)

I ran these with the real tip script through `-RemoveRecordedCanary`. Every probe exited 0 and every sentinel survived:

- repo root
- empty
- whitespace
- the parent
- `/`
- a valid-named directory outside the parent
- a trailing `/`
- a trailing `\`
- uppercase hex
- `\n` injection (`<canary>\n<repo>`)
- a lone `\r`
- a short hex name
- a relative name
- `~/...`
- a valid-named symlink to a sentinel directory: only the link was unlinked, and the target survived
- a valid-named symlink to the repo: the repo was intact
- a real canary containing a symlink to a sentinel directory: the canary was removed and the target survived

Controls:
- The exact record removed the canary.
- A `..` spelling that normalises to the canary removed it.
- `-RemoveRecordedCanaryRepo /` exited 1.

**Windows, reasoned only (not run).**
- The Ordinal parent comparison refuses 8.3 short names, `\\?\` prefixes and case variants, so it fails safe and at worst leaks a canary.
- A junction canary has `ReparsePoint` set and goes through `Directory.Delete(path, false)`, which removes only the junction.
- .NET Framework's recursive delete removes nested junctions without descending into them.
- An invalid-character path makes `GetFullPath` throw inside `finally`, which turns the result into `audit refused`.

## Defects

**D-1: `-RemoveRecordedCanary` on the production script exits 0 for `-Check` and `-Update` with no pre-check, no evaluation and no output.** This is the same fail-open class as round-2 D-1.

- **Where:** `scripts/audit-land-verification-graph.ps1:24-25` (parameters) and `:201-208`.
  - That block runs before the `-Check`/`-Update` handling at `:210-270`, so any `-Check` or `-Update` on the same command line is ignored and the script ends in `exit 0`.
  - The parameter was introduced in round 2 (`5ab632c73`) and is part of this review's diff against base `8004259c7`.
  - `RefusingFixtureRejectsEveryRemainingFlag` (`tests/Antiphon.Tests/Application/LandGraphEvaluationFailOpenTests.cs:37-62`) covers only `-EvaluationBudgetSeconds` and `-ToolPath`, so the claim "every remaining flag exits non-zero" is not tested for these two parameters.
- **Failure (reproduced on the refusing fixture `w`):**
  - A plain `-Check -ToolPath <tool>` exits 1 with `filesystem-function`.
  - These all exit **0** in under 1 s, with empty output:
    - `-Check -ToolPath <tool> -RemoveRecordedCanaryR y -RemoveRecordedCanary x`
    - `-Check -RemoveRecordedCanary /nonexistent -RemoveRecordedCanaryRepo /nonexistent`
    - `-RemoveRecordedCanary x -RemoveRecordedCanaryRepo y`, which with no switch defaults to check mode
  - `-Update ... -RemoveRecordedCanary x -RemoveRecordedCanaryRepo y` also exits **0** and reports success, but publishes nothing. The records are unchanged, so a caller believes they were refreshed.
- **Why:** `-Check` exit 0 is the acceptance signal that every earlier stage (V-7 "real-repo acceptance") and the brief treat as approval. Any agent or test that passes these flags gets approval for a graph that real evaluation refuses. That violates "-Check ... always evaluates" (script header `:3`) and this round's invariant that every remaining flag on the refusing fixture exits non-zero. The records are not rewritten, so the land digest itself is not corrupted; this is a false pass of the gate, not a forged record.
- **Fix (one of):**
  - (a) Refuse the combination: if `-RemoveRecordedCanary` or `-RemoveRecordedCanaryRepo` is set together with `-Check`, `-Update`, `-ToolPath` or `-EvaluationBudgetSeconds`, print `audit refused: ...` and exit 1. Also make the removal-only mode exit with a distinct non-zero code, for example 3, so it can never be read as an approval.
  - (b) Move the removal entry point out of the production script, for example into a `scripts/lib/` helper that the tests dot-source, so the audit script keeps only `-Check`/`-Update`/`-ToolPath`/`-EvaluationBudgetSeconds`.

  Add rows to `RefusingFixtureRejectsEveryRemainingFlag`:
  - `-Check` and `-Update`, each with `-RemoveRecordedCanary <record> -RemoveRecordedCanaryRepo <repo>`;
  - the removal flags alone.

  Each row must give exit != 0, no `approved`, and byte-identical records. Commit the test red first.

## Advisories

- **A-1: `PhaseClock.Bound` starts `result = 0`, which equals `Approved`** (`tools/Antiphon.LandGraphAudit.Core/PhaseClock.cs:18`).
  - Correctness therefore depends entirely on `WallClock.TryComplete` never returning true for unfinished work. Today it is correct: `task.Wait(budget)` returns true only once the task has completed.
  - But a clock bug would fail open. Pass 2 would run against a baseline that pass 1 is still filling.
  - `WallClock` (`tools/Antiphon.LandGraphAudit/ToolClocks.cs`) has no direct test, because the library tests use `ManualPhaseClock`.
  - Suggest initialising `result` to a non-approved value such as `2`, and moving `WallClock` into Core behind a cancel callback so it can be unit-tested.
- **A-2: The tool's `--repo X --solution Y --api-probe` exits 0 after only an API probe, without evaluating** (`Program.cs:36`, `:64`, `:85`). This predates the chain; the base has it.
  - The script never passes it and cannot be steered to.
  - A distinct exit code would make this independent of the caller.
  - The same reasoning applies to `--print-budget` exiting 0, which round 1 already noted as A-5.
- **A-3: `tests/rc-project-categories.json` has no entry for `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj`.**
  - The advisory RC project selection adds `unknown-project:` for it and falls back to Full.
  - That is harmless today. The selection already falls back to Full at base for 6 other unknown projects, and it is shadow-only (`effectiveMode=Full`).
  - Add a `tooling` entry beside its sibling. Nothing else needs to learn about the project:
    - It is not in `Antiphon.sln`, as designed. Its sibling is not in the solution either.
    - The Dockerfile, the AppHost and the packaging scripts do not reference it.
    - The tool ProjectReference builds it.
- **A-4: `-RemoveRecordedCanaryRepo` with a trailing separator deletes a canary-named directory inside the repo.** `GetDirectoryName("<repo>/")` returns the repo itself.
  - The production path passes the separator-free `$RepoRoot`, and the name gate still applies, so this is harmless.
  - The fix for D-1 option (b) removes this surface anyway.
- **A-5: Windows is unrun.** The following still need a native run:
  - PS 5.1 `-File` exit code on an unknown parameter. pwsh gives 1; this was not checked on 5.1.
  - The junction-canary delete.
  - The provisional 12 s Windows copy budget and the 120 s pass floor. Both are marked PROVISIONAL in `EvaluationBudget.cs` and `docs/orchestration-loop.md:2378-2387`, which match the code.
  - C1385 on the desktop.
- **A-6: Very large `-EvaluationBudgetSeconds` values (for example 2147483647) refuse with a raw `TimeoutMs` Int32 conversion message.** The behaviour is fail-closed. A range check with a plain message would read better.

## Notes

- Following the standing authority, I proceeded without asking. No desktop failure occurred, so it did not apply.
- **Scratch left in place.** Removing it would need a recursive delete, which the cleanup rule forbids. It is under `/tmp/claude-1654/-work-worktrees-task-0e1ec94a/f8b5707d-53e1-4339-ac9d-82f2432d0cd0/scratchpad/`:
  - `repo/`: the tip clone, with `bin-review` outputs;
  - `w/`: the refusing fixture;
  - `r3/`: the refresh and audit clone, with `bin-land-audit`;
  - `del/`: the delete probes;
  - `trx/`.
- No `bin-*` was created in the assigned worktree.
- No canary was left anywhere.
