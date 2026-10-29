# Review cd8476f1: CARD-0849 fixture gate, final review of chain 1af06cf5 -> 554a22c6 -> 49fc10b8

Reviewed `feat/card-task-49fc10b8` at `89923afeb91df00990b21caabd4d0efd5306c08e` (worktree clean). Card base: rc `8004259c70a068507decec1b1b9896d58c5b4047`. Chain: 10 commits `cf06c598f..89923afeb`. Five files change: `scripts/c590-remote.sh`, `scripts/fixtures/c1192-workspace-contract.sh`, `docs/docker-stack.md`, `RemoteScriptContractTests.cs`, `DockerStackDocumentationTests.cs`.
Scope: **Interim**, as the brief asked. The Unit lane and the whole RemoteScriptContractTests class were not run.

**Verdict: 2 defects, both in the tests. The production fixture is sound.** I checked every kill, rm and signal in the new code. None of them can reach a target that is not this run's proven probe. I found no fail-open path. No new unbounded wait was added. Every round-2 defect (69fea092 D-1, D-2, D-3) is fixed. The two defects are a witness that passes when the thing it guards is broken, and a hang bound claimed in round 4 that no test covers. Both are test-only fixes.

## Defects

### D-1: the process-group INT witness passes even when the INT handler does nothing (vacuous/weakened witness)
- **Where:** `scripts/fixtures/c1192-workspace-contract.sh:4328-4507` (`c1192_signal_witness … group`), asserted at `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` (`C849_Refused_launch_keeps_a_foreign_container_and_signals_release_proven_cids`, the `signal-INT-group-*` facts). Owner: Code 49fc10b8, commit `89923afeb`.
- **Failure:** I ran scratch mutations of `c590-remote.sh` against the unmodified harness.
  - m6: `c849_fixture_on_signal` returns at once for INT, so it does no release and no exit 130. The group case stays green: `exit=2 accepted=1 containers=1 procs=1 lease=1 volumes=1 foreign=1`, in 3 s. The pid case goes red (`signal-INT-exit=2`).
  - m1: the release's `docker kill`/`docker rm -f` by cid is removed. The group case stays green, because the EXIT-trap finalize removes the containers by recorded id. The pid case goes red (`cid-kills=0`).
  - m5: the release kills by **name** instead of cid, which is the wrong-target class. The group case stays green. The pid case goes red (`cid-kills=0`).
  - In the fake-Docker model, a group SIGINT stops the probes themselves. The barrier then exits 2 through the ordinary probe-death path. So exit 2 plus "everything is gone" is reachable without the trap ever acting.
  - `signal-INT-group-foreign` cannot go red from anything the fixture targets. The sleeper is in another process group and is on no ledger, so only a host-wide kill could reach it.
- **Why:** the brief requires invariant (b) to be impossible to satisfy vacuously. Right now it shows only that some path cleaned up. It does not show that an operator Ctrl-C makes the fixture release. With real Docker, the probe container's PID 1 (`/bin/sh -c`) has no SIGINT handler. I therefore expect real probes not to stop on their own (inferred, not run against real Docker). If the trap regressed, a Ctrl-C would hold both slots until the 45-minute deadline, which is the incident class this card exists for. Compared with round 2, the group case drops exact exit 130, the `kill <cid>` order lines and the no-`kill <name>` check. The pid case still has all three, so for now the pid case alone carries that protection.
- **Fix:** do both of the following.
  - Make the fake `docker run` in group mode behave like sig-proxy to a PID 1 with no handler: the container survives SIGINT. Then require `kill <cid>` for each live-before cid and no `kill <name>`.
  - Either require exit 130, or accept exit 2 only when the trap's release provably ran: an `on_signal` marker or order lines written before the barrier's own refusal, plus elapsed time well under `C589_SLOT_WAIT_SECONDS`.
  - Plant the foreign pid as a `pid pending name` ledger line, as `c1192_reap_witness` does, so the unrelated-pid check can actually go red.
  - Add m6, m1 and m5 as red witnesses.

### D-2: the round-4 claim "driver exits 4 at the shared slot deadline" has no witness
- **Where:** `scripts/c590-remote.sh:4219-4240` (`driver.sh` heredoc in `c849_fixture_race_materialize`) and `:4141-4156` (`go_deadline` passed as `$3`). Owner: Code 49fc10b8, commit `d820bce7c`.
- **Failure:** m7 deletes the three-line `if [ "$(date +%s)" -ge "$deadline" ]; then exit 4; fi` from `driver.sh`. `slot-delay`, `slot-refusal` and `slot-late-cid` all stay green. `c1192_driver_order` checks only the line order of slot-held, go, ready and restore. The fake Docker `simulate_restore` never executes `driver.sh`; it only parses the epoch argument. No C# test reads the deadline text.
- **Why:** this is the only bound on a probe that holds a slot after a missed `go`, for example an orphaned in-flight probe. Without it, that slot is held until someone removes the container by hand. The bundle rejects a claimed bound with no test. I checked it by hand only: `sh driver.sh <dir> $((now+2))` exits 4 after about 2 s, and a non-numeric deadline falls back to 2700 s.
- **Fix:** add a harness row that runs the materialized `driver.sh` under `sh`, with a past or near epoch and no `go`, and asserts exit 4 within the bound. Add a second leg with `go` present that asserts `ready` is written. The row must go red under m7.

