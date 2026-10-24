# Group 28 Debug result

Fixed all eight failures reproduced on Linux. The three changed classes passed 137/137 after repair; the unchanged inbound recovery class passed 41/41 at base. Latest results therefore cover 178 cases with zero failures or skips across the two identified invocations. Native Windows qualification remains owed.

## Source and publication

Branch: `feat/card-task-6d1c77a9`. Base: `e970c2c9ff2f723dd1cccf130e61901eeca5f200`, the commissioned minimum base. Fetched master before reproduction and again before handoff. No branch switch, rebase, merge, force push, tag, release script, Unit lane, Review, desktop run, or native lock was used.

Each class repair was committed and pushed separately before verification:

- `c3efdb3943898aee350fdae57e004ed4a8e89ccb`: `tests/Antiphon.Tests/Application/SessionRuntimeActionRoutingTests.Finish.cs`; bind the replacement recipient's runtime generation and shared Claude observation before the positive G2 receipt. The original stale-action refusal is still checked first.
- `2c666fa1277bffb8ab91550039489d508064fe91`: `tests/Antiphon.Tests/Application/ChannelBridgeTests.cs`; opt the redelivery witness into the state-store/loader/action registrations used by `BridgeQueueHarness`, so its real runtime can ingest the submitted prompt. The other fixture callers retain their existing setup.
- `07732b6ba513d4432e18e97cc5dc0590f24083e7`: `tests/Antiphon.Tests/Application/CodexStartupDeliveryTests.cs`; retain the diagnostic assertion with a status-specific expectation: Starting/Stopping report startup readiness, while Created/Stopped/Failed report liveness refusal. Conflict, no-input, queue identity, attempt count, and subsequent Running-delivery assertions remain.

No product code, test case, timeout, retry, or input/delivery assertion was removed. Codex's two diagnostic assertions now check the correct exact reason for each status instead of requiring a startup message for every non-Running state.

## Counts and attribution

Fresh TRX counts are passed / failed / skipped. Baseline source is e970c2c9; repaired source is 07732b6ba.

| Class | Reported native failures | Linux baseline | Latest Linux result |
|---|---:|---:|---:|
| SessionRuntimeActionRoutingTests | 29 | 34 / 1 / 0 | 35 / 0 / 0 at 07732b6ba |
| ChannelBridgeTests | 22 | 39 / 1 / 0 | 40 / 0 / 0 at 07732b6ba |
| ChannelInboundRecoveryTests | 1 | 41 / 0 / 0 | 41 / 0 / 0 at e970c2c9; unchanged |
| CodexStartupDeliveryTests | 13 | 56 / 6 / 0 | 62 / 0 / 0 at 07732b6ba |

All eight residual failures were demonstrated at the committed base before edits:

- **Old-generation observation:** `C1259_missing_or_truncated_prompt_never_confirms(old-generation)` changed the persisted generation while retaining the fake's G1 observation. Its intended positive G2 request remained deferred and produced null instead of LateConfirmed. Refreshing only after the stale-action assertions restores the positive control without granting the old action authority.
- **Missing DI:** `Redelivered_message_is_not_routed_twice` called `ObserveTranscriptAsync` without a `SessionStateStore`. Runtime ingestion requires that store and returned no persisted receipt; the unchanged complete-UserPrompt count was zero instead of one. The repaired witness exercises the same runtime ingestion path and passes that count assertion.
- **Stale input-admission diagnostic expectations:** both `Send_now_during_boot_is_refused_without_input` and `Mode_now_during_boot_is_refused_without_input` failed for Created, Stopped, and Failed. `RequireImmediateAdmissionAsync` checks `IsLiveOrUnknownAsync` before readiness; only Starting/Running/Stopping are eligible for liveness. Correcting the expected reason retains the refusal checks and lets their no-input/state-preservation/recovery checks execute.

