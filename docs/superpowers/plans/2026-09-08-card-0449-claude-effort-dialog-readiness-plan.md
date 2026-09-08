# CARD-0449: preserve requested effort through Claude startup readiness

Date: 2026-09-08. Stage: Plan. Next stage: TestDesign.

The fix will recognize Claude Code 2.1.263's unnumbered effort-default dialog,
select the effort requested by the resolved launch, verify that the dialog clears,
and then complete the existing composer round trip. This document specifies the
fix; it does not claim that the runtime has been changed or verified.

## Evidence and ground truth

Root-cause authority: **Investigate task `6d120c92`, final report**, as carried
forward by the full brief for Plan task `62044973`. No CARD-0449 investigation
document was present under `docs/investigations/` when this plan was written.
The confirmed failure chain is an unrecognized effort dialog, a composer probe
timing out at approximately 90 seconds, readiness failure, then the existing
`KillAndDisposeAsync` cleanup. Alias and effort composition are confirmed correct.
This plan accepts that finding; it does not reopen the investigation.

Code references below were checked at `d9d1f90e`. The three retained rendered
screens were read through the runner's snapshot endpoint for fixture provenance
only; no session was launched, answered, restarted or killed.

| Brief assumption / required behavior | Current code or supplied evidence | Consequence for this fix |
|---|---|---|
| The effort dialog is invisible to the detector | `ClaudeBlockingPromptDetector.Detect` in `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs` returns early unless compact text contains `1yes`, `2no`, `entertoconfirm` or `esctocancel`; none occurs in these captures. | Recognize the complete effort-choice structure before this legacy gate. Do not loosen the generic choice predicate. |
| The selection must preserve the requested effort | Captures say `Fable 5.1 with xhigh effort` and highlight `Keep xhigh`. `ClaudeLaunchArgs.Effort(Frontier)` already returns `xhigh`. | Read the resolved launch's existing `--effort` value; do not compute another tier/model table. |
| Adding detection alone fixes launch | `ClearStartupTrustPromptAsync` answers only trust; other detected kinds become `NotAnswerable`. Both adapters skip the probe and return true for that outcome. | Effort needs its own explicitly authorized answer path and failure outcome; classifying it as generic `Choice` would bypass the essential probe. |
| One pre-probe snapshot covers startup | `RunnerClaudeAdapter.WaitForReadyAsync` checks the trust gate after quiet, then waits out `ClaudeReadyMinTotalWaitMs`, then probes without checking again. | Recheck after the floor and observe blockers during the probe, including delayed appearance. |
| The probe itself protects against dialogs | `ComposerInputProbe.RunAsync` writes its token before reading a screen; retries and Ctrl+U also have no modal gate. | Add an optional pure blocker predicate, checked against fresh snapshots before writes and while polling. |
| Only the production adapter needs consideration | `server/Infrastructure/Agents/Pty/ClaudeAdapter.cs` implements the same readiness contract, with its minimum floor inside `ClaudeReadyDetector`. | Share the effort resolver/readiness coordination and wire both adapters. Do not move the in-process floor a second time. |
| Dismissal can be inferred from any output | The existing detector correctly uses rendered screens because accumulated output retains dismissed dialogs forever. | Confirm the specific dialog's disappearance from fresh rendered screens; then require the normal probe to render and clear its token. |
| Failure can already carry a useful reason | `AgentLaunchBlock`, `WaitForReadyOrThrowAsync`, and `GrokSignInIncident.ToSessionBlock` support named blocks, but the enums have only sign-in and trust kinds. | Add one effort-specific block and its mapping, using the existing failure/cleanup path. |

### Captured fixture provenance

Use **three separately identified fixtures**, even though their dialog content and
wrapping are identical. Do not invent three different layouts from these captures.

| Fixture | Runner session | Task banner |
|---|---|---|
| Capture 1 | `9184ec6d-f3ff-407d-add2-baad58758f77` | `task-beaf82ff` |
| Capture 2 | `2968433b-f664-4410-b03b-73d176c06f70` | `task-de877677` |
| Capture 3 | `787bfee2-ec0a-4f7c-9716-f44012730a89` | `task-2722ce41` |

Source for each: `GET http://localhost:17204/sessions/<session-id>/snapshot`, field
`renderedScreen`. Each screen is 120 columns with trailing empty rows; the header
contains `Claude Code v2.1.263`, `Fable 5.1 with xhigh effort · Claude Max`, and
`C:\src\markdown-package`. The modal is separated by horizontal rules. Its exact
nonempty text and line wrapping are:

```text
 Use Fable 5.1 at high effort by default?

   high is the default effort for Fable 5.1 and is recommended for most coding tasks; xhigh spends more tokens per
   task. You can change this any time with /effort.

   xhigh effort is ~1.7x the estimated cost of high (the default).

   > Keep xhigh
     Switch Fable 5.1 to high effort
```

TestDesign should retain the full rendered strings as offline fixture data when
available; the table and excerpt above preserve the relevant evidence if retained
runner sessions disappear. Tests must not depend on the production runner. Any
new wrapping, marker, model or effort variants are explicitly synthetic tests.

## Decisions

**D-1 — Add a distinct effort-default prompt kind and a structured parser.**
Introduce `EffortChoice` after existing `ClaudeBlockingPromptKind` values. A small
`ClaudeEffortPrompt` helper in the PTY library parses the question
`Use <model> at <suggested-effort> effort by default?`, the two option rows
`Keep <current-effort>` and `Switch <same-model> to <same-suggested-effort> effort`,
and which option carries `>` or `❯`. Require the co-located question and both
option labels in an active modal section; validate matching model/effort tokens
between question and switch row. Read line/option structure, normalizing casing,
whitespace, box borders and wrapped continuation rows without flattening unrelated
prose into a menu. A complete effort menu with no unique readable highlight is
recognized but unanswerable. The model name is opaque text; the CLI effort
vocabulary is `low`, `medium`, `high`, `xhigh`, `max`, not a Fable-only test.

The effort branch precedes the old `hasChoices` early return. Leave trust,
permission and generic choice recognition intact. Generic `TryAnswerDetailedAsync`
must not fall through to `AffirmativeKey` for this new kind; only the effort-aware
resolver may answer it. Rejected: matching `effort` or `Keep` alone, treating every
unnumbered list as authorized, and assigning a blind affirmative Enter.

**D-2 — Select from resolved launch intent, never from a new tier mapping.**
Both adapters retain an effort intent parsed from `AgentLaunchSpec.Args` at
`StartAsync`. Put this small reader beside the effort helper so adapters do not
fork it. Recognize the existing separate `--effort`, value pair and the explicit
`--effort=value` spelling; ignore unrelated argument contents and stop at `--`.
Normalize only the effort token. Repeated identical values are harmless;
conflicting, missing-value or unsupported explicit values are ambiguous and must
not silently fall back to a default when an effort dialog is present.

Choose the option whose parsed effort equals the explicit request, preferring
`Keep` when it matches. Thus the reported Fable/xhigh case accepts `Keep xhigh`;
another model or requested effort uses the same rule. If the request matches only
the switch option, select that option deliberately. If neither matches, withhold
input and fail with the named block. With no explicit effort argument (including
an attached session with no launch spec), choose `Keep <current-effort>`: preserve
the displayed current setting without guessing a tier. Log that intent came from
the Keep option, rather than claiming an explicit launch value was checked.

