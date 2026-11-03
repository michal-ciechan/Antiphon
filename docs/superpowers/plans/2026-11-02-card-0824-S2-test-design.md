# CARD-0824 S2 test design: delete the origin branch after confirmed publication

TestDesign task 6a12a89d, 2026-11-02, server2 (Linux), base `b6bb58e763c9a8cdc5c8ff86f31e344b92e36a93`
(the CARD-0824 plan, revision 2). Scope is **S2 only**: plan §3.10, §5 step 5, §6 S2 and its CP-12.
This companion adds verification detail to the
[per-slot worktree plan](2026-11-02-card-0824-per-slot-worktrees-plan.md). It does not rewrite the fix
design. Where the plan leaves a test-relevant choice open, this document binds a default that the Code
stage can test, and lists it under Open questions.

This is a docs-only deliverable. No production code, build or test ran. All tests, assertions, mutants
and minutes below are proposed and **estimated**. None is measured evidence. S2 ships after S1, S4 and
S5 (H-8), but its tests do not depend on S1 types: S2 touches landing, plus one new runner command.

## Verification design

### Inspection

Bodies read at the base before naming cases, with the boundaries they bind:

- The plan, all of it: §2 rows "Delete the branch" and "Recycle safety", §3.10, §4 (CARD-0669/0692
  rows), §5 step 5, §6 S2 (V-14..V-19, CP-12), §7 H-1, §8 risk 4, Performance budget, Checkpoints.
  -> V-14..V-19 and the new V-38..V-48.
- `server/Application/Services/AgentTaskLandingProtocol.cs` `CleanupAsync` (`:419-459`),
  `ExpectedBranch` (`:736`), `RecheckRemoteSourceAsync`/`RequireSourceSnapshot` (`:820-842`).
  -> trigger point; the lease value (B-1); the recheck guard G-20.
- `AgentTaskLandingState.cs` `HasPublication`, `Transition`, `ExpectedDeletion` (`:11-142`).
  -> trigger guard G-2; schema guard G-3.
- `LandOperationFactory.cs` (`:80-125`): schema 3 sets `OriginalSourceSha = ReviewedSourceSha =
  expected`, `SourceLocalSha`, `SourceRemoteSha/Ref/Fingerprint`. -> B-1, G-6.
- `Domain/Entities/AgentTaskLanding.cs`, `Domain/Enums/LandingEnums.cs`. -> the new row fields (B-4).
- `Application/Interfaces/ILandingGit.cs`, `Infrastructure/Git/LandingGit.cs` `PushSourceOwnedAsync`
  (`:1419-1433`, the existing `--force-with-lease` source push), `RecheckSourceRemoteAsync`
  (`:1236-1254`), `ChangesRegistrations` (`:524-545`). -> the delete primitive and its fail-closed
  default interface member (B-2), G-21.
- `Domain/Entities/AgentTask.cs`: `WorktreeBaseBranch`, `WorktreeBaseTaskId`,
  `RequestedWorktreeBaseTaskId`, `RepairSourceTaskId`, `WorktreeBranch`, `RunnerId`,
  `RemoteWorktreePath`. -> the dependency terms G-10..G-15 and the runner guard G-16.
- `RemoteWorkspaceService.RemoveMirrorAsync` (`:1007-1030`): the capability-gated phone-home pattern
  (`RunnerCapabilityFeatures`, `PhoneHomeRunnerClient`). -> runner delivery (B-3), G-19.
- `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs` `MirrorAsync` (`fetch origin <b>`,
  `worktree add -B <b>`, `:161-176`, `:300-308`), `RemoveAsync` (`:535-583`; removes the worktree but
  **not** `refs/heads/<b>`), `PublishAsync` (`:585-660`). `PhoneHomeCommandDispatcher` operation
  switch (`:340-420`). -> which runner refs exist after a legacy mirror is retired; V-40.
- Fixtures: `tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs` (real land, isolated schema,
  settable `Clock`, `Fault`, `GitOverride`, `ConfigureServices`, `RestartServicesAsync`),
  `LandingGitFixture.cs` (real `canonical` repo, bare `remote.git`, independent `observer` clone,
  `FixtureGit.BeforeCommand/AfterCommand/Commands`, `PushIndependentAsync`, `AssertRemoteSourceAsync`),
  `AgentTaskLandRemoteSourceRetentionATests.cs` (refusal families through `Card0452LandingCases`),
  `WorktreeResiduePublicationLaneTests.cs` (pure policy rows plus one harness case; the shape copied
  for CP-12), `PhoneHomeMirrorJoinTests.cs` `JoinWorld` (real phone-home socket, runner mutation lock,
  FIFO hold, crash retry, `FakeTimeProvider`), `Scripts/VolumeRetirementFixture.cs` and
  `RollingVolumeRecycleScriptTests.cs` `C1008HostFixture` (runs the real CARD-1008 audit; `Preview`,
  `AuditText`, `SeedPublishedOuter`), `VolumeRetirementContentTests.Every_ref_recovery_tip_...`
  (the gone-ref refusal shape), `RunnerWorkspaceBindingTests.cs` (runner scratch repos). -> every
  CP row below.
- `docs/docker-stack.md` CARD-1008 audit paragraph: "a tracking ref kept for a branch deleted on
  origin still refuses", and the receipt counters `gone-tracking` and `unadvertised-local`. -> V-43.
- `docs/testing-and-build.md` Checkpoint manifest, `docs/test-performance-standards.md`, AGENTS.md
  "Tests and builds". -> Checkpoints, Performance budget.

Not read in full, so not relied on: the CARD-0692 branch (Review), the Hangfire job registration code
beyond `WorktreeResidueSettings`/`WorktreeResidueJob`, and the server attention-feed writer.

### Bindings this design tests (from the plan and the code)

- **B-1 Lease value.** Schema 3 never moves the task branch (`ExpectedBranch`). The origin branch
  therefore still holds the source SHA that the land observed and rechecked, `op.SourceRemoteSha`,
  which equals `ReviewedSourceSha` and `OriginalSourceSha`. `VerifiedSourceSha` is the **rebased**
  commit, which the branch never held unless the base was unchanged. The plan's "lease on the landed
  SHA" is therefore bound to `--force-with-lease=<SourceFullRef>:<SourceRemoteSha>` (Q-1). V-14 is
  built so that a lease on `VerifiedSourceSha` goes red.
