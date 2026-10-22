# CARD-0500: interactive Codex submit-confirm flake (CodexCommandLengthSessionTests)

**Date:** 2026-10-22. **Investigate task:** `f439753d` (Frontier, runner mirror, Linux). **Card:** CARD-0500
(`66ec2bb0`), InProgress, filed 2026-09-12 from CARD-0497 Mutation task `7dd45165`.
**Source examined:** `origin/master` `f2b0f1748376a88e6c0ef4ed1bdd4e1959b2d41d`; the four files below are
byte-identical between the branch base `294fa3d1a` and that tip (`git diff --stat` empty).

## Verdict

The flake is one opt-in Windows canary, measured once (3 of 5 runs, 2026-09-12, commit `11eadbac`)
and never run again. Its failure sits at the first-turn submit of a cold, fresh-`CODEX_HOME` Codex
TUI through `RunnerCodexAdapter.SendPromptAsync`. At the time of the measurement the adapter's ready
gate was the 1 s quiet wait that CARD-0574 proved unsafe from live frames one week later (a body typed
into `model: loading` or MCP boot strands with no rollout and no `Working`), and the canary's own
placeholder check waits for exactly the screen CARD-0574's attempt 1 showed while the brief sat
queued. CARD-0574 S2 (`f5be8d22d`, 2026-09-19, in `origin/master`) replaced that gate with the
positive ready classifier in the adapter the canary uses. Whether that closed the flake is unmeasured.

The failing arm cannot be classified from stored evidence because the canary constructs the adapter
without a logger, so every CARD-0108/CARD-0133 delivery diagnostic is discarded, and it deletes its
artefact root in `finally`. That is a confirmed fixture defect independent of the product question.

Not reproducible from this host: the test skips off Windows and without the npm Codex shim. The one
narrow repeat-run the brief asked for needs the desktop lane.

## 1. Which test flakes, and how often

| Item | Evidence |
|---|---|
| Test | `tests/Antiphon.Tests/Agents/CodexCommandLengthSessionTests.cs:30` `Incident_fixture_launches_through_node_and_delivers_the_whole_developer_block` (plan V-1/V-2). Its short control launch is `RunInteractiveAsync` line 94 → `LaunchOnceAsync` line 102. |
| Failing step | Line 185 `adapter.SendPromptAsync(nonce prompt)` returns, then line 186-191 waits 60 s for a stub `POST /v1/responses` carrying the nonce and asserts `"stub never saw the nonce — prompt did not reach Codex"`. The CARD-0497 Mutation report (DB `AgentTasks.Result`, task `7dd45165`, completed 2026-09-12T19:01Z) says every failure was on the 200-character control launch, never the 8,366-character fixture; the legacy-launcher arm "failed identically at `chatHit`". |
| Rate | 5 unmodified runs on 2026-09-12 at `11eadbac90ed48df3417df64481100009b08c1f3`: fail, fail, fail, pass, pass. Legacy `codex.cmd` arm: 1 of 3 failed the same way (the other two died later at `Win32_Process pid not found`, the overflow being tested). V-3 `Near_cap_fixture_launches_and_arrives_intact` failed 2 of 2 after its sentinel test bug was patched locally, "submit flake, then `chatHit`". |
| Since then | Zero runs. Card revision 1 (2026-09-18, triage task `a55b7ed6`, `docs/investigations/2026-09-18-card-0486-pre-existing-red-tests-triage.md:78-88`) says "not re-run". No later task, report or doc names the class (`grep -rl CodexCommandLengthSessionTests` over the repo and `.antiphon/`; `card.ps1 search` finds only CARD-0500). |
| Nightly / RC | Never executed by either, by construction: `[Explicit]` + `ANTIPHON_REAL_CLI_STUB_TESTS=1` (`RealCliStubGate.cs:18-28`), `tests/test-execution-policy.json:97` ("opt-in real-provider or interactive workflow; needs a live CLI session", owner CARD-0599), `tests/linux-test-roster.json:830-839` `disposition: exclude`. The nightly red cards and the RC 2026-10-20 Windows qualification reds (`2026-10-21-rc-2026-10-20-windows-qualification-reds.md`) contain no interactive Codex canary. |
| Comparable canary | None. `CodexBootWedgeProbeTests` records, does not assert; `CodexRealCliStubProxyCanaryTests` uses `codex exec` + the vendored exe; `CodexHerdrRealCliStubProxyCanaryTests` does not drive `SendPromptAsync` (triage doc lines 84-88, still true at this tip). |