Rejected: always accepting the default highlighted row; always switching to the
provider recommendation; hardcoding `fable`/`xhigh`; reading or editing the user's
Claude configuration; adding effort to launch DTOs or recomposing arguments.
The dialog action itself may persist Claude's default preference; it must only
choose the value authorized by this rule, with no additional settings mutation.

**D-3 — Verify selection, then verify dismissal with bounded input.**
The answerer takes snapshot/write delegates, parsed launch intent and a settle
budget. It re-reads and revalidates the same dialog identity before each key.
If the desired option is highlighted, send a separate `\r`. Otherwise use the
existing Select navigation candidates (`j`, Down, Ctrl+N), one at a time with a
fresh observation between them. Send Enter only after the desired option is
visibly highlighted. These candidates come from the existing trust answerer;
their effectiveness on the effort picker is a verification target, not a new
claim of live measurement. Never send `1`, Escape, `/effort`, or a work body.

Use a new `ClaudeEffortPromptSettleMs` setting, default 15,000 ms, for the whole
selection/dismissal attempt. Allow at most three Enter attempts, at least 1,500 ms
apart, and only while a fresh, complete screen still shows the same dialog and
the intended row selected. Allow the initial recognized dialog to settle for
1,500 ms as well; this is a conservative choice based on the existing Select
family's input-refusal window, not measured timing for this particular picker.
Unknown highlights, changed identities, cancellation or exhausted deadlines
withhold further input. Every delay is cancellable and charged to the same budget.

After Enter, a blank/redrawing frame is not clearance. Require the effort dialog
to be absent on two consecutive polls and positive evidence of the next UI:
recognized composer chrome (`ClaudeScreen.ComposerIsLive`) or a different known
startup modal. The latter means **advance the startup gate**, not **ready**. A
still-visible or malformed remnant of the effort menu remains blocked. Observe
and compare the effort in a current-session banner/status when it is available;
a contradictory visible effort fails. Do not require a banner that may scroll
away, or count absence of the old dialog as proof of a new model response.

Rejected: fire-and-forget Enter, unlimited retry loops, trusting sequence advance,
accepting a transient blank frame, or weakening the composer probe. Keep existing
trust key behavior; only adjust its clearance predicate as necessary to recognize
trust-to-effort transitions as the trust dialog clearing, then resolve the next
dialog rather than incorrectly labeling it `TrustDialogNotCleared`.

**D-4 — Keep the startup gate active through readiness.**
Use a small shared `ClaudeStartupReadiness` coordinator in the PTY library with
snapshot/write delegates, launch effort intent and timing options. Both adapters
call it; the library does not depend on server/domain types. It owns the sequence
of known startup dialogs and composer probing, with structured outcomes for the
adapter to log/map. Preserve the existing early trust check where appropriate,
then recheck after the minimum startup floor and immediately before probing.

Extend `ComposerInputProbe.RunAsync` compatibly with an optional pure
`isBlocked(screen)` predicate and an `InterruptedByModal` result. Without a
predicate its callers retain today's behavior. With one, use fresh snapshots
before the first token, every token retry and every Ctrl+U, during both polling
phases, and before returning responsive. If blocked, stop before the next write
and return to the coordinator. Do not put Claude selection logic in the generic
probe. This also catches a dialog appearing after the first token was sent;
historical token text in a dialog is not composer evidence.

The coordinator resolves an effort/trust prompt and starts a fresh composer
round trip after clearance. Invalidate pre-dialog probe evidence; account for
all previous token writes and elapsed time, preserving the configured total
token-write ceiling. A late modal never restarts a fresh 90-second clock. Bound
the post-floor gate/probe phase by one existing probe budget (90 seconds by
default); each dialog also has its smaller settle budget. Keep the existing
bounded composer-clear budget. When the probe is explicitly disabled, still run
the startup gate, bounded by `ClaudeReadyMaxWaitMs`, but send no probe token.
No fixed extra wait is added to launches without a dialog.

Other pre-existing `NotAnswerable` modals keep their current logged, no-input,
probe-skipped pass-through behavior. Effort refusal/timeout must never take that
lenient arm. Handle trust then effort, effort then another modal, and recurrence
inside the same bounded gate; do not assume there is exactly one startup screen.
The check/write boundary cannot be atomic with a remote TUI; this design stops
input at the first snapshot showing a blocker and verifies recovery before
further probing. It does not claim to prevent a frame appearing after a write
has already crossed the transport.

Rejected: adding a fixed startup sleep; checking only once after quiet; only
rechecking after a full failed 90-second probe; changing quiet/probe timeouts;
duplicating the resolver in the two adapters; modifying transcript delivery.

