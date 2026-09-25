# CARD-0696 Code evidence

Server2 Linux, Codex Astra. Final verification profile v1. Plan:
`docs/superpowers/plans/2026-09-25-card-0696-phone-home-outage-followups-plan.md`.
All positive controls remain pending for method-scoped SourceLanding Mutation.
No production outage, deployment, native CLI qualification or board transition was performed.

## Implementation

- Accepted mentions use the ordinary queue insert with `Mention = 7`, an occurrence GUID as
  row identity, normalized-body digest collision validation, and inline delivery disabled until
  commit. Same-occurrence replay (including concurrent insertion) preserves delivery state;
  identical new occurrences remain separate rows/turns. Activity records acceptance, and the
  stranded sweep recovers non-AlwaysOn mentions without re-resolving their target.
- Immediate admission uses current binding/connection eligibility. Mode Now creates no row
  on a safe 503 refusal; send-now restores every scalar from the pre-call row snapshot in the
  pre-body race; durable immediate enqueue deletes only its newly inserted unsent row.
  Once a body may have left, later unavailability retains typed uncertainty and attempt evidence.
- Pending runner membership is a singleton-owned five-second snapshot with single-flight
  refresh, cached empties, failure throttling and authoritative recovery precedence. Known-row
  gates retain new active targets during a warm negative; terminal/unaccepted bindings stay down.
  The indexed working-state SQL from CARD-0698 was left intact. CARD-0701 can absorb this
  membership helper without inheriting a transcript/working-state cache.
- Owner guidance was updated in `docs/session-runtime-invariants.md` and `docs/ops-http.md`.

At the start, fetched origin/master matched `f801109b56d810a6c4b867fc611bfe2561fd85ae`; CARD-0698 was Review with its query/index
changes already present, and CARD-0701 remained Backlog. Execution used the caller's standing
server2/Codex authority without requesting another approval.

## Red-first evidence

Tests were committed separately at `38c4abe4cb236f34e7dbedeae7719d77b4e07c9c`.
CP-1 produced all five intended assertion failures: zero durable mention rows in both outage
windows; HTTP 409/conflict instead of 503/phone_home_unavailable for both immediate routes;
132 bound-ID SELECTs instead of one for the first 132-call burst.
CP-2 changed only preview's production predicate to `ListLiveSessions().Contains(parsed)`.
The real preview DTO returned AgentLive=false and failed the intended assertion. The exact
original source bytes were restored in `finally`, touched, and checked with a clean diff before
implementation. Both red builds compiled; neither red result was a fixture or discovery failure.

```text
CHECKPOINT CP-2 commit=38c4abe4cb236f34e7dbedeae7719d77b4e07c9c build=ok filter=/*/*/PhoneHomeSchedulePreviewTests/Before_first_List_preview_remains_queueable_without_writes executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-58011e5e/.antiphon/c696-checkpoints/CP-2-20260925-113026-0733/run.trx
CHECKPOINT CP-1 commit=38c4abe4cb236f34e7dbedeae7719d77b4e07c9c build=ok filter=/*/*/(PhoneHomeOutageMentionTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomePendingInventoryTests*)/C696Red_* executed=5 passed=0 failed=5 skipped=0 trx=/work/worktrees/task-58011e5e/.antiphon/c696-checkpoints/CP-1-20260925-112733-d6d6/run.trx
```

## Final checkpoints

The first CP-3 build at `0de3885e1d4517c7c0862bafc88629c199e387db` passed all 30 new methods
(0 failed, 0 skipped). The required duration analysis found three >=5s immediate-send matrix
methods; the class was tagged Slow and registered with its measured reason. CP-3 was rerun
because that classification, a bounded SQL mention-target filter, and stronger recovery/uncertainty
coverage changed the source. That rerun at `fed2b652d374b877516e2eb900d937c8058bffaa`
passed 29/30: the new unbound fixture cleared RunnerId without the required store/cwd,
so PostgreSQL rejected setup under `CK_AgentSessions_RunnerBinding_AllOrNone`.
The next build at `f52a150d` failed compilation (zero tests) because the fixture used a
nullable string for the nullable GUID store. Both fixture errors were corrected. The same
30-method roster also now explicitly covers other-board mentions and blocked Herdr admission.
The following CP-3 run at `b7539dd4` passed 29/30: the added blocked control still used the
outage harness's pty fallback without SessionDeliveryProfile. That fixture was corrected to
register the real per-session profile and advertise the Herdr capability in an isolated local
graph, as the existing Herdr tests do. No production guard was changed for these fixture fixes.

Prior CP-3 attempts (including the compile-only failure):

```text
CHECKPOINT CP-3 commit=0de3885e1d4517c7c0862bafc88629c199e387db build=ok filter=/*/*/(PhoneHomeOutageMentionTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomePendingInventoryTests*)|(PhoneHomeSchedulePreviewTests*)/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-58011e5e/.antiphon/c696-checkpoints/CP-3-20260925-114811-5c47/run.trx
CHECKPOINT CP-3 commit=fed2b652d374b877516e2eb900d937c8058bffaa build=ok filter=/*/*/(PhoneHomeOutageMentionTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomePendingInventoryTests*)|(PhoneHomeSchedulePreviewTests*)/* executed=30 passed=29 failed=1 skipped=0 trx=/work/worktrees/task-58011e5e/.antiphon/c696-checkpoints/CP-3-20260925-115323-df15/run.trx
CHECKPOINT CP-3 build=failed project=tests/Antiphon.Tests outputPath=bin-c696-green/ exit=1
CHECKPOINT CP-3 commit=b7539dd4e8c515d36fe4dbc64dc65eb230cbe461 build=ok filter=/*/*/(PhoneHomeOutageMentionTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomePendingInventoryTests*)|(PhoneHomeSchedulePreviewTests*)/* executed=30 passed=29 failed=1 skipped=0 trx=/work/worktrees/task-58011e5e/.antiphon/c696-checkpoints/CP-3-20260925-120020-9674/run.trx
```

