# CARD-1545 S1c chain Final Review (task 95bc3d62)

Outcome: **defects found; not mergeable.** Reviewed source `c7a7b2c9e27c793ff441bbf37e89dc46a4ed82e2`
(verified code `d0f6d204f`), branch `feat/card-task-82f0a842`, chain `d7702d1cb..c7a7b2c9e` (31 commits) over plan
commits from base `cbe3e6b42`. The closed ordinary list is **red at the tip** (CP-17) and on the master-based
scratch. Original Code landing owner: task `82f0a842-2299-49f9-a518-f5335454ce7a`.

## Defects

### D-1 (High) CP-17 is red at the tip: S1c-startup broke S1c-executable's V-1 caller audit
- **Where:** `tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs:28-38` (Control method now calls
  `startup.ControlAsync(experiment, () => RunStartupControlAsync(executable, profile, experiment))`) versus
  `tests/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests.cs:53-58` (PC-4 case requires a direct
  `LaunchUntilReadyAsync` consumption in that method) and `tests/Shared/LiveClaude/LiveClaudeExecutableAudit.cs:32-41`
  (`Call` rejects calls inside lambdas).
- **Failure:** `ClSessionUsesAttestedExecutable` fails `C1545 audit/Control_auto_connects_a_bridge_session_before_the_first_user
  required consumed launch delegation`. Checkpoint tool at the tip: `CP-17 executed=3 passed=2 failed=1` (run
  20261031-103408-f386); same on the master-based scratch (run 20261031-105933-2951) and in my plain DLL run.
- **Why:** introduced by 796579546 (S1c-startup); neither S1c-startup (CP-3 only) nor S1c-delivery (CP-19 only) reran
  CP-17/CP-23, so the earlier slice's closed row regressed unseen. The Final ordinary list cannot be green.
- **Fix:** make the executable audit follow the control's delegated launch (`RunStartupControlAsync` ->
  `LaunchUntilReadyAsync(executable, ...)` with the admitted `executable`), keeping PC-4's removed/dead/swallowed
  variants red; then rerun **all six** companion rows (CP-17, CP-23, CP-18, CP-24(+PARITY), CP-3, CP-19) at the new tip.

### D-2 (High) S1c refusals do not use master's S1b exit-20 semantic: setup/outage become RED
- **Where:** `tests/Shared/LiveClaude/LiveClaudeLaunchContext.cs:30-35` (`LiveClaudeRefusal : InvalidOperationException`);
  thrown inside test bodies e.g. `ClaudeRemoteControlAtStartupCanaryTests.cs:105` (`premise-absent`),
  `LiveClaudeStartupEvidence.cs:38-46`, `LiveClaudeDeliveryEvidence.cs:39,46` (`provider-error`); `ReadJsonl` (`:117-120`)
  collapses the API error to a bool. Recipient: master `scripts/lib/live-lane-outcome.ps1:158-161`.
- **Failure:** only `missing-context` is converted to a skip (`ClSession.RequireCore`). A host whose Claude does not
  auto-connect at startup makes the Control test **Failed** with `20 premise-absent`; S1b classifies any `Failed` row
  without transcript/probe outage proof as `1 assertion-failed` **before** it looks at `Setup`, so "auto-connect not the host
  default" is RED, not INCONCLUSIVE. A mid-run Overloaded after the receipt fails with `20 provider-error`, but no typed
  error class or request correlation is retained, so `Test-LiveLaneTranscriptOutage` can never prove it: a provider
  outage becomes a code failure. Same for `bridge-unestablished`, `trust-uncleared`, `tui-unready`, `settings-unobserved`.
- **Why:** brief item (1); companion PC-119 promises "typed error retained for S1b". There is no producer-to-recipient
  test from an S1c TRX outcome to an S1b classification.
- **Fix:** surface setup/premise refusals as typed skips (Setup `StartupDefaultAbsent`/`BridgeNotEstablished`/
  `TrustNotCleared`/`ReadyTimeout`) and retain the typed provider error (`Overloaded`/`ServiceUnavailable`/`RateLimited`) plus
  session/request correlation as a structured fact the S1b reader consumes; add one producer-to-recipient witness
  (S1c refusal -> S1b `Get-LiveLaneOutcome` = 20 with the right reason). If the orchestrator assigns the mapping to the
  native-acceptance slice instead, record that and keep CP-5/6/7 un-creditable until it lands.

