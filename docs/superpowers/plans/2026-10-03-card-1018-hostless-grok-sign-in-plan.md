# CARD-1018: platform-neutral host-less Grok sign-in refusal

Date: 2026-10-03. Plan task: `245a0a09`.
Inspected base: `2d3c582416c5610d72e53df3e790bc114d87ad82`.

Fix the host-less `ProviderSignInRequiredException` remedy so it identifies the
session-runner user and affected `GROK_HOME` on either operating system. The
defect remains present at the inspected base; CARD-1006 did not supersede it.
This dispatch writes a plan only. **Next: TestDesign**; the brief did not fold
that stage into Plan. The verification design below is the proposed bounded
scope for TestDesign to freeze before Code.

## Ground truth

CARD-1018 was read with `scripts/card.ps1 get CARD-1018 -Board Antiphon`, its
JSON detail, and `history`. Its sole revision at inspection was this Plan
dispatch's move to InProgress. `terminalReason` and `completedAt` were null;
there was no prior verdict or supersession in the returned history.

| Card assumption | What the code does at the inspected base | Planning consequence |
|---|---|---|
| Host-less Grok sign-in 409 names a Windows user. | `server/Application/Exceptions/ProviderSignInRequiredException.cs`, `MessageFor`, selects the host-less branch with `string.IsNullOrWhiteSpace(runnerId)` and returns the Windows-specific sentence. | Change only this branch's human-readable message. Null, empty and whitespace runner IDs keep their existing normalization and branch selection. |
| The remedy should identify the affected home. | The constructor receives `grokHome`; `BuildExtensions` already returns it as `grokHome`, but the host-less message ignores it. | Include that supplied value as `GROK_HOME=<value>` without resolving another path or reading credentials. |
| Runner-bound wording is already suitable. | The other Grok branch names the runner and its `GROK_HOME`, and says to log in inside that runner. | Preserve the runner-bound branch. |
| CARD-1006 fixed a separate message. | `src/Antiphon.Agents.Pty/GrokDetectors.cs`, `BlockReason`, already says session-runner user and runner user's `GROK_HOME`; commit `7d6f0bbeeedd3b64700c9bea5517bcbd1bad7599` is in this base's history. Its `GrokSignInPromptDetectorTests.C1006_Block_reason_is_platform_neutral` covers that wording. The exception's last change predates it (`c7d92e5c0`). | Do not edit the detector, its fixtures, or CARD-1006's plan. The HTTP message still needs its own fix. |
| Create and retry can return the same defect. | `AgentTaskService.CreateAsync` and `RetryAsync` both call `RefuseUnauthenticatedGrokAsync`; its local-store refusal constructs this exception without a runner ID. | The shared message fix reaches both paths. No service, routing or retry changes are necessary. |
| This exception text becomes HTTP detail. | `server/Api/Middleware/ExceptionMiddleware.cs`, `Classify`, maps `HttpException` to `(StatusCode, Message)` and writes `detail`; the constructor supplies 409 and `provider_sign_in_required`. | Preserve the middleware and machine-readable response contract. Source inspection establishes the unchanged transport mapping; the proposed runtime test exercises the service exception, not HTTP serialization. |
| Existing coverage catches the wording defect. | `ProviderSignInRequiredCreateTests.Create_returns_409_provider_sign_in_required_when_the_store_is_Absent` asserts code/home/extensions and no queued task, but not the message. `PhoneHomeTaskCreateTests.Runner_bound_grok_create_refuses_a_logged_out_runner` already rejects Windows wording for the runner-bound case. | Strengthen the existing host-less test and retain the existing remote regression class. No new test harness is needed. |

