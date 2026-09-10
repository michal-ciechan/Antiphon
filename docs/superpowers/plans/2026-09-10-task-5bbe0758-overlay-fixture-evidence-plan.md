# Overlay fixture prompt evidence correction

Date: 2026-09-10. Plan task: `5bbe0758`. Complexity: easy; TestDesign is folded in.
No owning card number was supplied. This is a test-fixture correction, independent of
CARD-0467/0466/0470/0471.

Give the existing overlay integration test the prompt evidence its fake already knows
how to emit, and require the queue's transcript-confirmed receipt plus exactly one
complete recorded body. Implementation is one test-file slice. No production change
or new test class is needed.

## Ground truth

Inspected checkout: `da781133df64fbfa50fb5d12ba2f9ae7ea7e2859`.
Established debug evidence is in
`C:\Antiphon\worktrees\card-task-74bb5bc1\.antiphon\task-74bb5bc1.md`;
its logs, TRX and live diagnostic dump remain in that worktree's
`.antiphon\hang-74bb5bc1` directory. Those are prior measurements, not Plan runs.

| Brief assumption | Current source or debug evidence | Design consequence |
| --- | --- | --- |
| The overlay failure predates the suspected locking changes. | Debug reproduced the same method's ConflictException on target `373e44a8` and baseline `da781133`: each PTY class run had 7 passes and 1 failure; the method took about 53 seconds. Both processes exited normally. | Accept the diagnosis; no repeat investigation or CARD-0467 locking fix. |
| FakeClaude is used as Grok. | `SessionMessageQueuePtyIntegrationTests.A_body_typed_while_an_overlay_is_up_recovers_via_Esc_and_submits` sets Grok in both launch spec and DB session, but supplies only `ANTIPHON_FAKE_OVERLAY_ON_COMMAND=/usage`. | Keep Grok because its provider contract supports overlay dismissal. Add the missing fake transcript path. |
| Screen output should confirm the body. | FakeClaude leaves the body in typed/submission/response output. Grok's `SubmitEvidence.IsEmptiedComposer` requires its head to disappear. The queue's 30-second confirmation window and 20-second Now grace cannot obtain a prompt from this setup. | Do not change screen predicates or widen deadlines; provide actual emitted prompt evidence. |
| A new transcript fixture might be needed. | The same test class already has `PumpTranscriptAsync`, `UserPromptsIn`, and `CleanupAsync`, used by the swallowed-Enter tests. The pump normalizes complete FakeClaude JSONL lines into session-scoped DB rows. | Reuse these helpers without changing their implementations or other callers. |
| `/usage` also needs a fake prompt record. | FakeClaude's armed overlay branch returns before `SubmitTurn`; it emits overlay markers, not a UserPrompt. An ordinary accepted body reaches `SubmitTurn`, which appends its UserPrompt when the transcript path is supplied. | Keep the transcript initially empty. Do not seed a confirming prompt or fabricate evidence for `/usage`. |
| Enqueue success can be asserted on a Sent row. | Mode.Now returns `SessionQueueDto.LastDelivery` and has no queued-message row. Its confirmation loop accepts the first matching prompt even when the baseline has no transcript rows. | Assert the returned receipt and the emitted/ingested prompt; no Sent-row assertion or synthetic TurnEnd seed. |
| The runner will read the fake's JSONL automatically. | `DirectSessionRunnerClient` defaults `grokTranscript` to true, selecting the native Grok format, while FakeClaude emits Claude-shaped JSONL. | Set `grokTranscript: false` for this client and use the explicit test pump as the sole ingestion source. |

## Decisions

- **D-1: Change only the existing overlay method and its explanatory comment.** Reuse
  the current helpers; retain the real queue/runtime/runner/ConPTY transport, Grok kind,
  inbox backend, Now mode, existing verification settings and `TimeProvider.System`.
  Changing the kind to Claude would change the provider path this test exercises.
- **D-2: Model accepted input through the fake's emitted JSONL.** Add a unique temporary
  transcript path to the launch environment, disable native Grok tailing for this fake,
  and start the existing pump after session seeding and before either enqueue. Keep the
  baseline empty; the first ordinary prompt exercises existing unobservable-baseline
  transcript confirmation. Seeding the expected UserPrompt, deriving it from screen
  markers, disabling transcript confirmation, clearing screen echoes, or adding a fake
  Grok mode would respectively invent evidence, weaken the verdict, or expand scope.
