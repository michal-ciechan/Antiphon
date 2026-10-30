# Review 4ecc5c49: CARD-1619 final review, S1..S4 (Final)

Subject: `origin/feat/card-task-2e5a1c42` tip `42af23dfa6783b4bd1ba313bafbfaa35b41ea089`, chain base pinned rc `e4c9547e1`. Reviewed read-only on server2 (Linux). No subject-branch writes.

**Verdict: two defects (D-1 blocks the land; D-2 is latent until Linux custody is advertised), five advisories.** Mutations and probes ran in scratch detached worktrees at the same SHA and were discarded.

## Checkpoint evidence (this review, tip 42af23dfa)

Manifest: `import --plan` on the branch plan gave 15 rows. Only slotWaitMinutes was edited (45 -> 25).

- Run `20261030-181915-9083`: aborted after 3 minutes. Its `--total-timeout 170` was read as seconds (my error). It still produced CP-8 GREEN (typecheck exit 0).
- Run `20261030-182352-ffe5`: CP-8 GREEN again, `exit=0 dirty=0 source=42af23dfa`. That `npm install` left two hard-linked esbuild binaries, so the source fingerprint refused (`selected file has multiple links`) every later build. I stopped the run, broke the link with a copy, removed the refused empty `bin-card1619-*` dirs, and reran without CP-8.
- Run `20261030-183250-a51c` (12 rows, serial): results in the table below.

| CP | Covers | Result (fresh TRX, tip 42af23dfa) |
|---|---|---|
| CP-1 | V-1..6 Card1619UnixExitTests | 10/10 passed, 0 failed/skipped |
| CP-3 | V-7, V-8 wire + Linux host | 11/11 |
| CP-4 | V-9 Card1619RunnerExitTests | 6/6 |
| CP-5 | V-11 Card1619ExitPersistenceTests | 8/8 |
| CP-13 | V-10, V-12 policy + consumers | 23/23 |
| CP-6 | R-3 AgentTaskLivenessTests | 6/6 |
| CP-7 | R-4 report-first, 3 methods | 3/3 |
| CP-14 | R-5 C502_V1/V3/V9 | 3/3 |
| CP-15 | R-6 A_ready_gate_kill_* | 7/7 |
| CP-9 | V-13 Card1619StopIntentTests | 11/11 |
| CP-10 | V-14 Card1619LinuxStopIntentTests | 8/8 |
| CP-11 | V-15 Card1619StopAttributionTests | 8/8 |
| CP-8 | client typecheck | exit 0 |

- **12 test rows** (run `20261030-183250-a51c`): verdict GREEN exit 0, wall 70m35s, 10 builds. `validate --expected-source-sha 42af23dfa...` returned `CHECKPOINT SOURCE VALID rows=12`, every row `sourceState=clean buildSource=verified`.
- **CP-8** (separate run `20261030-194351-9834`): typecheck exit 0, `dirty=0`. `validate` returned `SOURCE VALID rows=1`.

Total: 104 executed, 104 passed, 0 failed, 0 skipped across the 12 test rows.

Review probes (scratch, not committed):

| Probe | Result |
|---|---|
| P1 disposal hangup, never/throw writer | 2/2 pass |
| P2 host operator kill, never/throw writer | 2/2 pass |
| P3 launch-cleanup with never writer | pass |
| P4 shared gate | **red**: D-2 demonstrated |
| P5 cross-process start identity | **red**: D-1 demonstrated |

CP-12 (cgroup-v1 V-16) and CP-2 (native Windows R-1/R-2) remain **owed**:
- **CP-12** needs an owned cgroup-v1 host with the installed `antiphon-custody-kill` helper and the shim. Run `--rows CP-12` alone at the final source; it needs 2/2 passed with 0 skipped. server2 is cgroup-v2/unprovisioned for it.
- **CP-2** needs an eligible native Windows lane at the same final SHA, covering modern and explicit inbox backends: 7 results (Card1619WindowsExitTests 2 + PtyKillProcessTreeTests 5), 0 skipped.

Neither can be credited from Linux or from nightly.

Other checks:
- `check-evidence-diff.ps1 -BaseRef e4c9547e1 -HeadRef 42af23dfa`: commits=36, entries=5, violations=0.
- Weakening: the only edits to pre-existing tests make private helpers internal, extend a fake, append the new evidence clause to one expected text, and add assertions (`R-3`/`R-5` `ShouldNotContain("exited on its own")`, signal nulls). No test was removed, relaxed or skipped.

## Defects

### D-1: new Linux kill identity check refuses live owned children whose start time was recorded by another process (runaway after runner restart)

