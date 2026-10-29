# Review b96e228b: CARD-0849 fixture gate (subject 1af06cf5)

Reviewed: feat/card-task-1af06cf5 at f9f3e18153da20560ad59ec02512a0dce3008234. Base: rc/2026-10-27 8004259c70a068507decec1b1b9896d58c5b4047.
Verdict: 2 defects. Not landable as is.

## Defects

### D-1: the refusal/signal release kills and removes a same-named container that the launcher refused to touch (ownership bypass)
- Where: `scripts/c590-remote.sh:4301-4302` (`c849_fixture_release_live_probes`), with the tracking at `c849_fixture_race_restore_barrier` and `c849_fixture_race_lock_probe` (`c849_fixture_track_probe` runs right after `&`, before the launch has proven anything).
- Failure: if a container named `c849-<RUN>-restore-p1` already exists, `c849_fixture_launch` refuses (`c849_fixture_helper_lookup` returns 1, so launch returns 2) and leaves it alone. But the parent has already written the name to `.fixture-live-probes`. `c849_fixture_wait_slot_markers` sees the probe exit 2 and returns 1. `c849_fixture_release_live_probes` then runs `docker kill -- c849-<RUN>-restore-p1` and `docker rm -f -- c849-<RUN>-restore-p1`, which destroys a container whose CID, labels and fingerprint were never proven to be this run's. Repro on the fake-docker harness: a scratch copy of `scripts/fixtures/c1192-workspace-contract.sh` with a new mode that runs `plant_container "c849-${RUN}-restore-p1" foreign other-run consumer ...` and then calls `c1192_slot_barrier`. Output: `slot-diagnosis=FixtureRestoreBarrierFailed`, `foreign-survived=0`, order log `kill c849-c1192fixture0001-restore-p1`.
- Why: the fixture's deletion contract (C1208_Wrong_cidfile_preserves_foreign_container, C1208_Recorded_id_with_changed_labels_is_untouched, C1208_Wrong_helper_id_preserves_foreign_container) is to delete only containers proven by CID, labels and fingerprint. Anything unproven goes to residue and finalization exits 2. This fixture runs real Docker on the production host ahead of the rollout. The allowlist (`c849_fixture_probe_name_ok`) keeps the blast radius to `c849-<RUN>-*` names, so the standing runner and task containers are safe, but the ownership proof is skipped.
- Fix: kill only a container this run proved it launched. Two ways: have the probe subshell publish its `--cidfile` path or ID, then before any kill/rm check that ID's labels (`io.antiphon.owner=c849-fixture`, `io.antiphon.fixture-run=$RUN`, role) and that the ID matches the name. Or record the name only after the launch passes `helper_lookup`. Otherwise write the name to residue/ledger and do not delete it. Add a witness: a pre-existing foreign `c849-<RUN>-restore-p1` survives a barrier refusal.

### D-2: the INT/TERM trap is hidden in `eval` to get past the single-trap pins, and its witness is a text grep
- Where: `scripts/c590-remote.sh:5485` (`eval "trap 'c849_fixture_on_signal' INT TERM"`, added in f9f3e1815 "keep one fixture exit trap") and `scripts/fixtures/c1192-workspace-contract.sh:4049` (`signal-trap` = `declare -f c849_fixture | grep -q c849_fixture_on_signal`).
- Failure: C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit and C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt require `.Single(command.StartsWith("trap "))`. A plain second trap line made them red. The `eval` wrapper makes those pins pass without updating them. Also, `signal-trap=1` holds for any text that mentions the function: a broken handler, or one installed for the wrong signals, stays green. The claimed "INT/TERM kills named probes and deletes leases" therefore has no outcome assertion.
- Why: test evasion of an existing structural pin. The adequacy rule rejects a witness with no outcome assertion. (I checked the behaviour separately: a scratch harness mode that installs the same trap, sends TERM during the slot wait and checks the result gave exit 143, both probes killed, lease DELETEd. So the code works, but nothing committed proves it.)
- Fix: use a plain `trap 'c849_fixture_on_signal' INT TERM` line. Update C1189/C1191 to select the EXIT trap (`StartsWith("trap ") && EndsWith(" EXIT")`) and pin the INT/TERM trap separately. Replace `signal-trap` with a behavioural witness: run the barrier in a background shell with the trap, send TERM during the slot wait, and assert exit 143, the named probes gone and the lease DELETE recorded.

