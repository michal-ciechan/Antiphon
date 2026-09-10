# CARD-0461 Code: preview/refusal increment; guarded disposal blocked

Code task: `a5e37919`. Retained worktree:
`C:\Antiphon\worktrees\card-task-a5e37919`.
Branch: `feat/card-task-a5e37919`.
Implementation/test tip: `293bae34f8e47feb9af161d8710df2e7ef15d62c`
(later commits may add evidence only).
The verification design commit `9a913f012785728a6a75146adc54b3aa75293520`
was already an ancestor at dispatch; no cherry-pick or landing was needed here.

This is **not completed CARD-0461**, is not ready for the full Mutation battery,
and does not establish safe pane disposal. The plan explicitly permits only
truthful preview/refusal until Herdr supplies the guarded teardown prerequisite.
No executable path or simulated guarded-close implementation was substituted.

## Backend prerequisite evidence

Read-only local inspection on 2026-09-10:

| Fact | Measured result |
|---|---|
| CLI path | `C:\Users\lndco\AppData\Local\Programs\Herdr\bin\herdr.exe` |
| `herdr --version` | `herdr 0.8.2` |
| `herdr api schema --json` | protocol `20`, schema version `1` |
| `PaneTarget` | required/properties only `pane_id: string` |
| Guarded-close method in that schema | absent |
| CLI SHA-256 | `467682CDB5FA482C54897C6B9C96A5300A3516A72EF70EC3D009E16FADB131B3` |

The captured schema is retained at
`C:\Antiphon\worktrees\card-task-a5e37919\.antiphon\c461-herdr-schema.json`.
This measures the installed CLI, **not a running production daemon**. Existing
Antiphon `HerdrClient` validates protocol 20 and wraps unconditional `pane.close`.
No authorized guarded Herdr source/build or process-tree fence instrumentation
was supplied in the brief. No live pane, provider home, shared runner or stack
was touched to establish this evidence.

The backend owner must supply an authorized source checkout and isolated build,
supported protocol/instance/incarnation/receipt contract, auditable platform fence
against external process starts, and the deterministic V-9 race instrumentation.
This Antiphon worktree does not authorize editing another existing checkout.
A fake RPC mutex or a second process snapshot cannot discharge this prerequisite.

## Delivered increment

- Independent runner and server preview/attempt/status endpoints; typed contracts,
  client capability gate and receipt-preserving Problem Details mapping.
- Read-only exact-pane preview: full expected UUID inputs, placement labels,
  foreground executable basenames/PIDs, all readable matching runtime/pending/
  placement/sidecar/last-pane claims, optional token UUID, 256-entry/two-minute
  preview store. Always ineligible, guard unavailable and inventory incomplete.
- Valid attempts produce atomic durable `Refused` receipts with HTTP 409
  `herdr_disposal_guard_unavailable`. Identical operation retries survive service
  recreation; changed payloads conflict. Unknown/expired previews do not imply
  absence. No Herdr RPC is issued during execution, no PID is killed, and no
  locator/history/session/owner state is modified. Receipt reasons are hashed
  into the request fingerprint, not logged or persisted as raw text.
- ASCII `scripts/herdr-pane.ps1` with inspect/dispose/status, explicit `-Execute`,
  `-ReasonFile`, JSON errors, normal task header and no automatic disposal retry.
- Operator/API documentation explicitly marks the increment as incomplete.

Native identity resolution, exact process creation identities, complete affected
process inventory, executable classifier, backend guard negotiation, standing
launch synchronization, acquisition leases, conditional locator cleanup, durable
in-flight reconciliation and seven-day receipt retention remain unimplemented.
Readable locator claims are best-effort observations, not a complete proof. No
lease is claimed by this read/refuse-only implementation. Receipts are retained
without pruning in this increment. Both server ownership gates and the full
runner/backend implementation must precede enabling execution.

## Ordinary verification

All runs use `dotnet run --project ... --property:OutputPath=bin-c461/ --`
and exact class filters; no PC mutant was applied. New HTTP tests build isolated
server/runner route hosts on random loopback ports with a named fake backend,
without booting Program, provisioning agents, or requiring a database. Real
script tests invoke pwsh through ArgumentList with the assembly ProcessSpawnLimit.

