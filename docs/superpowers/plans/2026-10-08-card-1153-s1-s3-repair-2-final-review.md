# CARD-1153 S1–S3 repair 2 — Final Review

subjectTaskId: 809c8b49-e3e4-4df5-9b44-3dee599082ff
reviewedSourceSha: cb466647676546da506975b08ca0c302fafd15d0
original Code landing owner: 4a4aacaa-a9bf-42fd-968f-98f62b5c4beb
review task: e3d066f6
verdict: clean

Reviewed the unchanged tip of `feat/card-task-809c8b49` (five commits over `1fecea489a01eec6e37ab9537562f3c9b99e5586`: F1 `f882161b2`, F2 `df6811dff`, F3 `e8c231ff0`, docs `47a942009`, evidence `cb4666476`). Code at `47a942009` and `cb4666476` is the same; the tip commit adds the repair-2 evidence note. This review branch was not rebased.

## Outcome

No fail-open. A certificate is returned only after `Store.Write` has appended and flushed the closure-log line and synced the directory that holds the closed record. A crash after that return does not read the id as an open identity. Replay admission stays monotone under rollback, restart, and the 4096 cap. A torn or disagreeing closure log reads as unknown for the store or for that id, and creation is refused. Nothing in S1–S3 stops, releases, kills, or fails a Working session.

## Ordinary scope

