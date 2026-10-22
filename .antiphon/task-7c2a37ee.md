# CARD-1446 milestone 2 (S4a-S4d) Final Review, task 7c2a37ee: clean

Subject: Code task `36786bbc-5de7-4d0f-b9d7-9bb0ad8114f2`, `feat/card-task-36786bbc` at
`d940a3af6d73e65b9d3b20db3b4c9f9c970c2640` (the report-only commit on top of the tested source `88fa931aa`; `git diff
88fa931aa d940a3af6` touches only `.antiphon/task-36786bbc.md`). Base `3fa8d7bd4`. Read-only: the subject branch was not touched.

## Receipt (checkpoint tool, server2, no -Runner/-Platform)

Run `20261022-074514-2e50` at `d940a3af6`, `--plan docs/superpowers/plans/2026-10-22-card-1446-stale-landings-plan-m2-checkpoints.md
--rows CP-8,CP-10,CP-12,CP-13,RV-1`: **verdict GREEN exit=0**, `unlisted: none`, outputs deleted.

```
CHECKPOINT CP-8 commit=d940a3af6d73e65b9d3b20db3b4c9f9c970c2640 build=ok filter=/*/*/FileSystemEvidenceRetentionTests/* executed=4 passed=4 failed=0 skipped=0 slot=granted dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=d940a3af6d73e65b9d3b20db3b4c9f9c970c2640 build=ok filter=/*/*/WorktreeIgnoredContentClassifierTests/* executed=50 passed=50 failed=0 skipped=0 slot=granted dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=d940a3af6d73e65b9d3b20db3b4c9f9c970c2640 build=ok filter=/*/*/(WorktreeResiduePublicationLaneTests*)|(WorktreeLandingCleanupRetryTests*)/C1446* executed=10 passed=10 failed=0 skipped=0 slot=granted waited=210s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT RV-1 commit=d940a3af6d73e65b9d3b20db3b4c9f9c970c2640 build=reused filter=/*/*/LandRefRetentionTests/C1432_protection_rule_rows executed=34 passed=34 failed=0 skipped=0 slot=granted waited=15s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=d940a3af6d73e65b9d3b20db3b4c9f9c970c2640 build=reused filter=/*/*/(WorktreeLandingCleanupRetryTests*)|(WorktreeResidueSweepTests*)|(WorktreeResidueRecoveryTests*)/* executed=56 passed=56 failed=0 skipped=0 slot=granted waited=300s dirty=0 sourceState=clean buildSource=verified
```

`pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261022-074514-2e50/report.json
-ExpectedSourceSha d940a3af6d73e65b9d3b20db3b4c9f9c970c2640 -Rows CP-8,CP-10,CP-12,CP-13,RV-1` ->
`CHECKPOINT SOURCE VALID source=d940a3af6d73e65b9d3b20db3b4c9f9c970c2640 rows=5` (exit 0). Counts equal Code's run
`20261022-070830-713f` (4/50/10/34/56). Wall 37 min (host load ~35; CP-13 10 min of tests, WorktreeResidueRecoveryTests 471 s).

Other build/test drivers, all under `scripts/build-slot.ps1`: the checkpoint tool build (`bin-c1446rv-tool/`, deleted); one
mutation build + one mutation test run in a discarded scratch worktree (below). Red rows CP-7/CP-9/CP-11 were not re-run (they
are witnesses at `a8e337a02`); their stated reasons in Code's report match the test assertions, and the mutations below confirm
the green tests can go red. CP-14..CP-16 belong to S5 (deferred, not in this milestone).

## Throwaway mutations (scratch detached worktree at 88fa931aa, discarded)

One build, two independent mutations in different files, filter
`/*/*/(WorktreeResiduePublicationLaneTests*)|(FileSystemEvidenceRetentionTests*)/(C1446_policy_rows*)|(C1446_copies_and_verifies*)`:
total 8, failed 2, succeeded 6, skipped 0.

| Mutation | Red case | Assertion |
|---|---|---|
| `PublicationLanePolicy.Decide` `queuedCount >= MaxCleanupRetries` -> `>` | `C1446_policy_rows(three-queued-abandons, ...)` | `Decide(...) should be Abandon` |
| `FileSystemEvidenceRetention` post-copy verify `!string.Equals` -> `string.Equals` | `C1446_copies_and_verifies` | `result.Reason should be null but was` (verify_failed) |

The other 6 policy rows stayed green (expected: the cap mutation only moves the boundary).

## Landability