Owners read for this scope: `docs/project-context.md`, `docs/ops-http.md`,
`docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, the Grok section of
`docs/agent-kinds.md`, and the checkpoint/build sections of
`docs/testing-and-build.md`.

## Decisions

- **D-1: use one platform-neutral, home-specific remedy.** Replace the host-less
  message with the text below, substituting only the supplied `grokHome` for
  `{grokHome}`. This removes the incorrect platform assumption and tells the
  operator which already-reported store needs login. Reject merely deleting
  `Windows`: that would still leave the target home unstated. Reject OS
  detection or a container-only remedy: neither is needed for this shared code.

  > Grok is not signed in on this host (GROK_HOME={grokHome}). Run `grok login` as the user that runs the session-runner, using that GROK_HOME, pick another agentKind, or re-send with allowUnauthenticatedProvider=true to queue anyway.

- **D-2: preserve the response and admission contracts.** Keep 409, error code,
  `agentKind`, `grokHome`, `remedy`, optional `runnerId`, public properties and
  constructor behavior. Preserve both alternative actions in the message.
  Do not modify runner-bound Grok, `ForCodex`, `CodexMessageFor`, or the credential
  probes. CARD-1023's allow-by-default compatibility policy remains separate;
  no version gate, freshness refusal, rerouting or new capability prerequisite
  belongs in this change. Reject centralizing the detector and HTTP prose:
  their launch-block and create/refusal contexts differ.
- **D-3: extend existing behavioral coverage.** Add assertions to the existing
  host-less create-refusal method rather than creating a new fixture or a
  full-message snapshot that copies the concatenation. Check the actual supplied
  home and actionable wording. Preserve the test's no-queued-row assertion.
  Reject real provider launches and a native backend matrix for this pure
  message edit. CARD-1022's legacy inbox ConPTY deprecation creates no dependency.
- **D-4: portable execution with separate TestDesign.** The card is `Any` and
  this edit has no OS-dependent behavior. Resolve current runner preferences
  at dispatch, omit `-Runner` and `-Platform`; use `-Platform Any` only to remove
  an inherited OS pin. TestDesign must finalize the checkpoint roster and any
  post-land positive control; Code must not treat this Plan task as test evidence.

## Scope and implementation slice

**S1 — host-less message and its existing regression assertions** (one commit).

| File | Change | Verification |
|---|---|---|
| `server/Application/Exceptions/ProviderSignInRequiredException.cs` | Replace only the host-less return expression in `MessageFor` with D-1. | V-1 through the existing local create-refusal method; source diff confirms other branches are untouched. |
| `tests/Antiphon.Tests/Application/ProviderSignInRequiredCreateTests.cs` | In `Create_returns_409_provider_sign_in_required_when_the_store_is_Absent`, add the V-1 assertions below; keep the existing fixture and second test. | V-1 and R-1, CP-2. |

Existing, unchanged regression file:
`tests/Antiphon.Tests/Application/PhoneHomeTaskCreateTests.cs` (R-2, CP-2).
Do not change `AgentTaskService.cs`, provider docs, runtime detectors, runner
code, deployment scripts, or test infrastructure for this message fix.

Commit and push S1 before checkpoint execution. Keep source frozen while the
rows execute. Any discovered failure is classified at the implementation task's
recorded base using only its failing test selection; inherited failures are
disclosed, not repaired or hidden by retries. Review verdicts are regression-only.

## Collision and placement notes

GET `/api/runner-defaults` and GET `/api/session-runners` were read at approximately
23:08 UTC on 2026-10-03. Defaults revision 2 selected the Linux lane, with no
per-kind override; eligible Linux and Windows lanes were available, and another
catalogue entry was unavailable/draining. These observations are not host pins.
Re-read both routes when dispatching; the orchestrator also checks pipeline and
host occupancy. Checkpoint Group names below identify the portable lanes.

The board-scoped active-task list confirmed CARD-1011 Code `b657a1e2` and
CARD-1008 Review `ca3d4a6b` as Dispatched, and CARD-0959 TestDesign `4170230f` as
Dispatched during this inspection. Their detailed summaries did not expose a
scope field; collision boundaries below use the caller's supplied footprints,
not a claim to have inspected unpublished diffs.

| In-flight work | Collision assessment and ordering |
|---|---|
| CARD-1011 Code `b657a1e2`: orchestrator bundle, provider docs, routing tests | No intended file overlap. Avoid those shared areas; do not alter `PhoneHomeTaskCreateTests.cs` just to strengthen adjacent routing coverage. Recheck the actual footprint before Code. |
| CARD-0959 re-freeze: runner Codex version probe/reporting | No intended file overlap. The exception also contains Codex helpers, so keep that branch untouched. CARD-1023 supersedes the old version-floor admission decisions; do not copy those decisions from CARD-0959's older plan. |
| CARD-1008 Review: `scripts/deploy-server2.ps1`, `scripts/c590-remote.sh` | No source overlap or runner deployment dependency. Coordinate shared AppHost activation with any landing/rollout operation; do not edit its scripts. |

There is no required code landing order between these cards and CARD-1018 under
the listed footprints. Defer/reconcile if a refreshed footprint includes either
S1 file. Keep the planning artifact itself confined to this card's new plan path.

## Plan-stage verification proposal (superseded by the TestDesign freeze below)

This is a proposed ordinary Final Code/Review scope for the separate TestDesign
stage. No test or build ran in Plan. All runtime counts below are required future
outcomes, not measured results.

| ID | Coverage and assertions | Existing class/method |
|---|---|---|
| V-1 | A refused host-less create reports `StatusCode == 409`; message contains `GROK_HOME=` followed by the actual temporary home, `grok login`, `as the user that runs the session-runner`, `using that GROK_HOME`, `pick another agentKind`, and `allowUnauthenticatedProvider=true`; message has no `Windows`. Keep code/home/agentKind/remedy assertions; also verify `RunnerId` is null and no `runnerId` extension exists. Preserve the no-queued-task assertion. | `ProviderSignInRequiredCreateTests.Create_returns_409_provider_sign_in_required_when_the_store_is_Absent` |
| R-1 | Explicit unauthenticated override still queues. | `ProviderSignInRequiredCreateTests.AllowUnauthenticatedProvider_queues_the_task` |
| R-2 | Runner-bound create remains supported, signed-out Grok returns its runner/home and a non-Windows remedy, and explicit override skips probing. Run all three existing methods. | `PhoneHomeTaskCreateTests.Runner_bound_create_admits_claude_and_explicit_codex`; `PhoneHomeTaskCreateTests.Runner_bound_grok_create_refuses_a_logged_out_runner`; `PhoneHomeTaskCreateTests.AllowUnauthenticatedProvider_does_not_probe_the_runner` |
| R-3 | Repository default Unit lane catches broader regressions and classification guards. No native/Pty suite, full assembly or namespace expansion is warranted by this message-only change. | Unit-category selection in CP-1. |

V-1 must fail against the old host-less return expression on the missing home
and/or Windows wording assertions. TestDesign should freeze one post-land,
method-scoped positive control restoring that original return expression and
running only `/*/*/ProviderSignInRequiredCreateTests/Create_returns_409_provider_sign_in_required_when_the_store_is_Absent`.
Require the intended assertion failure and a restored green run; fixture/build
errors and zero tests are not red. Execute any such control only in a separately
commissioned SourceLanding Mutation task, with external restoration evidence.
The planned ordinary method count stays five because S1 adds assertions, not tests.

CP-2 uses temporary credential homes, existing service fakes and the repository's
isolated test PostgreSQL infrastructure. It requires Docker/Testcontainers but
does not launch Grok or target a production runner. Retry and HTTP serialization
are covered here by inspection of the unchanged shared call path and middleware,
not claimed as independent executed tests. TestDesign can challenge that bounded
scope before freezing it; do not silently add runs during Code.

Use the checkpoint tool once for committed S1 with this plan and
`--rows CP-1,CP-2 --expected-source-sha <S1-sha>`. Bootstrap any missing tool output
through `scripts/build-slot.ps1` with an isolated `bin-.../` output and report
that bootstrap separately. The checkpoint driver takes its own slot; do not
double-wrap it. Await completion, continuing `wait` while exit is 75. Exit 4
means not run/blocked, never permission to bypass the slot gate. Preserve fresh
TRX rosters, unedited CHECKPOINT lines, actual counts/skips and SHA-validated
source/build receipts in the task report. Generated payloads remain ignored.
Run `scripts/check-evidence-diff.ps1` over the entire Code/Review task range.
Remove only task-owned alternate outputs after every run has finished.

### Cost

Estimated ordinary checkpoint floor: **9 minutes** (6 + 3), plus authoring,
optional tool bootstrap and slot wait. CP-1's `Min=1` is only a nonzero Unit-lane
floor; report the actual expanded count and disclose every failure/skip. CP-2
requires exactly the five named results, zero failures and zero skips.
Separate ordinary Review repeats the frozen scope, estimated another 9 minutes.
One eventual method-scoped positive-control cycle is estimated at 6 minutes,
excluding SourceLanding setup; TestDesign owns that final budget.

#### Proposed checkpoints (historical; not the executable manifest)

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1018/` | portable-unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed; report complete Unit roster, 0 failed | 1 | 6 |
| CP-2 | S1 | CP-1 | portable-grok-refusal-integration | `/*/Antiphon.Tests.Application/(ProviderSignInRequiredCreateTests*)\|(PhoneHomeTaskCreateTests*)/*` | V-1, R-1, R-2 | all 5 listed methods, 0 failed/skipped | 5 | 3 |