- **B-2 Primitive.** A new default interface member,
  `ILandingGit.DeleteSourceBranchAsync(repository, sourceFullRef, expectedRemoteSha,
  expectedFingerprint, ct)`, returns `delete_unsupported` by default, as `PushSourceOwnedAsync` does,
  so existing fakes fail closed. `LandingGit` runs
  `push <endpoint> --force-with-lease=<ref>:<sha> :<ref>` after the same fingerprint check.
- **B-3 Order of one deletion** (new `PublishedBranchDeletionService.RunAsync`, a recurring Hangfire
  job, Q-6):
  1. decide with the pure policy;
  2. recheck the remote source (`RecheckSourceRemoteAsync`). A different SHA is `Kept(branch_moved)`
     with no push; a missing ref is `already_absent`;
  3. if the task ran on a runner: the runner deletes its local `refs/heads/<b>` and
     `refs/remotes/origin/<b>` (new phone-home op `WorkspaceBranchRefsDelete`, capability
     `WorkspaceBranchRefsV1`). Any refusal or failure is `Deferred`, and origin is untouched;
  4. journal `BranchDeletionState=Deleting` (the intent) and save;
  5. push the delete with the lease;
  6. re-read with `ls-remote`: absent means `Deleted`;
  7. delete the desktop `refs/remotes/origin/<b>` with `update-ref -d <ref> <old>`. Keep
     `refs/heads/<b>` while a worktree has it checked out.
  The runner pass comes **before** origin (Q-7). Deleting a ref that is still published loses nothing
  and can be restored by a fetch. Deleting origin first would leave the volume with unpublished
  tracking refs, so the recycle would refuse.
- **B-4 Recording.** New `AgentTaskLanding` fields (additive migration): `BranchDeletionState`
  (`None, Deferred, Deleting, Deleted, Kept`), `BranchDeletionAt`, `BranchDeletionDetail` (≤ 400 chars),
  `BranchDeletionReason`, `RunnerRefsDeletedAt`. One `AgentTaskEvent` (new type `LandingBranchDeletion`)
  is written per state or reason change, never once per sweep.
- **B-5 Settings** (`LandingSettings` or the existing landing options; Code picks the owner class):
  - `DeleteSourceBranchAfterPublication=false`;
  - `SourceBranchDeleteDelayMinutes=1440`;
  - new `SourceBranchDeletionAfterUtc=null` (backlog cutoff: null means no operation is eligible, Q-3);
  - new `MaxSourceBranchDeletionsPerRun=25`;
  - new `SourceBranchDeletionRunnerIds=[]` (volumes S4 has drained; Q-5).

### Delivery inventory

S2 adds two async paths. Neither carries session input, so no UserPrompt transcript applies.

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt |
|---|---|---|---|---|---|
| D-1 delayed origin delete | `CleanupAsync` reaching `PublicationConfirmed` (the due time is derived from `RemoteConfirmedAt`) | origin `refs/heads/<b>` | landing row: `BranchDeletionState=Deleting` saved **before** the push (G-22) | next sweep after a restart: re-reads `Deleting`, rechecks the remote: absent -> `Deleted`; same SHA -> push again with the lease; other SHA -> `Kept(branch_moved)` | an independent `observer` clone's `ls-remote` shows the ref absent, **and** the row is `Deleted` |
| D-2 runner ref cleanup | `PublishedBranchDeletionService` step 3 | runner repo `/work/repos/antiphon` refs, via phone-home `WorkspaceBranchRefsDelete` | landing row `RunnerRefsDeletedAt` saved after the runner's response | next sweep: the runner op is idempotent (an absent ref is success); an offline, busy or refusing runner -> `Deferred`, origin untouched | the runner response lists `deleted[]`/`absent[]`, **and** an independent `git show-ref` in the runner repo finds no `<b>` ref |

The durable identity that joins producer and recipient is `(landing operation id, SourceFullRef,
SourceRemoteSha)`. The runner request carries the branch and the expected SHA, and the runner checks
both.

The producer-to-recipient test is V-44 (`C0824BranchRefsDeliveryTests`, CP-18). It runs over the real
phone-home socket, the runner mutation lock and a scratch runner repository, built from `JoinWorld`. It
covers:
- an idle eligible runner;
- a busy runner, its mutation lock held by the FIFO hold: no origin delete before release;
- a disconnected runner (send failure): `Deferred`;
- a crash after the runner deleted its refs but before the row save, followed by a restart: idempotent,
  and origin is deleted exactly once;
- a crash after the origin push but before the save: the restart records `Deleted` and pushes no second
  delete.

Substitutes and what they cannot prove:
- CP-12 uses a fake `IRunnerBranchRefs` seam (new interface; its production implementation wraps
  `PhoneHomeRunnerClient`). It proves ordering and deferral, not the transport.
- CP-16 drives `RunnerWorkspaceService` directly. It proves the git effects, not the transport.
- Only V-44 proves recipient evidence.
- The fixture origin is a local bare repository. GitHub branch protection, credential helpers and real
  latency are not proven (see Out of scope and Native).

A request, a phone-home frame or an `ok` response is not a receipt. Only the absent refs read
independently in the runner repo and in the observer clone count.

### Fixtures and seams

- **Real land:** `LandingSafetyHarness`, with `Clock = new FakeTimeProvider(<fixed UTC>)` set before
  `InitializeAsync()`. A land that is a real rebase comes from `h.AddSourceAsync()`, then
  `h.Fixture.PushIndependentAsync("stranger")` (master moves, so `VerifiedSourceSha != SourceRemoteSha`),
  then `h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef)`, then
  `h.RunAsync()`.
- **The deletion service:** registered in the harness through `ConfigureServices` (new scoped
  `PublishedBranchDeletionService`, options via `Options.Create(...)`). Each test calls
  `RunAsync(ct)` directly. There is no Hangfire and no real wait: the delay is crossed with
  `FakeTimeProvider.Advance`.
