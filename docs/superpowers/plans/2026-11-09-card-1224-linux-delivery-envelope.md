# CARD-1224 Linux UnixPty delivery envelope

Plan for task `215b89b7` on `feat/card-task-215b89b7`. Base `188ac942166884f060fdcc7cbf3b15f670a27b43`.

Measure the Linux prompt path with the staged fake agents through `PtyAgentRunner`, commit that receipt, and record the derived ceilings on an explicit `CeilingsFor(PtyBackend.UnixPty)` arm. A runner-bound session whose cached capabilities say `UnixPty` uses that arm. Every other report stays on the inbox arm. A body over the recorded ceiling spills or raises the existing oversize incident, the same way Windows does. No live Claude, Codex, or Grok process, and no paid model call.

`GET /api/runner-defaults` and `GET /api/session-runners` were not readable from this runner (`127.0.0.1:17202` connection refused). This plan names no fleet host. Checkpoints omit `-Runner` and `-Platform`. CP-1 is the Linux lane because its tests skip unless `OperatingSystem.IsLinux()` is true. CP-2, CP-3, and CP-4 are portable.

This plan adds no session that waits for input. The CARD-1083 release checklist does not apply.

The historical investigation `docs/investigations/2026-10-13-card-0038-linux-docker-readiness.md` is not in this checkout. It remains at `d7648f8e9`. Do not edit it. The living text is `docs/docker-stack.md` and `docs/session-runtime-invariants.md`.

## Decisions

- **D-1.** The automated oracle is the staged fake, not a live TUI. `PtyAgentRunner.WriteCoreAsync` (`src/Antiphon.Agents.Pty/PtyAgentRunner.cs:808-815`) writes one UTF-8 buffer to the Porta master. FakeClaude's `SUBMITTED:` line is the body the composer accepted (`src/Antiphon.FakeClaude/Program.cs:640`). `RAWBURST:` with `ANTIPHON_FAKE_DEBUG_INPUT=1` shows whether `ESC[200~` arrived (`:441-456`). FakeGrok's `SUBMITTED:` line shows whether newlines survived (`src/Antiphon.FakeGrok/Program.cs:591-598`). There is no `FakeCodex` project. Codex stays join-safe and unmeasured.
- **D-2.** The search stops at 86,400 bytes. That is the modern single-write edge, used here as a comparison bound. A whole delivery at 86,400 records 86,400 as the edge of the evidence. It does not delete the tripwire and it does not prove a larger write. The ladder is 64, 900, 1,024, 1,350, 4,096, 16,384, 43,200, 86,400. Each size runs twice. Paced rows are 1,350, 43,200, and 86,400, split at 1,024 bytes with 25 ms between writes, markers still wrapping the whole body.
- **D-3.** Ceilings are derived, then copied. `wholeSingle` is the largest ladder size with both single-write reps whole. `wholePaced` is the largest paced size with both reps whole. A missing size counts as not whole. The 64-byte control must be whole or the run is invalid and records nothing. Markers survived only when the debug burst contains `ESC[200~` and one submit contains every line of a four-line body that also contains a CR. If markers did not survive, or `wholeSingle` is under 1,024, or `wholePaced` is under 1,024, the arm keeps inbox numbers: brief 900, reply 3,000, single write 1,024, `DeliveryBackend.InboxConhost`. Otherwise `single = wholeSingle` (at most 86,400), `brief = min(wholePaced, single / 2)`, and `reply = brief / 3`. `brief` below 1,024 also keeps the inbox triple. A raised arm uses `DeliveryBackend.UnixPty`.
- **D-4.** `CeilingsFor` grows an explicit `PtyBackend.UnixPty` arm. The discard arm stays inbox. The Unix arm appends `card-1224-unixpty` to the caller reason in every outcome, including the outcome that keeps inbox numbers. `IsPastePath` stays `Backend != InboxConhost`, so it becomes true only when D-3 selects `DeliveryBackend.UnixPty`.
- **D-5.** Runner-bound input stops hardcoding `PtyBackend.InboxConhost` only for a cached report of `UnixPty`. `null`, empty, `InboxConhost`, `ModernConPty`, and any other string stay on the inbox arm. A remote `ModernConPty` report is not evidence for this card. Unknown is not a raise. The lookup is `ISessionRunnerDirectory.DescribeAsync` on the session's `RunnerId`, which reads the phone-home slot cache (`PhoneHomeRunnerDirectory.cs:798-839`). It does not query the database and it does not probe the desktop runner for a remote id. No `RunnerId` means no lookup.
- **D-6.** Join-safe delivery is unchanged. `RequiresJoinSafeDelivery` still denies every kind except `ClaudeCode` (`PtyDeliveryCeilings.cs:79`). A Grok measurement that keeps newlines does not move Grok off that gate. Codex has no fake, so it cannot move either.
- **D-7.** Over-ceiling behaviour is the Windows behaviour. A Claude brief over `BriefInlineMaxBytes` spills to a file and a pointer is typed (`AgentTaskDispatcher.FitBriefForTyping`, `7086-7159`). A queue body over `SingleWriteMaxBytes` spills when `SpillQueueBodyAsync` can (`SessionMessageQueueService.cs:376-431`), and a body that still reaches the typer is sent and records `OversizedTerminalDelivery` (`:4002-4010`). Nothing in this card drops the message. Specialist full-inline still requires `DeliveryBackend.ModernConPty` (`SpecialistInputPolicy.cs:46-47`).
- **D-8.** Live Claude, Codex, and Grok binaries are operator-gated and are not a slice. `PtyPasteMarkerExperiments.Real_claude_delivery_envelope` stays Windows-only. Do not port `ClSession`, `GkSession`, or `CxSession`.
- **D-9.** No migration, no HTTP DTO, no route, no compose change, no change to `PtyBackendPolicy`, no change to the modern-only corroboration probe in `PtyDeliveryProfile` (`:108-128`). A Linux process already resolves `UnixPty` (`PtyBackend.cs:90-92`) and already calls `CeilingsFor` with it (`PtyDeliveryProfile.cs:71`). The new arm is what that call returns.