- **D-3: Assert receipt, complete prompt, and multiplicity separately.** Require a
  non-null `LastDelivery`, `Verdict == "Delivered"`,
  `ConfirmedBy == DeliveryConfirmedBy.Transcript`, and `Degraded == false` for the body.
  Require exactly one normalized raw-file UserPrompt equal to `hello after overlay`,
  and exactly one matching session-scoped DB UserPrompt with that complete text.
  Retain overlay-open and submitted screen checks and add overlay-closed evidence as
  scenario diagnostics. A screen marker alone cannot satisfy the delivery verdict.
- **D-4: Own the pump through teardown.** Declare its task outside the try, initially
  `Task.CompletedTask`; assign it when started. Cancel and await it in the outer
  finally, then run `CleanupAsync` in a nested finally so a pump fault still releases
  the fake child and removes session-owned rows/files. Do not swallow pump faults.
  This prevents writes racing session deletion on assertion failures and controls.
- **D-5: Preserve the diagnosed scope.** No queue/lock/timeout changes, FakeClaude source
  changes, shared helper refactor, stack restart or full-suite run. The historical
  2.5-hour stall remains unproven by this reproduction. If it recurs in separate work,
  retain Detailed progress, logs, TRX and a watchdog dump; use `dumpasync` to identify
  the unfinished operation before suggesting a production fix.

No user decision is needed for these defaults.

## Implementation slice

**S-1: Truthful overlay evidence and assertions.** Modify only
`tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs`.

1. Keep the test name. Introduce `const string body = "hello after overlay"`, a unique
   temp JSONL path, a pump CTS and the task lifetime described in D-4. Pass
   `grokTranscript: false` to this test's `DirectSessionRunnerClient` constructor.
2. Add `ANTIPHON_FAKE_TRANSCRIPT_PATH` alongside the existing overlay environment
   entry. Preserve Grok in both launch and DB record. Do not use
   `SeedIdleClaudeSessionAsync`: it changes kind and seeds an observable baseline.
3. Start `PumpTranscriptAsync(transcriptPath, sessionId, pump.Token)` after the session
   exists. Enqueue `/usage` exactly as before and assert `OVERLAY:open` before sending
   the ordinary body. At that point assert that raw-file UserPrompts and this session's
   DB UserPrompts are empty, proving the fixture has supplied no advance receipt.
4. Capture the body's `EnqueueAsync` result inside `Should.NotThrowAsync` (a block
   lambda assigning a nullable `SessionQueueDto` local is sufficient). Use the assertion
   message `overlay body delivery must be transcript-confirmed`. Then make the D-3
   receipt assertions. This converts the known ConflictException into a specific
   acceptance assertion failure in the negative controls.
5. Retain the submitted-marker wait and require `OVERLAY:closed` in the snapshot.
   Count the full normalized raw-file UserPrompt list with `UserPromptsIn`; require
   count 1, then exact body equality. Query DB rows by this session ID and UserPrompt
   kind, require exactly one and exact text. The raw file proves what the fake accepted;
   the receipt and DB row prove the queue could observe it. A duplicate retained echo
   does not count as a second prompt.
6. Use D-4's joined teardown and existing `CleanupAsync`. Leave other test bodies and
   helper implementations unchanged. Keep logs on failure and report their locations.
7. Run the verification below, restore every temporary control, commit the test change
   and report V/R/PC outcomes. Landing and deployment remain the caller's operations.

## Verification design

### Inspection

