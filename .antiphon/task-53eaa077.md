# CARD-1082 S1 Final Review (task 53eaa077)

Outcome: clean. No defect found. ordinaryScopeCompleted: Full.

Subject: Code owner `50e9b2c3-4fae-4252-a9ac-0f4b4ef661b6`, `refs/heads/feat/card-task-50e9b2c3` at
`b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9`, one commit over master `33dfd65edfe7d8ba596c37663ef31e3ba0dfeec5`
(10 files, +319/-10). Reviewed in the runner mirror `/work/worktrees/task-53eaa077` at the same SHA, tree clean.
Plan: `docs/superpowers/plans/2026-10-06-card-1082-settlement-sync-debt-plan.md` (D-1..D-8, S1).

## Rerun: one checkpoint-tool run, one build, serial

Run `20261006-183452-a94a` from a review-only `### Checkpoints` table kept in the scratchpad,
`start --plan <table> --serial --expected-source-sha b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9`.
Verdict GREEN exit=0. `unlisted: none`. 141 results, 0 failed, 0 skipped. Wall 12m23s, one build (132 s),
every row `slot=granted`, `source=b5dcf888... sourceState=clean buildSource=verified`. The tool deleted
`bin-c1082-rv/`; the driver build (`scripts/build-slot.ps1`, lease `9948e1c8-2199-461b-96ad-7aae6120de99`,
held 3 s) was deleted with a root-confined `rm`. Evidence: `.antiphon/checkpoints/20261006-183452-a94a/report.md`.

```
CHECKPOINT CP-1 commit=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 build=ok filter=/*/*/(SettlementSyncDebtPolicyTests*)|(DelegationLeaseSettingsTests*)/* executed=31 passed=31 failed=0 skipped=0 trx=/work/worktrees/task-53eaa077/.antiphon/checkpoints/20261006-183452-a94a/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 build=reused filter=/*/*/(ReviewEvidenceResettlementTests*)|(BlockedTaskSyncRecoveryTests*)|(BlockedTaskParkDeliveryTests*)/* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-53eaa077/.antiphon/checkpoints/20261006-183452-a94a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 build=reused filter=/*/*/RunnerTaskSettlementTests/* executed=26 passed=26 failed=0 skipped=0 trx=/work/worktrees/task-53eaa077/.antiphon/checkpoints/20261006-183452-a94a/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 build=reused filter=/*/*/(ReviewEvidenceRecoveryTests*)|(ReviewEvidenceRecoveryEndpointTests*)/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-53eaa077/.antiphon/checkpoints/20261006-183452-a94a/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 build=reused filter=/*/*/TerminalRunnerSeatReleaseTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-53eaa077/.antiphon/checkpoints/20261006-183452-a94a/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-53eaa077/.antiphon/checkpoints/20261006-183452-a94a/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9 sourceState=clean buildSource=verified
```

