# CARD-1389 repair: a reclaimed lock must stay readable

Repair after Final Review 37547310 defect D1. Start ref `960899389e30e0ceb5b823dd5daedbf2e150e589`. Master first. This branch does not backport. Command rows are the closed list below. No Unit lane.

## Decisions

- **D-1 - Downgrade after the winner publishes.** `Enter-NightlyReclaimedLock` still opens `FileMode.Open`, `FileAccess.ReadWrite`, `FileShare.None` for the identity compare, the in-place rewrite, and `lock-reclaim.json`. Nothing is deleted. After that publish succeeds, close the exclusive handle and reopen `FileMode.Open`, `FileAccess.Write`, `FileShare.Read`, and keep that stream in `$script:NightlyLockStream`. The gap is safe because a contender still compares under its own exclusive open and a stale snapshot fails the identity check. `Exit-NightlyExclusiveLock` and the `-ContinueRunId` hop read through `Read-NightlyLockRecord` (`File.ReadAllText`). On this Linux host `FileShare.None` makes that read fail in the same process; `FileAccess.Write` with `FileShare.Read` allows it and still refuses a second `FileShare.None` open. Windows `FileShare.None` also blocks readers, so the same downgrade is required there. This host is Linux; Windows is untested.
- **D-2 - Reboot only for two registry boot ids.** `dead-owner-rebooted` applies only when the host matches and both boot ids are decimal (`^[0-9]+$`, the registry `BootId` shape). If the host matches and the boot ids differ but either one is not decimal, refuse `lock-held`. A `LastBootUpTime` `o` string such as `2026-01-02T03:04:05.0000000Z` is not decimal. The recycled-pid witness plants decimal ids `156` and `157`. A non-decimal pair, including a Linux `/proc/sys/kernel/random/boot_id` UUID, takes this refuse.

## Tests

Cases stay in `scripts/test-nightly-run.ps1`. `-Case` dispatches one function. Do not add them to the `C487_G0` or `C545_` loops.

- `C1389_ReclaimThenExitReleasesLock`. Seams: nothing alive, boot `157`, host `host-current`, parent id 0. Plant a dead same-boot lock, `Enter-NightlyExclusiveLock` reclaims, `Exit-NightlyExclusiveLock` removes `run.lock`, and the next enter returns `acquired`.
- `C1389_HopChildReadsReclaimedLock`. The same reclaim, then a child `pwsh` calls `Enter-NightlyExclusiveLock -ContinueRunId` with the new run id and this process id. The child sees that run id and `Reason=hop`.
- `C1389_OldFormatBootIdRefuses`. Plant boot id `2026-01-02T03:04:05.0000000Z`, current boot `157`, same host, owner pid alive. Result is `Ok=false`, `Reason=lock-held`, the planted run id remains, and `lock-reclaim.json` is absent.

### Checkpoints

Command rows. CP-1 through CP-3 against the test-only commit are the red witness (harness exit 1). The green run is CP-1 through CP-9 together, against the production commit, all exit 0. `TEMP=/tmp` is set on every row. `Expect` is the green rule.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | n/a | reclaim-exit | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1389_ReclaimThenExitReleasesLock` | D1 | 3 passed, 0 failed, exit 0 | n/a | 4 | TEMP=/tmp |
| CP-2 | S1 | n/a | hop-read | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1389_HopChildReadsReclaimedLock` | D1 | 2 passed, 0 failed, exit 0 | n/a | 4 | TEMP=/tmp |
| CP-3 | S1 | n/a | old-boot | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1389_OldFormatBootIdRefuses` | advisory | 2 passed, 0 failed, exit 0 | n/a | 4 | TEMP=/tmp |
| CP-4 | S1 | n/a | loser-record | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1389_LoserReclaimRecord` | C1389 | 2 passed, 0 failed, exit 0 | n/a | 4 | TEMP=/tmp |
| CP-5 | S1 | n/a | double-reclaim | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1389_DoubleReclaim` | C1389 | 2 passed, 0 failed, exit 0 | n/a | 6 | TEMP=/tmp |
| CP-6 | S1 | n/a | boot-source | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1389_BootSourcePinned` | C1389 | 3 passed, 0 failed, exit 0 | n/a | 4 | TEMP=/tmp |
| CP-7 | S1 | n/a | dead-owner | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1386_DeadOwnerReclaimed` | C1386 | 6 passed, 0 failed, exit 0 | n/a | 6 | TEMP=/tmp |
| CP-8 | S1 | n/a | live-owner | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1386_LiveOwnerRefused` | C1386 | 4 passed, 0 failed, exit 0 | n/a | 4 | TEMP=/tmp |
| CP-9 | S1 | n/a | ambiguous-owner | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1386_AmbiguousOwnerRefused` | C1386 | 6 passed, 0 failed, exit 0 | n/a | 4 | TEMP=/tmp |