The only detailed failure text (per-run assertions and run logs) is the desktop file
`C:\Antiphon\worktrees\card-task-7dd45165\.antiphon\task-7dd45165.md`, cited by the report as its
artifact. It is not reachable from this mirror and was not committed.

## 2. Mechanism

### 2.1 The path the canary exercises

1. `LaunchOnceAsync` builds a fresh temp root per launch: new `cwd`, new `logs`, new `codex-home`
   (lines 112-119), so every control launch is a cold first-ever Codex start with no rollout, no
   cached model list and no `version.json`. The stub (`FakeLlmApiServer`, `Codex = true`) answers
   `/v1/models` and `/v1/responses` instantly.
2. `adapter.WaitForReadyAsync` must return true (line 180), then the test's own
   `WaitForIdleComposerAsync` (lines 242-254) polls up to 30 s for the placeholder
   `Ask Codex to do anything` and **returns silently on timeout**.
3. `SendPromptAsync` (`RunnerCodexAdapter.cs:88-115`) captures the transcript baseline and calls
   `CodexSubmitConfirmation.SubmitAsync` with `RunnerTerminalSession.SendLineAsync`
   (`RunnerTerminalSession.cs:112-121`: encoded body, 20 ms, separate `\r`), Enter re-press every
   `CodexSubmitReEnterIntervalMs` = 4000 up to `CodexSubmitAttempts` = 3 extra Enters, total
   `CodexSubmitConfirmTimeoutMs` = 20000 (`AgentRegistrySettings.cs:90,96,104`; the test overrides
   none of these).
4. `CodexSubmitConfirmation.SubmitAsync` (`server/Infrastructure/Agents/CodexSubmitConfirmation.cs:74-142`)
   returns without throwing on three arms:
   - **Confirmed**: a `UserPrompt` row past the baseline whose text matches the body (line 106-110,
     `PollForConfirmationAsync` 218-220).
   - **Working** (no row ever seen): `CodexWorkingIndicator.IsVisible` on either of two looks
     (`ConcludeUnobservableSubmitAsync` 159-173) — "degraded success".
   - **Blind** (no row ever seen, no `Working`, body not visible on both looks): log and return
     (186-192). Pinned by `RunnerCodexAdapterSubmitConfirmTests.A_session_with_no_observable_transcript_degrades_to_a_blind_send_instead_of_failing` (`:169`).
   It throws only when the transcript is live and never confirms (133-141) or when the body is still
   visible on both post-Enter looks (175-184).
5. On a first turn the rollout does not exist until Codex runs a turn (class comment lines 41-45;
   CARD-0574 investigation: "Codex still creates the JSONL on first submit"). So a control launch whose
   body never submits produces **no rows at all** and always reaches step 4's unobservable arms after
   20 s and 4 Enters. A test failure at line 191 therefore means the Working or Blind arm returned (or
   a confirmed row was followed by no HTTP call, see uncertainties). The Blind arm is the only one
   consistent with "no `Working`, body gone, nothing reaches the stub".

### 2.2 Why the body would not submit: the readiness gate of 2026-09-12

- At `11eadbac` `RunnerCodexAdapter.WaitForReadyAsync` was the 1 s quiet-after-visible-output wait
  plus trust handling, then `CodexMcpBoot.WaitUntilAbsentAsync`, which returned on its first MCP-free
  snapshot (CARD-0574 plan ground truth, `docs/superpowers/plans/2026-09-19-card-0574-codex-positive-ready-gate-plan.md`, rows 1-3; the plan pins `CodexMcpBootTests.WaitUntilAbsent_returns_immediately_when_the_line_is_not_on_screen`).
- CARD-0574's investigation (`docs/investigations/2026-09-19-card-0574-codex-boot-prompt-wedge.md`,
  2026-09-19, live snapshots + sidecars + pty-host logs) shows what a body typed through that gate
  does on a cold start: attempt 1 typed at +6.1 s into `model: loading` and the brief landed under
  `Queued follow-up inputs` while the composer showed the idle placeholder `Ask Codex to do anything`;
  attempt 2 typed at +2.1 s during `Starting MCP servers (1/3)` and the brief stayed in the composer.
  Neither produced a rollout or a `Working (` line; both ended `BootWedged`. 2 of 2 cold starts
  failed; its verdict names "the CARD-0133/0299 not-ready TUI class".
