# Group 35a Linux diagnosis and fixes

Task: 7cd0d43e. Base: 5b6a1f9cb14d79b7169a19ba1a6fc2ebb5270d82 (the brief's admitted master baseline). Assigned branch: feat/card-task-7cd0d43e. No desktop/native qualification lock was acquired.

## Baseline evidence

A gated isolated build succeeded: 0 errors, 1197 pre-existing warnings, 458.34 seconds; slot wait 0 seconds, lease held 459 seconds. OutputPath: bin-7cd0d43e/, UseAppHost=false. The 64 MiB checkpoint-reader limitation is the brief's reason for using the build-slot wrapper with dotnet run --no-build.

One gated sequential selection at the clean base executed 62 rows: 22 passed, 8 failed, 32 skipped, test wall time 304.321 seconds; lease held 308 seconds, slot wait 0 seconds. Filter:

`/*/Antiphon.Tests.Application/(SessionQueueReceiptPlumbingTests*)|(HerdrAlwaysOnChannelParityTests*)|(RemoteControlRecoveryPtyIntegrationTests*)|(SessionMessageQueuePtyIntegrationTests*)/*`

| Class | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| SessionQueueReceiptPlumbingTests | 6 | 0 | 16 |
| HerdrAlwaysOnChannelParityTests | 12 | 6 | 6 |
| RemoteControlRecoveryPtyIntegrationTests | 4 | 2 | 2 |
| SessionMessageQueuePtyIntegrationTests | 0 | 0 | 8 |

Generated logs, TRX, HTML and opt-in phase data stay ignored under `.antiphon/evidence/task-7cd0d43e/`. The baseline TRX is `base/base.trx`; source is unchanged at the admitted base. Per-test durations are retained there; external-call and fixture phases without instrumentation are unknown, not zero.

Reproduced clusters:

1. Scripted C514: `C514_Working_modal_waits_for_operator_then_converts_without_replay` and `C514_Internal_queue_drain_creates_no_replay_request` call automated Claude input while the modal is open and receive the intended `modal-blocked` refusal. The operator path is `SendInteractiveInputAsync`; provider-internal activity is distinct from automated server input.
2. Herdr/Claude: both `Standing_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, Herdr, alwaysOn)` rows, `Herdr_launch_definition_starts_adopts_and_exits(ClaudeCode)`, and `Named_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume` fail because production Herdr reports Unknown input custody. `docs/session-runtime-invariants.md` CARD-0680 explicitly requires that behavior; composer text alone cannot authorize input.
3. PtyHost/Claude: both `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, alwaysOn)` rows reach their existing 90-second deadline waiting for the fake provider's native prompt. The append-only fake does not paint supported Claude composer chrome.

The other 26 native rows did not reproduce on Linux: 16 receipt-plumbing rows, 8 SessionMessageQueuePtyIntegration rows, and 2 real C514 ConPTY rows were explicitly skipped. No native fix is inferred from these skips.


## Implemented fixture repairs

- `RemoteControlRecoveryPtyIntegrationTests.cs`: the operator clears the modal through the explicit interactive-input API; provider-internal activity enters the fake provider instead of automated server input. The busy/idle transition uses `BridgeQueueHarness`'s store-aware helpers. The final conditional-input assertion now requires exactly the work body and Enter, preserving the prohibition on automatic modal keys and RC replay while accounting for guarded ordinary delivery. No assertion was removed.
- `src/Antiphon.FakeClaude/Program.cs`: optional `ANTIPHON_FAKE_CLAUDE_COMPOSER=1` paints the supported composer layout and redraws it in place. This lets the readiness probe observe both its token and its removal by Ctrl+U. Defaults are unchanged. This is test-peer behavior, not a production service or guard change.
- `HerdrAlwaysOnChannelParityTests.StandingRecovery.cs`: native-history fixtures opt into that composer, including subsequent policy environments on resume. Existing launch-completion assertions run before recipient-proof waits, retaining every history/identity/reply assertion and reporting launch failure immediately.

Commits (all pushed individually):

| SHA | Slice |
| --- | --- |
| 1d7cbd2dbec7a6e6f0209d1311bf69662ac8724e | Preserve base failure evidence before repairs |
| a053af717508e02924a246a7e80c05d39380bf37 | Distinguish operator/provider input in C514 |
| 55b88789b1cf2591ca2a3aa2c454680c43fa3ad8 | Opt-in fake composer for native-history fixtures |
| 9160fed63bde8e23a22c9396c5a197885c041f67 | Store-aware C514 busy/idle transitions |
| 0e38d27d3e2aedf9230398e0202f27710ebcb600 | Assert launch completion before waiting for proof |
| 8cf9943ca1968bd101eac8ce00b377d0f0fa469c | Redraw the fake composer so cleared probe input disappears |

## Execution history and final results

The final tested code SHA is `8cf9943ca1968bd101eac8ce00b377d0f0fa469c`, clean at execution. Subsequent commits change only this report. SHA-256 hashes and sizes of the actual test DLL and staged fake provider are retained in the external `build-provenance.json`; the test DLL is 68,508,160 bytes, exceeding 64 MiB.

| Selection/run | Passed | Failed | Skipped | Test wall time |
| --- | ---: | ---: | ---: | --- |
| Initial four classes at admitted base | 22 | 8 | 32 | 304.321 s |
| Initial fixture repair, same four classes | 23 | 7 | 32 | 320.215 s |
| Store-aware C514 + earlier launch assertion, two changed classes | 18 | 6 | 8 | 122.980 s |
| Redraw correction, exact PtyHost history method (four arguments) | 4 | 0 | 0 | 81.037 s |
| Final four-class selection | 26 | 4 | 32 | 107.054 s |

All builds passed with zero errors and 1197 existing warnings. Initial build cost: 458.34 s; necessary fixture rebuilds: 126.01 s, 129.63 s, 129.73 s. All used `scripts/build-slot.ps1`; each slot wait was zero. Source remained frozen during every test run. No Unit lane, release script, tag, desktop process-spawning test, or native lock was used.

| Final class | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| SessionQueueReceiptPlumbingTests | 6 | 0 | 16 |
| HerdrAlwaysOnChannelParityTests | 14 | 4 | 6 |
| RemoteControlRecoveryPtyIntegrationTests | 6 | 0 | 2 |
| SessionMessageQueuePtyIntegrationTests | 0 | 0 | 8 |

The final run exited 2 because four inherited Herdr/Claude rows remain red:

- `Standing_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, Herdr, False)`
- `Standing_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, Herdr, True)`
- `Herdr_launch_definition_starts_adopts_and_exits(ClaudeCode)`
- `Named_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume`

Their persisted failure reason is `Automated Claude input withheld: observation-unknown`. The runner deliberately computes known input history only for a non-adopted PtyHost session, not Herdr. CARD-0680 explicitly requires Herdr to report Unknown. Making these success expectations pass would require changing their acceptance contract or bypassing that safety invariant, neither of which is part of this fixture repair. They remain visible, unchanged assertions; no new skip or production bypass was added. The caller must commission that contract follow-up separately. This is not a green class, full rc qualification, or a claim that the original 31 Windows failures are repaired.

No Windows-specific path/shell cluster reproduced, so there is no Windows-semantics string witness to credit. The skipped native tests are not Linux passes.

The final full selection uses the filter above with `--maximum-parallel-tests 1`, TRX reporting, and `dotnet run --no-build --property:OutputPath=bin-7cd0d43e/ --property:UseAppHost=false` behind the build-slot wrapper. Initial selections omitted the phase recorder's required attempt field and therefore retained TRX durations only. Final execution sets all five phase variables, including `ANTIPHON_TEST_PHASE_ATTEMPT=1`; its case/phase sidecar includes test IDs, batch 35a and assembly MVID. Uninstrumented external calls and phases remain unknown, not zero. Existing timeouts were not increased; test-driver hard bounds were 900 seconds (600 for the focused four-row witness), including at most five minutes of slot admission.

## Native rerun rows

The caller should schedule these sequentially in the owning Windows qualification lane, with the staged fake providers and native prerequisites. The protected a779085c supervisor/lock was untouched. Build once under the slot gate with alternate `OutputPath=bin-35a-native/`, then use this template once per row:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 35a-native-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-35a-native/
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 35a-native-row -- dotnet run --no-build --project tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-35a-native/ -- --treenode-filter "/*/*/<Class>/*" --maximum-parallel-tests 1 --report-trx
```

| Class substituted into the exact filter | Native coverage still needed |
| --- | --- |
| SessionQueueReceiptPlumbingTests | 16 Windows-only rows, including ModernConPty recovery/receipt/paste cases |
| SessionMessageQueuePtyIntegrationTests | All 8 rows, including both backend arguments and large/batched multiline and swallowed Enter |
| RemoteControlRecoveryPtyIntegrationTests | Two real ConPTY C514 producer-to-recipient rows; portable six already pass |
| HerdrAlwaysOnChannelParityTests | Six Linux-skipped rows and Windows parity of repaired native history; four policy-conflicting Claude/Herdr rows remain expected failures pending contract follow-up |

The C514 deferred-card, clearance and internal-drain sample methods belong to `RemoteControlRecoveryPtyIntegrationTests`; the large/batched multiline and swallowed-Enter samples belong to `SessionMessageQueuePtyIntegrationTests`.

## Evidence and handoff

Durable generated evidence root: `/work/test-evidence/task-7cd0d43e/`. It contains all logs/TRX/HTML, final case/phase data, per-class historical counts and tested binary hashes. Generated payloads are not committed. The tracked report is `/work/worktrees/task-7cd0d43e/.antiphon/task-7cd0d43e.md`.

All 33 producer-owned `bin-7cd0d43e` directories were removed after the completed runs; their exact inventory is retained externally. Binary hashes matched the pre-execution provenance record before removal. Evidence-policy and whitespace checks passed. A pre-report `git merge-tree --write-tree origin/master HEAD` was clean; publication checks are repeated after the final report commit. Fixture-only repairs are authorized to proceed without Review by the brief; inherited policy-conflict failures and outstanding Windows execution remain explicit landing/qualification limitations.

--- next stage ---
next: land
handoff: Land the scoped fixture repairs with the four inherited Herdr policy failures and native rerun debt explicitly retained; commission acceptance-contract follow-up separately. Do not credit full rc qualification.
artifact: /work/worktrees/task-7cd0d43e/.antiphon/task-7cd0d43e.md
