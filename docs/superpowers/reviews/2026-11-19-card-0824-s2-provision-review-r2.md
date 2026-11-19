# CARD-0824 S2 desktop slot provision: Review round 2 (Final, diff only)

Review task 5294f97f. Subject: `feat/card-task-fa168f8a` (repair Code task fa168f8a of
Code task ac92f48f, Grok author). Reviewed SHA `34be9ee804bfc2967d2920982c78cc44f86e2880`;
round-2 range `476398188481df53cfe8b0adf105aad82c8fb919..34be9ee804bfc2967d2920982c78cc44f86e2880`
(red `4794baaca`, fix `34be9ee80`). Round 1: 609e64c8, D-1 to D-3.

Verdict: **CLEAN**. D-1, D-2 and D-3 hold. No blockers. The advisories below do not hold the land.

## Evidence

| Check | Result |
|---|---|
| CP-1 at tip, checkpoint tool run `20261119-141756-86c3` | GREEN exit 0, `C0824S2 passed=281 failed=0` (281 PASS lines), releases-parent `exit=2 created=False`; `slot=granted waited=0s dirty=0 sourceState=clean buildSource=notApplicable`; `validate --expected-source-sha 34be9ee8...` -> `CHECKPOINT SOURCE VALID rows=1`. Start load 13.5 |
| Guard union (9 classes) + DerivedImpactIndexTests, one build-slot run, fresh TRX | 108 executed, 107 passed, 1 failed. TestClassificationPolicy 42, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3, MechanicalPcContract 11, TaskPlatformGuidance 5, C1729LayeringGuard 11, TestLaneCategoryGuard 1, DerivedImpactIndex 27. These counts match round 1 |
| `DerivedImpactIndexTests.committed_index_matches_the_builder` red | Inherited, same failure as round 1. The round-2 range touches no `.cs`, `.csproj` or `.json` file (4 files: two plan docs and two `.ps1`). Master regenerated `tests/derived-impact-index.json` three times after base `0822e8bbc` (`36d0c7582`, `d362fa545`, `4da65a3cb`) |
| `check-evidence-diff.ps1 -BaseRef 0822e8bbc -HeadRef 34be9ee80` | commits=4 entries=0 violations=0, exit 0 |
| `audit-land-verification-graph.ps1 -Check` | `approved`, exit 0. Start load 11.3 |
| `git merge-tree --write-tree origin/master(26a99f1a7) HEAD` | One known add/add on `docs/superpowers/plans/2026-11-19-desktop-hot-worktree-slots-plan.md`. The only difference from master's copy is S4 step 2's added `-Execute`, and that addition is correct. The replay must keep it |

Unlisted runs, with the reason for each:
- `dotnet run --project tools/Antiphon.Checkpoints -- run`: the documented checkpoint entry. Its own tool build was outside a lease.
- `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` (203 s, leased) for the guard batch.
- Linux pwsh probes that dot-source the script: no build. They covered `Get-SlotTextRefusal` over 29 inputs, symlinked `.git`, a symlinked `repos`, and a symlinked `worktrees` under `-RemoveEmpty`.

Every build and test command ran under its own build-slot lease. **Load-gate deviation:** the guard run started at a 1-minute load of 17.55, above the 16 gate. I misread the load. CP-1 and land-graph started below 16.

## Fix verification

