# CARD-1658 Review round 1 (task 39a11efd)

Subject: feat/card-task-24291915 tip `cac23c108fc1baa8ad3f6006ac57a31950006ee4` (source = fix `964afbc7098a9d6d4e500ff9b6a004668e63c38a`; the tip only adds the Code report). Base `a00962fd6a055c513fa567c3a930fb875660dbef`. Runner mirror, Linux (server2).

Verdict: **no defects**. No row the old passes kept is changed or deleted; the audit cutoff is captured once per sweep; a protection-load failure ends the pass and is never treated as an empty list; every pass terminates. Four advisories below.

## Data-loss checks (code reading against `git show a00962fd6:...`)

- **Predicates unchanged.** Usage `ObservedAt < cutoff`; residue `(Outcome == Held || Preview) && EvaluatedAt < cutoff`; audit `CreatedAt < cutoff && FullContent != null`. These are byte-identical to the old statements. Each cutoff is computed once, outside the batch lambda: `UtcNow()` for usage and residue, `AuditService.ArchiveCutoff` = `DateTime.UtcNow.AddDays(-RetentionDays)` for audit, as before. Only `OrderBy(Id)` (audit: `CreatedAt, Id`) plus `Take(n)` were added, so the ids are picked by the full predicate inside the same single statement. The ordering is deterministic and complete (unique Id tiebreak). The audit write still sets only `FullContent = null`; no other column or condition changed. The `DELETE /api/audit/archive` endpoint still uses the unchanged one-statement `ArchiveFullContentAsync`. No code updates residue-candidate or usage-sample rows in place (inserts only), and audit `FullContent` is nulled only by the archive. A concurrent change cannot move a row out of the predicate between the id pick and the write.
- **Audit cutoff fixed per sweep.** I proved it with a probe (uncommitted, deleted): 7 old records, batch 2, and an interceptor that records the `DateTime` parameter of every `UPDATE "AuditRecords"`. After batch 1 it waits 400 ms, then inserts a record with `CreatedAt = cutoff + 100 ms`.
  - At the fix, the probe is green: 4 batches, 1 distinct cutoff, and the late record keeps its `FullContent`.
  - Mutant M6 recomputes the cutoff per batch. The probe goes red at the distinct-cutoff assertion (line 51).
- **A-1 load failure.** `LoadOutboundTaskIdsAsync` runs per attempt, outside the inner `try`. An exception from it (a timeout, now under the 60 s batch timeout, or a malformed descriptor) propagates out of `PruneTaskBatchesAsync`. The pass reports Failed, and no batch runs with an empty or stale list. A task becoming unreferenced only makes it eligible later, which is not a hazard.
- **A-2 skip.** The attempt loop is bounded at 3 (`TaskBatchAttempts`). Each attempt reloads protection and reruns eligibility. A failed attempt rolls back its transaction (`await using tx`) and clears the tracker. On the third failure the skip is logged with the type chain only, and the cursor advances (`_taskCursor.After = batch[^1]`). The outer loop is still capped by `TaskPruneMaxBatchesPerSweep`, so it cannot spin. The skipped range is revisited when the scan wraps (cursor reset to null at scan end); this is documented in the XML doc and the Code report. The last skip exception is rethrown, so Tasks reports Failed.
- **A-3 bounds.** `PruneRowBatchesAsync` runs `while (Batches < max)` and increments on every iteration, so it terminates. It sets the command timeout on the shared scoped `AppDbContext`, which `AuditService` also uses (both `AddScoped`), and restores it in `finally`. A failure propagates to `RunPassAsync`, which isolates it (CARD-1654 behaviour kept) and reports progress for the batches already committed. The defaults (1000 rows / 60 s / 50 batches = 50,000 rows per sweep) cover the 14,508 residue rows in one sweep.

## Runs (all through `scripts/build-slot.ps1`; native Debug output; tested source = 964afbc70)

| Run | Build (slot) | Result |
|---|---|---|
| Fix + probe | waited 75 s, held 142 s | 104/104 passed, 0 failed, 0 skipped (TRX per class: DataRetentionServiceTests 93, WorktreeResidueCandidateRowTests 3, ChannelOutboundRetentionTests 6, AuditArchiveEndpointTests 1, probe 1); 2m32s |
| Mutant A (M1 outbound load hoisted, M2 attempts=1, M3/M4/M5 Take removed: usage, residue, AuditService batch) | waited 30 s, held 170 s | `DataRetentionServiceTests/C165*` 8: all 5 C1658 red on their intended assertions (protected roots[3] deleted; Thrown 1 != 3; Rows=5 statement ×3); the 3 C1654 tests green |
| Mutant B (M6 audit cutoff per batch) | waited 0 s, held 237 s | probe red (distinct cutoffs); C1658_AuditArchiveBacklog green, so that witness does not pin cutoff fixity (see A-2) |

One run launched with a wrong TFM path (net10.0) failed in 1 s without running anything and was relaunched. One extra build: `tools/Antiphon.LandGraphAudit` Release (slot held 3 s), needed for the land graph evaluator.

## Gates

- Land graph `-Check` (native `-ToolPath`): approved, count=69, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE (matches; no -Update).
- `check-evidence-diff -BaseRef a00962fd6 -HeadRef cac23c108`: commits=3 entries=1 violations=0.
- merge-tree: clean onto origin/master `4bd1473f0`. Onto rc/2026-11-01 `10a826b9d` it is textually clean, but rc lacks `DataRetentionTaskCursor` (CARD-1654), so an rc port waits for CARD-1654's three commits (held).

## Advisories

- A-1 **Worst-case Tasks pass time grew.** Before, the first failing batch ended the pass in about 60 s. Now, if eligibility times out for every batch (the CARD-1654 desktop failure mode), each of 50 batches costs 3 × (load + 60 s), so a sweep can take about 2.5 h or more before the later passes run. The outbound-id load (which deserializes every protected capture) can also run up to 150× per sweep. Suggest a per-sweep failure budget, for example ending the pass after two consecutive skipped ranges.
- A-2 **Cutoff fixity has no committed witness.** No committed witness pins "audit cutoff captured once" (M6 survives the suite). Adopt the probe above as a C1658 witness or PC.
- A-3 **Audit batch scan cost.** Audit batches use `IX_AuditRecords_CreatedAt` and rescan the already-archived (`FullContent` null) prefix older than the cutoff. During a backlog, cost grows with each batch. Watch `BatchMilliseconds` after deployment.
- A-4 **Failed batches are not timed.** A failed row-pass batch and a load failure record no `BatchMilliseconds` entry (minor).

## Scope note

The brief's named selection and the affected retention callers (ChannelOutboundRetention, AuditArchiveEndpoint) ran. The whole Unit lane did not run: it is about 70 min on loaded server2, over this round's 75-minute budget, and the operator's standing rule is minimal testing. No checkpoint-tool receipts exist (there is no CARD-1658 plan or `### Checkpoints` table). So `ordinaryScopeCompleted: Interim` and `reviewedSourceClean: false`. PCs (PC-1..PC-5 from the Code report, plus a cutoff-fixity PC if adopted) remain pending for the SourceLanding Mutation.