Run `20261008-070954-2ed8`, checkpoint tool, `--serial`, `--expected-source-sha cb466647676546da506975b08ca0c302fafd15d0`, `--total-timeout 90m`, hand-made manifest outside the repo (the plan file's first `### Checkpoints` table is still the pre-repair numbering; the rows below are the test-design list CP-1..CP-17, CP-37..CP-44, CP-101..CP-121). `UseAppHost=false` injected off Windows. Four isolated builds (`bin-c1153-s1`, `bin-c1153-s2`, `bin-c1153-s3-runner`, `bin-c1153-s3-client`), max concurrent builds 1, every slot granted (one row waited 15s). Wall 11m11s. `unlisted: none`. Tool deleted its output directories.

`validate`: `CHECKPOINT SOURCE VALID source=cb466647676546da506975b08ca0c302fafd15d0 rows=46`.

46 rows, 489 executed, 489 passed, 0 failed, 0 skipped. Every row `dirty=0 sourceState=clean buildSource=verified`.

| CP | executed | CP | executed | CP | executed |
|---|---:|---|---:|---|---:|
| CP-1 | 1 | CP-11 | 1 | CP-40 | 4 |
| CP-2 | 9 | CP-12 | 4 | CP-41 | 11 |
| CP-3 | 3 | CP-13 | 3 | CP-42 | 5 |
| CP-4 | 5 | CP-14 | 14 | CP-43 | 5 |
| CP-5 | 28 | CP-15 | 4 | CP-44 | 2 |
| CP-6 | 3 | CP-16 | 35 | CP-101 | 1 |
| CP-7 | 1 | CP-17 | 1 | CP-102 | 23 |
| CP-8 | 2 | CP-37 | 12 | CP-103 | 38 |
| CP-9 | 4 | CP-38 | 12 | CP-104 | 7 |
| CP-10 | 12 | CP-39 | 5 | CP-105 | 43 |
| CP-111 | 1 | CP-112 | 2 | CP-106 | 21 |
| CP-113 | 29 | CP-114 | 9 | CP-107 | 11 |
| CP-115 | 3 | CP-116 | 1 | CP-108 | 7 |
| CP-117 | 9 | CP-118 | 3 | CP-109 | 37 |
| CP-119 | 5 | CP-120 | 28 | CP-110 | 22 |
| CP-121 | 3 | | | | |

Unedited verdict line: `wall: 11m11s  sequential-equivalent: 7m23s  builds: 4  max-concurrent-builds: 1  rows: 46 green 0 red 0 skipped`.

Not run: CP-18..CP-34 (S4/S5) and Windows rows CP-35, CP-36. PC-1..PC-33 stay pending. The whole Unit lane was not run; this brief's ordinary scope is the 46 rows above.

## Durability order

Production store construction is `new RunnerAbsenceEvidenceStore(sessionLogPath)` in `SessionRunnerRuntime`, which uses `RunnerAbsenceEvidenceFiles.Instance`. Linux `SyncDirectory` opens the directory `O_RDONLY` and calls libc `fsync`. The test counting wrapper calls that same instance unless a sync fault is injected. Windows `WriteAtomic` uses `MoveFileExW` with `MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH`. Windows `SyncDirectory` opens the directory with `GENERIC_READ|GENERIC_WRITE`, share mode 7, `OPEN_EXISTING`, `FILE_FLAG_BACKUP_SEMANTICS`, then `FlushFileBuffers`. A failed open or flush throws. Certify catches that, latches, and returns 503 with no certificate.

Initialize, before any certificate:

1. Create the evidence root and any missing ancestors.
2. `SyncDirectory` of each created directory's parent, outermost first. That covers each new directory name.
3. `WriteAtomic` of an empty `closed.log` (file flush, then rename), then `SyncDirectory(root)`. That covers the log name.
4. `WriteAtomic` of `store.json`, then `SyncDirectory(root)`. That covers the header name and also the log name if step 3 were skipped.
5. `WriteAtomic` of the anchor last, then `SyncDirectory` of the root's parent. That covers the anchor name.

`Write` of `ClosedUnused`: `AppendDurable` of the closure line (content flushed on an already-named file), then `WriteAtomic` of the record, then `SyncDirectory(root)`. `Certify` returns `Ok` only after that `Write` returns. An adopted healthy store calls `EnsureNamespaceDurable` once, syncing root and parent, before its first write.

Crash after a returned certificate: the closure line and the record's directory entry are durable on the Linux path the tests model. A missing record with a durable log line is `ClosedRecordLost`. A `ClosedUnused` record with no log entry, or a record whose bytes differ from the logged hash, is `ClosureMismatch`. A partial last line (length not a multiple of 163) makes the whole store `StoreUnknown`. Dropping a complete last line leaves the earlier chain valid and that id `ClosureMismatch`. Anchor present and root missing is `LostRoot`. All of those refuse creation. A crash before the anchor, with only a header and an empty log, reads as never initialized, and no certificate has been returned yet.

The power-loss fixture keeps a name only after `SyncDirectory` of its parent, and `Crash` drops names whose parent directory did not survive. Mutating `PowerLossEvidenceFiles.SyncDirectory` to return without promoting pending names made V-28 fail 10 of 11 (the `sync-fails` arm uses the fault seam, not that volume). Restored.

Windows, read and not executed: `MOVEFILE_WRITE_THROUGH` is documented to return only after the rename is on disk. `FlushFileBuffers` is documented for a file or a volume and requires write access, which the code requests. It is not documented as the POSIX directory-entry fsync. A refused open or flush fails closed, so no certificate. If a flush returns success without persisting a new directory, a pre-existing session-log directory plus a write-through anchor still leaves "anchor present, evidence root gone", which is `LostRoot` and refuses creation. The plan text names those two Windows APIs and the fail-closed throw; that matches the code. The outcome word "durable" for a newly created ancestor is stronger than Microsoft's documented guarantee. CP-35 and CP-36 remain the rows that would execute that path.

## Replay

High water only increases, and only for an `issuedAtUtc` that already passed the ±30s freshness check. A request issued more than 30s before the mark is 401. A nonce is removed only once its issued time is below `mark - 30s`, so forgetting it cannot admit the same request again. The 4096 cap returns 503 and does not evict; the mark advances before the cap check. Identical requests take the same lock, so one is 409 once the other has stored the nonce. After restart the cache is empty and any request issued at or before `epochStart + 30s` is stale.

The step detector compares wall time with monotonic elapsed time. The allowance is 500ms plus 0.1% of the gap (1000 ppm). Ordinary NTP slew sits under that, so it does not trip. One step larger than the allowance refuses every evidence request for 30s of monotonic time (the dispatcher keeps its Failed path) and the new wall time becomes the baseline, so a stable clock does not trip again. Further large steps restart that 30s window. A step under 500ms can leave the mark above the rolled-back clock plus 30s; the effect is a stricter refusal. The evidence note's present-tense sentence that the mark cannot exceed the runner clock plus 30s describes the mark at the moment it is stored.

H-14's residual stands as written: the mark is memory-only, so a runner that starts more than about 30s behind and then jumps forward can admit a pre-restart request for an id that was never written. Prior-epoch records still refuse certify and re-prepare.

## Closure log and races

Each line is 163 bytes: id, SHA-256 of the exact `ClosedUnused` bytes, SHA-256 of the previous line (the first line names a seed bound to the incarnation). A duplicate, reorder, or foreign line fails the chain and the whole store is `StoreUnknown` before the per-id duplicate check. Recovery from a torn tail does not turn an id whose certificate was returned into an open read.

Prepare, certify, start, and attach take the same per-session `SemaphoreSlim` (`SessionRunnerRuntime._launchLocks`). Phone-home prepare and certify call those runtime methods. Certify writes `ClosedUnused` before `Ok`. Start records `Attempted` before custody or process effects, so a certify that runs after that sees `Attempted` and refuses. Adoption finishes before the runner reports ready. `RecordAdoptedAttempt` on an already-closed id latches and keeps the existing session. An unavailable store (`_absence` null) lets launch proceed and certify returns 503, which is today's unavailable behaviour and issues no certificate. No path here stops a Working session. CARD-0079 is untouched.

## Mutants

Single-condition edits, isolated `OutputPath`, `scripts/build-slot.ps1`, then `git checkout` restore. Worktree clean afterwards (`git status` empty; 38 `bin-c1153*` output directories removed).

| Mutant | Result |
|---|---|
| Power-loss `SyncDirectory` does not persist names (V-28) | 10/11 failed |
| M1-E record-directory sync removed (V-28) | 11/11 failed |
| M1-B anchor-directory sync removed (V-28) | 9/11 failed |
| M1-F root sync after closure-log creation removed (V-28) | 0/11 failed (equivalent: the header's later root sync persists the log name before the anchor) |
| M2-A backward-step check disabled (V-29) | 2/5 failed |
| M2-B high-water refusal removed (V-29) | 2/5 failed |
| M3-A unlogged `ClosedUnused` check removed (V-30) | 2/5 failed |
| M3-B record-hash compare removed (V-30) | 1/5 failed |
| M3-D `TryAdd` duplicate refusal replaced by overwrite (V-30) | 0/5 failed |
| M3-E closure-log length check removed (V-31) | 1/2 failed |
| Certify treats `Attempted` as certifiable (28 whitelist cases) | 1/28 failed |
| Validator skips `identityClosed` (35 whitelist cases) | 1/35 failed |

M3-D is a missing-control candidate, as PC-32 already says. A raw duplicate breaks the previous-hash first, so the store is `StoreUnknown`. A chain-consistent second line for the same id is what `TryAdd` alone rejects. Overwriting still leaves the id closed when the record matches the last hash, or `ClosureMismatch` when it matches an earlier hash. That does not read as absence. H-24 already covers an attacker who rewrites both the log and the record.

No assertion was weakened or deleted in the repair-2 diff beyond the fixture clock and store-path defaults the evidence note describes. `scripts/check-evidence-diff.ps1` on `1fecea48..cb466647` (5 commits) and on `e987af29..cb466647` (20 commits): 0 violations. `git diff --check` clean. Grep of the repair diff and of run `20261008-070954-2ed8` found no HMAC, signature, or key material. Route logs carry a key id or a disable problem. The latch log carries the reason string. Phone-home mutation logs carry launch, kill-generation, and release outcomes.

## Merge and platform

`origin/master` is `27e3e3f7f4b46fa26e49c15e8b59436faf714553`. `git merge-tree --write-tree` exit 1, result tree `7103128d5a0db4f29cfa11f2f00ed4eb58304b37`, eight add/add conflicts: the plan, the test design, and the six absence-evidence test skeletons (master holds rebased copies). A trial rebase in a discarded scratch worktree skipped `999de4d8b`, `bb77118e9`, and `061e29c34` as already applied and applied the remaining 17 commits cleanly (trial head `cafc265e0`). This branch was left at `cb4666476`. The brief's "16 candidate commits" counted the tip before the evidence commit; the evidence commit is the 17th and applied cleanly.

`GET /api/runner-defaults` 200, revision 2, provenance Human, kinds Grok, ClaudeCode, Codex. `GET /api/session-runners` 200, three entries, no host named here: windows capacity 2 occupied 0 accepting; linux capacity 10 occupied 0 draining and not accepting; linux capacity 10 occupied 10 accepting. No `-Runner` or `-Platform` pin.

CARD-1153 is InProgress, Normal, rank 10. CARD-1149 is InProgress, High, rank 7. Both still describe the absent-launch hold this runner certificate is for.

## Disclosures for a Backlog card

- M1-F is equivalent. M3-D is an unguarded line the hash chain already covers for the tested damage.
- Windows directory-entry durability is the API the code calls, is fail-closed when that call fails, and was not executed (CP-35, CP-36).
- The plan's first checkpoint table was not rewritten. Landing and later runs need the test-design rows (and CP-101..CP-121), not that table.
- Activation: runner first, then server. A runner that still has a round-1 closure log refuses creation of new ids until an operator moves that store aside. Server and runner clocks must agree within 30s or every evidence request is refused and the dispatcher keeps Failed.
- H-14's memory-only mark residual, and a sub-500ms rollback that leaves the mark above the current clock plus 30s (stricter refusal).
- PC-1..PC-33 pending.
