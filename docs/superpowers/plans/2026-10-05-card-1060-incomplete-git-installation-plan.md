# CARD-1060: skip an incomplete earlier Git installation in readiness tool discovery

Status: **Plan with folded verification design; next is Code.** Inspected source
`f41748999c285a6ea00a7b8bf486ec51ee92fa16` (task base, branch
`feat/card-task-42198ac1`). Card read with `scripts/card.ps1 get CARD-1060 -Board
Antiphon`. Predecessor design: [CARD-1049 plan](2026-10-04-card-1049-windows-readiness-retry-plan.md)
(D-4 native Windows coverage, D-5 fixture-local tool discovery). This is a
test-fixture repair only; `scripts/deploy-am-service.ps1` and both
`scripts/test-deploy-am-service*.ps1` seams stay byte-for-byte unchanged.

Owners read: [testing and build](../../testing-and-build.md) (Checkpoint
manifest, Combined class filters, CARD-1049 note), [orchestration](../../orchestration-loop.md)
§1, [project conventions](../../project-context.md).

## Ground truth

All code citations are `tests/Antiphon.Tests/Infrastructure/AmServiceDeployReadinessTests.cs`
at the inspected source.

| Card assumption | What the code actually does | Consequence |
|---|---|---|
| `FindProgram` keeps only the first `git.exe` on PATH. | True. `FindProgram` (line 240) returns the first PATH entry whose `<name>` or `<name>.exe` exists. `ResolveTools` (line 255) calls it once for `git` and once for `sh`, so installation-relative `usr/bin` candidates are derived from at most two launchers: the first `git.exe` and the first `sh.exe`. | The defect is launcher enumeration, not candidate filtering. Enumerate every `git.exe` and `sh.exe` on the supplied PATH, in PATH order. |
| `ResolveTools` must learn to "skip an incomplete installation and continue". | The candidate loop already `continue`s past a `usr/bin` that lacks any of `sh.exe`, `sleep.exe`, `cygpath.exe` (the `All(File.Exists)` check). It never reaches the complete installation because that installation's `usr\bin` is not a PATH entry and its `cmd\git.exe` is never a launcher. | The skip is the retained guard (G-2 below), not new work. The new work is feeding the loop a candidate per installation (G-1). |
| `C:\Program Files\Git\usr\bin` "is later on PATH". | CARD-1049 I-1 measured this desktop: original-PATH `sh`/`sleep`/`cygpath` searches all failed; only `C:\Program Files\Git\cmd` is on PATH. The complete installation is reachable only installation-relatively from its `cmd\git.exe`. | Exactly the launcher that the first-match-only enumeration never visits when Grok's `cmd\git.exe` precedes it. The fix must not depend on `usr\bin` being a PATH entry. |
| The fixture throws before any probe. | `ReadinessFixture.RunAsync` (lines 359-360) throws `InvalidOperationException(toolError)` when `ResolveTools` returns null, before writing wrappers or starting `pwsh`. | Correct; the four methods fail closed and cannot green through a stall. Keep this. |
| Only the resolver needs to change. | `ResolvesGitToolsWithoutUsrBinOnPathAsync` (line 113) also picks its reference installation with `FindProgram("git", parentPath)` and asserts every resolved tool lives in that installation's `usr\bin` (`SamePath(..., usrBin)`). With Grok's Git first, the reference is the incomplete installation. After the resolver fix this method would fail at the `SamePath` assertion instead of at the resolution assertion. | V-3 must take its reference from the first **complete** installation, i.e. `ResolveTools(parentPath)`, then strip that installation's `usr\bin` and append its `cmd`/`bin` as today. |
| The failure text is actionable. | The error names the three prerequisites generically; it does not say which installations were inspected or what each lacked. The Grok mask was diagnosed by reading PATH by hand in the CARD-1049 Final Review. | Append one clause per skipped installation naming its root and missing files, so the next host with an odd first `git.exe` is diagnosable from the test output. |
| The CP-2 watchdog hit may be an intermittent hang. | Not reproduced (60.5 s across three shells, per the card). Nothing in this plan touches the 300 s fixture deadline, the wrapper's 100-tick acknowledgment loop or any curl deadline. | Out of scope; the card says do not widen the 45 s watchdog. |
| Linux is unaffected. | The Linux branch of `ResolveTools` (lines 261-266) returns before the Windows candidate logic. The Windows-only method skips off Windows with `SkipTestException`. | One Linux row proves the edited file still compiles and the Linux fixture path is unchanged; the Windows rows carry the behaviour. |

## Decisions