- **Remote reads:** an independent `LandingGitFixture.FixtureGit` reader on `h.Fixture.Remote` and the
  `Observer` clone, never the production instance (the `AssertRemoteSourceAsync` pattern).
- **Fault seams:**
  - `h.Fault.Phase = LandPhase.PublicationConfirmed` leaves an operation unconfirmed;
  - a new `SaveFault.Matches` on `BranchDeletionState == Deleting/Deleted` cuts the sweep's saves;
  - `FixtureGit.BeforeCommand` injects the "branch moved at push" race. Before the delete push runs,
    an observer pushes a new commit to the source ref.
- **Runner seam:** a new `IRunnerBranchRefs`. The test fake (`RecordingRunnerBranchRefs`, new, in the
  test file) records calls with a global sequence number shared with a `FixtureGit.Commands` index, so
  that order is assertable. Its outcomes are `Deleted`, `Refused(code)`, `Throws(IOException)` and
  `CapabilityMissing`.
- **Runner repository (CP-16):** a real scratch repository: bare origin, a `repo` clone with
  `refs/heads/feat/card-task-<8hex>` and `refs/remotes/origin/feat/card-task-<8hex>`, and an optional
  legacy mirror `worktrees/task-<8hex>` made with `worktree add -B` (the production shape). It follows
  the `RunnerWorkspaceBindingTests` scratch layout, and the root is deleted in `finally` by its exact
  literal path.
- **Recycle audit (CP-17):** `C1008HostFixture(main: false)` plus `VolumeRetirementFixture.Preview`.
  The runner repository is seeded under `WorkMount(fixture)/repos/<name>` with a bare origin, as
  `SeedPublishedOuter` does, then a branch commit that master does **not** contain (the rebase-land
  shape). The class needs `NativeLinuxGate.Require` and a `tests/linux-test-roster.json` entry.
- **Limiters:** git-only Antiphon.Tests classes take `[ParallelLimiter<GitProcessSpawnLimit>]`. The
  C1008 class (bash and fake docker) and the phone-home class (FIFO, `sh`) take `ProcessSpawnLimit`.
  SessionRunner.Tests has no `GitProcessSpawnLimit`, so CP-16 takes `ProcessSpawnLimit`.

### Proves it works now

Each row: behaviour | layer | test | expected. The bracket names the one-line mutant that must turn it
red, and "red today" says why the test fails against a stub (the first red commit's stubs are listed in
Implementation steps).

- **V-14** A confirmed-published rebase land's source branch is deleted with the lease on
  `SourceRemoteSha` once the delay has passed. The pins survive.
  - Layer: server, real git. Test: `C0824PublishedBranchDeletionTests.C0824_confirmed_land_deletes_with_lease_after_delay`.
  - Arrange: the real rebase land above, flag on, cutoff before now, delay 1440.
  - Act: advance the clock to `RemoteConfirmedAt + 1440 min`; `RunAsync`.
  - Assert:
    - the observer's `ls-remote --exit-code <remote> <SourceRef>` exits 2 (`remote-absent`);
    - exactly one push command with argv
      `["push", <endpoint>, "--force-with-lease=<SourceRef>:<SourceRemoteSha>", ":<SourceRef>"]`
      (`lease-argv`);
    - the row is `Deleted` with `BranchDeletionAt == now` (`row-deleted`);
    - `refs/antiphon/land/<task>/<op>/*` pins are unchanged (`pins-kept`, V-48);
    - the canonical repo has no `refs/remotes/origin/<b>` (`tracking-gone`, V-45);
    - exactly one `LandingBranchDeletion` event (`one-event`, V-46).
  - Red today: the stub service deletes nothing, so `remote-absent` fails.
  - Mutant: lease on `VerifiedSourceSha`. The lease is rejected and the row becomes `Kept(branch_moved)`,
    so `remote-absent` fails.
- **V-15** A refused or unconfirmed land keeps the branch.
  - Test: `C0824_refused_or_unconfirmed_land_keeps_branch` with `[Arguments]`:
    - `target-hook-reject` (`Card0452LandingCases.InstallRejectingTargetHookAsync`, Refused);
    - `divergent-remote` (the Retention V06 setup);
    - `unconfirmed-cut` (`Fault.Phase = PublicationConfirmed`, so the operation stays `PushStarted`
      without `RemoteConfirmedAt`).
  - Act: flag on, delay 0, `RunAsync` twice.
  - Assert: `AssertRemoteSourceAsync()` passes, no delete push is in `Commands` (`no-delete-push`),
    and the row stays `None`.
  - Red today: this is a safety witness. A no-op stub is trivially safe, so its red comes from PC-2.
  - Mutant: trigger on `Phase != Refused` instead of `HasPublication`.
- **V-16a** A branch that moved before the sweep is kept without a push.
  - Test: `C0824_branch_moved_before_sweep_is_kept_without_push`.
  - Arrange: after the land, the observer pushes a new commit to `SourceRef`.
  - Assert:
    - the remote still has the new SHA (`remote-kept`);
    - the row is `Kept`, reason `branch_moved`;
    - `Commands` contains no `push` after the land (`no-push`).
  - Mutant: skip the recheck. The lease still refuses, but `no-push` goes red.
- **V-16b** A branch that moves between the recheck and the push is refused by the lease.
  - Test: `C0824_branch_moved_at_push_is_refused_by_lease`.
  - Arrange: `BeforeCommand` on the first `push ... :<SourceRef>` pushes a new commit through the
    observer, then lets the command run.
  - Assert: the remote keeps the new SHA (`lease-held`), and the row is `Kept(branch_moved)`.
  - Mutant: plain `:<ref>` with no lease.
- **V-17** An open dependent keeps (defers) the branch.
  - Test: `C0824_dependent_rows` (isolated schema, no git). Each row seeds a published operation plus
    one dependent task, then calls the new query `PublishedBranchDeletionDependents.FindAsync`.
  - Rows:
    - `worktree-base-branch`;
    - `worktree-base-task`;
    - `requested-base-task-queued`;
    - `repair-source-open`;
    - `same-branch-follow-up`;
    - `pending-land-request-other-task`;
    - a liveness control, `terminal-dependent-does-not-block` (expects none).
  - Assert: exactly the expected dependent id, or none for the control.
  - Mutant: drop one OR term (PC-10..PC-15).