Final checkpoints (CP-3 reruns=4: three test executions and one compile-only failure):

```text
CHECKPOINT CP-3 commit=cb726b317bb5c8644bfa5af02fc27c3a3c3b8c4f build=ok filter=/*/*/(PhoneHomeOutageMentionTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomePendingInventoryTests*)|(PhoneHomeSchedulePreviewTests*)/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-58011e5e/.antiphon/c696-checkpoints/CP-3-20260925-120452-343e/run.trx reruns=4
CHECKPOINT CP-4 commit=cb726b317bb5c8644bfa5af02fc27c3a3c3b8c4f build=reused filter=/*/*/(AgentChannelServiceIntegrationTests*)|(PhoneHomeStrandedQueueTests*)|(PhoneHomeDirectoryTests*)|(SessionMessageQueuePhoneHomeDropTests*)|(ScheduleEndpointsTests*)|(ScheduleSweepTests*)/* executed=61 passed=61 failed=0 skipped=0 trx=/work/worktrees/task-58011e5e/.antiphon/c696-checkpoints/CP-4-20260925-120939-5ce1/run.trx
CHECKPOINT CP-5 commit=cb726b317bb5c8644bfa5af02fc27c3a3c3b8c4f build=reused filter=/*/*/*/*[Category=Unit] executed=3042 passed=3042 failed=0 skipped=28 trx=/work/worktrees/task-58011e5e/.antiphon/c696-checkpoints/CP-5-20260925-121106-f370/run.trx
```

CP-3's complete roster was 10 immediate + 8 mention + 8 inventory + 4 preview methods.
CP-4's roster was 6 channel + 16 stranded queue + 7 directory + 5 transport drop +
9 schedule endpoint + 18 schedule sweep methods. Final integration total: 91/91 passed,
zero skipped. Unit discovered 3,070: 3,042 passed, 28 skipped, zero failed/errors/timeouts.
The 28 existing runtime skips are 27 Windows-only cases (argv/path/executable/ConPTY)
and one Edge/Chrome PDF renderer case with no browser installed. No new test skips.
Both lane and Slow-registry classification guard tests passed.

The required read-only duration audits found zero unlisted >=5s tests in CP-3 or CP-4.
The Unit audit returned exit 1 for ten cases in four unchanged test classes; these are
recorded as duration observations, not assertion failures or baseline timing claims.
Their source/classification was left outside this card's scope:

```text
SLOW-TEST TRIPWIRE: 10 unlisted tests >= 5s
5.122s  Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests.A_trust_dialog_that_will_not_clear_fails_the_launch_with_a_named_block  A_trust_dialog_that_will_not_clear_fails_the_launch_with_a_named_block
7.400s  Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Disabling_the_probe_does_not_disable_effort_resolution  Disabling_the_probe_does_not_disable_effort_resolution(True)
7.427s  Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.An_effort_dialog_that_never_clears_fails_within_its_budget  An_effort_dialog_that_never_clears_fails_within_its_budget
7.738s  Antiphon.Tests.Application.HerdrSupervisionFailureEvidenceTests.Model_has_the_evidence_and_state_columns_and_the_migration_adds_only_them  Model_has_the_evidence_and_state_columns_and_the_migration_adds_only_them
7.315s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G065  C487_G065
9.211s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G066  C487_G066
6.513s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G067  C487_G067(inherits)
6.936s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G067  C487_G067(no-inherits)
9.768s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G069  C487_G069(ordinary)
9.573s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G069  C487_G069(optin)
```

## Reproduction and cleanup

Use the exact CP-3/CP-4/CP-5 filters, minimum counts, expected classes and output paths in
the plan's Checkpoints table. Build CP-3, then reuse it with -NoBuild for CP-4/CP-5.
All direct git calls used timeout 30s (60s for network); the checkpoint helper inherited
an execution-only PATH shim that bounded its internal git calls. Only listed checkpoint
filters were run; duration audits and TRX/roster inspection execute no tests.

Removed all 51 build-log-proven directories for bin-c696-red/, bin-c696-preview-red/ and
bin-c696-green/ across 17 projects; a final scan found zero remaining task output directories.
The exact producer-owned inventory remains at
/work/worktrees/task-58011e5e/.antiphon/c696-output-inventory.json,
and TRX/log evidence remains under /work/worktrees/task-58011e5e/.antiphon/.
No unrelated outputs or processes were removed. Final git diff --check passed.

Review the task branch's full diff against the recorded base. The final evidence commit is
documentation only; the final tested source commit is cb726b317bb5c8644bfa5af02fc27c3a3c3b8c4f.
SourceLanding Mutation owns every still-pending positive control; no deployment decision is
needed from Code.