- **D-1: enumerate every launcher, keep today's precedence.** Add
  `FindPrograms(name, searchPath)` yielding every PATH match in order and make
  `FindProgram` its `FirstOrDefault()`. `ResolveTools` derives the three-ancestor
  `usr/bin` candidates from **all** `git.exe` matches, then all `sh.exe`
  matches. Candidate order stays: direct PATH entries first, then derived
  candidates in launcher order. First complete installation wins. Rejected:
  a single pass that interleaves direct and derived candidates per PATH entry
  (changes precedence between an explicit `usr\bin` PATH entry and a derived
  one; not needed by the card); a disk search or hard-coded
  `C:\Program Files\Git` (CARD-1049 D-5 forbids both); any machine or parent
  PATH edit (card forbids; parent PATH remains byte-for-byte unchanged).
- **D-2: still refuse when no complete installation exists, and say which were
  skipped.** Keep the existing refusal sentence and append
  `Skipped incomplete Git installation <root>: missing <a>, <b>` for every
  candidate whose root has `cmd\git.exe` or `bin\git.exe` but whose `usr\bin`
  lacks one or more of `sh.exe`, `sleep.exe`, `cygpath.exe` (that fixed order,
  only the absent names, clauses joined by `; `). Rejected: falling back to a
  partial installation (`sh.exe` without `cygpath.exe`): `ToPosixPath` needs
  real cygpath and the curl wrapper needs real sleep, so the failure would
  move later and lose its cause.
- **D-3: V-3 references the first complete installation.** Replace the
  `FindProgram("git", parentPath)` reference in
  `ResolvesGitToolsWithoutUsrBinOnPathAsync` with `ResolveTools(parentPath)`;
  `usrBin` is its `Directory`, `gitRoot` is that directory's grandparent.
  Everything else in the method (strip `usrBin`, append `cmd`/`bin`, cygpath
  absent precondition, witness `C1049-toolchain-resolved-without-usrbin`,
  spaces-in-path sh/cygpath round trip, fixture run, parent PATH unchanged)
  stays. Rejected: keeping the first-`git.exe` reference (fails at `SamePath`
  under a Grok-first PATH once D-1 lands); loosening the identity assertion to
  "any complete installation" (would hide a resolver that picks a different
  installation than the one whose `usr\bin` was removed).
- **D-4: new tests are Windows-only and use fake installations on a private
  PATH.** Both new methods skip off Windows with the existing
  `SkipTestException` guard and build a temp tree of empty `.exe` files:
  `incomplete\cmd\git.exe` + `incomplete\usr\bin\sh.exe` only;
  `complete\cmd\git.exe` + `complete\usr\bin\{sh,sleep,cygpath}.exe`;
  `tools\curl.exe`. `FindProgram`/`ResolveTools` only test `File.Exists`, so
  no process starts. V-1 additionally prepends the fake incomplete `cmd` to
  the real parent PATH so the card's exact scenario is reproduced on any
  Windows host with one complete Git, whether or not Grok's Git is on that
  host's PATH. Rejected: a portable Linux simulation of the Windows branch
  (needs a platform flag through `FindProgram` and the `.exe` logic; more
  change than the defect, and the Windows lane exists); relying only on the
  machine's real PATH order (not reproducible on a host without Grok's Git).
- **D-5: one Code slice pair, 30-60 minutes, verification folded here.**
  Exact methods only, trailing method wildcards for the pinned TUnit discovery
  (see Combined class filters in the testing owner), no whole Unit, namespace
  or assembly run. Three method-scoped positive controls run in post-land
  SourceLanding Mutation. Independent Review precedes land.

## Implementation slices

