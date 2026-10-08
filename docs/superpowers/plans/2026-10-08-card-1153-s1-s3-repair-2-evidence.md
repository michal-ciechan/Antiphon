# CARD-1153 S1-S3 repair 2 (Review a086fe80 F1-F3): Code evidence

Repair Code task `809c8b49` on branch `feat/card-task-809c8b49` (runner mirror worktree
`/work/worktrees/task-809c8b49`), fast-forward from the reviewed commit
`1fecea489a01eec6e37ab9537562f3c9b99e5586`. Original Code task and landing owner:
`4a4aacaa-a9bf-42fd-968f-98f62b5c4beb`. Findings: Final Review `a086fe80`
(`/work/review-evidence/a086fe80/review.md` on server2). Plan
`docs/superpowers/plans/2026-10-08-card-1153-runner-absence-evidence-plan.md` (D-2 replay and
closure paragraphs), test design `docs/superpowers/plans/2026-10-08-card-1153-test-design.md`
(H-14 rewritten, H-37, H-38, V-28..V-31, G-31..G-34, PC-30..PC-33, CP-41..CP-44, cost).

## Commits

| Repair | SHA |
|---|---|
| F1 names durable before a certificate | `f882161b263a31a8489f9dd147d024a8373fdcac` |
| F2 monotone replay admission | `df6811dff4cfd07053f8f41f5e06c273684c85bf` |
| F3 closure record and log agree (+ guard-line controls) | `e8c231ff05bc44a988efe857a309bde188bf45b7` |
| Docs (plan, test design) | `47a9420091851a65c27e59df3e1b206787d9cfa6` (tested SHA) |

## F1 (P1): no certificate before the closure's names are durable

`IRunnerAbsenceEvidenceFiles.SyncDirectory` is new. Production: Unix opens the directory
read-only and `fsync`s it; Windows uses `FlushFileBuffers` on a directory handle
(`FILE_FLAG_BACKUP_SEMANTICS`, read/write) and every rename is `MoveFileExW(... REPLACE_EXISTING |
WRITE_THROUGH)`; any failure throws. `Initialize` now: create the root; sync the parent of the
root and of every ancestor it created (outermost first); write `closed.log`, sync root; write
`store.json`, sync root; write the anchor last, sync its directory. Every record write syncs the
root after its rename. A healthy store this process did not initialize (an earlier process may
have died before the OS wrote its names) syncs root and anchor directory once before its first
write. So a write, and therefore a ClosedUnused record or a certificate, returns only after the
closure-log append is flushed and every name it depends on is durable; a sync failure makes
prepare refuse (503) and certify latch and refuse (503). Platform guarantee as stated in the
store XML: Linux/Unix rely on the POSIX directory fsync; Windows relies on write-through
renames plus a directory-handle flush and fails closed when the host refuses either. The
Windows path compiles and is reviewed but was not executed in this round (Linux mirror).

Tests: V-28 `C1153_Certified_closure_survives_power_loss` (11). `PowerLossEvidenceFiles` is an
in-memory volume where a name is durable only after the sync of its directory; a crash keeps
the durable names plus none, all, or only the latest pending name (arbitrary writeback order),
then drops names whose directory did not survive. Each case loses power right after one step
(root-created, closure-log-created, header-written, anchor-written, prepared-record-written,
closure-appended, closed-record-written) or after the certificate (certificate-returned,
session-log-path-created with missing ancestors, adopted-unsynced-store), under all three
writeback outcomes, restarts and requires: the store is usable; a returned certificate's
record survives and the id refuses fence, marker and certify; a logged closure refuses; an id
with neither stays open; the recovered store's next certificate survives a second power loss.
`sync-fails`: a failing root sync refuses the certificate and latches.

## F2 (P2): replay admission is monotone

