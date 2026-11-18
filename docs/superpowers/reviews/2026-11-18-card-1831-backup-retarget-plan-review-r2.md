# Review: CARD-1831 backup plan, round 2

Round 2, Final, diff only, blockers only. Subject Docs task `36161193-ac18-4627-9f83-0fb27486c449` on `feat/card-task-36161193`, tip `d04fa130454503cc12650d4cce3cb65c0fe2e3b4`. Range `aff936777756af49d305ba76718458aca881eced..d04fa130454503cc12650d4cce3cb65c0fe2e3b4` (one commit). The only file is `docs/superpowers/plans/2026-11-18-postgres-backup-retarget-plan.md`. No product code or tests changed, so the named guard classes were not re-run. It answers round 1 `dc055ca9c85c6435e22cf34c1ccfba5ca938fbb5`.

`origin/master` is `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`. Citations below are that tree. Mutation stays waived. No PC-n is named. Every PC stays pending.

## Verdict

Clean. W-5, V-15 and CP-15 close the round 1 blocker. The folded advisories match the round 1 notes and the master tree. No new blocker.

## Blocker closed

Where it was: W-5 wrote `snapshot-target` back to `desktop` only when W-3 or W-4 failed. A phase 6 rollback after a successful proof left the daily job aimed at server2.

W-5 now lists three reasons phase 6 runs, including an operator rollback after W-3 and W-4 succeeded. Once phase 6 has restarted `antiphon-postgres` and AppHost against the desktop volume, the operator runs `Set-SnapshotTarget desktop` and checks for `SNAPSHOT TARGET desktop`. The same write runs if the window is abandoned after the flip and before phase 4, while production is still the desktop volume. The write is idempotent when the file is already `desktop`.

That matches `Invoke-CutoverRollback`: stop gate 6 (row comparison, including a failed read) runs after `docker compose start postgres` and before `Set-CutoverConnectionSecret -Remove`. AppHost is still on the server2 secret, so leaving the word there does not aim the job at a volume the writer has left. A completed phase 6 removes that secret and restarts AppHost, and W-5 then requires the desktop write.

V-15 `TargetRollbackRestoresDesktop` is the witness: `server2` then `desktop` leaves exact ASCII `desktop` and prints `SNAPSHOT TARGET desktop`; a second `desktop` write is the same; `Desktop`, `server3` and `''` return `''` and leave the bytes; the wrapper's recorded child then carries `-Target desktop`. CP-15 runs that case. CP-7's full harness also covers V-15. S2 is the only flip and rollback path, through the dot-sourced helper. S4's README sentence is the same rule, including after a successful W-3 and W-4.

## Advisories folded

- V-4 now refuses `DOCKER_HOST=ssh://x` and `DOCKER_HOST=unix:///var/run/docker.sock` with `reason=docker-host` and no `exec`. D-3 names both `unix://` and `npipe://` for `-Target server2`, and keeps today's local acceptance for `-Target desktop`. Master `postgres-snapshot.ps1:743-744` is that local check.
- V-14 / CP-17: `-Target server2` plus an explicit `-Container` is `ambiguous-mode` with no `step dump:`, including `-Container antiphon-server2-postgres-postgres-1`. `-Target desktop -Container antiphon-postgres` stays `reason=ok`.
- R-3 names `PosixHarnessToolTests.SnapshotCases` (`tests/Antiphon.Tests/Scripts/PosixHarnessToolTests.cs:64-68`): Windows `StagingRefusesLink` and `WindowsAclRules`; Linux `StagingRefusesLink`, `StagingRefusesForeignRoot` and `DrillRefusesIdPrefix`. `DrillRefusesIdPrefix` is CP-16. The C# list is unchanged.
- D-5 adds `Running`, `ComposeProject`, `VolumeMounts` as `<name>:<destination>`, and `ImageId` on the same inspect read. Master `Get-SnapshotContainerFact` (`:856-910`) stores the drill label and volume names without a destination, and has no running state or image id.
- D-6 stages the newest `antiphon-<utc>.dump` from `C:\Users\lndco\antiphon-backups`. Master `:79` is `$script:SnapshotPlainDumpPattern` and `:80` is `$script:SnapshotPlainDestination` with that path.
- The three cited lines match master: the child argument list is `invoke-postgres-snapshot.ps1:67-69`; the age `-v <stage>:/work:ro` mount is `postgres-snapshot.ps1:1275-1279`; the OneDrive default is `:1812`.
- Single owner: this plan owns the two snapshot scripts. `830d7e92b8609f54bb101ac8475223390169f939` is the red witnesses in both harnesses. `e1031dbc71c2c723a9b4c85415d0b4758430cb46` edits `postgres-snapshot.ps1`, the invoke wrapper, `README.md` and `docker-stack.md`. It uses process `DOCKER_HOST` default `ssh://mc@server2`, treats a missing `SNAPSHOT-TARGET` as desktop, refuses `-RestoreDrill` off desktop, and fails with `bad-target`. It has no D-5 source check. Those two commits must not land. Re-cut at `579980a07468a4858ee34cbe938a820381f29636` still drops them.

## Checks

- `scripts/check-evidence-diff.ps1` over the round-2 range: commits=1, entries=0, violations=0. Over `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4..d04fa130454503cc12650d4cce3cb65c0fe2e3b4`: commits=3, entries=0, violations=0.
- `git merge-tree --write-tree origin/master d04fa130454503cc12650d4cce3cb65c0fe2e3b4` exit 0, tree `d3cb6a0363ecde87ccd3d9c06b658df201676784`.
- `scripts/impacted-classes.ps1 -Range` on the amendment selected no kind and no filter. Downgrade selector count 0. No impacted row.
- The checkpoint table is 17 data rows, header `CP, After, Build, Group, Filter, Covers, Expect, Min, EstimatedMinutes`. New rows are command rows (`Build` `n/a`, `Min` `n/a`) with `EstimatedMinutes` 1. The 17 estimates sum to 18. `test-job-status.ps1` already takes `-Case` and exits 2 on an unknown name, so CP-15 is a real row. The rows were not executed: this subject ships no code.
- No session waits for input. Parking does not apply. No producer/recipient path changed.

## Note for the orchestrator

Does not hold the land. At review time `origin/feat/card-task-801355c0` is `dcf142e69c57e72a1e07c9a9c16736d561ea39db`, one commit past the plan's observed `e1031dbc71`. That commit only teaches `test-postgres-snapshot.ps1 -Case` a comma list. Re-cut at `579980a074` still excludes it. W-5's last bullet (next 07:50 prints `src=desktop`) is not separately scoped away from the stop-gate-6 "leave the word" bullet. The completed-rollback paragraph already requires the desktop write and the `src=desktop` proof.

`reviewedSourceClean` is false. This is plan text. There is no checkpoint-tool receipt and no build provenance.