Row map (review table ids, not the plan's): CP-1 = plan CP-1 (policy-settings, 31 = 17 policy + 14 settings).
CP-2 = the plan's CP-8 filter (legacy evidence/park controls R-3, R-4: 9 + 2 + 4). CP-3 = whole
`RunnerTaskSettlementTests` (26; holds the three CARD-0657 lease tests, R-2; touched via `RunnerSettlementWorld`).
CP-4 = `ReviewEvidenceRecoveryTests` + `ReviewEvidenceRecoveryEndpointTests` (21 + 6; `RunnerSettlementWorld` via
`ReviewRecoveryWorld`). CP-5 = whole `TerminalRunnerSeatReleaseTests` (39; `RunnerSeatReleaseFixture` consumer, R-6).
CP-6 = registry guard (3). Not run, by the brief: the whole Unit lane, the After=all rows CP-13..CP-15, S2+ rows.

Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef 33dfd65e... -HeadRef b5dcf888...`: commits=1 entries=0
violations=0, exit 0.

## Code report audit (CP-n lines against the plan's Checkpoints table)

- The Code report's CP-1 line matches the plan row exactly (filter, exact 31, build=ok, slot=granted, source clean,
  buildSource=verified) and this rerun reproduces 31/31 from a fresh build.
- Its unlisted runs (the three legacy classes, the three CARD-0657 lease tests method-scoped, the registry guard)
  carry a stated reason (the plan's "each slice runs the existing classes named in its row" and the brief's scope)
  and ran through build-slot leases named in the report; the stopped `--after S1` run is disclosed. No driver
  outside the slot gate. No missing row, no zero count.
- The new tests can go red: `ShouldBeSameAs` on the unchanged instance, `Confirmed` false with a desktop SHA
  present, exact warning text built from constants, validator messages. No self-compare, no constant assertion.
- "The four guards failed 4 of 31 before restore" is the Code stage's own control exercise. PC-1..PC-16 stay pending
  for method-scoped SourceLanding Mutation; S1 contributes detectors for PC-1, PC-3 and PC-4.

## Hard checks

(a) Unwired. In `server/`, `SettlementSyncDebtPolicy`, `RunnerSyncDebtOnSettlement`, `RunnerSyncDebtAttentionMinutes`
and `SourceDescends` are referenced only by the policy file, `DelegationSettings` and tests. No production call site
of `Classify`, `BlockReason`, `PendingWarning` or `WorkspaceNote`. Master behaviour is unchanged by landing.

(b) Fail-closed. `Eligible` = switch on AND `State == Unavailable` AND `Reason == runner_sync_lease_busy` AND
`GitObjectId.IsFull(RemoteSha)` (40 or 64 lowercase hex). Null, short or uppercase SHA; `LeaseWaiting`, `Timeout`,
`FetchUnavailable`, `DependencyUnavailable`, `InspectionUnavailable`; `Refused`, `NotApplicable`, `Synchronized`,
`NoPushedProgress`; and a disabled switch all return the same instance. Today only the acquire lease-busy outcome
in `RemoteWorkspaceService.SyncOwnedCheckoutAsync` carries `RemoteSha = s` (the observed tip); the observation
lease-busy outcome carries none. Disclosure, not a defect: `FullRef` is not pinned to the task's owned ref (CARD-1113).

(c) Pending is never Confirmed. `Confirmed` stays `Synchronized or NoPushedProgress` with `DesktopAfterSha`.
Consumers: `ReviewEvidenceBindingService.PrepareRepairAsync` refuses (`review_evidence_sync_unconfirmed`);
`ReviewEvidenceRecoveryService` refuses stored Pending (`stored_sync_unconfirmed`); `MergeBackAsync` returns
"left for review" for `Confirmed != true`; `RemoteSyncBlockReason` blocks any unconfirmed result with its reason;
`ResolveDeliverableAsync` and `TryDescribeGitAsync` yield nothing / "base unknown" when unconfirmed;
`TaskCompletionProgressService.EvaluateRemoteAsync` is Indeterminate for any state other than Synchronized;
`BlockedTaskSyncRecoveryService` cannot see Pending because `RemoteWorkspaceService` never returns it;
`LandApproval`, `AgentTaskLandingProtocol` and `AttentionService` do not read `RemoteSync`. Enum values are explicit
and append-only (`Pending = 5`); no switch, cast or ordering on the enum in `server/`; it is persisted only inside
`CompletionProgressEvidenceJson` through `TaskProgressJson` (`JsonStringEnumConverter`, by name) and exposed through
the API with string enums (`Program.cs`); no database column, migration, PowerShell or client/TypeScript mirror;
`delegate.ps1` prints the state generically. `SourceDescends` is a trailing optional positional parameter, existing
constructions are unchanged, and `RemoteSettlementSyncResult` itself is never serialized.

(d) Kill switch default true; attention minutes default 30 with a validator floor of 1 (tested 0, 1, 30). The
`syncDebt` knob exists only in test fixtures. `BridgeQueueHarness` resolves `Delegation ?? new DelegationSettings()`,
so the D-8 opt-out is exactly the default settings minus the switch: no settings drift for the legacy controls.

(e) No assertion changed in `ReviewEvidenceResettlementTests`, `BlockedTaskParkDeliveryTests` or
`BlockedTaskSyncRecoveryTests`; only the fixture argument and a one-line comment.

## Disclosures (no defect; Backlog card filed)

CARD-1113 (Backlog, Antiphon board): pin `FullRef` inside `Eligible`; `BlockReason(Pending, Code, evidence: null)`
returns null (parity with today's Confirmed shape, so S5 must pass the freshly evaluated evidence, never null);
`PendingWarning` and `WorkspaceNote` do not assert `State == Pending`, so S5 call sites must guard on Pending.

Tooling note: `scripts/card.ps1 new` on the Linux mirror created the card and then exited 1 at line 523
(`Join-Path` on a Windows `repositoryPath` after `card files NOT WRITTEN: board_not_opted_in`).

## Platform read

`GET /api/runner-defaults`: globalRunnerId server2. `GET /api/session-runners`: desktop windows 0/2 delegated tasks;
server2 draining; server2-temp linux 5/10 sessions. Build slots 2/4 occupied when this review started. The next
stage needs no `-Runner` or `-Platform`.

--- review evidence ---
subjectTaskId: 50e9b2c3-4fae-4252-a9ac-0f4b4ef661b6
reviewedSourceSha: b5dcf888921b0b65605a4bf1ed1d0c23c3da2ba9
reviewedSourceClean: true
ordinaryScopeCompleted: Full