## Ground truth

| What the card or the 2026-10-13 investigation says | What this checkout does |
|---|---|
| `PtyBackend.UnixPty` exists and Linux selects it. The investigation cites `PtyBackend.cs:20` and `:90` at `d7648f8e9`. | `UnixPty = 2` is `PtyBackend.cs:19-20`. Off Windows, `Resolve` returns it at `:90-92` before any Windows selector. `PtyAgentRunner` writes through Porta at `:808-815`. |
| `CeilingsFor` has only a ModernConPty arm. The investigation cites `DelegationSettings.cs:291`, brief 900 at `:159`, tripwire 1,024 at `:201`. | The switch is `DelegationSettings.cs:300-308`. `ModernConPty` returns 43,200 / 14,400 / 86,400. Every other value, including `UnixPty`, returns `DeliveryBackend.InboxConhost`, `BriefInlineMaxBytes` 900 (`:168`), `ReplyInlineMaxChars` 3,000 (`:124`), `PtySingleChunkBytes` 1,024 (`:210`). |
| Runner-bound input forces inbox. The investigation cites `AgentTaskReplyService.cs:1059`. | `FitRefinementForTyping` does that at `AgentTaskReplyService.cs:1096-1098` when `session.RunnerId` is set. `FitBlockedReplyForTyping` does it at `:1161-1162` for every runner-bound reply. `CeilingsForBrief` does it whenever `runnerCwd` is set (`AgentTaskDispatcher.cs:7170-7176`). `SessionDeliveryProfile.ForSessionAsync` does it whenever `RunnerId` is set (`:58-68`). The spill fallback does it at `SessionMessageQueueService.cs:295-296`. |
| The only stored Linux delivery is a 700-character Codex UserPrompt, not a large paste. | That sentence is in the investigation at `d7648f8e9` line 46. This checkout has no newer Unix paste receipt. `docs/docker-stack.md:1065` still says Linux is supported at the inbox numbers and names this card. |
| `Unix_backend_keeps_conservative_limits` pins the inbox triple and `IsPastePath` false. | `PtyDeliveryCeilingsTests.cs:308-315`. It passes on this base. It is the test the card says must fail once `UnixPty` stops resolving to those numbers. |
| A non-Claude brief already spills on every backend. | `ForAgentKind` sets `BriefInlineMaxBytes` to 0 (`PtyDeliveryCeilings.cs:99-106`). `SingleWriteMaxBytes` is not narrowed. |
| The Windows instrument cannot run here. | `Real_claude_delivery_envelope` requires `ClSession` and the Windows redistributable (`PtyPasteMarkerExperiments.cs:250-254`). `PtyLargeWriteTests` skips off Windows and looks for `fakeclaude.exe` (`PtyLargeWriteTests.cs:40-44`). Linux staging is the extensionless file under `fakeclaude/` (`FakeClaudeContractTests.cs:1796-1797`). |

