# Review 39bfc274: land-graph audit round 4 (subject 80b32016)

I reviewed code commit `2c3d20a50014e21e3978efb7c761e136270091b6` on server2 Linux. The subject branch tip `d05c8834b` adds only the author's report. The base is `8004259c7`, and the rc base `01c32a59a` contains it.

**Result: no defects, 5 advisories.**
- I found no new way for the production script or the tool to exit 0 or print `approved` without a real evaluation.
- The round-3 bypass is closed.
- The delete guard holds, and each of its marked checks is individually necessary.
- The approval records reproduce byte-for-byte.
- The kill and timeout paths remove the canary and refuse.

**Scope is Interim, as commissioned.** The whole Unit lane was not run. PCs stay pending. There is no checkpoint-tool receipt, because the brief ordered build-slot + `dotnet exec`, so `reviewedSourceClean=false`. Under the standing operator rule, an Interim Review cannot approve a land on its own.

## Evidence

**Build.** I built a scratch clone at `2c3d20a50` (lease `8cf57b9a`, 133 s):
```
dotnet build tests/Antiphon.Tests -c Debug --property:UseAppHost=false --property:OutputPath=bin-review/
```
It finished with 0 errors.

**Targeted run.** Lease `4c447cd5`, 22m10s:
```
dotnet exec tests/Antiphon.Tests/bin-review/Antiphon.Tests.dll --treenode-filter "/*/*/(LandGraphEvaluationTests*)|(LandNonCodeSkipTests*)|(LandGraphAuditCleanupTests*)|(RcRosterRepairTests*)|(WindowsUnitPlatformAdmissionTests*)|(C1514LandingSplitBudgetTests*)/*" --maximum-parallel-tests 6
```
- **Total 259: 258 passed, 0 failed, 1 NotExecuted.** The NotExecuted test is `C1210_Windows_linux_only_outcomes_are_exact`, which is Windows-only.
- Per class, from the TRX:

  | Class | Result |
  |---|---|
  | LandGraphEvaluationTests (all partial classes) | 130 |
  | LandNonCodeSkipTests | 111 |
  | LandGraphAuditCleanupTests | 5 |
  | C1514LandingSplitBudgetTests | 5: C1404, C1577, C1584, C1410, G2865 all passed |
  | RcRosterRepairTests | 3 |
  | WindowsUnitPlatformAdmissionTests | 4 passed, including C1210_Roster_closure_matches_source_gates |

- These new tests all passed:
  - RefusingFixtureRejectsEveryRemainingFlag
  - ToolArgumentAllowlistIsClosed
  - ClockThatSkipsWorkCannotApprove
  - ProductionWallClockApprovesOnlyFinishedWork
  - EachCanaryGuardIsNecessary
  - RecordedCanaryDelete* (2)
- C1385_RegistryMatchesNarrowGraph also passed.

**Other checks.**
- `check-evidence-diff.ps1 -Repository . -BaseRef 8004259c7.. -HeadRef 2c3d20a50..` reported commits=6, entries=1, violations=0.
- **Landability.**
  - `merge-tree` against `origin/rc/2026-10-27` (`cf1a12403`, which contains `01c32a59a`): clean.
  - A whole-branch merge into `origin/master` (`f8bddb6ef`) conflicts, because the branch has rc-only ancestry.
  - The 6 commits `8004259c7..2c3d20a50` cherry-pick cleanly onto master in sequence, ending at tree `531b0306`.
  - Master and rc carry the same base manifest `124B52D4.../65`, so the refreshed records are valid on both.

## Check 1: every exit 0 / `approved` path

### Script

There is exactly one `exit 0`: the last line, `:249`. It is reached only when the `try` (`:190-239`) completes.
- **`-Update`** runs the pre-check, then `Assert-EvaluatedGraph`, then `Assert-HeadSource`, then publishes.
- **`-Check`, or no switch** (the `else` branch) compares the record, then runs `Assert-SelectableSdk`, then `Assert-EvaluatedGraph`, then `Assert-HeadSource`.
- `Assert-EvaluatedGraph` throws on any tool exit code other than 0 (`:170`).
- There is no script-scope `return`. The only `return` statements are inside functions.
- `Invoke-AuditChild` has no output pollution, so `.Code` is a scalar and never an enumerated array.

**Dot-sourcing the library** only sets `$ErrorActionPreference='Stop'` and defines `Remove-RecordedCanary`. It has no `exit` and no side effects. A missing library is a terminating error under `Stop` and exits 1.

**Parameter binding.** `[CmdletBinding()]` with no `ValueFromRemainingArguments`. Positional binding exists, but it can only bind `ToolPath` and then `EvaluationBudgetSeconds`. I reproduced each of these on a scratch clone, and each exited 1:

| Arguments | Result |
|---|---|
| `-Check -RemoveRecordedCanary x -RemoveRecordedCanaryRepo y` | parameter-binding error |
| `-Check a b c` | Int32 transform error |
| `-WhatIf`, `-Confirm` | no such parameter |
| `-ErrorAction SilentlyContinue`, `-ErrorAction Ignore`, and `-WarningAction`/`-InformationAction` together with a missing tool | `audit refused` (`:28` re-forces Stop, and the throw lands in the `catch`) |
| `-OutVariable`/`-ErrorVariable` | refused |
| `-EvaluationBudgetSeconds -5` | refused |
| `-Chec` (prefix match) | resolves to `-Check`, then a real refusal |

### Tool

The argument set is closed: `--repo`, `--solution`, `--canary-record`, `--api-probe`, and `--print-budget` only as argv[0].
- `--print-budget` exits 0, but it prints only the budget lines and the script never treats it as a verdict.
- `--api-probe` returns 3 on success, with the members reported on stderr.
- An evaluation returns 0 only from `AuditPhases.Run` after all three phases return 0.
- `PhaseClock.Bound` starts at `Refusal` (2).
- The watchdog exits 2 through `Environment.Exit`.

### The evaluator's identity (the brief's main question)

The script approves on the **exit code of `dotnet <ToolPath-or-default-dll>`** and never checks which binary that is.

I reproduced this: a `dotnet` shim placed first on PATH printed `ceiling=30` and `approved` and exited 0. With `-ToolPath README.md`:
- `-Check` exited 0;
- `-Update` exited 0 (the records were unchanged, because the HEAD was unchanged).

A fake `-ToolPath` DLL works the same way, by code reading.

**Verdict: this is accepted design, not a fail-open of the gate.**
- The land never runs this script. It compares only the committed manifest digest (`docs/orchestration-loop.md`).
- Anyone who can pass `-ToolPath` or change PATH can also hand-edit the two committed records. The script's guarantee is against accidental approval by an honest author, not against the operator.
- No production caller passes `-ToolPath`. The only references are the tests, the doc text ("`-ToolPath` or the documented default path") and plan docs (`-Update -ToolPath <built-dll>`).

See A-1 for the hardening I recommend.

### Environment variables