- **V-18** The delay is respected.
  - Covered inside V-14's method as the named assertion `before-due-no-push`. At `due - 1 s` a sweep
    runs no push and the row stays `None`; then the clock advances to `due`.
  - Mutant: compare against `CreatedAt`, or use a zero delay.
- **V-19** Flag off is a no-op.
  - Test: `C0824_flag_off_runs_no_git_and_writes_nothing`.
  - Arrange: the confirmed land, flag default.
  - Assert: `Commands` is unchanged across `RunAsync` (`no-git`); the row's `BranchDeletion*` fields
    are default (`no-write`); `AssertRemoteSourceAsync`-style tip equality holds.
  - Mutant: default the flag to true.
- **V-38** Protected and non-delegate branch names are kept.
  - Policy rows in `C0824_policy_rows`: `protected-master`, `protected-rc`, `non-delegate-name`.
    Each expects `Kept(protected_branch)`.
  - Mutant: drop the `DelegateBranchName.TryParse` guard.
- **V-39** Recovery or adoption lands and non-schema-3 operations are kept.
  - Policy rows: `recovery-adopt` -> `Kept(recovery_land)`; `schema-2` -> `Kept(legacy_schema)`;
    `source-sha-mismatch` -> `Kept(source_identity_mismatch)`.
- **V-40** The runner deletes its local refs safely.
  - Layer: runner, real git. `C0824RunnerBranchRefsTests`, CP-16 (method list in Checkpoints).
  - Assert:
    - both refs are gone at, or behind, the expected SHA;
    - absent refs are success;
    - a checked-out branch refuses `branch_checked_out` and keeps the refs;
    - a local tip ahead of the expected SHA refuses `unpublished_local_ref`;
    - non-delegate names refuse;
    - a ref moved between read and delete refuses (old-value check);
    - a missing expected object refuses `expected_object_missing`, never "absent".
- **V-41** The runner step precedes origin, and every runner failure defers with origin untouched.
  - Test: `C0824_runner_refusal_or_absence_defers_before_origin` with `[Arguments]`:
    `checked-out`, `unpublished-local`, `offline` (throws `IOException`), `capability-missing`,
    `runner-not-enabled` (runner id absent from `SourceBranchDeletionRunnerIds`).
  - Assert: the origin tip is unchanged (`origin-untouched`) and the row is `Deferred` with the code.
  - Separate test: `C0824_runner_refs_precede_origin_delete`: the fake's call sequence is lower than the
    push's command index (`runner-first`).
- **V-42** Crash recovery.
  - Test `C0824_crash_after_push_before_save_recovers_once`:
    - Arrange: `SaveFault` throws on the save that sets `Deleted`.
    - Act: `RestartServicesAsync`, then `RunAsync`.
    - Assert: the row is `Deleted`, there is exactly one delete push in total, and one event.
  - Test `C0824_crash_after_runner_refs_recovers_idempotently`: the fault is on the
    `RunnerRefsDeletedAt` save. After the restart the runner fake is called again, returns `absent`,
    and origin is deleted once.
  - Mutant: no `Deleting` intent. After the crash, a restart that skips `Deleting` rows leaves the row
    stuck in `None`/`Deferred` and pushes a second delete or none.
- **V-43** Interaction with the CARD-1008 recycle audit (linux-native, CP-17), class
  `C0824BranchDeletionRecycleAuditTests`:
  - `C0824_legacy_mirror_blocks_runner_refs_and_origin_kept`: a legacy mirror with the branch checked
    out. `RunnerWorkspaceService.DeleteBranchRefsAsync` refuses `branch_checked_out`, origin is not
    touched, and `Preview` exits 0 (everything is still published).
  - `C0824_after_runner_refs_and_origin_delete_audit_passes`: the legacy mirror is removed through
    `RemoveAsync` (the S4 path), then `DeleteBranchRefsAsync`, then the leased origin delete.
    `Preview` exits 0 with the Ready marker.
  - `C0824_origin_delete_without_runner_refs_audit_refuses` (the control that proves the oracle can go
    red): the same sequence but without `DeleteBranchRefsAsync`. `Preview` exits 2;
    `AuditText` contains `RecycleUnpublishedWork` and `gone-tracking` or `unadvertised-local`.
- **V-44** Delivery over the real phone-home socket (linux-native, CP-18), class
  `C0824BranchRefsDeliveryTests`:
  - `C0824_idle_runner_deletes_refs_then_origin`;
  - `C0824_busy_runner_defers_origin_until_released`;
  - `C0824_disconnected_runner_defers_origin`;
  - `C0824_crash_between_handoffs_recovers_once`.
  - Receipts: `git show-ref` in the runner scratch repository plus the observer's `ls-remote`.
- **V-45** The desktop tracking ref is removed only after the remote delete is confirmed. Named
  assertion `tracking-gone` in V-14; in V-16b the tracking ref is still present (`tracking-kept`).
  `refs/heads/<b>` is kept while it is checked out in `trees/source` (`local-branch-kept`, asserted in
  V-14 before retirement).
- **V-46** Recording: one event per state or reason change.
  - Test: `C0824_deferred_reason_evented_once`. Three sweeps with an open dependent give one
    `Deferred` event (`deferred-once`). When the dependent closes, the next sweep after the due time
    writes one `Deleted` event.
  - Mutant: write an event on every sweep.
- **V-47** Backlog cutoff and per-run bound.
  - Test: `C0824_backlog_cutoff_and_run_bound`. Seed 30 synthetic schema-3 published rows that pass
    `HasPublication` (a `PublishedLandingRow` builder, new, in the test file) after the cutoff, plus 2
    rows before it. A fake `ILandingGit` returns the recheck SHA and records deletes.
  - Assert:
    - the first `RunAsync` gives 25 deletes (`bound-25`);
    - the second gives 5;
    - the two pre-cutoff rows are never touched (`cutoff-held`);
    - a null cutoff deletes 0 (`null-cutoff-none`).
- **V-48** A reverted land stays recoverable: the `pins-kept` assertion in V-14. The recovery pins'
  source ref still resolves to `SourceLocalSha` after the deletion.

