# C514 arm-unconfirmed qualification debug, 2026-10-30

Task `5e08c4d4`, Linux runner worktree `/work/worktrees/task-5e08c4d4`, branch
`feat/card-task-5e08c4d4`, base `80b3a59c40cd18dea905829e428d35844cb89b8d`.

## Classification and limits

Confirmed product liveness defect at the real automatic-arm service/runtime seam:
a composer redraw after recovery's snapshot but before the runtime's dialog observation
returns `stale-observation`, with zero backend bytes for Enter. Recovery previously made
that definitive refusal terminal, closing the request as `ArmUnconfirmed` with the command
still typed. A deterministic portable regression produces exactly that result on unchanged
production code. The conditional guard's refusal is correct; giving up on output-only
movement is the liveness defect.

This is a strong match for qualification `d47848cc`, candidate `cde8def41`, group 48,
but the exact Windows failure branch remains unproven: the supplied extract does not include
the maintenance row's `MaintenanceEvidence` or the conditional-write result. No Windows
logs or private notes were read, and the live qualification was not touched. Native Windows
confirmation remains owed. Host load can widen the race window; its causal contribution is
unmeasured, not a proven deadline problem.

## Evidence and code path

- `tests/Antiphon.Tests/Application/RemoteControlRecoveryPtyIntegrationTests.cs`,
  `C514_Health_request_reaches_busy_and_idle_recipients`: first delivers one real work body,
  waits for the child's ingested UserPrompt, inserts explicit working/idle transcript facts,
  then expects the reserved arm to be observed. It retains all original assertions: busy
  writes no command, idle submits the command, child emits its arm marker, and RC creates
  no work UserPrompt.
- `server/Application/Services/RemoteControlRecoveryService.cs`,
  `ExecuteAutomaticArmUnderLockAsync`: persisted submission intent, guarded command body,
  composer evidence, guarded separate CR, bridge confirmation. `MapWriteOutcome` maps
  stale observations and unknown writes to `ArmUnconfirmed`; `FinishArmAsync` closes the
  slot. The generation's uncertainty veto prevents a later full command replay.
- `server/Application/Services/AgentSessionRuntime.cs`, `SendConditionalInputAsync`:
  the runtime obtains a separate fresh dialog observation and refuses if its output sequence
  differs from recovery's expected sequence. The runner independently fences generation,
  output sequence, runtime epoch and input revision at the backend boundary.
- `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`: stale guards run before
  `WriteBackendAsync`; input revision advances immediately before each actual backend write
  attempt. The supplied revision 3 is consistent with work body + work Enter + RC body,
  without a fourth backend write for RC Enter. This corroborates an early refusal; it does
  not by itself identify which observation refused it.
- The real lane sets `EvidenceTimeoutSeconds=10`; bridge confirmation polls every 100 ms
  within that budget. Composer polling was every 50 ms. The supplied test body ended in
  roughly three seconds (15:46:08.4 start to 15:46:11.69 disposal), so expiry of a full
  10-second composer/bridge wait is inconsistent with that extract. A snapshot read also
  has its separate one-second fixture bound; missing exact failure evidence prevents ruling
  out every early read failure.
- `src/Antiphon.FakeClaude/Program.cs`: the first submitted `/remote-control` in scenario
  `c514` synchronously emits `SUBMITTED:/remote-control`, `remote-control is active` and
  `RCMENU:armed`, with no arm timer/delayed banner. A later command opens the menu. A lone
  Enter burst submits; body and Enter remain separate. `Crunched for 1s` is a literal output
  marker, not a one-second sleep. The fake repaints a readable composer after processing
  each input burst. Initial UserPrompt persistence precedes response output and that repaint,
  so waiting for UserPrompt alone does not establish a fully painted idle screen.
- Production arm proof is `IRcBridgeProbe` / bridge state, not a title, generic screen
  movement or an invented UserPrompt. `ChildOutputRcProbe` substitutes the child's own
  `RCMENU:armed` output in this fixture. Work delivery still requires a complete correlated
  native UserPrompt. LF normalization, bracketed paste and the separate Enter contract are
  unchanged.

