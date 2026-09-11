# CARD-0478: Incomplete native custody Code checkpoint

Date: 2026-09-11. Code task: `8d562a1a`. Base: `11b6c7ff`.
Branch: `feat/card-task-8d562a1a`.

**S1-S5 is not implemented. This checkpoint is not a release candidate.**
The supported Windows native foundation works in the selected tests, but it
does not provide a runner-owned, generation-bound durable receipt or safe
verification cleanup. There is no new native/platform blocker to return to Plan.
The cross-process persistence, server integration and full ordinary verification
remain unfinished. Continue Code before ordinary Review, landing or deployment.

The authorities remain the [original plan](../superpowers/plans/2026-09-10-card-0478-mutation-after-land-plan.md)
and [runner-custody amendment](../superpowers/plans/2026-09-10-card-0478-runner-custody-amendment.md).
All **230 positive controls remain pending**. No compiling production defect was
applied and no deliberate positive control was executed by this Code task.

## Included implementation

- `ModernConPtyConnection` has an opt-in tracked spawn. It places the unnamed,
  non-inherited kill-on-close job in `PROC_THREAD_ATTRIBUTE_JOB_LIST`, creates
  the provider suspended, checks membership and actual job limits, invokes the
  Tracking journal callback and only then resumes. Attribute values remain
  allocated until their attribute list is destroyed. Failure disposes the owned
  native resources without running a fallback provider.
- The original retained job supplies real `ActiveProcesses` accounting. Kill
  success/error is separate from observations. Console/output drain can retain
  the job handle for a final accounting query. The ordinary unsourced path
  retains its existing run-then-assign behavior.
- `PtyAgentRunner.StartTrackedAsync` requires the resolved modern backend. One
  tracked attempt consumes the runner instance even if startup fails. Root exit
  closes input; a fast exit is replayed after subscription. Explicit observation
  invokes the seal callback under the input gate before querying. Nonempty jobs
  stay nonempty; canceled observation never returns an empty result. The owned
  output drain continues after caller cancellation; canceled/failed output reads
  cannot certify successful drain. Late resize after console close is rejected.
- `IPtyCustodyJournal` is only a host I/O contract, **not a persistence
  implementation**. Its test implementations are in-memory callbacks.
  `PtyCustodyObservation` is a local observation, **not a custody receipt or
  deletion authority**. The native I/O seam covers membership, configuration,
  accounting, resume and termination; native creation/storage fault seams still
  need extending for the complete amendment.
- A compiled test peer creates root/middle/leaf processes with named-event
  barriers and relative child command lines. Its ordinary, detached and new
  console descendants remain counted after ancestors exit; an explicit
  breakaway is denied. The fixture uses no paid provider or external executor.

No source selector, HTTP endpoint, runner capability, host wire message,
migration, cleanup permission or active stage reorder is enabled by this diff.

## Ordinary evidence

Windows `10.0.19045`, x64, .NET `9.0.16`, actual shipped modern ConPTY backend.
Assemblies run sequentially; process tests use the PTY assembly's own limiter.
Both commands use isolated output and disable compiler/build-server reuse.

```powershell
$env:MSBUILDDISABLENODEREUSE='1'
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c478-native/ --property:UseSharedCompilation=false -- --treenode-filter '/*/*/(PtyCustodyTests*)|(PtyKillProcessTreeTests*)|(PtyBackendContractTests*)|(ModernPtyDa1Tests*)|(PtyBracketedPasteContractTests*)/*' --report-trx --report-trx-filename native-and-compatibility.trx --results-directory .antiphon/c478-final-native-2
dotnet run --project tests/Antiphon.PtyHost.Tests --property:OutputPath=bin-c478-native/ --property:UseSharedCompilation=false -- --treenode-filter '/*/*/HostSessionPipeTests/*' --report-trx --report-trx-filename host-compatibility.trx --results-directory .antiphon/c478-final-host-2
```