`AdmitOnceLocked` keeps a per-epoch, in-memory high-water mark of issuedAtUtc values that
passed the freshness and epoch-start checks (it cannot exceed the runner clock plus 30 s). A
request issued more than 30 s before the mark is refused for good (401 stale); a consumed
nonce is retired only below the mark (no longer when the wall clock passes its expiry), so
neither eviction nor a wall-clock rollback revives an admitted request. The 4096 cap still
refuses (503) rather than evicting; the mark advances on a fresh authenticated request before
the cap check, so a later request retires the oldest entries and a flood cannot wedge the
cache. The wall clock is compared with `TimeProvider.GetTimestamp` at every request; a backward
step beyond 500 ms plus 0.1% of the monotonic gap refuses every evidence request (503
`absence_evidence_unavailable`, reason "runner clock stepped backwards") for one full window of
monotonic time. No new wire code. Tests: V-29 `C1153_Replay_admission_is_monotone` (5) with
`SteppingWallClock` (runner wall clock = FakeTimeProvider plus an offset; monotonic unchanged).

## F3 (P2): disagreeing closure metadata is unknown

Each `closed.log` line is now `<id:N> <SHA-256 of the exact ClosedUnused record bytes>
<SHA-256 of the previous line, or a seed bound to the incarnation>\n` (163 bytes). `Read`
returns the new kind `ClosureMismatch` for a ClosedUnused record without an entry or one whose
bytes differ from the logged hash (covers epoch, generation, store, identity and timestamps);
a broken chain (duplicated, reordered, removed or foreign line) or a bad length makes the whole
store `StoreUnknown`. Both refuse creation with the closed-identity type and refuse
certification. The round-1 33-byte format was never deployed; a store written by it reads
malformed (unknown), so no migration. Tests: V-30 `C1153_Closure_record_and_log_must_agree` (5:
record-without-log-entry, last-entry-truncated, record-differs-from-logged-closure,
duplicate-entry, reordered-entries; certify is asserted first with no prior fence latch, then
fence and marker after a restart). Log entry without record remains V-25 deleted-record and
reverted-record (green at base by design).

Guard lines the previous round disclosed (V-31 `C1153_Store_guard_lines_fail_closed`, 2):
attempt-store-turns-unknown (anchor unreadable on the marker's write-time read: closed-identity
refusal, latch, no marker) and closure-log-length-malformed. The anchor-last order is guarded
by V-28 anchor-written/header-written (PC-30 variant 6) and the 4096 cap by V-29
flood-then-replay-at-cap (PC-31 variant 4).

## Changed assertions

None weakened or deleted. Existing tests changed only through the fixture: the harness service
now takes `RunnerClock` (zero offset = the same clock, so every existing assertion is unchanged)
and `StoreFiles`/`StorePath` default to the previous values. V-1 still expects 4 writes (syncs are
not writes).

## Red proofs

At `1fecea48` (scratch detached worktree, HEAD test files copied, one inert shim
`RunnerAbsenceEvidenceFiles.SyncDirectory(string) { }` added to the base production class only so
the fixture compiles; the base store never calls it): `/*/*/RunnerAbsenceEvidence*/*` 116
executed, 95 passed, **21 failed = every new F1-F3 case** (V-28 11, V-29 5, V-30 5), each on its
intended assertion (lost certificate record; replay admitted; disagreeing closure read as known).
The 93 pre-existing cases pass at base with the new fixture. V-31's 2 cases pass at base by design
(they guard existing lines) and go red under their mutations below.

Quick method-scoped mutations at HEAD (script: apply, isolated build, run the named method,
`git checkout` restore; not PCs, all pending as PC-30..PC-33):