- The canary's placeholder wait (step 2) accepts exactly attempt 1's frame. The CARD-0574 plan's
  ground-truth row "A composer/placeholder proves readiness → No" is the same finding.
- Every canary launch is cold (2.1 item 1). CARD-0299 measured the production stranding rate at
  3/55 (5.5 %) with warm homes (`2026-09-01-card-0299-codex-plan-unsubmitted.md:21`); the canary's
  3/5 is the cold-start shape CARD-0574 saw at 2/2. The 60 % is consistent with the cold class, not
  with the warm 5.5 % class.
- CARD-0574 S1/S2 (`14066b03631b5ba0a5819b8f91bf70bf30f0956c`, `f5be8d22d02e223bdcd0e1b810322ce3278eb156`,
  2026-09-19, both in `origin/master`) replaced the gate in both adapters, `RunnerCodexAdapter`
  included (`RunnerCodexAdapter.cs:143-192` now drives `CodexReadyWait.WaitAsync`). The classifier
  (`src/Antiphon.Agents.Pty/CodexStartupReadiness.cs:86-120`) refuses `model: loading`, requires an
  idle composer **and** a `model · effort · cwd` footer, and the tracker needs two consecutive positive
  observations (`:336-387`). CARD-0859 (`d07c7e070`, 2026-10-01) added the 0.158 footer-only ready
  layout. None of these changed `CodexSubmitConfirmation` or the canary.

### 2.3 Comparison with CARD-0108 / CARD-0055

| Card | Mechanism it fixed | Relation to this flake |
|---|---|---|
| CARD-0055 | Delivery is proven only by a transcript `UserPrompt` row; retry is Enter-only. | The canary's confirm loop is this contract (`CodexSubmitConfirmation` summary lines 33-39). It cannot help when the rollout never exists; it degrades by design. |
| CARD-0108 S1 | Production body + 20 ms + CR folded inside Codex's paste window 6/6 (2026-08-20); fix = the confirm loop + Enter re-press. | The canary already passes `-c disable_paste_burst=true` (line 135) and gets 3 re-presses. CARD-0299 showed `disable_paste_burst` "is not sufficient" and that re-pressing Enter into a not-ready TUI submits nothing. |
| CARD-0299 | Queue path (`SessionMessageQueueService`) latched emptied-composer on one poll. | Different path: the canary never uses the queue (CARD-0299 investigation item 1-2 says `CodexSubmitConfirmation` "never ran" for delegates). Not the mechanism here. |
| CARD-0574 | Quiet ready gate let a cold TUI accept typed text before it could submit. | Same symptom family, same adapter, cold starts. Shipped 7 days after the card was filed; the card's 09-18 note predates it. |

## 3. Classification and reproducibility

- **Product defect (historic):** the readiness gate the canary ran through on 2026-09-12 was unsafe
  and is fixed by CARD-0574 S2. Whether any residual cold-start input loss remains is unknown.
- **Test-timing assumption:** the canary's `WaitForIdleComposerAsync` uses the placeholder as a
  readiness proof, which CARD-0574's attempt 1 refutes; after CARD-0574 it is redundant with the
  positive gate but still harmless.
- **Fixture defect (confirmed, current):** `new RunnerCodexAdapter(client, options)` (line 157)
  passes no `ILogger` (constructor `RunnerCodexAdapter.cs:32-36`), so the Warning lines that name the
  arm taken (`CodexSubmitConfirmation.cs:89-92,115-119,162,171,186-192`) are dropped; `finally`
  (lines 225-230) deletes `tempRoot` including pty-host logs and the stub sidecar; the stub's
  `/v1/models` receipt (line 207) and the transcript are only inspected after the failing assertion.
  No run of this test can say which arm fired.
- **Reproducible at `origin/master`?** Not from this host. `RealCliStubGate.SkipIfNotEligible`
  throws `SkipTestException` off Windows (`RealCliStubGate.cs:20-21`) and the shim path is
  `%APPDATA%\npm\codex.cmd` (line 233-239). The runner mirror is Linux (`ANTIPHON_PTY_BACKEND=inbox`).
  No build or test was run here; the brief's at-most-10-repeat run is owed by the desktop lane with
  `ANTIPHON_REAL_CLI_STUB_TESTS=1` and the filter
  `/*/*/CodexCommandLengthSessionTests/Incident_fixture_launches_through_node_and_delivers_the_whole_developer_block`.