- **Where:** `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:2455-2458` (`KillPidBestEffort`, new `!SessionGeneration.Equal(process.StartTime, expectedStart)` -> `return`) and `src/Antiphon.SessionRunner/HerdrPaneChild.cs:1211`. Callers that pass a sidecar value written by an earlier runner process:
  - orphan kill `TryKillOrphanedChild` (:2368);
  - `CloseHerdrAfterBarFailed` (:2972);
  - pending dispose (:4341);
  - adopted-pane P8 stop (`HerdrPaneChild.cs:1150`).
- **Failure:** .NET computes `Process.StartTime` on Linux from a boot time estimated once per process, so the value differs between processes.
  - Five `pwsh` processes reading PID 1 gave values up to 3,187 us apart.
  - Probe P5 (SessionRunner.Tests, scratch): a `/bin/sleep` start time recorded by a separate .NET process was off by 3,564 us. `runtime.StopOwnedProcess(pid, thatStart, ...)` returned without signalling, and the child was still alive 3 s later (`orphanKilled=False`).
  - `SessionGeneration.Equal` compares at microsecond precision, so this mismatch is near-certain.
  - After a runner restart on Linux, a Herdr orphan or adopted child is therefore never killed. The new `IsOsAlive` -> `Unreachable` / `return false` branches (:2266/2278/2298, :2975, :4344, `HerdrPaneChild.cs:1152`) then keep it Pending with its sidecar and seat. Every retry repeats the mismatch, and the orphan path still logs `HerdrOrphanedChildKilled`.
  - Base `e4c9547e1` killed unconditionally, gated only by `IsAlive`'s 2-minute pid-reuse tolerance. The plan said "their existing pid/start validation stays", not a new exact-us check. V-14 `Orphan_and_pending_kills_keep_identity` records and compares the start in the same process, so it cannot go red.
- **Why:** this is a stop regression that leaves processes alive on the very platform the card targets. Herdr is opt-in on Linux, but the path is live.
- **Fix:** compare Linux identity by `/proc/<pid>/stat` field 22 (start ticks), which is process-independent. Persist ticks next to the DateTime, or use the existing `PidReuseTolerance`-style bound instead of exact equality. Add a witness whose expected start is produced by a different process (child `pwsh`/`dotnet`), plus a negative recycled-pid case. Also re-check liveness after SIGKILL with a short bounded wait, not an immediate `IsAlive`: SIGKILL delivery is asynchronous, so an immediate re-check can report a successful kill as an unobserved stop (A-3).

### D-2 (latent: Linux custody only): one process-wide stop-intent gate is held across the cgroup helper, serialising unrelated stops for up to 45 s

- **Where:**
  - `src/Antiphon.SessionRunner/RunnerCustodyLedger.cs:276-286` runs `_recovery.Kill(...)` (sudo helper, `WaitForExit(45 s)` in `IPtyCustodyContainment.cs:202`) inside `StopIntents.Attempt`'s `lock (_gate)` (`ProcessStopIntentStore.cs:58-80`) on the runner-wide store.
  - That store is shared by `SessionRunnerRuntime.StopIntents` with every runner-level stop: Herdr child kills, `KillHostIfStillOurs`, `KillPidBestEffort`, launcher cleanup and adoption `Snapshot`.
  - Host side: `PtyAgentRunner.cs:599-602` runs `containment.Terminate()` inside the host store gate. `HostSession.GetStatus` (:336-351) and the attach replay take that gate while holding `HostSession._gate`.
- **Failure:** probe P4 (PtyHost.Tests, scratch) held one `Attempt` for a simulated 3 s helper. An unrelated `StopProcess` of another owned child was delayed by **3,136 ms** before its SIGKILL. With the real helper the bound is 45 s, against the 5 s watchdog / 5000 ms `KillGraceMs`. Host status and attach also block for the helper's duration.
- **Why:** the brief's ordering rule is that nothing may delay an authorized signal. Today this is unreachable because Linux custody is unadvertised (CARD-1614). It becomes live as soon as CP-12/V-16 qualifies custody.
- **Fix:** publish `Requested` under the gate, release it, run the helper, then publish `Issued`/`Failed` under the gate. A concurrent observer then sees `Requested`, which is conservative. Alternatively key the gate per session/generation. Never call `Snapshot` while holding `HostSession._gate`. Add a two-session witness with a slow helper.

## Brief checks (1)-(8)

