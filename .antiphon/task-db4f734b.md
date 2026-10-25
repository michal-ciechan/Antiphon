# Group 38 Debug+Fix report

All 26 failures reproduced at the fetched Linux baseline are repaired. Latest assigned-class results: 115 passed, zero failed, five Windows-only skips. Seven additional landing verification-window tests passed. One production defect was found and fixed with a real-lease red witness; ordinary Review is required before landing because the brief waived Review only for fixture-only changes.

Branch: `feat/card-task-db4f734b`.
Fetched starting master/base: `9376a1daacff7b26d2c2788466efca8d9dac0b11`.
Final code and final 30-test selection: `24052d14fcb0aa9736bf495daef9939e06a92238`.
Earlier green assigned-class selection: `de9ba11cd49132cc2b04c03e65f6a64b764eb186`.
Real-lease product red witness: `87d7064d4615d9e814d3addec382dca911a9dee6`.

## Counts and attribution

P/F/S means passed/failed/skipped. The latest column combines the final affected-class run at 24052d14 with unchanged classes' preceding results at de9ba11c; it is not a claim that all classes were rerun at the final commit.

| Assigned class | Baseline P/F/S | Latest P/F/S | Latest run |
|---|---:|---:|---|
| PhoneHomeStrandedQueueTests | 8/8/0 | 16/0/0 | 24052d14 |
| SpecialistInputTransportTests | 15/1/0 | 16/0/0 | de9ba11c |
| SpecialistQualificationTests | 1/14/0 | 15/0/0 | de9ba11c |
| SessionMessageQueueInterruptedAttemptTests | 14/1/0 | 15/0/0 | de9ba11c |
| DefaultRunnerRerouteTests | 16/0/0 | 16/0/0 | de9ba11c |
| PhoneHomeStandingLaunchTests | 15/0/0 | 15/0/0 | de9ba11c |
| AgentTaskLandAdoptionConcurrencyTests | 6/1/0 | 7/0/0 | 24052d14 |
| SessionMessageQueueGrokPtyIntegrationTests | 0/0/5 | 0/0/5 | de9ba11c |
| WallRerouteDispatchTests | 14/1/0 | 15/0/0 | de9ba11c |

Additional product-boundary coverage: `LandVerificationLeaseWindowTests`, 7/0/0 at 24052d14. This additional selection is justified by the production ticket-validation change; it covers ticket fields, worktree identity/head/tree, source residue, compatible preflight, and the new unrelated-hold refusal.

DefaultRunnerRerouteTests and PhoneHomeStandingLaunchTests did not reproduce on Linux. No fix is claimed for their historical Windows failures. All five Grok PTY cases explicitly skip off Windows; their original native failure remains unverified here. No Windows path/shell defect was established, so no speculative platform fix or Windows-string witness was invented. Native rerun rows are below.

## Changes

- `tests/Antiphon.Tests/Application/SpecialistInputTransportTests.cs`: after a fixture changes the persisted session generation, bind the replacement terminal's dialog observation to that generation. The existing stale specialist-policy exception and zero-input assertions remain effective instead of being intercepted by an unavailable old observation.
- `tests/Antiphon.Tests/Application/SpecialistQualificationTests.cs`: reuse Card0758Fixture.SyntheticModernProfile rather than asking Linux to resolve a native Windows backend; bind alternate fake terminals with ClaudeDialogFixture; publish synthetic completed turns through StoreAwareTranscript. All 15 cases pass, including fallback, backlog, cancellation and stale-generation cases.
- `tests/Antiphon.Tests/Application/SessionMessageQueueInterruptedAttemptTests.cs`: use the shared MarkIdleAsync transition so the projected working state sees the complete late prompt's TurnEnd. All 15 assertions/cases retained and green.
- `tests/Antiphon.Tests/Application/PhoneHomeStrandedQueueTests.cs`: the peer now uses the shared fake adapter for supported composer snapshots, current generation-bound observations, advertised capability and guarded conditional writes. It records the actual submitted prompt. Reconnect recovery drives the required teardown/catch-up cycles. The offline channel availability case explicitly uses Raw so it reaches unavailable transport without requiring an observation from an absent Claude peer. Existing online Claude queue cases remain Claude. Failed-refresh fixture intervals now await server processing before advancing fake time, with the pump wired to the host's capturing logger.
- `tests/Antiphon.Tests/Application/WallRerouteDispatchTests.cs`: use a capacity-wall stimulus for automatic rerouting. The old Fable subscription-quota text deliberately takes the newer explicit-redispatch path; all rerouting assertions remain.
- `tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs`: retain the real competing drain, Held state, hold episode/provenance, reset count and publication assertions. The exact expected reason is updated to the newer outer `land_execution_busy` gate. Publication failures now include phase/reason diagnostics. Temporary execution-lease overrides were removed completely; LandingSafetyHarness has no net change.
- **Production:** `server/Application/Services/AgentTaskLandingProtocol.cs`, ValidateTicketAsync: a duplicate drain can durably record a Held/land_execution_busy wait while the original coordinator still retains the genuine execution lease. The old Running-only check refused that owner's unchanged verification ticket. Allow exactly that hold reason alongside Running; preserve every pending/request/attempt/operation/ticket/registration/source check. Other Held reasons remain refused.
- `tests/Antiphon.Tests/Application/LandVerificationLeaseWindowTests.cs`: add C1388_UnrelatedHoldStillRefusesResume, asserting unrelated Held state refuses publication, never starts a push, and leaves remote master unchanged.

