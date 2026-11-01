# Debug 1c2161bd — group 126 D-1 effective zone

Subject tip was ce291773d354e150de324ca80460804ba0098c42. Fix is 3cba1be6db7a370f7f1903c86fbefc97b7e8d7f3 on feat/card-task-1c2161bd. No land.

## Fix

The projection turns date-shaped strings through ConvertFrom-Json, which follows the process zone. Passing a resolved TimeZoneInfo into every conversion is not a small change: the parses are implicit and spread through the journal reader. The check and the writer identity now use `[TimeZoneInfo]::Local`, the zone those conversions actually use.

`Assert-MpOffset` refuses a requested offset the process does not have, with `zone-not-applicable requested=<TZ> expected=<n> actual=<local minutes> id=<local id>`. `Get-TestPhaseWriterEnvironment` records that same Local id and its current offset, so two hosts that share a TZ string but not a process zone no longer look identical at the resume comparison in `test-phase-evidence.ps1`.

`ProjectionPreservesLegacyBytes` prints `SKIP zone-not-applicable <cell> <reason>` and counts it. On Linux the three IANA zones still have to apply (applied=12, skipped=0). On Windows, skipped must be greater than 0 and applied less than 12, because one host offset cannot be 0, 330, and -240 at once.

## Witness

Red at ce291773, harness `zone-effective` with `TZ=India Standard Time`, before the fix:

`zone-effective-writer=Core|7.5.4|.NET 9.0.10|||India Standard Time|+05:30 expected-suffix=|UTC|+00:00`

The process zone was UTC. The pinned zone and the writer identity were India Standard Time +05:30. Commit 04330ed0ed9552ef4ce00dca7cf18333043a5e80.

Green after 3cba1be6, same case, and the same shape on `v3-write` with `MP_EXPECT_OFFSET=330`:

`zone-not-applicable requested=India Standard Time expected=330 actual=0 id=UTC`

`zone-map` and `successor-drain` stayed ok. IANA `v3-write` for UTC, Asia/Kolkata, and America/New_York stayed ok. The Kolkata writer identity was `Core|7.5.4|.NET 9.0.10|||Asia/Kolkata|+05:30`.

## Mutants (harness, then restored)

1. Offset check pointed back at `Get-TestPhasePinnedTimeZone`. `zone-effective` returned `zone-effective-pinned-accepted pinned=India Standard Time offset=330 local=UTC offset=0`.
2. IANA-to-Windows branch removed from `Resolve-TestPhaseTimeZone`. `zone-map` returned `windows-shaped-iana-unmapped`.

## Tests

One build, lease 485117e0-d825-4318-9d6a-66a453f2cbc6, `OutputPath=bin-d1zone126/`, `UseAppHost=false`, `TEMP=/tmp`, 0 errors, 4m 32s.

One run of that output, lease c371616f-09c3-4034-a695-305c5c9ba956, `--timeout 25m`, filter `/*/*/(RcManifestJournalTests*)|(RcRosterRepairTests*)/*`. total 10, failed 0, skipped 0, 3m 09s. Stdout: `zone-grid applied=12 skipped=0` and `zone-effective zone-not-applicable requested=India Standard Time expected=330 actual=0 id=UTC`.

## Other gates

- check-evidence-diff ce291773..3cba1be6: commits=2 entries=0 violations=0.
- merge-tree onto origin/master and origin/rc/2026-11-01, both 679b696bd178e3c8bb14b32fcd04a1c26b148435: clean, tree d9f0087fc6ed36d463f35d3748810bfe19b9de00.
- land graph -Check, no -Update, tool from the same build: `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69`, `approved`, exit 0.

## Native Windows rerun of group 126

Linux owns the cross-zone proof above. On Windows, `TZ` does not move `TimeZoneInfo.Local`, so the grid must print a `SKIP zone-not-applicable` line for every cell whose requested offset is not the host offset at 2026-10-30 12:00, and the summary must show skipped > 0 and applied < 12. The cell whose offset matches the host zone still runs and must pass. `applied=12 skipped=0` on Windows means the pinned-zone check is back. `EffectiveZoneCheckRefusesPinnedWindowsId` must report `zone-not-applicable` with the host id and the host offset (expected=330) whenever the host zone is not already India Standard Time. If the host zone is India Standard Time, that witness prints `SKIP zone-effective-not-applicable` because there is no divergence to show.
