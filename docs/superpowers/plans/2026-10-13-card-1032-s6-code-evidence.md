# CARD-1032 S6 Code evidence

S6 is implemented and the closed ordinary selection is green: CP-11=4,
CP-12=28 and CP-31=2, all passed with zero failures/skips. Production cleanup
remains uncomposed. Next: ordinary Review, then caller lands the original Code
owner and commissions SourceLanding Mutation. Restart: none.

Original Code / landing owner: f0995c2a-9679-4c21-a518-ce2b6c8a30f0.
Branch: feat/card-task-f0995c2a.
Worktree: /work/worktrees/task-f0995c2a.
Task base: 51fb2fc753e24993f2b3a2ff9b39bedb9ee9a02a.
Production implementation and CP-11/12 tested source:
fe77b1686599e10ffa2403f29005b1bbe8b41f8f.
Server fixture repair and CP-31 tested source:
aa60541ea3193540d4fe4eeb9355936532807e3e.
The repair changes only the new server test fixture; the source verified by
CP-11/12 is unchanged. The later evidence commit is documentation only.

## Implementation and paths

- src/Antiphon.SessionRunner.Contracts/CompletedCardWorkspaceContracts.cs:
  strict inspection validation and typed inspection/removal response DTOs.
- src/Antiphon.SessionRunner/RunnerCompletedCardWorkspaceTransport.cs:
  explicit fixture composition checks the shared gate/journal, invokes the S5
  read-only inspector and existing remover, and returns their recipient facts.
- PhoneHomeCommandDispatcher.cs, PhoneHomeRuntimeAdapter.cs and SessionRunnerRuntime.cs
  in the same runner project: operations 40/41 require installed capability,
  strict raw JSON and the current registered runner/store. The runtime's local
  capability description suppresses an uninstalled caller-supplied feature.
  The transport shares MutationLock and the workspace gate with configured
  legacy workspace operations. No Program, route, scheduler or Execute flag changed.
- RunnerWorkspaceNoFollowDelete.cs adds a bounded GUID read using held native
  no-follow path/type metadata; RunnerWorkspaceUseGate.cs exposes its journal
  internally so explicit composition can reject a mismatched pair.
- server/Application/Interfaces/ICompletedCardWorkspaceClient.cs and
  server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs: typed
  external I/O seam, capability/unsupported refusal without legacy fallback,
  incomplete completion ACK refusal. Server admission/endpoint persistence is S7/S8.
- New tests: tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceTransportTests.cs
  and tests/Antiphon.Tests/Application/PhoneHomeCompletedCardTransportTests.cs.
  The latter uses the actual phone-home socket, receive pump, mutation wait queue,
  scratch Git and external journal. The runner fixture is linked into the server
  tests and accepts a fixture runner identity; its default remains unchanged.
  The server Slow registry includes the new class.

The deletion/prune path inventory and gate ownership are in
2026-10-13-card-1032-s6-verification.md. S6 adds no filesystem deletion executor;
actual removal stays in S5 behind its existing journal, eligibility, inventory,
source, ownership and held-handle re-validation guards. Tests remove only their
owned scratch roots. Full task/preparation/store identity remains in the command
and recipient journal/receipt.

## Ordinary verification

The exact S6 manifest is
/docs/superpowers/plans/2026-10-13-card-1032-s6-verification.md.
It binds TestDesign CP-11/12 (not the superseded proposed plan numbering), adds
CP-31 for the required real-queue witness, and reuses CP-11's runner build for
CP-12. All outputs use forward-slash trailing-slash OutputPath and UseAppHost=false.
No build is named tests. No full assembly, Unit lane or whole-class server run
was made: the explicit S6 brief selects the closed checkpoint roster and excludes
those broader runs. No known inherited red was substituted or silently omitted
from this roster. No timeout/assertion was widened or weakened.

Fresh TRX inspection confirms CP-11's four exact methods, CP-12's 25
RunnerWorkspaceServiceTests plus three PhoneHomeCommandDispatcherTests methods,
and CP-31's two exact PhoneHomeCompletedCardTransportTests methods, all nonzero.
The fresh TRX result counters match the CHECKPOINT lines below. The server class
is fully covered by those two named methods; no broader server selection is claimed.

Receipt validation exited 0 for fe77b168... rows CP-11,CP-12 and aa60541e... row
CP-31, each dirty=0/sourceState=clean/buildSource=verified. Runs were bound using
the inherited task token, never printed. Every launched build/test row received
slot=granted with waited=0s. A transient owner HTTP 502 was recorded during the
implementation group and recovered; the terminal report did not end owner-unverified.