## Landing and activation order

1. Publish this plan, then commission TestDesign to freeze the assertions,
   five-result integration roster, checkpoint manifest and positive control.
2. Code commits/pushes S1 and completes the frozen ordinary checkpoints. Separate
   Final Review assesses introduced regressions and verifies the full task diff
   and evidence. Record any required post-land verification companion before land.
3. Confirm publication of the Code task. This server Application change needs
   server/AppHost activation; it needs no runner binary, CLI update, migration,
   client rebuild, credentials, or runner rollout.
4. The orchestrator follows `docs/orchestration-loop.md` and
   `docs/apphost-runbook.md`: wait for active lands, update the canonical checkout,
   check restart locks, then use `scripts/restart-apphost.ps1` from that checkout.
   Never activate from this worktree. Confirm `/health` and GET `/api/version`
   match the activated canonical source SHA; publication alone is not activation.
5. Report activation separately from the checkpoint evidence. No production
   unauthenticated task is needed as a canary for a wording edit. Run the frozen
   post-land control only under its own commissioned verification task and do
   not claim PC-clean before its restoration receipt exists.

## Plan validation and handoff

Plan-stage validation is limited to source/card inspection, path and roster
checks, Markdown table consistency, and `git diff --check`. It does not claim
checkpoint importer or runtime execution. TestDesign must import the table with
the real checkpoint tool and finalize the verification design before Code.
No product decision is outstanding; D-1 through D-4 state the chosen design.

--- next stage ---
next: test-design
handoff: Freeze CARD-1018's two-file S1: platform-neutral host-less Grok remedy naming the supplied GROK_HOME, with assertions in the existing create-refusal method. Validate CP-1 Unit plus CP-2 five existing integration methods, import the manifest, and finalize one method-scoped post-land control. Preserve runner-bound/Codex messages and admission policy; recheck the named in-flight footprints.
artifact: docs/superpowers/plans/2026-10-03-card-1018-hostless-grok-sign-in-plan.md

