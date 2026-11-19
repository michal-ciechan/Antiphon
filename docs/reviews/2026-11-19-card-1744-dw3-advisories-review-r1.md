# CARD-1744 DW-3 helper advisories: Final review round 1 (2 blockers)

Review task 58087192. Subject: Code task `1eb957bd-ccc5-4bf5-a27b-95c300ecf6a3`, branch
`feat/card-task-1eb957bd`, tip `b5d5cb87f487158116cd1d8f697d3bc0d5b66c37`, range
`5be178005f06aa868bbb038e561c2ef22a654cb5..b5d5cb87f487158116cd1d8f697d3bc0d5b66c37`. Author Grok;
reviewer Claude Opus. Plan `docs/superpowers/plans/2026-11-19-card-1744-dw3-advisories-checkpoints.md`.

Verdict: **BLOCKERS (2)**, both in the new `-SweepStranded` selector (`Write-Dw3StrandedReport` /
`Test-Dw3StrandedName`). The byte-array clear, the exit codes, the ambiguity refusal and the leak
surface are sound.

## Checkpoints (checkpoint tool, server2, at the tip)

| CP | Result | Run |
|---|---|---|
| CP-1 | RESULT passed=279 failed=0, exit 0, validated (`CHECKPOINT SOURCE VALID rows=1`) | 20261119-195337-a4e4 |
| CP-2 | 42/42, 0 skipped, buildSource=verified | 20261119-193940-e1ba |
| CP-3 | 39/39, 0 skipped; C1729 `VIOLATIONS 0 LISTED 162` | 20261119-193940-e1ba |
| CP-4 | 27/27, 0 skipped | 20261119-193940-e1ba |
| CP-5 | 6/6, 0 skipped (C1210 Windows case owed on Windows) | 20261119-193940-e1ba |
| CP-6 | exit 0 (`approved`) | 20261119-193940-e1ba |
| CP-7 | exit 0, digest `ab041a1cb0e999549e95391e38520161e5fd31c93af0a481e67fc79060b42cac`, not re-pinned | 20261119-193940-e1ba |
| CP-8 | exit 0, `commits=2 entries=0 violations=0` | 20261119-193940-e1ba |

The first run's CP-1 was red (277/2, `owner-fail-backup-names-*`) because the reviewer ran a mutant
copy of the same test script at the same time. Both runs use the fixed root
`/tmp/dw3-phase1-secrets-test` (see A-4). CP-1 run again alone was green, and that run is the receipt.
That first run is RED overall, so the CP-2..CP-8 rows have no validated receipt. Their CHECKPOINT lines
show `sourceState=clean` and `buildSource=verified` at the tip.

merge-tree: clean against `origin/master` `3f24857e8251ba0eb735f4b6dd1f4c963f3cc05d` and against
`1443b7ebc82277e9b21cc83d31a686bdb62208c2`. ASCII only: no non-ASCII byte in either script. PS 5.1:
no 7-only syntax in the diff.

## Judged points

1. Sweep. Deletes go through `Remove-Item -LiteralPath` only, on `Get-ChildItem -LiteralPath <dir> -Force -File`
   results from two directories: the parent of `-UserSecretsPath` and the secrets dir. There is no
   glob and no recursion. A directory symlink named `x.dw3part` is not returned by `-File`, checked on
   Linux pwsh. A file symlink is removed as a link and its target stays, also checked. Stranded
   contents are never read: the content mutant below kills three witnesses. **No lock and no age guard
   exist (D-1). The name match is suffix-only and case-insensitive (D-2).**
2. Byte clear and exit codes. The old bug is confirmed by a direct probe: `foreach ($i in @($list))`
   over a `List[object]` holding `byte[]` throws `ArgumentException` in pwsh 7 on server2. The indexed
   loop does not throw. The exit codes were tightened in a scratch copy: the four `Exit -ne 0`
   assertions (`replace-fault-json`, `replace-fault-secret`, `replace-restore-fault`, `delete-recheck`)
   were changed to `-eq 2`, and that copy passed 279/0. Tip witnesses show success 0, rotate success 0,
   dry-run ambiguous 0, and exit 2 for every refusal, rollback, restore fault, owner-only failure, and
   ambiguous `-Execute` / `-Execute -Rotate`.
3. Ambiguity. The refusal sits before the `try` that copies phase1 and stages files, so no secret write
   comes first. The only earlier mutation is the operator-requested sweep. The message prints no key
   name and no value. Matching compares whole planned keys, case-insensitively. A top-level
   `PhoneHomeRunner` object of any case is refused, including a legitimate nested
   `PhoneHomeRunner:Runners:server2`; that false refusal is acceptable, see A-3.
