# CARD-0475: repair and scope the regression suite

Plan task: `c1384c67`. Source inspected at `7a6b44f97a7534578fa017af35c1376f90809729`.

The five requested changes are implementable without relaxing landing safety or delivery acceptance. The overlay failure is now diagnosed: the fake submits successfully, but its missing transcript ingestion leaves Grok confirmation reading the fake's persistent screen echo. A temporary transcript-pump experiment passed with unchanged timing settings. Implementation is still pending.

## Ground truth

The full profiling report was read first: `C:\Antiphon\worktrees\card-task-722e21e7\.antiphon\task-722e21e7.md`. Its evidence is under **`.antiphon/profile-722e21e7/`**, not a top-level `profile-722e21e7/`. The profile measured commit `8234ae3e5f288f4de1c1a67d1d591b763da7ccd4`; it is evidence about that version and host load, not a fresh full-suite benchmark of this branch.

| Brief assumption | Source/evidence and consequence |
|---|---|
| Four failures need repair | Three Unit failures reproduced twice in the supplied report; the overlay also failed alone. No evidence establishes when they were introduced. |
| The enum test is stale | `tests/Antiphon.Tests/Application/DelegationUnitTests.cs:1460`, class `UnmarkedWaitingContractTests`, pins named values correctly but incorrectly asserts the maximum is 28 at line 1479. `AttentionKind` is in `server/Application/Dtos/AttentionDtos.cs`; later appended members legitimately reach 38. |
| Admission forgot the graph | `DelegationHarnessCensusTests.cs:44` only accepts same-file text. `LandingSafetyHarness.BuildServices` really calls `AddDelegationWorktreeGraph`; admission adds its dispatcher through `ConfigureServices`. Fix the census's helper recognition. |
| A test class has no lane | `DelegateScriptLandStatusTests.cs:10` executes PowerShell against a private HTTP listener, so it belongs to Integration. It already has the process limiter. |
| Overlay recovery is broken | A new diagnostic captured `OVERLAY:closed`, one `SUBMITTED:hello after overlay`, and the response. The terminal accepted the body. The fixture has neither `ANTIPHON_FAKE_TRANSCRIPT_PATH` nor a pump; Grok's unobservable fallback sees the echoed body and returns `NoSubmitOutput`. This is a real failing integration fixture, not evidence of a broken Esc implementation or a harmless timing flake. |
| The Git matrices repeatedly build the solution | They use `LandingSafetyHarness.ControlledVerifier`; their cost is real Git I/O. The three classes contain 169 rows and consumed 4,671.609 seconds (77m 51.6s) of serialized lifecycle work. Boundary V10 alone is 90 rows. |
| Replacing a DI registration removes that cost | `LandingSafetyHarness.InitializeAsync` always initializes `LandingGitFixture`; `CreateLand` also passes `Fixture.Git` directly. A test-only controlled harness must avoid both paths, not merely register a different `ILandingGit`. |
| V35 has distinct pass/fail proofs | `AgentTaskLandVerifierTests.cs:18` calls V34 for both outcomes. V34 already protects the private `bin-land` file. Only V35 cancellation is distinct. |
| Scoped regression is new policy | `docs/testing-and-build.md` already makes Unit the local default and names touched classes. `stage-code.md` and `stage-review.md` omit actionable scope requirements. |
| The old tripwire found 202 new slow tests | `test-duration-tripwire.ps1:25` checks a method/argument `testName` against class names. Joining `testId` to `TestDefinitions/UnitTest/TestMethod@className` reduces 202 hits to 174 with the existing allowlist. The remaining four landing classes are genuinely unlisted. |

## Decisions

- **D-1: use a separate TestDesign dispatch.** The controlled Git substitution changes which layer supplies safety evidence, and existing C448 mutation selectors need remapping. TestDesign must append the executable V/R/guard/PC inventory before Code. This plan supplies explicit row allocation, experiment evidence and acceptance criteria; it does not claim the positive-control audit is already complete.
- **D-2: preserve the full behavioral matrices; make Git real only where its semantics are the proof.** Add 140 controlled protocol cases and retain 60 existing real-Git rows across the three expensive classes. There will be 200 cases across the split, not fewer total assertions. Reject deleting alternate argument rows, shared seed repositories as the main fix, and production inspection caching: they respectively lose coverage, miss most command cost, or change production safety.
- **D-3: keep real services, persisted state and repository locking in controlled tests.** Substitute external Git I/O; do not replace `AgentTaskLandService`, `AgentTaskLandingProtocol`, the dispatcher, their database or their admission decisions with canned outcomes. Controlled classes remain Integration because they use PostgreSQL and filesystem leases. Remove the process-spawn annotation only from new classes that actually spawn no children; leave the existing one-wide limiter and project sequencing unchanged.
- **D-4: repair the overlay fixture's evidence path.** Use its existing production-normalizer transcript pump and assert the complete receipt. No change to Grok's screen fallback, the overlay permission/working gates, timeout settings, provider capabilities or the fake's default rendering is warranted by the measured failure. An independently reproduced production defect discovered during implementation must be reported with its evidence before expanding this slice.
- **D-5: default Code and Review to Unit plus named affected integration classes.** Scope is a coverage obligation, not permission to omit cross-cutting capstones. Namespace/full-assembly runs require a concrete affected-contract rationale in the plan or brief; routine broad sweeps remain CI/nightly work. Do not silently edit `LandVerifyFilter` or the production verifier's behavior as part of a documentation policy change.
- **D-6: make duration identity class-aware and baselines explicit.** The present allowlist is entirely class names: accept exact simple or fully qualified class names, case-insensitively. No substring match against method arguments. Preserve expanded row reporting, and label aggregate class duration as summed TRX body time, not elapsed wall time.
- **D-7: keep other performance experiments outside this card.** No wider PTY/Git process limit, new Git concurrency lane, lazy database bootstrap, new scheduled task, full-suite refactor or blanket timeout change. Savings below are attributable to less repeated Git work and correct selection.

## Slices

### S1 — restore the three contract/guard checks

Files: `tests/Antiphon.Tests/Application/DelegationUnitTests.cs`, `tests/Antiphon.Tests/TestHelpers/DelegationHarnessCensusTests.cs`, `tests/Antiphon.Tests/Application/DelegateScriptLandStatusTests.cs`.

1. Keep explicit numeric assertions for `ReportUnsettled=22` through `ImportedIssueNeedsReview=28`. Remove the claim that ImportedIssueNeedsReview is forever the enum's last member. Replace it with an append-compatible check against a fixed name/value map of the **full legacy prefix 0 through 28**, including the earlier members: that prefix stays unchanged and members outside it must have values greater than 28. Do not derive the expected map from the enum under test, change the production enum or merely move the maximum assertion to 38. Later append operations must pass while renumbering a protected value or reusing a prefix slot fails.
2. Teach RuleB that a constructed `LandingSafetyHarness` owns graph registration, analogous to RuleC's existing `BridgeQueueHarness` recognition. Support the new controlled harness added in S4 as another explicit owner. Require actual construction/helper invocation, not an arbitrary comment mentioning the helper or a filename allowlist. Extract the small classification predicate only as needed for positive and negative source fixtures; do not build a general C# dependency crawler. Pin each recognized helper's call to `AddDelegationWorktreeGraph` and verify real DI resolution through the helper. Unrelated bare `AddScoped<AgentTaskDispatcher>` must still fail. Do not add redundant graph registrations to admission.
3. Add `[Category("Integration")]` to `DelegateScriptLandStatusTests`; keep its limiter. Execute its eleven `C467_V18_StatusAndAcceptance` scenarios, as well as the category guard. Newly controlled DB tests also receive Integration, not Unit.

Acceptance: the named enum test, all census rules and `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration` pass; valid helper use passes and bare registration still fails. No enum renumbering, duplicate service registrations or category exemptions.

### S2 — fix the overlay fixture with receipt evidence

Primary file: `tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs`. Read `SessionMessageQueueDeliveryVerificationTests`, `src/Antiphon.FakeClaude/Program.cs`, `src/Antiphon.Agents.Pty/SubmitEvidence.cs` and `ComposerDeliveryEvidence.cs` before implementing. The session runtime and PTY owners apply.

**Diagnosis and falsification experiment:**

| Run in this Plan task | Executed / pass / fail / skip | TRX body | Runner duration | Result |
|---|---:|---:|---:|---|
| Original behavior plus diagnostic snapshot in `finally` | 1 / 0 / 1 / 0 | 54.017s | 70.285s | Same ConflictException; raw snapshot proves overlay closed and exactly one body submitted. |
| Same case plus transcript file/pump and exact UserPrompt assertion | 1 / 1 / 0 / 0 | 4.440s | 21.465s | One complete `hello after overlay` UserPrompt, despite the same body remaining on screen. |

Initial isolated build: 0 errors, 143 warnings, 94.33 seconds. The second command rebuilt the diagnostic test before executing it. These are two diagnostic variants of **one** case, not two new coverage cases. No timing setting was changed. All temporary test changes were restored byte-for-byte; the restored file timestamp was refreshed. `bin-plan-c1384c67/` contains experimental binaries and must not be reused as a clean regression build.

