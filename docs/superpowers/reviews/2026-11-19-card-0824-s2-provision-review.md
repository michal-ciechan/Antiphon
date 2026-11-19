# CARD-0824 S2 desktop slot provision: Review round 1 (Final)

Review task 609e64c8. Subject: `feat/card-task-ac92f48f` (Code task ac92f48f, Grok author).
Reviewed SHA `476398188481df53cfe8b0adf105aad82c8fb919`; range
`0822e8bbc60f2c8f333504d60cf3484cc7b6f2b8..476398188481df53cfe8b0adf105aad82c8fb919`
(red `99d884ea9`, fix `476398188`). Spec: `docs/superpowers/plans/2026-11-19-desktop-hot-worktree-slots-plan.md`
on master `3021acaf531d` (S2, D-5, D-9, Desktop hazards).

Verdict: **3 blockers** (D-1 to D-3). Next stage: code.

## Evidence

| Check | Result |
|---|---|
| CP-1 at tip, checkpoint tool run `20261119-123644-d5af` | GREEN exit 0, `C0824S2 passed=121 failed=0`, releases-parent `exit=2 created=False`; `dirty=0 sourceState=clean slot=granted`; `validate` -> `CHECKPOINT SOURCE VALID rows=1` |
| CP-1 at red `99d884ea9` (detached scratch worktree) | RED exit 1, `C0824S2 passed=57 failed=51`; releases-parent `exit=0` |
| Guard union (9 classes) + DerivedImpactIndexTests, one build-slot run, fresh TRX | 108 executed, 107 passed, 1 failed. Per class: TestClassificationPolicy 42, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3, MechanicalPcContract 11, TaskPlatformGuidance 5, C1729LayeringGuard 11, TestLaneCategoryGuard 1 (union 81), DerivedImpactIndex 27 |
| `DerivedImpactIndexTests.committed_index_matches_the_builder` red | Inherited. `scripts/regenerate-derived-impact-index.ps1` on detached base `0822e8bbc` and tip trees gives byte-identical indexes; both differ from the committed file only in `server/Infrastructure/Data/AppDbContext.cs` and `HostLoadModel.cs` (`classes` 608->609, `owed` 583->584). No C# literal names either new script. This is the stale-index failure the fixer reported |
| `check-evidence-diff.ps1` base..tip | commits=2 entries=0 violations=0, exit 0 |
| `audit-land-verification-graph.ps1 -Check` | approved, exit 0 |
| `git merge-tree --write-tree` vs `3021acaf531d` and vs current `origin/master` `c84185e9a` | clean (exit 0) both |

Unlisted runs, with the reason for each: the checkpoint tool build (3 s), the Antiphon.Tests incremental build (16 s) for the guard batch, and two regenerate-index runs that prove the index red is inherited. Every build and test command ran under its own build-slot lease. The land-graph `-Check` started with a 1-minute load of 29.7, above the 16 load gate. All other rows started below 16.

## Blockers

### D-1: a root that contains a forbidden directory is accepted