4. Leaks. The diff adds no new child argv. The new `stranded:`, `swept:`, `kept:`, `owner-only-failed:`,
   `recovery:` and `changed:` lines print only paths or fixed text. Mutant check (scratch `git archive`
   copy, then reverted): one mutant prints the generated secret after `New-Dw3Secret`, another appends
   `ReadAllText(path)` to `stranded:`. Result passed=272 failed=7. The killed witnesses are
   `execute-`, `nested-date-`, `rotate-success-` and `clear-bytes-streams-have-no-secret`, plus
   `stranded-list-`, `stranded-execute-` and `stranded-sweep-no-contents`.

## Defects

### D-1 (blocker): `-SweepStranded` deletes a live temp of a concurrent run

- Where: `scripts/desktop-win-phase1-secrets.ps1`, `Write-Dw3StrandedReport`, the sweep loop
  (`Remove-Item -LiteralPath $path`), called at the top level before any lock.
- Failure: run A (`-Execute`) has staged `<guid>.dw3part`, or holds a `.dw3bak` during a Windows
  `File.Replace`. Run B with `-SweepStranded` lists that file and deletes it at once. The file's age
  and the other run are never checked. The tip witness `stranded-sweep` proves there is no freshness
  check: it seeds both files milliseconds before the run, and the run sweeps them. A then throws
  `staged temp is missing` or a FileNotFound. If A was in the partial-replace restore path, it loses
  the on-disk backup of the old `secrets.json`. That file also holds unrelated user secrets.
- Why blocker: the brief names "a delete of a live temp" as blocker grade and asks for an age or lock
  guard. Neither exists.
- Fix: skip any stranded file whose `LastWriteTimeUtc` is newer than a fixed bound, for example
  15 minutes, and print `kept: <path> fresh`. Or hold an exclusive run lock file in the secrets dir
  for the whole run and refuse the sweep while another run holds it. Witness: a fresh `.dw3part` is
  kept under `-SweepStranded`; a backdated one is swept. The existing `stranded-sweep` case must
  backdate its seeds.

### D-2 (blocker): the sweep matches any `*.dw3part` / `*.dw3bak`, not the helper's exact names

- Where: `Test-Dw3StrandedName` (`EndsWith(..., OrdinalIgnoreCase)`).
- Failure: the helper only creates `<32 lowercase hex>.dw3part` and `<32 lowercase hex>.dw3bak`
  (`[guid]::NewGuid().ToString('n')`). The sweep also deletes names such as `Notes.DW3BAK` or
  `copy of x.dw3bak` in the user-secrets dir or the secrets dir. Both directories come from operator
  parameters (`-UserSecretsPath`, `-SecretFile`), so a mistyped path widens the reach. Its own
  `Remove-Dw3Tracked` and `Remove-Dw3Temp` use case-sensitive `EndsWith`.
- Why blocker: the brief requires exact literal names with the helper's exact stem.
- Fix: match `^[0-9a-f]{32}\.(dw3part|dw3bak)$`, ordinal and case-sensitive, in both listing and
  sweep. List but never delete anything else. Witness: `Notes.DW3BAK` and `x.dw3part` stay present
  after `-SweepStranded`, while the guid-named seeds are swept.

## Advisories (to cards; they do not hold the land)

- A-1: `-SweepStranded` deletes in a dry run and under `-WhatIf`. The `stranded-sweep` witness runs
  without `-Execute`. Make `-WhatIf` list only.
- A-2: in the same run, the sweep deletes a `.dw3bak` that the `recovery:` line just told the operator
  to rename back over a missing target. Consider keeping a `.dw3bak` when the target in its directory
  is missing.
- A-3: the `shared-secret-ambiguous` message does not say what to do. Name the action, for example
  "merge or remove the case-variant or nested PhoneHomeRunner entry, then re-run", without key names.
- A-4: the test script uses a fixed temp root (`/tmp/dw3-phase1-secrets-test`). Two runs on one host
  collide, as observed in this review. Use a per-run unique root.
- A-5: tighten the four `Exit -ne 0` assertions to `-eq 2`; the tip already satisfies them.
- A-6: DW3-WIN (the Windows ACL and the `File.Replace` backup path) stays UNOBSERVED. It needs a
  Windows observation before the operator run.

## Not discharged

Mutation was waived by the operator on 2026-11-12. The PCs stay pending. Required manual acceptance
is the Windows observation (A-6) and stays pending.

Process note: the CP-1 rerun started at a 1-minute load of 22.17. That is above the brief's start bar
of 16; the other runs started below it.

The kept `tests/Antiphon.Tests/bin-dw3adv/` from the first run (RED) is left for mirror retirement.