## History and public card evidence

`git log -S C514_Health_request -- tests/Antiphon.Tests/Application/RemoteControlRecoveryPtyIntegrationTests.cs`
identifies `93c9f2d4a`. `6792147f0ed0966ab53caa6dd3164e10736b41c7`
fixed the original stale body-sequence fence and immediate bridge-probe race;
`b8024f67d83e3500aca53d7ad4766d4e549db05e` then sourced the Enter sequence from
the same runner as the body. `4de01c9a50ceb5874294b70f79deaa718b57b908`
introduced the additional dialog-guard observation. These fixes did not cover a further
redraw between the two fresh observations.

Public board search `card.ps1 search C514 -Board Antiphon -All` completed (3 hits),
and `card.ps1 get CARD-0514 -Board Antiphon` was read. CARD-0514's closure explicitly
records the earlier typed-but-unsubmitted product bug and a separate fake menu-paint race.
Its old 8/8 Windows receipt is historical, not evidence for this fix. Repository report
`docs/investigations/2026-10-19-rc-windows-live-reds.md` records a different C514 working-modal
failure; it does not establish this health-arm symptom as a known harmless flake.

## Repair and portable coverage

Red witness commit: `b65503281ef4e138c4ce6ca2a4e528b1847736c4`.
Fix commit: `43a5291f93ac0081f5edf96779faf480f6d7508c`.

Recovery now retains the runtime epoch/input revision from before the single command-body
write and expects exactly one revision advance. Composer recognition and Enter refresh share
the original composer-evidence deadline; no timeout setting or numerical budget increased.
Only `stale-observation` (definitive zero-byte refusal) may refresh the output fence. Before
each attempt the command must occupy the readable composer, input custody must be unchanged,
the generation must remain proven, no blocking dialog may stand, and transcript state must
still be idle. Every actual write result other than stale is terminal. `Written` is followed
only by the existing bridge probe; `Unknown` never authorizes another Enter or body.

The runtime now honors caller-supplied input revision/epoch instead of replacing those pins
with a newer observation's authority. This tightening is necessary to prevent a redraw retry
from submitting intervening operator input. Native runner fences and transcript receipt
semantics remain intact.

The fake already has raw Linux tty support and a staged native Linux apphost. The lane helper
now resolves that executable and admits Linux UnixPty as well as Windows ConPTY. The Windows
inbox backend pin is retained. No test was removed, skipped, relaxed or given a longer sleep.

## Verification

Linux Debian 12, .NET 9.0.22 runtime / SDK 10.0.403, host `4551c5a7852f`.
All drivers used `TEMP=/tmp`, `UseAppHost=false` and the checkpoint/build-slot gate.
No Windows-native tests were run. The green selection executed 55 tests, all passed,
zero failures/skips: maintenance 28, PTY integration 8, recovery 17, dialog delivery 2.
The original health-arm test passed in 2.228 seconds and real modal clearance in 3.454
seconds on UnixPty. The redraw regression passed in 4.070 seconds. These are single
Linux samples, not measured Windows p95 values or proof of the original Windows failure.

Green receipt timing: slot wait 15 seconds; build 247.919 seconds; startup/discovery
45.406 seconds; tests wall interval 42.135 seconds; teardown 2.890 seconds; whole child
host wall 90.430 seconds. TUnit reports 86.824 seconds. Nested spans are not summed into
wall time. Detailed per-database/per-IO/child phase attribution is unavailable for this run
(the wrapper TimingRoot alone does not enable the opt-in phase recorder); retain it as
unknown. Per-test durations and the exact 55-case membership are in the fresh TRX.