## Checks the brief asked for

**(1) Wrong-target kill.** I went through every signal, kill and rm in the new code:
- `c849_fixture_release_live_probes` runs `docker kill`/`rm -f` only when the cid matches `^[0-9a-f]{64}$` **and** `c849_fixture_probe_proven` passes. That requires exact labels owner, fixture-run=`$RUN` and role, plus `.Name == "/$name"`, with the name taken from a fixed seven-name roster. A container named with 64 hex characters fails the name check.
- A cid that is not proven is never a kill target. An absent one is "gone". A present one goes to residue and the ledger.
- `c849_fixture_reap_probe` sends TERM and `-9` only while `jobs -pr` lists the pid. A job that is still listed cannot have had its pid reused. m2, which drops the guard, turns the reap witness red (`unowned-escalated=1 survived=0`).
- Pid matching is `"$pid "*`, quoted and space-terminated, so 123 does not match 1234.
- `drop`, `clear` and `publish` `rm`/`mv` only fixed `$CASE_DIR/.fixture-live*` paths or a `mktemp` in `CASE_DIR`.
- Lease DELETE takes only 36-character GUIDs parsed from this run's probe logs. A stale GUID is a no-op.
- There is no `eval`, and no unquoted kill list in production. `kill $pids` appears only in the harness.
- A launch subshell orphaned by a reap can publish after the clear. Its pid is no longer a job, so it gets no signal. Its cid is still proven before any kill.

**(2) Fail-open.**
- Every refusal path ends in `write_result false … 2`.
- `on_signal` runs `trap '' INT TERM`, releases, then exits 130 or 143. The EXIT trap then writes `UnhandledExit <ec>`.
- The 60 s await ends in release plus residue (`unproven in-flight probe`), never in "proceed".
- New statements that run under `set -e` outside a conditional (`track_probe`, `drop_live_probe` after `wait`) can only end the fixture non-zero through the EXIT trap.
- The barrier and lock phase now keep `wait` status explicitly (`|| probe_status=$?`).
- SIGHUP also runs the EXIT trap, so finalize releases. I checked that bash runs the EXIT trap on HUP and exits 129.

**(3) Hang.**
- The launch spin cap was removed, so the poll now ends when docker exits, the same bound as the old `wait`.
- `await` is wall-clock 60 s per in-flight probe.
- `flock -w 60`.
- The slot wait is bounded by `date +%s`. The in-container `build-slot.ps1` gets the same `C589_SLOT_WAIT_SECONDS`, and the driver deadline bounds a held slot (D-2: not tested).
- Exit-4 path: `slot-refusal` shows the lease DELETEd, probes removed, no live wait and volumes removed. Both proven cids are killed and their leases are DELETEd from the logs.

**(4) Witness (b).** Arming requires all of these before the INT:
- at least two restore lines in `.fixture-live-probes`, each with a 64-hex cid that `probe_proven` accepted at publish;
- `BUILD SLOT granted lease=` in `race-p1.log`;
- `nrestore ≥ 2` checked again.

So a fixture that never started a probe fails. The invariants themselves are vacuous with respect to INT handling: see D-1.

**(5) Weakening.**
- Round 2 asserted one pid-only INT -> 130 plus `kill <cid>` and no `kill <name>`.
- Round 4's pid case keeps 130 and the cid checks, but repeats INT up to 400 × 20 ms, because bash drops a pid-only SIGINT that arrives while the shell waits on a foreground `sleep`. Nothing now asserts that a single INT is acted on (A-1).
- The group case drops exit 130, cid kills and name kills (D-1).
- `copied_live_cids` is stricter than round 2's `≥2 lines`.
- The reap witness is stricter: any signal to the foreign pid is now red, not only `-9`.
- `has_exact_line` replaced the SIGPIPE-prone `printf | grep -q` (69fea092 D-3a).

**(6) Merge.**
- `git merge-tree --write-tree origin/rc/2026-10-27 (cf1a12403) 89923afeb` is clean: tree `cb486250d6bf`.
- A whole-branch merge onto master `e30ba1237` conflicts on 17 files, because the branch rides rc. That is expected.
- The cherry-pick simulation `git merge-tree --write-tree --merge-base=8004259c7 origin/master 89923afeb` is clean: tree `33a17c18d62d`. Only the five files above change.
- For each of the five files, `patch-id` of the target -> merged diff equals the branch's `8004259c7..89923afeb` diff. So a port to either side keeps the target's own lines untouched: rc's `29218e553`/`6a58c2262` audit-proof edits, master's rc top-ups 33+34 and the CARD-1607 audit proof in `c590-remote.sh`, and rc's `eb176a309`/`01c32a59a` harness/RSC timing.
- What a port must keep from this chain: all ten commits' hunks in those five files. `test-phase-evidence.ps1` and the add/add files are not touched by the chain, so drop them from any pick.
- I ran the fake-Docker harness on both merged trees for `slot-round2`, `slot-late-cid`, `slot-refusal`, `slot-delay` and `slot-timeout`. All are green.

