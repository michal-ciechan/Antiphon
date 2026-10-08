# CARD-1153 S1-S3 repair F1-F3: Code evidence

Repair Code task `f9488462` on branch `feat/card-task-f9488462` (runner mirror worktree
`/work/worktrees/task-f9488462`), fast-forward from the reviewed commit
`a3269946be08276d7b4e441a8b2789f75a30c7eb`. Original Code task and landing owner:
`4a4aacaa-a9bf-42fd-968f-98f62b5c4beb`. Findings: Final Review `1a174347`
(`/work/review-evidence/1a174347/review.md` on server2). Plan
`docs/superpowers/plans/2026-10-08-card-1153-runner-absence-evidence-plan.md`, test design
`docs/superpowers/plans/2026-10-08-card-1153-test-design.md` (rows CP-37..CP-40, V-25..V-27,
H-35/H-36 added; H-2 revised).

## Commits

| Repair | SHA |
|---|---|
| F1 closure survives storage failure | `91e0a8a5df9285bca3b861a1a555111643ffc478` |
| F2 replay rejection | `ee530cf3bf1f1b33b3dc093919215910f741e5df` |
| F3 deadline covers discovery | `3d2ed8e8ee4c61434a8b080243fc0e9c21fdf583` |
| Docs (plan, test design) | `4506df592dc9519e3bcc9fd5c465e17dc66c3a84` |

## F1 (P1, fail-open after certification)