| Test or fixture inspected | Boundaries covered or excluded |
| --- | --- |
| Entire `SessionMessageQueuePtyIntegrationTests.cs`, including all seven method bodies (eight cases with the two backend arguments), provider setup, launch/seeding helpers, pump, raw prompt parser, cleanup and raw-output wait. | Overlay open -> ordinary body -> Esc -> accepted input; empty baseline -> first prompt; receipt source; complete text; zero/one/multiple prompt records: V-1, R-1/R-2 and PC-1..3. Existing single-line, batched/multiline, argv and swallowed-Enter cases: V-2/R-3. |
| `DirectSessionRunnerClient` constructor, launch mapping, transcript/snapshot/input methods and disposal; `TestDbFixture` initialization and context-options setup. | In-process runner, declared inbox backend, explicit native-tailer opt-out, shared test Postgres with unique session ID and local child ownership: S-1 and V-2. No helper changes. |
| FakeClaude overlay dispatch and `SubmitTurn`; queue Now receipt path, confirmation loop and `TryDismissOverlayAsync`; `SubmitEvidence`. | `/usage` emits no UserPrompt; only accepted ordinary input writes JSONL; empty-baseline matching exists; screen fallback cannot satisfy the strengthened receipt assertion. These sources are inspected dependencies, not implementation targets. |

### Proves it works now

| ID | Layer and exact case | Expected evidence |
| --- | --- | --- |
| V-1 | Integration: `SessionMessageQueuePtyIntegrationTests.A_body_typed_while_an_overlay_is_up_recovers_via_Esc_and_submits`. | Overlay opens with no prompt evidence, then closes; body enqueue passes the no-exception assertion; nondegraded transcript receipt; one complete raw UserPrompt and one corresponding DB UserPrompt. Fresh result: 1 executed, 1 passed, 0 failed/skipped. |
| V-2 | Integration: the complete `SessionMessageQueuePtyIntegrationTests` class after all mutations are restored. | All 8 current cases execute and pass, including both launch-argv backends and both swallowed-Enter outcomes. No skipped overlay test, detached owned child or pump left running. Report any changed discovery count explicitly. |

### Guards the regression

- **R-1:** Removing the environment path/pump or accepting only a screen verdict fails
  V-1 at the enqueue/receipt assertions. Prompt sources start empty, so advance test
  seeding cannot accidentally be the success evidence.
- **R-2:** Losing the body or accepting it twice fails V-1's exact raw-file count/text
  assertions; ingestion also has to produce exactly one complete session-owned DB
  UserPrompt. Keeping the overlay open prevents the test from passing for an ordinary
  no-overlay send.
- **R-3:** Existing helper consumers remain correct: V-2's swallowed-once case records
  one prompt and Sent, while the every-Enter-swallowed case records zero and Pending.
  V-2 also retains the existing multiline/argv transport coverage without widening it.

### Guard inventory

These are the acceptance guards of the corrected fixture; no production guard is
being added or modified. Existing unrelated transport and permission/working-state
guards are unchanged and outside this slice.

| ID | Plan reference and independently falsifiable assertion | Control |
| --- | --- | --- |
| G-1 | D-2/D-3: an ordinary-body success needs observed prompt evidence and a transcript receipt. | PC-1 |
| G-2 | D-1/D-3: the armed overlay must actually be recovered before delivery succeeds. | PC-2 |
| G-3 | D-3: the complete body must be recorded exactly once, independently of a successful receipt. | PC-3 |

### Positive controls

All three use only the exact V-1 method filter. Mutate separately because they share
one method. Each must produce an assertion failure, then pass after restoration.

- **PC-1 (G-1):** Remove only this method's `ANTIPHON_FAKE_TRANSCRIPT_PATH` environment
  entry; leave its pump, native-tailer opt-out and all assertions in place. The fake
  still submits on screen but cannot emit a prompt. Expect the
  `overlay body delivery must be transcript-confirmed` no-exception assertion to fail
  with the delivery-verification ConflictException after the existing confirmation
  and grace windows. This reproduces the missing-evidence defect without production
  edits. Restore and rerun green.
- **PC-2 (G-2):** Set only this method's `OverlayRecoveryEnabled` to false. `/usage`
  still opens the overlay; the body is discarded. Expect the same no-exception
  assertion to fail with NoComposerEvidence, before any submitting Enter can be
  accepted. Restore and rerun green.
- **PC-3 (G-3):** Immediately before the raw prompt-count assertion, append one extra
  copy of the actual JSONL line that normalized to the ordinary UserPrompt (select
  that line through `TranscriptNormalizer`, then append it plus LF). This is a
  temporary compiling test mutation, not retained fixture behavior. Expect the raw
  UserPrompt count assertion to fail with 2 versus 1 even though the receipt already
  succeeded. Restore and rerun green. Do not weaken the parser to deduplicate records.