**(7) Doc pin.** Accurate for the barrier, the proven-cid release, absent-is-gone, refused-name residue, `jobs -pr`, lease DELETE and the driver deadline. See A-1 and A-2 for two statements that are too strong.

## Advisories (no Review rerun needed for these alone)
- **A-1 (doc):** "INT exits 130" is too strong. A pid-only SIGINT is dropped while the shell waits on a foreground child that does not die of it (bash wait-and-cooperative-exit; the harness comment documents it). A group INT may exit 2 (FixtureRestoreBarrierFailed). Neither is fail-open: the barrier still releases at its deadline. `c590-real.ps1` runs the script over `ssh` with no `-t`, so no automation sends INT today.
- **A-2 (doc/bound):**
  - "waits up to 60 seconds" is per in-flight probe, run one after another: up to 120 s in the barrier. INT and TERM are ignored during that time.
  - A TERM during a foreground launch is deferred until that docker exits: offline p3, helper runs, `race_materialize`. Bash runs the trap only after the foreground `( … )` subshell returns. I checked this with a 3-second probe. For p3 that can be the 45-minute slot wait. This predates the chain.
- **A-3:** an in-flight probe whose cid is not published within 60 s is recorded as residue. Its outer subshell is killed, but the orphaned launch subshell and its container go on until the driver deadline (≤45 min, see D-2). That is fail-closed, but it can hold a slot.
- **A-4:** in-flight detection and reaping depend on `jobs -pr` inside a process substitution listing the parent's jobs. That is witnessed on this runner's bash 5.2.15. The server2 host's bash version is not verified here. If it differed, the only effect would be no signal and no in-flight residue line. The barrier still fails 2.
- **A-5 (carried, predates the chain):** a dotnet restore that hangs after `go` (`wait "$a"`) has no bound. A signal that arrives during the EXIT handler on the normal path can still cut finalization short (69fea092 A-2).
- **A-6:** my m3 mutation run left one harness private directory, `/tmp/c1192-parent.*`. The harness refused its own cleanup with `delete-failed`. It is outside my scratch, so I did not delete it. Seven such directories exist, and the other six are not mine to judge.

## Test runs (all at 89923afeb, through scripts/build-slot.ps1)
- Build: `dotnet build tests/Antiphon.Tests -p:UseAppHost=false --property:OutputPath=bin-rvcd84/`. Lease 3bef5784, waited 0 s, held 275 s, 0 errors.
- RemoteScriptContractTests pins `(C849*)|(C1192*)|(C1189*)|(C1191*)|(C1193*)|(C976*)|(C973*)|(C1208*)`: lease 2c306f79, waited 0 s, held 598 s. **138/138 passed**, 0 failed, 0 skipped, 9 m 56 s.
- RollingVolumeRecycleScriptTests (own slot): lease 99d72568, waited 15 s, held 1787 s. **75/75 passed**, 0 failed, 0 skipped, 29 m 45 s.
- Classes DockerStackDocumentationTests, RollingProductionMountTests, RetiredTempContainerHostTests, RetiredTempContainerScriptTests, RcRosterRepairTests, WindowsUnitPlatformAdmissionTests (C1210_Roster_closure) and C1514LandingSplitBudgetTests (census C1404/C1577/C1584/C1410/G2865): lease 613769a9, waited 1215 s, held 950 s. **79 total: 78 passed, 1 skipped, 0 failed**. The per-class TRX counts are 16, 12, 22, 16, 3, 5 and 5. The skip is `C1210_Windows_linux_only_outcomes_are_exact`, a Windows-only case that is NotExecuted on Linux. `C1210_Roster_closure_matches_source_gates` passed. RcRosterRepairTests 3/3. All five census tests passed.
- Witnesses on the fake-Docker harness (scratch copy that only adds mode aliases), run first at load about 38: **process-group INT 20/20** green (all exit 130), **pid INT 20/20** green (all 130, cid-kills=1).
- Mutation probes (scratch copies of c590-remote.sh, unmodified harness logic):

| Mutation | Result |
|---|---|
| m1, no cid kill | pid red, group green |
| m2, no `jobs -pr` guard | reap red |
| m3, await 0 s | late-cid red |
| m4, INT exits 0 | both red |
| m5, kill by name | pid red, group green |
| m6, INT handler a no-op | pid red, group green |
| m7, no driver deadline | all green (D-2) |

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 8004259c7 -HeadRef 89923afeb`: commits=10, entries=0, violations=0.
- Not run: the Unit lane and the full RemoteScriptContractTests class. Live Docker and real slots were not used, and nothing touched production.
- Cleanup: all 33 `bin-rvcd84` directories were removed by literal absolute path with `rm -r` (no `-f`), and `find` counts 0. The mutation and merged-tree copies are in the session scratchpad only.
