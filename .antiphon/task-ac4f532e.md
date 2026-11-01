# CARD-1567 packer census

Outcome: already fixed on master `679b696bd178e3c8bb14b32fcd04a1c26b148435`. This pass changed no product source. Close the card. Landing stays with the orchestrator while qualification c8a7bcee holds the desktop.

## Invariant

Every planned qualification chunk stays under the 60-minute antiphon watchdog with headroom, and that watchdog stays a hang detector at 3600000 ms. The failure avoided is an unpriced heavy class packed into one launch that the watchdog kills while tests are still finishing (groups 15-18, 992 nodes, lost TRX).

## Already on master

The seconds packer, the heavy-class prices, the 2400 s antiphon ceiling and the 1800 s E2E ceiling are ancestors of this master.

| Commit | What it holds |
|---|---|
| `0b10929257c8eeee4dfb66eef72a1af0a4df346d` | Heavy-class prices and the launch-estimate refusal |
| `f46c44bc1ad6054a2f944684ed8a02bc06b9bb71` | Per-case E2E prices and the 1800 s E2E ceiling (CARD-1584) |
| `34f81022019a97f6c4284ecd33e4d3bfab043037` | Admitted ceiling paired with the suite watchdog |

Code task 5a0bdb0b and Merge task 1bc9ff8a settled on 2026-10-23. `origin/feat/card-task-5a0bdb0b` at `08d4020722628c0488932b80cb18389d4ad591bc` stays off master; the landed copy is `0b1092925`. `origin/feat/card-task-1bc9ff8a` is an ancestor of master.

`tests/test-execution-policy.json` watchdogs: antiphon 3600000, e2e 7200000. `Get-NightlyLaunchEstimateCeilingSeconds` returns 2400 for antiphon and 1800 for e2e. A planned chunk above that ceiling throws `launch-estimate-ceiling` before any test process.

## Packer run

`Resolve-NightlySuiteChunks` on `scripts/lib/nightly-class-costs.json` (771 rows: 711 measured, 26 observed-bound, 29 conservative-bound, 5 pace). The stated fraction is the card's 40-minute guard, 2400/3600.

Antiphon, Windows rc exclusions, `scripts/fixtures/nightly/c1410-antiphon-listing.txt`, 19012 nodes:

- 150 launches
- sum 106343.8 s
- max 2088 s (58% of 3600), `all-group-8` `LandNonCodeSkipTests`
- above 1800 s (50% of 3600): 3
- above 2400 s: 0
- above 2880 s (80% of 3600): 0

The three lone classes above 50% are `LandNonCodeSkipTests` 2088, `AgentTaskLandPersistenceFailureTests` 1980, and `AgentTaskLandActiveSourceClaimTests` 1920. Twenty-one lone launches sit above the 900 s packing target. A multi-class bin stops at 900 s.

Next antiphon launches: HalfReset 1776, RollingVolumeRecycle 1675.6, PostLandMutationDelivery 1662, CheckpointMatrix 1636.1, WorktreeGuardedCleanup 1593, RunCheckpointScript 1420, PostLandMutationCleanup 1398, LandRemovalMatrix 1360.6, ChannelOutboundUnifiedTransport 1277.

E2E, current `tests/Antiphon.E2E` source census, 183 nodes, 4 Windows exclusions:

- 75 launches
- sum 61710 s
- max 1500 s (42% of 3600, 21% of the 7200 s E2E watchdog), `all-group-1` `ContractSnapshotTests`
- above 1800 s: 0
- above 2880 s: 0
- 21 launches above 900 s, next is `WorktreeRetirementDeliveryE2ETests` at 1500 s and `AgentTaskLandDeliveryE2ETests` splits at 1200 s

## Witness

`TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-1567-costs -- pwsh -NoProfile -File scripts/test-rc-roster-repairs.ps1 -Case Costs`

Lease `2578d538-f322-4ed3-9182-4203e827a0a8`, waited 0 s, held 4 s, maxcpucount 6. 4 passed, 0 failed: prices retained, 2401 s refused, 2400 s accepted, antiphon watchdog still 3600000.

## Measured Windows run

Audit `e7a0b7ac14df91fe135abeede8a20b912b51da9d` on `origin/feat/card-task-8cf13b6c` (`docs/investigations/2026-11-01-test-timing-stats-d47848cc.md`, doc only, off master). Finished run d47848cc: max chunk wall 1516 s, 42% of 3600. Every measured chunk finished under 1800 s. The 29 non-pass chunks record timedOut false.

`LandNonCodeSkipTests` is priced 2088 s on the 42-node c1410 denominator. The Windows census in that audit is 111 cases. The same per-node rate prices the class past 2400 s, so this packer splits it. d47848cc recorded `all-split-12` at 612 s and `all-split-15` at 577 s.

## Left for other cards

- CARD-1638 (Backlog) refits the biased model. Planned/measured was 0.51 overall, E2E 0.23, and small suites 18-40x low because a launch costs about 20 s. It also adds cross-run timing history. PersistenceFailure was planned 1980 s with a 1695 s test-sum. ActiveSourceClaim was planned 1920 s and ran 1327 s. Recheck those two after the refit.
- CARD-1563 (Review) owns per-test and per-row timeouts at about 2x p95. The open flake is `DelegateBranchDeliveryTests.C1213_QueuedBranchRecovery`, 277 s of a 300 s timeout. That audit leaves launch packing as it is.
- CARD-1584 is still Review on the board. Its prices and 1800 s ceiling are on this master.
- CARD-1404 is still InProgress on the board. This packer is the cost balance it asked for.

## Next

```
--- next stage ---
next: none
handoff: close CARD-1567
artifact: .antiphon/task-ac4f532e.md
```