`tests/impacted-classes.json` matches this diff at three kinds. `server/Application/Services/AgentTaskDispatcher.cs` selects `statement-budgets` (`:77-84`, method `DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets`, 12 arguments) and `land-notification` (`:97-118`). `server/Application/Services/AgentTaskReplyService.cs` selects `server-git-start-sites` (`:146-165`: `ServerGitProcessTests`, `GitIndexLockTests`, `ServerGitAssemblyTests`). No migration, public HTTP DTO, route, operation label, or remote script changes. The statement-budget method is CP-4. The land-notification classes and the three git classes are Final scope through `scripts/impacted-classes.ps1 -Range <base>..<tip> -Json`. They are not ordinary rows. Do not add a database read on the brief path; CP-4 is the witness that the cached lookup added no SQL.

## Intended behaviour

### Before

1. Linux spawn resolves `UnixPty`. `CeilingsFor(UnixPty)` falls through to inbox: 900 / 3,000 / 1,024, `IsPastePath` false.
2. A Linux server's own `PtyDeliveryProfile` therefore types under the inbox ceiling. The modern probe never runs, because local backend is not `ModernConPty`.
3. Any session with `RunnerId` or `RunnerCwd` is fitted under inbox even when that runner's capabilities say `UnixPty`. A Claude brief above 900 bytes spills. The reporting contract alone is about 838 bytes, so a normal brief spills.
4. Non-Claude briefs spill because the inline ceiling is 0. That is the composer axis, not the pty axis.
5. A body that still exceeds `SingleWriteMaxBytes` at the typer is sent, logged, and recorded as `OversizedTerminalDelivery`. A spill that cannot be written becomes an API pointer. The message is not dropped.
6. Windows inbox and modern numbers are the measured Windows envelopes. Herdr is a separate record.

### After

1. S1 writes `docs/superpowers/plans/2026-11-09-card-1224-linux-delivery-envelope-receipt.md` from one Linux run of the fake-agent ladder. The receipt is the only place the byte counts are chosen.
2. `CeilingsFor(UnixPty, reason)` returns the D-3 record and its reason contains `card-1224-unixpty`. A raised record uses `DeliveryBackend.UnixPty = 3`. An unraised record uses `DeliveryBackend.InboxConhost` and the inbox triple.
3. A runner-bound fit whose cached `Capabilities.PtyBackend` is `UnixPty` uses that record. Every other report uses the inbox record, still through `CeilingsFor(InboxConhost)`, and does not append the measurement id.
4. Claude briefs follow the recorded `BriefInlineMaxBytes`. Non-Claude briefs stay at 0. Replies follow the recorded `ReplyInlineMaxChars` only on the Unix arm; inbox and modern reply ceilings stay 3,000 and 14,400.
5. Spill, pointer, and `OversizedTerminalDelivery` stay the mechanisms in D-7, now compared with the record that step 3 selected. `docker-stack.md` and `docs/session-runtime-invariants.md` quote the receipt id, the three derived numbers, and the fake-agent oracle.
6. Latency is a pair of millisecond samples in the receipt. No product timeout is added.

### Unchanged

`PtyBackendPolicy`, Porta spawn, `PtyInputEncoding`, the modern and inbox constants, `HerdrCeilings`, `ForAgentKind`, `SpecialistInputPolicy`'s `ModernConPty` check, `PtyDeliveryProfile`'s modern-only probe, Windows `PtyLargeWriteTests`, `Real_claude_delivery_envelope`, argv guards, and every caller that has no runner report. `CeilingsForBrief(modern, cwd, settings)` with no report still returns inbox, which is `PtyDeliveryCeilingsTests.A_runner_bound_brief_is_fitted_to_the_inbox_ceiling_not_the_desktop_pty`.

## Design

### What is measured

One `PtyAgentRunner` per trial, backend left to the platform default so Linux resolves `UnixPty`. The child is the staged fake: `Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude")` on Linux, and the same shape for `fakegrok`. Do not append `.exe` on Linux. Fail the test if the file is absent on Linux. Do not skip a missing fake on Linux.

The body is ASCII, so characters and UTF-8 bytes match. Encode with `PtyInputEncoding.EncodeBody`. Submit with a separate `\r`, as `PtyLargeWriteTests.AssertArrivesWholeAsync` does. Whole means the `SUBMITTED:` line contains every marked line. Lost means any marked line is absent, or more than one `SUBMITTED:` appears for one trial. The control is one 64-byte line. A control that is not whole fails the run before any ceiling is written.

