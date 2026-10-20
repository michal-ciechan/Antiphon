# CARD-1284: Linux fake CLI regression run

Plan task `b36894e8-06ec-43d9-a5ac-240dabd3851e`. Target: **master only**.
Inspected HEAD and freshly fetched `origin/master`:
`f17dfb7b030206a4ae168e6c260b1603934f1c9a` on 2026-10-20.
Final fetch observed `cfbc89f1d8f60599b65d6d532a6b4753178c3ab2`;
the intervening CARD-1383 registry changes leave both fake implementations,
all selected test bodies and the Pty-project census unchanged.

**Work remains, but the two-project premise is too broad.** Five classes in
`Antiphon.Tests` contain Linux-capable fake CLI process tests. The fake CLI PTY
callers in `Antiphon.Agents.Pty.Tests` are Windows-gated; running that project's
contract classes on Linux would not exercise the changed reader. Do not port them
as part of this verification card. CARD-0690's closure does not close this gap.

This dispatch did not fold in TestDesign. Next: **test-design**, to certify the
bounded selection below and append `## Verification design`; no build or test
was run during Plan. There is no new product design or proposed runtime change.

## Ground truth

| Card assumption | Current code / retained evidence | Consequence |
|---|---|---|
| CARD-1277's Linux fake stdin change still needs post-land coverage. | Master contains `2b4e9b4fb5b473ae84cf624cb6bcf1e2c1063c6b` and `b5d46722a18c963acac6ebc47ac12a50487ed87c`. Both `src/Antiphon.FakeClaude/Program.cs` and `src/Antiphon.FakeGrok/Program.cs` call `TryEnableLinuxRawStdin` and use libc in `ReadStdinChunk` on Linux. | A Linux execution remains meaningful; Windows green cannot cover this branch. |
| CARD-0690 may make this card obsolete. | Live CARD-0690 is Done: CARD-0801 repaired the Unix fake **Herdr listener**; its 2026-10-20 closure cites 37 targeted passes, not a whole-Unit run. `FakeHerdrServer.PaneSendTextJson` simulates launch detection; it never launches fakeclaude/fakegrok. | Do not repeat the SessionRunner Unit lane or treat its listener results as fake CLI stdin evidence. |
| The two named projects both have Linux fake-driven PTY classes. | The Pty project's `FakeClaudeContractTests`, `FakeGrokContractTests`, `ClaudeSubmitContractTests`, `PtyLargeWriteTests`, and fake arm of `ClaudeVerifiedDeliveryTests` reject non-Windows hosts before launch. `C1022TypedInputTests` gates its native fake case on Windows. `FakeVsRealClipParityTests` is explicit, headed, uses a Windows fake executable and calls live-provider eligibility first. | No native Linux checkpoint in that project at this SHA. Pure model tests there do not reach libc stdin. |
| Existing CARD-1277 verification may already cover everything. | Review `cd4110a7-5246-4374-b394-065c2460d539`, retrieved from the task API, reports only CP-2 (2/2) and CP-3 (4/4), zero failed/skipped, at `679d88ee802b63def56a36e2d5843c9e84171096`, run `20261015-103802-2089`. It explicitly discloses that other fake-driven classes were not run. Code evidence is [here](../evidence/2026-10-15-card-1277-code.md). CARD-1284 has no recorded stage outcomes at inspection. | Preserve those historical passes; they do not establish a complete post-change sweep. Current-source execution of the bounded roster remains outstanding. |
| Nightly will provide Linux coverage eventually. | `scripts/windmill/antiphon-nightly-tests.json` commissions the Windows nightly checkout; the fake readers choose their implementation by OS. | Do not substitute Windows nightly evidence for this Linux run. |
| Tests must be pinned to a particular fleet host. | Read `GET /api/runner-defaults` (revision 2, configured preference resolves to Linux) and `GET /api/session-runners`: eligible, non-draining Linux capacity is present at inspection. | Commission the **Linux native fake CLI lane**, with `-Platform Linux` and no `-Runner` pin. Re-read placement before dispatch; no fleet address belongs in the manifest. |

## Decisions

- **D-1 — Verify the existing Linux execution paths once.** Use the five classes
  below, with method filters for mixed classes. Reject a full Unit/namespace/
  assembly sweep: it adds unrelated tests and still does not turn Windows-gated
  fake contracts into Linux coverage.