## Remaining uncertainties

1. Which arm returned in the 3 failures (Blind, Working, or Confirmed-then-no-HTTP) is not in any
   stored evidence; only the desktop `task-7dd45165.md` may hold the run logs, and it is a gitignored
   file in a worktree that may be retired.
2. Whether CARD-0574 S2 removed the flake: no run since 2026-09-12. A 0/10 at `origin/master` would
   close CARD-0500 as fixed-by-0574; any failure with the logger attached classifies the arm.
3. The Codex TUI-side cause of input loss on a cold start (queued follow-up vs. consumed keystrokes)
   is not established in the repository; CARD-0574 states "the precise internal MCP/client stall is
   not established".
4. `WaitForIdleComposerAsync` returning silently after 30 s is a second silent path; whether it ever
   timed out in the failing runs is unknowable for the same logging reason.

## Not done, noted

- Fix idea (one line): make the canary diagnosable — pass a test-output `ILogger` into
  `RunnerCodexAdapter`, keep `tempRoot` on failure, and turn `WaitForIdleComposerAsync`'s timeout
  into a failure — then re-measure on the desktop lane; no product change is proposed until that run
  says the flake survives CARD-0574.

## Handoff material requested by the brief

File: `tests/Antiphon.Tests/Agents/CodexCommandLengthSessionTests.cs` only.

Red witness shape: a `CodexSubmitConfirmation` fake transcript that never produces rows, with the
body gone from the screen and no `Working`, must surface the Blind-arm log line in the test's output
sink; today the same scenario through the canary's adapter construction records nothing. A
unit-level witness exists already for the arm itself
(`RunnerCodexAdapterSubmitConfirmTests.A_session_with_no_observable_transcript_degrades_to_a_blind_send_instead_of_failing`);
the new witness is the canary wiring, so the red is "logger sink empty after a blind submit".

Closed checkpoint list for one Code task (30-60 min, test-only):

| CP | Build | Filter | Expect |
|---|---|---|---|
| CP-1 | `tests/Antiphon.Tests` isolated | `/*/*/RunnerCodexAdapterSubmitConfirmTests/*` | all green, unchanged (9 methods) |
| CP-2 | same | `/*/*/CodexCommandLengthSessionTests/Legacy_batch_launcher_still_dies_on_the_incident_fixture` | skip on Linux/CI; green on desktop with the shim |
| CP-3 (desktop lane, `ANTIPHON_REAL_CLI_STUB_TESTS=1`) | same | `/*/*/CodexCommandLengthSessionTests/Incident_fixture_launches_through_node_and_delivers_the_whole_developer_block` x10 sequential | record pass/fail per run and, for each failure, the arm line from the logger sink and the retained `tempRoot` path |

Decision rule for the caller: 0/10 → close CARD-0500 as fixed by CARD-0574 (note the measurement on
the card); ≥1/10 → the arm line and artefacts are the input to a product Investigate on the Codex
cold-start input path.

## Evidence sources

- Board: `card.ps1 get CARD-0500 -Board Antiphon`, `card.ps1 history CARD-0500` (2 revisions:
  2026-09-18 ContentEdit, 2026-10-22 Move).
- Database (read-only, dev Postgres): `AgentTasks` rows `58e21153` (TestDesign, 2026-09-12 15:19Z),
  `7dd45165` (Mutation, 2026-09-12 17:00-19:01Z, `Result` 6,467 chars), `a55b7ed6` (triage,
  2026-09-18).
- Repository at `f2b0f1748`: files cited by line above; `git log --follow` on the canary (3 commits:
  `b67710b97`, `916f28089`, `dc5ad8b6e`, all 2026-09-12); `git log --since=2026-09-12` on the Codex
  submit/ready path (CARD-0574, 0662, 0777, 0859, 0907, 1299, 0242, 1322; none touch
  `CodexSubmitConfirmation.cs`).
- Docs: CARD-0497 plan §V-1/V-2 and the TestDesign addendum; CARD-0108 plan §1; CARD-0299
  investigation; CARD-0574 investigation and plan; CARD-0486 triage.