**Read by the script:**
- None explicitly.
- Implicitly: `PATH` (resolves `git` and `dotnet`), `TMPDIR`/`TEMP` (the canary-record path), and `GIT_*` (`GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, honoured by the git children that bind HEAD).
- `MSBuild*` variables are stripped from the children.

**Read by the tool:**
- Explicitly: `NUGET_PACKAGES` (the test pins this as the only one).
- Implicitly: `DOTNET_ROOT` and the other SDK-discovery variables, through MSBuildLocator. The whole remaining environment is also visible as MSBuild properties, which the CARD-1540 reviewed-property policy refuses.

## Check 2: the library and the delete guard

I ran these probes with the real `scripts/lib/land-graph-canary-removal.ps1`, dot-sourced on Linux pwsh. Every probe returned normally. The repo, parent, outside, uppercase and canary sentinels all survived in each of these:

- the repo root
- empty
- whitespace
- the parent
- `/`
- a valid-named directory outside the parent
- a trailing `/`
- a trailing `\`
- uppercase hex
- `\n` injection
- a lone `\r`
- a relative name
- `<canary>/../repo`
- a repo given with a trailing separator
- a repo directory that does not exist
- the repo given as the parent
- a relative repo
- a missing record file
- a repo reached through a symlinked parent (the canary leaks, which is fail-safe)

Links:
- A valid-named symlink to a sentinel directory: only the link was removed, and the target and sentinel survived.
- A valid-named symlink to the repo: the repo was intact.
- A real canary containing a symlink to a sentinel directory: the canary was removed and the sentinel survived.

Controls:
- The exact record removes the canary.
- A leading space is trimmed, and the exact canary is removed.

**Mutation proof (my own probe harness, not the test).** I replaced each marked guard with `if ($false) { return }`:

| Guard disabled | What was deleted |
|---|---|
| `repo-directory` | the canary beside a missing repo |
| `name` | an uppercase-named sibling |
| `parent` | the outside canary, the canary when the repo was given as the parent, and the canary under a symlinked parent |
| `rebuilt` | the canary on a trailing `/` and on a trailing `\` |
| `repo-separator` | a canary-named directory *inside* the repo, when the repo was given as `repo/` |
| `repo-root` | the repo itself, when it carries a canary name |

So all six guards are individually necessary. The in-suite test `EachCanaryGuardIsNecessary` covers the same six.

## Check 3: is the allowlist test real?

- **Adding a parameter goes red.** In a scratch copy I added `[string]$ReviewMutantExtra` to the script. `RefusingFixtureRejectsEveryRemainingFlag` went red (lease `9c4a…` build, run 2): `should be [...4 names] but was [...5]`.
- **Removing matrix coverage stays green.** I mutated the generator to `var count = parameters.Count - 1;`, which drops `EvaluationBudgetSeconds` from every combination. The test stayed **green** (1/1, 38 s). See A-2.

## Check 4: PhaseClock and the WallClock rename

- `ClockThatSkipsWorkCannotApprove` is a direct test: the clock returns true without running the work, and `Bound` must return `Refusal`. It is red by construction under the old `var result = 0`.
- `ToolClocks.cs` was renamed to `tools/Antiphon.LandGraphAudit.Core/WallClock.cs`. No `ToolClocks` reference remains anywhere.
- Both projects and the tests compiled, and the Core project is referenced by the tool and by Antiphon.Tests.

## Check 5: the approval records

In scratch clone `p` I restored both records to their `8004259c7` bytes and ran a real `-Update -ToolPath <staged tool>`:
- It exited 0 in 29 s: `digest=841DD41B42D127CC768ABEC521683496D2C385ECFE753A4E725FEB5ADD54D052 count=66`, `phase=pass2`, `approved`.
- `sha256sum -c` against the committed files passed: evaluation `90632e85…`, manifest `7e067dec…`.
- No canary was left.

## Check 6: real audit, kill and timeout

**Real audit.** Scratch clone `p` ran `-Check` and approved in 29 s.
- The sha256-of-sha256 over all 9,145 tracked files was `2cea12de…` both before and after.
- `git status` was clean, and no `antiphon-landgraph-canary-*` was left in the parent.

**Budgeted runs.** I ran `-Check -EvaluationBudgetSeconds` with 6, 16 and 22:

| Budget | Outcome |
|---|---|
| 6 s | exit 1, `exceeded 6 s` / `refused (exit 2)` |
| 16 s | killed after `phase=pass2` with its canary already recorded: exit 1, and the canary was removed |
| 22 s | finished in time and approved |

After all three, the tracked hash was unchanged and no tool process was left.

There is no hard-link code: a grep for `CreateHardLink`, `link(`, `DllImport` and `LibraryImport` found no matches.

## Check 7: docs

`docs/orchestration-loop.md` matches the code:
- the per-pass clocks;
- 45 s per pass on Linux, with the 120 s Windows floor marked provisional;
- the plain copy, at 5 s on Linux and 12 s on Windows (provisional);
- the 60 s grace;
- an exit 2 on timeout;
- no sweep of a leaked canary.

`RemoveRecordedCanary` and `WitnessClock` no longer appear in docs, scripts or tools.

## Advisories

**A-1: restrict the evaluator path (hardening, not a defect).** Both `-ToolPath` and a PATH `dotnet` choose the binary whose exit code approves. The default DLL can also be a stale build from older source, which would approve under older rules. Recommendations:
- Accept only a `-ToolPath` that resolves, through its real path, under `<repo>/tools/Antiphon.LandGraphAudit/bin-*/`. Tests could use an explicit test-only seam instead.
- Optionally, have the tool print its source tree hash so the script can refuse a mismatch with `HEAD:tools/Antiphon.LandGraphAudit*`.
- Resolve `dotnet` from `DOTNET_ROOT` or a pinned host path.

**A-2: the generated matrix does not assert that it covers every parameter.** It checks only three labels (`seen` at FailOpenTests.cs:66-68). Dropping a parameter from `ParameterCombinations` stays green (reproduced). Fix: assert that every parameter name appears in at least one label, and that the set of pairs is complete.

**A-3: the static `exit` guard is too narrow.** It counts only literal `exit 0` lines (FailOpenTests.cs:23). A bare `exit`, `exit $x` or a script-scope `return` also ends the script with code 0. The presence matrix would still catch any presence-keyed bypass. Neither guard can catch one keyed on a value or an environment variable, which only code review covers. Consider also refusing script-scope `return` and any `exit` other than `exit 1` or the final `exit 0`.

**A-4: `-EvaluationBudgetSeconds` above 2,147,483 gives a confusing message.** It overflows the `[int]$TimeoutMs` conversion and refuses with a type-conversion message rather than a budget message. This was carried from round 3, and the result is still a refusal.

**A-5: a relative `RepositoryRoot` with no directory part makes `GetFullPath('')` throw inside `Clear-AuditCanary`.** That error escapes the `catch` before `exit 1`, so pwsh still exits 1. The production script always passes an absolute `$RepoRoot`, so this is unreachable today.

## Housekeeping

- The scratch clones `c/r` and `p/r` are under my scratchpad, and each holds a `tests/Antiphon.Tests/bin-review` directory of about 670 MB. Under the cleanup rule I left them rather than run a recursive rm.
- I did not touch the shared checkout. The only thing pushed is this report.