### Guards the regression

- **R-1** Flag off (the default) never deletes a remote source on landing, refusal or recovery.
  Existing `AgentTaskLandRemoteSourceRetention{A,B,C,D}Tests.C448_V21_RefusalNeverDeletesRemoteSource`
  (32 cases), decisive assertion `AssertRemoteSourceAsync` (CP-15).
- **R-2** Landing publication and cleanup are unchanged by the scheduling fields and the new step.
  Existing `AgentTaskLandPublicationTests` (CP-19); the decisive assertions are each test's own
  `Phase`/`Cleanup`/`BranchRemoved`.
- **R-3** The runner mirror binding and the phone-home workspace ops are unchanged by the new operation
  and capability. Existing `RunnerWorkspaceBindingTests` (CP-20).

### Guard inventory

Every safety-critical S2 guard, split wherever two checks can be bypassed independently:

- G-1 §6 S2 flag `DeleteSourceBranchAfterPublication` defaults false | PC-1
- G-2 §3.10 trigger: `AgentTaskLandingState.HasPublication(op)` | PC-2
- G-3 B-1 schema 3 only | PC-3
- G-4 §3.10 recovery or adoption lands kept (`RecoveryMode == None`) | PC-4
- G-5 server protected or non-delegate name refused | PC-5
- G-6 B-1 `SourceRemoteSha == ReviewedSourceSha` identity | PC-6
- G-7 §3.10 delay from `RemoteConfirmedAt` | PC-7
- G-8 B-5 backlog cutoff (null means none) | PC-8
- G-9 B-5 per-run bound | PC-9
- G-10 dependent term `WorktreeBaseBranch` | PC-10
- G-11 dependent term `WorktreeBaseTaskId` | PC-11
- G-12 dependent term `RequestedWorktreeBaseTaskId` (queued) | PC-12
- G-13 dependent term open `RepairSourceTaskId` (CARD-0753) | PC-13
- G-14 dependent term non-terminal task on the same `WorktreeBranch` | PC-14
- G-15 dependent term pending land request for another task on the branch | PC-15
- G-16 §5 step 5: the runner volume is enabled (`SourceBranchDeletionRunnerIds`) | PC-16
- G-17 B-3: a runner refusal defers before origin | PC-17
- G-18 B-3: a runner that is unreachable or throws defers before origin (a separate catch path) | PC-18
- G-19 B-3: the runner capability gate `WorkspaceBranchRefsV1` | PC-19
- G-20 B-3 step 2: remote recheck before the push | PC-20
- G-21 B-2 `--force-with-lease` on the delete push | PC-21
- G-22 D-1: the `Deleting` intent is saved before the push | PC-22
- G-23 B-3 step 7: the desktop tracking ref is deleted only after confirmed absence | PC-23
- G-24 B-3 step 7: the desktop `refs/heads/<b>` is untouched while checked out | PC-24
- G-25 runner: refuses a checked-out branch | PC-25
- G-26 runner: refuses an unpublished local tip (ancestry check) | PC-26
- G-27 runner: deletes with the old value (`update-ref -d <ref> <old>`) | PC-27
- G-28 runner: delegate-name guard | PC-28
- G-29 runner: a missing expected object refuses (unknown is not absent) | PC-29
- G-30 V-48: S2 never touches `refs/antiphon/land/**` pins | PC-30

### Positive controls

Each control: break the guard with a compiling one-line defect; the named method must go red at the
named assertion; then restore and confirm green.

- **PC-1** Default the flag to `true` -> `C0824_flag_off_runs_no_git_and_writes_nothing`: `no-git`.
- **PC-2** Replace `HasPublication(op)` with `op.Phase != LandPhase.Refused` ->
  `C0824_refused_or_unconfirmed_land_keeps_branch(unconfirmed-cut)`: `no-delete-push`.
- **PC-3** Drop `SchemaVersion == 3` -> `C0824_policy_rows(schema-2)`: decision equals
  `Kept(legacy_schema)`.
- **PC-4** Drop the `RecoveryMode` check -> `C0824_policy_rows(recovery-adopt)`.
- **PC-5** Drop the name guard -> `C0824_policy_rows(protected-rc)`.
- **PC-6** Drop the identity equality -> `C0824_policy_rows(source-sha-mismatch)`.
- **PC-7** Set the due time to `RemoteConfirmedAt` (zero delay) ->
  `C0824_confirmed_land_deletes_with_lease_after_delay`: `before-due-no-push`.
- **PC-8** Treat a null cutoff as `DateTime.MinValue` -> `C0824_backlog_cutoff_and_run_bound`:
  `null-cutoff-none`.
- **PC-9** Remove `.Take(MaxSourceBranchDeletionsPerRun)` -> `C0824_backlog_cutoff_and_run_bound`:
  `bound-25`.
- **PC-10..PC-15** Delete one OR term from `PublishedBranchDeletionDependents.FindAsync`. Each goes red
  in its own `C0824_dependent_rows` row, in this order: `worktree-base-branch`, `worktree-base-task`,
  `requested-base-task-queued`, `repair-source-open`, `same-branch-follow-up`,
  `pending-land-request-other-task`. The assertion is the expected dependent id.
- **PC-16** Skip the runner-id allow-list check ->
  `C0824_runner_refusal_or_absence_defers_before_origin(runner-not-enabled)`: `origin-untouched`.
- **PC-17** Treat `Refused(code)` from the runner as success ->
  `C0824_runner_refusal_or_absence_defers_before_origin(checked-out)`: `origin-untouched`.
- **PC-18** In the catch around the runner call, continue instead of deferring -> the same method's
  `(offline)` row: `origin-untouched`.
- **PC-19** Skip the capability check, so the call is attempted and the fake's default `Deleted`
  answers -> the same method's `(capability-missing)` row: `origin-untouched`.
- **PC-20** Skip the step-2 recheck -> `C0824_branch_moved_before_sweep_is_kept_without_push`:
  `no-push`.
- **PC-21** Drop the `--force-with-lease=...` argument from `DeleteSourceBranchAsync` ->
  `C0824_branch_moved_at_push_is_refused_by_lease`: `lease-held`.