- **D-2 — Preserve platform guards and fixture transport settings.** Do not
  remove skips, add Linux ports, set headed/live-provider flags, or globally set
  `ANTIPHON_FAKE_LF_ENTER`. CARD-1277 deliberately tests with that flag absent;
  the readiness/disposal fixtures deliberately set it in their own children.
  Reject environment changes that would hide a raw-stdin regression.
- **D-3 — One isolated build, five serial rows.** Reuse the build only at the same
  clean committed SHA. Preserve process limiters and set
  `TUNIT_MAX_PARALLEL_TESTS=1`. Reject overlapping test projects/hosts and
  repeat-until-green execution. No second Pty-project run is warranted by the
  current Linux census.
- **D-4 — Repair only a demonstrated failure.** Preserve the failing row and
  attribute it before editing. Fix a bounded fake/fixture regression with a
  focused failing test, then rerun affected rows; file or link unrelated
  defects with exact evidence. Reject timeout widening, assertion weakening,
  extra skips, or treating an infrastructure failure as a test failure.
- **D-5 — Master-only evidence, no activation.** No RC merge, daemon restart,
  deployment, nightly configuration change, or session lifecycle change.
  A later source repair must receive its ordinary review before landing.

## Execution census and slices

The paths below are under `tests/Antiphon.Tests/`. Source inspection followed
direct fake executable references, `TestAppHostPath.Require`, the
`DirectSessionRunnerClient` callers and `OwnedPtyFixture` consumers, rather than
counting references in comments as process launches.

| Slice / coverage | Files and exact existing tests | What must remain true |
|---|---|---|
| S1 / V-1 | `Agents/Card1277PtyHostFixtureTests.cs`: both tests | A body followed by a separate CR produces normalized UserPrompt/AssistantText with the complete nonce; screen echo remains cut at 60, not 61, characters. Two results. |
| S1 / V-2 | `Agents/RunnerGrokAdapterReadyTestsPty.cs`: `Fake_dashboard_marker_reaches_ready_and_complete_first_prompt` | Both marker arguments reach ready and produce exactly one complete UserPrompt; native host disposal witnesses pass. Two results. Exclude the Windows modern qualification and paid real-CLI methods. |
| S1 / V-3 | `Agents/DirectSessionRunnerClientDisposalTests.cs`: entire class, including its `OwnedPtyFixture` helper in the same file | Five tests launch fakegrok; the sixth is the adjacent shell/linger cleanup control. Ready/adopted sessions still accept complete prompts, owned hosts exit, another client's host survives, and cleanup remains idempotent. Six results. |
| S1 / V-4 | `Application/HerdrAlwaysOnChannelParityTests.cs` and `.StandingRecovery.cs`: `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply`, `Standing_native_wire_missing_target_never_creates` | Four real PtyHost history cases preserve identity, complete queued channel wire and nonce-bearing reply. The second method retains the Grok/PtyHost missing-session control plus its two cheap Herdr argument controls. Seven results. |
| S1 / V-5 | `Application/ChannelOutboundRecoveryTests.cs`: `Running_converter_reconciles_after_dispatcher_death`, which calls `Process_death_preserves_ownership_at_each_boundary("conversion-running")` | The Linux shell wrapper launches staged fakegrok through the real isolated PtyHost/queue; recovery observes native UserPrompt, AssistantText and TurnEnd and completes the converter/publication checks. One result. Other crash-cut methods do not launch this CLI and are excluded. |

S1 is an execution/evidence slice, not an instruction to edit these tests.
TestDesign records the final manifest in this plan before execution. Keep raw
TRX, JSON, logs and receipts ignored under `.antiphon/`; preserve the exact
CHECKPOINT lines and provenance in an optional individual report at
`docs/superpowers/evidence/2026-10-20-card-1284-linux-fake-cli-run.md`.

S2 is conditional on S1 failure. Candidate repair files are the two fake
`Program.cs` files, the failing test file, or its demonstrated shared helper
(`tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs` and
`tests/Antiphon.Tests/TestHelpers/TestOwnedPtyHost.cs` only if evidence reaches them).
Read the relevant owner before changing an area. Commit/push a focused failing
test before its fix. Rebuild once at the repair SHA and rerun only rows reached
by the changed code; a shared fake-reader repair reaches all applicable rows.
Record new regression methods and update the manifest before their run.

### Exclusions verified from source

- Windows-gated `Antiphon.Tests` callers:
  `SessionMessageQueuePtyIntegrationTests`,
  `SessionMessageQueueGrokPtyIntegrationTests`,
  `DelegationBriefCeilingPtyTests`, `GrokDelegateEndToEndTests`,
  `GrokSignInRuntimeTests`, `SessionQueueReceiptPlumbingTests`, and
  `RemoteControlRecoveryPtyIntegrationTests` through
  `TestHelpers/RemoteControlPtyLane.SkipIfUnavailable`.
