# CARD-1065 S2 Code evidence

Strict runner workspace publication is implemented and CP-2 is green (3 executed,
3 passed, 0 failed, 0 skipped). No wire caller, seat/session release, feature
activation, migration, deployment or restart was added/performed.

Original Code task / landing owner: `bc986d96-6c94-49bb-a8dd-66e79f10da6c`.
Branch: `feat/card-task-bc986d96`.
Worktree: `/work/worktrees/task-bc986d96`.
Assigned desktop checkout (not accessed): `C:\Antiphon\worktrees\card-task-bc986d96`.
Task base: `939c5bd0c1c8c7dbdf876ce9cf522e0963177eb8` (S1 owner
`9bae45cf-ea9d-47fe-ba9f-ab074c1be317`). The caller lands S2 by adoption after S1 lands.
Plan: `docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md`,
superseding Verification design and its single executable Checkpoints table.

## Delivered boundary

- `src/Antiphon.SessionRunner.Contracts/WorkspacePark.cs`: immutable action/task/
  attempt/block event/agent/session/generation/source bindings, typed publication
  result and receipt. A receipt is source evidence, never release authority.
- `src/Antiphon.SessionRunner/RunnerWorkspaceParkService.cs`: PrepareAsync and
  read-only source VerifyAsync. Strict owned path/ref/common-repository admission,
  common-directory `antiphon/landing.lock` file lease, baseline/remote ancestry,
  no auto-commit, ordinary single-ref nonforced push of the captured SHA, fresh
  exact remote reads even for equal/missing refs, endpoint fingerprint checks,
  final source/ref/cleanliness reinspection. Dirty tracked/index/untracked/submodule
  files, sequencers and hidden index flags refuse. Read uncertainty is Unknown;
  failed/canceled push never produces a receipt. All Git children use the existing
  owned, awaited process path and bounded cancellation cleanup.
- `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs`: expose reusable internal
  Git/path helpers and extract the unchanged common-directory ownership predicate.
  Existing PublishAsync behavior/arguments are unchanged.
- `tests/Antiphon.SessionRunner.Tests/WorkspaceParkPublicationTests.cs`: exactly
  the three planned Integration methods, with ProcessSpawnLimit, real bare remotes,
  empty global/system Git configuration, controlled I/O failures and boundary
  barriers. Every scratch Git child is awaited and cleaned up. Lease contention
  uses two actual publishers. Read-error assertions include the actual inspection
  decision so subsequent ancestry checks cannot mask its removal.

All implementation/test seams remain within the S2 file table. S3 owns the wire
capability, generation/input reservation and final release gate. S4 owns workspace
mode/no-commit/commit-recovery and unresolved ignore-policy admission (PC-12).
No assertion or timeout was loosened. Feature switches remain default-off.

## Ordinary verification

The brief's explicit S2/CP-2-only and no-whole-Unit instruction, matching the plan's
closed list, was used over its generic Final-profile boilerplate. This is complete
ordinary verification for S2, not the full integrated CARD-1065 qualification.

| ID | Exact method | Final result |
|---|---|---|
| V-3 | WorkspaceParkPublicationTests.C1065_CleanCommittedTipIsPublishedExactly | 1 passed, 0.9242954s |
| V-4 | WorkspaceParkPublicationTests.C1065_DirtyOrUnsafeSourceCannotPublishReceipt | 1 passed, 2.1095751s |
| V-5 | WorkspaceParkPublicationTests.C1065_PushAckWithoutExactRemoteProofIsHeld | 1 passed, 4.4184928s |

Fresh final TRX definitions and results were independently parsed: exactly these
three methods, exactly three Passed outcomes, no unexpected class/method or skip.
No R-n is assigned to S2. R-1, R-2, R-3, R-4, R-5 and R-6 remain for S11;
V-1/V-2 belong to S1 and were not rerun here; V-6 through V-27 belong to S3-S10.
CP-1 and CP-3 through CP-13 were not run or claimed passed by this task. Windows,
provider/OS process-tree and isolated activation acceptance remain future work.

Each run used the checkpoint tool with `--after S2 --expected-source-sha <HEAD>`
and the exact filter `/*/*/WorkspaceParkPublicationTests/C1065_*`. Source was
committed/pushed before every run and frozen until the foreground run completed.
No full assembly or Unit lane was run. No deliberate mutants were applied.

Final tested implementation SHA: `bdf6273420b033a2dedb8c1cd8f02902a253c1ec`.
This later evidence-only commit changes no tested implementation/test file; the
final publication SHA is recorded in the caller report, not substituted for the
actual tested SHA below.

### Unedited checkpoint receipts

Initial run (compiler failure in the new Shouldly assertion; zero tests executed):

```text
CHECKPOINT CP-2 commit=2fc20d814ff7bfcc70c28c30837c9c9c87547e37 build=failed filter=/*/*/WorkspaceParkPublicationTests/C1065_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=2fc20d814ff7bfcc70c28c30837c9c9c87547e37 sourceState=clean buildSource=unknown
```