## Verification design

**TestDesign freeze — task `f8613557`, 2026-10-03.** This appended section
supersedes only the proposed verification, cost and handoff above. D-1 through
D-4 and the two-file S1 fix design remain unchanged. The earlier table heading
is historical so the real importer finds exactly one `### Checkpoints` section.
No build, test, provider login or runtime mutation ran in TestDesign.

### Inspection

Bodies read at `c70a3ec7aa546fc05a209f056a11811c3dea15a4`:

| Bodies read | Boundaries -> coverage or exclusion |
|---|---|
| Entire `ProviderSignInRequiredException.cs`, including both constructors, `MessageFor`, `BuildExtensions`, `ForCodex`, `CodexMessageFor` and `BuildCodexExtensions`; `HttpException` constructors | Supplied home and host-less remedy -> V-1, G-1–G-4. Null runner property/extension and 409/code contract -> V-1. Bound-runner metadata -> R-2. Codex helpers remain byte-for-byte unchanged; no new Codex behavior or test claimed. |
| Both complete methods and all helpers/nested `TempDir` in `ProviderSignInRequiredCreateTests.cs` | Absent temporary store, host-less Shared create with override false/true -> V-1, R-1. Two `[Test]` methods, no argument expansion. Local fixture uses the assembly's shared test database, **not** an isolated schema; its override method deletes its own event/task rows. |
| All three methods, `Service`, `RepoRoot`, `ProbeDirectory` and `ProbeClient` in `PhoneHomeTaskCreateTests.cs` | Bound runner `server2`, negative Grok probe and override, explicit Claude/Codex admission -> R-2. Three `[Test]` methods, no argument expansion. Each method owns a cloned database despite the historical `CreateIsolatedSchemaAsync` name. |
| `TestDbFixture.cs`, `TestDbFixtureLifecycle.cs`, `SharedStoreWarmup.SelectionNeedsSharedStore`/call traversal, `MockEventBus.cs`, `RecordingSessionStopper.cs` | Real PostgreSQL 16 Testcontainer, migrated shared store and cloned databases; temporary directories; fake events/stopper. CP-2 is service/database integration, not an HTTP, runner or terminal fixture. No new fixture/helper/file is needed. |
| `AgentTaskService.CreateAsync` refusal call, task insert and save region; `RetryAsync`; entire `RefuseUnauthenticatedGrokAsync`; `GrokCredentialStore.cs` | Refusal precedes persistence -> V-1/R-2. Overrides -> R-1/R-2. Retry calls the same exception path before `RequeueAsync`; source inspection only. Credential `Absent`/`Empty`, API-key and profile exemptions are unchanged and excluded from new behavior coverage. |
| Entire `ExceptionMiddleware.cs` | `HttpException.StatusCode`/`Message` become HTTP status/detail and extensions are copied. Inspection only; V-1 does not execute HTTP serialization. |
| `GrokSignInPromptDetector.BlockReason`; entire `GrokSignInPromptDetectorTests.cs`; CARD-1006 fixture-pin, sign-in ordering and Windows-regression methods in `GrokLinuxBlockingPromptTests.cs` | CARD-1006's `C1006_Block_reason_is_platform_neutral` pins its complete launch-block text for Unix and Windows home strings, including device-auth and `auth.json`. R-3 selects that existing Unit test. CARD-1018 changes a separate HTTP refusal message and must not copy its text into the detector or alter its pinned fixtures/tests. |
| Entire `PlanTableImporter.cs`, `ManifestValidator.cs`, `CheckpointManifest.cs`, `AfterSelector.cs`, `ManifestLoader.cs`; `Program.Import`, `RowTimeout`; `CheckpointImportTests.cs`; checkpoint/build and Mutation owner sections | Header order, escaped OR pipe, first-section selection, build reuse, category roster handling, count/time distinction and independent PC phases -> CP-1/CP-2 and Cost. No checkpoint infrastructure edits. |

Boundary combinations are frozen as follows. Host-less + absent home + no
override is V-1; host-less + absent home + override is R-1. Bound runner +
`LoggedIn=false` + no override and bound runner + override are R-2. Explicit
runner-bound Claude and Codex are also R-2. Null/empty/whitespace runner IDs all
use the same unchanged `IsNullOrWhiteSpace` branch and normalization; only null
is exercised through the existing create fixture. Empty and whitespace are
excluded as separate executed combinations because neither branch selection nor
normalization changes. The actual temporary path on the selected OS supplies
the V-1 home oracle; Unix/Windows string interpolation has no path processing,
so a second OS run is not required for this literal edit. No claim of a new
cross-platform runtime matrix is made.