Unlisted build: the checkpoint tool bootstrap was necessary to run the closed
rows. It used scripts/build-slot.ps1, OutputPath=bin-c1032-s6-tool/,
UseAppHost=false, slot=granted waited=0s, and succeeded (one existing warning).
An initial tool invocation supplied an incorrectly transcribed full expected SHA;
it refused source_mismatch before any row/build. The SHA was corrected immediately.
Read-only receipt validation, TRX inspection, Git and evidence-diff checks are not
additional test/build drivers.

## Red proof and repairs

All six new test methods were committed/pushed before production implementation.
CP-11 at 23cd32f4d6e7350e65a234f58ab5d47eb86810ab: 4 executed, 4 assertion failures.
C1017_GuardedOperationIsDistinct, C1032_TransportRequiredPayload and
C1032_TransportDispatchReplay failed at the absent S6 concrete transport type.
C1032_TransportDefaultInert failed because DescribeCapabilities advertised an
uninstalled caller-supplied cleanup feature. These are ordinary pre-slice defect
proofs, not deliberate Mutation cycles.

The first CP-31 build at b6bb7a81d847221f43744e4d5d7baf56e779e367 failed for a
missing TUnit.Core.Exceptions import; zero tests ran, so it is not red proof.
After that test-only fix, CP-31 at 4cd45b384b1fde176bf81c0d19920e75edc1d56c:
2 executed, both assertion failures at the absent typed server-client seam.

The implementation group passed CP-11/12 and CP-31's unsupported-client case.
Its queue case passed removal/receipt checks but failed on same-process reconnect:
the fixture allocated a new boot identity during the existing server lease.
The test-only repair retains one PhoneHomeProcessIdentity, reconstructs the
journal/gate/workspace/runtime and requires a newer server epoch. Original
30-second connect, 45-second case and 10-second stop bounds were retained.
CP-31 then passed both methods. This proves connection/object reconstruction;
OS process-death/coordinator recovery remains S8 and is not claimed here.

Invocations: CP-11=2 (baseline red, implementation green); CP-12=1;
CP-31=4 (compiler failure, baseline assertion-red, one fixture-red, repaired green).
Only CP-31 reran after its fixture repair; no further unchanged repetition followed
its green result. No inherited-failure or flake claim was made.

## V/R outcomes and deferred obligations

| ID | Actual S6 outcome |
|---|---|
| V-1 | Earlier binding/schema/capture proofs not rerun; S7/S8 portions deferred. |
| V-2 | S6 typed dispatch, capability, validation, durable receipt and actual socket wiring passed CP-11/31. Earlier schema and later endpoint receipt-import work not newly certified. |
| V-3 | Earlier inspection/content/source proofs carried; bound-rebase positive remains S7 CP-13 C1032_AdmissionBoundRebaseProof. No new positive rebase claim. |
| V-4 | Held-use lease refuses typed and explicitly gated legacy removal in C1032_TransportDispatchReplay; earlier launch/adoption census race methods not rerun. |
| V-5 | S6 real physical directory/registration removal, replacement preservation and durable replay passed CP-11/31. Earlier S5 process-death and future S8 coordinator recovery not rerun. |
| V-6 | Server endpoint admission/import/outcome persistence belongs to S7/S8; deferred, not passed. |
| R-1 | All 28 frozen Mirror/Publish/Remove/Workspace compatibility results passed CP-12. |
| R-2 | Default-inert capability and unsupported-client/ACK refusal passed CP-11/31. Earlier unbound contract proof not rerun. |
| R-3 | Parent distinct guarded operation and S6 transport/replay methods passed CP-11/31; earlier parent U methods carried, later parent M methods deferred. |

S7 CardWorktreeMirrorCleanupService/authoritative landing-map production,
C1032_AdmissionBoundRebaseProof (PC-60 positive and PC-581), S8 endpoint/outcome
recovery and activation remain outside this slice and are not marked passed.
Required native Windows Final Review remains pending at the implementation source:
/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileIdentityRefuses*
/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileContentRefuses*
/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateRootIdentityRefuses*
The new registration/transport portable methods should also be qualified there via
CP-11's exact filter before activation; Linux green does not certify Windows handles.