| Mutation (production line) | Filter | Result |
|---|---|---|
| M1-A `EnsureNamespaceDurable` syncs removed | V-28 | 1/11 red: adopted-unsynced-store |
| M1-B anchor-directory sync removed | V-28 | 9/11 red |
| M1-C created-ancestor parent syncs removed | V-28 | 2/11 red: anchor-written, session-log-path-created |
| M1-D root sync after header removed | V-28 | 2/11 red: anchor-written, prepared-record-written |
| M1-E root sync after record write removed | V-28 | 11/11 red |
| M1-F root sync after closure-log creation removed | V-28 | 0/11: equivalent (the post-header root sync persists it before anything depends on it) |
| M1-G anchor written before header | V-28 | 2/11 red: anchor-written, header-written |
| M2-A backward-step check disabled | V-29 | 2/5 red: expiry-then-rollback, forward-jump |
| M2-B high-water refusal removed | V-29 | 2/5 red: rollback-then-identical, flood-then-replay-at-cap |
| M2-C nonce retirement by wall clock (`issued + window < now`) | V-29 | 1/5 red: small-step-under-skew |
| M2-D 4096 cap removed | V-29 | 1/5 red: flood-then-replay-at-cap |
| M3-A unlogged-ClosedUnused check removed | V-30+V-31 | 2/7 red: record-without-log-entry, last-entry-truncated |
| M3-B record-hash compare removed | V-30+V-31 | 1/7 red: record-differs-from-logged-closure |
| M3-C chain compare removed | V-30+V-31 | 1/7 red: reordered-entries |
| M3-D `closed.TryAdd` duplicate refusal replaced by overwrite | V-30+V-31 | 0/7: survives (a raw duplicate breaks the chain first); missing-control candidate |
| M3-E closure-log length check removed | V-31 | 1/2 red: closure-log-length-malformed |
| M3-F marker `RunnerAbsenceStoreUnknownException` catch removed | V-31 | 1/2 red: attempt-store-turns-unknown |

Every new case is killed by at least one mutation and fails at base, except V-31 (base-green by
design).

## Checkpoint run (closed list)