- `git fetch origin master` -> `bb43ec3529b9d8ad2a799f81080b9e34d89fa9c9`; `git merge-tree --write-tree origin/master d940a3af6` exit 0.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 3fa8d7bd4 -HeadRef d940a3af6` -> `commits=9 entries=1 violations=0`.
- rc (`origin/rc/2026-10-20` = `ceb6e3d6b`, scratch worktree, discarded): cherry-pick in order 60a2994ec, 9ecac5ead (S4a),
  6f5cc369e, 0a7b70e5f (S4b), 88fa931aa (S4d), a8e337a02 (amendment) all clean; S4c-test b1be44b2d conflicts
  (`WorktreeResidueSettings.cs`, `WorktreeResidueSettingsValidator.cs`, `LandRefRetentionTests.cs`), and
  `AntiphonRefProtection.cs` is absent on rc (CARD-1432 not there). Not built on rc.

## Checked, no defect

1. **Evidence retention.** Budget (files, bytes) decided before any copy; `CreateNew` copy hashes while writing, then re-hashes
   the destination; a mismatch deletes only the bad copy and refuses; an existing destination is never overwritten (identical bytes
   accepted, otherwise `evidence_retention_conflict`). Any exception propagates out of `RetainAsync` before `AuthorityAsync`,
   the second reading, move-aside and delete in `GuardedWorktreeRemoval`, so the tree is kept. Links are refused before retention
   (`ReparsePoints` at both readings), escaping/rooted paths are protected by the classifier and re-checked. Attempt-less removals
   share `Guid.Empty` and fall back to the conflict rule. The default root `<Git:WorktreeBasePath>/.retained` is not scanned as
   residue (`ScanResidueCandidatesAsync` only takes `card-task-*`). Copies are never deleted by code.
2. **Catch-all.** Order for a `.antiphon` path is protected -> explicit `.antiphon` evidence -> `.antiphon` disposable -> catch-all
   -> protected, fixed in code; non-`.antiphon` paths cannot match the catch-all. Protected names (`report.md`, `deliverables/**`,
   `.claude/**`, `appsettings.*.json`) still win; nothing protected becomes disposable; the only move is unlisted spill
   Protected -> Evidence (copied first). The four changed classifier rows are the plan's contract change, with a without-catch-all
   variant keeping the old one.
3. **Abandoned.** Only `AbandonCleanupAsync` sets it, under the task row lock, requiring the active op, `ActiveLandingId`,
   `HasPublication`, `Cleanup == Refused` and no pending request (a racing retry or new land refuses it). An explicit `-Land` on a
   published op routes to `CleanupAsync` (`AgentTaskLandingProtocol.cs:144`), so it is reversible; a refused retry keeps Abandoned,
   a clean one completes; the interrupted-after-publication path keeps it. Counters are durable Queued candidate rows, never reset.
   Ref protection: a refused cleanup sits at phase `CleanupStarted`, so `Cleanup != Abandoned` really unprotects it, but only for an
   op whose publication is confirmed (equivalent to Complete). Live tasks, live requests (including the `-Land` retry) and
   Pending/Held debts (milestone 1) still protect. No still-owed sync loses a ref.
4. **Lane.** `WorktreeResidue:Execute` is false by default (`WorktreeResidueSettings.cs:26`, `appsettings.json:215`); queue, cursor
   stamp and abandon all sit under `execute && !preview && actions < budget`. Cooldown uses no action and cannot loop; abandon uses
   one action. No DB check constraint on `Cleanup`, and V-9 writes `Abandoned` through Postgres.
5. **Tests.** CP-11's 7 rows are accounted for in the amendment (4 V-8 behaviours + 3 rollback switches; Expect "7 executed, at
   least 2 failed"). The `(A*)|(B*)/M*` rewrites are the known TUnit grammar workaround; CP-13 is a superset. RV-1 is plan D-3's
   conditional row and was listed with its reason.

## Disclosures (not defects under the verdict policy)

- D1: The `evidence_retention_verify_failed` branch has no test. Deleting the post-copy verify would survive every test (the
  inversion is caught only because it refuses good copies). Also, a crash or cancellation mid-copy leaves a partial destination,
  and every later pass then refuses `evidence_retention_conflict` until an operator deletes it. That fails closed (tree kept).
- D2: The second reading compares the evidence path set, not its bytes. A write to an evidence file after the copy is lost with
  the tree. This needs a live writer in a tree that passed the authority checks.
- D3: `RetainedEvidenceRoot` is not validated: it could be configured inside a worktree. The derived default uses raw
  `Git:WorktreeBasePath` (a relative value resolves against the process CWD, not `WorktreeRoots`' `AppContext.BaseDirectory`).
- D4: A secret-like file under `.antiphon/` that matches no protected name is now copied to the retained root and deleted from the
  tree instead of keeping the tree. The exposure moves; nothing is lost.
- D5: The sweep wiring (`WorktreeResidueSweepService` Queued-count query, `landing:<opId>` cursor stamp, `Held/cooldown`,
  abandon dispatch) has no producer-to-recipient test; the policy and `AbandonCleanupAsync` are tested separately. A crash between
  the retry request commit and the run save loses the stamp and the Queued row, which means one extra retry. Superseded or
  canceled queued retries still count toward the cap. Both are reversible and keep the tree.
- D6: Abandoned is visible as a `LandingCleanup` event, a warning log and the candidate row `Refused/cleanup_abandoned`; not traced
  to an attention-feed item. The client unions `client/src/api/agentTasks.ts:251,375` lack `'Abandoned'` (no runtime consumer).
- D7: rc: S4d's `docs/orchestration-loop.md` text describes S4c (cooldown, cap, Abandoned), so a partial rc port of
  S4a+S4b+S4d documents behaviour rc will not have until CARD-1432 + S4c follow.

Backlog suggestion (one): a sweep-level C1446 test that drives two Execute runs inside the cooldown (second is `Held/cooldown`)
and a fourth refused run that abandons, plus a verify-failure seam in `FileSystemEvidenceRetention` (D1, D5).

PCs (Code report "Mutation targets", S4 part) stay pending for SourceLanding Mutation; my two mutations do not discharge them.