No stale-screen, Windows path/platform, shared-state, load-induced, or product-defect cluster was demonstrated in the assigned classes. Consequently no Windows string witness or product repair was needed. The other 57 reported native failures do not reproduce at this newer base; these Linux results do not establish their native disposition.

Sample ownership was checked: all named C1213 branch samples and the C1558 platform samples are in `DelegateBranchDeliveryTests`; C1104's production-chunk pointer sample is in `BlockedTaskParkDeliveryTests`. Those sibling classes and all other sibling-owned classes were untouched.

## Execution, timing, and evidence

Host: server2/Linux, Debian 12, .NET 9.0.21, TUnit 1.44. Every build/test driver used a host build slot, granted with maxcpucount 6 and reported admission wait 0s. Sources stayed frozen during each run. All drivers exited and were joined.

1. Baseline checkpoint attempt `g28-base`: compiler build succeeded, zero errors, elapsed 3m51.39s. The wrapper then refused `output-inventory-failed`, exit 2, with zero tests executed. Its complete held interval was 320s, including preflight and inventory work. This failed receipt is preserved, not credited as green.
2. Baseline direct gated execution of that same built output: 178 cases, 170 passed / 8 failed / 0 skipped, 4m50.821s; driver exit 2. This extra invocation supplied the execution the wrapper had not started, without another build.
3. Repair build at 07732b6ba: zero errors, elapsed 3m33.73s, driver exit 0. Rebuild was necessary to compile the committed fixture changes. Both builds retained 1,222 existing warnings.
4. Repair execution: exact three changed classes, 137 passed / 0 failed / 0 skipped, 3m59.767s; driver exit 0. The inbound class and shared helper source did not change, so its prior result was retained rather than rerun.

Bounds were provisional finite Debug budgets, not measured p95: 900s for the first combined checkpoint including up to 5 minutes of admission; subsequent build/test drivers had 600s total bounds including up to 3 minutes admission. INT cleanup escalation had 30s. No bound was reached and no timeout was increased to obtain green.

TRX retains per-test durations, arguments, and executed class membership. Opt-in phase JSONL is retained beside the runs. Assembly DB startup/migration was shared once per invocation: baseline start 5.252s and migration 33.457s; repaired start 4.984s and migration 36.180s. Other uninstrumented spans remain unknown/remainder, not claimed as zero. The sidecars label the shared fixture outcome incomplete; TRX determines the test outcome. These are small-sample Debug timings, not native qualification or a p95 study.

The checkpoint inventory refusal is a separate inherited tooling defect: the built test DLL was 68,581,376 bytes, exceeding the inventory reader's 64 MiB content limit. Directly calling its `Read-CheckpointBytes` on that DLL confirmed `selected file exceeds size limit`. No inventory bypass, reader repair, or checkpoint credit is claimed. The fallback was the brief-authorized direct build-slot workflow.

Essential unedited failed receipt:

```text
CHECKPOINT g28-base commit=e970c2c9ff2f723dd1cccf130e61901eeca5f200 build=failed filter=/*/*/(SessionRuntimeActionRoutingTests*)|(ChannelBridgeTests*)|(ChannelInboundRecoveryTests*)|(CodexStartupDeliveryTests*)/* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-6d1c77a9/.antiphon/group28/base/g28-base-20261024-134739-077c/run.trx slot=granted waited=0s dirty=0 source=e970c2c9ff2f723dd1cccf130e61901eeca5f200 sourceState=clean buildSource=unknown
CHECKPOINT g28-base EXIT CODE: 2
```

Evidence root: `/work/worktrees/task-6d1c77a9/.antiphon/group28/`:

- `base.log`, `base/g28-base-20261024-134739-077c/{build.log,source.json,git.txt}`; source start/end both clean at e970c2c9 with the same fingerprint.
- `base-tests.log`, `base-tests/base.trx`, `timing-base/`.
- `fixed-build.log`, `fixed-tests.log`, `fixed-tests/fixed.trx`, `timing-fixed/`.
- `base-assembly.sha256`, `fixed-assembly.sha256`, `fixed-source.sha`, `fixed-pre-test-status.txt` (empty), and `owned-outputs.json`.