- **PC-22** Remove the `Deleting` save before the push ->
  `C0824_crash_after_push_before_save_recovers_once`: one event, and the row is `Deleted` (the restart
  cannot tell an in-flight delete from a fresh one, so it records `already_absent` and no `Deleted`).
- **PC-23** Move the desktop tracking-ref delete before the push ->
  `C0824_branch_moved_at_push_is_refused_by_lease`: `tracking-kept`.
- **PC-24** Delete `refs/heads/<b>` too with `update-ref -d` (git does not stop this for a checked-out
  branch) -> `C0824_confirmed_land_deletes_with_lease_after_delay`: `local-branch-kept`.
- **PC-25** Skip the `worktrees/*/HEAD` scan -> `C0824RunnerBranchRefsTests.C0824_checked_out_branch_refuses_and_keeps_refs`:
  `refs-kept`.
- **PC-26** Skip the `merge-base --is-ancestor` check ->
  `C0824RunnerBranchRefsTests.C0824_unpublished_local_tip_refuses(local-branch-ahead)`:
  refusal `unpublished_local_ref`.
- **PC-27** Delete without the old value ->
  `C0824RunnerBranchRefsTests.C0824_ref_moved_between_read_and_delete_refuses`: `moved-ref-kept`.
- **PC-28** Skip `DelegateBranchName.TryParse` on the runner ->
  `C0824RunnerBranchRefsTests.C0824_non_delegate_branch_refused(master)`: `master-kept`.
- **PC-29** Map a `cat-file -e` failure to "absent" ->
  `C0824RunnerBranchRefsTests.C0824_missing_expected_object_refuses`: refusal
  `expected_object_missing`.
- **PC-30** Add the land-pin prefix to the delete set (`refs/antiphon/land/<task>/<op>/source` deleted
  after the remote delete) -> `C0824_confirmed_land_deletes_with_lease_after_delay`: `pins-kept`.

A Mutation runs break, red, restore and green after land. Code runs the V and R rows. Review judges this
inventory before land.

If independent checks mask a mutant (for example, PC-20 is masked by the lease for the outcome alone),
the named assertion above is the one that separates them (`no-push`). Never weaken another guard to get
a red.

PCs 3..6, 8..15 and 25..29 each touch one method but share files. They are batchable only across
different files and methods (git-common): PC-21 (`LandingGit.cs`) can batch with one runner PC, and
PC-9 (service) with one policy PC.

Design audit: **guards=30, mapped=30, missing=0, duplicate PC maps=0**. Execution audit: **PCs run
0/30**. They wait for implementation, Review and land. This docs-only handoff does not claim any ran.

### Negative and safety witnesses (brief item 3), S2 view

- **Never for a refused or reverted land; only after confirmed publication.** V-15 (three families),
  G-2. A reverted land keeps the original source commits in the land pins (V-48). The 24 h grace delay
  (V-18) is the window for a manual revert before the name goes. There is no automatic revert
  detection (Q-4).
- **Local mirror refs cleaned.** The desktop tracking ref (V-45), the runner refs (V-40, V-44), and the
  audit after cleanup (V-43).
- **The S4 dependency.** G-16 (the volume allow-list) and G-25 (the runner refuses while a legacy
  mirror still has the branch checked out). V-43 proves with the **real** audit that skipping the runner
  step makes a recycle refuse, and that the guarded order passes.
- **Flag off is byte-identical.** V-19 (no git command, no row write) plus R-1 (32 existing
  retention cases).
- **S1 slot interplay.** A released slot has already deleted its runner refs (plan §3.4), so the runner
  op sees `absent` and returns success (`C0824_absent_refs_are_idempotent_success`).
- **S1-only items do not apply to S2:** dirty slot handover, slot sharing, reset or cold reset of a
  leased slot, re-homing, overflow bounds, native `bin/obj`, cross-task leakage, StartRef/BaseTask
  semantics, exact detached review trees. Their witnesses are V-1..V-13 and V-27..V-37 in the S1
  TestDesign. S2 adds nothing that leases, resets or creates a workspace.

### Out of scope

- CARD-0692's non-owner branch deletion. The S2 primitive is reusable there, but no row tests that.
- CARD-0669 land-residue refs, other than proving that S2 leaves the pins alone (V-48).
- GitHub-side behaviour: branch protection rejecting deletes, server-side hooks, HTTPS credential
  helpers, real latency. These belong to a canary after enable (rollout step 5), not to a checkpoint.
- Runner volumes other than the primary repository (secondary repositories, D-3).
- Windows execution of the server-side deletion (see Native below).
- Any Full or assembly-wide run.

### Implementation steps (red first, then green)

1. **Stubs that compile** (one commit). This includes:
   - settings with their defaults;
   - the `AgentTaskLanding` fields and the EF migration;
   - the event type;
   - `PublishedBranchDeletionPolicy.Decide`, which returns `NotEligible` for every input;
   - `PublishedBranchDeletionService.RunAsync`, a no-op;
   - `PublishedBranchDeletionDependents.FindAsync`, which returns empty;
   - the `ILandingGit.DeleteSourceBranchAsync` default, which refuses;
   - `IRunnerBranchRefs` and a refusing implementation;
   - the runner `RunnerWorkspaceService.DeleteBranchRefsAsync`, which refuses everything as
     `not_implemented`;
   - the contracts `PhoneHomeWorkspaceBranchRefsDeleteRequest/Response`, the operation
     `WorkspaceBranchRefsDelete` and the capability constant, with the capability not yet advertised.
2. **Tests** (one commit, pushed before the run): `C0824PublishedBranchDeletionTests` and
   `C0824RunnerBranchRefsTests`. Run CP-12 and CP-16 at this commit and record the reds:
   - **behavioural reds:** V-14, V-17 (dependent rows), V-40 delete cases, V-42, V-46, V-47 (`bound-25`);
   - **green against the stub:** the safety witnesses (V-15, V-16, V-19, V-38, V-39, V-41). Their red
     evidence is their PC at Mutation. Report them as "safe-by-stub".
3. **Server policy, dependents and service, plus the `LandingGit` primitive** (one commit). Then re-run
   CP-12, which must be green.