The real production red witness at 87d7064d used genuine execution and repository leases, with no lease override and no production change relative to the starting production tree. The sole failure was C883_HeldWriterBetweenMoveAndResetDoesNotLoseHoldFacts: `phase=Refused; reason=verification_ticket_changed; publication=Refused`. Phone-home was 16/16 green in that same run. At 24052d14 the witness is green, and the seven verification-window cases are green. The new negative guard prevents treating every Held request as eligible.

## Execution evidence and costs

All drivers ran under scripts/build-slot.ps1. TUnit used dotnet run --no-build, isolated OutputPath=bin-db4f734b/, UseAppHost=false, exact class filters, and a fresh TRX per invocation. The brief's direct-driver exception was used because Antiphon.Tests.dll exceeds the checkpoint inventory reader's 64 MiB limit. No checkpoint receipt or native qualification credit is claimed. No Unit lane, desktop test, native lock, release script, tag, restart or deployment was used.

| Build | Source | Elapsed | Slot wait | Result |
|---|---|---:|---:|---|
| baseline full graph | 9376a1da | 7m15.48s | 0s | 0 errors, 1197 warnings |
| fixture full graph | de9ba11c | 3m48.32s | 60s | 0 errors, 1197 warnings |
| follow-up full graph | 256dcb9f | 4m28.70s | 0s | 0 errors, 1197 warnings |
| real-hold witness tests-only | 87d7064d | 1m30.62s | 210s | 0 errors, 1155 warnings |
| product full graph | 24052d14 | 2m52.61s | 15s | 0 errors, 1197 warnings |

The tests-only rebuild used --no-restore --property:BuildProjectReferences=false because production sources were unchanged; the final product build rebuilt the complete graph. The baseline and final full builds report the same warning count; no warning suppression or timeout widening was introduced.

| Run | Source compiled | P/F/S | Duration | Slot wait |
|---|---|---:|---:|---:|
| nine assigned classes, baseline | 9376a1da | 89/26/5 | 2m05.925s | 0s |
| nine assigned classes, first fixes | de9ba11c | 113/2/5 | 2m39.903s | 75s |
| quiet-card inventory isolation | de9ba11c DLL | 1/0/0 | 34.912s | 75s |
| phone-home + adoption follow-up | 256dcb9f | 20/3/0 | 1m16.991s | 420s |
| real-lease red, phone-home + adoption | 87d7064d | 22/1/0 | 1m00.231s | 0s |
| final phone-home + adoption + verification-window | 24052d14 | 30/0/0 | 1m24.460s | 0s |

Historical failures are retained: the initial execution-lease fixture override was invalid for verification windows and was removed; the quiet-inventory case passed at baseline, failed in the combined first-fixes run, and passed unchanged in isolation. It is classified as scheduling-sensitive, not proven inherited red. The added server-processing wait initially lacked a capturing pump logger; wiring that logger produced 16/16 phone-home passes in both the real-hold and final runs. There are no remaining latest-result failures.

Build execution fences were 900 seconds for the first three builds and 600 seconds thereafter. Test fences were 900 seconds for the first two combined runs, 120 for the isolated case, 600 for the first final run, 300 for real-hold, and 600 for product green; admission wait is separate. No run hit a timeout or returned build-slot exit 4.

