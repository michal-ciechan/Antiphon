# CARD-1149/1150 S2 repair 8 (Review df0d419a F13): true CP-75 composition, documentation only

Outcome: F13 fixed. Two false sentences now state the true CP-75 composition, and two more false claims about CP-75 were corrected in place. The diff touches no file under server/ or tests/.

- Task 3e7ac85d. Original Code task / landing owner: ea5ef98c-75dd-4918-9513-f13a66b1680d. Branch `feat/card-task-3e7ac85d`, worktree `/work/worktrees/task-3e7ac85d`, started at 5b65f1a7c85769959db6f80ad5b4f936f67c9f5c, not rebased.
- Fix commit: bfe45f1ac68b33a85762ed3409023f390faa467d. This report is committed after it and is docs only.
- Plan `docs/superpowers/plans/2026-10-07-card-1149-1150-dispatch-recovery-plan.md` (unchanged). Test design `docs/superpowers/plans/2026-10-07-card-1149-1150-test-design.md`.

## True CP-75 composition (`ResumeBookkeeping.cs`)

| Case | Kind | Delivery during the resume | Fault |
|---|---|---|---|
| `C1150_Resumed_backfill_event_save_failure_keeps_the_working_recipient` | post-delivery fault witness | ensure `Delivered` | "brief re-queued" save |
| `..._flips_one_step(requeued-event-save)` | post-delivery fault witness | ensure `Delivered` | "brief re-queued" save |
| `..._flips_one_step(flush-after-ensure-input)` | post-delivery fault witness | ensure `Delivered` | boot flush supervision read |
| `..._flips_one_step(resumed-event-save)` | post-delivery fault witness | ensure `Delivered` | "launch resumed" save |
| `..._flips_one_step(flush-input-then-resumed-event-save)` | post-delivery fault witness | boot flush `Delivered` (pre-persisted row) | "launch resumed" save |
| `..._flips_one_step(healthy)` | healthy control | ensure `Delivered` | none |
| `..._flips_one_step(pre-input-legacy-requeued-event-save)` | pre-input control (kill/Failed) | none: no dispatch time, so the ensure only enqueues | "brief re-queued" save |
| `..._flips_one_step(pre-input-resumed-event-save)` | pre-input control (kill/Failed) | none: brief Sent and turn ended before the restart | "launch resumed" save |

Five post-delivery fault witnesses, one healthy control and two pre-input controls. No case has a write that began and did not complete.

## Every place checked (file:line at bfe45f1a)

Changed:
- test design :1069-1086 (Repair 9 evidence, scope reduction). This was the F13 sentence. It now gives the 5/1/2 composition and names each case.
- test design :849-856 (Repair 7 evidence). "which the earlier S2 cases (CP-75) need" was false: the five fault cases return `Delivered` (true without a mark), and the pre-input controls must not be protected. Corrected in place.
- test design :1144-1148. `-31f67fe1` was added to the historical-reports list, with a note on its correction.
- `.antiphon/task-9a17d8c3.md`:15. This was the F13 sentence. It now gives the 5/1/2 composition.
- `.antiphon/task-31f67fe1.md`:38. "which CP-75 needs" was false for the same reason. Corrected in place.

Checked true and left unchanged:
- test design :242 (CP-75 row, 8), :746-766 (Repair 6 rows: standalone + 4 post-input + healthy + 2 pre-input), :770-773 (red 5 of 8; 3 controls passed), :776-790 (mutant table), :892, :981, :1064 (`Delivered` ... kept (F10)), :1114 (CP-75 unchanged at 8).
- `.antiphon/task-6da8a413.md`:35-45, 51, 57, 59, 80, 82. Each states the same true composition.
- `.antiphon/task-2fa55c42.md`:17, 66, 100; `.antiphon/task-9a17d8c3.md`:46, 76, 112; `.antiphon/task-31f67fe1.md`:18, 64, 106. These are counts or CHECKPOINT lines only.
- `.antiphon/task-44fc0e5a.md`, `-d30ad28b.md`, `-ea5ef98c.md` and the plan: no CP-75 or "all deliver" claim. Their only F10 mentions are not about CP-75 (`task-d30ad28b.md`:246 is an existing qualification).
- Code comments (true, not edited): `ResumeBookkeeping.cs`:17-19 and :58-59; `PreInputRefusal.cs`:26-30 ("no F10 case depends on keeping it"); `AgentSessionService.cs`:890-897 and :1006-1008; `SessionMessageQueueService.cs`:1666 and :1698-1702; `SessionMessageQueueService.DispatchBrief.cs`:15-18.

## Verification (ordinary scope for a docs-only diff)

- `git diff 5b65f1a7c..HEAD -- server/ tests/` is empty (0 bytes). `git diff cd6d0e671..HEAD -- server/ tests/ tools/` is also empty. The 46-row run at cd6d0e67 (921/921, repair 7) and Review df0d419a's rerun of it remain the behavioural evidence: the tree compiles identically.
- Doc-pin tests: none. Nothing under tests/ or tools/ reads the test design or the task reports.
- Registry guards, one isolated build (`bin-r10/`, UseAppHost=false, deleted afterwards: 28 directories), serial:

```
CHECKPOINT CP-54 commit=bfe45f1ac68b33a85762ed3409023f390faa467d build=ok filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-3e7ac85d/.antiphon/checkpoints/CP-54-20261008-221525-85ab/run.trx slot=granted waited=0s dirty=0 source=bfe45f1ac68b33a85762ed3409023f390faa467d sourceState=clean buildSource=verified
CHECKPOINT CP-54 EXIT CODE: 0
```

- The whole Unit lane was not run. The brief scopes it out for a docs-only diff, and AGENTS.md forbids whole-Unit runs here. No other build or test ran.
- `scripts/check-evidence-diff.ps1`: task range 5b65f1a7..bfe45f1a gave commits=1, violations=0. Card range 31632adc..bfe45f1a gave commits=30, violations=0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master cef08a5def1db497e43eb344ec16e8af6779a587) is clean: tree 6e2507e3b92d25e6cc6a93a13f7de8d5fed60fe0.
- PCs: every PC-F10/F11 and earlier PC is still pending for SourceLanding Mutation. None was discharged.
- Restart: none.