Missing setup resolved by the manifest: `GrokCredentialStore.ResolveAuthPath`
can inherit `GROK_AUTH_PATH`, overriding the fixture's temporary home. CP-2
therefore sets child-only `GROK_AUTH_PATH=`. Each Mutation phase must likewise
clear that variable in its owned driver process without reading its previous
value. The registry fixture supplies only its temporary `GROK_HOME`; its
non-null environment dictionary already prevents the ambient API-key fallback.
The tool binary is absent in this worktree; Code may need the separately
reported, slot-gated tool bootstrap described above. Docker/Testcontainers and
the build-slot broker must be available when Code runs; neither was exercised
by this docs-only freeze. No credentials or auth files were read.

Collision recheck at approximately 23:30 UTC used the board-scoped task list,
individual task detail and fetched, published branch tips (not unpublished
worktree diffs):

| Work | Observed state and footprint |
|---|---|
| CARD-1011 Code `b657a1e2` | Succeeded; `6bd81b8c696022b5d777896c0804aaad61f513d7`, compared with its recorded base `10f6249730dee4f467ae8d15c9d2b270b585493a`. Provider/orchestration docs, Grok qualification helpers/tests and `InstructionBundleTests.cs`; neither S1 file nor `PhoneHomeTaskCreateTests.cs` changes. |
| CARD-0959 TestDesign `4170230f` | Succeeded; `9bd096d2bb3695b7bd06a98cff15cbf6f96fcb05`, compared with base `bfc12c6d73f828cc864a205bc72967a8244ce883`. Only its runner-Codex plan changes. Preserve CARD-1018's Codex helpers. |
| CARD-1008 Review `ca3d4a6b` | Dispatched; published/base tip `bcfb52b6b9cc0c9e5156767e2e081d0dda752b3f`. Candidate differences cover deploy/remote scripts, their fixtures/tests, Docker docs and evidence; no S1 or CP-2 fixture overlap. Review has no additional published commit. |
| CARD-1006 | `7d6f0bbeeedd3b64700c9bea5517bcbd1bad7599` is already an ancestor. Its detector message and exact-string Unit pin remain untouched. |

No landing order dependency was found. Recheck footprints before Code if these
branches advance. The fetched master tip was
`2d3c582416c5610d72e53df3e790bc114d87ad82`; its
`scripts/lib/checkpoint-usage.ps1` census literal is **377**. CARD-1018 adds
**zero** tests to `Antiphon.Tests.Checkpoints`, adds zero test methods overall,
and must not change that census.

### Delivery inventory

New/changed asynchronous delivery paths: **zero**. S1 changes one synchronous
exception message, before task admission. No producer, destination, persistence
handoff, recovery mechanism or durable delivery identity changes. Consequently
busy-recipient, already-eligible-recipient and crash/enqueue-failure handoff
cases have no changed path to exercise and are excluded.

The existing override tests observe a queued task; R-1 joins the returned ID to
its stored row. That proves admission/persistence only. `MockEventBus`,
`RecordingSessionStopper`, the canned runner auth answer and a queue insert are
not delivery receipts. There is no recipient transcript in these fixtures and
no session-delivery claim: any future change to dispatch/input would require a
producer-to-recipient real-queue test and the matching complete `UserPrompt`
transcript. This design does not substitute an event, flag or acknowledgement
for recipient evidence.

### Proves it works now

- **V-1:** host-less create refusal gives the D-1 home-specific, platform-neutral
  remedy | service/database integration | CP-2,
  `ProviderSignInRequiredCreateTests.Create_returns_409_provider_sign_in_required_when_the_store_is_Absent`
  | one refused call, 409, correct properties/extensions and no stored task.
  In that existing method retain its current assertions and add the following
  independent assertions; do not call production message helpers for expected
  text and do not replace them with a full-message snapshot:

  ```csharp
  ex.StatusCode.ShouldBe(409);
  ex.AgentKind.ShouldBe("Grok");
  ex.Extensions["grokHome"].ShouldBe(grokHome.Path);
  ex.RunnerId.ShouldBeNull();
  ex.Extensions.ContainsKey("runnerId").ShouldBeFalse();
  ex.Message.ShouldContain("GROK_HOME=" + grokHome.Path);
  ex.Message.ShouldContain("grok login");
  ex.Message.ShouldContain("as the user that runs the session-runner");
  ex.Message.ShouldNotContain("Windows");
  ex.Message.ShouldContain("using that GROK_HOME");
  ex.Message.ShouldContain("pick another agentKind");
  ex.Message.ShouldContain("allowUnauthenticatedProvider=true");
  ```

  Keep the existing `Code`, `GrokHome`, remedy and extension agent-kind assertions,
  including the preceding `Extensions.ShouldNotBeNull()` check. Keep the final
  fresh-context `CountAsync(... Goal == "run on grok").ShouldBe(0,
  "a refused create must not leave a queued row")`. The old expression fails
  the supplied-home and neutral-actor/Windows assertions. TestDesign establishes
  that by reading the expressions; Code/Mutation must supply execution evidence.

### Guards the regression

- **R-1:** explicit host-less override remains queued | unchanged
  `ProviderSignInRequiredCreateTests.AllowUnauthenticatedProvider_queues_the_task`
  | returned `Status == Queued`, `AgentKind == Grok`, and the database row with
  the returned ID has `Status == Queued`; retain cleanup. Admission only.