Final DLL SHA-256, checked unchanged after the green run: `6988d7203a95734c87e578239d87496cb6ce2013d760c13dbf7358234577ac35`.
Logs, fresh TRX files, hashes and summaries: `/work/worktrees/task-db4f734b/.antiphon/db4f734b-evidence/` (ignored generated evidence).
All 33 task-created bin-db4f734b output roots were removed after the final process exited; no such roots remain.

## Rerun selections

Final Linux verification command, after rebuilding the isolated output:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label db4f734b-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-db4f734b/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label db4f734b-verify -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-db4f734b/ --property:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(PhoneHomeStrandedQueueTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(LandVerificationLeaseWindowTests*)/*' --report-trx --report-trx-filename verify.trx --results-directory .antiphon/db4f734b-rerun
```

The caller's native qualification owner should rerun these rows through its native workflow; this task did not run them or touch the detached desktop supervisor. Native builds must stage fakegrok.exe normally (do not carry Linux's UseAppHost=false override).

| Native row | Exact filter | Expected cases |
|---|---|---:|
| stranded queue | `/*/*/PhoneHomeStrandedQueueTests/*` | 16 |
| specialist transport | `/*/*/SpecialistInputTransportTests/*` | 16 |
| specialist qualification | `/*/*/SpecialistQualificationTests/*` | 15 |
| interrupted attempts | `/*/*/SessionMessageQueueInterruptedAttemptTests/*` | 15 |
| default runner reroute | `/*/*/DefaultRunnerRerouteTests/*` | 16 |
| phone-home standing launch | `/*/*/PhoneHomeStandingLaunchTests/*` | 15 |
| adoption concurrency | `/*/*/AgentTaskLandAdoptionConcurrencyTests/*` | 7 |
| Grok native PTY | `/*/*/SessionMessageQueueGrokPtyIntegrationTests/*` | 5 |
| wall reroute | `/*/*/WallRerouteDispatchTests/*` | 15 |
| product verification guards | `/*/*/LandVerificationLeaseWindowTests/*` | 7 |

The scripted peer helper also has existing consumers in PhoneHomeOutageHarness and MultiRunnerRecoveryTests. Their files were not edited and those sibling-owned selections were not run here; their owners should include the updated helper in their native follow-up.

## Publication and handoff

All meaningful slices were committed and pushed to the assigned branch. No rebase, reset, amend, target merge or force push was performed. Diff whitespace check is clean. Evidence-history check at the code tip: 11 commits, zero evidence-policy violations.
Fetched master for the final code merge-tree check: `0d51344da28bcb5dea4f37a86c7434bc4c606da4`.
`git merge-tree --write-tree origin/master HEAD` exited 0 with tree `e11e7eccd2bf96dccd606ab64265f739bf78bef2`; no merge was applied.

Commits, oldest first:

```text
b8754f0998750f4bfab2dcd6607832af4d613a1a replacement specialist observation
d008127baca7ce1068c4d4f9020f9a680745d5c4 projected interrupted-attempt idle
505d1909f356d229e1942e1e19ee66be9caad6e5 supported qualification transport/turns
06a5dadef40fae0b7af4b855bb873f8efd2acd49 capacity-wall fixture semantics
c3971431e944613fa665bd4b62f6a03b2fb00035 historical outer-gate fixture attempt
de9ba11cd49132cc2b04c03e65f6a64b764eb186 guarded phone-home peer/reconnect
310d9936deba7b324e28cba279fdadbacb388e49 historical owner-lease correction
256dcb9f1b3c85e8f025e574d2735214fd3807cd refresh processing boundary
87d7064d4615d9e814d3addec382dca911a9dee6 capturing logger and real-lease red witness
e80c0393aee743048366c55ebd81e548587cf8ff unrelated-hold negative guard
24052d14fcb0aa9736bf495daef9939e06a92238 narrow production ticket-state fix
```

Standing authority was sufficient for the reproduced repairs; no approval question was needed. The product finding makes the fixture-only Review waiver inapplicable, so ordinary Review should precede the requested land. Native qualification remains pending and is not implied by the Linux results.

--- next stage ---
next: review
handoff: Review the narrow production ticket-state fix and the real-lease red/green plus unrelated-hold negative evidence; then land the original db4f734b task branch and rerun the native rows.
artifact: .antiphon/task-db4f734b.md