Failure: CS8122 at WorkspaceParkPublicationTests.cs:35 (pattern matching inside a
Shouldly expression tree). This was introduced here, not attributed to the base.
The failed build itself held a granted slot, waited=0s; the row's slot=skipped
means its test host never started. Run: `20261005-225429-ab5a`.

Repair round 1 corrected assertion syntax without weakening it:

```text
CHECKPOINT CP-2 commit=db6728668d0e422f059a5cfa6285bdf8e0af6490 build=ok filter=/*/*/WorkspaceParkPublicationTests/C1065_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-bc986d96/.antiphon/checkpoints/20261005-225537-560c/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=db6728668d0e422f059a5cfa6285bdf8e0af6490 sourceState=clean buildSource=verified
```

Repair round 2 was a self-review test-strengthening change: expose and assert the
actual first read-error decision for PC-22, avoiding later-guard masking. That
source change justified the final CP-2 rerun; no unchanged proof repetitions ran.

```text
CHECKPOINT CP-2 commit=bdf6273420b033a2dedb8c1cd8f02902a253c1ec build=ok filter=/*/*/WorkspaceParkPublicationTests/C1065_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-bc986d96/.antiphon/checkpoints/20261005-225752-3f6c/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=bdf6273420b033a2dedb8c1cd8f02902a253c1ec sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=18.6486358s startup=2.140481s testsWall=7.4946481s teardown=0.4928914s hostWall=10.1280973s
CHECKPOINT SOURCE VALID source=bdf6273420b033a2dedb8c1cd8f02902a253c1ec rows=1
```

Final JSON receipt:
`/work/worktrees/task-bc986d96/.antiphon/checkpoints/20261005-225752-3f6c/report.json`.
Validated with the checkpoint tool's `validate --evidence <report.json>
--expected-source-sha bdf6273420b033a2dedb8c1cd8f02902a253c1ec --rows CP-2`, exit 0.
Final build and test each held its own granted slot with waited=0s, maxcpucount=6.
Each CP-2 invocation performed one build; the tool's “builds: 13” summary counts
the imported full manifest, not thirteen executed builds.

### Other checks and provenance

One additional build bootstrapped the unchanged checkpoint tool, as the plan
explicitly permits: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label
c1065-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints
--property:OutputPath=bin-c1065-driver/ --nologo`. Source was
`2fc20d814ff7bfcc70c28c30837c9c9c87547e37`, slot=granted, waited=0s,
maxcpucount=6, exit 0, 5.61s MSBuild time, one CS8602 warning in unchanged
TaskOwnerGuard.cs. No other unlisted build/test driver was used.

Full-plan static coverage ran before and after the final strengthening. Both
returned exit 2, INPUT_INVALID “unresolved selected class”, because later S3-S10
test classes do not yet exist. The parser reports its last enumerated source
`tests/Shared/TestClassificationMetadata.cs`, as documented in TestDesign.
This is not a clean coverage claim. Integrated lint remains for S11; do not add
placeholder classes. Final diagnostic: `.antiphon/c1065-coverage.txt`.

Full task-range evidence guard at the tested source returned:

```text
EVIDENCE result commits=3 entries=0 violations=0 base=939c5bd0c1c8c7dbdf876ce9cf522e0963177eb8 head=bdf6273420b033a2dedb8c1cd8f02902a253c1ec
```

The same `scripts/check-evidence-diff.ps1 -BaseRef
939c5bd0c1c8c7dbdf876ce9cf522e0963177eb8 -HeadRef HEAD` is rerun after this
report commit; its final outcome is in the caller report. Generated JSON/TRX/logs
remain ignored; this individual Markdown report is the only committed evidence.
The checkpoint tool removed its CP-2 outputs; the bootstrap output is removed
before settlement. No runtime-owned evidence directory is force-added.

ANTIPHON_TASK_TOKEN was present. Authenticated GET /api/runner-defaults and
/api/session-runners were read before implementation. No routing pin, platform
setting, runner budget or session was changed. No fleet location was embedded.

## Mutation and handoff

All 21 S2 controls remain **pending** method-scoped SourceLanding Mutation:

- V-3: PC-8, PC-200.
- V-4: PC-9, PC-10, PC-11, PC-13, PC-14, PC-15, PC-16, PC-17, PC-18, PC-19, PC-20.
- V-5: PC-21, PC-22, PC-23, PC-24, PC-25, PC-26, PC-27, PC-28.

No red/restore/green mutation evidence is claimed. Mutation owns any missing
controls it discovers; PC-12 is explicitly assigned to S4 by the superseding
table. Read-error, root-admission and ancestry decisions have direct assertions
where later fences could otherwise hide a bypass. Tests also exercise actual
publication/refusal and independent valid controls.

Next: Review S2 ordinary implementation/evidence; caller then lands original
Code task bc986d96 by adoption after S1 lands, commissions SourceLanding Mutation,
and commissions S3 wire/capability/final release binding from the plan. S3 must
preserve Working/input/generation safety and default-off activation.

Restart: **none** for this dormant slice. Future runner/server activation is owned
by the caller's separately commissioned rollout after the plan's qualifications.