| Slice | Files | Work and completion |
|---|---|---|
| S1, tests first (10-15 min) | `tests/Antiphon.Tests/Infrastructure/AmServiceDeployReadinessTests.cs` | Add a private helper `WriteFakeGitInstallation(string root, bool complete)` that creates `cmd\git.exe` and `usr\bin\sh.exe`, plus `usr\bin\sleep.exe` and `usr\bin\cygpath.exe` when `complete`. Add the two Windows-only methods below (V-1, V-2). Compile against the unchanged resolver. Commit `test(card-1060): ...; implementation pending`, push. Run CP-1 and CP-2 as baseline reproduction: both must be red at the named assertions (see Verification design). |
| S2, fix plus note (10-15 min) | same test file; `docs/testing-and-build.md` CARD-1049 paragraph | Implement D-1, D-2, D-3. Add two sentences to the CARD-1049 note: every `git.exe`/`sh.exe` on the supplied PATH is a discovery root, an incomplete earlier installation (Grok's bundled Git lacks `sleep.exe`/`cygpath.exe`) is skipped and named in the failure (CARD-1060). One commit so the final tested SHA is HEAD. Push, then run CP-1..CP-6 on Windows and CP-7 on Linux at that SHA. |

Resolver shape after S2 (Windows branch of `ResolveTools`):

```csharp
var launchers = FindPrograms("git", originalPath).Concat(FindPrograms("sh", originalPath));
foreach (var launcher in launchers) { /* unchanged three-ancestor usr/bin derivation */ }
var skipped = new List<string>();
foreach (var directory in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
{
    /* unchanged: must be <root>/usr/bin and <root> must have cmd/git.exe or bin/git.exe */
    var missing = new[] { "sh.exe", "sleep.exe", "cygpath.exe" }
        .Where(name => !File.Exists(Path.Combine(directory, name))).ToList();
    if (missing.Count == 0) { error = ""; return new ToolPaths(curl, sh, sleep, cygpath, directory); }
    skipped.Add($"Skipped incomplete Git installation {root}: missing {string.Join(", ", missing)}");
}
error = "<existing refusal sentence>" + (skipped.Count == 0 ? "" : ". " + string.Join("; ", skipped));
return null;
```

New test methods (both `[Test]`, Windows-only via `SkipTestException`, temp
root `Path.Combine(Path.GetTempPath(), "c1060-" + Guid.NewGuid().ToString("N"))`
deleted in `finally`, path comparisons through `SamePath`, text containment
`Case.Insensitive` because Windows temp paths vary in case):

- `ResolvesGitToolsPastIncompleteInstallationAsync` (V-1).
- `RefusesWhenNoCompleteGitInstallationExistsAsync` (V-2).

No new class, so `ProcessSpawnLimit`, lane-category and slow-allowlist guards
need no change. The class keeps `[Category("Integration")]` and
`[ParallelLimiter<ProcessSpawnLimit>]`.

## Verification design

### Inspection

Read `FindProgram`, `SamePath`, `ResolveTools` (both branches), `ToPosixPath`,
`ReadinessFixture` constructor and the first ten lines of `RunAsync`, and the
whole of `ResolvesGitToolsWithoutUsrBinOnPathAsync`. Confirm the production
deployment script and both PowerShell test seams have an empty diff. No
production delivery path, queue or receipt is involved; this is a test fixture.

### Proves it works now

- **V-1: `ResolvesGitToolsPastIncompleteInstallationAsync`.** Private PATH
  `incomplete\cmd;tools;complete\cmd`. Precondition: `FindProgram("git",
  privatePath)` is the incomplete `git.exe` (the mask is real) and
  `FindProgram("sh", privatePath)` is null. Act: `ResolveTools(privatePath, out
  error)`. Decisive assertion `tools.ShouldNotBeNull("C1060-incomplete-git-skipped:
  " + error)`; then `tools.Directory` same-path `complete\usr\bin`, `Sh`,
  `Sleep`, `Cygpath` under it and existing, `Curl` same-path `tools\curl.exe`,
  `error` empty. Second half, witness `C1060-incomplete-git-skipped-real-path`:
  `ResolveTools(incomplete\cmd + ";" + parentPath)` is non-null and its
  `Directory` same-path `ResolveTools(parentPath).Directory`, which itself must
  be non-null (a Windows lane without a complete Git fails, never skips, per
  CARD-1049 D-4). Parent PATH asserted byte-for-byte unchanged at the end.
- **V-2: `RefusesWhenNoCompleteGitInstallationExistsAsync`.** Private PATH
  `incomplete\cmd;tools`. `ResolveTools` returns null, witness
  `C1060-incomplete-only-refused`; `error` contains the existing prerequisite
  sentence, contains `"Skipped incomplete Git installation " + incompleteRoot +
  ": missing sleep.exe, cygpath.exe"` (`Case.Insensitive`) and does not contain
  `"missing sh.exe"`. Then `new ReadinessFixture(0, "ready", privatePath)
  .RunAsync("V-2-no-complete-git", <deploy-am-service.ps1 path>)` throws
  `InvalidOperationException` whose message equals that `error`, witness
  `C1060-incomplete-only-fixture-throws`; no wrapper or child is started
  because the resolver throws first.
- **V-3: amended `ResolvesGitToolsWithoutUsrBinOnPathAsync`** per D-3. The
  reference is `ResolveTools(parentPath)`; the resolution, spaces-in-path
  round trip, immediate-ready fixture run and unchanged parent PATH assertions
  are retained verbatim. Witness `C1049-toolchain-resolved-without-usrbin`.

Baseline reproduction at S1 (unchanged resolver): CP-1 is red at
`C1060-incomplete-git-skipped` (tools null, because only the first `git.exe`
is a launcher) and CP-2 is red at the `Skipped incomplete Git installation`
containment (the null assertion before it passes). Report both CHECKPOINT
lines with `failed=1`; the S2 rerun of the same rows counts as `reruns=1`.
This is defect reproduction, not a Mutation cycle. A new test that is green
at S1 is a stub and must be fixed before S2.

### Guards the regression

- **R-1:** `RetriesUntilHttpReadyAsync`,
  `FailsWhenHttpReadinessBudgetIsExhaustedAsync` and
  `SucceedsImmediatelyAndPreservesDeployContractAsync` pass on Windows at the
  final SHA with every existing assertion intact. On a host whose PATH starts
  with Grok's Git these are the card's four reds turning green; on any other
  host they are the unchanged regression set. Record the resolved
  `tools.Directory` from the `C1049 tools:` line where the method prints it.
- **R-2:** `SucceedsImmediatelyAndPreservesDeployContractAsync` passes on
  Linux: the file compiles and the untouched Linux branch still resolves
  `sh`/`sleep` from PATH.
- **R-3:** Review verifies `git diff <base> <head> -- scripts/` is empty and
  that no test timeout, watchdog or curl deadline changed.

### Guard inventory

Three changed behaviours, three controls, zero unmapped changed guards. The
D-3 change to V-3 is test-side; the resolver behaviour it exercises is still
guarded by CARD-1049 PC-2 (remove installation-relative discovery, red at
`C1049-toolchain-resolved-without-usrbin`), which remains valid unchanged and
is not renumbered here.

- G-1 (D-1: a later complete installation is found past an earlier incomplete
  launcher) maps only to PC-1.
- G-2 (D-2: no complete installation still refuses) maps only to PC-2.
- G-3 (D-2: the refusal names the skipped installation) maps only to PC-3.

### Positive controls

Every phase selects exactly one method with the filter in its row,
`-MinExecuted 1` and the exact `Class.Method` in `-Expect`; the trailing
method wildcard is intentional for the pinned TUnit discovery implementation.
All three mutations edit `ResolveTools` in the same file, so run the cycles
sequentially, never batched. A build error, skip, zero-test run, missing TRX
or watchdog is not red.

| Guard / PC | Compiling test-fixture mutation | Exact method filter | Decisive red |
|---|---|---|---|
| G-1 / PC-1 | In `ResolveTools`, enumerate only the first match again: `FindPrograms("git", originalPath).Take(1).Concat(FindPrograms("sh", originalPath).Take(1))`. | `/*/*/AmServiceDeployReadinessTests/ResolvesGitToolsPastIncompleteInstallationAsync*` | `C1060-incomplete-git-skipped`: expected non-null, observed null with an error naming the fake incomplete root. Preconditions passed. |
| G-2 / PC-2 | Replace the completeness test with `File.Exists(Path.Combine(directory, "sh.exe"))` only, returning the first installation that has `sh.exe`. | `/*/*/AmServiceDeployReadinessTests/RefusesWhenNoCompleteGitInstallationExistsAsync*` | `C1060-incomplete-only-refused`: `tools.ShouldBeNull()` fails with `Directory` = fake `incomplete\usr\bin`. |
| G-3 / PC-3 | Drop the `skipped` clauses: set `error` to the bare refusal sentence. | `/*/*/AmServiceDeployReadinessTests/RefusesWhenNoCompleteGitInstallationExistsAsync*` | `error.ShouldContain("Skipped incomplete Git installation " + incompleteRoot + ...)` fails; the null assertion before it passes. |

Mutation runs in a fresh SourceLanding worktree after land, on Windows
(`-Platform Windows`): baseline green, mutate, red, restore exact tracked
bytes, green, for each PC; raw phase evidence in the assigned external
evidence root; no commits from the snapshot; remove owned `bin-c1060-*`
outputs after every child exits.

### Out of scope

No production readiness policy, no change to `scripts/deploy-am-service.ps1`
or either PowerShell seam, no machine or parent PATH edit, no WSL, no disk
search, no shared repository-wide tool resolver, no timeout or watchdog
change, no Linux-only conversion of the class, no card edit by the delegate.

### Placement and execution

Read at about 19:03 UTC on 2026-10-05: `GET /api/runner-defaults` revision 2
(global default server2, Linux); `GET /api/session-runners` lists an eligible
Windows runner (capacity 2 delegated tasks, 1 occupied) and an eligible Linux
runner (capacity 10 sessions, 8 occupied). Re-read at dispatch; no fleet
location is embedded here. Omit `-Runner`.

The behaviour is Windows-only and the new tests can only go red there, so
dispatch Code with `-Platform Windows`; it runs CP-1 and CP-2 at S1 (red), then
CP-1..CP-6 at S2 through the checkpoint tool
(`dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this plan>
--rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6 --expected-source-sha <S2> --serial`).
CP-7 needs `-Platform Linux` at the same S2: the caller arranges that lane
(a second Code or the Review task); a delegate does not sub-delegate. Do not
run the whole table on one OS: the Windows-only methods skip on Linux and
`0 skipped` fails them. Group names carry the lane. Every build and test
driver takes the host build slot; `run-checkpoint.ps1` and the checkpoint tool
lease their own. Exit 4 is a slot timeout to report, never a reason to run
unleased. Keep every `wait` owned until terminal; exit 75 means wait again.
Build to `bin-c1060-windows/` and `bin-c1060-linux/` with forward slashes and
remove the task-owned `bin-c1060-*` directories after children exit.

Validate receipts for the exact committed SHA, `dirty=0`, `sourceState=clean`,
`buildSource=verified`, the exact roster and zero failures/skips. Retain
unedited CHECKPOINT lines in `.antiphon/task-<id>.md`; TRX/JSON/logs stay
ignored. Code and Review run `scripts/check-evidence-diff.ps1 -BaseRef
f41748999c285a6ea00a7b8bf486ec51ee92fa16 -HeadRef <S2>` over the whole range.

### Cost

Ordinary floor is the `EstimatedMinutes` sum: 19 minutes (14 Windows, 5 Linux,
two test-project builds), plus about 5 minutes for the S1 baseline run of CP-1
and CP-2 (one build, two sub-second tests). Authoring 20-30 minutes. Code total
44-54 minutes excluding slot waits and the Linux lane handoff. Mutation: three
sequential cycles, nine phase builds on Windows, 40-55 minutes. No repeat proof
after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1060-windows/` | windows-incomplete-skipped | `/*/*/AmServiceDeployReadinessTests/ResolvesGitToolsPastIncompleteInstallationAsync*` | V-1 | exactly 1 passed, 0 failed/skipped | 1 | 5 | true |
| CP-2 | all | CP-1 | windows-incomplete-refused | `/*/*/AmServiceDeployReadinessTests/RefusesWhenNoCompleteGitInstallationExistsAsync*` | V-2 | exactly 1 passed, 0 failed/skipped | 1 | 1 | true |
| CP-3 | all | CP-1 | windows-git-tool-discovery | `/*/*/AmServiceDeployReadinessTests/ResolvesGitToolsWithoutUsrBinOnPathAsync*` | V-3 | exactly 1 passed, 0 failed/skipped | 1 | 1 | true |
| CP-4 | all | CP-1 | windows-retry | `/*/*/AmServiceDeployReadinessTests/RetriesUntilHttpReadyAsync*` | R-1 | exactly 1 passed, 0 failed/skipped | 1 | 3 | true |
| CP-5 | all | CP-1 | windows-exhaustion | `/*/*/AmServiceDeployReadinessTests/FailsWhenHttpReadinessBudgetIsExhaustedAsync*` | R-1 | exactly 1 passed, 0 failed/skipped | 1 | 2 | true |
| CP-6 | all | CP-1 | windows-immediate-legacy | `/*/*/AmServiceDeployReadinessTests/SucceedsImmediatelyAndPreservesDeployContractAsync*` | R-1 | exactly 1 passed, 0 failed/skipped | 1 | 2 | true |
| CP-7 | all | `tests/Antiphon.Tests -> bin-c1060-linux/` | linux-immediate-legacy | `/*/*/AmServiceDeployReadinessTests/SucceedsImmediatelyAndPreservesDeployContractAsync*` | R-2 | exactly 1 passed, 0 failed/skipped | 1 | 5 | true |

`After: all` means S1 and S2 are committed for the green run; the S1-only run
of CP-1 and CP-2 is the authorized baseline reproduction described under
"Proves it works now" and is reported as those rows with `failed=1`.

## Acceptance and next stage

Acceptance: CP-1 and CP-2 red at S1 at the named assertions; all seven rows
green at one S2 with clean receipts; `scripts/` diff empty; no timeout change;
the testing-owner note added. Review then land; the three PCs run in
post-land SourceLanding Mutation on Windows. Next: **Code** on
`-Platform Windows`, with the Linux CP-7 lane arranged by the caller.
