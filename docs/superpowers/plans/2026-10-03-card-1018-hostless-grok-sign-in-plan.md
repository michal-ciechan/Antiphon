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

## Verification design

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

### Checkpoints

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