An initial short ExpectedSourceSha was refused before any build/run (exit 2). The first
actual red invocation used an invalid all-bare method OR selection: zero tests, exit 3.
It is explicitly not red evidence. Separate exact-method rows reused the verified build:
the redraw witness failed `ArmedObserved` versus `ArmUnconfirmed`; the custody witness
failed expected `stale-observation` versus actual `written`. Both were assertion failures,
not setup/build failures. Build provenance is clean/verified at the red commit.

The scoped green row selects only RemoteControlMaintenanceQueueTests,
RemoteControlRecoveryPtyIntegrationTests, RemoteControlRecoveryTests and
ClaudeDialogDeliveryTests. These cover the modified arm state machine, real child lane,
shared guarded-input boundary and unchanged modal dismissal. Broad Unit/full-assembly runs
are outside this focused Debug selection.

No build definition file changed. The real land graph `-Check` (both evaluation passes and
canary) passed on the red tree and again at the fix commit, with the evaluator built from
that fix's verified isolated output. The final check held its build slot 47 seconds and
reported digest `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`,
count 68, `phase=pass2`, `approved`, exit 0. No `-Update` was run. The evidence-history
guard passed base..fix (two commits, zero evidence entries/violations).

Essential unedited checkpoint lines (raw logs and JSON receipts remain ignored):

```text
CHECKPOINT CP-RED-REDRAW commit=b65503281ef4e138c4ce6ca2a4e528b1847736c4 build=reused filter=/*/*/RemoteControlMaintenanceQueueTests/C514_Composer_redraw_before_Enter_reobserves_without_retyping executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-5e08c4d4/.antiphon/checkpoints/CP-RED-REDRAW-20261030-160251-0c82/run.trx slot=granted waited=0s dirty=0 source=b65503281ef4e138c4ce6ca2a4e528b1847736c4 sourceState=clean buildSource=verified
CHECKPOINT CP-RED-CUSTODY commit=b65503281ef4e138c4ce6ca2a4e528b1847736c4 build=reused filter=/*/*/RemoteControlMaintenanceQueueTests/C514_Pinned_arm_input_custody_cannot_be_replaced_by_a_fresh_dialog_read executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-5e08c4d4/.antiphon/checkpoints/CP-RED-CUSTODY-20261030-160412-95cc/run.trx slot=granted waited=0s dirty=0 source=b65503281ef4e138c4ce6ca2a4e528b1847736c4 sourceState=clean buildSource=verified
CHECKPOINT CP-GREEN commit=43a5291f93ac0081f5edf96779faf480f6d7508c build=ok filter=/*/Antiphon.Tests.Application/(RemoteControlMaintenanceQueueTests*)|(RemoteControlRecoveryPtyIntegrationTests*)|(RemoteControlRecoveryTests*)|(ClaudeDialogDeliveryTests*)/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-5e08c4d4/.antiphon/checkpoints/CP-GREEN-20261030-160700-07d2/run.trx slot=granted waited=15s dirty=0 source=43a5291f93ac0081f5edf96779faf480f6d7508c sourceState=clean buildSource=verified
```

Raw record roots: `.antiphon/checkpoints/CP-RED-20261030-155752-b6c3/` (isolated red
build, exit 0, 207.05 seconds; zero-test first selection), the two red exact-method roots
above, and the green root above. Final graph log: `.antiphon/c514-final-land-graph.log`.

Read-only compatibility checks after fetching targets: `git merge-tree --write-tree
origin/rc/2026-10-27 HEAD` passed against RC tip
`335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f` (tree
`21fe427099734b123a758cc8d4ceeff4a4a9f115`). The required master probe against
`bce716158d9a3fcca5bf548a07fdb257de637f1f` found four conflicts outside the task's
changed files: `.antiphon/task-af2b78c8.md`, the CARD-1584 investigation,
`CapacityRecoveryService.cs`, and `CapacityLockOrderTests.cs`. Repeating that read-only
probe with the unchanged task base produces the same four conflicts, confirming inherited
RC/master divergence. No merge, rebase, branch switch or target write was performed. This
report makes no master-landable claim; the caller requested RC integration.