| Evidence | Executed result | Tested commit |
|---|---|---|
| `HerdrPaneDisposalServiceTests` final | 16 passed, 0 failed, 0 skipped | `293bae34` |
| `HerdrPaneDisposalHttpWireTests` | 3 passed, 0 failed, 0 skipped | `a28cd260` |
| `HerdrPaneScriptTests` | 5 passed, 0 failed, 0 skipped | `a28cd260` |
| Existing runner R classes below | 84 passed, 0 failed, 0 skipped | `d3da3d76` |
| Existing server R classes | 52 passed, 0 failed, 0 skipped | `293bae34` |

Runner regression class counts: HerdrAdoptionSweepTests 22, HerdrAttachTests 23,
HerdrNamedTabPlacementTests 23, HerdrPaneChildKillTests 5,
HerdrPaneSidecarTests 6, HerdrPlacementCheckRouteTests 5. These preserve ordinary
Stop/detach, all existing adoption behavior and launched-kill semantics.
Server regression counts: AgentAttachHerdrTests 13,
StandingSessionSwitchConcurrencyTests 24, SessionRunnerHttpClientHerdrWireTests 15.
Final distinct executed cases: **160 passed, 0 unresolved failures, 0 skipped**.
This total is the final result per case, not one combined suite invocation.
The runner batch additionally ran the 16 new cases: 99 passed / 1 failed because
the new capacity test cast a deserialized List to an array. The test was corrected;
the full new class then passed 16/16. The earlier server build had three new-test
compilation errors (ambiguous JSON options construction and two missing namespace
references); corrected before its green 8-case execution. Neither failure is
reported as PC evidence. No production defect was hidden by a fixture skip.

TRX files, relative to the retained worktree:

- `tests/Antiphon.SessionRunner.Tests/bin-c461/TestResults/c461-preview-final.trx`
- `tests/Antiphon.SessionRunner.Tests/bin-c461/TestResults/c461-runner-regression.trx`
- `tests/Antiphon.Tests/bin-c461/TestResults/c461-http-script-fixed.trx`
- `tests/Antiphon.Tests/bin-c461/TestResults/c461-server-regression.trx`

Full logs are in `.antiphon/c461-*.log`; alternate build outputs and TRX evidence
are retained in this Code worktree. They were not cleaned or moved to a shared
checkout. No stack restart, deployment, live cleanup, card move or landing ran.

Rerun commands (from the retained worktree, sequentially):

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c461/ -- --treenode-filter '/*/*/(HerdrPaneDisposalServiceTests*)|(HerdrPaneChildKillTests*)|(HerdrAttachTests*)|(HerdrAdoptionSweepTests*)|(HerdrNamedTabPlacementTests*)|(HerdrPaneSidecarTests*)|(HerdrPlacementCheckRouteTests*)/*' --report-trx --report-trx-filename c461-runner-rerun.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c461/ -- --treenode-filter '/*/*/(HerdrPaneDisposalHttpWireTests*)|(HerdrPaneScriptTests*)|(AgentAttachHerdrTests*)|(StandingSessionSwitchConcurrencyTests*)|(SessionRunnerHttpClientHerdrWireTests*)/*' --report-trx --report-trx-filename c461-server-rerun.trx
```

## Required continuation

Full V-1 through V-9 and the 117 specifically named guard tests remain outstanding;
the increment's smaller tests do not replace them. V-9 is **unexecuted**, not a
skipped-green test. **PC-1 through PC-117: 0 executed**, as directed for Code.

Resolve the backend source/build prerequisite, then resume **Code** in this
retained worktree to finish the plan and all ordinary V/R tests. Only then
bootstrap **Mutation** against the exact completed Code SHA/worktree, carrying
original Code task `a5e37919`, the full plan, all PC-1..PC-117, both repository/
binary identities, `review-required: yes`, and eventual restart target
`server + runner + guarded Herdr`. Mandatory **Review** follows Mutation.
Do not send this incomplete increment to Mutation as if all 117 test targets exist.

Plan/verification artifact:
`docs/superpowers/plans/2026-09-10-card-0461-herdr-leftover-pane-disposal-plan.md`.