- **Where:** `scripts/desktop-slot-provision.ps1:79-99` (`Test-ForbiddenSlotRoot`). It checks only "root is under prefix", never "prefix is under root".
- **Failure:** `-Root C:\Antiphon` (also `C:\Antiphon\` and `C:\src`) returns `forbidden=False`. With `-Execute`, the pool's `worktrees` child is `C:\Antiphon\worktrees`, the legacy `card-task-*`/S4a root that D-9 rejects. The script reports it as `unchanged:` and accepts it as the slot worktree root. It also creates `C:\Antiphon\{workspace-slots,retained,repos}` beside `releases`. `-RemoveEmpty -Execute -Root C:\Antiphon` removes `C:\Antiphon\worktrees` when the four children are empty or absent. Observed with the normaliser on Linux, in probe output.
- **Why:** the brief requires that the pool never sit over the release checkouts or the legacy worktrees. Only `C:\` is refused, as a filesystem root.
- **Fix:** refuse when any forbidden prefix is equal to or under the root (`Test-UnderPrefix $prefix $Comparable`), and when any of the four children resolves to a forbidden path. Add witnesses for `C:\Antiphon`, `C:\Antiphon\`, `C:/ANTIPHON`, and `C:\src`, with and without `-RemoveEmpty`.

### D-2: Windows path aliases bypass the textual forbidden-root check

- **Where:** `ConvertTo-SlotPath`, `ConvertTo-DriveSlotPath` and `Get-SlotSegments` (`:32-70`). Normalisation is textual only: it converts slashes, removes `.`/`..` and empty segments, and compares case-insensitively. Those parts are correct: case, trailing slash, forward slash, `..`, and drive-relative `C:src\Antiphon` are all refused. Every later operation uses the normalised string.
- **Failure:** the normaliser returns `forbidden=False` for each of these inputs (observed):
  - `C:\src\ANTIPH~1\pool` (8.3 short name)
  - `C:\src\Antiphon.\pool`, `C:\Antiphon\releases.\pool`, `C:\Antiphon\worktrees.` (Win32 drops a segment's single trailing period, so `New-Item` and `Remove-Item` reach the forbidden directory)
  - `\\?\C:\Antiphon\releases\x`, `\\.\C:\src\Antiphon`, `\\localhost\C$\src\Antiphon\pool` (extended, device, and UNC admin-share forms; on Windows `GetFullPath` keeps them, and the prefix test never matches)
  - `\\server\share\releases\pool` (`Get-SlotSegments` splits non-drive paths on `/` only, so the ancestor-`releases` rule never runs)

  A junction or symlink ancestor, such as `C:\link\pool` with `C:\link -> C:\src\Antiphon`, is never resolved. On the create path, the UNC and `\\?\` forms are refused only by accident: the `DriveInfo` constructor throws, `$info` stays `$null`, free space reads as 0, and the script refuses with `low-free-space`. `-RemoveEmpty` skips the free-space check and reaches `Remove-Item` for those forms.
- **Why:** this is the forbidden-root bypass class the brief makes blocker-grade. The 8.3, trailing-dot and junction forms place the four directories inside `C:\src\Antiphon` or a release checkout, on a drive whose `DriveInfo` works.
- **Fix:**
  - After normalisation, require `^[A-Za-z]:\\`, and refuse `\\?\`, `\\.\` and UNC forms.
  - Refuse any segment that ends in `.` or a space, or that contains `~`.
  - Walk the existing ancestors and refuse any that has `FileAttributes.ReparsePoint`. The walk works on 5.1 through `(Get-Item -Force).Attributes`.
  - Add a witness per vector. The reparse vector can use a Linux symlink fixture.

### D-3: the `core.longpaths` write can land outside the clone

- **Where:** `Add-SlotClonePlan` (`:155-191`) gates only on `Test-Path repos\antiphon\.git`, then runs `git -C <clone> config --local`.
- **Failure:** if `repos\antiphon\.git` exists but is not a valid git directory (an empty directory or an interrupted clone), git discovers the repository upward. `config --local` then writes the enclosing repository's `.git/config`. The read-back reads the same file and sees `true`, so the script prints `longpaths=true` and exits 0. Observed on Linux through `Invoke-DesktopSlotProvision -Execute`: a scratch outer repository gained `core.longpaths=true`, and `rev-parse --show-toplevel` from the clone path named the outer repository. A gitfile `.git` (a linked worktree) writes the common dir's config. Combined with D-2, a root inside `C:\src\Antiphon` reaches the AppHost config, which the plan's Desktop hazards forbid.
- **Why:** the brief makes "a config write outside the clone" blocker-grade.
- **Fix:** before the write, require `git -C <clone> rev-parse --show-toplevel` and `--absolute-git-dir` to equal `<clone>` and `<clone>\.git`, and refuse otherwise. Alternatively, pass `--git-dir`/`--work-tree` explicitly with `GIT_CEILING_DIRECTORIES=<root>\repos`. Add a witness: an empty `.git` inside an outer fixture repository must refuse and leave the outer config unchanged.

## Advisories (do not hold the land; file as cards)

- **A-1:** `-RemoveEmpty` has a check/delete race (`:198-229`). If a child appears between the `Get-ChildItem` check and `Remove-Item -LiteralPath -Force` (no `-Recurse`), PowerShell prompts "has children ... continue". In a non-interactive host it throws (observed). In an interactive host, a Yes deletes recursively, and on 5.1 a recursive delete follows junctions. An enumeration error (access denied) is non-terminating, so the count reads 0 and the directory counts as empty. Use `[System.IO.Directory]::Delete($path, $false)`, which refuses a non-empty directory, and refuse a child that is a reparse point. A symlink in place of one of the four directories: the script removes the link and keeps the target (observed on Linux; NTFS junction behaviour UNOBSERVED).
- **A-2:** plan S4 step 2 (`powershell.exe ... desktop-slot-provision.ps1 -Root C:\Antiphon\slots`) has no `-Execute`, so it is now a dry run. The S2 plan text still describes only `-WhatIf`. Update the S4 command.
- **A-3:** the witnesses cover only literal forbidden prefixes. Add ancestor-root, alias, reparse and invalid-`.git` cases with D-1 to D-3. `releases-parent.created` passed at the red stub; the case still reds through `.exit` and `.message`.
- **A-4:** a UNC root fails through a `New-Object DriveInfo` exception plus a `low-free-space` refusal. An explicit `refused: unsupported-root` is clearer.
- **A-5:** the clone URL is hard-coded (`https://github.com/michal-ciechan/Antiphon`). Consider printing the canonical origin from configuration.

## Checked and clean

- **Dry run:** no `New-Item`, `Remove-Item` or git write. The free-space probe is read-only. The dry-run, whatif and dry-run-longpaths witnesses assert the root is absent and the fixture `core.longpaths` stays `false`. End to end through `pwsh -File ... -WhatIf -Execute`, nothing was created (observed).
- **Idempotence:** a second `-Execute` reports `unchanged:` and leaves 5 root children.
- **Git scope:** the script runs no `--global`/`--system` config, no clone and no `dotnet` (enforced by the AST witness).
- **Windows PowerShell 5.1:** ASCII-only (0 bytes above 0x7F). No ternary, `??`, `?.`, `&&`/`||`, `-AsHashtable` or `ForEach-Object -Parallel`. `$PSNativeCommandUseErrorActionPreference` is an inert variable on 5.1.
- **Script entry:** the `-File` path works for dry run, execute and forbidden refusal (exit 2).

## UNOBSERVED on Windows (the Linux test cannot prove these)

- 8.3 name resolution, Win32 trailing-dot normalisation, and NTFS junction traversal by `New-Item`, `Test-Path` and `Remove-Item`.
- `GetFullPath` and `DriveInfo` with `\\?\` and UNC on .NET Framework.
- The script parsed and run under `powershell.exe` 5.1. The AST check used the pwsh 7 parser.
- `-WhatIf` as a declared switch under `[CmdletBinding()]` on 5.1.
- Git for Windows writing `core.longpaths`.
- Real `DriveInfo` free bytes on `C:`.
- The root-unavailable case: it ran on Linux only, where `C:\` is absent.