Artifacts: `C:\Antiphon\worktrees\card-task-c1384c67\.antiphon\profile-c1384c67\` contains `build.log`, `overlay-diagnostic.trx`, `overlay-diagnostic.log`, `overlay-snapshot.json`, `overlay-pump.trx`, `overlay-pump.log`, and `overlay-pump-experiment.patch`. The patch is diagnostic evidence, not an implementation to apply unreviewed.

The mechanism is concrete:

1. The Grok `/usage` declaration takes `TypeLocalCommandAsync`, which deliberately expects no UserPrompt for that local command.
2. The measured overlay fragment triggers proactive recovery at `SessionMessageQueueService.cs:2487`; `TryDismissOverlayAsync:4036` performs the fresh transcript pull and idle/question guards, then Esc. The fake records `OVERLAY:closed` and the next body is actually submitted.
3. `FakeClaude.SubmitTurn` prints `SUBMITTED:<body>` and a response containing the same text. Without transcript output/ingestion, `WaitForTranscriptConfirmAsync:2616` reaches its unobservable fallback. `SubmitEvidence.IsEmptiedComposer` requires the head to disappear from the screen. At the deadline the head is still visible, so the explicit guard near line 2730 returns `NoSubmitOutput` and Enqueue throws. Enlarging the deadline cannot make this static echo disappear.
4. The pump adds the missing evidence; the transcript matcher runs before screen fallback and confirms the actual complete UserPrompt. This explains both red and green without changing recovery behavior.

Implementation:

- Give the overlay fake a unique `ANTIPHON_FAKE_TRANSCRIPT_PATH`; start the existing `PumpTranscriptAsync` after the session row exists and before typing. Keep Grok kind and pinned inbox backend. The pump must consume only complete JSONL lines through the production normalizer, without synthesizing a matching user row from the submitted body.
- Assert the overlay was open, closes once, and one exact body is submitted. Assert exactly one complete UserPrompt in the fake file **and** the destination session's stored transcript after the pre-send baseline, with the same normalized body. `/usage` must not become a model UserPrompt. Screen markers alone are insufficient.
- Own the pump Task: cancel and await it before deleting its rows, then kill/dispose the private runner and clean only this fixture's session, transcript, queue and temp files. Do not leave the existing fire-and-forget pump pattern in newly touched code.
- Keep the original timing configuration. Retain the working/permission-popup, unknown-provider, one-Esc, withheld-Enter and swallowed-Enter negative tests. Keep the unobservable Grok visible-body rejection test; adding a fake-specific screen exception would invalidate that protection.
- Extend the same receipt plumbing to the basic, large-multiline and batched success fixtures in this class where they currently lack a live UserPrompt feed. Preserve their exact-body, normalization and submit-count assertions. This is a secondary saving, not a reason to alter the deliberate every-Enter-swallowed negative.

Delivery evidence inventory for TestDesign: producer is Enqueue/flush; recipient is the fake session reached through real `AgentSessionRuntime -> DirectSessionRunnerClient -> SessionRunnerRuntime -> inbox PTY`; WhenIdle work persists as its actual queue/delivery identity while the existing Now overlay path is rowless. The normalizer pump substitutes only runner-to-server ingestion, not typing or recipient acceptance. Durable linkage is session ID plus queued message/delivery identity where applicable, pre-send sequence/time baseline and complete normalized body. Preserve and explicitly map existing busy-recipient, already-idle, interrupted-attempt and late-receipt coverage; no new outbox is introduced. A no-pump diagnostic fails on purpose and must not be added as an always-red suite test.

### S3 — remove duplicate verifier invocations

File: `tests/Antiphon.Tests/Application/AgentTaskLandVerifierTests.cs`.

Change V35 to a single cancellation test with no pass/fail argument rows and no call to V34. Keep V34's three real generated-project cases: SelectedPass, SelectedFailure and DoesNotExist. Keep private-output preservation, selected-marker, nonzero/fresh passing TRX and exited-child-journal assertions there. Keep all four V32 outcomes.

Expected class census: **10 -> 8 expanded cases**: V34 three, V35 one, V32 four. The profile assigns 15.93 seconds to the two removed calls; it is a historical saving estimate, not new measured performance.

### S4 — split landing protocol permutations from real Git semantics

Add `tests/Antiphon.Tests/TestHelpers/ControlledLandingGit.cs` and `LandingProtocolHarness.cs`; add `AgentTaskLandBoundaryControlledTests.cs`, `AgentTaskLandAdmissionControlledTests.cs`, and `AgentTaskLandConcurrencyControlledTests.cs` under Application. Edit only the argument allocation in the three original classes, retaining their real harness and the real-Git assertions below. Prefer an independent controlled harness to altering the shared `LandingSafetyHarness` used by crash/publication tests.

#### Controlled seam

- `LandingProtocolHarness` creates unique private directories and an isolated real test database, then registers its controlled `ILandingGit` through `AddDelegationWorktreeGraph`. It never constructs `LandingGitFixture`, `ScratchGitRepo`, `LandingGit`, or a real verifier. Its service factory must pass the injected Git instance everywhere, including `AgentTaskLandService`'s explicit argument. Existing DTOs/interfaces suffice; no production seam is needed.
- Keep production `RepositoryMutationLease` over a fixture-owned common directory so canonical-repository/source aliases contend on the same file lock. Keep real `AgentTaskLandService`, `AgentTaskLandingProtocol`, `AgentTaskDispatcher`, transactions, request/attempt updates and separate observer contexts. The dispatch launch queue has no consumer or hosted service, exactly as in the original admission fixture.
- The controlled Git model owns distinct source/target/remote/pin SHAs, registration identity, symbolic heads, sequencer/status data, ancestry and destination fingerprint. Methods derive results from that state. Supported command shapes are explicit; unexpected calls throw. There is no default-success or fallback-to-real-Git arm. Errors remain different from absence. `RunOwnedAsync`/`PushOwnedAsync` invoke the supplied started callback so durable intent/PID ordering is exercised; model process identities never refer to a live process.
- Use named one-shot hooks for first/second remote observation, ancestor check before rebase intent, verifier entry and each acknowledged phase. The existing `SaveFault.AfterAcknowledged` pattern supplies real committed-phase boundaries. Every injected case asserts that its named hook fired exactly once and checks the persisted checkpoint through a new DbContext.
- Register a controlled `IWorktreeManager` for incidental touch/create operations; its typed removal delegates to the real `GuardedWorktreeRemoval` with controlled Git and real `IWorktreeRemovalEvidence`. Never return canned Complete for cleanup. Operations that would enter legacy WorktreeManager/GitWorkspaceService process paths throw unless explicitly required and modeled. A factory/setup smoke test records zero native Git/verifier children for the controlled harness. Reference `LandingRemovalPolicyControlTests.RemovalFixture` for bounded command modeling, but do not copy its blanket-success or unsupported-owned-operation shortcuts into a full protocol model.
- Prepare Fresh/AlreadyPresent state by changing the model's remote containment. Prepare ResumePublication by running the real protocol against the model and faulting after committed LocalTargetAdvanced. Prepare CleanupRetry through real modeled publication, deliberate guarded-cleanup refusal and the real repost path. This is cheap once Git launches are gone and keeps the causal setup intact. Do not seed publication/receipt fields as a substitute for testing their creation.
- Preserve all original refusal reasons, phase assertions, attempt accounting, pending request, writer status and AgentId claims, verifier calls, mutation exclusions, source/target identity and sentinel contents. No oracle may compare only the fake's configured expected answer back to itself. Git process/ref/index semantics are proved by retained real cases, not claimed from these model assertions.

#### Complete allocation of the original 169 rows

Method names below omit the shared `C448_` prefix. Existing method identities and each argument tuple must be linked to its destination class in the implementation's coverage manifest. Controlled admission drops `Real` from its new method name; the original eight-capstone method retains it. Publish executed tuple counts from fresh TRX, not only source attributes.

| Original class / method family | Original rows | Controlled rows | Real-Git rows retained | Proof |
|---|---:|---:|---:|---|
| Boundary / V10_EachAcknowledgedBoundaryRechecksSource | 90 | all 90 | 15 specified below | Every reachable checkpoint/mutation combination remains a protocol assertion; native effects retained at selected boundaries. |
| Boundary / V10_VerificationCannotFreezeOldTaskCoordinates | 4 | all 4 | 0 | Separate committed DB changes to source-ref, source-path, target and repository; fresh reload, no target/push/delete afterward. |
| Boundary / V11_TargetMutationAfterFastForwardCannotBeAcknowledged | 4 | 0 | all 4 | Advance, switch, dirty and staged changes after real FF; no acknowledgement/publication; preserve actual HEAD/index/bytes. |
| Boundary / V32_EachRecoveryPinFailureStopsDependentMutation | 6 | 0 | all 6 | Three pins x failure/collision; expected-old-zero must not overwrite a racing ref. |
| Boundary / V11_TargetAdvanceUsesTheCapturedCheckoutAndOldSha | 2 | 0 | both | Checked-out HEAD/worktree update and non-checked-out expected-old-SHA race. |
| Boundary / V32_RebaseConfigurationCannotStashOrRewriteUnrelatedRefs | 1 | 0 | 1 | Hostile autoStash/updateRefs, different prepared SHA, unrelated ref and no stash. |
| Boundary / V36_CreationUsesTheRepositoryLeaseAndThreadsNestedOwnership | 1 | 0 | 1 | Real nested creation and shared lock ownership. |
| Boundary / V11_TargetSequencerBlocksPreparation | 6 | 0 | all 6 | MERGE_HEAD, CHERRY_PICK_HEAD, REVERT_HEAD, rebase-merge, rebase-apply, sequencer. |
| Boundary / V26_RecoveryPinsRemainPrerequisitesAfterVerification | 3 | 0 | all 3 | Actual source/target-before/prepared pin deletion must stop target advance and publication. |
| Admission / V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther | 24 | all 24 | 8 specified below | Two real dispatcher writer kinds x four modes x three orderings, durable claims and exactly one eventual attempt. |
| Concurrency / V36_SettlementLeasePrecedesItsFirstMutation | 2 | 0 | both | Settlement commits must acquire the lease before add/commit; actual dirty bytes and HEAD stay unchanged. |
| Concurrency / V36_SettlementAndChildMergeExcludeLandingInBothOrders | 4 | 0 | all 4 | Both orderings x root settlement/local child merge, actual shared Git side effects. |
| Concurrency / V14_EveryModeHonoursWriterAndLeaseHolds | 12 | all 12 | 4 specified below | Shared/follow-up/lease holds x every mode, stable attempt/request, release plus service recreation permits progress. |
| Concurrency / V10_VerificationDoesNotAuthorizeChangedSourceOrTarget | 10 | all 10 | 4 specified below | Source/target evidence changes while verifier is in flight; verifier success authorizes no stale mutation. |
| **Total** | **169** | **140** | **60** | **200 resulting cases; 109 full real-protocol executions replaced, 31 intentional cross-layer capstones.** |

The Boundary V10 matrix is **nine mutations x ten reachable boundary/mode pairs**. Mutations are `advance`, `dirty`, `staged`, `untracked`, `switch`, `metadata`, `metadata-path`, `metadata-target`, `metadata-repository`. Pairs are `remote` with contained=false and true, plus the eight later boundaries with contained=false. Keep all 90 in the controlled class. AlreadyPresent skips rebase/target/push, so creating contained=true cases at those unreachable boundaries would fabricate coverage. ResumePublication and CleanupRetry mode admission/holds stay in their complete 24/12 matrices; recovery/cleanup semantics retain the real suites listed below.

**The 15 real Boundary V10 tuples**, in `(boundary, change, contained)` order:

| Boundary | Retained mutation(s) |
|---|---|
| remote | `(advance,false)`, `(advance,true)`, `(switch,true)` |
| BeforeRebaseIntent | `(dirty,false)` |
| RebaseStarted | `(staged,false)` |
| Prepared | `(untracked,false)`, `(metadata,false)` |
| Verified | `(switch,false)`, `(metadata-path,false)` |
| TargetAdvanceStarted | `(advance,false)` |
| LocalTargetAdvanced | `(dirty,false)` |
| BeforePushIntent | `(staged,false)`, `(metadata-target,false)` |
| PushStarted | `(untracked,false)`, `(metadata-repository,false)` |

Every existing boundary and mutation type is represented in the real capstones, including the AlreadyPresent shortcut. Keep the current `fired`, exact reason, no-push/no-remove, retained source SHA/content/index and independent remote-source observer assertions. Early cases still forbid rebase; Prepared forbids verifier entry; TargetAdvanceStarted forbids FF/update-ref; LocalTargetAdvanced forbids a new fetch; BeforePushIntent forbids saved push intent. Controlled versions retain the same per-boundary assertions across **all** tuples.

**The eight real Admission tuples**, in `(writer, mode, order)` order:

| Mode | Shared writer | Follow-up writer |
|---|---|---|
| Fresh | land-first | dispatch-first |
| AlreadyPresent | dispatch-before-acquire | land-first |
| ResumePublication | dispatch-first | dispatch-before-acquire |
| CleanupRetry | land-first | dispatch-first |

These retain both writer kinds in every mode and all three orderings for each writer. The real fixture must continue manufacturing committed resume/publication/cleanup state through the production protocol, not by seeding receipts. `dispatch-before-acquire` must still stop before acquisition, commit the real competing dispatch, then release and prove the landing reloads that claim. Controlled hooks must not move this barrier until after the reload under test.

**The eight additional real Concurrency matrix tuples:** V14 keeps `(lease,Fresh)`, `(shared,AlreadyPresent)`, `(follow-up,ResumePublication)`, `(lease,CleanupRetry)`; V10 keeps `source`, `target`, `source-registration`, `target-switch`. Together with the six V36 cases this leaves 14 real cases. Keep the real lease alias check using the source path, post-hold service restart and exact next-attempt assertions.

#### Dangerous coverage that stays real and outside matrix reduction

Do not migrate or trim `AgentTaskLandRecoveryTests` (including real worker deaths C03/C05/C09/C12/C14/C16/C17, acknowledgement gaps and interrupted rebase), `AgentTaskLandCheckpointMatrixTests`, `AgentTaskLandPersistenceFailureTests`, `AgentTaskLandPublicationTests`, `AgentTaskLandCleanupSafetyTests`, `AgentTaskLandRemovalMatrixTests`, `AgentTaskLandIdentityMatrixTests`, `AgentTaskLandPreparationIdentityTests`, or `AgentTaskLandRefusedRetryTests`.

These own crash/restart/save/commit boundaries, publication confirmation versus push exit, competing remote movement, cleanup retry, stale identity, manual work preservation and expected-SHA deletion. `LandingGitTests` keeps real component proofs for push endpoint/refspec, collision, tri-state errors and registration identity. Their existing controls remain authoritative; TestDesign must identify selectors affected by the new controlled class names. A test-only fixture refactor must not silently reduce those suites' assertions or route their crash workers through a fake.

Avoid changing the shared real harness. If sharing a small helper becomes necessary, declare that deviation and expand affected-class regression to every consumer of that helper's behavior, particularly real crash/recovery and cleanup cases. That is an explicit cross-cutting scope, not permission to run the whole assembly by habit.

### S5 — make scoped regression the dispatch default

Files: `docs/testing-and-build.md`, `server/Bundles/stage-code.md`, `server/Bundles/stage-review.md`.

- Put a short, prominent default recipe in the guide and both bundles: build once into producer-owned isolated output; execute the Unit lane; execute named affected integration classes together where feasible; inspect fresh TRX for each intended class/method and nonzero counts. Reference the guide for pinned syntax rather than duplicating a long operational manual.
- Require a coverage-to-class list in the brief/verification section and require Code/Review to report the filters and actual expanded counts. Unit-only is insufficient for changes to native delivery, landing, leases or persistence. Review reruns the claimed **scoped** ordinary checks and judges Mutation evidence read-only; it does not expand to broad Application/full assembly merely because it is Review.
- A broad exception names the affected cross-cutting invariant, the classes that cannot be bounded and the expected cost before the run. Missing rationale is a review defect. Do not add an approval question for already authorized affected-class verification. This is bundle/doc enforcement, not a new runtime scheduler or server-side test-filter validator.
- Keep per-PC method-only Mutation execution and project sequencing rules. CI/nightly keep their existing broad responsibility. Documentation-only follow-ups can use their named contract checks without rebuilding unrelated native code.
- Update Gotcha #74 and Fast lane together so no active paragraph still treats 3,893 tests / 25.5 minutes as current. Retain that dated history as history if useful; explicitly state that a newer **492-case slice** took 94m 23.46s raw and that a current full-suite wall time was not measured. The old 35-minute observation threshold must not remain a suggested kill deadline.

Use the dated 2026-09-10 profile numbers together: **7,072 discovered cases**, 1,992 in the clean Unit selection (70.29s outer wall time; 1,988 pass, three fail, one skip), and 492 in the broad selection (491 pass, one fail). The broad 91m 35.3s adjusted figure substitutes one clean case for a diagnostic pause; it is arithmetic, not a clean rerun. Shared-host interference affected the broad sample. These limitations travel with the baseline.

The actionable command shape (run projects sequentially; use a fresh results directory per invocation):

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c475/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c475/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c475-unit
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c475/ -- --treenode-filter '/*/*/(AgentTaskLandBoundaryControlledTests*)|(AgentTaskLandAdmissionControlledTests*)|(AgentTaskLandConcurrencyControlledTests*)/*' --report-trx --report-trx-filename controlled.trx --results-directory .antiphon/c475-controlled
```

The directory names above are examples: timestamp or otherwise create fresh empty directories for actual invocations. Do not combine UID and tree selectors. Keep each OR operand parenthesized with its trailing wildcard and check actual selection; `--list-tests` is not execution evidence on this pinned runner. Unit and named integrations may be separate invocations of the same built output; do not invent unverified mixed category/class filter syntax.

### S6 — repair and rebaseline the duration tripwire

Files: `scripts/test-duration-tripwire.ps1`, `tests/Antiphon.Tests/slow-tests-allowlist.txt`, the timing sections of `docs/testing-and-build.md`; add a narrowly scoped process test such as `tests/Antiphon.Tests/TestHelpers/TestDurationTripwireTests.cs` with Integration category and `ProcessSpawnLimit`.

Build a namespace-aware TestDefinition index keyed by UnitTest `id`, then join every UnitTestResult's `testId`. Read `TestMethod@className`, retaining both full and simple class identity; preserve the result's expanded display name for reporting. Namespaced and namespace-free TRX must work. Match allowlist entries exactly against either class form using OrdinalIgnoreCase. Update the allowlist header to describe class matching and comment each newly accepted slow class with its concrete real-process reason.

Do not join by display name or executionId, split a parameterized test name on dots/commas, or deduplicate results by method: equal display names can belong to different classes, and every expanded row contributes cost. A missing definition/class must be visibly unresolved and never be exempted by an argument containing an allowlisted class name. Invalid duration data must be reported as invalid input, not silently discarded to produce green. Preserve exit 0 for no unlisted slow rows and exit 1 for hits; malformed input must also be nonzero.

Report slow expanded rows with full class+method identity and duration, plus per-class expanded count, slow-row count and summed body seconds. Apply the existing >=5.0-second threshold; 4.999s is below, exactly 5s is included. Summed body durations can overlap and must not be called elapsed runtime.

Baseline reproduced by `rebaseline.py` / `tripwire-rebaseline.json` in this task's evidence directory, against the saved original TRX and current unmodified allowlist:

| Sample | Expanded rows | >=5s rows | Old hits | Class-aware hits | Remaining unlisted |
|---|---:|---:|---:|---:|---|
| broad | 492 | 202 | 202 | 174 | Boundary 116, Admission 24, Concurrency 25, Verifier 9 |
| clean Unit | 1,992 | 2 | 2 | 2 | RunnerClaudeAdapterEffortPromptTests 2 |

The raw broad class body totals for those four landing classes are Boundary 2,657.697s, Admission 1,400.060s, Concurrency 781.997s and Verifier 138.190s. Admission includes the diagnostic pause; do not present that total as the corrected lifecycle baseline. The four retained real-Git/verifier classes may receive explicit class allowlist entries with the capstone reasons above after the post-change run. The new controlled classes receive no blanket slow exemption. The two Unit hits remain visible unless their owner supplies a concrete justified exception; changing matching must not blanket-whitelist the whole landing namespace or Unit lane.

Record two versions of evidence: corrected matching applied to historical TRX, and fresh post-change TRX/case census for the split classes and verifier. Archive metadata with commit, filter, environment, outcomes, body totals and elapsed command time. Do not overwrite the original profiling files or infer a new whole-suite duration from a partial run.

## Verification handoff requirements

This is input to a **separate TestDesign** stage, not a completed `## Verification design`. That stage must append the standard structure and commit/push it before Code. Read the touched bodies and all fixtures before assigning executable V/R/PC selectors. In particular:

- Cover the three repaired contract/guard failures and eleven script-status scenarios; negative source-census cases must still reject missing ownership.
- Cover overlay receipt from the real queue/PTY recipient, including exact full text, no duplicate submit, one Esc and local-command nonreceipt. Map the existing working/question/unknown-provider/recovery-disabled/one-shot gates, busy versus already eligible queue paths, crash recovery and late-receipt guards. The transcript-pump substitution and its limits must be explicit. If no production delivery guard changes, explain which existing controls supply that evidence instead of manufacturing redundant mutations.
- Verify the three controlled classes execute 94/24/22 rows, and the three real classes execute 38/8/14 rows. Require a tuple-level manifest linking the old test identity to controlled coverage and any retained real capstone; a coverage count alone does not prove equivalence. The fake's own strict unexpected-command, independent source/target state, callback and no-real-process behavior need contract checks.
- Keep per-boundary negative mutation assertions and independent real repository observations. Inventory independently bypassable source/coordinate/pin/target/lease/publication/deletion guards and map existing C448 controls to surviving exact methods; add missing controls rather than reusing one PC for unrelated guards. Replaying a canned fake result is not positive-control evidence.
- Verify the verifier class's 3+1+4 census and all old V34/V35 invariants. Verify tripwire namespaced/no-namespace, parameterized rows, same method in different classes, exact simple/full class, argument-only spoof, unresolved definition, malformed duration and threshold/exit behavior using temporary TRX fixtures through the real script.
- Guide/bundle edits are low-impact prose; inspect their agreement and run the existing `InstructionBundleTests` if bundle composition changes. Do not add a brittle wording test just to mirror prose. Require Code/Review dispatch evidence to actually name scoped filters.
- Require affected-class regression (Unit plus these split classes, verifier, script status, duration script and the touched PTY success class). Expand to the real crash/publication/cleanup classes only when their shared helpers or production behavior change; preserve all such suites for nightly/full integration regardless. Run `Antiphon.Agents.Pty.Tests` after `Antiphon.Tests` if a touched fake/transport file requires its native contract classes.
- Implementation ordinary V/R evidence belongs to Code; deliberate red/restore/green belongs to Mutation. The safety-layer substitution warrants `review-required: yes` through Mutation. Code must report original landing owner, exact branch/commit/worktree, plan and evidence paths. Expected restart is **none** under this plan.

## Cost and acceptance

`coverage_cost.py` / `coverage-cost.json` in this task's evidence directory join the exact retained tuples to the corrected profiling rows. The selected 60 real rows previously consumed **1,534.237s (25m 34.2s)**; the 109 replaced real rows consumed **3,137.371s (52m 17.4s)**. Together they reproduce the report's 77m 51.6s matrix total. This is a historical allocation, not a benchmark of the new harness or a promise of 52 minutes saved: the 140 controlled rows and new setup still cost time.

Planning estimate for ordinary Code verification: one isolated build 1.5-3 minutes; Unit/startup 1-2 minutes; controlled classes 2-5 minutes; retained real matrices 26-35 minutes; verifier and touched script/PTY classes 5-10 minutes. Total **35.5-55 minutes**, with shared invocations avoiding repeated discovery. Untouched crash/publication suites are not priced into this narrow default. If helpers change, TestDesign must price their added scope explicitly. Mutation cost is separate and must be quantified after its executable guard inventory; it cannot honestly be called zero or priced from these ordinary-run totals.

The split should remove native Git execution from all 140 controlled rows and materially reduce the three-class cost under comparable load. Measure once after implementation using the same tuple identities, fresh TRX and lifecycle spans; investigate if a controlled matrix still takes minutes due to accidental real process calls. Do not impose a timing assertion that can be satisfied by skipping rows. No claimed suite-wide or parallelism savings are part of acceptance.

The plan is complete when committed and pushed with `next: test-design`. No implementation or broad regression sweep was performed in this Plan task; the two overlay diagnostic variants above are the only executed test cases.

## Verification design

TestDesign task: `80852ec0`. Inspected landed plan/source at `dc6af1823a29e6a99a0d4570febc2dc3be6bf42d` on 2026-09-10. This appendix adds verification; S1-S6, all 169 original tuple identities, the 140 controlled allocation and 60 real-Git capstones remain binding. New supporting tests and the 72 existing control-wrapper rows are additional to the 200 allocated matrix cases.

**Execution ownership:** Code implements tests and runs V/R; **a separate Mutation role** runs every PC below; **review-required: yes**, after Mutation. No deploy/restart is required. The final handoff carries the original Code task ID, full implementation SHA, branch, retained worktree, this artifact, actual filters/counts, evidence paths and `restart: none`. Historical C448 evidence explains a control; it does not earn current-SHA Mutation credit.