Bracketed paste: a four-line body with an embedded CR, `ANTIPHON_FAKE_DEBUG_INPUT=1`. Markers survived when the raw burst contains the paste start and one submit contains all four lines. Grok uses `ANTIPHON_FAKE_LF_ENTER=1` on Linux, which is what `RunnerGrokAdapterReadyTestsPty` sets, and records whether the submitted text still contains `\n`. Codex is a receipt row `codex: unmeasured` because there is no fake. Latency is `Stopwatch` around the control write and around one write at the derived brief size, two reps, stored as integers. It is not asserted against a product limit.

`ANTIPHON_FAKE_STDIN_CLIP` stays unset. With the clip model off, a whole body only supports a raise when the markers arrived. That is D-3. A lossless pipe with the markers stripped stays at inbox.

### Where the numbers go

S1 commits the receipt. S3 copies `singleWriteBytes`, `briefInlineMaxBytes`, and `replyInlineMaxChars` into `UnixPtySingleWriteMaxBytes`, `UnixPtyBriefInlineMaxBytes`, and `UnixPtyReplyInlineMaxChars` on `DelegationSettings`, next to the modern trio. The test reads the receipt and compares `CeilingsFor(UnixPty)` to it, so a hand-edited constant that drifts from the receipt fails.

`DeliveryBackend` gains `UnixPty = 3` only in the raised outcome. The inbox outcome does not need the new enum value. Add the enum in S3 either way so the raised outcome has a home; the unraised outcome must not use it.

`RunnerBoundDelivery` is a new static type in `server/Application/Settings/RunnerBoundDelivery.cs`. `ForRunner(DelegationSettings, string? reportedPtyBackend)` implements D-5. `SessionDeliveryProfile` takes an optional `ISessionRunnerDirectory?` as its last constructor parameter. The `RunnerId` branch passes `DescribeAsync(runnerId).Capabilities.PtyBackend` into `ForRunner`. A null directory, a null descriptor, or null capabilities passes null and stays inbox. `CeilingsForBrief` gains an optional `string? reportedPtyBackend = null` and, when `runnerCwd` is set, returns `ForRunner` instead of the hardcoded inbox call. The async call sites pass the cached report: `FitBriefForSession` from `EnqueueReleasedSeatAnswerBriefAsync` (`AgentTaskDispatcher.cs:5678-5680`), `DeliverReuseMessagesAsync` (`:9268`, add `RunnerId` to the select at `:9232-9235`), `InsertAbsentBriefAsync` (`SessionMessageQueueService.DispatchBrief.cs:184-185`), `AcceptDispatchBriefAsync` (`AgentSessionService.cs:1152-1153`), `QueueLaunchBriefAsync` (`GrokRulesRefreshService.cs:197-198`, resolve the directory from the scope that method already opens), `FitRefinementForTyping`, `FitBlockedReplyForTyping`, and the fallback at `SessionMessageQueueService.cs:295-296`.

### Rejected

Raising Linux to the modern 43,200 / 86,400 pair without a Linux receipt. Those numbers are one Windows machine and one Claude version.

Deleting the tripwire because Porta does not clip. The card forbids that. D-3 records the measured edge and the typer still sends past it only with `OversizedTerminalDelivery`.

Arming `ANTIPHON_FAKE_STDIN_CLIP` and treating the opt-in 1,024 model as the Linux cliff. That model is the Windows typed-input cliff. It is off by default, and paste bytes are exempt inside the fake.

Trusting a phone-home `ModernConPty` report. That would undo CARD-0649 for any runner that says modern. This card only recognises `UnixPty`.

Relaxing `ForAgentKind` from the Grok fake. The gate is default-deny until a named kind is moved, and this card does not move one.

A live composer-counter run on server2. The brief forbids a live agent and a paid call. D-8 records the operator gate.

A server-side `OperatingSystem.IsLinux()` check in `CeilingsFor`. The runner's reported backend is the input. A second OS check would raise a Windows process that happens to be running on a mis-detected host, and it would hide a Linux runner that still reported `InboxConhost`.

### Receipt shape

`docs/superpowers/plans/2026-11-09-card-1224-linux-delivery-envelope-receipt.md` contains these literals, one per line:

- `measurementId: card-1224-unixpty`
- `oracle: fake-agent`
- `markersSurvived: true` or `markersSurvived: false`
- `singleWriteBytes: <int>`
- `briefInlineMaxBytes: <int>`
- `replyInlineMaxChars: <int>`
- `grokNewlinesKept: true` or `grokNewlinesKept: false`
- `codex: unmeasured`
- `latencyControlMs: <int>`
- `latencyBriefMs: <int>`

Plus one result line per ladder trial: size, mode (`single` or `paced`), rep, `WHOLE` or `LOST`. The harness writes this file only under an explicit record switch used by the unlisted S1 run. Checkpoint runs read the committed file and do not write it.

## Tests

### S1 harness

New class `tests/Antiphon.Agents.Pty.Tests/UnixPtyDeliveryEnvelopeTests.cs`. `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`. No `[NativeLinuxOnly]`, so the linux-only-policy selector does not gain a new literal. Each method calls `OperatingSystem.IsLinux().ShouldBeTrue()` and then throws `SkipTestException` when that is false. On Linux a missing fake fails.

Methods:

- `Control_body_arrives_whole`
- `Single_write_ladder_matches_the_receipt`
- `Paced_ladder_matches_the_receipt`
- `Bracketed_paste_markers_match_the_receipt`
- `Multiline_lf_body_matches_the_receipt`
- `Grok_newline_join_matches_the_receipt`
- `Delivery_latency_is_recorded`

S1's first unlisted run is the record switch. It prints the ladder and writes the receipt. Commit that receipt. The methods above then assert the live trials against it. A later drift fails CP-1. Do not point the record switch at the repo during CP-1.

### S2 red witnesses

New class `tests/Antiphon.Tests/Application/UnixDeliveryCeilingTests.cs`, `[Category("Unit")]`. It reads the receipt with the same literal keys. S2 also adds `RunnerBoundDelivery.ForRunner` returning `CeilingsFor(PtyBackend.InboxConhost, "runner-bound input")` and ignoring the report. No Unix arm yet.

- `Unix_arm_names_the_receipt`. `CeilingsFor(PtyBackend.UnixPty, "probe").Reason` contains `card-1224-unixpty`. Red on S2 because the reason is `probe`.
- `Unix_numbers_match_the_receipt`. Brief, reply, single write, and backend match D-3 applied to the receipt. Red on S2 when the receipt raises any number or selects `UnixPty`. When the receipt keeps the inbox triple, this one is green on S2 and the reason witness is the red. Count the TRX. Do not change the assertion.
- `Claude_brief_follows_the_recorded_inline_ceiling`. Build a Claude brief under the recorded ceiling and one over it. Under is inline. Over contains `YOUR BRIEF IS NOT IN THIS MESSAGE`. Use `FitBriefForTyping` with `CeilingsFor(UnixPty)`.
- `Runner_unix_report_uses_the_unix_arm`. `ForRunner(settings, "UnixPty").Reason` contains `card-1224-unixpty`. Red on S2.
- `Runner_without_a_report_stays_inbox`. `ForRunner(settings, null)` and `ForRunner(settings, "ModernConPty")` are brief 900, single write 1,024, backend `InboxConhost`. Green on S2. This is the regression, not the red.
- `Non_claude_brief_inline_ceiling_stays_zero`. `CeilingsFor(UnixPty, "probe").ForAgentKind(AgentKind.Grok).BriefInlineMaxBytes` is 0, and the same for `AgentKind.Codex`. Green on S2.
- `Docker_stack_records_the_unix_receipt`. `docs/docker-stack.md` contains `card-1224-unixpty` and the three decimal numbers from the receipt. Red on S2.
- `Session_runtime_records_the_unix_contract`. `docs/session-runtime-invariants.md` contains `card-1224-unixpty`, the three numbers, and the sentence `A UnixPty body over the recorded single-write ceiling is still sent and raises OversizedTerminalDelivery.` Red on S2.
- `Specialist_full_inline_stays_on_modern`. `SpecialistInputPolicy.cs` still contains `Backend != DeliveryBackend.ModernConPty`. Green on S2.

Unlisted, each under `pwsh -NoProfile -File scripts/build-slot.ps1`, after the S2 commit and before S3:

- Label `c1224-s2-ceilings`. `dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/*/UnixDeliveryCeilingTests/*"`. Expect the reason witness, the runner-unix witness, and both doc witnesses to fail. Record the other counts from the TRX.
- Do not run CP-1 again in S2.