- **D-1 (contained forbidden dirs), holds.** `Test-UnderPrefix $prefix $Comparable` adds the containment check. `Test-ForbiddenSlotChild` also refuses any of the four children that would resolve forbidden. Matching is segment-bound, because `Test-UnderPrefix` appends `\` before `StartsWith`, and it uses `OrdinalIgnoreCase`. Observed:
  - Refused as forbidden: `C:\Antiphon`, `c:\antiphon\`, `C:\src`, `C:/SRC`, `C:\Antiphon\WORKTREES`, `C:Antiphon`, `C:\Antiphon\slots\..\worktrees`.
  - Refused as filesystem roots: `C:\` and `C:`.
  - Allowed: `C:\Antiphon\slots`, `C:/antiphon/SLOTS/`, `C:\Antiphon\slots2`, `C:\AntiphonX`, `C:\Antiphon\worktrees2`, `C:\Antiphon\releasesX[\pool]`, `C:\src\Antiphon2`, `C:\srcX`.
  - New, stricter behaviour: `D:\releases` is now refused, because the `releases` segment becomes a parent through the child check. This is over-refusal and is safe.
- **D-2 (aliases), holds for the listed vectors.** `Get-SlotTextRefusal` runs on the raw text before normalisation.
  - The `\\` prefix (after `/`->`\`) gives `unsupported-root`. That covers `\\?\`, `\\.\`, UNC and `//?/`.
  - Any drive-path segment that ends in `.` or a space, or contains `~`, gives `alias-segment`.
  - The relative branch checks after `GetFullPath`. Win32 normalisation there matches what `New-Item` later does.
  - The tilde rule is textual. It refuses legitimate names that contain `~`, and trailing-dot names that are legal on Linux. That over-refusal is acceptable and documented in the script header.
  - The reparse check (`:166-191`) walks every **existing** ancestor of the root up to the drive or `/`. For each one it uses `Get-Item -Force -LiteralPath` and `($attrs -band [IO.FileAttributes]::ReparsePoint)`. That covers junctions and symlinks on Windows, and symlinks on Linux, where .NET reports `ReparsePoint` for a symlink. The `reparse-ancestor` witness is green on Linux.
  - A non-existing ancestor is skipped. It cannot be a link, and an alias spelling is refused textually before the walk.
- **D-3 (longpaths escape), holds.**
  - `GIT_CEILING_DIRECTORIES=<clone parent>` stops discovery from leaving `repos/antiphon`.
  - `rev-parse --show-toplevel` and `--absolute-git-dir` must equal the clone and `<clone>/.git` (`/`-normalised, case-insensitive); otherwise the script refuses with `clone-unresolved` or `clone-mismatch`.
  - The write is `config --local` with explicit `--git-dir`/`--work-tree`, and the value is read back.
  - Covered states:
    - An empty `.git` refuses.
    - A gitfile `.git` refuses, and the enclosing repo's `core.longpaths` stays `false` (witnessed).
    - A real clone passes and the decoy stays `false` (the `execute` case).
  - Probed on Linux: a symlinked `.git` to an outer repo, and a symlinked `repos` dir to another repo's parent, both refuse with `clone-mismatch`, because git realpaths the git dir and toplevel. Neither foreign config was written.
- **-RemoveEmpty, holds.** `[IO.Directory]::Delete(path, $false)` is non-recursive, and the `catch` turns a race into `remove-empty-blocked reason=not-empty`. The race witness is green.
  - The reparse check does **not** cover the four children.
  - A pre-existing `worktrees` symlink or junction whose target is empty is removed as the link only, and the target is kept (probed on Linux).
  - A non-empty target is blocked by the `Get-ChildItem` precheck.
  - In no case can the delete remove anything beyond the four entries or follow a link into target content.
- **No regression.** The dry-run, whatif, idempotent second execute, low-free-space, budget, forbidden-prefix, releases-parent and filesystem-root cases are all green in the 281 count.
- **PS 5.1.** The new code uses only .NET Framework APIs: `Directory.Delete(string,bool)`, `Path.GetFullPath`, `String.Equals(...,StringComparison)` and `Env:` provider calls. It has no ternary, `??`, `&&`, `||`, LINQ or PS7-only cmdlet. The `ps51-ascii` AST/byte case is green. PS 5.1 itself was not run here; the test prints this as UNOBSERVED.

## Advisories (to cards, do not hold the land)

- **A-1:** extend `Test-SlotReparseAncestor` to the four children and to `repos\antiphon`.
  - Today a child junction is accepted as `unchanged:`.
  - Git for Windows junction resolution for the `clone-mismatch` compare was not observed here. On Linux, git's realpath makes it refuse.
  - The ancestor walk uses `Test-Path`, which returns false for a dangling link, so a dangling junction ancestor is skipped. A write through it fails rather than escapes.
- **A-2:** some volume-level aliases are not refused textually: `subst` or `net use` drive letters mapped onto `C:\Antiphon` or `C:\src`, and NTFS stream syntax (`C:\Antiphon\worktrees::$INDEX_ALLOCATION\pool` returns `''`). This is unverified on Windows. It needs deliberate operator input, and the script would only create or remove empty dirs. Canonicalise the deepest existing ancestor (final path by handle), or refuse `:` beyond the volume separator.
- **A-3:** the Windows-only behaviour is UNOBSERVED in CP-1: 8.3 names, junctions, trailing-dot collapse and `powershell.exe` 5.1. It remains a required manual check at S4 step 2 on the desktop. Nothing ran on the desktop in this review.