- **R-2:** runner-bound behavior remains intact | all three unchanged
  `PhoneHomeTaskCreateTests` methods | roster and decisive assertions:

  | Exact method | Decisive existing assertions |
  |---|---|
  | `Runner_bound_create_admits_claude_and_explicit_codex` | Each persisted task has runner `server2` and the explicitly requested ClaudeCode/Codex kind. |
  | `Runner_bound_grok_create_refuses_a_logged_out_runner` | `Should.ThrowAsync<ProviderSignInRequiredException>`, code `provider_sign_in_required`, home `/state/grok`, runner `server2`, message contains `grok login` and not `Windows user`, provider calls exactly `["grok"]`, matching-goal row count zero. |
  | `AllowUnauthenticatedProvider_does_not_probe_the_runner` | Returned status Queued and `probe.Client.Providers.ShouldBeEmpty()`. |

- **R-3:** repository default Unit lane | CP-1 | nonzero execution, all failures
  and skips disclosed, full expanded TRX roster retained. In particular retain
  CARD-1006's exact `blockReasonExact` pin. This is the required default lane,
  not a claim that all Unit cases are specific to this edit or database-free.

### Guard inventory

The safety inventory includes the changed credential-targeting instructions
and the independently bypassable admission/persistence protections asserted by
this frozen integration scope. Machine-readable field values, login-command
spelling and alternative-action prose remain compatibility assertions in V-1;
they introduce no additional admission/recovery predicate. Unchanged Codex,
credential-state/profile exemptions and detector guards are outside S1, not new
CARD-1018 safety claims. No in-scope safety guard is left inspection-only.

| Guard | Plan reference and safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-1: host-less remedy identifies the **supplied** credential home. | PC-1 |
| G-2 | D-1: login actor is the user running the session-runner. | PC-2 |
| G-3 | D-1: no Windows-only restriction in the host-less remedy. | PC-3 |
| G-4 | D-1: login must use that same GROK_HOME. | PC-4 |
| G-5 | D-2/D-3: absent local store without override refuses admission. | PC-5 |
| G-6 | D-2/D-3: local refusal leaves no persisted queued task even when it throws correctly. | PC-6 |
| G-7 | D-2: explicit local override actually admits a queued task. | PC-7 |
| G-8 | D-2: known signed-out bound runner refuses admission. | PC-8 |
| G-9 | D-2: runner refusal leaves no persisted queued task even when it throws correctly. | PC-9 |
| G-10 | D-2: explicit remote override bypasses the provider probe. | PC-10 |
| G-11 | D-2: explicit remote override actually admits a queued task. | PC-11 |

### Positive controls

These are **designs, not executed evidence**. Code runs V/R; Review judges this
PC design and ordinary evidence before land. A separately commissioned
SourceLanding Mutation runs each PC independently after confirmed land, using
the owner's external evidence/restoration contract. All mutations below are
compiling C# edits to production code; never change the assertion to make red.
Do not combine controls sharing a file/method.

Exact method selectors (each selects **one** non-parameterized execution):

| Selector | Exact `--treenode-filter` |
|---|---|
| L | `/*/*/ProviderSignInRequiredCreateTests/Create_returns_409_provider_sign_in_required_when_the_store_is_Absent` |
| O | `/*/*/ProviderSignInRequiredCreateTests/AllowUnauthenticatedProvider_queues_the_task` |
| B | `/*/*/PhoneHomeTaskCreateTests/Runner_bound_grok_create_refuses_a_logged_out_runner` |
| P | `/*/*/PhoneHomeTaskCreateTests/AllowUnauthenticatedProvider_does_not_probe_the_runner` |