Leave `Unix_backend_keeps_conservative_limits` in place through S2 so the old pin stays true while the new class is red. S3 deletes that method. Its assertions move into `Unix_numbers_match_the_receipt` for the inbox outcome only. The modern and inbox methods in `PtyDeliveryCeilingsTests` stay.

### S3 behaviour

The Unix arm, the `ForRunner` mapping, the call-site wiring, the doc edits, and the deletion of `Unix_backend_keeps_conservative_limits`. No new test methods in S3. `docker-stack.md` row "Delivery ceilings" keeps the feature name and replaces the blocker sentence that says Linux is stuck at the inbox numbers with the receipt id, the three numbers, and whether markers survived. `docs/session-runtime-invariants.md` gains the contract sentence on Gotcha #41 (`:1395`), beside the Windows typed-input paragraph, and states that the oracle is the fake agent.

## Migrations

None. No `server/Migrations` file, no model change, no `Down`.

## Slices

### S1 — measure (about 90 minutes)

Add `UnixPtyDeliveryEnvelopeTests` and the record switch. One unlisted build-slot run writes the receipt. Commit the harness and the receipt. Push. Then run CP-1.

### S2 — red witnesses (about 80 minutes)

Add `RunnerBoundDelivery` in its inert form and `UnixDeliveryCeilingTests`. Commit and push before any Unix arm or doc edit. Run the unlisted S2 filter and record the failures.

### S3 — record the arm (about 110 minutes)

Implement D-3, D-4, D-5, and D-7's wiring. Update the two docs. Delete `Unix_backend_keeps_conservative_limits`. Commit and push. Run CP-2, CP-3, and CP-4.

## Performance budget

All four rows are provisional. None of these classes has a measured p95 (CARD-1404 / CARD-1720). The first green checkpoint run is the collection. Do not repeat a green row to manufacture a p95. If a row's charged execution exceeds 30 minutes, stop and hand the run id, SHA, host, and phase times to a Debug task. Do not widen a timeout or drop a ladder size to absorb that.

CP-1 is one cold `tests/Antiphon.Agents.Pty.Tests` build plus about 25 fake launches (8 single-write sizes times 2, 3 paced sizes times 2, plus marker, multiline, Grok, and latency). No web host and no database. Provisional wall 12 minutes, above a few seconds per launch, because a cold build is included and is not a measured p95. CP-2 is a cold `tests/Antiphon.Tests` build plus nine unit methods that construct records or read a file. Provisional 4 minutes. CP-3 reuses that build for three existing unit methods. Provisional 3 minutes. CP-4 reuses it for 12 isolated-schema arguments of `C1149_C1150_Statement_budgets`. `TestDbFixture` is the assembly's shared store. Provisional 12 minutes. The methods clean up their own rows.

Importer row deadlines are `max(15, 3 * EstimatedMinutes)`: 36, 15, 15, and 36 minutes. That 3x floor is the CARD-1720 importer exception, not an R5 measurement. Each estimate is at most 12, so none trips the 45-minute advisory. Admission is the build-slot gate, separate from charged execution, default 45 minutes. Derived total allowance is `max(30, 2*31+10)` = 72 minutes. No `totalTimeoutMinutes` override.

Phase times come from the checkpoint report. Per-launch time stays unknown until CP-1 splits it. Evidence stays under `.antiphon/checkpoints/<run>/`. The receipt markdown is source, not evidence.

Category is the shared pty runner plus the server delivery settings that read it. No messaging batch. CP-1 must not run in the same process as `Antiphon.Tests`. The checkpoint rows are ordered and CP-1 is `Serial`.

## Risks and operator steps

No deploy, restart, or desktop run. The live-agent envelope on a named Claude, Codex, or Grok build is operator-gated. It is not required to commit the fake-agent ceilings. If that run is later commissioned, it can only confirm this receipt or lower it. It must not delete the tripwire.

A Linux server process starts using the new arm for its own in-proc ptys as soon as this lands and that process restarts. This plan does not restart it. Phone-home sessions pick up `UnixPty` only when that runner's cached capabilities say so. A missing cache stays inbox.

Windows inbox and modern ceilings are covered by CP-3. A Linux skip of the Windows redistributable methods is the existing `PtyDeliveryCeilingsTests` behaviour and those methods are not in CP-3.

Nothing in this card runs on the desktop. The Windows-native public paste instrument stays owed to its existing Windows tests. This card does not add a Windows execution of `UnixPtyDeliveryEnvelopeTests`.

## OS witness obligations