4. **Runner op** (`DeleteBranchRefsAsync`, the dispatcher case, the advertised capability) and the
   server's `PhoneHome` implementation of `IRunnerBranchRefs` (one commit). Then CP-16.
5. **Delivery and recycle classes** `C0824BranchRefsDeliveryTests` and
   `C0824BranchDeletionRecycleAuditTests`, plus the roster entry (test commit first, then any fix
   commit).
6. **Hangfire recurring job** `antiphon:published-branch-deletion` (every 15 min, Q-6), plus the docs:
   `docs/orchestration-loop.md` (S2 rule, settings, enable order) and `docs/docker-stack.md` (the
   recycle note: enable S2 per volume only after S4).
7. **Run A, then Run B** (below) at the final tip; report per CP-n with counts.

### Performance budget

No measurement was taken. The basis is the plan's receipts: Antiphon.Tests build p50 326 s and p95
579 s; SessionRunner.Tests build p95 135 s (n=11/117, server2, loaded).

Per-test bodies (provisional, n=0):
- `LandingSafetyHarness` real land plus sweep: about 4–8 s, with an isolated schema per test;
- pure policy rows: < 50 ms;
- dependent rows: about 1 s (isolated schema, no git);
- runner scratch-repo cases: about 1–2 s;
- C1008 `Preview`: unknown. The existing C1609 content tests run it several times per method, so take
  ≤ 30 s per Preview, provisionally;
- phone-home world: about 10–20 s, like `C1032_Join*`.

Per-test `[Timeout]`, provisional until measured, then about 2x p95:
- policy rows: none (synchronous);
- harness and dependent rows: 60 s;
- runner: 30 s;
- recycle: 180 s;
- delivery: 120 s (the `C1032_Join*` value).

Shared setup:
- one isolated schema per harness test. This is the existing harness contract, and sharing a schema
  across tests is unsafe for landing rows;
- the policy rows share nothing expensive;
- the C1008 fixture is built once per method;
- no Docker, AppHost, browser or real wait. All delays use `FakeTimeProvider`.