Code must record the mutation and exact red assertion for each control. A compile
failure, setup failure, timeout of the test runner, or zero-test result is not a PC
pass. Refresh restored source modification times or force recompilation so restored
green cannot run a mutated DLL.

### Commands and evidence

Run from the implementation worktree, on Windows with the test Postgres available.
Use a unique relative alternate output directory ending in `/`, and execute all
commands sequentially. Retain native exit status and fresh TRX for each invocation.

```powershell
$overlayRunId = [Guid]::NewGuid().ToString('N')
$overlayOutput = "bin-overlay-$overlayRunId/"
$overlayResults = Join-Path (Get-Location) ".antiphon\overlay-$overlayRunId"
New-Item -ItemType Directory -Path $overlayResults | Out-Null
$overlayMethod = '/*/*/SessionMessageQueuePtyIntegrationTests/A_body_typed_while_an_overlay_is_up_recovers_via_Esc_and_submits'

# V-1. Repeat this invocation for each PC red/green with a distinct filename.
dotnet run --project tests/Antiphon.Tests "--property:OutputPath=$overlayOutput" -- --treenode-filter $overlayMethod --output Detailed --report-trx --report-trx-filename v1.trx --results-directory $overlayResults
$overlayExit = $LASTEXITCODE
# Record overlayExit and inspect the named method and counters in the fresh TRX.

# V-2 / R-3, fixed source only, after all PC cycles.
dotnet run --project tests/Antiphon.Tests "--property:OutputPath=$overlayOutput" -- --treenode-filter '/*/*/SessionMessageQueuePtyIntegrationTests/*' --output Detailed --report-trx --report-trx-filename v2.trx --results-directory $overlayResults
$overlayExit = $LASTEXITCODE
```

Use filenames `pc1-red.trx`, `pc1-green.trx`, through `pc3-green.trx` for the six
method-scoped PC runs. Capture full console output as well as TRX; the native exit
code and actual executed method names determine coverage. Never run
`Antiphon.Agents.Pty.Tests` concurrently. No daemon outputs or production runner are
needed. Existing debug red evidence is sufficient for the original baseline; it does
not replace the three planned controls.

### Out of scope

- Real-provider/headed canaries, native Grok transcript ingestion, new provider modes,
  transcript binding/discovery and fake-format changes: this fixture uses an explicit
  file pump, whose existing normalizer handles the fake's emitted shape.
- Working-session permission dialogs, alternate overlay fragments, stale/unrelated
  prompt matching and production retry/locking changes: no behavior is changed there.
  Do not introduce new suites to retest those independent contracts.
- A separate negative permanent PTY test: the existing swallowed-Enter pair and the
  targeted mutation runs already exercise rejection and assertion sensitivity.
- Full assembly, client/E2E, CARD-0467 recovery classes and historical stall diagnosis:
  the one-file fixture change has no dependency on those paths. Preserve diagnostic
  evidence if a separate full-run stall recurs, as D-5 states.

### Cost

Suites forced: only the V-1 exact method and V-2 class in `Antiphon.Tests`, including
three sequential method-only red/restore/green PC cycles. Estimated verification
floor **14 minutes**: initial build/setup 2; V-1 1; V-2 4; PC-1 3 (includes the known
50-second failure wait); PC-2 2; PC-3 2. Each PC allowance includes incremental builds,
test-host/Postgres setup, red execution and restored green. These are planning
estimates; only the prior debug class duration of about 4 minutes is measured.

No added full-assembly run saves approximately 25.5 minutes relative to adding the
full run documented in `docs/testing-and-build.md`. Method-scoping six PC invocations
instead of class-scoping them avoids roughly 18 minutes at a 4-minute class versus
1-minute method allowance per invocation. Do not trade away the specified controls
or the final class run to meet an estimate.

Design completion check: all planned changed test bodies and relevant fixture/helper
bodies were inspected; zero/one/multiple prompt and overlay/no-recovery boundaries
are mapped above. Guards=3, mapped=3, missing=0, duplicate PC mappings=0. All referenced
PCs are defined, use executable method filters, and have explicit red assertions.
Cost includes the complete planned execution budget. Plan stage ran no builds or
tests. Ready for Code after the caller lands this plan through the normal operation.