Change. `RunnerAbsenceEvidenceStore` gains a durable identity: on first write it creates the
root, an empty closure log `closed.log`, a header `store.json`, and last an anchor
`absence-evidence.identity.json` beside the root, header and anchor naming one random
incarnation. A closure appends the id to the closure log (flushed) before the ClosedUnused
record is written. Positive states are only "never initialized" (no anchor; no record or logged
closure under the root) and "healthy" (anchor equals header equals the incarnation this process
first saw; closure log present and well formed). Missing or changed anchor or header, lost root,
missing or malformed closure log, and a logged closure whose record is missing, unreadable or
no longer ClosedUnused are unknown kinds (`StoreUnknown`, `ClosedRecordLost`, plus the existing
`Corrupt`, `Denied`, `IoError`, `LostRoot`, `UnknownSchema`). The fence (`RequireOpenIdentity`)
and the marker (`RecordCreationAttempt`) now admit only a positive read; ClosedUnused and every
unknown read throw `SessionIdentityClosedException` (new `EvidenceProblem`, same HTTP 409
`session_identity_closed` / phone-home `phone_home_session_identity_closed` mapping, so the
server's existing refused-launch path), latch, and write nothing. The optional Attempted write
may still fail open, but only after a positive read (V-6 unchanged). Certify/prepare refuse on
an unknown store (`Ready`/`Unready` read store health). Adoption keeps existing work and
latches. Rename durability is qualified in the store XML: the directory is not fsynced; a lost
rename leaves the flushed closure-log entry, which reads as `ClosedRecordLost`. Residual
(H-24 tampering): deleting both a closed record and its log line, or the anchor together with
the whole root, is indistinguishable from never-closed.

Recovery: a store that reads unknown refuses every creation on that runner until an operator
repairs or deliberately retires the evidence store (stop the runner, confirm no certificate it
issued is still relied on, move `absence-evidence/` and its anchor aside). No automatic reset.

Tests. V-25 unit `C1153_Closure_survives_storage_failure` (12: corrupt-record,
truncated-record, zero-length-record, deleted-record, directory-in-place, denied-read,
read-error, store-wiped, store-replaced, lost-root, reverted-record, anchor-lost) and runtime
`C1153_Certified_identity_survives_store_damage` (12 fault/entry/restart cases incl. real
`chmod 000`); V-23 + `store-header-changed`. V-1 now expects the record plus the three store
identity files and 4 writes (expectation updated, not weakened).

## F2 (P2, replay) - decision: reject

Idempotent acceptance would have to prove that a replay can never change state, extend a
window or revive an expired nonce, and it contradicted the brief; rejection is fail closed and
cheaper to prove. Each request now carries a signed `issuedAtUtc` (sixth body member, covered by
the request MAC through the body digest; both transports parse the exact member set). The
shared evidence service admits each well-formed request at most once per runner epoch, whatever
its operation or outcome: issued within `RequestFreshness` (30 s) of the runner clock, and after
the epoch start plus 30 s; nonce cache bounded at 4096 (refuses with 503 when full, never evicts
a live nonce; an entry is forgotten only once its request is stale). Restart: the cache is
memory only, so any request issued before the new process's start plus 30 s is refused as stale
(a pre-restart capture never meets an empty cache); the first 30 s after a runner start refuse
evidence requests (fail closed, launches unaffected). Codes: `absence_evidence_replayed` (409),
`absence_evidence_stale_request` (401); refusals are MAC-authenticated answers. A fresh nonce for
the same identity still behaves as before (prepare returns the original record; certify on
ClosedUnused re-runs every exclusion). H-2 and plan D-2 now say this.

Changed test assertions (none weakened or deleted): V-23 `prepared-prior-epoch` and
`closed-prior-epoch` now sign their request after the restart (a request signed before the new
epoch would be refused as stale instead of exercising the prior-epoch rule; same refusal
asserted). No existing test encoded replay acceptance (V-1 and V-8 re-requests use fresh nonces).

Tests. V-26 `C1153_Replayed_request_is_rejected` (prepare-twice, certify-twice, cross-route,
after-skew-window, across-restart) over the production routes; V-23 + replayed-nonce,
stale-issued-at, future-issued-at, issued-in-epoch-window, issued-at-missing (V-23 now 28).

## F3 (P2, deadline)

`SessionRunnerHttpClient.ExchangeAbsenceAsync` creates the five-second deadline (injected
clock) and the linked token before `GetCapabilitiesAsync`; a deadline during discovery is
`absence_evidence_deadline` (certify Unknown, prepare not prepared), caller cancellation still
propagates, and the validator's elapsed time (`sentAt`) starts there. `PhoneHomeRunnerClient`
already bounded discovery; its `sentAt` now also starts before discovery. V-27
`C1153_Deadline_covers_capability_discovery` (certify/prepare x pending-discovery,
discovery-plus-post) with a pristine 2 s + 2.9 s control per method.

## Red proofs

At `a3269946` (scratch worktree; HEAD test files copied; for F2 an inert compile shim adding
only `RunnerAbsenceRequest.IssuedAtUtc` and the new constants, never read or serialized):
all 25 F1 cases red (plus V-1's updated expectation), all 10 F2 cases red, all 4 F3 cases red
(2 by the test's own 10 s wait: discovery never ends at base).

Quick method-scoped mutations at HEAD (applied by script, build, run, `git checkout` restore;
these are not PCs):

| Mutation (production line) | Red cases |
|---|---|
| F1-A: `Read` record-absent closed -> NoRecord; `Inspect` header-vs-anchor compare dropped; anchor-present lost root -> fresh; in-process anchor-lost branch disabled (batched, disjoint sets) | deleted-record (unit, runtime x2); store-replaced, V-23 store-header-changed; lost-root (unit, runtime x2, V-4 lost-root); anchor-lost |
| F1-B: `Read` logged-closure-not-ClosedUnused check dropped; header-missing -> healthy | reverted-record; store-wiped (unit, runtime x2) |
| F1-C: `AdmitLocked` unknown read returns instead of throwing | all 12 unit cases; 10 of 12 runtime cases (the other two are still refused by the marker's write-time store check) |
| F1-D: closure log append skipped | deleted-record (unit, runtime x2), reverted-record |
| F2-A: freshness window check and epoch-start guard dropped | stale-issued-at, future-issued-at, issued-in-epoch-window, after-skew-window, across-restart |
| F2-B: nonce containment check and issuedAt presence check dropped | replayed-nonce, issued-at-missing, prepare-twice, certify-twice, cross-route |
| F3-A: discovery on the caller token | certify/prepare pending-discovery |
| F3-B: deadline budget doubled | all 4 F3 cases and V-13 over-5s-deadline |

No new case survives every mutation. Lines without a dedicated killing case (pending for
SourceLanding Mutation as missing-control candidates): the marker's
`RunnerAbsenceStoreUnknownException` catch (store turns unknown between read and write);
`Initialize` writing the anchor last; the replay-cache 4096 cap; the closure-log malformed-length
branch.

## Checkpoint run (closed list)

Run `20261008-044146-4aac`, hand-made manifest (CP-1..CP-17 and CP-37..CP-40 from the test
design, the S1-S3 Code report's supplemental CP-101..CP-120; CP-5/CP-120 Min 28), `--after R
--serial --expected-source-sha 4506df59`: verdict GREEN, 41 rows, 463 executed, 463 passed,
0 failed, 0 skipped, 2 builds (`UseAppHost=false`), max concurrent builds 1, wall 7m47s,
`unlisted: none`; `validate`: `CHECKPOINT SOURCE VALID source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 rows=41`.

```
CHECKPOINT CP-1 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=ok filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_records_only_a_fresh_identity* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_refuses_existing_evidence* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Restart_or_store_change_never_renews_proof* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Unknown_or_unreadable_state_is_not_absence* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Certify_requires_every_fact* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Creation_consumes_proof_before_effects* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Store_failure_disables_proof_without_stopping_work* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Certificate_and_launch_race_is_serialized* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Closed_identity_refuses_delayed_creation* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/UnixPtyArgvAdmissionTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceContractTests/C1153_Real_unknown_transcript_has_a_separate_certificate* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceContractTests/C1153_Http_authentication_covers_request_and_response* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidencePhoneHomeTests/C1153_Authenticated_operation_preserves_binding* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Closure_survives_storage_failure* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Certified_identity_survives_store_damage* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-38/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceContractTests/C1153_Replayed_request_is_rejected* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-101 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/TestClassificationGuardTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-101/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-102 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/HerdrAttachTests/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-102/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-103 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/TerminalSeatReleaseTests/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-103/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-104 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerCapabilitiesTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-104/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-105 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/PhoneHomeCommandDispatcherTests/* executed=43 passed=43 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-105/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-106 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/GrokRulesFileLaunchTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-106/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-107 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/GrokRulesRunnerRefusalTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-107/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-108 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/HerdrRunnerSessionTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-108/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-109 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/HerdrLaunchShapeTests/* executed=37 passed=37 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-109/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-110 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/HerdrAdoptionSweepTests/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-110/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-115 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidencePhoneHomeTests/C1153_Authenticated_operation_preserves_binding* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-115/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-116 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_records_only_a_fresh_identity* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-116/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-117 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_refuses_existing_evidence* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-117/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-118 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Restart_or_store_change_never_renews_proof* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-118/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-119 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Unknown_or_unreadable_state_is_not_absence* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-119/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-120 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Certify_requires_every_fact* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-120/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=ok filter=/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Rejects_noncertificate_wire_shapes* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Freshness_and_cancellation_are_bounded* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/RunnerAbsenceEvidenceValidatorTests/C1153_Validator_requires_every_fact* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/PhoneHomeConnectionTests/Authentication_is_required_at_both_endpoints* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Deadline_covers_capability_discovery* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-40/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-111 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/TestClassificationGuardTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-111/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-112 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/SlowTestTripwireTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-112/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-113 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/PhoneHomeConnectionTests/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-113/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
CHECKPOINT CP-114 commit=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 build=reused filter=/*/*/SessionRunnerGenerationWireTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-f9488462/.antiphon/checkpoints/20261008-044146-4aac/rows/CP-114/run.trx slot=granted waited=0s dirty=0 source=4506df592dc9519e3bcc9fd5c465e17dc66c3a84 sourceState=clean buildSource=verified
```

## Other builds and runs (all slot-gated, all disclosed)

Dev and proof runs on server2 (each `BUILD SLOT granted`, waited 0s): runner-tests builds x12
(12-33 s) and test-host runs of `/*/*/RunnerAbsenceEvidence*Tests/*` x12 (5-10 s each):
dev 81/81, 83/83, 93/93; base reds 24/81, 26/83, 36/93 failed; mutations F1-A 10, F1-B 4,
F1-C 22, F1-D 4, F2-A 5, F2-B 5 failed of 83/93. `Antiphon.Tests` builds x5 (61-146 s; one
first build failed on a test syntax error, fixed before commit) and runs of
`/*/*/SessionRunnerAbsenceEvidenceClientTests/*` x4: dev 22/22, base 4/22 failed, F3-A 2/22,
F3-B 5/22 failed. One zero-test run (a malformed OR filter) was discarded. Checkpoint tool
bootstrap build x1 (5 s). Reasons: compile and dev verification, red-at-base proofs, quick
mutations required by the brief.

## Not run / deferred

- Whole Unit lane: not run (AGENTS.md/brief forbid it; not part of this ordinary scope).
- CP-18..CP-34 (S4/S5 and final-group rows) and Windows rows CP-35 (R-9), CP-36 (R-5): not run.
- Every PC-1..PC-29 variant stays pending for method-scoped SourceLanding Mutation, plus the
  missing-control candidates above.

## Source, merge and safety audit

- `scripts/check-evidence-diff.ps1`: `a3269946..HEAD` commits=4 entries=0 violations=0;
  `061e29c3..HEAD` commits=11 entries=0 violations=0. `git diff --check` clean.
- No migration; `AgentTaskDispatcher.cs`, `SessionMessageQueueService*`,
  `SessionReconciliationService.cs`, `scripts/c590-remote.sh` untouched over `061e29c3..HEAD`;
  boot-stall tail untouched. Nothing in S1-S3 stops, releases or fails a Working session: F1
  refuses only new creation/attach of an id; adoption of existing sessions is preserved.
- No key material or MACs in logs, receipts or test output (V-10 canary unchanged and green).
- `origin/master` = `ce338f69766fe6a100acb835f67593713fdf564c`; `git merge-tree --write-tree
  HEAD origin/master` exit 1 with 8 add/add conflicts (the 6 test skeletons plus the plan and
  test-design docs master holds as rebased planning commits). Trial rebase in a scratch
  worktree skipped the previously applied planning commits and applied all 11 candidate commits
  cleanly (trial head `7952edaf3f0a41aeac4023cfd0d72e5831632591`); this branch was not rebased.
- GET `/api/runner-defaults` and `/api/session-runners` read (200); no host pin.

## Activation and compatibility

Runner first (`pwsh -NoProfile -File scripts/restart-session-runner.ps1` from the canonical
checkout, no `-KillSessions`; server2 by the rolling phases), then AppHost
(`scripts/restart-apphost.ps1`, confirm `/api/version`). Restart owner: the orchestrator after
land; restart: runner then server. Cross-version: S1-S3 have never been deployed, so the wire
change (sixth member `issuedAtUtc`) has no deployed peer. New runner / old server: markers and
the fail-closed fence apply to launches; nothing calls prepare/certify. New server / old runner:
capability absent, client returns Unsupported. A new server against an S1-S3-era runner that
lacks `issuedAtUtc` parsing would get 400 (no proof; existing Failed path). Note the new launch
refusal: a runner whose evidence store reads unknown refuses creation until repaired (F1).