| PC | Break the mapped guard with this single defect | Exact method selector and expected red assertion |
|---|---|---|
| PC-1 | In **only the host-less** `MessageFor` return, replace the concatenated/interpolated `grokHome` value with literal `/wrong/c1018-home`, retaining `GROK_HOME=` and all other wording. | L: `ex.Message.ShouldContain("GROK_HOME=" + grokHome.Path)`. |
| PC-2 | In only that return, replace `as the user that runs the session-runner` with `as the operator`; retain the home and neutral wording. | L: `ShouldContain("as the user that runs the session-runner")`. |
| PC-3 | Append literal ` Windows` to only that return, retaining the correct actor phrase and supplied home. | L: `ex.Message.ShouldNotContain("Windows")`. |
| PC-4 | Remove only `using that GROK_HOME, ` from that return. | L: `ShouldContain("using that GROK_HOME")`. |
| PC-5 | In `RefuseUnauthenticatedGrokAsync`, replace final `if (GrokCredentialStore.IsLaunchBlocking(finding))` with `if (finding == GrokCredentialStore.Finding.ApiKeyAuth)`. The selected fixture supplies Absent. | L: the existing `Should.ThrowAsync<ProviderSignInRequiredException>` fails because create returns instead. |
| PC-6 | In `CreateAsync`, inside `if (repeatOf is null && !routingExhausted)` immediately before the Grok-refusal call, add `if (agentKind == AgentKind.Grok && string.IsNullOrWhiteSpace(remoteRunnerId) && !request.AllowUnauthenticatedProvider) { _db.AgentTasks.Add(task); await _db.SaveChangesAsync(ct); }`. Leave refusal intact; the fully constructed valid task is saved before it throws. | L: final matching-goal `CountAsync(...).ShouldBe(0, "a refused create must not leave a queued row")` sees 1. |
| PC-7 | In `CreateAsync`, immediately before the existing `_db.AgentTasks.Add(task)` after `AdmitWorkspaceAsync`, add `if (agentKind == AgentKind.Grok && string.IsNullOrWhiteSpace(remoteRunnerId) && request.AllowUnauthenticatedProvider) task.Status = AgentTaskStatus.Blocked;`. | O: `created.Status.ShouldBe(AgentTaskStatus.Queued)` sees Blocked. |
| PC-8 | In the runner branch of `RefuseUnauthenticatedGrokAsync`, change only `if (answer?.LoggedIn == false)` to `if (answer?.LoggedIn == true)`. | B: `Should.ThrowAsync<ProviderSignInRequiredException>` fails because the negative probe now admits. |
| PC-9 | Same insertion location as PC-6, independently restored first, but use `!string.IsNullOrWhiteSpace(remoteRunnerId)` instead of `string.IsNullOrWhiteSpace(remoteRunnerId)` in the inserted condition. | B: `(await db.AgentTasks.CountAsync(t => t.Goal == "do remote grok")).ShouldBe(0)` sees 1 after the unchanged refusal. |
| PC-10 | At entry to `RefuseUnauthenticatedGrokAsync`, before its current early-return condition, add `if (allowUnauthenticated && agentKind == AgentKind.Grok && !string.IsNullOrWhiteSpace(runnerId) && _runners is not null) await _runners.Resolve(runnerId).GetProviderAuthAsync("grok", ct);`. Keep the original early return. | P: queued status still passes, then `probe.Client.Providers.ShouldBeEmpty()` fails with `grok`. |
| PC-11 | Same insertion location as PC-7, independently restored first, but use `!string.IsNullOrWhiteSpace(remoteRunnerId)` instead of `string.IsNullOrWhiteSpace(remoteRunnerId)` in the inserted condition. Leave the override's probe bypass intact. | P: `created.Status.ShouldBe(AgentTaskStatus.Queued)` sees Blocked. |

Each PC runs baseline/green, break/red, exact restore/green with its **own exact
selector** above, `-MinExecuted 1`, full `Class.Method` as `-Expect`, and distinct
`bin-c1018-pcN-baseline/`, `bin-c1018-pcN-red/`, `bin-c1018-pcN-green/` outputs.
Use the unchanged copied `scripts/run-checkpoint.ps1` and copied
`scripts/lib/build-slot.ps1` under the assigned external evidence root, as the
Mutation owner prescribes; the driver acquires the build slot. Clear
`GROK_AUTH_PATH` in that owned process before the phases. Use fresh Testcontainer
processes for each phase: PC-5/6/8/9 can leave deliberate queued rows in their
own disposable databases. No live dispatcher is hosted by these fixtures.

Red requires build exit 0, exactly the named executed test, driver exit 1 and
the stated assertion failure in fresh TRX. Zero tests, missing tooling,
fixture/DB failure or compile failure is not red. Restore the complete source,
rebuild and obtain exactly one passed, zero failed/skipped result before moving
to another PC. Preserve phase logs, hashes and restoration evidence externally;
never commit/push the SourceLanding snapshot. All eleven controls are executable
against the named production bodies and the existing fixtures plus V-1's
planned assertions; none requires a new test seam.

### Out of scope

- Real queue delivery, terminal/PTY readiness, session launch and crash recovery:
  no such path changes. The service fakes cannot prove recipient delivery.
- A new retry or HTTP test: unchanged shared refusal call and unchanged
  middleware mapping are inspected. No claim that CP-2 exercises those layers.
- New auth-state/standing-profile/API-key/Codex probe matrices, a full assembly,
  a second OS, provider login and production canaries: no predicate, launch
  policy or platform-specific code changes. CARD-1023 admission policy and
  CARD-1022 native-backend deprecation remain separate.
- Changes to CARD-1006 text, fixtures or pins, provider docs, routing tests,
  `InstructionBundleTests`, deploy scripts, checkpoint code or the 377 census.