Record per-test TRX durations and the harness phase spans (git process wait, schema, land, sweep).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-12 | S2 | tests/Antiphon.Tests -> bin-c0824-s2/ | portable-branch-delete | /*/Antiphon.Tests.Application/C0824PublishedBranchDeletionTests/* | V-14..V-19, V-38, V-39, V-41, V-42, V-45..V-48 | class, 0 failed/skipped | 40 | 13 | true |
| CP-15 | S2 | CP-12 | portable-retention-regression | /*/Antiphon.Tests.Application/(AgentTaskLandRemoteSourceRetention*)/* | R-1 | 4 existing classes, 0 failed/skipped | 32 | 4 | true |
| CP-16 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s2r/ | portable-runner-refs | /*/Antiphon.SessionRunner.Tests/C0824RunnerBranchRefsTests/* | V-40 | class, 0 failed/skipped | 11 | 4 | true |
| CP-17 | S2 | CP-12 | linux-native-recycle-audit | /*/Antiphon.Tests.Scripts/C0824BranchDeletionRecycleAuditTests/* | V-43 | class, 0 failed/skipped | 3 | 3 | true |
| CP-18 | S2 | CP-12 | linux-native-branch-refs-delivery | /*/Antiphon.Tests.Application/C0824BranchRefsDeliveryTests/* | V-44 | class, 0 failed/skipped | 4 | 3 | true |
| CP-19 | S2 | CP-12 | portable-publication-regression | /*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/* | R-2 | existing class, 0 failed/skipped | 26 | 4 | true |
| CP-20 | S2 | CP-16 | portable-binding-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests/* | R-3 | existing class, 0 failed/skipped | 11 | 2 | true |

CP-12 refines the plan's CP-12 (Min 6 -> 40, estimate 12 -> 13). CP-15..CP-20 are new S2 rows and
renumber nothing. The union is S2's whole ordinary scope.

Method rosters (Min counts TUnit executions):
- **CP-12 (40):**
  - `C0824_policy_rows` with 16 rows:
    - `eligible-after-delay`, `before-delay`, `flag-off`;
    - `unconfirmed-pushstarted`, `refused-phase`, `residue-confirmed`;
    - `schema-2`, `recovery-adopt`;
    - `protected-master`, `protected-rc`, `non-delegate-name`;
    - `source-sha-mismatch`, `before-cutoff`, `runner-not-enabled`, `desktop-only-enabled`;
    - `already-deleted`.
  - `C0824_dependent_rows` (7).
  - `C0824_confirmed_land_deletes_with_lease_after_delay`.
  - `C0824_refused_or_unconfirmed_land_keeps_branch` (3).
  - `C0824_branch_moved_before_sweep_is_kept_without_push`.
  - `C0824_branch_moved_at_push_is_refused_by_lease`.
  - `C0824_flag_off_runs_no_git_and_writes_nothing`.
  - `C0824_runner_refusal_or_absence_defers_before_origin` (5).
  - `C0824_runner_refs_precede_origin_delete`.
  - `C0824_crash_after_push_before_save_recovers_once`.
  - `C0824_crash_after_runner_refs_recovers_idempotently`.
  - `C0824_backlog_cutoff_and_run_bound`.
  - `C0824_deferred_reason_evented_once`.
- **CP-16 (11):**
  - `C0824_deletes_local_and_tracking_refs_at_expected_sha`;
  - `C0824_absent_refs_are_idempotent_success`;
  - `C0824_checked_out_branch_refuses_and_keeps_refs`;
  - `C0824_unpublished_local_tip_refuses` with `local-branch-ahead` and `tracking-moved`;
  - `C0824_ancestor_tip_is_deleted`;
  - `C0824_non_delegate_branch_refused` with `master`, `rc/2026-11-01` and `feat/other`;
  - `C0824_ref_moved_between_read_and_delete_refuses`. This uses a new internal seam
    `Func<string, Task>? BeforeBranchRefDelete` on `RunnerWorkspaceService`, which moves the ref;
  - `C0824_missing_expected_object_refuses`.
- **CP-17 (3), CP-18 (4):** as listed in V-43 and V-44.
- **CP-15:** 32. A, B, C and D are each one 8-case method.
- **CP-19:** a floor of 26 (25 methods plus the Delivery partial).
- **CP-20:** a floor of 11.

No new class name is a prefix of another. All four new classes start `C0824` and differ before any
shared suffix, and none starts with an existing `C0824WorkspaceSlot*` name.

**Runtime and runs.** Each run builds once, so two runs:
- **Run A** (Antiphon.Tests, one build): `--rows CP-12,CP-15,CP-17,CP-18,CP-19`, about 27 min
  (13 + 4 + 3 + 3 + 4). That is under the 30-minute run standard, but close. If the first measured
  CP-12 exceeds 15 min, move CP-19 into its own run rather than over-run.
- **Run B** (SessionRunner.Tests, one build): `--rows CP-16,CP-20`, about 6 min (4 + 2).

Per-row timeouts in the imported YAML are 2x each estimate, with a floor of the build p95 plus the body
(CP-12 26 min, every other row 8 min).

The lanes `portable` and `linux-native` both resolve to server2 today. If the importer refuses to reuse
CP-12's build across lanes, CP-17 and CP-18 take their own build `bin-c0824-s2l/` (+10 min, a separate
run).

The red run of step 2 executes CP-12 and CP-16 only (about 17 min). Delete every `bin-c0824-s2*/` and
the tool output after each run.

### Native (Windows) proof and what Linux cannot prove

The server, and so the land worker and S2's sweep, runs on the Windows desktop. Before
`DeleteSourceBranchAfterPublication` is enabled on the desktop server, a windows-native lane run of
CP-12 and CP-15 (same filters, its own build) must show green. Linux cannot prove these:
- NTFS case-insensitive ref collisions (`Feat/` vs `feat/`; retention family V02 exercises the
  shape) when `update-ref -d` deletes a tracking ref, and packed-ref deletes with a Windows lock held;
- Git for Windows argv handling of the empty-source refspec `:<ref>` through `ArgumentList` (no shell
  is involved, so the risk is low);
- the real GitHub endpoint: branch protection, credential helper and latency. A one-land canary with
  delay 0 on a disposable branch is the rollout check, not a checkpoint.

The runner half (CP-16, CP-17, CP-18) is Linux by nature: server2 runners. S3's Windows desktop slots
do not change S2.

### Cost

**Estimated, not measured.**
- Ordinary V/R floor (Code) = the sum of the CP estimates: CP-12 13 + CP-15 4 + CP-16 4 + CP-17 3 +
  CP-18 3 + CP-19 4 + CP-20 2 = **33 min**, in Run A (27) plus Run B (6).
- Step 2's red runs: **17 min**.
- Tool bootstrap and import: **2 min**.
- **Code machine total: 52 min**, plus unmeasured slot waits.

PC floor (Mutation), 30 cycles, each a fresh build for baseline, red and restored green:
- Antiphon.Tests PCs (PC-1..PC-24, PC-30; 25 cycles): 3 × (9.7 build + 1 method) + 0.5 ≈ 32.6 min
  each = **815 min**;
- SessionRunner PCs (PC-25..PC-29; 5 cycles): 3 × (2.3 + 0.5) + 0.5 ≈ 8.9 min each = **45 min**;
- **PC floor ≈ 860 min.**

Total planned machine cost = 2 + 17 + 33 + 860 = **912 min**. The aggregate exceeds 30 minutes, so a
Debug timing task is needed before the battery (test-performance standards).

Savings:
- Sharing one baseline green per assembly at the landed SHA, if the Mutation owner contract allows it,
  would save about 24 × 10.7 + 4 × 2.8 ≈ **268 min**.
- The cross-file batches named above (PC-21 with one runner PC; PC-9 with one policy PC) save about
  **2 × 32 min**.
- Neither saving is pre-credited.
- One Antiphon.Tests build serves five rows. Separate builds would cost 4 × 9.7 ≈ **39 min** more, so
  that saving is real and already counted.

### Open questions for the Code stage (each has a default)

1. **Q-1 Lease value.** Default: `SourceRemoteSha` (B-1). The plan says "landed SHA", but for schema 3
   the branch never holds the rebased `VerifiedSourceSha`. Using it would refuse every real rebase land.
2. **Q-2 Open dependents.** Default: `Deferred` and re-evaluated every sweep, so the branch is deleted
   once the dependents close. The alternative is a permanent `Kept`.
3. **Q-3 Backlog.** Default: the new `SourceBranchDeletionAfterUtc` must be set to enable anything
   (null means no operation), and `MaxSourceBranchDeletionsPerRun=25`. This prevents a first enable
   from deleting every historical branch in one sweep.
4. **Q-4 Recovery or adoption lands and reverts.** Default: `RecoveryMode != None` is `Kept`, and there
   is no automatic revert detection. The 24 h delay and the land pins are the remedy.
5. **Q-5 Per-volume enable.** Default: `SourceBranchDeletionRunnerIds` (empty). A task that ran on an
   unlisted runner is `Deferred(runner_volume_not_enabled)`. Desktop-only tasks need only the global
   flag.
6. **Q-6 Cadence.** Default: a Hangfire recurring job every 15 minutes, not inline in the land.
7. **Q-7 Order.** Default: the runner refs are deleted **before** the origin delete (B-3), with an
   idempotent re-pass on the next sweep.
8. **Q-8 `LandedWithResidue`.** Default: a confirmed publication with refused worktree cleanup is still
   eligible (the policy row `residue-confirmed`). The desktop branch survives where it is checked out
   (G-24).

### Risks to testability

- `PhoneHomeMirrorJoinTests` (the template for CP-18) was 6/6 red on server2 at rc base `f84228f2`.
  Before blaming S2, the Code stage must run one `C1032_JoinIdleRecipient` at its base. If the world is
  still red there, CP-18 must be built on a repaired world or reported inherited, and the delivery
  proof returns to Plan.
- The `Preview` cost of the C1008 fixture is unmeasured. If CP-17 exceeds about 6 min, split it into
  its own run.
- Wide Antiphon.Tests rows can hit Postgres `53300`. CP-12 is one class, so the risk is low; rerun with
  `--maximum-parallel-tests 4` before calling a red real.