1. **Kill sites.** Every session-death site routes through `PtyStopCallback`/`ProcessStopIntentStore.Attempt`, whose `send()` runs first and whose IO is offered after the gate is released:
   - `PtySession.cs:83-97` (SIGKILL, then fallback SIGHUP);
   - `Dispose` (:119-126, hangup, outcome `Requested`, never `Issued`);
   - `SessionRunnerRuntime` `KillPidBestEffort`/`KillHostIfStillOurs`;
   - `HerdrPaneChild.KillPidBestEffort`;
   - `PtyHostLauncher.cs:229-245` (host image plus `--session` argv bound);
   - `RunnerCustodyLedger`;
   - custody release and helper timeout.

   Operator stop, CPU-spin watchdog (`SessionCpuWatchdogService.cs:131`), CARD-0079 compaction stop, pool release (`PoolDelegateRelease` -> runtime kill) and shutdown all reach the host through `RunnerSession.KillAsync` -> KillMessage -> `HostSession.KillAsync` -> `PtyAgentRunner.KillAsync` -> `PortaPtySession.Kill`. The Linux grace starts at entry (`PtyAgentRunner.cs:560`).

   Probes with a never-completing writer and a synchronously throwing writer, all passing:

   | Probe | Never-completing writer | Throwing writer |
   |---|---|---|
   | P1 disposal hangup | exit SIGHUP observed in 104 ms | 2 ms |
   | P2 operator kill | stop completed with SIGKILL in 640 ms, LiveIssuer, durable=false | 845 ms |
   | P3 launch-failure cleanup | `Issued` host-target in 1.1 s | — |

   Authored V-13 (`Blocked_record_write...`, both async and sync blockers) and V-14 cover the host and runner routes. Mutation M1 (record before signal) turned V-13 red.

   Defect D-1 (refused kill) and latent D-2 (gate held across the helper) are the exceptions. `LinuxCgroupCustodyProbe.cs:136` and the server/git/probe kills are correctly out of scope.

2. **Attribution.** `stopped by Antiphon` requires `CanAttribute` (`SessionExitPolicy.cs:51-56`): LiveIssuer + Issued + Child/Custody target + pid/start + matching signal + issuer/host ids. The runner demotes any LiveIssuer intent that does not match its own session, generation, host instance, child pid and child start (`SessionRunnerRuntime.cs:4463-4470`). Disk loaders always demote (`PtyHostManifest.TryLoad`, `ReadRecovered`, `CreateAdoptedExited`). The server request alone gives request-only text.

   Real stop end to end (CP-11 `Matching_live_issuer_intent_is_qualified_Antiphon_stop`, native): runner `KillAsync` -> KillMessage -> `HostSession` -> `PtyAgentRunner` -> `PortaPtySession.Kill` -> `RecordStop`/`Attempt` -> native wait signal 9 -> `ExitedMessage` with LiveIssuer intent -> runner validation -> `ObserveExitAsync`. It produces `stopped by Antiphon (SIGKILL; matching stop request recorded); killed by signal 9 (SIGKILL)`, or `...stop record not durable)...` when the snapshot precedes the record; the test asserts the prefix. The server `KillGenerationAsync` -> runner HTTP hop is unchanged code that stores `TerminationSource` first; the native trace starts at the runner.

   Planted record: a forged LiveIssuer/Issued/durable record in the agent-writable `pty-hosts/stop-intents`, then a self-SIGKILL and adoption by a new runner, gives `killed by signal 9 (SIGKILL); sender unconfirmed (untrusted stop record)` in the DB, attention and task failure (CP-11). Mutation M2 (keep provenance on recovery) turned it red.

   When the observer is not the sender:
   - server request with no live issuer: `Antiphon stop requested; killed by signal 9 (SIGKILL); sender unconfirmed`;
   - runner host-target kill with the child unobserved: `Antiphon host stop issued; child exit unobserved (exit code unknown; signal unknown)`.

   Both are V-10 cases in CP-13.

3. **Exit evidence.** `IsCleanStop` is signal-first (`SessionExitPolicy.cs:15-24`): signal > 0 is Stopped only with a server-stored request source. MemoryKilled is always Failed. CPU-spin stays the plan's policy-stop exception. Legacy null signal keeps the old lifecycle fallback, but its text is `signal unknown`, never `exited on its own`. The dead-session sweep keeps report-first settlement (`AgentTaskDispatcher.cs:3190-3204`), and `ClassifyFailure` composes evidence for both Stopped and Failed rows.
   - Unchanged authority: SessionStopStuck (no timing change, text only), the janitor/release sweep and terminal-seat release (no code-zero authority), checks, and `CheckCompactionContinuationService` (`ExitCode ??= 0` only, enrichable).
   - CARD-1190: the R-6 seven `A_ready_gate_kill_*` tests ran in CP-15. Mutation M5 (compat -1 into launch-cleanup ownership) turned V-12 red at `Card1619ExitConsumerTests.cs:249`.
   - Reflection failure gives signal null, never 0. Mutation M3 turned V-1..6 `Unsupported_contract_keeps_signal_unknown` red 3/3.
   - Vanished processes no longer get a synthetic -1, and unstarted runner adapters default to -1, not 0.