Code writes version-1 JSON under the ignored evidence root and runs:

`pwsh -NoProfile -File scripts/check-os-witnesses.ps1 -BaseRef 188ac942166884f060fdcc7cbf3b15f670a27b43 -HeadRef <full S3 sha> -EvidencePath <ignored-json> -Plan docs/superpowers/plans/2026-11-09-card-1224-linux-delivery-envelope.md`

Inventory every changed non-document path. The new production selection reads a capabilities string. It does not call `OperatingSystem`. Pre-existing OS candidates in `AgentTaskDispatcher.cs`, `AgentTaskReplyService.cs`, `AgentSessionService.cs`, `GrokRulesRefreshService.cs`, and `SessionMessageQueueService.cs` are `non-behavior`: the hunks do not change those guards. Do not delete a candidate to make the checker exit 0.

Inspected paths include `server/Application/Settings/DelegationSettings.cs` (`portable`), `server/Application/Settings/DeliveryBackend.cs` (`portable`), `server/Application/Settings/RunnerBoundDelivery.cs` (`portable`), `server/Application/Services/SessionDeliveryProfile.cs` (`portable`, cache consumer), the service files above (`portable` helpers whose new calls pass a string), and the two test files (`test-only`). Docs are documentation.

Witnesses:

- Linux ladder. `productionType` `PtyAgentRunner`, `factory` `StartAsync`, `testCase` `Control_body_arrives_whole`, `targetOS` Linux, `executionMode` native, `checkpointId` CP-1. `setupAssertion` is `OperatingSystem.IsLinux().ShouldBeTrue()`. `nativeObligation` recorded only with the CP-1 receipt.
- Unix arm. `productionType` `DelegationSettings`, `factory` `CeilingsFor`, `testCase` `Unix_arm_names_the_receipt`, `executionMode` portable-forced, `targetOS` Linux, `nativeObligation` `not-required`. `input.kind` is `other`. `setupAssertion` is the call `CeilingsFor(PtyBackend.UnixPty, "probe")`. The input is the enum, not an OS read.
- Runner report. `productionType` `RunnerBoundDelivery`, `factory` `ForRunner`, `testCase` `Runner_unix_report_uses_the_unix_arm`, `executionMode` portable-forced, `targetOS` Linux, `nativeObligation` `not-required`. `setupAssertion` is `ForRunner(settings, "UnixPty")`.

`restartImpact.required` is false. No restart-script path changes. `Program.cs` is unchanged. `additionalPaths` includes `server/Application/Settings/DelegationSettings.cs` because a running server keeps the old numbers until it restarts. This card restarts nothing.

Also run `scripts/check-evidence-diff.ps1` over `188ac942166884f060fdcc7cbf3b15f670a27b43..HEAD` before the Code report. Both scripts are unlisted because their SHAs are the S3 tip.

## Verification design

V-1: `UnixPtyDeliveryEnvelopeTests.Control_body_arrives_whole` asserts a 64-byte body is whole on Linux `UnixPty`.

V-2: `Single_write_ladder_matches_the_receipt` asserts both reps of every ladder size match the committed receipt.

V-3: `Paced_ladder_matches_the_receipt` asserts the three paced sizes match the receipt.

V-4: `Bracketed_paste_markers_match_the_receipt` asserts the marker and submit result match `markersSurvived`.

V-5: `Multiline_lf_body_matches_the_receipt` asserts the four-line CR body matches the receipt's submit count.

V-6: `Grok_newline_join_matches_the_receipt` asserts the Grok submit matches `grokNewlinesKept`.

V-7: `Delivery_latency_is_recorded` asserts the two latency integers in the receipt are present. It does not assert a maximum.

V-8: `UnixDeliveryCeilingTests.Unix_arm_names_the_receipt` asserts the Unix reason contains `card-1224-unixpty`.

V-9: `Unix_numbers_match_the_receipt` asserts the D-3 triple and backend.

V-10: `Claude_brief_follows_the_recorded_inline_ceiling` asserts spill above the recorded brief ceiling and inline below it.

V-11: `Runner_unix_report_uses_the_unix_arm` asserts a `UnixPty` report selects that arm.

V-12: `Docker_stack_records_the_unix_receipt` asserts the docker-stack row quotes the receipt.

V-13: `Session_runtime_records_the_unix_contract` asserts the contract sentence and the three numbers.

R-1: `Runner_without_a_report_stays_inbox` asserts null and `ModernConPty` reports stay at 900 / 1,024.