### Inspection

These are bodies and setup/teardown actually read, not a file-search census:

| Bodies/fixtures read | Boundaries and verification |
|---|---|
| All of AgentTaskLandBoundaryTests, AgentTaskLandAdmissionTests, AgentTaskLandConcurrencyTests, AgentTaskLandVerifierTests | Every existing argument, barrier, refusal/phase/trace assertion, attempt check and independent remote observer; V-3..V-6, R-3..R-6. |
| LandingSafetyHarness and LandingGitFixture, including BuildServices/CreateLand, SaveFault/TransactionFault, crash-worker dispatch, FixtureGit owned callbacks and disposal | Real DB commit boundaries, explicit Git injection, private roots, child ownership; V-3/V-4. These helpers remain unchanged. |
| LandingSourceBoundaryControlTests and LandingAdmissionControlTests, all wrappers | 30 source-boundary + 42 admission wrapper rows; V-5 and the explicit remap below. Attributes alone do not change a wrapper's direct method call. |
| LandingIdentityControlTests and its InspectionFixture/Replies; LandingRemovalPolicyControlTests and its RemovalFixture; DelegationTestServices/Tests; TestDbFixture | Nearest fixtures for new model/contract tests; unsupported replies, identity versus SHA, crossed authority, template-clone setup and TryAdd behavior. The removal fixture's default-success shortcuts cannot become the full protocol model. |
| Entire SessionMessageQueuePtyIntegrationTests including PumpTranscriptAsync/UserPromptsIn/CleanupAsync; DirectSessionRunnerClient launch, send, transcript and disposal paths | Real queue/runtime/runner/inbox transport; fake JSONL versus server storage; missing pump, cancellation and sequence-floor collisions; V-7/V-8. |
| SessionMessageQueueDeliveryVerificationTests: observable harness; stale-body/screen-only/Grok cases; local-command/overlay/Now-lock cases; completeness, attempt, late/grace and timestamp cases named below; all SessionMessageQueueInterruptedAttemptTests | Existing negative and recovery oracles; V-9/R-7..R-9. BridgeQueueHarness registration, OnSubmitted, seed/insert helpers, runner catch-up and disposal were read. Its adapter-generated receipt is a substitute, not native proof. |
| UnmarkedWaitingContractTests body in DelegationUnitTests; all DelegationHarnessCensusTests, TestLaneCategoryGuardTests, ProcessSpawnLimitTests; DelegateScriptLandStatusTests/Stub and DelegateScriptRunner | Full-prefix compatibility, honest graph ownership, lane/process requirements, all 11 HTTP-script scenarios; V-1/V-2. |
| test-duration-tripwire.ps1 and allowlist; AgentTaskLandVerificationEvidenceTests; stage-code/test-design/mutation/review; testing owner | TRX identity, counters, scoped execution and role boundaries; V-6/V-10..V-12. |
| AgentTaskLandingProtocol Run/RecheckSource/CheckTarget/Owned/Transition/Cleanup; AgentTaskLandService admission/FindWriter; GuardedWorktreeRemoval; LandingGit identity/observation/pin; LandingVerifier | The actual independent guards used to define compiling mutants, including guards hidden behind the controlled I/O seam. |
| SessionMessageQueueService Deliver/confirm/dismiss branches; SubmitEvidence, ComposerDeliveryEvidence; FakeClaude SubmitTurn; GrokQuestionPopup and its tests | Transcript-first diagnosis and dormant question recognition limitation below. Relevant session-runtime and ConPTY owner contracts were read. |

The full original profile and the Plan's diagnostic patch were read. The observed overlay red/green remains **54.017s failed / 4.440s passed**, one case each, with unchanged settings. This TestDesign does not rerun that experiment or label it a production Grok receipt test.

Inspection found two additional verification details:

- The existing pump starts at sequence 1. The batch fixture already seeds sequence 2; start from the destination's persisted maximum, not a shared constant. Resume ingestion using the saved cursor/UUIDs so a restart cannot duplicate a committed record.
- GrokQuestionPopup currently has **two empty chrome constants**; IsPresent always returns false. The current working-after-catch-up guard protects permission dialogs. Do not claim the dormant popup-specific branches have native acceptance coverage, invent chrome, or require a live-model canary for this fixture repair. The no-guess classifier contract is controlled below; enabling popup recognition belongs to its own measured change.

### Delivery inventory

No business outbox is added or changed. The landing fixtures keep ReplyTo.None and no launch consumer: their publication state is **not** a caller-delivery assertion. Existing C467 land-notification producer/outbox/receipt behavior is outside this change and must not be inferred from C448 success.

| Path / durable linkage | Producer and handoffs | Persistence / recovery | Observable recipient evidence |
|---|---|---|---|
| Now basic, overlay and large body: session ID + pre-send sequence/timestamp + exact normalized body (no queue row) | EnqueueAsync(Now) -> real SessionMessageQueueService -> AgentSessionRuntime -> DirectSessionRunnerClient -> real SessionRunnerRuntime -> inbox PTY -> FakeClaude | Now is rowless; a thrown/canceled call is not a durable retry promise. Before-write failure must produce no receipt/submit. After-submit ambiguity is resolved by recipient JSONL/storage; do not automatically resend the body. | LastDelivery.ConfirmedBy=Transcript and exactly one complete matching UserPrompt in **both** fake JSONL and the destination DB after the baseline. /usage is local output, never that UserPrompt. |
| WhenIdle single and batch: SessionQueuedMessage IDs + session + sequence/conversation key + LastDeliveryStartedAt/LastDeliveryBaselineSequence | Enqueue persists Pending -> idle/turn-end flush claims attempt -> same real transport -> fake JSONL -> pump -> DB -> confirmation | Queue insert failure leaves no row and no input. A committed pending row survives service recreation. A committed attempt retains its floor/attempt number across interrupted confirmation; recovery late-confirms before retype, or uses Enter-only if the composer still holds the body. | Busy case remains pending with zero submitted bodies before TurnEnd; eligible case submits without another turn event. Final body/order/one-submit and complete destination UserPrompt are decisive; Sent alone is insufficient. |
| Test ingestion: fake record UUID + destination session + committed cursor/sequence | FakeClaude appends complete JSONL -> owned PumpTranscriptAsync -> production TranscriptNormalizer -> TranscriptEntries | Partial final line is retained; cursor advances only after complete-line persistence. Transient read/save failure retries uncommitted data. Resume must not replay committed UUIDs. Cancel and await the pump before row/file disposal, including assertion/error exits. | Original fake UUID/text/timestamp preserved, one normalized UserPrompt at destination, monotonic sequence beyond any seeded rows; no prompt manufactured from the enqueue argument. |

**V-8 recovery fixtures (new, isolated, same real queue and DirectSessionRunnerClient):** implement `SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(string cut)` with six explicit cuts: `insert-fails`, `pending-before-flush`, `attempt-before-write`, `body-before-enter`, `recipient-before-ingestion`, `receipt-before-verdict`. Use a test-owned EF interceptor for insert/attempt saves, an ISessionRunnerClient forwarding decorator with one-shot write barriers, and a pauseable pump. These substitute fault timing only; successful writes still go to the real PTY. Catch the injected exception/cancellation and await the old queue invocation before constructing a fresh queue service against the same DB/session. Retain the real runner until teardown.

For insert-fails, assert no persisted message, zero writes and no UserPrompt; a subsequent explicit enqueue must produce one receipt. For pending-before-flush, pause by marking the session busy, recreate the service, then insert TurnEnd and flush the same row. For attempt-before-write, interrupt only after the baseline/attempt commit and before forwarding bytes; assert stored attempt/floor, then recover that row. For body-before-enter, assert the body is still on screen, recreate, and require Enter-only plus one UserPrompt. For recipient-before-ingestion, pause before reading the **real** file, then restart ingestion and recover with no additional input. For receipt-before-verdict, fault the verdict save after receipt is stored and require late confirmation of the same row without retyping. Age recovery metadata in a fixture-owned DB only where the existing interrupted-window predicate requires it; preserve the actual attempt identity. This models server-service loss, not OS power-loss durability or runner adoption.

Add `C475_AlreadyIdleWhenIdleHasRecipientReceipt` as the explicit eligible-recipient companion; the old basic test's name says Queued but it actually uses Now. Extend the existing batched test to assert pending/no input while busy, two exact queue IDs, then one complete formatted batch after TurnEnd. The four existing success methods retain their exact-body and protocol assertions. Use a **separate** plumbing class so the original PTY class remains eight cases.

Add `C475_PumpPersistsCompleteLinesOnce(string cut)` with `partial-line`, `read-fails`, `save-fails`, `restart-after-commit`, `seeded-sequence`: use unique real-format JSONL, production normalization, actual DB writes, UUID/cursor tracking and two destination sessions (the other session must remain untouched). Partial-line must cause no write until its newline. Save failure must not consume the line; restart-after-commit must not duplicate it. Add `C475_PumpIsJoinedBeforeFixtureDisposal`: hold persistence at a test barrier, initiate cleanup, assert cleanup is still waiting and rows exist; release the barrier and require pump completion before deletion. Inject an assertion-path exception inside an owned try/finally to exercise that exit too. These are Integration tests; the class that launches PTY children carries ProcessSpawnLimit.

Add `C475_ProactiveAndReactiveRecoveryShareOneEscBudget` using the real forwarding client: open the measured fake overlay, then have the forwarding decorator withhold body writes after its first Esc (an explicit transport fault) while retaining a valid idle transcript. Capture the delivery refusal and assert one Esc across proactive and reactive paths, zero submitting Enter and zero UserPrompts. Add `C475_MultilineWritesKeepPasteMarkers`: record forwarded payloads before sending them to the PTY; assert LF-normalized text inside bracketed-paste markers, a separate CR call, and one complete recipient receipt. Do not credit the recipient body alone for the wire-shape assertions.

**Limits:** the pump substitutes runner-to-server transport/discovery; it proves neither SSE reconnect nor production transcript-tail binding. BridgeQueueHarness simulates the recipient. Controlled Git proves protocol responses/transactions, not Git ref/index/OS process behavior. Retained native capstones provide that latter evidence. No fake broker, provider home, production runner, live destination, or new notification path is involved.

### Proves it works now

All new names below are requirements for Code, not claims that the tests already exist.

| ID | Behavior / layer | Executable selection or review | Required result |
|---|---|---|---|
| V-1 | S1 compatibility/census; Unit | UnmarkedWaitingContractTests, DelegationHarnessCensusTests, DelegationTestServicesTests, TestLaneCategoryGuardTests, ProcessSpawnLimitTests through Unit lane | Three original Unit failures fixed; fixed literal map covers all names 0..28; extension >28 accepted; helper-negative fixtures still fail classification; no lane exemptions. |
| V-2 | Script status; process integration | DelegateScriptLandStatusTests.C467_V18_StatusAndAcceptance | **11** passed; publication/receipt/held facts and 202 acceptance assertions retained. |
| V-3 | Full tuple equivalence | Three controlled and three original matrix classes; manifest audit described below | Controlled **94/24/22**; real **38/8/14**, no missing/duplicate original tuple, all designated capstones executed. |
| V-4 | Model/DI/DB/lease seam | New LandingProtocolHarnessTests and ControlledLandingGitTests, plus LandingProtocolGuardTests | Strict unsupported-call refusal; independent state; no native Git/verifier; callbacks and acknowledged hooks observed; real DB/lease/removal checks. |
| V-5 | Mutation wrapper compatibility | LandingSourceBoundaryControlTests and LandingAdmissionControlTests | **30 + 42** rows, unchanged wrapper method identities/arguments, now controlled; no silent legacy real-Git setup. |
| V-6 | Real verifier | AgentTaskLandVerifierTests plus Unit AgentTaskLandVerificationEvidenceTests | **8 = 3 V34 + 1 V35 + 4 V32**; pass/fail/no-selection, cancellation, output preservation, selection marker, no in-tree obj, exited child journals; nine counter rows remain. |
| V-7 | Native recipient acceptance | SessionMessageQueuePtyIntegrationTests | All **8** original cases pass, including both argv backend rows; four success paths have complete JSONL+DB receipt; swallowed/all-swallowed negatives retained. |
| V-8 | Receipt plumbing and recovery | SessionQueueReceiptPlumbingTests: six recovery cuts, one eligible companion, five pump cuts, disposal, one-shot recovery and paste methods | **15** new cases pass; no transport ack/Sent-only success and no pump task left running. |
| V-9 | Existing delivery safety plus two missing pins | SessionMessageQueueDeliveryVerificationTests exact methods listed below; SessionMessageQueueInterruptedAttemptTests (all 11); new C475_OverlayRecoveryDisabledNeverEscapes and C475_OverlayRecoveryPullsBeforeWorkingDecision | Each intended method executes; no premature Enter/Esc, no stale/truncated confirmation, no duplicate retype, durable attempt preservation. |
| V-10 | TRX script contract | New TestDurationTripwireTests methods specified below; real pwsh script with private TRX/allowlist files | Exact class joins and thresholds; malformed input nonzero; every expanded row retained. Integration+limiter. |
| V-11 | Historical and new duration evidence | Run fixed tripwire on saved broad/unit-clean TRX with the **original** allowlist copy; then on fresh scoped evidence with final allowlist | Historical 492/202/174 and 1992/2/2 reproduced; new class/body/elapsed totals separately labeled, raw originals unchanged. |
| V-12 | Scoped policy | Review S5's guide and both bundles side by side; Unit includes existing bundle tests | Same Unit+touched-class rule, rationale/cost for broad exceptions, Code->Mutation->required Review, current dated history, no production LandVerifyFilter or runtime scheduler change. No prose-mirroring tests. |
| V-13 | Final ordinary scope | Commands below, fresh output/TRX and per-ID ledger | All V/R evidence recorded at the committed implementation; expected skips remain explicit; no required native test counted as passed when skipped. |

**V-3 manifest:** Code writes a UTF-8 CSV/JSON under its evidence directory before moving rows, with original class/method/typed argument tuple, destination controlled identity, retained-real identity or explicit non-capstone, layer and decisive assertions. Build the expected 169-row set from the landed source at the SHA above, not the newly edited source. Join actual TRX TestDefinitions and expanded argument identities to that set. Compare sets, not only counts. Old V10's 90 rows are nine mutations times ten reachable boundary/mode pairs; no contained=true later-phase cases are invented. Source and target hashes/status, pending request and attempts, verifier calls, fired-once hooks, sentinels and independent remote-source observations must remain in the appropriate layer. Additional supporting cases cannot fill a missing matrix tuple.

**V-9 exact existing methods in SessionMessageQueueDeliveryVerificationTests:**

- `Wedged_composer_withholds_enter_reverts_message_and_restarts_always_on_agent`
- `A_WritesUserPrompt_false_command_sends_exactly_one_Enter_and_skips_confirm`
- `NoComposerEvidence_on_a_working_session_sends_no_Esc`
- `NoComposerEvidence_on_idle_Supported_kind_sends_one_Esc_retypes_and_succeeds`
- `NoComposerEvidence_on_idle_Unknown_kind_sends_no_Esc`
- `Overlay_recovery_is_one_shot_two_evidence_failures_produce_one_Esc`
- `Proactive_detector_Escs_before_typing_when_a_measured_fragment_is_visible`
- `Proactive_detector_does_not_Esc_an_unmeasured_modal`
- `Mode_Now_waits_for_the_per_session_lock`
- `Grok_unobservable_redraw_with_body_visible_is_NoSubmitOutput_not_Sent`
- `Grok_unobservable_transient_empty_frame_does_not_latch_emptied_composer`
- `Grok_unobservable_sustained_composer_departure_confirms_by_screen`
- `Grok_matching_UserPrompt_still_wins_as_transcript_proof`
- `Grok_observable_queued_body_without_UserPrompt_reverts_to_Pending`
- `A_stale_record_alone_never_produces_delivered`
- `A_clipped_prefix_parks_as_truncated_not_sent`
- `Late_confirm_does_not_promote_a_truncated_body_to_sent`
- `Late_confirm_marks_the_message_sent_with_zero_writes_to_the_terminal`
- `Queue_enqueue_does_not_confirm_delivery`
- `A_failed_delivery_whose_body_landed_anyway_is_never_typed_twice`
- `Attempt_metadata_survives_the_revert_a_failed_delivery_does`
- `A_working_session_is_not_killed_when_the_record_never_arrives`
- `A_record_that_lands_just_after_the_deadline_confirms_instead_of_killing`
- `Card0164_unobservable_old_timestamp_row_does_not_confirm`
- `Card0164_unobservable_null_timestamp_row_does_not_confirm`
- `Mode_Now_response_carries_a_transcript_confirmed_receipt`

The disabled pin uses BridgeQueueHarness.ConfigureDeliveryVerification to set OverlayRecoveryEnabled=false, Grok, a measured overlay and no composer echo: no Esc or submit. The fresh-pull pin gives EmptyRunnerClient a real-format newer AssistantText entry while the DB still looks idle; a Now send must catch it up and withhold Esc. Do not simulate that by pre-seeding Working, which would miss the ordering defect. No change to shared BridgeQueueHarness is needed.

**V-10 new methods and exact data:**