4. **Compatibility.** Frames and manifests use `JsonSerializerDefaults.Web` with no `UnmappedMemberHandling.Disallow`, so unknown members are skipped both ways. V-7 freezes the v1 reader and the old read-modify-write writer: absence is null/unknown, never 0. Runner<->server JSON uses numeric enums on both sides; the server's public API uses string enums, matching `client/src/api/boards.ts`, and all client fields are optional (CP-8 typecheck exit 0). The server rejects undefined enum values (`Bound`), which fails closed.

   The migration `20261030141651_AddSessionExitSignalEvidence` only adds nullable columns: three `int?` plus `varchar(4096)`, no backfill, no index. The snapshot delta matches those four columns. The author's graph `-Check` result was not re-run here (no definition-file change in S4).

5. **Porta.** `PortaExitSignalReader` checks the real loaded assembly name, `1.0.7.0`, the informational version `1.0.7+54684ba...`, `Linux.PtyConnection` deriving from `Unix.PtyConnection`, and a private instance `int exitSignal`. It reads only after `WaitForExit(0)`. Any failure gives null plus one bounded diagnostic. `Porta_exitSignal_private_contract_holds` uses the production reader against the real DLL, so a version bump turns it red. The authored witness covers `1.0.8.0` and `exitSignalX`. Mutation M4 (drop the wrapper fast-exit replay) turned `Fast_exit_is_replayed_once` red at :49.

6. **Mutations.** All five were red on the tip:

   | Mutation | Change | Witness red |
   |---|---|---|
   | M1 | record before signal | V-13 Signal_precedes (:39), Blocked (:60) |
   | M2 | trust disk record | V-13 Planted (:219), V-15 Planted (:121) |
   | M3 | unknown signal -> 0 | V-6 Unsupported 3/3 (:123) |
   | M4 | drop fast-exit replay | V-5 Fast_exit (:49) |
   | M5 | compat -1 into launch-cleanup writer | V-12 Launch_cleanup (:249) |

   They ran in three batched builds (Agents.Pty / PtyHost / Antiphon.Tests). All PCs remain pending for the method-scoped SourceLanding Mutation stage.

7. **Closed rows.** See the results table above.

8. **Trial picks.**
   - `origin/rc/2026-10-27` (`335b4fbb3`): all 36 commits `e4c9547e1..42af23dfa` cherry-pick cleanly in order (96 files, the same +18164/-212 stat). `merge-tree origin/rc HEAD` is clean. rc has no migration change since `e4c9547e1`, so the snapshot applies. The pick carries the plan file (rc has `docs/superpowers/plans/`), plus the five slice reports under `.antiphon/` and two living docs.
   - `origin/master` (`31f990317`): a whole-branch `merge-tree` conflicts (rc/master divergence: attention, release-gates, CARD-1199 plan and others). A sequential 36-commit cherry-pick applies textually, but the RC Designer and snapshot must **not** be carried (plan A-5).
   - CARD-1610 S2+S3 (task `40e35f61`, `origin/feat/card-task-40e35f61` `192f642b2`, not yet on master) adds `20261030114738_AddTaskCeilingSupervision` and `20261030143219_AddTaskSupervisionQueuePurpose`. This card's `20261030141651` sorts **between** them, so the master port must land after CARD-1610 and re-create the migration with `dotnet ef migrations add AddSessionExitSignalEvidence` on that tip. The new ID must sort after `20261030143219` and come with a fresh Designer and snapshot.

Input waits: none changed (no new hold or parking interaction). The new `Unreachable`/Pending branch has no automatic release. Combined with D-1, it retains a live orphan indefinitely; without D-1 it releases when the child is observed gone.

## Advisories

- **A-1:** `PortaPtySession` (`PtySession.cs:52-55, 86-96`): if the constructor's `StartTime` read fails, `_ownedStartedAtUtc` is null and Linux `Kill()` sends nothing, forever. Base always tree-killed. It is unlikely right after spawn; fail toward the old kill, or retry the identity read.
- **A-2:** `ProcessStopIntentStore.cs:72` evicts with `_attempts.Keys.First()`. Dictionary order is not insertion order after removals, so a fresh live attempt can be evicted. That only degrades attribution, but use an ordered queue.
- **A-3:** an immediate `IsAlive` after SIGKILL (`HerdrPaneChild.cs:1152`, `SessionRunnerRuntime.cs:2975, 4344`, Unreachable verdicts) races kernel teardown and can report a successful kill as unobserved. Use a bounded short wait; fix it with D-1.
- **A-4:** `PtyAgentRunner.cs:268-272` now replays a pre-subscription exit on every backend, including Windows modern and inbox without custody. It is strictly a fix, but it is a Windows behaviour change for CP-2 to witness.
- **A-5:** the orphan-path log line `HerdrOrphanedChildKilled` is emitted even when the kill was refused or failed. Log the actual outcome.