After joining every run, the two task-created isolated build directories
`/work/worktrees/task-5e08c4d4/tests/Antiphon.Tests/bin-c514-red` and
`/work/worktrees/task-5e08c4d4/tests/Antiphon.Tests/bin-c514-green` were removed by their
exact literal paths. Raw receipts/logs remain under `.antiphon`; no other output directory
or Windows checkout was deleted. The source receipt remains tied to the original verified
build even after output cleanup.

## Owed native Windows row

Wait until qualification `d47848cc` releases the native lock. From the clean, synced,
reviewed task checkout (not the live canonical checkout), run these **on native Windows**.
Do not run them concurrently with qualification. Each repetition creates a new child PTY;
the host stays the same, as documented for checkpoint `-Repeat`.

```powershell
$sourceSha = (git rev-parse HEAD).Trim()
$env:ANTIPHON_TEST_PHASE_ROOT = Join-Path (Get-Location) '.antiphon/c514-windows-phases'
$env:ANTIPHON_TEST_PHASE_RUN_ID = 'c514-native-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$env:ANTIPHON_TEST_PHASE_ATTEMPT = '1'
$env:ANTIPHON_TEST_PHASE_SUITE = 'Antiphon.Tests'
$env:ANTIPHON_TEST_PHASE_GROUP_ID = 'C514-WIN-REGRESSION'
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name C514-WIN-REGRESSION -Project tests/Antiphon.Tests -OutputPath bin-c514-win-regression/ -Filter '/*/*/RemoteControlRecoveryPtyIntegrationTests/C514_*' -MinExecuted 8 -ExpectedSourceSha $sourceSha -TimingRoot .antiphon/c514-windows-timing
$env:ANTIPHON_TEST_PHASE_GROUP_ID = 'C514-WIN-ALONE20'
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name C514-WIN-ALONE20 -Project tests/Antiphon.Tests -OutputPath bin-c514-win-alone20/ -Filter '/*/*/RemoteControlRecoveryPtyIntegrationTests/C514_Health_request_reaches_busy_and_idle_recipients' -Repeat 20 -MinExecuted 20 -ExpectedSourceSha $sourceSha -TimingRoot .antiphon/c514-windows-timing
$env:ANTIPHON_TEST_PHASE_GROUP_ID = 'C514-WIN-LOAD10'
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name C514-WIN-LOAD10 -Project tests/Antiphon.Tests -OutputPath bin-c514-win-load10/ -Filter '/*/*/RemoteControlRecoveryPtyIntegrationTests/C514_Health_request_reaches_busy_and_idle_recipients' -Repeat 10 -MinExecuted 10 -ExpectedSourceSha $sourceSha -TimingRoot .antiphon/c514-windows-timing
```

Run ALONE20 without competing qualification/build work, then LOAD10 with the representative
AppHost/agents workload recorded (no artificial load generator or interference with someone
else's run). Require 8/8, 20/20 and 10/10, zero skips/failures, clean verified source receipts,
and each native failing-test iteration's child markers and prompt assertions. Retain TRX,
phase JSONL and PTY diagnostics; record source SHA, host/backend, CPU/load observations,
build-slot wait, build/discovery/run/teardown times and per-test p50/p95/max. Unknown phase
timing remains unknown. Thirty success samples provide bounded confidence, not proof that
all Windows scheduling interleavings are covered. If a sample fails, retain its maintenance
evidence/write outcome and full PTY diagnostic before teardown; do not loosen assertions,
add sleeps or raise deadlines. Existing timeout tuning requires measured comparable p95.

Native execution and Windows p95 remain pending. No Windows-native result is inferred from
the portable seam or UnixPty run. Review should retain that incomplete gate.

--- next stage ---
next: merge to rc after a quick Opus check; native re-run owed when the qualification run ends
handoff: review the pinned-custody Enter refresh and portable receipts, then run the owed native rows once d47848cc releases its lock; do not claim native qualification complete from Linux
artifact: docs/investigations/2026-10-30-c514-arm-unconfirmed-debug.md
