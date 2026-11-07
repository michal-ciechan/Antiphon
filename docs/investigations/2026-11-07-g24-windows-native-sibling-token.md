# 2026-11-07 — g24 Windows-native: missing long-branch sibling token (C443 D9)

Subject: live Windows run 8e938b6d (rc/2026-11-08), `antiphon-all-group-24.trx`, one failure:
`AgentTaskWorktreeLockOutcomeTests.C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics(False, inbox)`,
`terminal.Detail` lacked the token `d6868759:feat/card-task-d6868759sss…` (189-char branch). The marker
`unlanded-sibling=` was present (line 92 passed); the per-token loop (line 96) failed.

Investigated on master `1394d6e3db90d12ec38de564e7042c1d4603ee2c`, Windows 10 desktop, natively,
while the live qualification run was using the host (CPU sampled 48–95 %, mostly 70–95 %).

## Verdict

Branch **(c)** fired: the sibling-inspection deadline expired during the 4th sibling's containment
check and the loop stopped with the visible warning `CARD-0443: sibling inspection incomplete
(inspection_timeout).` The deadline was **2 s, not 5 s**: the test harness constructs
`AgentTaskLandService` without `IOptions<GitSettings>`, so `_gitSettings` is null and
`AgentTaskLandService.cs:1138` falls back to `?? 2`. Production DI always resolves
`IOptions<GitSettings>` (`Program.cs:141`), so production uses the 5 s default.

Branches (a) and (b) never fired: every `show-ref --verify` succeeded (exit 0) and every containment
result was `NotContained`, never `Unknown`. The common-directory check (a silent `continue` at the top
of the loop) also never fired.

**Classification: TEST defect** (harness wiring: `LandingSafetyHarness.CreateLand` drops `GitSettings`,
so the fixture runs under an undocumented 2 s budget that four ~190-char-branch siblings exceed on a
loaded Windows host). The product does not silently drop the sibling: it emits a named
`inspection_timeout` warning, which is the specified behaviour.

## Native run (one build, one run)

- Build: `build-slot.ps1 -Label g24-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false`,
  0 errors, 10 min 56 s under load.
- Run: `build-slot.ps1 -Label g24-run1 -- dotnet run --project tests/Antiphon.Tests --no-build
  -p:UseAppHost=false -- --treenode-filter "/*/*/AgentTaskWorktreeLockOutcomeTests/*"`:
  **total 20, succeeded 19, failed 1** (5 min 03 s). The failing row is the same one as in the live
  run: `C443_D9_…(False, inbox)` (10.4 s). No second run was needed; the failure reproduced on the first.

Temporary instrumentation (log to `.antiphon/g24-instr.log`) was added to
`server/Application/Services/AgentTaskLandService.cs` (`CollectUnlandedSiblingsAsync`) and
`tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs`, then removed with
`git checkout --` on both files; nothing from it is committed.

### The four long-branch rows

| Row | Result | Duration | Siblings inspected | Inspection elapsed (deadline 2 000 ms) | Detail length | Missing tokens | Warnings |
|---|---|---|---|---|---|---|---|
| (True, modern) | Passed | 16.3 s | 4/4 | 1 718 ms | 1 848 | none | 4 × "has committed work absent" |
| (False, modern) | Passed | 15.9 s | 4/4 | 1 776 ms | 1 848 | none | 4 × "has committed work absent" |
| (True, inbox) | Passed | 15.9 s | 4/4 | 1 197 ms | 1 848 | none | 4 × "has committed work absent" |
| (False, inbox) | **Failed** | 10.4 s | 3/4 | **2 388 ms → TIMEOUT-BREAK** | 1 649 | 1 (`55881c3b`) | 3 × "has committed work absent" + `CARD-0443: sibling inspection incomplete (inspection_timeout).` |

In the failing row the 4th sibling's `show-ref` finished at 1 945 ms; the 2 s deadline then cancelled
its `ObserveContainmentAsync` (the break was logged at 2 388 ms, after the git child was torn down).
The missing token is always the last sibling in `OrderBy(s => s.Id)` order, which is unrelated to the
test's creation order, so which branch name appears in the assertion message is random.

### Per-sibling evidence (all four long rows)

- `show-ref --verify --quiet refs/heads/<branch>`: exit 0 for all 16 long siblings (and all 8 short ones).
- Loose ref path: `C:\Users\lndco\AppData\Local\Temp\antiphon-c448-<32hex>\canonical\.git\refs\heads\feat\card-task-<id><s…>`
  = **296 characters** (fixture root 80 chars + 189-char branch), > MAX_PATH 260, yet `File.Exists`
  was true and git found it (the fixture sets `core.longpaths=true`). MAX_PATH is not a factor.
- `show-ref` cost per long sibling: 51–245 ms (short branches: 3–7 ms).
- Containment (`ObserveContainmentAsync`) per sibling: 210–570 ms; all `NotContained`.
- Total per long sibling: ~260–660 ms, so four siblings take 1.2–2.4 s under this load; the 2 s
  budget is crossed whenever the host is busy, which explains a flake only in the loaded Windows
  qualification and 20/20 on Linux (Debug 5c57130c).
- Short-branch single-sibling rows: 209–541 ms, well inside the budget.

The live-run TRX (read only) agrees: the other three D9 rows passed (26.9 s, 15.8 s, 15.9 s) and the
failing row stopped early (8.0 s), the same shape as the local reproduction.

## Recommended smallest fix (TEST)

`tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs`, `CreateLand`: pass the production
default settings to the service, e.g. add `gitSettings: Options.Create(new GitSettings())` to the
`new AgentTaskLandService(...)` call. The fixture then runs with the same 5 s
`WorktreeBaseInspectionTimeoutSeconds` that production DI supplies (max observed here 2.4 s, so
roughly 2× headroom). No assertion and no timeout value changes; the harness only stops relying on
the hidden fallback.

Optional follow-up (product hygiene, not required for this flake): the `?? 2` fallback at
`AgentTaskLandService.cs:1138` disagrees with the `GitSettings` default of 5 and only applies to
callers that construct the service without options (tests). It could read
`new GitSettings().WorktreeBaseInspectionTimeoutSeconds` so the two cannot drift; that is a separate
card, because it changes the effective budget for every settings-less construction.

Not recommended: changing the product to drop the deadline or to treat the timeout as silent; the
product already reports the incomplete inspection visibly.