Every prior plan PC and named variant, including PC-52/267..270, PC-578..587,
and every S6 candidate PC-588..594/variant remains pending SourceLanding Mutation.
Candidate: /docs/superpowers/mutations/2026-10-13-card-1032-s6-candidate.md.
It is unbound. Mutation owns compiling payloads, separate field/variant red,
restoration/green and missing-control discovery (including candidate variants not
individually established by ordinary vectors). No deliberate mutant ran here.

## Re-run and publication

Bootstrap the checkpoint tool through scripts/build-slot.ps1 with an isolated
forward-slash output, then invoke its built DLL (avoids an unleased implicit build):
run --plan docs/superpowers/plans/2026-10-13-card-1032-s6-verification.md
--rows CP-11,CP-12,CP-31 --expected-source-sha <full committed HEAD> --max-wait 50s.
Wait each exit 75 with wait <run-id> --max-wait 50s until terminal.
Do not rerun already green unchanged proof without a concrete new reason.

All meaningful test/implementation/fix slices were committed and pushed to the
assigned fast-forward-only task branch. No amend/rebase/reset/force push or shared
master publication occurred. git ls-files: 7548. Fetched origin/master merge-tree
--write-tree exited 0 at aa60541e... (tree d2bc929ed3622f1f5eeac6ec24e99f3031d5a215).
Remote ref equaled aa60541ea3193540d4fe4eeb9355936532807e3e before this evidence-only
commit. Final HEAD/ref/clean status and full-range evidence-diff are in the final
caller report and .antiphon/task-f0995c2a.md. Checkpoint outputs were automatically
removed by the green run; the bootstrap output is removed before settlement.

## Unedited checkpoint lines

Run 20261013-115544-e9df

```text
CHECKPOINT CP-11 commit=23cd32f4d6e7350e65a234f58ab5d47eb86810ab build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_GuardedOperationIsDistinct*)|(C1032_Transport*) executed=4 passed=0 failed=4 skipped=0 trx=/work/worktrees/task-f0995c2a/.antiphon/checkpoints/20261013-115544-e9df/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=23cd32f4d6e7350e65a234f58ab5d47eb86810ab sourceState=clean buildSource=verified
```

Run 20261013-115804-8ec4

```text
CHECKPOINT CP-31 commit=b6bb7a81d847221f43744e4d5d7baf56e779e367 build=failed filter=/*/*/PhoneHomeCompletedCardTransportTests/C1032_Transport* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=b6bb7a81d847221f43744e4d5d7baf56e779e367 sourceState=clean buildSource=unknown
```

Run 20261013-120152-436e

```text
CHECKPOINT CP-31 commit=4cd45b384b1fde176bf81c0d19920e75edc1d56c build=ok filter=/*/*/PhoneHomeCompletedCardTransportTests/C1032_Transport* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-f0995c2a/.antiphon/checkpoints/20261013-120152-436e/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=4cd45b384b1fde176bf81c0d19920e75edc1d56c sourceState=clean buildSource=verified
```

Run 20261013-120945-9758

```text
CHECKPOINT CP-11 commit=fe77b1686599e10ffa2403f29005b1bbe8b41f8f build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_GuardedOperationIsDistinct*)|(C1032_Transport*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f0995c2a/.antiphon/checkpoints/20261013-120945-9758/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=fe77b1686599e10ffa2403f29005b1bbe8b41f8f sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=fe77b1686599e10ffa2403f29005b1bbe8b41f8f build=reused filter=/*/*/(RunnerWorkspaceServiceTests*)|(PhoneHomeCommandDispatcherTests*)/(Mirror_*)|(Publish_*)|(Remove_*)|(Workspace_*) executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-f0995c2a/.antiphon/checkpoints/20261013-120945-9758/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=fe77b1686599e10ffa2403f29005b1bbe8b41f8f sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=fe77b1686599e10ffa2403f29005b1bbe8b41f8f build=ok filter=/*/*/PhoneHomeCompletedCardTransportTests/C1032_Transport* executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-f0995c2a/.antiphon/checkpoints/20261013-120945-9758/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=fe77b1686599e10ffa2403f29005b1bbe8b41f8f sourceState=clean buildSource=verified
```

Run 20261013-121457-2aa6

```text
CHECKPOINT CP-31 commit=aa60541ea3193540d4fe4eeb9355936532807e3e build=ok filter=/*/*/PhoneHomeCompletedCardTransportTests/C1032_Transport* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-f0995c2a/.antiphon/checkpoints/20261013-121457-2aa6/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=aa60541ea3193540d4fe4eeb9355936532807e3e sourceState=clean buildSource=verified
```
