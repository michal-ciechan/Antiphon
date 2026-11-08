# Review: CARD-1731 faster G-287 evidence path check (round 1, Final)

Review task 6c5b2084 (Claude Frontier) of Code task 317b5f73 (Grok), branch
`feat/card-task-317b5f73`, tip `cf0aba324bdfff15e10f4ef0120c84a6c380f345`,
range `9e7ff34988cd8c273d94db3c002975d93ea26f9e..cf0aba324bdfff15e10f4ef0120c84a6c380f345`
(three commits: red 555022d714, fix af40099a19, note cf0aba324b). Master at review time
`74a8004f405bc54666fea33889839f0ff4d62ec7`; `merge-tree --write-tree` is clean, tree
`da40b8e6a41c12824fd3673b25bcf2fc31f57bd1`. Reviewed on server2 (Linux, .NET 9.0.22 test host,
pwsh 7.5.4 / .NET 10 for probes), load 23 to 28 on 24 cores.

## Verdict

CLEAN. No blocker. Four advisories (A-1 to A-4), none holding the land.

## 1. Refusal-set equivalence

Only the per-component loop of `Resolve-ReleaseAuthorityEvidencePath` changed. The pre-loop
refusals (blank, `IsPathRooted`, lexical `GetFullPath` plus `StartsWith(root + sep)`) are
byte-identical, so absolute, drive-relative, UNC, rooted-backslash and `..` escapes behave as
before on both OSes. Inside the loop the old test was `LinkTarget -ne $null -or ReparsePoint`
on an existing component; the new test is `ReparsePoint` from `File.GetAttributes`. On Windows
`LinkTarget` is non-null only for symlink and mount-point reparse tags, and on Linux only for
`S_IFLNK`, which .NET also reports as `ReparsePoint`, so the old refusal set is a subset of
the new one for every existing component. Neither walk followed links when reading attributes
(`GetFileAttributesEx` and `lstat` first). Missing components (`FileNotFoundException`,
`DirectoryNotFoundException`, including a file used as a directory and a child of a broken
link) are skipped exactly as before and the caller's `Test-Path -PathType Leaf` still refuses
them as `chunk-evidence-missing`. The root itself is still never checked. There is no state in
the function; the fixture's last row proves a component that becomes a link is refused on the
next call. TOCTOU is unchanged in shape and narrower in time (one syscall per component
instead of three cmdlets). A component that disappears mid-walk is skipped like a missing one.

Linux probe (pwsh, same tree, old function text from the base commit and new text from the
tip, `$ErrorActionPreference = 'Continue'` as in `publish-release.ps1`): identical verdicts
for ordinary, directory symlink child, relative symlink child, file symlink, broken link,
broken-link child, missing file, file-as-directory, mode-000 directory, trailing slash,
`link/../x`, `C:evidence`, `\\server\share\x.trx`, `\evidence\suite.trx`, trailing dot and
`file:stream` (the last five are plain Linux filenames and both walks admit them). The only
difference is a component under a mode-000 directory: old admitted it at the resolver (the
downstream leaf check then refused `chunk-evidence-missing`), new rethrows the `IOException`
(A-1). `File.GetAttributes` on Linux: directory symlink `Directory, ReparsePoint`, file
symlink `ReparsePoint`, broken symlink `ReparsePoint`; a symlink to a directory is flagged.

## 2. Witness coverage

`C1731_EvidenceRefusals` (13 rows, 17 seeded shapes, expected values computed independently
of the resolver, no self-compare) covers: ordinary, missing file, missing parent, blank, empty,
absolute, `../`, deep `..`, root `.`, `..` and `.` that normalise inside, directory link
outside, directory link inside the root, file link, broken link and its child, hard link
(admitted), the full seeded set, and the no-cache post-replacement row. PC-5 (skip the
`ReparsePoint` refusal) fails six rows: outside link, inside link, file link, broken link,
seeded set, post-replacement; the Code report's 6 of 13 is consistent. `C1731_EvidenceWalk`
(611 resolutions under 15 s) failed the old walk at 115 to 138 s, so it is a real tripwire.
Not covered: Windows-only shapes (drive-relative, UNC, ADS, device names, trailing dot or
space, case-folding), a link in a middle component of a three-segment path, and native
Windows execution of the 13 rows (the fixture falls back to junctions without symlink
privilege). The Windows-only shapes are refused by unchanged pre-loop lines or normalised the
same way by both walks, so this is a documentation gap (A-2), not a behaviour gap.

## 3. Timing claim and budget

Fresh run at the tip (checkpoint run `20261108-003904-7b44`, CP-1, `C1731_TIMING_ROOT`
phase records, load 25 to 28, the guard row running beside it for its first five minutes):