### D-3 (Medium) Profile admission and the startup canary require mutually exclusive hook commands
- **Where:** `tests/Shared/LiveClaude/LiveClaudeProfile.cs:136-146` (hook token 0 must be a fully qualified, declared
  interpreter) versus `ClaudeRemoteControlAtStartupCanaryTests.cs:165-181` (requires exactly
  `node "<OwnedRoot>/settings-source-witness.mjs" "<marker>" <source> "<model>"`).
- **Failure:** a native profile configured as the canary requires refuses at `new LiveClaudeProfile` with
  `20 unowned-instructions` (`node` is not fully qualified/declared); a profile the profile slice admits (absolute owned
  interpreter, as in `LiveClaudeProfileCases.cs:150-163`) refuses in `RequireHookConfiguration` with `20 settings-unobserved`.
  Native CP-6 (Control/Off/Slash) can never reach a verdict. The canary's bare `node` is also a PATH-resolved, unattested
  interpreter inside the hook.
- **Why:** seam integrity (brief item 2); each slice's portable witness exercises only its own shape.
- **Fix:** one shared hook-command builder used by both, with the absolute declared interpreter; a witness that builds a
  profile from the canary's expected settings and passes both admission and `RequireHookConfiguration`.

### D-4 (Medium) C680's config-dir seam stops at the child: the in-process tailer still reads the ambient profile
- **Where:** `tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs:49-60,101-109` puts `CLAUDE_CONFIG_DIR` only in
  the child launch env; `DirectSessionRunnerClient(claudeTranscript: true)` runs the production tailer in-process, whose root
  is the **test process** env or `~/.claude` (`src/Antiphon.SessionRunner/TranscriptTailer.cs:1218-1225`). The helper's own
  contract says isolated `CLAUDE_CONFIG_DIR` "must also be set on THIS process" (`tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs:87-92`).
- **Failure:** natively, C680 enumerates the operator's `~/.claude/projects/**/*.jsonl` and never binds the dedicated
  transcript, so its UserPrompt/ToolCall precondition times out: C680 cannot pass, and the ambient profile is scanned.
- **Fix:** scope the in-process runtime's `CLAUDE_CONFIG_DIR` to `profile.Paths.ConfigDir` under the existing environment
  lock (or pass an explicit projects root), plus a portable witness that the tailer root equals `profile.Paths.Projects`.

### D-5 (Low) Three guards have no test that goes red when deleted (own mutants, plain build + DLL run)
- `LiveClaudeProfile.cs:79` `settings-drift` in `Complete()`: deleted -> CP-18 class 6/6 green; no test references
  `settings-drift` anywhere. A settings change between launch and completion is accepted.
- `LiveClaudeProfile.cs:142` hook script-name check: deleted -> 6/6 green.
- `LiveClaudeStartupEvidence.cs:66-67` `RequireOff` elapsed-window guard: deleted -> CP-3 class 5/5 green (V-20 only
  feeds `ObserveOffAsync` output).
- **Fix:** add a direct negative for each (or delete the guard and say why it is unreachable).

## Mutants I ran (17; one gated lease, incremental build + exact class run each, restored by `git checkout`)

| Id | Site | Result |
|---|---|---|
| D1 | delivery: drop `Sequence > Floor` | RED `C1545 V-23/prompt-floor-submit` |
| D2 | delivery: drop first intervening-prompt check | survived (equivalent: second check subsumes it; companion PC-117 mutates both) |
| D3 | delivery: drop after-submit bridge check | RED `V-25/after-submit-missing` |
| D4 | delivery: drop partial-transcript check | RED `V-23/partial-record` |
| D5 | delivery: drop `response.Error` | survived (equivalent: `end` is the same uuid row; companion PC-119 mutates both) |
| S1 | startup: drop `ModelPrompts != 0` | RED `V-18/zero-prompts` |
| S2 | startup: `RequireOff` elapsed guard | **survived** (D-5) |
| S3 | startup: retained-setting equality | RED `V-22/retained-setting-user` |
| S4 | startup: sidecar session check | RED `V-18/reader-foreign` |
| S5 | caller: delete `RequireOff` in Off test | RED `startup-audit/off-verdict` |
| E1 | executable: chain comparison | RED `V-3/unbound-chain` |
| E2 | executable: attempt binding | RED `V-2/context-attempt` |
| E3 | executable: observed PID | RED `V-3/image-PID` |
| P1 | trust: `existed=false` | RED `V-7/restore-existing` |
| P2 | profile: `settings-drift` | **survived** (D-5) |
| P3 | profile: hook script-name check | **survived** (D-5) |
| P4 | profile: effective-model check | RED `V-12/effective-model` |

