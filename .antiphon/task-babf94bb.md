# CARD-1791 Code (task babf94bb) — Hangfire lease hardening: done

Branch `feat/card-task-babf94bb`, worktree `/work/worktrees/task-babf94bb` (server2 runner mirror), base `d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa`. Original Code task / landing owner: `babf94bb`. No plan file (card-driven brief). No existing implementation found (`git log --all --grep`, `card.ps1 search backend_start -All`: only CARD-1791).

## Commits

| Role | SHA |
|---|---|
| red (witnesses + base-behaviour seams) | `26c4805cf0e387440203410c04741e462cfa7784` |
| red helper fix (relay count) | `44cdff34c7f4356dc3b94f64b860ff74ed7b857c` |
| fix | `a4e49996bb283b22553d7f5f2f82a502745e2cbf` |
| fix (nullable warning, CS8629) — tested SHA | `85a3c972f26200fd103c724fd87f77a91004e271` |

Files: `server/Infrastructure/Agents/HangfireConfiguration.cs`, `tests/Antiphon.Tests/Infrastructure/HangfireDurableStorageTests.cs`, `docs/docker-stack.md`. No migration, `tests/test-execution-policy.json` unchanged (no policyHash/PinnedInputDigest refresh), `server/Program.cs` untouched.

## What changed (per advisory)

- A-2 + A-8: the lease remembers its backend as `BackendIdentity(pid, backend_start)` (`pg_stat_activity`). `ReadGrantedHolder` returns the holder's (pid, backend_start). A replacement session reads its own identity first and records it as `_pending` before any lock take, so a link that breaks right after the take leaves a holder the next tick recognises as its own and reclaims. `IsOwn` = current backend or pending. Reclaim terminates only `pid AND backend_start IS NOT DISTINCT FROM` the remembered start. `OwnOpenBackendStillHolds` treats a pending holder as ours (falls through to `ReplaceConnection`). Another role's session has a null start, so it never matches.
- A-3: committed witness for `ReclaimOwnBackend` via a `DetachConnection` seam.
- A-4: doc now states at most 45 s (15 s + host 30 s); the Hangfire drain runs inside the host budget.
- A-5: `WithinCrashExpiryWindow` uses `TimeProvider.GetTimestamp`/`GetElapsedTime`.
- A-7: the sibling-database witness's last hand-over queues a blocking `pg_advisory_lock` waiter before the terminate; the try-lock of the 50 ms tick cannot jump the queue.

Seams (test-only, internal): `RememberBackend`, `DetachConnection`, `FaultAfterNextFreshLock`.

## Witnesses

| ID | Test | Base (26c4805c/44cdff34) | 85a3c972 |
|---|---|---|---|
| A-2 | `HangfirePostgreSqlStorageTests.Worker_lease_treats_a_reused_pid_with_another_start_as_another_worker` | RED: `first.EnsureHeld` should throw but did not (base terminated the reused-pid holder) | Passed |
| A-8 | `...Worker_lease_reclaims_a_new_backend_it_lost_right_after_taking_the_lock` (TCP `SeverableLink` relay drops the client side, keeps the backend) | RED at 44cdff34: second `EnsureHeld` threw "another worker" (first red at 26c4805c was a helper count bug, fixed in 44cdff34) | Passed |
| A-5 | `HangfireStorageSelectionTests.Crash_expiry_window_follows_the_monotonic_clock_not_the_wall_clock` | RED: window false after +1 h wall step | Passed |
| A-4 (+A-2/A-8/A-5 prose) | `HangfireLeaseDocumentationTests.Durable_hangfire_documents_the_lease_the_package_and_renames` | RED: missing 45 s sentence | Passed |
| A-3 | `...Worker_lease_reclaims_its_own_backend_when_its_connection_is_gone` | Passed (covers existing behaviour; a coverage gap, not a defect) | Passed; RED under mutant `IsOwn => holder == _pending` (threw "another worker"), A-8 green under the same mutant |
| A-7 | `...Worker_lease_ignores_another_database_holder_and_stops_for_its_own` (deterministic hand-over) | Passed | Passed |

## Runs (all through build-slot, native output, `-p:UseAppHost=false`, `TEMP=/tmp`)

- Red build 26c4805c (slot waited 0 s, held 156 s); red run, filter `/*/Antiphon.Tests.Infrastructure/(HangfireStorageSelectionTests*)|(HangfirePostgreSqlStorageTests*)|(HangfireLeaseDocumentationTests*)/*`: total 36, passed 32, failed 4 (the four above).
- Red2 build 44cdff34 (held 304 s); single A-8 method: 1 failed at the intended assertion.
- Fix build a4e49996 (CS8629 warning), then 85a3c972 build (waited 0 s, held 230 s, no warning in the touched file).
- Touched classes at 85a3c972, same filter: **total 36, passed 36, failed 0, skipped 0** (slot waited 15 s, held 46 s). TRX roster contains all six witnesses above as Passed.
- Guard batch at 85a3c972, filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/*`: total 77, passed 68, **failed 9, all inherited**: every failure is `lane-xor Antiphon.IO.WriteBehind.Tests.WriteBehindFileSystemTests` (`Registry_matches_compiled_metadata`, `C487_G068`, `C1383_Valid_reason_pairs_are_accepted` x4, `C1383_Layout_opt_in_is_scoped(non-repository)`, `C1383_Rebase_preserves_distinct_appended_pairs` x2). Proof: detached base worktree at d2ff7e8d (build held 234 s, removed after), `(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)`: total 42, failed 7, the same seven names and message. Not caused by this diff (no policy/registry/WriteBehind file touched).
- Unlisted runs, with reason: the red2 rebuild/rerun (helper fix had to be re-proven red at base); the base worktree build + guard run (inherited-red proof); one mutant build + run for A-3 (its witness is green at base by design; first mutant run timed out in the slot queue at my 200 s outer limit, not run; rerun waited 75 s). Mutant source restored with `git checkout --`; outputs deleted.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef d2ff7e8d… -HeadRef HEAD(85a3c972)`: exit 0, commits=4, entries=0, violations=0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master `d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa`): exit 0, tree `2b8ea7af2eca5041361998be497b32886ca42ae1`, no conflicts.

## Owed / deferred (not passed)

- Whole Unit lane: not run in this task (brief scoped touched classes + guard batch); deferred to the nightly/rc backstop.
- PCs pending for method-scoped SourceLanding Mutation (none discharged here):
  - PC-1 identity compare by pid only (drop `backend_start` from `BackendIdentity` equality / holder read) → A-2 witness.
  - PC-2 `IsOwn` without `_backend` → A-3 witness (run once here as a can-go-red check only).
  - PC-3 `IsOwn` without `_pending`, or `_pending` assigned after the take → A-8 witness.
  - PC-4 wall-clock window → A-5 witness.
  - PC-5 drop a pinned doc sentence → doc witness.
  - PC-6 (no witness, advisory) `OwnOpenBackendStillHolds` `other != _pending` exemption: reachable only with an open own connection whose check fails while a pending orphan holds; no committed witness.
  - PC-7 (no witness, advisory) identity-conditional terminate in `TerminateBackend`: defence against pid reuse between the holder read and the terminate; not observable deterministically.
- Live fleet: GET /api/runner-defaults and /api/session-runners not read (no checkpoint tool run, no runner pin needed).