- `C475_ClassIdentityJoin(string shape)`: namespace/no-namespace x exact-simple/mixed-case-full/no-match (6 rows); two definitions share a parameterized display name but have different testId/class. Reverse definition order and use unrelated executionIds. Only the class allowlisted is exempt.
- `C475_ArgumentsCannotWhitelistAClass`: slow unlisted class, argument containing an allowlisted class, and class-prefix/suffix lookalikes; expect all three hits.
- `C475_UnresolvedIdentityCannotBeExempted(string shape)`: missing definition, missing TestMethod, missing className (3); output visibly unresolved and nonzero with argument spoof.
- `C475_ExpandedRowsAndThresholdAreExact`: same method has 4.999/5.000/5.001 seconds, plus another equal-display row with another testId at 7 seconds; four expanded rows, three slow hits, class sum **22.000 seconds**, not deduplicated/elapsed; empty allowlist. Companion allowlisting that exact class exits 0.
- `C475_InvalidInputIsNotGreen(string shape)`: malformed XML, invalid duration, missing duration, negative duration (4). Nonzero diagnostic; never zero slow tests by omission. Negative elapsed duration is invalid data.
- `C475_SimpleAndFullNamesAreExact`: separate invocations for exact simple and exact fully-qualified (mixed case), substring and argument-only names; 0 only for the exact matches.

These total **16** test cases (individual methods may invoke several private script fixtures); retain actual process exit and stdout/stderr, never a piped tail's status.

### Guards the regression

| ID | Regression and decisive assertion | Evidence |
|---|---|---|
| R-1 | Future enum append passes; each legacy name remains its literal value; alias in 0..28 is rejected. Census accepts actual helper ownership, rejects mention-only/bare registration. | V-1; PC inventory contract rows |
| R-2 | Lane/limiter drift cannot hide the eleven script rows or move DB-controlled matrices into Unit. | V-1/V-2/V-4 |
| R-3 | A deleted tuple or substituted fake oracle cannot appear as a faster green matrix. | V-3 set equality, V-4 model contract, V-5 wrapper census |
| R-4 | A later refusal cannot conceal bypass of an earlier source/target barrier. | Original immediate phase/forbidden-next-command assertions in all controlled rows and retained real cases |
| R-5 | Shared/follow-up/lease holds retain pending state and spend zero attempts in all modes; release/recreation spends exactly one. | V-3/V-5 |
| R-6 | Verifier dedup cannot remove cancellation, private-file, selection, executed/passed counter or native journal proof. | V-6, original method names preserved |
| R-7 | Raw SUBMITTED/overlay-close and screen redraw cannot certify complete delivery. | V-7, V-9 Grok visible-body negative; DB+file exact-body assertions |
| R-8 | Busy versus eligible delivery, failure at a persistence/transport handoff and late ingestion cannot lose or duplicate work. | V-8 recovery cuts; V-9 interrupted/late controls |
| R-9 | Partial JSONL, wrong destination, seeded sequence or failed/canceled pump cannot silently consume/duplicate a receipt or outlive teardown. | V-8 pump/disposal controls |
| R-10 | Parameterized test names cannot evade duration enforcement; missing/invalid evidence cannot produce green. | V-10/V-11 |
| R-11 | Default dispatch scopes cannot expand silently to a full namespace/assembly, or skip affected native evidence. | V-12/V-13 actual command/count ledger |
| R-12 | Untouched crash/retry/publication/deletion proofs remain real. | Compare unchanged protected suite/helper files; V-3 real allocation; explicit exclusions below |

### Guard inventory

The inventory below covers the guards whose proof is changed or relied on by S1-S6: the substitute's trust boundary, all moved matrix assertions, retained capstones, receipt plumbing and the script/census gates. Each numbered G has exactly one distinct PC with the same number. Reusing an exact test method for different compiling defects is permitted; merging independently bypassable defects into one PC is not. Ordinary R rows may reuse V evidence.

This is not a claim that every unrelated guard in the application was changed. The untouched crash/receipt-policy suites listed under Out of scope retain their existing C448/C467 inventories. No unsupported fake result, exception during setup, missing method, skipped test, compiler error or timeout earns a successful control.



| Guard | Plan / invariant | Distinct control |
|---|---|---|
| G-1 | S1: Legacy AttentionKind name/value prefix is immutable | PC-1 |
| G-2 | S1: New names cannot reuse legacy slots | PC-2 |
| G-3 | S1: Bare dispatcher registration is rejected | PC-3 |
| G-4 | S1: Helper text/comments cannot grant ownership | PC-4 |
| G-5 | S1: Real helper must still register the graph | PC-5 |
| G-6 | S1: Controlled helper must still register the graph | PC-6 |
| G-7 | S1: Process fixture remains Integration | PC-7 |
| G-8 | S1/S4: One-wide native process safety remains | PC-8 |
| G-9 | S4: New process script fixtures remain limited | PC-9 |
| G-10 | S4: Every allocated matrix tuple remains executed | PC-10 |
| G-11 | S4: Unsupported commands fail explicitly | PC-11 |
| G-12 | S4: Unsupported helper paths cannot launch native Git | PC-12 |
| G-13 | S4: All service entry points use the injected model | PC-13 |
| G-14 | S4: Source, target, remote and pin state are independent | PC-14 |
| G-15 | S4: Query error remains different from absence | PC-15 |
| G-16 | S4: Owned rebase/mutation start callback is awaited once | PC-16 |
| G-17 | S4: Owned push start callback is awaited once | PC-17 |
| G-18 | S4: Save hooks mean committed acknowledgement | PC-18 |
| G-19 | S4: Cleanup uses actual guarded removal and durable evidence | PC-19 |
| G-20 | S4: Canonical repository and source aliases share lease | PC-20 |
| G-21 | S4: Recreated services read committed request/attempt state | PC-21 |
| G-22 | S4: Fresh source fence at remote | PC-22 |
| G-23 | S4: Fresh source fence at BeforeRebaseIntent | PC-23 |
| G-24 | S4: Fresh source fence at RebaseStarted | PC-24 |
| G-25 | S4: Fresh source fence at Prepared | PC-25 |
| G-26 | S4: Fresh source fence at Verified | PC-26 |
| G-27 | S4: Fresh source fence at TargetAdvanceStarted | PC-27 |
| G-28 | S4: Fresh source fence at LocalTargetAdvanced | PC-28 |
| G-29 | S4: Fresh source fence at BeforePushIntent | PC-29 |
| G-30 | S4: Fresh source fence at PushStarted | PC-30 |
| G-31 | S4: Fresh task repository must match operation | PC-31 |
| G-32 | S4: Fresh task path must match operation | PC-32 |
| G-33 | S4: Fresh task source_ref must match operation | PC-33 |
| G-34 | S4: Fresh task target_ref must match operation | PC-34 |
| G-35 | S4: Fresh task active_operation must match operation | PC-35 |
| G-36 | S4: Fresh task task_status must match operation | PC-36 |
| G-37 | S4: Fresh task verification_filter must match operation | PC-37 |
| G-38 | S4: Accepted source snapshot head is fenced | PC-38 |
| G-39 | S4: Accepted source snapshot common is fenced | PC-39 |
| G-40 | S4: Accepted source snapshot admin is fenced | PC-40 |
| G-41 | S4: Accepted source snapshot registered_path is fenced | PC-41 |
| G-42 | S4: Rejected source inspection cannot be used | PC-42 |
| G-43 | S4: Existing source recovery pin is re-read | PC-43 |
| G-44 | S4: Existing target-before recovery pin is re-read | PC-44 |
| G-45 | S4: Existing prepared recovery pin is re-read | PC-45 |
| G-46 | S4: Target target decision is independent | PC-46 |
| G-47 | S4: Target checkout decision is independent | PC-47 |
| G-48 | S4: Target sequencer decision is independent | PC-48 |
| G-49 | S4: Target symbolic decision is independent | PC-49 |
| G-50 | S4: Target dirty decision is independent | PC-50 |
| G-51 | S4: Target status-error decision is independent | PC-51 |
| G-52 | S4: Target head decision is independent | PC-52 |
| G-53 | S4: Target post-FF evidence is checked before acknowledgement | PC-53 |
| G-54 | S4: Non-checked-out target mutation uses expected-old SHA | PC-54 |
| G-55 | S4: Checked-out target advances HEAD/index/worktree together | PC-55 |
| G-56 | S4: source pin failure blocks dependent mutation | PC-56 |
| G-57 | S4: target-before pin failure blocks dependent mutation | PC-57 |
| G-58 | S4: prepared pin failure blocks dependent mutation | PC-58 |
| G-59 | S4: Pin creation cannot overwrite a racing ref | PC-59 |
| G-60 | S4: rebase.autoStash=false overrides hostile repository config | PC-60 |
| G-61 | S4: rebase.updateRefs=false overrides hostile repository config | PC-61 |
| G-62 | S4: Landing itself holds the repository lease in every mode | PC-62 |
| G-63 | S4: Shared writer claim blocks every landing mode | PC-63 |
| G-64 | S4: Exact-source follow-up claim blocks every mode | PC-64 |
| G-65 | S4: Shared dispatch acquires the exclusion before durable claim | PC-65 |
| G-66 | S4: Follow-up dispatch acquires exclusion before durable claim | PC-66 |
| G-67 | S4: Writer read occurs under lease, after competing admission | PC-67 |
| G-68 | S4: Exact-source writer read occurs under lease | PC-68 |
| G-69 | S4: Lease holds do not spend an attempt | PC-69 |
| G-70 | S4: Claim holds do not spend an attempt | PC-70 |
| G-71 | S4: Each admitted mode spends exactly one attempt | PC-71 |
| G-72 | S4: Hold retains the request identity and pending work | PC-72 |
| G-73 | S4: Settlement acquires lease before committing dirty source | PC-73 |
| G-74 | S4: Local child merge acquires lease before first mutation | PC-74 |
| G-75 | S4: Nested creation threads its existing lease | PC-75 |
| G-76 | S3: Canceled verifier cannot delete private output | PC-76 |
| G-77 | S3: Successful/failed verifier cannot delete private output | PC-77 |
| G-78 | S3: Real verifier honors exact selected test | PC-78 |
| G-79 | S3: No executed tests cannot pass verification | PC-79 |
| G-80 | S3: Every executed test must have passed | PC-80 |
| G-81 | S3: Nonzero/missing failed count cannot pass | PC-81 |
| G-82 | S3: Failed verifier never authorizes publication | PC-82 |
| G-83 | S3: Verifier child journal clears only on completed exit | PC-83 |
| G-84 | S2: Missing transcript ingestion cannot masquerade as repair | PC-84 |
| G-85 | S2: Pump writes only to the intended destination | PC-85 |
| G-86 | S2: Partial final JSONL is not consumed | PC-86 |
| G-87 | S2: Failed receipt save does not consume the line | PC-87 |
| G-88 | S2: Restarted pump does not replay committed UUIDs | PC-88 |
| G-89 | S2: Pump starts beyond pre-existing destination sequence | PC-89 |
| G-90 | S2: Pump completion precedes fixture deletion | PC-90 |
| G-91 | S2: Transcript confirmation outranks echoed screen | PC-91 |
| G-92 | S2: Visible Grok body plus redraw is not Sent | PC-92 |
| G-93 | S2: A transient empty frame cannot latch success | PC-93 |
| G-94 | S2: Observable Grok still requires actual UserPrompt | PC-94 |
| G-95 | S2: Wrong body cannot confirm this attempt | PC-95 |
| G-96 | S2: Identifying prefix is not complete receipt | PC-96 |
| G-97 | S2: Late confirmation also requires complete body | PC-97 |
| G-98 | S2: Queue-enqueue record is not recipient proof | PC-98 |
| G-99 | S2: Unobservable receipt must be fresh | PC-99 |
| G-100 | S2: Unobservable missing timestamp is not fresh | PC-100 |
| G-101 | S2: Late recipient proof prevents any new writes | PC-101 |
| G-102 | S2: Interrupted composer body resumes Enter-only | PC-102 |
| G-103 | S2: No transcript/snapshot knowledge cannot justify retry | PC-103 |
| G-104 | S2: Failed attempt retains its durable sequence floor | PC-104 |
| G-105 | S2: Busy recipient is not prematurely flushed | PC-105 |
| G-106 | S2: Missing composer evidence withholds submitting Enter | PC-106 |
| G-107 | S2: Overlay recovery respects operator-disabled switch | PC-107 |
| G-108 | S2: Unknown provider has no automatic dismissal authority | PC-108 |
| G-109 | S2: Working-after-fresh-pull forbids Esc | PC-109 |
| G-110 | S2: Fresh transcript catch-up precedes working decision | PC-110 |
| G-111 | S2: Dismissal happens at most once | PC-111 |
| G-112 | S2: Proactive recovery matches measured fragment only | PC-112 |
| G-113 | S2: Unmeasured question chrome is never guessed | PC-113 |
| G-114 | S2: Local no-UserPrompt command sends only one Enter | PC-114 |
| G-115 | S2: Retry presses Enter without retyping body | PC-115 |
| G-116 | S2: All swallowed Enters remain unconfirmed | PC-116 |
| G-117 | S2: CRLF body becomes LF before typing | PC-117 |
| G-118 | S2: Body and submitting Enter are separate writes | PC-118 |
| G-119 | S2: Bracketed paste wrapping survives multiline encoding | PC-119 |
| G-120 | S2: A working session is not killed for absent receipt | PC-120 |
| G-121 | S2: Committed enqueue precedes transport | PC-121 |
| G-122 | S2: Late receipt recovery works after verdict-save failure | PC-122 |
| G-123 | S4: Mode manufacture does not pre-seed publication authority | PC-123 |
| G-124 | S4: Initial remote absence/error cannot count as publication | PC-124 |
| G-125 | S4: Successful push exit still needs independent containment | PC-125 |
| G-126 | S4: Cleanup retry refreshes remote proof | PC-126 |
| G-127 | S4: Cleanup rereads durable authority after final inspection | PC-127 |
| G-128 | S4: Cleanup identity guard at reading 1 | PC-128 |
| G-129 | S4: Cleanup identity guard at reading 2 | PC-129 |
| G-130 | S4: Cleanup ignored guard at reading 1 | PC-130 |
| G-131 | S4: Cleanup ignored guard at reading 2 | PC-131 |
| G-132 | S6: TRX identity joins by testId, not display name | PC-132 |
| G-133 | S6: Class exemption is exact, never substring/argument match | PC-133 |
| G-134 | S6: Exact simple class names are accepted | PC-134 |
| G-135 | S6: Unresolved identity never gets a silent exemption | PC-135 |
| G-136 | S6: Every expanded row contributes duration and count | PC-136 |
| G-137 | S6: Threshold includes exactly five seconds | PC-137 |
| G-138 | S6: Malformed duration cannot produce green | PC-138 |
| G-139 | S4: Cleanup durable authority: task | PC-139 |
| G-140 | S4: Cleanup durable authority: operation | PC-140 |
| G-141 | S4: Cleanup durable authority: repository | PC-141 |
| G-142 | S4: Cleanup durable authority: path | PC-142 |
| G-143 | S4: Cleanup durable authority: git-directory | PC-143 |
| G-144 | S4: Cleanup durable authority: common-directory | PC-144 |
| G-145 | S4: Cleanup durable authority: source-ref | PC-145 |
| G-146 | S4: Cleanup durable authority: target-ref | PC-146 |
| G-147 | S4: Cleanup durable authority: target-sha | PC-147 |
| G-148 | S4: Cleanup durable authority: deletion-sha | PC-148 |
| G-149 | S4: Cleanup durable authority: verified-sha | PC-149 |
| G-150 | S4: Cleanup durable authority: cleanup-intent | PC-150 |
| G-151 | S4: Cleanup durable authority: phase | PC-151 |
| G-152 | S4: Cleanup durable authority: inactive | PC-152 |
| G-153 | S4: Cleanup durable authority: schema | PC-153 |
| G-154 | S4: Cleanup durable authority: unconfirmed | PC-154 |
| G-155 | S4: Cleanup durable authority: operation-namespace | PC-155 |
| G-156 | S4: Cleanup durable authority: destination | PC-156 |
| G-157 | S4: Cleanup durable authority: fingerprint | PC-157 |
| G-158 | S4: Cleanup durable authority: confirmation-method | PC-158 |
| G-159 | S4: Cleanup durable authority: observed-sha | PC-159 |
| G-160 | S4: Cleanup durable authority: lease | PC-160 |
| G-161 | S4: Cleanup independently rereads source recovery pin | PC-161 |
| G-162 | S4: Cleanup independently rereads target-before recovery pin | PC-162 |
| G-163 | S4: Cleanup independently rereads prepared recovery pin | PC-163 |
| G-164 | S2: Transient pump read failure retains retryable input | PC-164 |
| G-165 | S2: Failed attempt retains attempt number | PC-165 |
| G-166 | S2: Failed attempt retains start time | PC-166 |
| G-167 | S6: Exact full class name remains accepted | PC-167 |
| G-168 | S6: Class identity comparison ignores case | PC-168 |
| G-169 | S6: Missing duration cannot produce green | PC-169 |
| G-170 | S6: Negative duration cannot produce green | PC-170 |
| G-171 | S6: Malformed XML cannot produce green | PC-171 |
| G-172 | S2: Now delivery shares the per-session queue lock | PC-172 |
| G-173 | S3: Verifier build artifacts stay outside source tree | PC-173 |
| G-174 | S4: Target symbolic query failure is not usable identity | PC-174 |
| G-175 | S2: Already eligible WhenIdle delivery needs no future turn event | PC-175 |
| G-176 | S4: Target registration authority: ambiguous | PC-176 |
| G-177 | S4: Target registration authority: locked | PC-177 |
| G-178 | S4: Target registration authority: prunable | PC-178 |
| G-179 | S4: Target registration authority: unrecorded | PC-179 |