Run `20261008-063031-97ac`, hand-made manifest (the 42 rows of Review a086fe80's run plus
CP-41..CP-44 from the revised test design; copy outside the repo), `start --plan --after R --serial
--expected-source-sha 47a94200`: verdict GREEN, **46 rows, 489 executed, 489 passed, 0 failed,
0 skipped**, 2 builds (`UseAppHost=false`; runner 20 s, server 147 s), max concurrent builds 1,
every slot granted waited=0s, wall 9m11s, `unlisted: none`, outputs deleted by the tool;
`validate`: `CHECKPOINT SOURCE VALID source=47a9420091851a65c27e59df3e1b206787d9cfa6 rows=46`.
The 42 inherited rows reproduce the Review's 466 executions with identical per-row counts.

    CHECKPOINT CP-1 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=ok filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_records_only_a_fresh_identity* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-2 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_refuses_existing_evidence* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-3 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Restart_or_store_change_never_renews_proof* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-4 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Unknown_or_unreadable_state_is_not_absence* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-5 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Certify_requires_every_fact* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-6 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Creation_consumes_proof_before_effects* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-7 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Store_failure_disables_proof_without_stopping_work* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-8 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Certificate_and_launch_race_is_serialized* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-9 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Closed_identity_refuses_delayed_creation* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-10 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/UnixPtyArgvAdmissionTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-11 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceContractTests/C1153_Real_unknown_transcript_has_a_separate_certificate* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-12 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceContractTests/C1153_Http_authentication_covers_request_and_response* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-13 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidencePhoneHomeTests/C1153_Authenticated_operation_preserves_binding* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-101 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/TestClassificationGuardTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-101/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-102 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/HerdrAttachTests/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-102/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-103 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/TerminalSeatReleaseTests/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-103/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-104 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerCapabilitiesTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-104/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-105 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/PhoneHomeCommandDispatcherTests/* executed=43 passed=43 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-105/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-106 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/GrokRulesFileLaunchTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-106/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-107 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/GrokRulesRunnerRefusalTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-107/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-108 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/HerdrRunnerSessionTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-108/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-109 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/HerdrLaunchShapeTests/* executed=37 passed=37 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-109/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-110 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/HerdrAdoptionSweepTests/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-110/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-115 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidencePhoneHomeTests/C1153_Authenticated_operation_preserves_binding* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-115/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-116 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_records_only_a_fresh_identity* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-116/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-117 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_refuses_existing_evidence* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-117/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-118 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Restart_or_store_change_never_renews_proof* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-118/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-119 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Unknown_or_unreadable_state_is_not_absence* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-119/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-120 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Certify_requires_every_fact* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-120/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-37 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Closure_survives_storage_failure* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-38 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Certified_identity_survives_store_damage* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-38/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-39 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceContractTests/C1153_Replayed_request_is_rejected* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-41 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Certified_closure_survives_power_loss* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-42 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Replay_admission_is_monotone* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-43 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Closure_record_and_log_must_agree* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-44 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Store_guard_lines_fail_closed* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-14 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=ok filter=/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Rejects_noncertificate_wire_shapes* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-15 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Freshness_and_cancellation_are_bounded* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-16 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/RunnerAbsenceEvidenceValidatorTests/C1153_Validator_requires_every_fact* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-17 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/PhoneHomeConnectionTests/Authentication_is_required_at_both_endpoints* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-111 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/TestClassificationGuardTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-111/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-112 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/SlowTestTripwireTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-112/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-113 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/PhoneHomeConnectionTests/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-113/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-114 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/SessionRunnerGenerationWireTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-114/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-121 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Caller_note_has_one_complete_user_prompt* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-121/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified
    CHECKPOINT CP-40 commit=47a9420091851a65c27e59df3e1b206787d9cfa6 build=reused filter=/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Deadline_covers_capability_discovery* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-809c8b49/.antiphon/checkpoints/20261008-063031-97ac/rows/CP-40/run.trx slot=granted waited=0s dirty=0 source=47a9420091851a65c27e59df3e1b206787d9cfa6 sourceState=clean buildSource=verified

## Other builds and runs (all slot-gated, all disclosed)

Every one ran through `scripts/build-slot.ps1` (each `BUILD SLOT granted`, waited=0s); builds
`tests/Antiphon.SessionRunner.Tests` with `UseAppHost=false` to `bin-c1153r2-dev/`,
`bin-c1153r2-mut/` or (base) `bin-c1153r2-base/`, all deleted afterwards. Lease prefix and held
time; reasons: compile and dev verification, quick mutations and the red-at-base proof the brief
requires.

| # | Label | Build lease / held | Run lease / held | Filter | Outcome |
|---|---|---|---|---|---|
| 1 | f1 dev | b7df4ab8 / 21 s | 732060de / 5 s | `RunnerAbsenceEvidence*/*` | 104/104 passed |
| 2 | f1 dev 2 | d9847a24 / 15 s | 1975365e / 4 s | same | 104/104 passed |
| 3-9 | M1-A..M1-G | 30debe95 19 s, 9d94c608 12 s, 7b1571d9 11 s, 6abf01bb 12 s, a8f1dbe5 11 s, 6fbc337a 11 s, 0bc04573 17 s | 817740b1, 6ad1a628, 180d7317, f3c2a777, 549dd9de, ec9ad4e7, ae955f20 / 1-2 s | V-28 | see mutation table |
| 10 | f2 dev | 3c70fcde / 18 s | 10576987 / 5 s | `RunnerAbsenceEvidence*/*` | 109/109 passed |
| 11-14 | M2-A..M2-D | 28ba23bb 19 s, 749cdb66 12 s, a64ffc81 17 s, 2fde90a2 18 s | f45f6b3d, 66487c97, e32850f3, b64d2c90 / 3-4 s | V-29 | see mutation table |
| 15 | f3 dev | 1b60a191 / 43 s | 9d24afaa / 5 s | `RunnerAbsenceEvidence*/*` | 116/116 passed |
| 16-21 | M3-A..M3-F | b1ca15b3 17 s, b7063a22 12 s, 2687351d 10 s, db16b609 13 s, dbdf39d2 11 s, 16c0245a 12 s | 470fc390, 08577ecf, 4bcd2735, b0a50f31, 9024f936, 01727701 / 1-2 s | V-30+V-31 / V-31 | see mutation table |
| 22 | base red (1fecea48 + shim) | 5dc5315d / 16 s | b367a8f4 / 5 s | `RunnerAbsenceEvidence*/*` | 116 executed, 21 failed (intended) |
| 23 | checkpoint tool bootstrap (`tools/Antiphon.Checkpoints` -> `bin-c1153r2drv/`) | 4f3e3ddd / 6 s | - | - | ok |

Totals outside the checkpoint run: 23 builds (22 runner-test, 1 tool), 22 test runs. The tool's
`import` (manifest and test design: 46 and 44 rows, no warnings) and `validate` commands are not
builds or test runs. No zero-test run, no rerun of any checkpoint row, no driver outside the gate.
Trial rebase (below) is git only.

## Not run / deferred

- Whole Unit lane: not run (AGENTS.md forbids it here; not part of this brief's ordinary scope).
- CP-18..CP-34 (S4/S5 and final-group rows) and Windows rows CP-35 (R-9), CP-36 (R-5): not run.
  The Windows directory-sync and write-through-rename path is implemented but unexecuted.
- Every PC-1..PC-33 variant (165) stays pending for method-scoped SourceLanding Mutation; the
  quick mutations above are not PCs. Missing-control candidate: `closed.TryAdd` (M3-D).
  Equivalent mutant: the root sync after closure-log creation (M1-F).

## Source, merge and safety audit

- `scripts/check-evidence-diff.ps1`: see the final report for `1fecea48..HEAD` and
  `061e29c3..HEAD`. `git diff --check` clean.
- `origin/master` = `65745cfa3aee43542fbeb6711996c769119028df`; `git merge-tree --write-tree HEAD
  origin/master` exit 1 with the expected 8 add/add conflicts (6 test skeletons, plan, test
  design). Trial rebase of HEAD in a scratch worktree skipped `999de4d8b`, `bb77118e9`,
  `061e29c34` and applied all 16 candidate commits cleanly (trial head
  `20971c753853a8f5c7a319ab77b2ee63ab8eec94`); this branch was not rebased.
- Untouched over `061e29c3..HEAD`: `AgentTaskDispatcher.cs`, `SessionMessageQueueService*`,
  `SessionReconciliationService.cs`, `scripts/c590-remote.sh`; no migration. Round 2 changes only
  `RunnerAbsenceEvidenceStore.cs`, `RunnerAbsenceEvidenceService.cs`, the S1 test fixture and
  tests, and the two plan documents. Nothing stops, releases or fails a Working session: F1 adds
  syncs to evidence writes, F2 refuses only evidence requests, F3 refuses only creation or
  certification of an id whose closure metadata disagrees (existing sessions and adoption are
  unaffected; launches are refused only on unknown evidence as before).
- No key material, MACs or signatures in logs, receipts or test output; refusal reasons carry no
  request bytes.
- GET `/api/runner-defaults` and `/api/session-runners` read (200); no host pin, no fleet path.

## Activation and compatibility

Runner first (`pwsh -NoProfile -File scripts/restart-session-runner.ps1` from the canonical
checkout, no `-KillSessions`; server2 through the rolling phases), then AppHost
(`scripts/restart-apphost.ps1`, confirm `/api/version`). Restart: runner then server; owner: the
orchestrator after land. Round 2 changes no wire shape and no refusal code (a clock step answers
the existing 503 `absence_evidence_unavailable`). Cross-version: S1-S3 were never deployed. A
runner that ran a round-1 build of this branch has a 33-byte-line `closed.log`, which reads
malformed: that runner refuses creation of new ids until an operator moves `absence-evidence/` and
its anchor aside (the round-1 recovery procedure). New runner / old server: markers and the
fail-closed fence apply to launches; nothing calls prepare/certify. New server / old runner:
capability absent, the client returns Unsupported. Operating requirement made explicit by H-14:
server and runner wall clocks must agree within 30 s, or every evidence request refuses (the
dispatcher keeps Failed).