| Method | TRX duration | body / gate / restore | Code host TRX | Decision doc or smoke |
|---|---:|---|---:|---|
| `C599A_ExecutionLedger` | 63.2 s | 55.2 / 19.6 / 19.5 s, 47 restores | 43.7 s | body 131.0 s, gate 106.6 s; killed at 300.2 s |
| `C599A_ExecutionLedgerChunks` | 45.3 s | 40.4 / 16.2 / 12.9 s | 58.4 s | 247.8 s |
| `C599A_ExecutionLedgerCounts` | 30.5 s | 24.8 / 6.9 / 6.7 s | 33.4 s | 76.7 s |
| `C599A_ExecutionLedgerJoins` | 40.9 s | 33.7 / 10.9 / 12.1 s | 37.7 s | 160.4 s |
| `C599A_ExecutionLedgerMarkers` | 63.3 s | 56.2 / 19.2 / 18.6 s | 57.3 s | 213.0 s |
| `C599A_ExecutionLedgerReceipt` | 28.6 s | 24.4 / 10.1 / 7.2 s | 50.5 s | 216.3 s |

The gate for 47 variants fell from 106.6 s to 19.6 s (0.42 s per gate); restore is now the
equal term. The work is not dropped: the ledger case bodies, variant counts (47 restores) and
every assertion are untouched by the diff, which changes only the resolver loop, adds the
fixture, dot-sources it, adds two test methods and the note. `ScriptHarnessOptions.Default`
is still 300 s with no per-case override; the worst method now sits at 4.7x headroom under a
load of 28. The walk case body (fixture plus 611 resolutions) was 1.2 s.

## 4. Documentation

The function comment, the commit messages and the mutation note's new section are accurate
(one attribute read per existing component, no cache, staleness none, PC-5 and PC-6 pending,
300 s unchanged). See A-2 and A-4 for the two sentences that should be added.

## Advisories

- A-1 (behaviour, fail-closed direction, not a blocker): an attribute read that fails for a
  reason other than not-found (Linux `EACCES` under an unreadable directory gives
  `IOException`; on Windows an access-denied that also fails the `FindFirstFile` fallback gives
  `UnauthorizedAccessException`, an invalid name gives `IOException`) now propagates out of
  the resolver. `publish-release.ps1` has no `try`/`catch`, so the publisher terminates with an
  unhandled error instead of printing `REFUSED: publication gate (chunk-evidence-missing:...)`.
  Nothing is published either way and no previously refused path is admitted. Either document
  this in the function comment or return `''` on those errors so the gate names a reason.
- A-2 (docs): add to the mutation note that the Windows-only shapes and native Windows
  execution of the 13 rows are not witnessed by this slice, and consider one row with a link
  in a middle component (`evidence/mid-link/x.trx`).
- A-3 (performance standards): the 15 s walk ceiling rests on one Linux sample (0.153 s); no
  Windows sample exists. Record one from the first Windows run of the class.
- A-4 (docs): the note's first section still says "No deliberate mutant was executed by
  Code" while the new section records local reds that were restored; one reconciling sentence.

## Verification evidence

- Checkpoint run `20261108-003904-7b44` at `cf0aba324bdfff15e10f4ef0120c84a6c380f345`,
  `sourceState=clean buildSource=verified`, verdict GREEN exit 0, `CHECKPOINT SOURCE VALID
  source=cf0aba324bdfff15e10f4ef0120c84a6c380f345 rows=2`, output `bin-c1731-rv-main/` deleted.
  Plan extract: `.antiphon/c1731-review-plan.md` (ignored).
- CP-1 `/*/*/(C599SnapshotTests*)|(ReleaseGatePublicationAuthorityTests*)/*`: executed 19,
  passed 19, failed 0, skipped 0; slot granted, waited 60 s, executed 612.6 s.
- CP-2 guard batch `(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(C1729LayeringGuardTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)`:
  executed 75, passed 75, failed 0, skipped 0; slot granted, waited 180 s, executed 140.1 s.
- Build: one cold `tests/Antiphon.Tests` build inside the run (856 s on the loaded broker); the
  checkpoint tool itself was built once through `build-slot.ps1` (lease
  `9f63817b-cbd0-42bf-911f-372d85912ac1`, waited 30 s, held 15 s). No other build or test
  driver ran.
- `check-evidence-diff.ps1` over the range: commits=3 entries=0 violations=0.
- Not rerun: the whole Unit lane. The diff contains no compiled production code (two
  PowerShell scripts, one fixture, one Integration/Slow test class, one note); the
  classification, slow-registry and layering guards that police that are the CP-2 row.
- PC-1 to PC-6 stay pending for SourceLanding Mutation; the Code's local reds are not credit.
