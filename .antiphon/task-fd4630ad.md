# Review fd4630ad: land-graph audit native-Windows repairs (task add64715)

Subject: feat/card-task-add64715, base b11c0ff5977412ecb6b4ac4665c9078e21b30164, tip
3c487de7f6c4a59d7685c1d5f9645f6625e3f6b7 (677ef8c7b, 41387492a, f389a3d61, ba8869e0f, 3c487de7f).
Platform: server2 Linux (24 CPU). Final round, run at Interim scope as the brief directs: the named classes
only, not the whole Unit lane.

Outcome: no link escape, no skipped link check, no path collision, no widened deadline, no digest change.
Two witness defects: mutations of the canary-copy link guards and of the duplicate-elimination/comparer
repair stay green.

## Defects

### D-1 The canary-copy link guards have no witness
- Where: tools/Antiphon.LandGraphAudit/CanaryRepository.cs:56 (destination check in the worker),
  :59-61 (worker re-check that the source is a link), :77-78 (source link classification).
- Failure: each guard was mutated out of a scratch tip build, with the dll swapped into a copied test output:
  - m3 drops the :56 `copy.CheckDestination`. All 135 LandGraph* tests stay green.
  - m2 forces `target` to false at :77 and m9 drops :59-61. The five link and approval tests stay green.
  - Scratch probe r2 (git repo whose tracked `docs` is a symlink to an outside directory holding o.md):
    under m3 the tool overwrote the outside o.md with `antiphon-c1483-canary`. Only the later
    added-canary check then refused.
  - The tip refuses before writing (`canary destination escapes through a link: .../docs/o.md`).
  - Base and tip produce the same refusal; the tip wraps it in a one-inner AggregateException that
    Program.Describe unwraps.
- Why: the only two link fixtures (C1483_S1_LinkedSkippableItemRefuses, S2_ImportOutsideTrackedSetRefuses)
  refuse in pass 1, before the copy runs. No test drives a tracked link into CanaryRepository.Create. These
  guards were moved into new Parallel.ForEach workers by f389a3d61 (m9 is new there), so the commit that
  rearranged them leaves them unwitnessed.
- Fix (Code owner add64715 lineage):
  1. Add LandGraphEvaluationTests fixtures that reach the copy phase. (a) A tracked `docs` dir symlink
     to an outside directory containing *.md: expect exit 2 `canary destination escapes through a link`
     and the outside bytes and listing unchanged. (b) A harmless tracked file link and a dir link inside
     the repository that must still approve. Under m2/m9 this goes red (spurious `source became a link`
     or a File.Copy of a directory).
  2. Commit the red against an m3/m2-style mutation before the green.

### D-2 The duplicate-copy repair and its comparer have no deterministic witness
- Where: CanaryRepository.cs:43 (separator normalisation, 3c487de7f) and :35 (the
  `ToHashSet(StringComparer.Ordinal)` key).
