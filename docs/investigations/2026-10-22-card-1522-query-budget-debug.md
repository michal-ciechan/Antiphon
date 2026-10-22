# CARD-1522 query-budget diagnosis

2026-10-22; Debug task `c6dd91f2`; source inspection only.

All seven failures are stale statement-count contracts. Recommend **raise budget** for each, with an explicit acceptance/rationale amendment. The deltas are one newly enabled harness sweep, one required credential write, and one required composer-safety read. No N+1 or redundant-read product regression was found in these deltas, so no product-regression Backlog card was filed. CARD-1522 remains the budget-repair owner; this report does not repair or waive the failing assertions.

The RC comparison indicates **five of these seven cases remain red** on `origin/rc/2026-10-20`, pinned at `0714a9a89e4bff65aae7e0b4b94098694aeb931c`; the two composer cases have their original 60-command path there. This is a source-derived conclusion, not a new RC execution result. No build/test was needed or run, and the live Windows qualification was untouched.

## Evidence and scope

- Original executable source: `ffa98a8f76588c12df6b0437d24b541762b95f09`, from [CARD-1514's profile](2026-10-22-card-1514-slow-test-profile.md) and [per-test observations](2026-10-22-card-1514-per-test-observations.md). Those reports were read from fetched master at `c54c0eea50daa7bdd9ccc1da8ff2bc655def1ea2` because they postdate this task's checkout.
- Original class run: 2026-10-22 22:23:19.844Z–22:24:28.242Z, **381 executed, 374 passed, seven failed, zero skipped**. All seven reached the count assertion; they were not connection, timeout, fixture or build failures. The other CARD-1514 failure is CARD-1512 and outside this diagnosis.
- Raw evidence root: `/work/test-evidence/card-1514-85ca6a8c/DelegationDispatchRecoveryBoundaryTests/`. The console SQL roster truncates each command to 180 characters, so source/control-flow inspection, rather than the displayed SELECT prefix alone, identifies ambiguous rows.
- Assigned base: `c5da1d2387c08adf86e301a3874a5cbcfd4d6a98`. The budget partials, shared harness, dispatcher and queue service inspected here have no differences from the profiled source. Only this report is changed.
- Despite the shorthand “query budget,” [the assertion](../../tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.cs#L75) is `total.ShouldBe(expected)`, an **exact command count**, not a maximum SELECT count. [FullCommandCounter](../../tests/Antiphon.Tests/TestHelpers/FullCommandCounter.cs) counts readers, nonqueries and scalars once per command, including writes and advisory locks. Design ceilings in the old plan do not override the exact assertions.

These existing files were SHA-256 checked against the committed observations; all matched:

| File under raw evidence root | SHA-256 |
|---|---|
| `run.trx` | `cac2b329d636593571586f2ce2422f02ec93dafc96116b8f0dd811a79ce1e4ec` |
| `console.log` | `c32812e4b2821b6cd18ad8214343c6b21017938cd5ca5a8bfa81f4bf56aa94ec` |
| `run.json` | `57b3af6f0ed79d08a2532318a9e8ef50115a883ec5146b74aa63e4deb2dbdb84` |
| `diag/log_261022222323909.diag` | `cf4e22b3e968780d5e303070327330233fc078d75ed403040037f03148909fed` |

## Per-test verdicts

Every row is a parameterized instance of the exact method `Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets(string action, int expected)`. Names below retain the original argument/budget. A/B/C identify the full commits and causal paths in the next section. “Observed” is CARD-1514; RC counts are inferred by reading, not measured here.

| Exact method instance (class/namespace above) | Asserted | Observed | Cause and added command | RC `0714a9a89` | Verdict / proposed current count | Evidence (`console.log`; `run.trx`) |
|---|---:|---:|---|---|---|---|
| `C1149_C1150_Statement_budgets(held-dispatched-tick, 18)` | 18 | 19 | A: routing-blocked sweep SELECT, roster #15 | Red, 19 vs 18 | **Raise budget to 19**; describe expanded DI graph | 45; 165 |
| `C1149_C1150_Statement_budgets(working-live-tick, 18)` | 18 | 19 | A: same single sweep SELECT, #15 | Red, 19 vs 18 | **Raise budget to 19**; no per-task added reads | 86; 211 |
| `C1149_C1150_Statement_budgets(first-dead-session-observation, 18)` | 18 | 19 | A: same single sweep SELECT, #15 | Red, 19 vs 18 | **Raise budget to 19**; first-observation grace still adds no recovery SQL | 4; 119 |
| `C1149_C1150_Statement_budgets(initial-refused-dispatch, 42)` | 42 | 44 | A: sweep SELECT #12; B: `AgentTasks.TokenHash` UPDATE #34 | Red, 44 vs 42 | **Raise budget to 44**; count the credential persistence before the refusing launch sink | 127; 314 |
| `C1149_C1150_Statement_budgets(repeated-blocked-tick, 16)` | 16 | 17 | A: routing-blocked sweep SELECT, #13 | Red, 17 vs 16 | **Raise budget to 17**; no new absent-launch recovery or repeated transition | 193; 389 |
| `C1149_C1150_Statement_budgets(watchdog-first-recovery, 60)` | 60 | 61 | C: canceled typed queue-row SELECT, #38 | No, source path remains 60 vs 60 | **Raise budget to 61** on source containing C; preserve composer protection | 232; 433 |
| `C1149_C1150_Statement_budgets(running-recovery-missing-brief, 60)` | 60 | 61 | C: same pre-send SELECT, #38 | No, source path remains 60 vs 60 | **Raise budget to 61** on source containing C; preserve composer protection | 315; 521 |

## Causal attribution

### A — CARD-0097 changed the harness graph, not the product sweep

Commit `49d7884d9adcf74f90050777c653f27a65915d39` (2026-10-14), `test(card-0097): add S-F matrix instruction and receipt regressions; verification pending`, added scoped `ComplexityRoutingService` and `ComplexityChainService` registrations to [BridgeQueueHarness.cs:185](../../tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs#L185). `git log -S 'ComplexityRoutingService'` and the commit diff identify that addition.

The dispatcher already called [ResumeRoutingBlockedAsync](../../server/Application/Services/AgentTaskDispatcher.cs#L1758) once per full tick. Its first guard returns without SQL when `_complexityRouting` is null. With the new registration it executes one SELECT for Blocked tasks with a complexity/pin and a `routing exhausted: ` reason. Each budget fixture has **zero matching routing-blocked tasks**, so it skips the grouped event query and all per-task work. This is precisely one additional command, regardless of the held/working/dead/blocked fixture's task count.

That SELECT is an intentional production recovery operation, originally introduced by CARD-0090 in `2c246c0aaab4f2f2f533fca23095a8cdf24b16f6`. The count increase here was activated by CARD-0097's test-service registration. It is not evidence that production gained a new query on October 14. Keep the richer shared harness and document its one sweep in these budgets; removing a useful production recovery read to restore the old fixture count would be the wrong repair.

### B — CARD-1285 adds a required write before a fresh launch

Commit `b602eac16a150d2acfe05d472aff500524168195` (2026-10-15), `fix(card-1285): recover and persist task token before fresh launch`, added the missing/stale-token repair in [FinishDispatchedLaunchSpecAsync](../../server/Application/Services/AgentTaskDispatcher.cs#L6375): after constructing the launch spec and before handing it to the launch sink, generate a replacement credential if necessary and save its hash.

[MeasureRefusedDispatchAsync](../../tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.Budgets.cs#L78) inserts a Queued task directly, without going through the creation path that provides the transient raw token. Thus the repair runs and the counter records the separate `UPDATE "AgentTasks" SET "TokenHash" ...` at roster #34, before the initial brief is queued and the launch sink refuses. This required write accounts for the second increment, **42 + A's SELECT + B's UPDATE = 44**. It protects restarted queues and fresh replacement launches from unauthenticated workers; it is neither a redundant read nor an N+1 loop.

### C — CARD-0266 checks for canceled input still in the composer

Commit `96ee5df43209cba8d991ac5e8d11c2d65f90b6d1` (2026-10-22), `fix(CARD-0266): S2 hold input over canceled typed queue rows`, introduced [CanceledTypingBlocksTheComposerAsync](../../server/Application/Services/SessionMessageQueueService.cs#L2090) and its call in the flush at [line 2403](../../server/Application/Services/SessionMessageQueueService.cs#L2403). `git log -S 'CanceledTypingBlocksTheComposerAsync'` identifies the commit.

After the ordinary pending/deliverable and generation checks, it reads canceled rows of this session that were attempted, have no SentAt, and belong to this or an uncertain legacy generation. Both failing recovery fixtures have none, so the method returns immediately after one SELECT; it does not run its per-canceled-row transcript checks. The SELECT is roster #38, between the execution-task status query (#37) and expectation-body query (#39). Both recoveries then send and verify the newly inserted brief, giving **60 + 1 = 61**.

The read asks a different question from the pending-row queries. Canceling a queue row does not erase bytes already typed into the terminal. Sending the next brief without this check can combine two bodies in one prompt. The [session runtime invariant](../session-runtime-invariants.md#L818) expressly requires proof of release through a later UserPrompt, an empty composer, or a new generation. The additional read is therefore legitimate. These no-canceled-row fixtures demonstrate a constant extra query, not an N+1 regression. Optimizing populated canceled-row scans would require separate evidence and is not a finding from these failures.

### Reads that are not responsible

The released-answer SELECT at roster #1 predates the budgets: CARD-0667 commit `6420ae9aeecccc0fe42c2fcd353b017e6ee3ea38` (2026-10-05), [dispatcher:5285](../../server/Application/Services/AgentTaskDispatcher.cs#L5285). The retained-capacity EXISTS at the end of the empty-queue ticks also predates them: `3b4a311b49ff8ae81399b17c1e8652bc03e80dc8` (2026-09-28), [dispatcher:514](../../server/Application/Services/AgentTaskDispatcher.cs#L514). Both are already present in the measured-budget commit `4bb18ce018156b4fc2fc242641e057e74e0236f6` (2026-10-09). Their visibility in a failing roster does not make them new overhead.

The CARD-1490 alias extraction changes the pinned-agent projection from ModelId/ModelLevel to ModelId, retaining one SELECT. The recent worktree/evidence changes suggested in the brief are not the causes of these deltas: these full-tick/dispatch fixtures use Shared workspaces and the recovery fixtures repair an existing session. Attribution above follows the actual executed paths and the shared harness history.

## Why the RC answer does not need another run

`git fetch origin master rc/2026-10-20` resolved the RC to the requested full SHA. `git merge-base --is-ancestor` establishes that A and B are present, and C is absent. Source comparison establishes the behavior, beyond ancestry alone:

- The budget method, `.Budgets.cs`, `.Watchdog.cs`, and `BridgeQueueHarness.cs` are byte-identical between RC and the profiled source. Their blob IDs respectively are `153028ffd86c2648ebe4930ebc58390574d420d5`, `5675dd9fb769991f90c0bc14d0bab9f17b07a150`, `53d792d485b4451b1a2b5256bee7900e9344d4df`, and `4378a8a9eefd167236fd1b08bcf6574af5d2bda7`.
- RC has the routing-service registration and the same empty-result sweep. It also has the token-repair SaveChanges before the launch sink. Neither is reverted or hidden by an RC-specific budget.
- RC lacks the canceled-typing helper/calls. The other queue diff adds call-site diagnostic tags without adding commands. CARD-1403 changes the single-session boot-reply EXISTS to the shared batch implementation; for these one-session calls that remains one command.
- The relevant recovery methods in AgentSessionService and the reconciliation service do not change their command paths. Runtime transcript-ingest/re-arm changes are outside the measured fake-prompt path: [WireDispatchPrompt](../../tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.Watchdog.cs#L552) directly saves the UserPrompt/TurnEnd with an uncounted context, and the harness does not start runtime-action workers.

Thus the five full-tick cases retain 19/19/19/44/17 on RC; the two missing-brief recovery paths lose precisely C's SELECT and retain 60. No RC binaries were built, no source was checked out over this branch, and no claimed green run is implied by this static comparison.

## Recommended slice order

1. **Amend the acceptance rationale explicitly.** CARD-1522's Plan/Code owner should record A/B/C and the exact new totals before editing assertions. The [CARD-1149/1150 plan D-6](../superpowers/plans/2026-10-07-card-1149-1150-dispatch-recovery-plan.md#L282) expressly pins 18/18/4; its [test-design measured totals and PC-18 explanation](../superpowers/plans/2026-10-07-card-1149-1150-test-design.md#L259) also need a dated amendment for the changed fixture. Preserve historical measurements and previous verification outcomes. Keep the 4-command grace scan and all unrelated budgets unchanged.
2. **Repair the five RC-common budget rows.** Set the three 18s to 19, initial refusal 42 to 44, and blocked tick 16 to 17, documenting the routing sweep and the required token write beside their fixtures. These changes belong on both lines if the caller commissions RC integration; this Debug task makes no RC mutation.
3. **Repair the two composer rows only where CARD-0266 is present.** Set missing-brief and watchdog-first recovery from 60 to 61 with the canceled-composer rationale. Do not transplant those values alone to RC, where exact equality would then fail in the other direction. Leave `running-recovery-existing-brief` at its observed passing 65.
4. **Verify the finished budget repair once with the existing exact method selection.** The 14 argument instances, including the seven unaffected controls, are sufficient ordinary scope for this assertion-only repair. Use the owner's checkpoint/build-slot process and `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets`; retain per-action counts and the full SQL roster. Keep exact equality and the original semantic/no-kill assertions. If any resulting count differs, inspect that delta instead of widening again. No whole-class, assembly or live Windows qualification rerun is requested by this diagnosis.

No product repair is recommended for the seven deltas. Required future verification remains pending until that separate repair is implemented and run; this report is not Clean/Full implementation evidence.

--- next stage ---
next: plan
handoff: Amend the exact statement-budget rationale for CARD-0097 harness routing, CARD-1285 credential persistence, and CARD-0266 composer safety; then commission the narrow budget-only Code repair. RC needs the first five changes only unless CARD-0266 is also integrated. Preserve historical evidence and the live qualification.
artifact: docs/investigations/2026-10-22-card-1522-query-budget-debug.md