Baseline DLL SHA-256: `94915a0ced55ec21bd91f529f5594b7bf1dd566b867ce4554677317ea61118c9`.
Repaired DLL SHA-256: `aa00876e3865eda11a6743318d92b038234e08eb0135fa65ccb3bf6abc592001`; verified unchanged after execution. Tracked source remained clean. Both exclusively task-produced alternate output roots, `tests/Antiphon.Tests/bin-g28-base/` and `bin-g28-fixed/`, were removed after drivers exited. A final directory search found no `bin-g28-*` residue. Generated evidence stays ignored.

Actual repair build command, from this checkout:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g28-fixed-build -SlotWaitMinutes 3 -- dotnet build tests/Antiphon.Tests --property:OutputPath=/work/worktrees/task-6d1c77a9/tests/Antiphon.Tests/bin-g28-fixed/ --property:ArtifactsPath=/work/worktrees/task-6d1c77a9/tests/Antiphon.Tests/bin-g28-fixed/artifacts --property:UseArtifactsOutput=true --property:UseAppHost=false --nologo
```

Repair test command (fresh results directory required):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g28-fixed-tests -SlotWaitMinutes 3 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=/work/worktrees/task-6d1c77a9/tests/Antiphon.Tests/bin-g28-fixed/ --property:ArtifactsPath=/work/worktrees/task-6d1c77a9/tests/Antiphon.Tests/bin-g28-fixed/artifacts --property:UseArtifactsOutput=true --property:UseAppHost=false -- --treenode-filter '/*/*/(SessionRuntimeActionRoutingTests*)|(ChannelBridgeTests*)|(CodexStartupDeliveryTests*)/*' --report-trx --report-trx-filename fixed.trx --results-directory /work/worktrees/task-6d1c77a9/.antiphon/group28/fixed-tests
```

The baseline command used `bin-g28-base`, `base-tests/base.trx`, and added `|(ChannelInboundRecoveryTests*)` to the same class OR. Timing variables were enabled on both test invocations: `ANTIPHON_TEST_PHASE_ROOT` pointed to the corresponding timing directory; RUN_ID was `g28-base` or `g28-fixed`, GROUP_ID `28`, ATTEMPT `1`, SUITE `antiphon-suite` under the `ANTIPHON_TEST_PHASE_` prefix.

At tested tip 07732b6ba, `check-evidence-diff.ps1` over the full base..HEAD range passed with three commits, zero evidence entries, zero violations. Merge-tree against fetched master `2cc2617ead0e0a71543b11f27a9dddea076a7beb` was clean, producing tree `2f509e0c0a53bddd0f4ccd8b7333280c1fd98a79`. The report-only tip is checked separately in the final handoff; it changes no tested source.

## Native rerun owed after landing

Rebuild the candidate on the owned Windows qualification lane through the build-slot gate, then run these exact rows into fresh TRX directories using `dotnet run --project tests/Antiphon.Tests --no-build` with that build's isolated OutputPath:

1. `/*/*/SessionRuntimeActionRoutingTests*/*` (35 Linux cases).
2. `/*/*/ChannelBridgeTests*/*` (40 Linux cases).
3. `/*/*/ChannelInboundRecoveryTests*/*` (41 Linux cases; reported native failure not reproduced).
4. `/*/*/CodexStartupDeliveryTests*/*` (62 Linux cases).

The native executed roster is authoritative. Do not use the still-owned qualification supervisor or its lock for an independent rerun. No native Windows result is claimed here.

--- next stage ---
next: land
handoff: Land the pushed test-only branch under the operator's no-Review instruction, then rerun the four native rows above. Separately triage the inherited checkpoint output-inventory size-limit refusal; its failed receipt remains failed.
artifact: /work/worktrees/task-6d1c77a9/.antiphon/task-6d1c77a9.md
