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