- FakeHerdr-only launch/label tests do not start a fake CLI:
  `HerdrLabelFollowWireTests`, `HerdrLabelFollowFlowTests`,
  `AgentAttachHerdrTests`, the Herdr history method and
  `Herdr_launch_definition_starts_adopts_and_exits`. A registry executable
  reference alone is not stdin coverage.
- Preserve the existing Linux skips on
  `Standing_claude_ptyhost_native_wire_missing_target_never_creates`
  (CARD-0878), `Standing_grok_unavailable_native_store_never_creates`
  and `AlwaysOn_channel_bound_survives_child_death_and_replies`
  (CARD-0865). This card does not resolve their obligations.
- Managed adapters, `StdinClipModelTests`, `ComposerInputProbeTests`,
  launch-spec-only `GrokDelegateDispatchTests`, process-limiter rosters and
  tests using real installed CLIs do not exercise the changed fake reader.
  Do not run headed canaries to fill the missing Pty-project lane.

## Proposed execution contract for TestDesign

Use the checkpoint tool's `run --plan <this-plan> --rows CP-1,CP-2,CP-3,CP-4,CP-5
--serial --expected-source-sha <full-committed-HEAD>` entry point. If it returns
75, keep the run ID and call `wait` until terminal; await the executor before
settlement. Bootstrap the tool once through `scripts/build-slot.ps1` into a
fresh alternate output and report that setup build separately. Checkpoint
rows acquire their own build slots; do not wrap them in another slot lease.
Use the existing Linux `UseAppHost=false` default and staged native fake/PtyHost
apphosts; do not revive earlier hand-built staging workarounds.

Each row below names the **Linux native fake CLI lane** in `Group`. All share
S1 and one source SHA. TestDesign must bind the expected executed method roster;
the stated 18-result total is derived from source attributes, not a measured
pass count. Require fresh TRX identities, nonzero executed counts, zero
failures/skips and clean-source/build-bound receipts. The two Herdr argument
controls and shell cleanup control are explicit bounded inclusions.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1284-linux/` | linux-fake-pins | `/*/*/Card1277PtyHostFixtureTests*/*` | V-1 | both methods, 0 failed/skipped | 2 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | linux-fake-ready | `/*/*/RunnerGrokAdapterReadyTestsPty*/Fake_dashboard_marker_reaches_ready_and_complete_first_prompt*` | V-2 | both marker arguments, 0 failed/skipped | 2 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | CP-1 | linux-fake-disposal | `/*/*/DirectSessionRunnerClientDisposalTests*/*` | V-3 | all six methods, 0 failed/skipped | 6 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | CP-1 | linux-fake-standing | `/*/*/HerdrAlwaysOnChannelParityTests*/(Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply*)\|(Standing_native_wire_missing_target_never_creates*)` | V-4 | four history and three missing-target arguments, 0 failed/skipped | 7 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | CP-1 | linux-fake-converter | `/*/*/ChannelOutboundRecoveryTests*/Running_converter_reconciles_after_dispatcher_death*` | V-5 | the native running-converter method, 0 failed/skipped | 1 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Estimated ordinary execution: **24 minutes**, including the one test-project
build, plus approximately 3 minutes for tool bootstrap and actual slot waits.
These are scheduling estimates, not deadlines or measured counts. No new
mutation cycle is commissioned by this existing-coverage sweep; CARD-1277's
four pending PCs retain their original owner. A repair may require its own
regression/guard design.

On failure, retain the exact class/method, source SHA, assertions, raw child
JSONL and owned process diagnostics. A red current-source test is not evidence
that CARD-1277 caused it. For that attribution, rerun only the failing method
on the relevant pre-change commit in a detached baseline checkout, using the
checkpoint baseline support and a separately declared diagnostic selection.
For a new S2 repair, also preserve the failing S1 source as its baseline.
Report additional runs and their reason; never rerun the whole selection just
to classify one failure. Search the board before filing a duplicate and link
an existing defect when appropriate.

Completion requires all selected results accounted for with qualified receipts,
or each red explicitly assigned to a repair/follow-on card with its evidence.
A file/build/slot/fixture refusal or zero-test selection is incomplete execution,
not a passing sweep. Remove only task-owned alternate outputs after all
processes finish. The caller receives the tested SHA, per-row counts and
dispositions; it must not close this card solely because CARD-0690 is Done.