### Positive controls

**Protocol for every table row:** exact-method green baseline -> apply only the specified compiling defect -> exact method RED at the stated assertion -> restore fixed bytes, refresh timestamps/rebuild -> same method GREEN. Record selected argument(s), all executed/pass/fail/skip counts, patch, source/DLL fingerprint and both TRX paths. Default is the main Antiphon.Tests project and the exact filter `/*/*/<Class>/<Method>`; run all attributes of that method unless using an observed UID from a fresh execution, in a separate invocation with no tree filter. Named new methods must exist by Code completion. Mutation must add a missing control discovered during its independent audit and return to Code for any detection gap.

Rows marked **policy** exercise the actual production guard with controlled replies; rows marked **real** retain native Git or PTY. Controlled model tests may mutate test support code because that is the implementation under test; they must not mutate/loosen their own assertions. A defect in a production predicate is a temporary mutant only, never a requested implementation change.


| PC / guard | Compiling defect to apply alone | Exact Class.Method | Required red assertion | Layer / C448 relationship |
|---|---|---|---|---|
| PC-1 / G-1 | AttentionDtos.AttentionKind: change ReportUnsettled=22 to 222 | `UnmarkedWaitingContractTests.unmarked_waiting_attention_kind_is_appended_after_report_unsettled` | literal prefix map mismatch | policy; new |
| PC-2 / G-2 | AttentionKind: add C475Collision=22 without changing existing names | `UnmarkedWaitingContractTests.unmarked_waiting_attention_kind_is_appended_after_report_unsettled` | non-prefix value must be >28 | policy; new |
| PC-3 / G-3 | New RuleB predicate: return owned=true for any AddScoped<AgentTaskDispatcher> | `DelegationHarnessCensusTests.C475_RuleBRejectsBareDispatcher` | bare fixture classified as missing ownership | policy; new |
| PC-4 / G-4 | RuleB predicate: replace construction detection with Contains("LandingSafetyHarness") | `DelegationHarnessCensusTests.C475_RuleBRejectsMentionOnlyHelper` | comment/string/type-only cases rejected | policy; new |
| PC-5 / G-5 | LandingSafetyHarness.BuildServices: omit AddDelegationWorktreeGraph | `DelegationHarnessCensusTests.C475_RecognizedOwnersActuallyRegisterTheGraph` | owner source lacks actual graph invocation; no DB/DI setup failure credited | policy; new |
| PC-6 / G-6 | LandingProtocolHarness service setup: omit AddDelegationWorktreeGraph | `DelegationHarnessCensusTests.C475_RecognizedOwnersActuallyRegisterTheGraph` | controlled owner source lacks graph invocation | policy; new |
| PC-7 / G-7 | Remove only Category("Integration") from DelegateScriptLandStatusTests | `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration` | missing contains DelegateScriptLandStatusTests | policy; new |
| PC-8 / G-8 | ProcessSpawnLimit.Limit: return 2 | `ProcessSpawnLimitTests.Caps_concurrent_process_spawning_tests_at_one` | expected 1 | policy; new |
| PC-9 / G-9 | Remove limiter from TestDurationTripwireTests, after Code adds it to the existing limiter census | `ProcessSpawnLimitTests.Process_spawning_classes_carry_the_limiter` | TestDurationTripwireTests missing limiter | policy; new |
| PC-10 / G-10 | Remove one advance/remote/false attribute only from BoundaryControlled | `LandingProtocolHarnessTests.C475_CoverageAllocationMatchesTheLegacyTupleManifest` | set difference names missing tuple (source-attribute census); fresh TRX audit also fails | policy; new |
| PC-11 / G-11 | ControlledLandingGit.RunAsync default: return success instead of throwing | `ControlledLandingGitTests.C475_UnknownCommandsAreRejected` | unknown command Should.Throw; no process spawned | policy; new |
| PC-12 / G-12 | Controlled worktree manager's unsupported CreateAsync: return a fabricated WorktreeInfo instead of throwing | `ControlledLandingGitTests.C475_UnsupportedWorktreeOperationsAreRejected` | Should.Throw for incidental unsupported operation | policy; new |
| PC-13 / G-13 | LandingProtocolHarness.CreateLand: pass a distinct new ControlledLandingGit instead of registered instance | `LandingProtocolHarnessTests.C475_AllServicesUseTheRegisteredGit` | reference identity/trace ownership mismatch before protocol run | policy; new |
| PC-14 / G-14 | ControlledLandingGit: source-advance also assigns target SHA | `ControlledLandingGitTests.C475_SourceTargetRemoteAndPinsAreIndependent` | target/remote/pin snapshots unchanged after source update | policy; new |
| PC-15 / G-15 | ControlledLandingGit show-ref --exists error arm: return 2 instead of 128 | `ControlledLandingGitTests.C475_QueryErrorsAreNotAbsence` | error code remains 128 and no absence transition | policy; new |
| PC-16 / G-16 | ControlledLandingGit.RunOwnedAsync: omit await started(...) | `ControlledLandingGitTests.C475_RunOwnedAwaitsTheStartedCallback` | callback count 1 and mutation blocked until callback released | policy; new |
| PC-17 / G-17 | ControlledLandingGit.PushOwnedAsync: omit await started(...) | `ControlledLandingGitTests.C475_PushOwnedAwaitsTheStartedCallback` | callback count 1 and remote unchanged while callback blocked | policy; new |
| PC-18 / G-18 | LandingProtocolHarness fault adapter: invoke AfterAcknowledged during SavingChanges instead of after commit | `LandingProtocolHarnessTests.C475_AcknowledgedHookReadsCommittedPhase` | fresh DbContext observes requested phase at hook, not prior phase | policy; new |
| PC-19 / G-19 | Controlled worktree manager.TryRemoveAsync: return canned clean outcome | `LandingProtocolHarnessTests.C475_CleanupUsesGuardedRemoval` | ignored sentinel remains and Cleanup=Refused; removal/evidence trace required | policy; new |
| PC-20 / G-20 | ControlledLandingGit.CommonDirectoryAsync: return input path rather than common path | `LandingProtocolHarnessTests.C475_SourceAliasContendsOnRepositoryLease` | second acquisition is null, same common directory | policy; new |
| PC-21 / G-21 | LandingProtocolHarness.RestartServicesAsync: use a newly seeded empty model/store | `LandingProtocolHarnessTests.C475_RecreationPreservesCommittedModeAndAttempts` | same operation/request identity and one next admitted attempt in four modes | policy; new |
| PC-22 / G-22 | AgentTaskLandingProtocol.RunAsync: omit only RecheckSourceAsync at inspected line 129 (remote); retain all later checks | `LandingSourceBoundaryControlTests.C448_V10_after_fetch` | RemoteBeforeSha remains null; no rebase; fired once, exact refusal and source retained | policy; C448 pc19-after_fetch |
| PC-23 / G-23 | AgentTaskLandingProtocol.RunAsync: omit only RecheckSourceAsync at inspected line 142 (BeforeRebaseIntent); retain all later checks | `LandingSourceBoundaryControlTests.C448_V10_before_rebase_intent` | RebaseStartedAt remains null; no rebase; fired once, exact refusal and source retained | policy; C448 pc19-before_rebase_intent |
| PC-24 / G-24 | AgentTaskLandingProtocol.RunAsync: omit only RecheckSourceAsync at inspected line 146 (RebaseStarted); retain all later checks | `LandingSourceBoundaryControlTests.C448_V10_before_rebase_child` | no rebase command; fired once, exact refusal and source retained | policy; C448 pc19-before_rebase_child |
| PC-25 / G-25 | AgentTaskLandingProtocol.RunAsync: omit only RecheckSourceAsync at inspected line 176 (Prepared); retain all later checks | `LandingSourceBoundaryControlTests.C448_V10_before_verification` | Verifier.Calls=0; fired once, exact refusal and source retained | policy; C448 pc19-before_verification |
| PC-26 / G-26 | AgentTaskLandingProtocol.RunAsync: omit only RecheckSourceAsync at inspected line 197 (Verified); retain all later checks | `LandingSourceBoundaryControlTests.C448_V10_before_target_intent` | Phase remains Verified; no target intent; fired once, exact refusal and source retained | policy; C448 pc19-before_target_intent |
| PC-27 / G-27 | AgentTaskLandingProtocol.RunAsync: omit only RecheckSourceAsync at inspected line 203 (TargetAdvanceStarted); retain all later checks | `LandingSourceBoundaryControlTests.C448_V10_before_target_mutation` | no --ff-only or target update-ref; fired once, exact refusal and source retained | policy; C448 pc19-before_target_mutation |
| PC-28 / G-28 | AgentTaskLandingProtocol.RunAsync: omit only RecheckSourceAsync at inspected line 222 (LocalTargetAdvanced); retain all later checks | `LandingSourceBoundaryControlTests.C448_V10_before_publication_observation` | no later fetch; fired once, exact refusal and source retained | policy; C448 pc19-before_publication_observation |
| PC-29 / G-29 | AgentTaskLandingProtocol.RunAsync: omit only RecheckSourceAsync at inspected line 228 (BeforePushIntent); retain all later checks | `LandingSourceBoundaryControlTests.C448_V10_before_push_intent` | PushStartedAt remains null; fired once, exact refusal and source retained | policy; C448 pc19-before_push_intent |
| PC-30 / G-30 | AgentTaskLandingProtocol.RunAsync: omit only RecheckSourceAsync at inspected line 235 (PushStarted); retain all later checks | `LandingSourceBoundaryControlTests.C448_V10_before_push` | no push; fired once, exact refusal and source retained | policy; C448 pc19-before_push |
| PC-31 / G-31 | RecheckSourceAsync: replace only SamePath(currentTask.RepoPath, op.RepositoryPath) predicate with true | `LandingProtocolGuardTests.C475_Task_repository` | committed concurrent change refuses before Verified/target intent; exact task_coordinates_changed or verification_filter_changed | policy; C448 pc3/18 (repository) |
| PC-32 / G-32 | RecheckSourceAsync: replace only SamePath(currentTask.WorktreePath, op.WorktreePath) predicate with true | `LandingProtocolGuardTests.C475_Task_path` | committed concurrent change refuses before Verified/target intent; exact task_coordinates_changed or verification_filter_changed | policy; C448 pc3/18 (path) |
| PC-33 / G-33 | RecheckSourceAsync: replace only FullRef(currentTask.WorktreeBranch)==op.SourceFullRef predicate with true | `LandingProtocolGuardTests.C475_Task_source_ref` | committed concurrent change refuses before Verified/target intent; exact task_coordinates_changed or verification_filter_changed | policy; C448 pc3/18 (source_ref) |
| PC-34 / G-34 | RecheckSourceAsync: replace only FullRef(currentTask.MergeTargetRef??"master")==op.TargetFullRef predicate with true | `LandingProtocolGuardTests.C475_Task_target_ref` | committed concurrent change refuses before Verified/target intent; exact task_coordinates_changed or verification_filter_changed | policy; C448 pc3/18 (target_ref) |
| PC-35 / G-35 | RecheckSourceAsync: replace only currentTask.ActiveLandingId==op.Id predicate with true | `LandingProtocolGuardTests.C475_Task_active_operation` | committed concurrent change refuses before Verified/target intent; exact task_coordinates_changed or verification_filter_changed | policy; C448 pc3/18 (active_operation) |
| PC-36 / G-36 | RecheckSourceAsync: replace only currentTask.Status==Succeeded predicate with true | `LandingProtocolGuardTests.C475_Task_task_status` | committed concurrent change refuses before Verified/target intent; exact task_coordinates_changed or verification_filter_changed | policy; C448 pc3/18 (task_status) |
| PC-37 / G-37 | RecheckSourceAsync: replace only currentTask.LandVerifyFilter==op.VerificationFilter predicate with true | `LandingProtocolGuardTests.C475_Task_verification_filter` | committed concurrent change refuses before Verified/target intent; exact task_coordinates_changed or verification_filter_changed | policy; C448 pc3/18 (verification_filter) |
| PC-38 / G-38 | AgentTaskLandingProtocol.Matches: replace only snapshot.HeadSha==sha with true | `LandingProtocolGuardTests.C475_Source_head` | changed otherwise-accepted snapshot gives source_changed, no target intent/push | policy; C448 pc18 prepared/source identity |
| PC-39 / G-39 | AgentTaskLandingProtocol.Matches: replace only SamePath(snapshot.CommonDirectory,op.CommonDirectory) with true | `LandingProtocolGuardTests.C475_Source_common` | changed otherwise-accepted snapshot gives source_changed, no target intent/push | policy; C448 pc18 prepared/source identity |
| PC-40 / G-40 | AgentTaskLandingProtocol.Matches: replace only SamePath(snapshot.GitDirectory,op.GitDirectory) with true | `LandingProtocolGuardTests.C475_Source_admin` | changed otherwise-accepted snapshot gives source_changed, no target intent/push | policy; C448 pc18 prepared/source identity |
| PC-41 / G-41 | AgentTaskLandingProtocol.Matches: replace only SamePath(snapshot.RegisteredPath,op.WorktreePath) with true | `LandingProtocolGuardTests.C475_Source_registered_path` | changed otherwise-accepted snapshot gives source_changed, no target intent/push | policy; C448 pc18 prepared/source identity |
| PC-42 / G-42 | RecheckSourceAsync: drop inspected.Accepted while retaining Matches; test supplies nonnull matching snapshot plus refusal reason | `LandingProtocolGuardTests.C475_Source_rejected` | refusal occurs before next command | policy; C448 pc18 source validity |
| PC-43 / G-43 | RecheckSourceAsync: skip pins iteration for name source | `AgentTaskLandBoundaryTests.C448_V26_RecoveryPinsRemainPrerequisitesAfterVerification` | recovery_pin_changed and zero FF/push/remove in named pin row | real; C448 pc22/47 pin prerequisites |
| PC-44 / G-44 | RecheckSourceAsync: skip pins iteration for name target-before | `AgentTaskLandBoundaryTests.C448_V26_RecoveryPinsRemainPrerequisitesAfterVerification` | recovery_pin_changed and zero FF/push/remove in named pin row | real; C448 pc22/47 pin prerequisites |
| PC-45 / G-45 | RecheckSourceAsync: skip pins iteration for name prepared | `AgentTaskLandBoundaryTests.C448_V26_RecoveryPinsRemainPrerequisitesAfterVerification` | recovery_pin_changed and zero FF/push/remove in named pin row | real; C448 pc22/47 pin prerequisites |
| PC-46 / G-46 | CheckTargetAsync: bypass only CommitAsync(target)==expected | `LandingRemovalPolicyControlTests.C448_V11_EachTargetDecisionRefusesIndependently` | LandingRefusal required in target row; no mutation | policy; C448 pc20-target-target |
| PC-47 / G-47 | CheckTargetAsync: bypass only TargetCheckoutRecorded plus captured path | `LandingRemovalPolicyControlTests.C448_V11_EachTargetDecisionRefusesIndependently` | LandingRefusal required in checkout row; no mutation | policy; C448 pc20-target-checkout |
| PC-48 / G-48 | CheckTargetAsync: bypass only !HasActiveSequencerAsync | `LandingRemovalPolicyControlTests.C448_V11_EachTargetDecisionRefusesIndependently` | LandingRefusal required in sequencer row; no mutation | policy; C448 pc20-target-sequencer |
| PC-49 / G-49 | CheckTargetAsync: bypass only symbolic.Output.Trim()==TargetFullRef | `LandingRemovalPolicyControlTests.C448_V11_EachTargetDecisionRefusesIndependently` | LandingRefusal required in symbolic row; no mutation | policy; C448 pc20-target-symbolic |
| PC-50 / G-50 | CheckTargetAsync: bypass only status.Output.Length==0 | `LandingRemovalPolicyControlTests.C448_V11_EachTargetDecisionRefusesIndependently` | LandingRefusal required in dirty row; no mutation | policy; C448 pc20-target-dirty |
| PC-51 / G-51 | CheckTargetAsync: bypass only status.Succeeded | `LandingRemovalPolicyControlTests.C448_V11_EachTargetDecisionRefusesIndependently` | LandingRefusal required in status-error row; no mutation | policy; C448 pc20-target-status-error |
| PC-52 / G-52 | CheckTargetAsync: bypass only CommitAsync(checkout,HEAD)==expected | `LandingRemovalPolicyControlTests.C448_V11_EachTargetDecisionRefusesIndependently` | LandingRefusal required in head row; no mutation | policy; C448 pc20-target-head |
| PC-53 / G-53 | RunAsync: omit CheckTargetAsync after real fast-forward before LocalTargetAfterSha assignment | `AgentTaskLandBoundaryTests.C448_V11_TargetMutationAfterFastForwardCannotBeAcknowledged` | Phase=TargetAdvanceStarted and LocalTargetAfterSha=null in advance/switch/dirty/staged rows | real; C448 pc20-post-fast-forward |
| PC-54 / G-54 | RunAsync target update-ref vector: omit current old-SHA argument | `AgentTaskLandBoundaryTests.C448_V11_TargetAdvanceUsesTheCapturedCheckoutAndOldSha` | checkedOut=false retains rival target SHA | real; C448 pc21-target-cas |
| PC-55 / G-55 | RunAsync checked-out branch: replace FF merge with ref-only update-ref at repository | `AgentTaskLandBoundaryTests.C448_V11_TargetAdvanceUsesTheCapturedCheckoutAndOldSha` | checkedOut=true has feature.txt, clean status and correct HEAD | real; C448 pc21-ref-only-ff |
| PC-56 / G-56 | Protocol.PinAsync: ignore failed result only when name is source | `AgentTaskLandBoundaryTests.C448_V32_EachRecoveryPinFailureStopsDependentMutation` | named pin failure/collision: recovery_pin_failed before verifier/FF/push/remove; rival pin retained | real; C448 pc22 source |
| PC-57 / G-57 | Protocol.PinAsync: ignore failed result only when name is target-before | `AgentTaskLandBoundaryTests.C448_V32_EachRecoveryPinFailureStopsDependentMutation` | named pin failure/collision: recovery_pin_failed before verifier/FF/push/remove; rival pin retained | real; C448 pc22 target-before |
| PC-58 / G-58 | Protocol.PinAsync: ignore failed result only when name is prepared | `AgentTaskLandBoundaryTests.C448_V32_EachRecoveryPinFailureStopsDependentMutation` | named pin failure/collision: recovery_pin_failed before verifier/FF/push/remove; rival pin retained | real; C448 pc22 prepared |
| PC-59 / G-59 | LandingGit.PinAsync: omit expected-old-zero from update-ref vector | `AgentTaskLandBoundaryTests.C448_V32_EachRecoveryPinFailureStopsDependentMutation` | collision rows preserve exact wrongSha; no dependent mutation | real; C448 pc22-pin-cas |
| PC-60 / G-60 | RunAsync rebase vector: omit the -c pair for rebase.autoStash=false | `AgentTaskLandBoundaryTests.C448_V32_RebaseConfigurationCannotStashOrRewriteUnrelatedRefs` | command contains rebase.autoStash=false; other-owner SHA/stash preserved | real; C448 pc23-autostash |
| PC-61 / G-61 | RunAsync rebase vector: omit the -c pair for rebase.updateRefs=false | `AgentTaskLandBoundaryTests.C448_V32_RebaseConfigurationCannotStashOrRewriteUnrelatedRefs` | command contains rebase.updateRefs=false; other-owner SHA/stash preserved | real; C448 pc23-update-refs |
| PC-62 / G-62 | RunRequestAsync: dispose the acquired lease just before calling protocol, retaining its remaining code | `LandingProtocolGuardTests.C475_LandingKeepsLeaseThroughProtocol` | at protocol entry's first model query, a nonblocking contender unexpectedly acquires; assert that observation before checking captured protocol refusal | policy; new |
| PC-63 / G-63 | FindWriterAsync: skip Shared candidates whose source differs from task source | `LandingAdmissionControlTests.C448_V14_shared_dispatch_first` | Held, unchanged attempt/pending, no mutation across four modes | policy; C448 pc43-ignore-shared-claim |
| PC-64 / G-64 | FindWriterAsync: skip non-Shared candidates | `LandingAdmissionControlTests.C448_V14_follow_up_dispatch_first` | Held, unchanged attempt/pending, source retained across four modes | policy; C448 pc44-ignore-followup-claim |
| PC-65 / G-65 | AgentTaskDispatcher shared admission: bypass its failed-lease hold branch (retain disposable fixture lease handles) | `LandingAdmissionControlTests.C448_V14_shared_land_first` | writer remains Queued, AgentId null, Held event, four modes | policy; C448 pc43-shared-admission-without-lease |
| PC-66 / G-66 | AgentTaskDispatcher follow-up admission: bypass its failed-lease hold branch | `LandingAdmissionControlTests.C448_V14_follow_up_land_first` | writer remains Queued, AgentId null, Held event, four modes | policy; C448 pc44-followup-admission-without-lease |
| PC-67 / G-67 | RunRequestAsync: cache FindWriterAsync result before TryAcquireAsync and use cached result after acquisition | `LandingAdmissionControlTests.C448_V14_shared_dispatch_before_acquire` | real committed Shared claim gives Held in all modes | policy; C448 pc43-44-claim-read-before-lease/shared |
| PC-68 / G-68 | Same moved FindWriterAsync read, separate defect cycle/selection for follow-up | `LandingAdmissionControlTests.C448_V14_follow_up_dispatch_before_acquire` | real committed follow-up claim gives Held in all modes | policy; C448 pc43-44-claim-read-before-lease/follow-up |
| PC-69 / G-69 | RunRequestAsync lease-null branch: increment and save task.LandAttempt | `LandingAdmissionControlTests.C448_V14_AdmittedModes` | attempt remains unchanged while held, four modes | policy; C448 pc50-lease-hold |
| PC-70 / G-70 | RunRequestAsync holder branch: increment and save task.LandAttempt | `LandingAdmissionControlTests.C448_V14_ClaimHoldAttempts` | attempt remains unchanged in eight holder/mode rows | policy; C448 pc50-claim-hold |
| PC-71 / G-71 | RunRequestAsync admitted transaction: omit task.LandAttempt increment, retain request update | `LandingAdmissionControlTests.C448_V14_AdmittedModes` | after release/recreation task attempt = before+1 for all modes | policy; C448 pc50-admitted-attempt (also AlreadyPresent/CleanupRetry) |
| PC-72 / G-72 | HoldAsync: set task.LandRequestedAt=null after recording hold | `LandingAdmissionControlTests.C448_V14_ClaimHoldAttempts` | LandRequestedAt nonnull and same operation/request before release | policy; C448 pc43/44 pending invariant |
| PC-73 / G-73 | DelegationWorktreeService.TryMergeBackAsync root-settlement path: perform settlement commit before lease attempt | `AgentTaskLandConcurrencyTests.C448_V36_SettlementLeasePrecedesItsFirstMutation` | localMerge=false: original HEAD/bytes and no add/commit | real; C448 pc42-settlement-and-merge-lease/root |
| PC-74 / G-74 | Same first-mutation move in localMerge path only | `AgentTaskLandConcurrencyTests.C448_V36_SettlementLeasePrecedesItsFirstMutation` | localMerge=true: original HEAD/bytes and no add/commit | real; C448 pc42-settlement-and-merge-lease/child |
| PC-75 / G-75 | WorktreeManager.CreateAsync overload with lease: request another acquisition before using supplied lease | `AgentTaskLandBoundaryTests.C448_V36_CreationUsesTheRepositoryLeaseAndThreadsNestedOwnership` | Acquisitions unchanged; counting wrapper observes duplicate request without awaiting a deadlock | real; C448 pc42-nested-creation-acquisition |
| PC-76 / G-76 | VerifyWithObserverAsync: delete fixture's bin-land before cancellation check | `AgentTaskLandVerifierTests.C448_V35_RealVerifierPreservesPreExistingOutput` | pre-existing keep.txt exists and bytes unchanged, OperationCanceledException | real; C448 pc8-unowned-verifier-output/cancel |
| PC-77 / G-77 | VerifyWithObserverAsync: delete fixture's bin-land on non-canceled completion | `AgentTaskLandVerifierTests.C448_V34_RealTUnitSelectionRequiresExecutedPassingTests` | private file unchanged in pass/fail/no-selection rows | real; C448 pc8-unowned-verifier-output/pass-fail remap |
| PC-78 / G-78 | VerifyWithObserverAsync: replace requested filter with /*/*/VerificationProbe/* | `AgentTaskLandVerifierTests.C448_V34_RealTUnitSelectionRequiresExecutedPassingTests` | SelectedPass remains passed/tests 1/1; NeverSelected fails mutant | real; C448 V34 selector |
| PC-79 / G-79 | HasPassingTestCounters: allow executed==0 | `AgentTaskLandVerificationEvidenceTests.C448_V34_CountersNeedExecutedPassingTests` | 0/0/0 accepted=false | policy; C448 pc25-zero-executed |
| PC-80 / G-80 | HasPassingTestCounters: remove parsed passed==executed prerequisite | `AgentTaskLandVerificationEvidenceTests.C448_V34_CountersNeedExecutedPassingTests` | 2/1/0 and missing passed rejected | policy; C448 pc25-passed-count |
| PC-81 / G-81 | HasPassingTestCounters: remove parsed failed==0 prerequisite | `AgentTaskLandVerificationEvidenceTests.C448_V34_CountersNeedExecutedPassingTests` | 2/2/1 and missing failed rejected | policy; C448 pc25-failed-count |
| PC-82 / G-82 | Protocol.RunAsync: bypass Require(verification.Passed) | `AgentTaskLandVerifierTests.C448_V32_VerifierOutcomeNeverMakesPrivateOutputDisposable` | fail row: VerifiedAt/remote receipt null, target=seed, no FF/push | real; C448 pc25-failed-verifier |
| PC-83 / G-83 | LandingVerifier.Observer.ExitedAsync: return completed task without journal acknowledgement | `AgentTaskLandVerifierTests.C448_V34_RealTUnitSelectionRequiresExecutedPassingTests` | children directory empty after each real run | real; C448 V34 journal preservation |
| PC-84 / G-84 | New owned receipt fixture setup: omit pump startup while leaving fake transcript enabled | `SessionMessageQueuePtyIntegrationTests.A_body_typed_while_an_overlay_is_up_recovers_via_Esc_and_submits` | after capturing Enqueue exception, complete destination UserPrompt count=1 and ConfirmedBy=Transcript fail; raw still has one submit | native; overlay diagnosed defect |
| PC-85 / G-85 | PumpTranscriptAsync: persist entries under the deliberately seeded other-session ID | `SessionQueueReceiptPlumbingTests.C475_PumpPersistsCompleteLinesOnce` | destination exact UUID/body and other session unchanged | policy; new |
| PC-86 / G-86 | Pump: advance consumed cursor over incomplete trailing line before newline | `SessionQueueReceiptPlumbingTests.C475_PumpPersistsCompleteLinesOnce` | partial-line row: no early insert and one row after completion (not parser exception) | policy; new |
| PC-87 / G-87 | Pump: advance cursor before DB commit and retain it after injected save failure | `SessionQueueReceiptPlumbingTests.C475_PumpPersistsCompleteLinesOnce` | save-fails row eventually one complete receipt | policy; new |
| PC-88 / G-88 | Pump resume: reset cursor/UUID check to zero without consulting persisted state | `SessionQueueReceiptPlumbingTests.C475_PumpPersistsCompleteLinesOnce` | restart-after-commit: one UUID/one UserPrompt, no duplicate | policy; new |
| PC-89 / G-89 | Pump initialization: use SeedSequence=1 instead of persisted maximum | `SessionQueueReceiptPlumbingTests.C475_PumpPersistsCompleteLinesOnce` | seeded-sequence: all new sequence values > pre-send maximum | policy; new |
| PC-90 / G-90 | Owned fixture cleanup: cancel pump but omit awaiting its task | `SessionQueueReceiptPlumbingTests.C475_PumpIsJoinedBeforeFixtureDisposal` | cleanup stays blocked/rows preserved until pump barrier released | policy; new |
| PC-91 / G-91 | WaitForTranscriptConfirmAsync: skip matching-record return for Grok | `SessionMessageQueuePtyIntegrationTests.A_body_typed_while_an_overlay_is_up_recovers_via_Esc_and_submits` | recipient receipt exists; captured outcome must be transcript-confirmed success, no ConflictException | native; C448-independent delivery |
| PC-92 / G-92 | Unobservable Grok deadline branch: accept output advance with head still visible | `SessionMessageQueueDeliveryVerificationTests.Grok_unobservable_redraw_with_body_visible_is_NoSubmitOutput_not_Sent` | Pending, NoSubmitOutput, SentAt=null | policy; new |
| PC-93 / G-93 | WaitForTranscriptConfirmAsync: latch first emptied-composer poll as final evidence | `SessionMessageQueueDeliveryVerificationTests.Grok_unobservable_transient_empty_frame_does_not_latch_emptied_composer` | Pending/NoSubmitOutput | policy; new |
| PC-94 / G-94 | WaitForTranscriptConfirmAsync: allow screen departure on observable Grok path | `SessionMessageQueueDeliveryVerificationTests.Grok_observable_queued_body_without_UserPrompt_reverts_to_Pending` | Pending, NoTranscriptRecord, zero UserPrompts | policy; new |
| PC-95 / G-95 | TranscriptConfirm.Classify: treat any nonempty transcript body as Complete | `SessionMessageQueueDeliveryVerificationTests.A_stale_record_alone_never_produces_delivered` | ours Pending/SentAt null despite a new stale UserPrompt | policy; new |
| PC-96 / G-96 | TranscriptConfirm.Classify: return Complete for identity match before completeness check | `SessionMessageQueueDeliveryVerificationTests.A_clipped_prefix_parks_as_truncated_not_sent` | parked Pending, no retype/kill | policy; new |
| PC-97 / G-97 | LateConfirmAttemptedMessagesAsync: accept Truncated as Confirmed | `SessionMessageQueueDeliveryVerificationTests.Late_confirm_does_not_promote_a_truncated_body_to_sent` | Pending/parked and zero inputs | policy; new |
| PC-98 / G-98 | TryFindConfirmingRecordAsync kind filter: include QueueEnqueue | `SessionMessageQueueDeliveryVerificationTests.Queue_enqueue_does_not_confirm_delivery` | status is not Sent | policy; new |
| PC-99 / G-99 | TryFindUnobservableConfirmingRecordAsync: drop timestamp lower bound | `SessionMessageQueueDeliveryVerificationTests.Card0164_unobservable_old_timestamp_row_does_not_confirm` | Pending despite old matching body | policy; new |
| PC-100 / G-100 | TryFindUnobservableConfirmingRecordAsync: treat null Timestamp as current time | `SessionMessageQueueDeliveryVerificationTests.Card0164_unobservable_null_timestamp_row_does_not_confirm` | Pending despite new unstamped matching body | policy; new |
| PC-101 / G-101 | LateConfirmAttemptedMessagesAsync: always return no confirmed rows | `SessionMessageQueueDeliveryVerificationTests.Late_confirm_marks_the_message_sent_with_zero_writes_to_the_terminal` | Inputs empty; exact message ID in LateConfirmedMessageIds | policy; new |
| PC-102 / G-102 | Interrupted recovery path: send body before recovery Enter | `SessionMessageQueueInterruptedAttemptTests.Verdict_less_Sent_with_head_on_screen_sends_Enter_only` | Inputs exactly [CR], attempt=1 | policy; new |
| PC-103 / G-103 | Interrupted recovery snapshot exception arm: continue as empty composer | `SessionMessageQueueInterruptedAttemptTests.Snapshot_unavailable_does_not_retype_or_revert` | zero inputs; verdict still null | policy; new |
| PC-104 / G-104 | Failed-delivery revert: clear only LastDeliveryBaselineSequence | `SessionMessageQueueDeliveryVerificationTests.Attempt_metadata_survives_the_revert_a_failed_delivery_does` | baseline equals the persisted pre-attempt floor | policy; new |
| PC-105 / G-105 | WhenIdle eligibility: bypass IsWorkingAsync for the batch path | `SessionMessageQueuePtyIntegrationTests.Batched_multiline_body_passes_composer_delivery_verification_and_submits_once` | both rows Pending and no submit before inserted TurnEnd | native; new |
| PC-106 / G-106 | DeliverAsync failed-composer branch: send CR before returning NoComposerEvidence | `SessionMessageQueueDeliveryVerificationTests.Wedged_composer_withholds_enter_reverts_message_and_restarts_always_on_agent` | Inputs contain body only, no CR | policy; new |
| PC-107 / G-107 | TryDismissOverlayAsync: omit OverlayRecoveryEnabled predicate | `SessionMessageQueueDeliveryVerificationTests.C475_OverlayRecoveryDisabledNeverEscapes` | no Esc/submit under measured overlay | policy; new |
| PC-108 / G-108 | TryDismissOverlayAsync unknown-provider branch: send Esc and return true before capability rejection (one unauthorized dismissal) | `SessionMessageQueueDeliveryVerificationTests.NoComposerEvidence_on_idle_Unknown_kind_sends_no_Esc` | no Esc for the unchanged Unknown contract | policy; new |
| PC-109 / G-109 | TryDismissOverlayAsync: omit IsWorkingAsync refusal | `SessionMessageQueueDeliveryVerificationTests.NoComposerEvidence_on_a_working_session_sends_no_Esc` | no Esc, Inputs body only | policy; new |
| PC-110 / G-110 | TryDismissOverlayAsync: omit CatchUpTranscriptAsync | `SessionMessageQueueDeliveryVerificationTests.C475_OverlayRecoveryPullsBeforeWorkingDecision` | new runner AssistantText ingested; no Esc | policy; new |
| PC-111 / G-111 | DeliverAsync reactive arm: remove !overlayDismissed gate; fixture must use measured proactive overlay and persistent no-echo | `SessionQueueReceiptPlumbingTests.C475_ProactiveAndReactiveRecoveryShareOneEscBudget` | exactly one Esc after proactive recovery followed by failed composer check | policy; new |
| PC-112 / G-112 | DeliverAsync proactive matcher: replace DetectFragments.Any match with true | `SessionMessageQueueDeliveryVerificationTests.Proactive_detector_does_not_Esc_an_unmeasured_modal` | first input is body, not Esc | policy; new |
| PC-113 / G-113 | GrokQuestionPopup.IsPresent: return true for arbitrary nonempty screen | `GrokQuestionPopupTests.Unmeasured_literals_mean_IsPresent_is_always_false` | unmeasured sample returns false | policy; new |
| PC-114 / G-114 | DeliverAsync: remove WritesUserPrompt=false local-command routing | `SessionMessageQueueDeliveryVerificationTests.A_WritesUserPrompt_false_command_sends_exactly_one_Enter_and_skips_confirm` | exactly one CR, no kill | policy; new |
| PC-115 / G-115 | Confirm re-enter branch: send payload again before CR | `SessionMessageQueuePtyIntegrationTests.A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once` | one exact full UserPrompt; original attempt=1 | native; new |
| PC-116 / G-116 | Confirm deadline: return Delivered when enters reach cap | `SessionMessageQueuePtyIntegrationTests.A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript` | Pending/SentAt null and zero recipient UserPrompts | native; new |
| PC-117 / G-117 | DeliverAsync: remove ReplaceLineEndings("\n") normalization | `SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn` | one exact normalized full body, no fragmented prompts | native; new |
| PC-118 / G-118 | DeliverAsync: append CR to payload write and omit standalone Enter | `SessionQueueReceiptPlumbingTests.C475_MultilineWritesKeepPasteMarkers` | forwarding decorator observes body and standalone CR in separate calls; complete native UserPrompt remains checked | native; new |
| PC-119 / G-119 | PtyInputEncoding.WrapIfMultiline: return unwrapped normalized text | `SessionQueueReceiptPlumbingTests.C475_MultilineWritesKeepPasteMarkers` | forwarding decorator records exactly ESC[200~ + LF body + ESC[201~, then standalone CR; complete native recipient body remains checked | native; new |
| PC-120 / G-120 | Failed delivery handling: bypass fresh working-state no-kill guard | `SessionMessageQueueDeliveryVerificationTests.A_working_session_is_not_killed_when_the_record_never_arrives` | adapter.Killed=false; row Pending | policy; new |
| PC-121 / G-121 | WhenIdle enqueue path: call flush/DeliverAsync before queue SaveChangesAsync (same body/session, keep explicit request API) | `SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery` | insert-fails: no bytes/receipt despite injected insert failure | native; new |
| PC-122 / G-122 | Interrupted recovery selection: exclude Sent rows with null DeliveryVerdict | `SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery` | receipt-before-verdict: same row late-confirmed after recreation, no extra input | native; new |
| PC-123 / G-123 | LandingProtocolHarness.PrepareModeAsync(CleanupRetry): insert canned Complete operation instead of running protocol | `LandingProtocolHarnessTests.C475_ModesAreReachedThroughProtocol` | observed real transition sequence and guarded refusal before repost, not just final mode fields | policy; new |
| PC-124 / G-124 | Protocol.ObserveAsync: replace error observation with containing OriginalSourceSha | `LandingProtocolGuardTests.C475_RemoteErrorDoesNotPublish` | no receipt/cleanup on injected remote error | policy; new |
| PC-125 / G-125 | Protocol post-push: use successful push exit to synthesize contains=true observation | `LandingProtocolGuardTests.C475_PushExitDoesNotConfirmPublication` | push succeeds in model, remote observation denies containment; remote receipt null, no cleanup | policy; new |
| PC-126 / G-126 | GuardedWorktreeRemoval.AuthorityAsync: return null before refreshRemote observation | `LandingProtocolHarnessTests.C475_CleanupRetryRequiresFreshRemoteContainment` | after real modeled publication then remote rewrite: retained sentinel/ref, cleanup refused; observed fresh proof read | policy; new |
| PC-127 / G-127 | GuardedWorktreeRemoval.RemoveAsync: omit final AuthorityAsync(refreshRemote:false) | `LandingRemovalPolicyControlTests.C448_V18_DurableAuthorityIsReadAfterTheFinalInspection` | two inspections; no destructive commands after crossed task receipt | policy; C448 pc32-final-durable-authority |
| PC-128 / G-128 | GuardedWorktreeRemoval.RemoveAsync: omit only Matches refusal for reading 1 | `LandingRemovalPolicyControlTests.C448_V18_EachContentReadingRefusesBeforeItsNextCommand` | named reading/head row: InspectionCount=1, no mutations, sentinel/ref retained | policy; C448 pc32-1 |
| PC-129 / G-129 | GuardedWorktreeRemoval.RemoveAsync: omit only Matches refusal for reading 2 | `LandingRemovalPolicyControlTests.C448_V18_EachContentReadingRefusesBeforeItsNextCommand` | named reading/head row: InspectionCount=2, no mutations, sentinel/ref retained | policy; C448 pc32-2 |
| PC-130 / G-130 | GuardedWorktreeRemoval.RemoveAsync: omit only HasProtectedIgnored refusal for reading 1 | `LandingRemovalPolicyControlTests.C448_V18_EachContentReadingRefusesBeforeItsNextCommand` | named reading/ignored-path row: InspectionCount=1, no mutations, sentinel/ref retained | policy; C448 pc7-1 |
| PC-131 / G-131 | GuardedWorktreeRemoval.RemoveAsync: omit only HasProtectedIgnored refusal for reading 2 | `LandingRemovalPolicyControlTests.C448_V18_EachContentReadingRefusesBeforeItsNextCommand` | named reading/ignored-path row: InspectionCount=2, no mutations, sentinel/ref retained | policy; C448 pc7-2 |
| PC-132 / G-132 | tripwire: index/join definitions by name instead of id/testId | `TestDurationTripwireTests.C475_ClassIdentityJoin` | only correct class exempt despite equal display names/reversed definitions | script; new |
| PC-133 / G-133 | tripwire: restore testName.IndexOf allowlist behavior | `TestDurationTripwireTests.C475_ArgumentsCannotWhitelistAClass` | all three slow spoof/lookalike rows reported | script; new |
| PC-134 / G-134 | tripwire: compare only full className and omit simple-name equality | `TestDurationTripwireTests.C475_SimpleAndFullNamesAreExact` | simple exact allowlist invocation exits 0 | script; new |
| PC-135 / G-135 | tripwire: continue when testId has no usable class | `TestDurationTripwireTests.C475_UnresolvedIdentityCannotBeExempted` | each shape nonzero and visibly unresolved | script; new |
| PC-136 / G-136 | tripwire: deduplicate results by display/method before reporting | `TestDurationTripwireTests.C475_ExpandedRowsAndThresholdAreExact` | four rows/three slow/22.000s class total | script; new |
| PC-137 / G-137 | tripwire: change below-threshold condition from <5 to <=5 | `TestDurationTripwireTests.C475_ExpandedRowsAndThresholdAreExact` | 5.000 row appears; 4.999 row does not; three hits | script; new |
| PC-138 / G-138 | tripwire: continue on unparsable duration instead of returning nonzero | `TestDurationTripwireTests.C475_InvalidInputIsNotGreen` | invalid-duration fixture produces nonzero diagnostic | script; new |
| PC-139 / G-139 | Bypass only op.TaskId != source.TaskId in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | task row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (task) |
| PC-140 / G-140 | Bypass only op.Id != id in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | operation row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (operation) |
| PC-141 / G-141 | Bypass only PathsEqual(op.RepositoryPath, source.RepositoryPath) in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | repository row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (repository) |
| PC-142 / G-142 | Bypass only PathsEqual(op.WorktreePath, source.WorktreePath) in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | path row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (path) |
| PC-143 / G-143 | Bypass only PathsEqual(op.GitDirectory, request.GitDirectory) in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | git-directory row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (git-directory) |
| PC-144 / G-144 | Bypass only PathsEqual(op.CommonDirectory, common) in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | common-directory row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (common-directory) |
| PC-145 / G-145 | Bypass only op.SourceFullRef != source.SourceFullRef in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | source-ref row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (source-ref) |
| PC-146 / G-146 | Bypass only op.TargetFullRef != source.TargetFullRef in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | target-ref row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (target-ref) |
| PC-147 / G-147 | Bypass only op.TargetBeforeSha != request.ExpectedTargetSha in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | target-sha row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (target-sha) |
| PC-148 / G-148 | Bypass only op.ExpectedDeletionSha != request.ExpectedSourceSha in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | deletion-sha row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (deletion-sha) |
| PC-149 / G-149 | Bypass only op.VerifiedSourceSha != request.ExpectedSourceSha in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | verified-sha row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (verified-sha) |
| PC-150 / G-150 | Bypass only op.CleanupStartedAt is null in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | cleanup-intent row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 pc31-cleanup-intent |
| PC-151 / G-151 | Bypass only op.Phase is not (CleanupStarted or Complete) in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | phase row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (phase) |
| PC-152 / G-152 | Bypass only !op.Active in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | inactive row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (inactive) |
| PC-153 / G-153 | Bypass only AgentTaskLandingState.HasPublication schema support predicate in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause; for the same invariant duplicated in HasIdentity and HasPublication, bypass its duplicate conjuncts together, not unrelated identity checks | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | schema row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (schema) |
| PC-154 / G-154 | Bypass only HasPublication RemoteConfirmedAt prerequisite in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause; for the same invariant duplicated in HasIdentity and HasPublication, bypass its duplicate conjuncts together, not unrelated identity checks | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | unconfirmed row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (unconfirmed) |
| PC-155 / G-155 | Bypass only HasPublication exact RecoveryRefPrefix prerequisite in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause; for the same invariant duplicated in HasIdentity and HasPublication, bypass its duplicate conjuncts together, not unrelated identity checks | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | operation-namespace row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (operation-namespace) |
| PC-156 / G-156 | Bypass only HasPublication destination equals target prerequisite in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause; for the same invariant duplicated in HasIdentity and HasPublication, bypass its duplicate conjuncts together, not unrelated identity checks | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | destination row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (destination) |
| PC-157 / G-157 | Bypass only HasPublication valid RemoteFingerprint prerequisite in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause; for the same invariant duplicated in HasIdentity and HasPublication, bypass its duplicate conjuncts together, not unrelated identity checks | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | fingerprint row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (fingerprint) |
| PC-158 / G-158 | Bypass only HasPublication ConfirmationMethod prerequisite in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause; for the same invariant duplicated in HasIdentity and HasPublication, bypass its duplicate conjuncts together, not unrelated identity checks | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | confirmation-method row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 V36/pc30 authority (confirmation-method) |
| PC-159 / G-159 | Bypass only HasPublication observed-remote OID prerequisite in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause; for the same invariant duplicated in HasIdentity and HasPublication, bypass its duplicate conjuncts together, not unrelated identity checks | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | observed-sha row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 pc30-observed-object |
| PC-160 / G-160 | Bypass only AuthorityAsync leases.Owns(request.Lease, common) in GuardedWorktreeRemoval.AuthorityAsync / the named HasPublication predicate; preserve every other clause | `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation` | lease row: Mutations empty, IsClean=false, sentinel and branch retained; valid companion reaches both mutations | policy; C448 pc39-forged-lease |
| PC-161 / G-161 | AuthorityAsync: omit only the source iteration from cleanup's pin checks | `LandingProtocolGuardTests.C475_CleanupPinsAreFresh` | source row changes only modeled pin after publication/ignored refusal; require recovery_pin_changed, no deletion | policy; C448 pc47 cleanup/source |
| PC-162 / G-162 | AuthorityAsync: omit only the target-before iteration from cleanup's pin checks | `LandingProtocolGuardTests.C475_CleanupPinsAreFresh` | target-before row changes only modeled pin after publication/ignored refusal; require recovery_pin_changed, no deletion | policy; C448 pc47 cleanup/target-before |
| PC-163 / G-163 | AuthorityAsync: omit only the prepared iteration from cleanup's pin checks | `LandingProtocolGuardTests.C475_CleanupPinsAreFresh` | prepared row changes only modeled pin after publication/ignored refusal; require recovery_pin_changed, no deletion | policy; C448 pc47 cleanup/prepared |
| PC-164 / G-164 | Pump: mark input consumed when its injected read fails | `SessionQueueReceiptPlumbingTests.C475_PumpPersistsCompleteLinesOnce` | read-fails row eventually stores exactly one complete original record | policy; new |
| PC-165 / G-165 | Failed-delivery revert: reset only DeliveryAttempts to zero | `SessionMessageQueueDeliveryVerificationTests.Attempt_metadata_survives_the_revert_a_failed_delivery_does` | attempt count remains 1 | policy; new |
| PC-166 / G-166 | Failed-delivery revert: clear only LastDeliveryStartedAt | `SessionMessageQueueDeliveryVerificationTests.Attempt_metadata_survives_the_revert_a_failed_delivery_does` | start timestamp remains nonnull | policy; new |
| PC-167 / G-167 | tripwire: compare simple names only | `TestDurationTripwireTests.C475_SimpleAndFullNamesAreExact` | fully-qualified exact allowlist exits 0 | script; new |
| PC-168 / G-168 | tripwire: change only equality comparer to Ordinal | `TestDurationTripwireTests.C475_SimpleAndFullNamesAreExact` | mixed-case-full invocation exits 0 | script; new |
| PC-169 / G-169 | tripwire: continue on absent duration | `TestDurationTripwireTests.C475_InvalidInputIsNotGreen` | missing-duration invocation nonzero diagnostic | script; new |
| PC-170 / G-170 | tripwire: permit negative TimeSpan | `TestDurationTripwireTests.C475_InvalidInputIsNotGreen` | negative-duration invocation nonzero diagnostic | script; new |
| PC-171 / G-171 | tripwire parse catch: return success with an empty report | `TestDurationTripwireTests.C475_InvalidInputIsNotGreen` | malformed-XML invocation nonzero diagnostic | script; new |
| PC-172 / G-172 | EnqueueAsync Now branch: bypass acquiring the session semaphore, retaining normal delivery | `SessionMessageQueueDeliveryVerificationTests.Mode_Now_waits_for_the_per_session_lock` | while first send holds its barrier, second task is incomplete and no second body is written | policy; new |
| PC-173 / G-173 | Real verifier invocation: redirect its artifacts/output placement into the fixture source obj directory instead of the private verification location | `AgentTaskLandVerifierTests.C448_V34_RealTUnitSelectionRequiresExecutedPassingTests` | source obj directory absent after each invocation; original private sentinel remains | real; C448 V34 source-tree preservation |
| PC-174 / G-174 | CheckTargetAsync: drop only symbolic.Succeeded from the target-checkout predicate | `LandingProtocolGuardTests.C475_TargetSymbolicQueryFailureRefuses` | return correct target-ref text with nonzero exit; assert target refusal before any mutation | policy; new |
| PC-175 / G-175 | WhenIdle enqueue: omit the post-commit immediate flush of an already-idle session | `SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt` | one complete native receipt and same queue row confirmed without manually flushing or inserting another TurnEnd | native; new |
| PC-176 / G-176 | TargetCheckoutAsync / CheckTargetAsync: bypass only rows.Count <= 1 | `LandingProtocolGuardTests.C475_TargetRegistrationAuthority` | ambiguous row: ambiguous_target_checkout before target mutation; all other target coordinates valid | policy; new |
| PC-177 / G-177 | TargetCheckoutAsync / CheckTargetAsync: bypass only !rows[0].Locked | `LandingProtocolGuardTests.C475_TargetRegistrationAuthority` | locked row: target_registration_unavailable before target mutation; all other target coordinates valid | policy; new |
| PC-178 / G-178 | TargetCheckoutAsync / CheckTargetAsync: bypass only !rows[0].Prunable | `LandingProtocolGuardTests.C475_TargetRegistrationAuthority` | prunable row: target_registration_unavailable before target mutation; all other target coordinates valid | policy; new |
| PC-179 / G-179 | TargetCheckoutAsync / CheckTargetAsync: bypass only op.TargetCheckoutRecorded | `LandingProtocolGuardTests.C475_TargetRegistrationAuthority` | unrecorded row: target_checkout_changed before target mutation; all other target coordinates valid | policy; new |

#### Fixture setup and selector preservation

New helpers are test-only. `ControlledLandingGitTests` is Unit and constructs only model state/private files. `LandingProtocolHarnessTests` and `LandingProtocolGuardTests` are Integration because they use isolated real DB clones. They do not launch children. Native/script fixtures and the existing wrapper classes retain their assembly-local limiter; explicitly add the two new process classes to the limiter census. Do not weaken that census to avoid registering a new class.

Every new C475 method in the PC table is a required executable test, with the setup below and the table's decisive assertion:

- The six ControlledLandingGitTests methods exercise the model directly. Use independent source, target, remote and pin OIDs; capture before/after snapshots. The unsupported-command/worktree calls must throw a descriptive exception. Query-error injection must preserve exit 128 versus absent exit 2. Owned-call tests block the supplied `started` callback with a TaskCompletionSource, observe one invocation and no mutation while blocked, then release it and await completion. No timeout-only oracle.
- Harness DI test checks reference identity of the registered ILandingGit and the explicit instance used by each service factory before invoking them. The acknowledgement test uses a fresh DbContext inside the acknowledged callback. Alias test acquires by canonical repository then source path and requires the second acquisition to fail nonblockingly. The cleanup test reaches publication through the protocol, creates an ignored private sentinel, then calls the real GuardedWorktreeRemoval through the worktree adapter and requires refusal and real evidence-read trace.
- `C475_RecreationPreservesCommittedModeAndAttempts(string mode)` and `C475_ModesAreReachedThroughProtocol(string mode)` each use Fresh/AlreadyPresent/ResumePublication/CleanupRetry. Assert the actual transition sequence and committed operation/request identity before and after service recreation. Resume uses the existing post-LocalTargetAdvanced acknowledgement fault shape; CleanupRetry first proves publication then a real guarded ignored-content refusal. Recreating a service retains the DB and model; it never fabricates a replacement operation.
- Seven `C475_Task_*` methods change only the named task coordinate/status/filter using a fresh committed DB context during the verifier barrier, then release verification. Capture the outcome, fresh-read the operation, and assert the first post-verification refusal plus no target intent/FF/push. Target/source snapshots, pin refs and every unrelated predicate stay valid. The filter change must be nonempty and distinct, so `verification_filter_changed` is reachable.
- `C475_TargetSymbolicQueryFailureRefuses` supplies correct target-ref text with failed symbolic-ref exit and requires target_checkout_changed before mutation. `C475_TargetRegistrationAuthority(string change)` has ambiguous/locked/prunable/unrecorded rows plus a valid companion. Invoke the actual private CheckTargetAsync as the inspected removal-policy fixture does, with otherwise-valid replies; each bad row fails at its named refusal, while valid passes without mutation. Model the ambiguous registrations with the same canonical checkout so a later path mismatch cannot mask the bypass.
- Five `C475_Source_*` methods change only one otherwise-accepted inspection component at the post-verifier recheck; rejected supplies a matching nonnull snapshot with Accepted=false. Require the hook fired once, the specific source refusal and no next mutation. This avoids relying on real Git's earlier identity rejection to test protocol-level Matches.
- `C475_LandingKeepsLeaseThroughProtocol(string mode)` uses all four modes. At the protocol's first model query after service admission, record a separate nonblocking contender's acquisition result; dispose any accidentally acquired handle. Then assert that observation before asserting the captured protocol outcome. Repeat at an in-protocol barrier. Never suspend while waiting for a lock held by the test; no deadlock is the oracle.
- `C475_RemoteErrorDoesNotPublish` starts a valid Fresh operation and injects an observation error; assert no RemoteConfirmedAt, no cleanup and preserved sentinel/ref. `C475_PushExitDoesNotConfirmPublication` permits owned push exit success while independent subsequent remote observation denies containment; require no publication receipt or deletion, and the actual observation call.
- `C475_CleanupRetryRequiresFreshRemoteContainment` reaches CleanupRetry causally, rewrites only the remote model so it no longer contains the source, and requires the fresh observation and preserved residue. `C475_CleanupPinsAreFresh(string pin)` has source/target-before/prepared rows; reach a published, prepared-source cleanup refusal first, corrupt only that pin, repost, then require `recovery_pin_changed` and no deletion.
- The three new census methods exercise extracted source-classification logic using bare construction, comments/strings/type-only mentions, and real helper construction, with both positive helper spellings. The owner-contract method verifies the real invocation of AddDelegationWorktreeGraph in each actual helper, and resolves the graph through each helper in the Integration harness test. Do not satisfy either by adding a comment or by searching the caller filename.
- `C475_CoverageAllocationMatchesTheLegacyTupleManifest` uses a checked-in test-data manifest/static data generated from the landed original 169 tuples. It reflects source test attributes without executing their Git bodies and compares typed tuples to the original plan's complete allocation, including all 60 designated native tuples. Copy the same manifest to the evidence directory for the actual-TRX join; do not make repeatable tests depend on ignored .antiphon data, Git invocation or the current branch's pre-edit files.
- Model smoke evidence includes executing the 140 controlled rows, inspecting the entire helper call closure for Process.Start/real Git/verifier construction, and reporting actual model/owned-command traces. Strict dispatch and type identity must make zero native Git/verifier launches structural; a zero count in an otherwise unused fake counter is insufficient.

| Existing C448 selector/family | Required surviving exact selection / redirect |
|---|---|
| pc19 nine source boundaries | Keep all nine LandingSourceBoundaryControlTests methods in PC-22..30, same 30 argument rows. Their direct calls construct AgentTaskLandBoundaryControlledTests and invoke its C448_V10_EachAcknowledgedBoundaryRechecksSource method with the original arguments. Preserve this exact method name when moving the body. |
| pc43/44 six writer/order wrappers | Keep LandingAdmissionControlTests.C448_V14_shared_land_first, C448_V14_shared_dispatch_first, C448_V14_shared_dispatch_before_acquire, C448_V14_follow_up_land_first, C448_V14_follow_up_dispatch_first, C448_V14_follow_up_dispatch_before_acquire. Each keeps all four modes and calls AgentTaskLandAdmissionControlledTests.C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther (the copied method drops only the misleading Real word); no LandingSafetyHarness in the redirected body. |
| pc50 admitted/already-present/cleanup/hold | Keep C448_V14_AdmittedModes (4), C448_V14_AlreadyPresentAttempt (3), C448_V14_CleanupRetryAttempt (3), C448_V14_ClaimHoldAttempts (8), in addition to the 24 writer wrappers. Redirect calls to AgentTaskLandConcurrencyControlledTests.C448_V14_EveryModeHonoursWriterAndLeaseHolds with unchanged holder/mode arguments. Total remains 42. |
| pc20 target decisions/post-FF; pc21 target mutation; pc22 pins; pc23 hostile rebase | Keep exact policy or real methods specified by PC-43..61. No red at an incidental setup error or a later guard. The original Boundary class now supplies the planned native capstones only. |
| pc42 settlement/local merge/nested creation | Keep exact original real methods PC-73..75, plus the four native both-orders Concurrency rows in ordinary V-3. Shared lease/cross-process implementation is not replaced. |
| pc8 private verifier output | Cancel uses surviving V35 no-argument method (PC-76). Pass/fail/no-selection uses V34 (PC-77); do not retain a wrapper which reintroduces the deleted V35 pass/fail runs. |
| pc25 counters/verifier | Keep the nine Unit counter rows and exact V34/V32 methods PC-78..83. Other unchanged state-machine verification-label PCs retain their old exact selectors. |
| pc30/31/39 crossed cleanup authority, pc32/pc7 content rereads | Keep LandingRemovalPolicyControlTests exact methods PC-127..131 and PC-139..160; separate authority fields have separate mutants. Existing policy wrappers already avoid real Git. |
| pc1..18, pc24..29, pc33..41, pc45..49 outside the substitutions above | Historical ledger selectors for untouched identity, publication, deletion-CAS, journal, crash and recovery tests remain callable and real where currently real. Do not rewrite their old ledger evidence or claim Pending controls ran. They stay nightly/integration coverage; change to their helper/production guard activates expanded scope below. |

The table names moved method identities, not a license to rename wrappers. The exact current direct-call names are mapped above; Code must report old -> new class/method/arguments for each of the 72 wrapper rows. A wrapper must not silently keep the old real body after the matrix attributes move.

### Out of scope

- Existing LandingSafetyHarness/LandingGitFixture, real crash-worker setup, production landing Git/protocol/lease/removal semantics and all original dangerous capstones remain protected. The normal scope does not rerun unrelated full crash/publication/recovery matrices. If Code changes either shared helper or any such production semantics, add the exact affected classes before running and update this cost floor; do not use the narrow estimate to omit them.
- Standalone real provider canaries, enabling Grok question chrome, SSE/network reconnect, production runner adoption, OS power loss and C467 caller outbox are not introduced by the test pump. Native inbox FakeClaude plus real queue/runner is the accepted S2 fixture boundary, not evidence from a real Grok subscription.
- Empty/dormant Grok popup-specific early returns cannot currently be reached by an actual measured popup. PC-113 pins the no-guess contract and PC-109/110 the active fresh-working protection. A future activation needs its own measured evidence and independent bypass controls for the newly reachable early returns.
- No new E2E, client build, full assembly/namespace regression, timing-threshold assertion, parallelism increase, live provider home access or restart. Those add no necessary evidence for unchanged files. A new production transport change requires the relevant native PTY project classes, run after Antiphon.Tests.
- S5 prose agreement and historical timing labels use manual/diff/data review, not tests that match their own wording. This is the only zero-mutation prose exclusion; executable lane/census/script guards have PCs above.

### Cost

**Mandatory scheduling floor, estimated at this TestDesign:** Code ordinary V/R **56 minutes**; separate Mutation **530 minutes**; combined setup + ordinary + all PC red/restore/green **586 minutes (9h 46m)**, before required Review. These are budget floors under the explicitly serial strategy below, not measured new-harness performance, a wall-clock assertion or permission to skip a remaining case when a budget expires. Allocate more on a loaded shared host. Recalculate transparently from actual initial timings; a faster completed run is acceptable if its complete per-ID evidence accounts for the difference.

| Ordinary Code component | Floor (minutes) | Basis |
|---|---:|---|
| Isolated initial build | 1.5 | Plan measured 94.33s; estimate rounded |
| Unit lane | 2.0 | Historical 70.29s plus current additions; not the old failure counts as acceptance |
| 140 controlled rows + 72 redirected wrapper rows + model/harness/guard supports | 3.0 | Estimate; real DB clones, no Git/verifier processes |
| 60 real matrix capstones | 25.6 | Historical selected body sum 1534.237s; new setup/load can add |
| Eight real verifier rows | 3.0 | Real generated-project startup/build retained |
| Eleven status rows + 16 tripwire fixtures | 3.0 | Real private pwsh children |
| Eight existing native queue rows | 3.0 | Includes swallowed-Enter negative, no timeout reduction |
| Fifteen new plumbing cases | 6.0 | Fault recovery, real PTY subset, DB pump rows |
| Delivery verification/interrupted-attempt classes | 3.0 | Existing controlled queue evidence plus two new pins |
| Additional runner discovery/startup | 3.0 | Batched ordinary classes and fixed output reused |
| Tuple/TRX joins, duration rebaseline, source/bundle review | 2.0 | Manual plus deterministic artifact audits |
| **Sum / rounded floor** | **55.1 / 56** | Expected working range roughly 56-95 minutes, not a guarantee |

**Mutation inventory is 179 PCs: 135 policy/model, 21 real Git/verifier, 11 native queue, 12 script.** There are **111 distinct exact method selectors**, including 49 named new methods, not 179 different test methods. Same-method controls remain separate defect cycles; baseline may be reused only for identical filter/arguments and implementation fingerprint. No independent-batch saving is assumed because most mutants share protocol, queue or helper files.

| Mutation component | Calculation | Floor minutes |
|---|---|---:|
| Initial setup/build and unchanged exact-method green baselines | 1.5 build + 111 x 0.3 startup + 38 body | 72.8 |
| Compiled mutant/restoration rebuilds | (135 + 21 + 11) x 2 x 0.5; the 12 script mutations require fresh pwsh execution but no C# rebuild | 167.0 |
| Red + restored-green runner startup | 179 x 2 x 0.3 | 107.4 |
| Policy/model method bodies | 135 x 2 x 0.10 | 27.0 |
| Real Git/verifier method bodies | 21 x 2 x 2.0; all method rows, not a class sweep | 84.0 |
| Native queue method bodies | 11 x 2 x 0.75 | 16.5 |
| Script-method bodies | 12 x 2 x 0.15 | 3.6 |
| Apply/restore/fingerprint per mutation | 179 x 0.2 | 35.8 |
| Final ledger completeness and clean-source audit | 10 | 10.0 |
| **Sum / rounded floor** | **524.1 / 530** | Approximately 8h 50m for Mutation alone |

The 0.3-minute runner startup and 0.5-minute incremental rebuild are deliberately estimates, not extrapolated test-body measurements. Method selections such as V32 pins run their complete argument lists, so they are budgeted as method bodies, not a single row. If process setup makes a two-minute native method estimate low, multiply its measured median by its remaining PC cycles and increase the remaining budget. If a proven isolated policy runner is proposed, first demonstrate exact test/assembly/source equivalence and reprice it; the old C448 adapter evidence does not automatically authorize an unbuilt C475 adapter.

Savings are specific: the original 109 replaced rows cost 52m17.4s. Against an estimated 3 minutes for the larger controlled/support batch, the matrix substitution may save about **49 minutes per comparable ordinary pass**. V35 dedup removes **15.93 seconds of historical body work**, not all verifier startup. The 60 real rows still cost at least their measured 25m34.2s allocation. These savings do not cancel the one-time 179-control trust-establishment battery. No full-suite or parallelism saving is claimed.

#### Commands and evidence contract

Use the current testing owner's isolated-output procedure; preserve the trailing slash, separate TRX directories, and actual exit code. Code's minimum ordinary selections, serialized:
```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c475/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c475-code-unit

dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c475/ -- --treenode-filter '/*/*/(AgentTaskLandBoundaryControlledTests)|(AgentTaskLandAdmissionControlledTests)|(AgentTaskLandConcurrencyControlledTests)|(LandingProtocolHarnessTests)|(LandingProtocolGuardTests)|(LandingSourceBoundaryControlTests)|(LandingAdmissionControlTests)/*' --report-trx --report-trx-filename controlled.trx --results-directory .antiphon/c475-code-controlled

dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c475/ -- --treenode-filter '/*/*/(AgentTaskLandBoundaryTests)|(AgentTaskLandAdmissionTests)|(AgentTaskLandConcurrencyTests)|(AgentTaskLandVerifierTests)/*' --report-trx --report-trx-filename real.trx --results-directory .antiphon/c475-code-real

dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c475/ -- --treenode-filter '/*/*/(DelegateScriptLandStatusTests)|(TestDurationTripwireTests)|(SessionMessageQueuePtyIntegrationTests)|(SessionQueueReceiptPlumbingTests)|(SessionMessageQueueDeliveryVerificationTests)|(SessionMessageQueueInterruptedAttemptTests)/*' --report-trx --report-trx-filename delivery-script.trx --results-directory .antiphon/c475-code-delivery-script
```

Code may split the last invocation into those exact classes for foreground progress; record its additional startup cost. Unit already includes ControlledLandingGitTests, GrokQuestionPopupTests and the unchanged policy controls. V-9 lists the minimum decisive methods within the selected delivery class; Code's two new pins make that class touched. If a filter parses but matches zero or extra classes, correct it and rerun; no inferred coverage.

Historical reproduction uses saved broad.trx and unit-clean.trx in `C:\Antiphon\worktrees\card-task-722e21e7\.antiphon\profile-722e21e7`, and a byte-preserved copy of the allowlist from the profiling commit. Run `pwsh -NoProfile -File scripts/test-duration-tripwire.ps1 -Trx <one saved TRX> -Allowlist <original allowlist copy>` for each, recording exit/hit count; nonzero is expected for the historical 174 and 2 hits. Then run against all four fresh Code TRX artifacts with the final allowlist, recording each file's class/row/body counts and reasons for retained real-class exceptions. Do not combine elapsed time from overlapping historical runs or change raw originals.

Each Mutation row supplies its exact class/method. For example PC-22:
```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c475-pc/ -- --treenode-filter '/*/*/LandingSourceBoundaryControlTests/C448_V10_after_fetch' --report-trx --report-trx-filename pc-22-red.trx --results-directory .antiphon/c475-mutation/pc-22/red
```

Apply the PC-22-only patch before that red command. Restore only the saved fixed bytes it changed, refresh source timestamps, and rerun the same method with `pc-22-green.trx` and a fresh `pc-22/green` directory, rebuilding as above. Baseline uses `pc-22/baseline`. Substitute only the table's exact selector and PC number for other cycles. Do not use --no-build after a C# mutant/restoration or run a class/suite for red/green. Script mutants still start a new pwsh process from the exact test fixture; record the script fingerprint.

Keep an ignored evidence manifest with `G, PC, implementation SHA, filter, expanded arguments, baseline/red/restored counts, intended assertion, patch, restored fingerprint, DLL hash, TRX paths, build/startup/body/wall time`. Retain supporting native file/DB receipts, queue identities, raw input sequence, owned pump completion and tuple mapping. Code's ordinary evidence and Mutation's control ledger are separate deliverables. Required Review verifies the layer substitution and completeness at the restored committed implementation; an unexpected red failure or surviving mutant returns to Code.

**Pre-handoff audit:** affected bodies and fixtures read; guards=179, mapped=179, missing=0, duplicate PC mappings=0; PC-1..179 each defined with a concrete compiling defect, exact existing or specified-new method and decisive red assertion. Existing selectors were checked against current class/method declarations; new methods have fixture setup above. S1-S6 and the original allocation text are unchanged. This TestDesign runs static document/selector audits only; no implementation, test, build or PC execution is claimed.

**Next:** Code implements S1-S6 plus this appendix and runs ordinary V/R, then hands off to **Mutation**, which must hand off to **Review** on success (`review-required: yes`). Do not fold Mutation into Code or route directly to Land. Expected restart remains **none**.