- Activation and post-land PCs are separately commissioned, not part of these
  two ordinary checkpoint rows. Publication is not server activation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1018/` | portable-unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed; retain full expanded Unit roster, 0 failed; disclose every skip | 1 | 6 | |
| CP-2 | S1 | CP-1 | portable-grok-refusal-integration | `/*/Antiphon.Tests.Application/(ProviderSignInRequiredCreateTests*)\|(PhoneHomeTaskCreateTests*)/*` | V-1, R-1, R-2 | exactly the 5 named methods, each once, 0 failed/skipped | 5 | 3 | `GROK_AUTH_PATH=` |

CP-1's 1 is deliberately a **nonzero lane floor**, not an invented Unit census.
The exact current expanded Unit count comes from Code's fresh TRX. The read-only
source census establishes CP-2's 2 + 3 = **5**, with no arguments/generators or
new methods. Assert all five exact names above in the report: the importer
extracts **class** tokens from the OR filter, not the `Expect` prose's method
roster, and `Min=5` alone cannot establish which five ran. Report actual
executed/passed/failed/skipped counts separately; do not relabel skips as passes.
The checkpoint receipt's stricter zero-skip qualification still applies; an
inherited skip is disclosed and cannot support `reviewedSourceClean: true`.

Run one checkpoint-tool `run --plan` for committed S1, selecting
`--rows CP-1,CP-2 --expected-source-sha` with the actual S1 SHA. Both rows share
one isolated build at the same source; the second reuses it. Await every run
through `wait` until exit is not 75. Build-slot refusal/timeout is a not-run
outcome, never permission to bypass the gate. Follow the existing source-receipt
validation, full-range evidence-diff check and owned-output cleanup requirements
above. The union of these rows is the complete ordinary Code/Review scope.

### Cost

All runtime figures below are **estimates**, not measured TestDesign results.

- Ordinary V/R floor (Code): **9 minutes** = CP-1 Unit filter **6** (estimated
  isolated build 2 + Unit execution 4) + CP-2 combined two-class filter **3**
  (reused build, Testcontainer startup and five executions). Separate Code setup
  allowance **2** for a missing tool bootstrap; authoring **5**. Code envelope
  **16 minutes** before slot wait or genuine failure-driven reruns.
- Ordinary Review floor: the same **9 minutes**, plus **2** setup if needed.
- PC floor (Mutation): **110 minutes** = each of PC-1 through PC-11 costs
  **10**: baseline isolated build/test **3**, apply defect **0.5**, red isolated
  build/test **3**, exact restoration **0.5**, restored isolated build/test **3**.
  PC-1–6 use L, PC-7 uses O, PC-8–9 use B, PC-10–11 use P. All eleven selections are
  method-scoped; no Unit/class-wide PC phase is authorized. Add **2** for external
  driver/evidence setup: Mutation envelope **112 minutes**. Snapshot scheduling
  may split controls across sequential resumed tasks if its foreground budget
  is shorter; no control ends before its restoration verdict.
- Required Code plus post-land verification total, excluding authoring and
  separate Review: **123 minutes** = setup **2** + ordinary build/execution
  **9** + Mutation setup **2** + eleven independent PC cycles **110**. Including
  Code authoring and ordinary Review/setup: **139 minutes**. Broker wait is
  additional measured wall time, not an execution count.
- Ordinary build reuse saves one estimated **2-minute** rebuild: **9** versus
  **11** minutes with separate identical builds. PC batching savings are
  **0**: the controls share production files and several share a method, so
  independent red/restore/green phases are required. Splitting the old proposed
  single broad mutation into eleven guards deliberately raises its estimated cost;
  it prevents one wording failure from concealing an untested admission guard.

Import validation (read-only, 23:32–23:33 UTC): executed the real checkpoint
tool's `import --plan` against this file with explicit `--repo-root` and ignored
output `.antiphon/c1018-import.yaml`; exit **0**, **2 rows**, **1 build**. No build
or test driver was invoked. The pre-existing binary was
`/work/worktrees/task-bd02f8d9/tools/Antiphon.Checkpoints/bin-c959-inert-tool/Antiphon.Checkpoints.dll`
(SHA-256 `9035ee47b2da91cbfbd4439a9e018fb0690dc5664167360b37b557e088fc32f7`);
its worktree source importer/validator files matched this checkout byte-for-byte.
Also invoked that assembly's real `ImportFile` with `isWindows=false` and
`isWindows=true`, then `ManifestValidator.Validate`: both produced **0 warnings**,
Min **1,5**, estimates **6,3**, the same `bin-c1018` build, exact unescaped OR
filter, class roster tokens, and CP-2's empty `GROK_AUTH_PATH` child environment.
Derived row deadlines are **18** and **15** minutes. These checks validate the
manifest contract only, not test discovery, compilation, execution or clean
source/build provenance. Generated YAML stays ignored; only this plan is committed.

Handoff audit: bodies read; **guards=11, mapped=11, missing=0,
duplicate PC maps=0**. All PCs name a compiling defect, exact selector and
assertion. No product decision or unverifiable seam remains; runtime evidence
is pending Code and separately commissioned Mutation.

--- next stage ---
next: code
handoff: Implement D-1 in the host-less Grok exception branch and add the frozen V-1 assertions in the existing create-refusal method only. Commit S1, run CP-1 Unit and CP-2 five integrations with GROK_AUTH_PATH cleared, validate receipts and report actual counts. Keep CARD-1006/Codex/routing untouched. Eleven independent method-scoped PCs remain post-land Mutation work.
artifact: docs/superpowers/plans/2026-10-03-card-1018-hostless-grok-sign-in-plan.md
