# CARD-1744 dw3 advisories review, round 2

Subject `origin/feat/card-task-1b3802c5`, tip `9867216fc31648eff54e98ca3567d1b153c1e78a`.
Round 2 diff only: `b5d5cb87f487158116cd1d8f697d3bc0d5b66c37..9867216fc31648eff54e98ca3567d1b153c1e78a`
(red 11b37456, fix a2e201e3, backup-switch repair 80860ef7, plan floor 9867216f).
Reviewer task 212806cc (Claude Opus; the author is Grok). Round 1 was 58087192 (D-1 live temp swept, D-2 broad name match).

Verdict: CLEAN. No blockers. Round-1 D-1 and D-2 are closed. Three advisories follow.

## Round-1 blockers

- D-1, live temp swept. Closed for `.dw3part`. The age guard reads `LastWriteTimeUtc`
  (`Test-Dw3StrandedFresh`). A live temp is created new by `New-Dw3OwnerOnlyEmpty` and written at
  once, so it is always fresh for 15 minutes. A future mtime is kept as fresh (probe P5). `.dw3bak` is
  never swept without `-SweepBackups` (probe P3a; witness `stranded-bak-keep`).
- D-2, broad name match. Closed. Listing (`Get-Dw3StrandedPaths`) and the sweep loop both apply
  `-cmatch '^[0-9a-f]{32}\.(dw3part|dw3bak)$'` to the leaf name (`FileInfo.Name` /
  `Path.GetFileName`). A path separator cannot reach the regex. Enumeration is `-File`,
  non-recursive, in the two target directories only. Witnesses cover `Notes.DW3BAK`, `abc.dw3bak`,
  uppercase hex, a 32-hex `.txt` and a directory with an exact name. Probes: a file with an exact name in a
  subdirectory is not listed (P6). A symlink with an exact name pointing at `secrets.json` is listed and
  kept as fresh, because its own mtime is new. Its target stays intact (P1). `Remove-Item` on a file
  symlink removes the link only.

## Attack results (probes against the tip, scratch dirs only)

| Probe | Result |
|---|---|
| P1 symlink `<hex>.dw3part` -> secrets.json, `-Execute -SweepStranded -SweepBackups` | exit 0, link kept fresh, target intact, no content printed |
| P2 `<hex>.dw3bak` + trailing LF, `-SweepBackups` | listed and deleted (see A-1) |
| P3a stale `.dw3bak` (mtime -600 min), `-Execute -SweepStranded` | exit 0, kept `backup-of-secrets-keep-until-confirmed` |
| P3b same with `-SweepBackups` | deleted; exit 2 is the designed secret-exists refusal of the second `-Execute` (no `-Rotate`) |
| P4a dry run `-SweepStranded -SweepBackups` (stale part + bak) | exit 0, 2x `would-sweep`, both files exist |
| P4b `-Execute -WhatIf -SweepStranded -SweepBackups` | exit 0, 2x `would-sweep`, both files exist |
| P5 future mtime part | kept `fresh` |
| P6 exact name in a subdirectory | not listed, kept |
| P7 `-Execute` without `-SweepStranded` | exit 0, `sweep: refused`, file kept |
| agent shell (`CLAUDECODE` set) | `REFUSED: agent-shell` exit 2 before any listing or sweep |

Leak mutants. These were run as the real test script against scratch copies, and the worktree was never edited:

- mutA: the `stranded:` line also prints `ReadAllText($path)`. RESULT passed=350 failed=12. Every
  `stranded-*-no-contents` witness failed.
- mutB: `Write-Host` of `$secret` after `New-Dw3Secret`. RESULT passed=350 failed=12
  (`execute-streams-have-no-secret`, `rotate-success-streams-have-no-secret`, `stranded-sweep-no-secret`, ...).

No secret value or stranded content appeared on any stream in the clean runs. The sweep path makes no
child-process call, so it adds no argv.

## Advisories (card them; they do not hold the land)

- A-1. `$` in `-cmatch '^...(dw3part|dw3bak)$'` also matches before a trailing LF. On Linux a file named
  `<32hex>.dw3bak<LF>` is listed and deleted. Because `EndsWith('.dw3bak')` is false for that name, it
  is swept as a part, without `-SweepBackups` (P2). This is not reachable on the desktop: NTFS forbids
  LF in names. On Linux it needs a crafted name in an owner-only directory. Fix: end the
  regex with `\z` and add an LF-name witness.
- A-2. A live `.dw3bak` is not protected by the age guard. On Windows, `File.Replace` renames the old
  target to the backup, which keeps the old mtime of `secrets.json`/`phone-home`. During a concurrent
  run's swap window the backup therefore looks stale, and `-SweepBackups` deletes it. The default
  (no switch) keeps it. This needs an explicit operator switch, so it is advisory. The help text says only
  "A .dw3bak is kept unless -SweepBackups is also set". Add "use -SweepBackups only when no other
  run of this helper is active; a backup keeps the old target's mtime, so the age guard does not
  cover it", and print the same caution when `-SweepBackups` is given.
- A-3. The stranded sweep runs before later refusals (`shared-secret-ambiguous`, secret-exists without
  `-Rotate`). Those refusals then say "Nothing was changed" after a requested sweep removed files.
  The wording could say "No target was changed".

## Rows (all at 9867216fc, server2 Linux, each in its own build-slot lease)

| Row | Result |
|---|---|
| CP-1 `scripts/test-desktop-win-phase1-secrets.ps1` (alone) | RESULT passed=362 failed=0, exit 0, 3m02s |
| CP-2+CP-3+CP-4 one run, 10 classes (`TestClassificationPolicy*`, 8 guard classes, `DerivedImpactIndex*`), ANTIPHON_LINUX_COMPILER_POLICY=0 | total 108, failed 0, skipped 0 (floor 42+39+27=108), 5m31s |
| CP-5 six `WindowsUnitPlatformAdmissionTests` methods | total 6, failed 0, skipped 0 |
| CP-6 `audit-land-verification-graph.ps1 -Check` | exit 0, approved |
| CP-7 `test-live-lane-run.ps1 -PrintInputDigest` | exit 0, digest ab041a1cb0e999549e95391e38520161e5fd31c93af0a481e67fc79060b42cac (not re-pinned) |
| CP-8 `check-evidence-diff.ps1` 5be17800..HEAD | commits=6 entries=0 violations=0, exit 0 (round range b5d5cb87..HEAD also 0) |
| merge-tree vs origin/master 645159add | clean, tree 0d1868df26654d94f85d04736f436c67fcd2bb11 |
| ASCII | 0 non-ASCII bytes in both scripts |

The build of `tests/Antiphon.Tests` (native output, UseAppHost=false) succeeded in 5m27s.
The diff's new constructs work in PS 5.1: `[int]::TryParse` with `[ref]`, `-cmatch`, `[datetime]::UtcNow.Subtract`
and `String.EndsWith(string)`. No 7-only syntax was added. The Windows ACL and `File.Replace` branches stay
UNOBSERVED on Linux (DW3-WIN pending). PCs stay pending; mutation checks are waived by the operator (2026-11-12).

One deviation: the first probe run started with the 1-minute load at 23.5. Load rose between the check and the start.
It held its lease for 9 s and then refused `agent-shell`. Every later row was gated below 16.
