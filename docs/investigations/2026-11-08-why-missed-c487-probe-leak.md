# Why missed: c487 probe temp leak

Debug task ee315192. Investigate task c04ce26d found it
([server2 /work volume full](2026-11-08-server2-work-volume-full.md), on
`feat/card-task-c04ce26d`). On 2026-11-08 server2 `/tmp` held 2503
`c487-probe-<32hex>` directories, about 48 MB each and 120 GB in total. The
volume had 1.8-6 GB free. This was not a failed rc chunk: no assertion ever
failed. The same five items still apply, because the miss is a review and
selection miss.

## What leaked, per run of the class

`tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs`
`RunProbeAsync` wrote a TUnit project into `/tmp/c487-probe-<guid>`, ran
`dotnet build` and the probe exe, and returned the path. Nothing deleted it.
Six probe calls run per pass of the class (G065 x1, G066 x1, G067 x2,
G069 x2), which leaves about 290 MB. The same class also kept six `c487-tw-*`
tripwire directories and four `c487-reg-*.txt` registry files per pass,
each about 10 KB.

Fix (master only): `WithTempDirectoryAsync` creates each of these
directories and deletes it in a `finally` with `StartRefGit.DeleteDirectory`,
which is best-effort and never fails the test. That covers a throw, a failed
build and a failed assertion inside the body. G065 now reads the probe
build inside the directory through an `inspect` callback. All registry files
go through `C1383WithRegistry`, which already deleted its file. Witnesses:
`C487_G066`, `C487_G075` and the two `C487_probe_temp_directory_is_removed_*`
tests.

## Introducing commit/card

introducing commit/card: afae299d415a87cbb94f0e7c3e7ae31c81fb5557 (CARD-0487 S1-S3, 2026-09-11)

Linux exposure: 93f18264 (CARD-0681 part 1, 2026-09-24) fixed the probe's
apphost path off Windows. From then on the probe built and ran, and leaked,
on server2.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 62016c5f, b87525d0, c97e1a38, 19f02f1b, b0de94fc, 37173dd5 (CARD-0487, 2026-09-11/12), scope Full. Scope labels
  predate CARD-1781. This note did not re-read each round's exact filter. The test design puts G065-G069 in
  this class, so every round that verified S1-S3 ran the probes, and they passed.
- fe1afc87, aaa02d5f (CARD-0681, 2026-09-24, Windows), scope Full, selection: the Linux Unit lane repair
  classes, including this class.

## Outside the selection

why the failing test was outside that selection (lane/category/path): lane Unit, category `Unit`,
path `tests/Antiphon.Tests/TestHelpers/`. The class was always in the selection, and nothing failed.
The defect is a resource leak that no test or review asserted:

- The CARD-0487 plan said "Each fixture owns a GUID root", but nothing required the fixture to remove
  that root. Its discovery spike kept the probe in `.antiphon/c487-probe/` on purpose, and the test copied
  that shape into `Path.GetTempPath()`.
- Reviews check assertions and counts. A green run that leaves 290 MB behind looks the same as a clean
  one, and a single leaked directory is invisible on a Windows desktop `%TEMP%`.
- Nothing watched disk. Server2 has no free-space alert. The CARD-1677 S2 runner temp sweep registers
  `c487-probe` (`src/Antiphon.SessionRunner/RunnerTempSweep.cs`), but it shipped off
  (`TempSweep:Enabled=false`, operator decision 2026-11-03). The leak grew from 12 directories a day
  (11-02) to 708 (11-08). The investigation attributes that growth to more server2 Code, Review, Merge
  and rc runs of `Antiphon.Tests` that include this class. Only free space running out made it visible.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs` -> `/*/*/TestClassificationPolicyTests/*`

Selection was not the gap, so that entry only keeps the witnesses in the run. The check that catches
this class of defect is a test-hygiene guard on the temp root. The proposal:

1. The checkpoint tool (`tools/Antiphon.Checkpoints`) and `scripts/build-slot.ps1` give each test row its
   own temp root: `TMPDIR`/`TEMP`/`TMP` = `<run>/rows/<CP>/tmp`. That root is created empty and owned by the
   row.
2. When the row ends, the tool records the entries and bytes left under it. Above a small threshold
   (for example 1 MB or 10 top-level entries), it fails the row with `temp_residue`, names the top
   entries and their prefixes, and deletes the root.

This needs no attribution across concurrent processes on a shared `/tmp`. A whole-`/tmp` snapshot guard
would need that attribution and would be flaky on server2. It costs one directory per row. With it,
CARD-0487's first Code checkpoint would have gone red.

## Other /tmp families measured on server2 (2026-11-08)

| Family | Entries | Avg size | Producer | Status |
|---|---:|---:|---|---|
| c487-probe | 2509 | 48 MB | this class | fixed here |
| c487-tw / c487-reg | 2517 / 1676 | 10 KB | this class | fixed here |
| antiphon-bridge-queue | 9727 | 8 KB | `BridgeQueueHarness` | left; `DisposeAsync` deletes, so the leak is `PreserveDatabaseOnDispose`, undisposed harnesses or a throw inside `CreateAsync` after `mkdir`. Finding those needs a per-caller audit. About 80 MB in total. |
| antiphon-land-verify | 552 | 2.4 MB | production `AgentTaskLandService.VerifyUnderSlotAsync` | left; production code retains unique outputs by design. Changing it needs a product card and Review. |
| pty-test-root (c1653-sock/peer/adopt/launch, c79-stop, adoption-tests, c1636-adopt) | ~3640 | 0.04-8.6 MB (~6.5 GB total) | `Antiphon.SessionRunner.Tests` `TestSessionLogRoot.Create` | left; a different assembly whose roots `PtyHostLeakSweep` keeps for diagnostics. The fix needs teardown ownership per fixture. |
| unregistered c459-, c723-, rcspeed-, c716-, antiphon-c448-, antiphon-c1150- | 2480 / 520 / 492 / 459 / 452 / 405 | not sized | various | not examined |

## Exemption

same cause as an already written note: none