Native result: **43 passed, 0 failed, 0 skipped**. This is 23 new custody cases
and 20 existing backend, kill-tree, DA1 and paste compatibility cases. The build
reported the existing `GrokSubmitWhileWorkingCanaryTests.cs:76` nullable warning.
Final host compatibility result: **7 passed, 0 failed, 0 skipped**. Across the
two final runs: **50 passed, 0 failed, 0 skipped**. No compiled custody test
helper remained running after the runs.

Logs and TRX files are under the indicated result directories in
`C:\Antiphon\worktrees\card-task-8d562a1a`.
Earlier development runs exposed and corrected fixture command quoting, an exact
process-count assumption and ambiguous C# type imports. In particular, a new
console descendant can add a console host to the job: assert that the live
descendant prevents zero, rather than assuming exactly three active processes.

These results are **partial S3a / V-14 evidence**. They do not satisfy all of
V-14, R-13 or their exact planned method/variant inventory. G-numbered test names
in this checkpoint do not claim completion of the entire corresponding row.
The tests have no host producer receipt, runner accepted receipt, importer or
real removal path. V-13/V-15/V-16/V-17 and the remaining original V/R are pending.

## Continuation required

1. Complete S3a's first-instruction/creation fault oracles, durable Tracking
   boundary, real nested host intermediary fallback, producer seal tests and
   remaining native failure arms. Add the required independently supervised
   process-crash fixture; current named-event helpers alone are not V-14/V-15
   crash evidence. Align every exact ordinary guard method/variant with the plan.
2. Implement S1/S2: confirmed publication admission with same-operation
   serialization; same authorized project/repository/board and distinct companion;
   restricted source FK and detail/CLI parity; actual snapshot creation at L;
   immutable reservation binding B with microsecond generation and append-only
   execution rows; CLI-generated EF migration. Fence every launch, recovery,
   retry and queue boundary. Prevent Mutation autosave, merge, land and pooling
   in both callers and lower services.
3. Implement S3b: host/runner stores, immutable producer/accepted receipts with
   Flush(true)/atomic rename, store/host/container identity, handshake and
   capability negotiation, execution replay and SessionId fencing, startup
   recovery/adoption, seal/read routes, observation independent of root exit,
   no-start proof and explicit Unknown/Unsupported outcomes. Keep host CWD,
   plumbing, audit and receipt files outside the snapshot. Root-exit shutdown
   must retain the observer until durable receipt import or truthful loss.
4. Implement S3c: durable task seal freezing all attempts under admission locks,
   runner receipt import, fresh context evidence reader, exact creation/source
   coordinates and guarded verification-only cleanup. Recheck sealed authority
   before the first output deletion and final Git removal. Preserve unknown
   attempts, residue, external evidence and receipts. Cleanup cannot kill.
5. Complete S4's active bundles/owners/CLI and S5's ordinary application,
   transport, crash/restart, card lifecycle and real receipt-to-removal tests.
   Run all original and amended ordinary V/R. Preserve all 230 PCs as pending.

An **unverified application draft**, restored out of the working tree before
this checkpoint, remains at:

`C:\Antiphon\worktrees\card-task-8d562a1a\.antiphon\task-8d562a1a-application-unfinished.patch`

`git apply --check` passed against this checkpoint's application files. It is
not built or tested and must not be applied wholesale as completed work. It
contains tentative admission/snapshot/no-publication and binding/entity ideas;
it omits the migration, durable host/runner custody, cleanup and all-path launch
fencing. Its CLI cleanup selector has no endpoint. The obsolete worker-manifest
cleanup reader from task `62229bff` is not included. The patch is an ignored
local recovery artifact, not part of the commit.

## Release boundary

Do not land or deploy this checkpoint alone. After complete Code and ordinary
Review, the caller records the companion verification obligation, lands the
original Code owner and deploys the **complete server/runner/host feature** from
the canonical main checkout. Verify loaded selector, bundles and actual custody
capability/tracking directly. Only then commission CARD-0478's 230 pending PCs
in a fresh SourceLanding Mutation worktree at the confirmed L. This task made
no live stack, card, dispatch or deployment changes.