**D-5 — Give an effort failure its own existing-path launch block.**
Add `EffortDialogNotCleared = 3` to `AgentLaunchBlockKind` and
`SessionLaunchBlock`, and map it in `GrokSignInIncident.ToSessionBlock` (despite
that helper's existing provider-specific name). Both adapters return false with
this block for a recognized effort dialog that cannot be safely selected or
cleared. The reason records requested/current/suggested effort, selected row,
whether Enter was sent, and the bounded failure cause. Include a concise remedy:
inspect the effort picker and relaunch with a supported explicit effort. Do not
dump full argv, environment or screen contents into this new diagnostic.

Reuse existing exception persistence, incidents and startup process cleanup. Do
not redesign kill/retry/supervision behavior or add a new incident subsystem.
Verify the new enum value persists through the existing storage conversion;
there is no planned schema shape change. Rejected: mislabeling effort as trust,
reporting ready while it is still up, and leaving only a generic 90-second probe
failure. This is startup failure, not a new policy to kill provider stalls.

**D-6 — Correct documentation without changing launch composition.**
Make a comment-only edit to `ClaudeLaunchArgs.cs`: `--effort` selects launch
effort, but Claude can still ask whether to change its default. Remove the stale
claim that the flag means "no picker to answer". Leave `EffortFlag`, the effort
table, every emitted argument, and `ModelLevelAliases.cs` unchanged. Update the
runtime and agent-kind owners to describe the new narrow effort exception to
the trust-only startup rule once implementation passes verification.

These are implementation defaults, with no outstanding user decision. Verification
is a separate stage under `server/Bundles/stage-plan.md`; this plan therefore
hands off to **test-design**, not code.

## Implementation slices and file list

All paths are repository-relative. New filenames below are the planned seams,
not files claimed to exist at Plan completion.

| Slice | Files | Change and exit evidence |
|---|---|---|
| S1: parse and answer the exact picker | `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs`; new `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs`; new `tests/Antiphon.Agents.Pty.Tests/ClaudeEffortPromptTests.cs`; `tests/Antiphon.Agents.Pty.Tests/ClaudeStartupTrustPromptTests.cs` | Add distinct recognition, argument intent reader, verified selection/dismissal and narrow trust-to-effort transition support. Replay all three captures and synthetic selection/negative cases. |
| S2: readiness ordering and late dialogs | New `src/Antiphon.Agents.Pty/ClaudeStartupReadiness.cs`; `src/Antiphon.Agents.Pty/ComposerInputProbe.cs`; both Claude adapter paths named in ground truth; `server/Application/Settings/AgentRegistrySettings.cs`; new `tests/Antiphon.Tests/Agents/RunnerClaudeAdapterEffortPromptTests.cs`; new `tests/Antiphon.Agents.Pty.Tests/ClaudeStartupReadinessTests.cs`; `tests/Antiphon.Agents.Pty.Tests/ComposerInputProbeTests.cs` | Share coordination; retain resolved effort; preserve timing floors, probe ceilings and disabled-probe semantics; test late appearance before and during probing and both modal chains. |
| S3: named failure persistence | `server/Application/Dtos/AgentLaunchBlock.cs`; `server/Domain/Enums/SessionLaunchBlock.cs`; `server/Application/Services/GrokSignInIncident.cs`; both adapters; `tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs` | Persist effort-specific reason through the existing launch-failure route; verify the probe and boot prompt are withheld on failure. |
| S4: documentation and acceptance evidence | `server/Application/Services/ClaudeLaunchArgs.cs` (comment only); `docs/session-runtime-invariants.md` (Gotcha #48); `docs/agent-kinds.md` (Claude startup behavior); this plan's eventual verification section | Correct the trust-only/no-picker descriptions, record focused results and any real-picker acceptance limitation. |

Use offline inline capture data in the new detector tests unless TestDesign
chooses dedicated fixture files. The existing scripted `ISessionRunnerClient`
pattern in `RunnerClaudeAdapterTrustPromptTests` is sufficient for production
adapter ordering; do not expand FakeClaude into a new model/UI simulator just
for these states. Shared coordinator tests pin the in-process contract; add a
small local-adapter integration fixture only if TestDesign identifies behavior
that the shared helper and existing adapter tests cannot observe.

## Regression requirements for TestDesign

Append the executable `## Verification design` section with V-n/R-n/PC-n items,
following `server/Bundles/stage-test-design.md`. At minimum cover:

1. **All three captures:** detector identifies `EffortChoice`, extracts Fable 5.1,
   current `xhigh`, suggested `high`, and `Keep` highlight. Replays remain offline.
   Add separately labeled wrapped-label, border and `❯` variants.
2. **Effort preservation:** explicit xhigh + highlighted Keep; wrong initial
   highlight must move before Enter; another model and non-xhigh effort;
   explicit request matching Switch; absent argument preserves Keep; conflicting,
   unsupported, malformed or unmatched explicit request withholds input.
3. **Positive selection and clearance evidence:** Enter only on the intended
   row; no Enter for unknown highlight or failed navigation; delayed redraw;
   swallowed first Enter followed by bounded successful retry; every dismissal
   attempt swallowed; a blank frame followed by the same modal; an observable
   conflicting resulting effort. Failed cases return the named effort block.
4. **Delayed startup:** initial quiet screen has no dialog; dialog appears during
   the minimum floor, between gate and probe, after the first token, before a
   retry, and during token clearing. Assert input order and fresh post-dismissal
   probe evidence, not just a final boolean. No additional token or Ctrl+U goes
   into a detected modal; no old token evidence can certify readiness.
5. **Dialog chains and bounds:** trust then effort, effort then trust, effort then
   generic permission/choice, repeated effort dialog, process exit/cancellation,
   deadline exhaustion and probe-write ceiling. A stuck dialog cannot repeatedly
   renew either the settlement or probe deadline. Probe disabled still resolves
   effort and never types a token.
6. **Unrelated dialogs stay unrelated:** existing numbered trust, highlighted
   trust, tool permission, generic choice, `/model` or ordinary effort picker;
   ordinary prose mentioning the captured phrases; inconsistent title/option
   model names, incomplete choices and misleading highlight markers. Assert both
   classification and absence of automatic effort input. Preserve existing
   `NotAnswerable` behavior and trust failure reasons.
7. **Healthy path and diagnostic persistence:** no-dialog launch performs its
   unchanged render-and-clear round trip without Enter or new fixed delay;
   `AgentSessionLaunchFailureTests` pins the new block and exact reason through
   the existing service path. Existing trust/probe/launch-argument tests stay green.

Positive controls must break real guards: bypass the effort recognition branch;
skip intended-highlight verification; accept dismissal without screen clearance;
remove the post-floor/probe blocker check; route effort failures through
`NotAnswerable`. Select a small independent set of controls covering these
assertions, with precise expected failing test names. Scope both red and restored
green runs to each PC's exact test method, as required by CARD-0451 in
`docs/testing-and-build.md`. Batch only independent mutations in different files
and methods, retaining a result for each PC. Do not multiply mutations that
exercise the same guard. Use a disposable checkout for mutations, refresh
restored source timestamps before rebuilding, and require assertion failures
rather than build errors or zero-test runs.

Real-picker acceptance is distinct from scripted proof. TestDesign should name a
bounded, isolated headed canary against the captured Claude version/modern ConPTY
that observes the effort dialog, selected effort, disappearance and successful
composer round trip without submitting a model turn. Follow existing credential
isolation and headed-test conventions; do not alter the user's live Claude
preferences to manufacture the prompt or send input to the three exited sessions.
If a safe isolated launch cannot reproduce this provider-controlled prompt,
record real-picker acceptance as unverified and retain the captured-fixture and
scripted proof; a launch that never showed the dialog does not verify dismissal.

## Verification commands and scope boundaries

TestDesign will finalize class filters and positive-control commands. The focused
execution set consists of the two new PTY test classes named above, existing
`ClaudeStartupTrustPromptTests` and `ComposerInputProbeTests`, new
`RunnerClaudeAdapterEffortPromptTests`, existing `RunnerClaudeAdapterTrustPromptTests`,
`AgentSessionLaunchFailureTests`, and unchanged `ClaudeLaunchArgsTests`.

For regression runs use one named class per invocation or the documented
combined-class syntax; individual PC cycles use exact method filters. Require
nonzero executed test counts. Examples once the new classes exist:

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter "/*/*/ClaudeEffortPromptTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter "/*/*/RunnerClaudeAdapterEffortPromptTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter "/*/*/AgentSessionLaunchFailureTests/*"
```

Run the two assemblies sequentially, not concurrently. No full solution, client
or browser suite is required for this scoped change. Tests booting real Program
must retain the production-runner guard; process-spawning/headed tests require
the assembly-local limiter and existing headed group/opt-in. Read
`docs/testing-and-build.md` before execution. Do not increase global deadlines to
make tests pass. Stage verification reports must distinguish scripted success
from any live acceptance evidence.

Out of scope: model aliases, effort argument composition, provider installation
or upgrade, settings-file seeding, remote-control behavior, transcript matching,
prompt transport, kill/supervision policy, fake gateway traffic, deployment and
production session operations. No implementation tests or builds were run by
this Plan stage; its deliverable is this committed plan.

## Verification design

Appended 2026-09-09 by TestDesign task `d335620d`, against plan commit
`04c1b1c0`. D-1 through D-6 and S1 through S4 above remain the fix design.
This section is executable work for Code, not a claim that the proposed tests
already exist or that the implementation passes them. Next stage: **code**.

### Evidence and harness contract

TestDesign independently fetched all three actual retained snapshots at
2026-09-08 23:10:33 UTC (2026-09-09 00:10:33 Europe/London). Their full,
unmodified `renderedScreen` strings are retained in
[the capture fixture](2026-09-08-card-0449-claude-effort-dialog-captures.json).
Only selected snapshot fields are retained; no raw output, credentials or live
input is involved. SHA-256 below covers UTF-8 encoding of the decoded screen
string, without BOM or newline normalization, rather than the JSON file bytes.

| Session / fixture | Last sequence | UTF-8 bytes | SHA-256 |
|---|---:|---:|---|
| `9184ec6d-f3ff-407d-add2-baad58758f77` / C1 | 23 | 1559 | `b82f0a8895c3ef0c38dbad6b1d1ee4cda66cc7db339f9e6723516426d07d70f6` |
| `2968433b-f664-4410-b03b-73d176c06f70` / C2 | 23 | 1559 | `8a57fb6c4a9e0a95014230de456eb06f66f9a6ab652ab9231e239c8c02b246fe` |
| `787bfee2-ec0a-4f7c-9716-f44012730a89` / C3 | 19 | 1559 | `8613cadad4b1491161b14181f4224c113e96f58aab10eefd0753f8c61663d378` |

Each decoded string has 828 characters, 30 LF-separated rows and a maximum row
width of 120. Direct inspection confirms ASCII `>` on `Keep xhigh`, Fable 5.1,
suggested `high`, and **none** of the four legacy compact markers. Replacing
only each task banner with the same placeholder makes the three screens exactly
equal. They are three captured occurrences, not three distinct provider layouts.
Retain all three identities and hashes in test discovery/results. Additional
layout/model/effort cases below are synthetic and must be labeled accordingly.

Code should embed the JSON as `Card0449.EffortCaptures.json` in both relevant
test projects, using a linked `EmbeddedResource` from the file above; add the
resource entries to `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj`
and `tests/Antiphon.Tests/Antiphon.Tests.csproj`. Load by resource name, not the
working directory or HTTP. Assert each decoded string's recorded hash before
using it. The test oracle for parsed model/efforts is literal expected data,
never output of the production parser used to generate its own expectation.

The following abbreviations identify exact test classes and their project:

| Alias | Class | Project / file |
|---|---|---|
| E | `ClaudeEffortPromptTests` (new) | `tests/Antiphon.Agents.Pty.Tests/ClaudeEffortPromptTests.cs` |
| S | `ClaudeStartupReadinessTests` (new) | `tests/Antiphon.Agents.Pty.Tests/ClaudeStartupReadinessTests.cs` |
| P | `ComposerInputProbeTests` (extend) | `tests/Antiphon.Agents.Pty.Tests/ComposerInputProbeTests.cs` |
| A | `RunnerClaudeAdapterEffortPromptTests` (new) | `tests/Antiphon.Tests/Agents/RunnerClaudeAdapterEffortPromptTests.cs` |
| F | `AgentSessionLaunchFailureTests` (extend) | `tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs` |
| L | `ClaudeAdapterEffortPromptTests` (new, S2's permitted local-adapter fixture) | `tests/Antiphon.Tests/Agents/ClaudeAdapterEffortPromptTests.cs` |
| C | `ClaudeEffortPromptCanaryTests` (new, conditional acceptance) | `tests/Antiphon.Tests/Agents/ClaudeEffortPromptCanaryTests.cs` |

Use the existing scripted-client approach in `RunnerClaudeAdapterTrustPromptTests`
and the delegate approach in `ComposerInputProbeTests`. The effort fixture needs
the following small independent state machine; do not call production detection
or selection code from its `WriteAsync` implementation:

- `Dialog(current, suggested, highlight)`: navigation changes the fake highlight
  only for its configured accepted key. An accepted Enter sets `AppliedEffort`
  from the fake's highlighted option and records `AcceptedOption`. Swallowed
  Enter leaves both unchanged. Neither token text nor Ctrl+U dismisses the menu.
- `Redraw` and `Composer(appliedEffort, text)`: transition according to the test's
  explicit script. Composer echoes the token and honors Ctrl+U independently of
  the production probe. Render `? for shortcuts` or `bypass permissions on`, plus
  a current-session effort line when the case requires it. A box alone is not
  positive composer chrome under D-3.
- Record an ordered trace of snapshot number, screen generation, writes,
  highlighted option, accepted option, applied effort, and monotonic elapsed
  time. Attach it to assertion failures. Record accumulated raw text separately:
  it intentionally retains old dialogs after the rendered screen clears.
- Phase transitions use explicit callbacks/barriers such as first token written
  or token observed. Except for the floor test, do not depend on arbitrary read
  counts or racing sleeps to insert a late dialog. Where a write follows a
  snapshot, the write log records the fake's actual active UI, not the SUT's view.
- A no-input assertion counts **all** input, including navigation, Enter, tokens
  and Ctrl+U. A no-probe-input assertion allows only authorized modal keys.
  `AppliedEffort`, readiness, clearance observations and empty composer must all
  be asserted independently; a helper's `Cleared`/`Responsive` boolean is not the
  entire oracle.

Use real elapsed time and cancellable tasks for the adapter checks; do not inject
a frozen server-wide clock. Ordinary successful effort cases use the production
1,500 ms initial settle and retry spacing, an 8,000 ms effort budget and a 12,000
ms probe budget. Runner quiet is 100 ms, floor is zero except where specified,
probe poll is 25 ms, retype 1,000 ms, clear budget 500 ms and max token writes 3.
The fake freezes one `StartedAt` value at Start/Attach, rather than returning a
different start time on each snapshot. Pure parser/selection table tests do not
wait for the answerer and should stay cheap.

For failure tests use the specific budgets named below and an independent
watchdog. Race the operation against the watchdog, **assert** that the operation
completed before the watchdog, and in `finally` cancel and await the owned task.
Watchdog cancellation is cleanup, not successful bounded-failure evidence.
Never accept a runner/test-framework timeout, a canceled test, or a fixture
exception as proof that the production deadline fired. A deadline mutation must
produce the named completion assertion failure and leave no task running.

### Proves it works now

Every method below is an executable test to add unless explicitly marked existing.
Parameterized methods must report each named row; grouped rows are not permission
to omit cases. Aliases expand using the class table above.

| ID / decisions / slices | Layer and exact test method(s) | Setup and required observation |
|---|---|---|
| **V-1** / D-1 / S1 | unit: E.`Captured_screens_are_effort_choices` | C1/C2/C3 as three rows. Check hash, `EffortChoice`, opaque model `Fable 5.1`, current `xhigh`, suggested `high`, highlight Keep, and `IsBlocked=true`. Do not add legacy footer text to make these pass. |
| **V-2** / D-1 / S1 | unit: E.`Synthetic_layout_variants_preserve_option_structure` | Separately labeled variants of C1: `❯`; CRLF; box borders; mixed casing/whitespace; question wrapped after model name; Switch label wrapped before `high effort`. Same parsed fields and selected row. |
| **V-3** / D-2 / S1 | unit: E.`Launch_effort_reader_preserves_explicit_intent`; E.`Requested_effort_selects_the_matching_option` | Reader rows: separate pair, equals spelling, uppercase value, identical duplicate, absent flag, text value containing `--effort high` inside one `--append-system-prompt` argument, and flag after `--`. Selection rows: every supported effort (`low`, `medium`, `high`, `xhigh`, `max`) as Keep with a different suggested effort; request matching Switch; absent request; equal Keep/Switch values prefer Keep. Use a synthetic non-Fable model for the first five. |
| **V-4** / D-2 / S1-S2 | unit: E.`Ambiguous_or_unmatched_intent_types_nothing`; A.`Attach_without_launch_effort_preserves_current` | Invalid rows: `--effort` with no value, empty value, `bogus`, conflicting duplicates, and explicit valid `max` when options are xhigh/high. Each recognized menu fails, with no keys and no applied change; none takes the absent-request fallback. Attach with no launch spec preserves Keep, and labels that provenance without claiming explicit intent. |
| **V-5** / D-2-D-3 / S1 | unit: E.`Requested_effort_is_applied`; E.`An_unmovable_wrong_highlight_withholds_Enter` | First method rows: Fable xhigh initially highlighting Switch; synthetic `Nimble 9` medium initially highlighting Switch (suggested high); Fable high initially highlighting Keep (current xhigh). Independently assert final `AppliedEffort` equals the literal request, `AcceptedOption` is correct, navigation precedes Enter and the final visible effort agrees. Second method ignores every navigation key: no Enter, no applied effort, failure. Also exercise no marker and two highlighted rows as unanswerable. |
| **V-6** / D-1-D-3 / S1 | unit: E.`A_changed_dialog_identity_stops_further_input`; E.`Generic_answering_cannot_confirm_an_effort_choice` | Change to a permission modal after navigation but before the refreshed Enter decision; no subsequent keys. Repeat with a different model's effort menu. Call the existing generic answer API with a detected effort prompt: it refuses/returns not-cleared without typing; only the effort-specific API is authorized. |
| **V-7** / D-3-D-4 / S1-S2 | unit: S.`Redraw_is_not_clearance_before_the_probe`; E.`Malformed_effort_remnants_do_not_count_as_clearance` | Accepted Enter is followed by blank, one composer frame, the old dialog, then two consecutive composer frames. No token until the final confirmed transition; reset the absence streak when the modal returns. In the remnant case, remove an option after previously detecting the full dialog and leave its title/remaining option: fail within the settle deadline, not success. Accumulated raw output retaining C1 cannot prevent real clearance. |
| **V-8** / D-3 / S1 | unit: E.`A_swallowed_Enter_is_retried_only_while_the_same_target_is_selected`; E.`Retries_respect_settle_and_attempt_limits` | Swallow first Enter and accept second: success, exactly two Enters. Swallow all with 7,000 ms effort budget: failure, at most three Enters, first no earlier than 1,500 ms from recognition and gaps at least 1,500 ms. Watchdog 9,000 ms. Change highlight/identity before a retry and prove it is revalidated; never send a delayed Enter after clearance. |
| **V-9** / D-2-D-3 / S1-S2 | unit: S.`A_contradictory_resulting_effort_fails_readiness`; S.`A_scrolled_away_effort_banner_does_not_block_confirmed_clearance` | Fake accepts requested xhigh but reports applied/current effort high on the next composer: failure, no probe/boot input. Companion with no post-clear banner succeeds after correct selection, two clear composer observations and a full round trip; banner absence is distinct from contradiction. |
| **V-10** / D-1-D-5 / S2 | unit, production adapter boundary: A.`Each_captured_dialog_clears_before_the_composer_probe` | C1/C2/C3 rows with args `--model fable --effort xhigh`. Start actual `RunnerClaudeAdapter` over scripted client; await readiness. Assert applied xhigh, Keep accepted, Enter only on Keep, at least two clear composer observations before the first token, exactly one successful token render/clear cycle, final empty composer and null LaunchBlock. |
| **V-11** / D-4 / S2 | unit: A.`A_dialog_appearing_during_the_minimum_floor_is_resolved`; P.`A_modal_interrupts_before_each_probe_write_or_verdict` | Floor case: immutable StartedAt, 100 ms quiet, 800 ms minimum floor, C1 scheduled at Start+400 ms; assert first clear-gate snapshot is before appearance and no probe before Start+800 ms/verified dismissal. The P method uses all six checkpoints listed below and requires `InterruptedByModal`, never Responsive, with no prohibited write. |
| **V-12** / D-4 / S2 | unit: S.`A_late_dialog_requires_a_fresh_post_clearance_round_trip` | First probe token renders; effort dialog then appears before clearing and contains that token as historical text. Clear the dialog to an empty composer. Readiness may succeed only after a new post-dialog token render and verified Ctrl+U clearance. Trace proves the old render/disappearance could not be reused; total token writes stay within 3. Also place the dialog just after the first write but before any token echo. |
| **V-13** / D-3-D-4 / S1-S2 | unit: S.`Startup_dialog_chains_preserve_each_gate` | Four rows: trust then effort; effort then trust; effort then tool permission; effort then generic choice. First two require both distinct selections/clearances before probe; last two preserve the existing logged NotAnswerable pass-through with no input to the unrelated modal and no probe. Failure at one gate names that gate. |
| **V-14** / D-3-D-5 / S2 | unit: A.`An_effort_dialog_that_never_clears_fails_within_its_budget` | C1 ignores all Enter attempts. Effort budget 7,000 ms, probe budget 12,000 ms, independent watchdog 9,000 ms after the gate starts. Assert readiness false before watchdog, `EffortDialogNotCleared`, requested/current/suggested effort and attempted selection in reason, at most three Enters, zero tokens/Ctrl+U/boot calls. Must neither wait for a 90-second composer timeout nor return true through NotAnswerable. |
| **V-15** / D-4 / S2 | unit: S.`Recurring_dialogs_cannot_renew_the_readiness_deadline`; S.`Interrupted_probes_share_the_token_write_limit`; A.`Disabling_the_probe_does_not_disable_effort_resolution` | Deadline case: 5,500 ms total budget, 15,000 ms per-dialog budget, max writes 100 solely to isolate time accounting, C1 recurs after each token; terminate by total budget plus one poll/transport return (watchdog 8,000 ms), without resetting time at each modal. Write-cap case: 20,000 ms total, cap 2; two interrupted attempts cannot lead to a third token; nonresponsive result, bounded failure. Disabled-probe rows: effort clears then true, and effort stuck then false; no token in either. Set ready-max wait 7,000 ms and watchdog 9,000 ms for the disabled failure. |
| **V-16** / D-3-D-4 / S2 | unit: S.`Cancellation_and_exit_stop_startup_input` | Cancellation during initial settle, during navigation poll and while verifying clearance; record input count, cancel, await completion, no later writes. Scripted exit makes the owning adapter's exit task complete during a dialog; readiness must not succeed or continue input. Preserve the established cancellation/exit result rather than fabricating successful clearance. Transport delegates must honor cancellation. |
| **V-17** / D-1 / S1-S2 | unit: E.`Legacy_choice_markers_keep_their_existing_classification`; E.`Unrelated_or_inconsistent_screens_are_not_effort_choices` | Run the explicit negative matrix below, plus existing `ClaudeStartupTrustPromptTests` and `RunnerClaudeAdapterTrustPromptTests`. Assert exact legacy kind where specified, not merely non-EffortChoice. No generic/menu action is auto-confirmed by the effort resolver. |
| **V-18** / D-5 / S3 | integration: F.`Interactive_effort_dialog_block_persists_reason_and_cleanup`; F.`Card_effort_dialog_block_withholds_boot_and_cleans_up` | Extend the existing LaunchFixture/FakeAgentProtocolAdapter pattern with readiness=false and the effort block. Interactive and card-launch callers throw/record their established failed result; stored session block is EffortDialogNotCleared, reason is preserved exactly, termination is SystemRequest, lifecycle is Kill then Dispose, and no boot prompt was sent. Query only fixture session/agent IDs. Pair with V-14 so a fabricated block alone cannot certify the adapter. |
| **V-19** / D-2-D-5 / S2 | integration: L.`Local_adapter_keeps_requested_effort_and_probes_after_clearance`; L.`Local_adapter_names_an_uncleared_effort_dialog` | Exercise actual `ClaudeAdapter.StartAsync`/`WaitForReadyAsync` with a small local child fixture as specified below. This pins wiring of launch effort, shared coordinator and named block that helper tests alone cannot prove. |
| **V-20** / D-4-D-6 / S2-S4 | unit + diff inspection: existing P.`A_reading_composer_answers_with_one_write_and_one_kill_line`, P.`The_probe_never_sends_a_carriage_return`, P.`A_token_that_never_renders_fails_and_the_launch_is_told`, P.`A_composer_that_will_not_clear_fails_even_though_the_token_rendered`; existing healthy/disabled tests in `RunnerClaudeAdapterTrustPromptTests`; existing `ClaudeLaunchArgsTests` | No-dialog path still does token/render/Ctrl+U/empty, with no Enter or effort-settle delay. Run legacy probe callers without the new predicate. `git diff 04c1b1c0 -- server/Application/Services/ClaudeLaunchArgs.cs` is comment-only and removes the no-picker claim; `git diff 04c1b1c0 -- server/Application/Services/ModelLevelAliases.cs` must be empty. Review owner-doc updates against D-6. |
| **V-21** / D-2-D-3 / S4 | conditional live probe: C.`Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer` | Execute the isolated canary below once. Passing requires the actual provider dialog, intended selection, resulting current effort and successful composer round trip. Absence of the dialog or unavailable safe prerequisites is explicitly unverified, never a dismissal pass. |

V-11's six probe checkpoints are separate parameter rows with literal input
expectations. Use a pure test predicate recognizing a sentinel modal, so these
tests verify the generic optional hook independently of Claude's parser:

| Checkpoint | Script insertion | Allowed inputs at `InterruptedByModal` |
|---|---|---|
| BeforeFirstToken | Modal in the first pre-write snapshot | none |
| AwaitingToken | First token written, next polling snapshot is modal | first token only |
| BeforeRetype | Token remains absent; publish modal on the fresh snapshot when retype becomes eligible | first token only |
| BeforeFirstClear | Token rendered; publish modal on the fresh pre-Ctrl+U snapshot | first token only |
| BeforeClearRetry | First Ctrl+U swallowed; modal before next clear write | first token, first Ctrl+U only |
| BeforeResponsive | Composer emptied, then modal on the final verdict check | first token, first Ctrl+U only; outcome still InterruptedByModal |

The V-17 negative matrix is explicit about the existing four-way `hasChoices`
predicate. Test each marker **alone**, avoiding a combined fixture that could
hide removal of one alternative:

| Input | Expected classification / action |
|---|---|
| `Choose an option?` plus only `1. Yes` | Choice; effort resolver sends nothing |
| Same question plus only `2. No` | Choice; no effort input |
| Same question plus only `Enter to confirm` | Choice; no effort input |
| Same question plus only `Esc to cancel` | Choice; no effort input |
| Same question with none of those markers and no effort option pair | null |
| Existing numbered trust / highlighted trust / unknown trust fixtures | TrustFolder with the existing layout; retain current trust answer/refusal behavior |
| Existing permission fixture | ToolPermission, no automatic effort input or probe |
| Synthetic `/model` list and ordinary `/effort` list ending `Enter to confirm` | Choice; not the startup-default dialog |
| C1 with question and Switch model different, suggested efforts different, missing Keep, or missing Switch | Not EffortChoice; no effort-resolver input |
| Normal composer/prose quoting the question, explaining Keep/Switch in sentences; labels from separate fenced examples | Not EffortChoice; no effort-resolver input. Do not claim to distinguish two byte-identical full modal screens. |
| Complete C1 option pair with missing/duplicated highlight | EffortChoice but unanswerable; no Enter (covered by V-5), not a generic choice to accept |
| Previously recognized C1 followed by a malformed remnant | The active attempt remains blocked and fails (V-7); do not reclassify disappearance of one label as clearance |

For V-19, put a small test-only console script in
`tests/Antiphon.Tests/Agents/Fixtures/claude-effort-dialog.ps1`. Launch it with
`pwsh.exe -NoProfile -File <fixture> --effort <requested>` through the real local
adapter, with no API/environment credentials. Load a capture file supplied by
the parent; render it via terminal output, not argv/title text. Read keys without
echo, implement only the two menu choices and single-line token/Ctrl+U behavior,
and write a test-owned JSON trace of accepted effort/keys. In one mode the initial
highlight is wrong and the accepted effort must equal the passed request; in
the other every Enter is swallowed. Keep the script ASCII by reading Unicode
screen data from the JSON fixture. The parent sets modern ConPTY using the
`ANTIPHON_PTY_BACKEND=modern` selection mechanism and restores its previous value
in `finally`; because this changes process-wide state, mark the class
`[NotInParallel]` without a group key, Integration and with the
assembly-local `ParallelLimiter<ProcessSpawnLimit>`. Kill/dispose only the child
the test started, then verify it exited. This is wiring evidence, not evidence
that Claude itself binds those keys. It adds no FakeClaude feature.

For V-18's card arm, call `fixture.CreateCardAsync()` followed by
`fixture.StartCardSessionAsync(cardId, "boot-must-not-be-sent", kind: AgentKind.ClaudeCode)`.
This exercises `AgentSessionService.StartAsync`; select the stored session and
RunAttempt by that card ID, require RunPhase.Failed, and check the fake received
no boot body. The interactive arm calls `fixture.LaunchInteractiveAsync()`.

### Guards the regression

| ID | Future regression | Caught by / load-bearing assertion |
|---|---|---|
| **R-1** | Effort detection falls back behind the legacy early return or becomes Fable-only | V-1's three untouched snapshots lack every old marker; V-2/V-3 non-Fable/effort cases require the same structure. |
| **R-2** | Generic text/permission/menu is treated as an authorized effort confirmation | V-6 and V-17 require exact legacy kinds, consistent modal structure, generic-answer refusal and zero unauthorized keys. |
| **R-3** | Default highlight or provider recommendation wins over requested effort | V-4/V-5 assert independent fake AppliedEffort/AcceptedOption and no Enter for ambiguous intent/highlight. |
| **R-4** | A stale target, repaint, blank frame or malformed remnant becomes clearance | V-6/V-7 require refreshed identity, two consecutive clear observations and no token before positive next-UI evidence. |
| **R-5** | An accepted key is reported as applied effort even when current status contradicts it | V-9 fails the contradictory banner and distinguishes a banner scrolling away. V-21 is separate real-provider evidence. |
| **R-6** | A late modal receives a token/retry/Ctrl+U or invalidates proof unnoticed | V-11's six checkpoints stop the exact pending action; V-12 requires a new round trip after the modal. |
| **R-7** | Recurrent dialogs reset time/write budgets, or swallowed input causes endless retry | V-8/V-14/V-15 assert attempt counts, spacing, cumulative writes, a fixed deadline and completion before an independent watchdog; V-16 stops input on cancellation/exit. |
| **R-8** | Recognized effort failure becomes lenient NotAnswerable, generic timeout, or leaked child | V-14 false/named block, V-18 persisted diagnosis/no boot/Kill-before-Dispose, V-19 actual local adapter outcomes. |
| **R-9** | Trust/permission handling, no-dialog launches or disabled probes change incidentally | V-13/V-17/V-20 retain trust layout behavior, permission no-input pass-through, and normal/disabled probe contracts. |
| **R-10** | The fix quietly changes model/effort composition or substitutes synthetic success for live acceptance | V-20 unchanged alias/argument behavior and comment-only diff; V-21 reports actual evidence or an explicit unverified outcome. |

### Positive controls

Run these **after the fixed implementation and named tests exist**. Each row
specifies a single semantic guard mutation; Code records the actual file/line
and one-line diff used. Mutate production behavior, never the test expectation,
fixture data, timeouts or test discovery. Existing source has no effort helper,
so copying future tests onto the baseline and obtaining compile failures is not
a positive control.

All 13 controls are method-scoped red/restored-green cycles. A parameterized
method runs its named rows; the report names which expected assertion failed.
Controls touching the same source file/method run separately. Given this small
set and the shared readiness state, execute serially rather than set up PC
shards. Preserve a committed implementation tip before mutation; restore and
refresh source timestamps/rebuild before green, and leave no mutation in the
delivered tree. CARD-0451's full protocol in `docs/testing-and-build.md` applies.

| ID / V-R / guard | One-line mutation in the planned implementation | Exact method and expected red |
|---|---|---|
| **PC-1** / V-1, R-1 / recognize the real shape | In `ClaudeBlockingPrompt.cs`, disable only the EffortChoice recognition branch before `hasChoices`. | E.`Captured_screens_are_effort_choices`: all three rows fail the non-null/EffortChoice assertion. Restore: all three parse. |
| **PC-2** / V-17, R-2 / require coherent structure | In `ClaudeEffortPrompt.cs`, replace the question-model/option-model equality check with true, keeping the complete labels parsable. | E.`Unrelated_or_inconsistent_screens_are_not_effort_choices`: the row with Fable in the question and Nimble in Switch is incorrectly admitted as EffortChoice. Restore: it is rejected. A parse exception is not the expected red. |
| **PC-3** / V-5, R-3 / apply explicit intent | In the target-option selector, force the chosen option to Keep. | E.`Requested_effort_is_applied`: Fable/high Switch row records actual xhigh and fails `AppliedEffort == "high"`; restored selection records high. |
| **PC-4** / V-4, R-3 / refuse ambiguous explicit intent | Change the invalid/conflicting effort-intent arm to the absent-intent Keep fallback. | E.`Ambiguous_or_unmatched_intent_types_nothing`: an invalid/conflicting case writes a key or succeeds; zero-input/refusal assertion fails. |
| **PC-5** / V-5, R-3 / verify highlight before Enter | Bypass the final intended-highlight check immediately before the confirming Enter. | E.`An_unmovable_wrong_highlight_withholds_Enter`: fake still highlights Switch and records Enter; no-Enter assertion fails. |
| **PC-6** / V-6, R-2/R-4 / revalidate dialog identity | Replace the fresh snapshot used for the next per-key validation with the last validated effort snapshot. | E.`A_changed_dialog_identity_stops_further_input`: publish a correctly highlighted effort snapshot, then replace it at the mandatory fresh pre-key read; the stale snapshot allows a key into the different menu and fails no-subsequent-input. |
| **PC-7** / V-7, R-4 / observe real clearance | Replace the post-Enter verified-clearance result with unconditional success. | S.`Redraw_is_not_clearance_before_the_probe`: trace records probe input before two consecutive valid clear frames, or readiness wrongly succeeds while the returning modal remains. |
| **PC-8** / V-9, R-5 / reject contradictory applied effort | Disable the visible post-selection effort-mismatch failure branch. | S.`A_contradictory_resulting_effort_fails_readiness`: incorrectly probes/returns ready for high after requested xhigh; false/no-probe assertions fail. |
| **PC-9** / V-11, R-6 / guard probe input and verdict | In `ComposerInputProbe.cs`, make the centralized optional blocker evaluation always false while leaving the callback installed. | P.`A_modal_interrupts_before_each_probe_write_or_verdict`: checkpoint rows record the forbidden write or wrong outcome. Report all six row results; every installed guard location must be reached. |
| **PC-10** / V-12, R-6 / discard pre-modal probe proof | Disable invalidation of the pre-modal token-rendered state when the coordinator resumes after clearance. | S.`A_late_dialog_requires_a_fresh_post_clearance_round_trip`: ready without a post-dialog token render/clear; new-round-trip trace assertion fails. If the implementation achieves invalidation solely by starting a new probe, replace that restart with reuse of its pre-interruption success/evidence at the one decision point. |
| **PC-11** / V-15, R-7 / keep one deadline | Replace remaining total budget on modal recovery with the original full budget. | S.`Recurring_dialogs_cannot_renew_the_readiness_deadline`: operation is incomplete at the independent 8-second watchdog; explicit completion assertion fails, then finally cancels/awaits it. The high write cap prevents a different guard from hiding this mutation. |
| **PC-12** / V-14, R-8 / fail a known effort blocker | In the runner adapter's effort-failure outcome mapping, return the existing lenient NotAnswerable/ready path instead of false/named block. | A.`An_effort_dialog_that_never_clears_fails_within_its_budget`: wrongly true or missing EffortDialogNotCleared; deadline completion alone cannot pass. |
| **PC-13** / V-6, R-2 / deny generic confirmation | Remove the new generic-answer API's refusal for EffortChoice, letting it send its affirmative key. | E.`Generic_answering_cannot_confirm_an_effort_choice`: any input is an assertion failure; restored generic API sends none. |

PC-2 and PC-6 must be small working bypasses of the specified guard, not mutations
that merely crash parsing. If implementation structure differs, record the exact
equivalent one-line bypass and the expected assertion. If a control stays green,
repair the test/guard reachability before reporting it; do not call an unrelated
fixture failure a substitute. Retry caps/spacing, cumulative token ceiling,
persistence and cancellation have direct assertions in V-8/V-15/V-18/V-16; the
PCs above target independent authorization, readiness-proof and deadline guards
without multiplying equivalent mutations.

Commands for a PC use the class table to choose the project and full method:

```powershell
# Example PC-5 red; repeat with a different filename after restoring fixed source.
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter "/*/*/ClaudeEffortPromptTests/An_unmovable_wrong_highlight_withholds_Enter" --report-trx --report-trx-filename c449-pc05-red.trx
# Example PC-12 red (never run the other assembly concurrently).
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter "/*/*/RunnerClaudeAdapterEffortPromptTests/An_effort_dialog_that_never_clears_fails_within_its_budget" --report-trx --report-trx-filename c449-pc12-red.trx
```

For each PC report mutation diff, implementation SHA, exact method-filtered
command, fresh TRX path, expected assertion failure/count, restoration, and
restored-green count. A zero-test invocation, skipped PC, build failure or
test-framework timeout does not satisfy red or green.

### Isolated real-picker acceptance

V-21 is an evidence-gathering canary with the conditional result already allowed
by the plan. Its test class is Integration, `[NotInParallel("Headed")]`, with
the assembly-local process limiter. Require both `ANTIPHON_HEADED_TESTS=1` and
`ANTIPHON_REAL_CLI_STUB_TESTS=1`; otherwise skip with the missing prerequisite.
Use `RealCliStubGate`/`HeadedClaudeGate` and resolve the actual Claude executable,
not an operator wrapper that may override isolation.

1. Start a test-owned `FakeLlmApiServer` on its random local port. Use
   `RealCliStubEnv.ForClaude` with a generated synthetic key and a fresh private
   `CLAUDE_CONFIG_DIR`; seed only test onboarding/trust with
   `RealCliStubClaudeConfig.SeedOnboarding`. Do not copy a real Claude home,
   login token or default-effort preference. Do not fake the presence of the
   effort dialog by rendering it in the canary.
2. Start the real CLI through `PtyAgentRunner("modern")`, cols=120, rows=30,
   `--model fable --effort xhigh --dangerously-skip-permissions`, using the
   isolated environment. Record CLI version (2.1.263 is the captured target;
   another version is separate evidence), backend and launch effort, without
   dumping environment or synthetic authorization headers.
   Assert `runner.Backend` reports `PtyBackend.ModernConPty`; a fallback is not
   evidence about the captured backend.
3. Within 45 seconds observe the actual matching effort menu. If absent,
   unsupported, or an unrelated prerequisite blocks it, record **V-21 unverified**
   and the specific visible condition, then dispose the owned session. API-mode
   isolation may not trigger a subscription-only prompt; do not alter live
   preferences or retry against a live provider to make this case pass.
4. Run the production effort resolver/coordinator against real snapshot/write
   delegates; retain sanitized before, selected-row, after and cleared-composer
   snapshots plus key timestamps. Require Keep xhigh, fresh visible current
   xhigh after dismissal, two confirmed clear frames, rendered probe token and
   empty composer after Ctrl+U. No work prompt, slash command or model turn is
   submitted. If the effort cannot be observed after dismissal, report the
   narrower selection/clearance evidence and leave applied-effort acceptance
   unverified. Once the target menu has appeared, refusal, failed selection,
   timeout or contradictory effort is a failure, not a skip.
5. Total canary budget is 120 seconds (including the 45-second appearance window;
   pass remaining time to subsequent phases). Always cancel/await outstanding
   work, kill/dispose only the owned child, verify exit, and remove only its
   private scratch directories after resolved-path checks. Confirm no submitted
   user turn/probe-as-turn reached the stub; list unexpected chat requests as a
   failure rather than claiming that no billing/egress follows from a quiet log.

Run once from PowerShell, preserving/restoring prior values of the two opt-in
flags in the calling session:

```powershell
$env:ANTIPHON_HEADED_TESTS = '1'
$env:ANTIPHON_REAL_CLI_STUB_TESTS = '1'
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter "/*/*/ClaudeEffortPromptCanaryTests/Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer" --report-trx --report-trx-filename c449-real-picker.trx
```

This is deliberately separate from mandatory scripted verification. No canary
was launched by this TestDesign task. Do not use the existing trust canary's
real-home mutation as the effort canary setup. If Code changes trust answering
beyond the planned narrow transition predicate, rerun the existing trust canary
under its owner instructions and report that added scope.

### Out of scope

- Model quality, token usage/cost comparisons, model turns and provider rollout
  eligibility. Scripted AppliedEffort is an independent fixture oracle; only a
  qualifying V-21 run establishes live provider application of the selection.
- Fixing other startup menu types or changing their pass-through policy. V-17
  protects their current classification/input contract; it does not make them
  automatically answerable.
- Alias tables, argument composition, global preferences, transcript transport,
  terminal backend changes, supervision/retry policy and deployment. Existing
  relevant tests/diffs are checked; these areas are not modified for this card.
- Full client/browser, E2E or full-solution regression sweeps. This change is
  covered at the pure-helper, both-adapter and launch-failure persistence seams.
  Production runner port 17204 is used only for the completed read-only capture
  retrieval, never by tests or the canary.

### Cost

Mandatory regression execution is the following serial class set. Use one class
per invocation to avoid ambiguous filters; retain TRX with per-method outcomes.
The F class contains pre-existing unrelated cases, but its whole class is the
focused service-failure regression required by S3. No namespace/full-assembly
run is called for here.

```powershell
$ptyClasses = @('ClaudeEffortPromptTests', 'ClaudeStartupReadinessTests', 'ComposerInputProbeTests', 'ClaudeStartupTrustPromptTests')
foreach ($testClass in $ptyClasses) {
    dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter "/*/*/$testClass/*" --report-trx --report-trx-filename "c449-$testClass.trx"
    if ($LASTEXITCODE -ne 0) { throw "Failed: $testClass (exit $LASTEXITCODE)" }
}
$serverClasses = @('RunnerClaudeAdapterEffortPromptTests', 'RunnerClaudeAdapterTrustPromptTests', 'ClaudeAdapterEffortPromptTests', 'AgentSessionLaunchFailureTests', 'ClaudeLaunchArgsTests')
foreach ($testClass in $serverClasses) {
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter "/*/*/$testClass/*" --report-trx --report-trx-filename "c449-$testClass.trx"
    if ($LASTEXITCODE -ne 0) { throw "Failed: $testClass (exit $LASTEXITCODE)" }
}
```

Verification floor estimate: **15-25 minutes** for builds, the focused regressions
and 13 method-scoped red/restored-green controls; the real-picker attempt adds up
to 2 minutes of observation plus build/setup/cleanup. This is an estimate, not
a measured run time or a reason to broaden tests. Pure parser tables cost no
timed waits; successful asynchronous effort cases each include the 1.5-second
settle, and bounded failures deliberately exercise 5.5-7-second budgets.
Use already-built outputs only when they are verified to contain the relevant
fixed/mutated/restored source; never hide a stale mutation behind `--no-build`.

Code's report must enumerate **V-1..V-21, R-1..R-10 and PC-1..PC-13**, with actual
test counts/failures, source SHA, commands and evidence paths. V-21 can be
explicitly unverified under the stated condition; all other behavioral checks
and every PC require executable results. TestDesign changed only this appended
section and the companion capture JSON; no implementation test/build result
is claimed here.