R-2: `Non_claude_brief_inline_ceiling_stays_zero` asserts Grok and Codex briefs stay at 0.

R-3: `Specialist_full_inline_stays_on_modern` asserts the ModernConPty check remains.

R-4: `PtyDeliveryCeilingsTests.The_inbox_backend_keeps_exactly_the_ceilings_that_shipped` asserts inbox 900 / 3,000 / 1,024.

R-5: `PtyDeliveryCeilingsTests.No_modern_ceiling_exceeds_the_measured_envelope` asserts modern 43,200 / 86,400.

R-6: `PtyDeliveryCeilingsTests.A_runner_bound_brief_is_fitted_to_the_inbox_ceiling_not_the_desktop_pty` asserts a missing runner report still spills the 120-line brief.

R-7: `DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets` asserts the 12 existing statement counts.

| PC | Mutation | Filter | Detects |
|---|---|---|---|
| PC-1 | drop the Unix arm so the reason is only the caller text | `/*/*/UnixDeliveryCeilingTests/Unix_arm_names_the_receipt` | `UnixDeliveryCeilingTests.Unix_arm_names_the_receipt` |
| PC-2 | copy modern 86400 into the Unix arm instead of the receipt | `/*/*/UnixDeliveryCeilingTests/Unix_numbers_match_the_receipt` | `UnixDeliveryCeilingTests.Unix_numbers_match_the_receipt` |
| PC-3 | ignore `markersSurvived` and raise anyway | `/*/*/UnixPtyDeliveryEnvelopeTests/Bracketed_paste_markers_match_the_receipt` | `UnixPtyDeliveryEnvelopeTests.Bracketed_paste_markers_match_the_receipt` |
| PC-4 | treat a lost paced trial as whole | `/*/*/UnixPtyDeliveryEnvelopeTests/Paced_ladder_matches_the_receipt` | `UnixPtyDeliveryEnvelopeTests.Paced_ladder_matches_the_receipt` |
| PC-5 | map a null report to UnixPty | `/*/*/UnixDeliveryCeilingTests/Runner_without_a_report_stays_inbox` | `UnixDeliveryCeilingTests.Runner_without_a_report_stays_inbox` |
| PC-6 | give Grok the Claude inline ceiling | `/*/*/UnixDeliveryCeilingTests/Non_claude_brief_inline_ceiling_stays_zero` | `UnixDeliveryCeilingTests.Non_claude_brief_inline_ceiling_stays_zero` |
| PC-7 | remove the contract sentence | `/*/*/UnixDeliveryCeilingTests/Session_runtime_records_the_unix_contract` | `UnixDeliveryCeilingTests.Session_runtime_records_the_unix_contract` |
| PC-8 | change inbox 900 | `/*/*/PtyDeliveryCeilingsTests/The_inbox_backend_keeps_exactly_the_ceilings_that_shipped` | `PtyDeliveryCeilingsTests.The_inbox_backend_keeps_exactly_the_ceilings_that_shipped` |
| PC-9 | change modern 43200 | `/*/*/PtyDeliveryCeilingsTests/No_modern_ceiling_exceeds_the_measured_envelope` | `PtyDeliveryCeilingsTests.No_modern_ceiling_exceeds_the_measured_envelope` |
| PC-10 | add a SQL round-trip to the brief fit | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets` | `DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets` |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Agents.Pty.Tests -> bin-c1224-pty/ | envelope | `/*/*/UnixPtyDeliveryEnvelopeTests/*` | V-1..V-7 | all listed, 0 failed; Linux executes all seven; off Linux each method skips | 7 | 12 | true |
| CP-2 | S3 | tests/Antiphon.Tests -> bin-c1224-unit/ | ceilings | `/*/*/UnixDeliveryCeilingTests/*` | V-8..V-13, R-1..R-3 | all listed, 0 failed | 9 | 4 | false |
| CP-3 | S3 | CP-2 | windows-ceilings | `/*/*/PtyDeliveryCeilingsTests/(The_inbox_backend_keeps_exactly_the_ceilings_that_shipped*)\|(No_modern_ceiling_exceeds_the_measured_envelope*)\|(A_runner_bound_brief_is_fitted_to_the_inbox_ceiling_not_the_desktop_pty*)` | R-4, R-5, R-6 | all listed, 0 failed | 3 | 3 | false |
| CP-4 | S3 | CP-2 | budgets | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets` | R-7 | all listed, 0 failed | 12 | 12 | false |