- Failure:
  - Dropping the normalisation is a no-op on Linux (DirectorySeparatorChar is '/'). Its only witness is
    the native-Windows timing of C1483_S4_RealRepositoryApproves against the 12 s copy limit. The
    investigation doc says that witness has not been recorded green at 3c487de7f ("not a claim that the
    last failure passed").
  - m7 switches the set to OrdinalIgnoreCase. On Linux, probe r3 silently drops the distinct tracked
    files src/s.cs and docs/a.md from the canary. The targeted LandGraph tests stay green.
  - 677ef8c7b and ba8869e0f ("test:") add no test code: only docs/investigations/...-add64715.md.
- Why: the repair's correctness is "one entry per canonical path". The ordinal, case-sensitive key is
  correct (on Windows it can only duplicate, never drop), but nothing pins it on either platform. The
  timing-only witness cannot separate the fix from a faster host.
- Fix:
  1. Extract the entry-set construction (git paths plus the docs enumeration, normalised to '/') into a
     pure function with an injectable separator.
  2. Unit-test it. A Windows-style `docs\x.md` plus git `docs/x.md` gives one entry. `src/S.cs` and
     `src/s.cs` stay two entries.
  3. Run that test red against the pre-3c487de7f shape and against an OrdinalIgnoreCase mutant.

## Checks that passed (evidence)

### (1) Avoid link-target handles (41387492a, SolutionAudit.cs:339-359)
- The attribute read now runs first on every component. Only FileNotFound and DirectoryNotFound count
  as "missing"; any other error still throws.
- LinkTarget is read only when ReparsePoint is set. On Linux, .NET sets ReparsePoint from lstat for every
  symlink, dangling or looped. On Windows, GetFileAttributesEx does not follow links, and LinkTarget is
  non-null only for SYMLINK/MOUNT_POINT tags, which always carry FILE_ATTRIBUTE_REPARSE_POINT. So the
  gate is a necessary condition and never skips a link. The Windows case was argued, not executed.
- 15 RealPath probes run by reflection against the base-built and tip-built tools gave identical
  output. They cover: relative, absolute, dangling-absolute and dangling-relative links; a self loop
  (>40 links); a file link; a chain; a link inside the repository; `..` as a link target; `..` after a
  link; a link in a mode-000 directory (UnauthorizedAccess); a missing parent; and ENOTDIR.
- m1 (no link resolution) turns C1483_S2_ImportOutsideTrackedSetRefuses and
  C1483_S1_LinkedSkippableItemRefuses red.
- m8 (always read LinkTarget) is functionally identical. It is a performance-only change, with no
  functional witness by nature.

### (2) Batched copies (f389a3d61)
- Links are created only on the main thread after `CopyOrdinary()` joins the batch. Every write still
  runs CheckDestination through RealPath.
- Workers use plain File.Copy or a WriteAllText of the canary. There are no hard links. Links are
  re-created and rebased only for tracked links.
- Probe r1 covers inside/outside/absolute/dangling/file/dir links, linked docs, ignored and untracked docs,
  and an untracked dir link under docs. The base and tip copy trees are byte-identical. Dispose removes the
  canary.
- On Linux the degree of parallelism is 1.
- Cancellation: Parallel.ForEach joins in-flight entries before WallClock's 2 s grace.

### (3) Normalisation and path collisions
- Replace(DirectorySeparatorChar, '/') is the identity on Linux. On Windows, '\' cannot occur in a
  filename, so two different paths cannot collapse into one.
- The input is GetRelativePath of a file enumerated under repo/docs with reparse points skipped, so it
  cannot leave the repository. The existing `destination.StartsWith(copy.Root + sep)` and the RealPath
  check remain.
- Probe r3 (case variants) gives identical base and tip copies.

### (5) Deadlines
- The five commits do not touch EvaluationBudget.cs, the Core project or any script.
- The tip equals master 0e7dd390: Linux copy 5 s (MeasuredCopyP95Milliseconds 2253), Windows multiplier 5
  (12 s), pass p95 22328, Windows pass floor 120 s, grace 60.
- rc ade54222 has multiplier 30 (68 s). That is the brief's figure, and the rc port keeps it.
- No environment variable was added.

### (6) Approval digest
- The real `scripts/audit-land-verification-graph.ps1 -Check` was run, never -Update, on a scratch
  clone at 3c487de7f, with the Release tool built through a build slot.
- Result: exit 0, `digest=E117DDECAC45785183AEF8BBE6818DB4DB85A6EC89B9EAD065C1047BF0EDD49B count=68`,
  then pass2 and `approved`, in 29 s on Linux.
- The SHA-256 manifest of all 9,197 tracked files was identical before and after. git status was clean
  and no canary directory remained.

### (7) Definition files and porting
- The commits touch only CanaryRepository.cs, SolutionAudit.cs and one docs .md. There is no
  csproj/props/targets/pins file, so they cannot change the digest.
- Both source files are byte-identical at base, master 0e7dd390 and rc ade54222.
- Range merge-tree with merge-base b11c0ff59 is clean onto master (tree c0e8118f) and onto rc (tree
  f5ad69d7).
- The whole branch is clean onto rc but conflicts onto master (18 files of rc lineage, including the
  approval records). Port to master by cherry-picking the range only.
- CARD-1613 round 4 (06fc8af7 at 0f6058ac9) touches only tests/Shared, a test and reports. It merges
  cleanly with this tip and changes no definition file. A single regeneration on the combined tree
  should still expect E117DDEC.

## Tests (one slot build with UseAppHost=false, then `dotnet exec`, all via build-slot.ps1)

| Selection | Result | Time |
|---|---|---|
| LandGraph* (LandGraphEvaluationTests partials + LandGraphAuditCleanupTests) | 135/135 | 9m22s |
| LandNonCodeSkipTests (own slot) | 111/111 | 10m57s |
| RcRosterRepairTests | 3/3 | |
| WindowsUnitPlatformAdmissionTests | 4 C1210 rows: 3 passed; C1210_Windows_linux_only_outcomes_are_exact skipped (requires Windows placement) | |
| C1514LandingSplitBudgetTests C1404/C1577/C1584/C1410/G2865 | 5/5 | |
| scripts/check-evidence-diff.ps1 b11c0ff59..3c487de7f | commits=5 violations=0 | |

Not run: the whole Unit lane, so the run is Interim. The native-Windows rows are not run: CP-9/10/11, the
four policy classes, C1483 S4/S5 on Windows, and the real -Check on Windows at 3c487de7f. All PCs remain
pending.

## Advisories
- A-1: Parallel.ForEach wraps worker failures in AggregateException. With two or more concurrent failures
  on Windows, Describe prints a generic aggregate message. It is still exit 2 and fail-closed.
- A-2: On Windows the set stays ordinal. A git-path/disk-case mismatch for a docs file would make two
  workers write the same file concurrently, risking a sharing violation and a spurious refusal. That is
  fail-closed.
- A-3: `.antiphon/task-add64715.md` is not on origin/feat/card-task-add64715. Only the docs investigation
  file was committed, so the native report's figures (1,724 duplicates, the 126-case parity, the
  E117DDEC -Check) are only partly recorded there. The native S4 and -Check at 3c487de7f are still
  unrecorded.
- A-4: The brief's "copy multiplier 30, 68 s" is the rc value. Master and this branch use 5 (12 s).

## Cleanup
- All 36 bin-fd4630ad* directories were removed by literal path.
- Scratch work (probes, base/tip/mutant builds, the -Check clone) is under the session scratchpad.
