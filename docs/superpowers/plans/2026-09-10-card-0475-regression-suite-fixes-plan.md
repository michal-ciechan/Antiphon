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