## Advisories (no Review rerun needed for these alone)
- A-1: `c849_fixture_wait_slot_markers` (`:4311-4337`) counts iterations (`deadline*5` × (0.2 s + one or two `docker exec` reader calls)), not wall-clock time. The probe's own exit 4 normally bounds the wait. But if the reader helper fails on every poll, the fixture spins for 2–5× the 45 minutes while holding up to 2 slots, and then reports `FixtureBuildSlotTimeout`. Use `$SECONDS` for the deadline, and give a persistent reader failure its own diagnosis.
- A-2: the race holds the first granted slot idle until the second is granted, for up to 45 minutes. This is bounded and not a deadlock (B waits on the broker, not on A), but on a 4/4-busy host it ties up one slot. Consider a shorter fixture-specific slot wait, or release-and-retry. Operator choice.
- A-3: the p3 offline restore (`:4432-4433`) also goes through build-slot.ps1. Its exit 4 is reported as `FixtureOfflineRestoreFailed`, not `FixtureBuildSlotTimeout`, and the claim says "probe exit 4 = FixtureBuildSlotTimeout". p3 also runs in the foreground, so TERM is deferred until it finishes.
- A-4: `c849_fixture_reap_probe` (`:4257`) sends `kill -9` to a tracked pid if `/proc/<pid>/stat` exists and is not Z. Bash reaps background children asynchronously, so a probe pid that already exited (for example, holder already waited before a lock-probe refusal) can be reused by an unrelated mc-owned process before the release runs. Only kill pids still listed by `jobs -pr`.
- A-5: SIGHUP (an ssh drop) is not trapped. Bash then runs the EXIT trap with `$?=0` and the fixture writes no receipt. This predates the change; add HUP to the signal trap.
- A-6: `volumes-removed=1` already holds at red commit cf06c598f (finalization removed volumes before this change), so that fact is not a witness.
- A-7: a lease granted but not yet logged when the probe is killed is not DELETEd; the broker TTL reclaims it. The driver's go-wait is still unbounded, so a SIGKILLed fixture leaves probes holding slots. Both predate the change.

## Checked and OK
- Container names are exact (`c849-${RUN}-restore-p1|p2|p3`, `lock-{holder,waiter}-{shared,private}`) and checked against an allowlist. No `docker ps --filter name=`, no globs, no prefix matching. A name containing `-` and non-hex characters cannot match a container ID prefix.
- Volume removal is the existing ledger/identity finalization path; nothing new removes volumes.
- Lease DELETE takes only `BUILD SLOT granted lease=<GUID>` lines from this run's own `$SERVER2_ROOT/race-*.log` and lock logs (the controller directory is fresh per run; `FixtureControllerOccupied` refuses a reuse). Only this fixture's own leases can be deleted.
- The race is still real: `slot-held` comes after the build-slot grant (the driver is the gated command), `ready` comes immediately after `go`, and both restores start within about 0.1 s of each other. The `go` protocol is unchanged.
- Red witnesses at cf06c598f (scratch `git archive` of the red commit's scripts, fake docker):
  - slot-delay: `delayed-race=0`, `driver-order=0`, `FixtureRestoreBarrierFailed`
  - slot-timeout: `FixtureRestoreBarrierFailed`
  - slot-refusal: `waited-live=1`, `probes-removed=0`, `lease-released=0`, `signal-trap=0`

  The same harness at the tip gives `slot-diagnosis=FixtureBuildSlotTimeout` for slot-timeout.
- Diagnosis tokens: no ps1/sh parser outside c590-remote.sh, tests and docs. `FixtureBuildSlotTimeout` uses exit 2 like its siblings, and the docs pin both tokens.
- Merges: `git merge-tree --write-tree origin/rc/2026-10-27(91b45006b) HEAD` is clean (tree ad1b766b9c405809be21d2e0330d1dc43f4f03dc). Cherry-picking the range onto origin/master 6f97e999e (`--merge-base=8004259c7`) is clean (tree f2f75047ee4b54e265efcbe180e4af73735e35e3). A whole-branch merge into master conflicts only on unrelated rc/master divergence (.antiphon/task-af2b78c8.md, a CARD-1584 investigation doc).
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 8004259c7 -HeadRef f9f3e1815`: commits=3, entries=0, violations=0.

## Test runs
- Build: `scripts/build-slot.ps1 -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --property:OutputPath=bin-rvb96/` finished with 0 errors in 252 s (lease caa555b9 released).
- Batch 1 (RemoteScriptContractTests, DockerStackDocumentationTests, RollingProductionMountTests, RetiredTempContainerHostTests, RetiredTempContainerScriptTests, WindowsUnitPlatformAdmissionTests, RcRosterRepairTests, C1514LandingSplitBudgetTests for C1404/C1577/C1584/C1410/G2865) ran through build-slot as one `dotnet exec --treenode-filter` call. It waited 91 s for a slot, then ran 62 minutes on a 4/4-busy host with other RemoteScriptContract and RollingVolumeRecycle runs alongside. I stopped it past the 70-minute budget with TERM to my own dotnet pid. No TRX was written and no counts are available. Lease 70eb773a was released and no orphaned processes remain. **NOT RUN as evidence.**
- RollingVolumeRecycleScriptTests (about 31 min): NOT RUN, over budget.
- Unit lane: NOT RUN (the brief said so up front).
- All 33 `bin-rvb96` directories were removed by literal path.