12 red, 2 equivalent, 3 real survivors. Restored source: delivery 4/4, startup 5/5, isolation 6/6, executable 2/3
(the pre-existing D-1 failure only).

## Sweep (checkpoint tool, fresh outputs, UseAppHost=false, TEMP=/tmp)

Tip `c7a7b2c9e` (source clean, buildSource verified):
- Isolation wrapper: CP-18 6/6 (run 20261031-103323-72c2), CP-24 4/4 + CP-24-PARITY 1/1 (run 20261031-103644-874e);
  both `CHECKPOINT SOURCE VALID`.
- Run 20261031-103408-f386 (RED, 32m18s wall, 15.5m execution): CP-17 3/2/**1**; CP-3 5/5; CP-19 4/4; CP-23 3/3;
  RV-PTYNB (ProcessSpawnLimit, TestClassificationGuard, NativeLinuxPolicyWitness, ClaudeProjectTrust in Pty) 13/12/1;
  RV-PTYALL (whole Pty.Tests assembly) executed 508, passed 505, failed 3, skipped 231 (headed/Windows);
  RV-ATNB (ProcessSpawnLimit, ScriptHarnessProcessContract, SlowTestTripwire, TestLaneCategoryGuard,
  TestClassificationPolicy, RcRosterRepair, TestClassificationGuard, NativeLinuxPolicyWitness in Antiphon.Tests) 99/98/1;
  RV-ATWD C680 skipped (`20 missing-context`, expected); RV-SRPOL 4/3/1; RV-PHPOL 4/3/1.
- Non-chain reds: `NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute` (`legacy-digest-mismatch`, digest
  8397984d...) in **all four** Shared-policy suites, including SessionRunner and PtyHost, which the chain does not touch.
  The witness and `tests/test-execution-policy.json` are byte-identical to base, and it is also red on the master-based
  scratch: inherited, not this chain. `ClaudeEffortPromptTests.Retries_respect_settle_and_attempt_limits` (timing, in
  RV-PTYALL only) passed alone: load flake in untouched code.
- No new class needed a policy row: TestClassificationGuardTests passed in all four suites.
- Census harness cases C1404, C1577, C1584, C1410, G2865: each `PASS` (gated).

Master-based scratch `01faf0452` (31 chain commits cherry-picked onto origin/master 2051d3271 cleanly, plus the companion):
CP-18 6/6, CP-24 4/4, CP-24-PARITY 1/1 (runs 20261031-104023-2808 / -104947-bc31, both `CHECKPOINT SOURCE VALID`);
run 20261031-105933-2951: CP-17 3/2/**1** (D-1), CP-3 5/5, CP-19 4/4, CP-23 3/3, Pty and Antiphon.Tests policy rows 4/3/1
each (same inherited `legacy-digest-mismatch` 8397984d...). The fixtures are master-compatible; only D-1 is red.

Evidence copies: `.antiphon/rv95/` (tip run, master runs, mutant/graph/census logs); the isolation receipts are
under `.antiphon/checkpoints/20261031-103323-72c2` and `-103644-874e`. Every scratch worktree, owned `bin-*` output and
owned `/tmp/c1545-portable-*` context was removed by literal path. Tool DLL SHA-256
680e40f7e1296fbd4568498cf29b1a92fea8ffa6292be5272fe04419560b62c6.

## Definitions (item 5)

Changed: `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj:18` (PackageReference
Microsoft.CodeAnalysis.CSharp 5.9.0, same version as tools) and `:105-107` (Compile `../Shared/LiveClaude/*.cs`);
`tests/Antiphon.Tests/Antiphon.Tests.csproj:220-222` (same Compile glob). Test-only, no `_PtyHostNativeProps`, no
new allow-list property. Real evaluation in a scratch at the tip (Release LandGraphAudit via build-slot):
`-Check` exit 1 (pre-check refused, computed digest 9D7BBFDE...); `-Update` approved both passes and changed only line 1 of
`LandVerificationBuildGraph.manifest.txt` and `.evaluation.txt`:
`7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99` -> `9D7BBFDE28CEF5693596472D737003825248CDA632285B8E17077AE0FD0CC528`,
count 68 -> 68, SDK 10.0.403 and pins unchanged; re-`-Check` exit 0. Master changed no definitions since base and still
records 7BDABBA0/68, so the merge-time `-Update` should produce exactly this. rc records E7C6EDAF.../69: an rc port needs its
own `-Update`.

## Merge-tree
- origin/master 2051d3271: only the known add/add on
  `docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md`.
- origin/rc/2026-10-27 fc064a682: whole-branch merge-tree conflicts on ~30 unrelated files (master history in the base);
  the 31 chain commits cherry-pick onto rc **cleanly**. Compile/rows on rc not run.
- `check-evidence-diff.ps1`: base..tip commits=48 entries=7 violations=0; f34e3c34f..tip commits=31 entries=4 violations=0.

## Answers to the brief's central-danger questions
- Delivery without a transcript UserPrompt: not possible. `RequireSubmit/RequireMenu` need a complete `IsCompleteIn`
  UserPrompt above the floor in the same session, the screen is readiness only, and `RequireNativeObservation()` refuses all
  native credit today.
- Bridge-survives vacuous: no. The owner bridge needs a non-empty id, and each phase must be readable and identical.
- Control skipped yet off counted: no. Off/Slash await the shared control and a faulted control is cached and rethrown.
  But see D-2 (premise-absent is RED, not 20).
- PATH `cl`: all five PTY core callers and C680 resolve via `LiveClaudeExecutable`. 22 non-core Pty canary files still use
  `ClSession.ResolveOrThrow`/PATH by design. C680's `Resolve("node")` and the canary hook's bare `node` (D-3) are remaining
  PATH lookups.
- Unattested setting source: the projection always records user/project/local/global/managed (+overlay); unproven managed
  location disables reuse.
- Harness-emitted marker: the marker comes from the hook child, but the `retained` value is the hook command's own
  argv literal echoed back (A-1).
- Provider outage: D-2.

## Advisories
- A-1: V-22's "retained setting" equality compares the hook-command literal with itself (`settings-source-witness.mjs`
  echoes argv). It proves the source file loaded, not that Claude's effective setting survived the off overlay. Native
  CP-6 still needs an effective-setting observation.
- A-2 Secrets/sinks (sanitiser not in chain): refusal messages carry reason codes only, and C514/startup screen prints were
  removed. The sanitiser must still cover: the non-core `One_Esc...` menu skip messages that print the full screen
  (`ClaudeRemoteControlMenuCanaryTests.cs:53,63,84`, which can carry the remote-control link), `ClaudeDialogCanaryDiagnostics`
  debug output, Shouldly failure text in C680, `captures.json` (stores `spec.Env` including `Context.Environment`;
  synthetic today), and any future print of `bridgeSessionId`.
- A-3: The source audits match exact statement text and find the repo through `Environment.CurrentDirectory` -> `Antiphon.sln`.
  That brittleness produced D-1. V-22 spawns PATH `node` in the Unit lane.
- A-4: `test-live-lane-isolation.ps1` never removes its `/tmp/c1545-portable-*` context directory (six from other
  tasks remain on server2).
- A-6: The verification profile's "whole Unit lane" was not rerun. The commissioned sweep (all six companion rows, the
  whole Pty.Tests assembly, named neighbour/policy classes in four suites, five census cases) did run. The companion's
  closed list explicitly excludes a broad Unit run.
- A-5: Earlier slices' timing/TUnit0015 limitations stand; nothing here re-qualifies them.

PCs stay pending for SourceLanding Mutation; native CP-4..7/CP-21, sanitisation and the formal battery remain owed.
