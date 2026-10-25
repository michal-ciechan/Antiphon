# db2c2088 Windows PTY/argv Debug

Task `840def81` pushed one reproduced product repair, fixture repairs and native diagnostics. The Linux selection has **104 passed, 53 skipped, 1 failed**; the failing method then **passed alone**. Native qualification remains pending. This report does not credit skipped native rows or replace the Windows qualification.

## Source and custody

- Assigned branch: `feat/card-task-840def81`; Linux worktree `/work/worktrees/task-840def81`.
- Fetched `origin/master` at start: `e9703a6334df49f87490a0e67c1e5ac124f01aad`, already the assigned branch base. No branch switch, rebase, reset, merge or native execution.
- Recorded Windows evidence: run `db2c2088f3a4454ca48c0bee60c3f53a`, candidate `a13e544e2baea214376944129a66ec4b525fafce`, extraction on `origin/feat/card-task-d805bdeb`, `docs/investigations/2026-10-25-rc-db2c2088-residual-failures.md`.
- The desktop qualification, its processes, locks and build slots were untouched.
- Code/test verification source: `2ebc5bd494bb03aaca63629171cc2485c370b17a`. This report is a later documentation-only commit.

## Repairs

### Product: Windows working-directory admission

`PhoneHomeCommandDispatcher.IsAdmittedCwd` previously compared a slash-only, ordinal prefix against paths produced by the native Windows filesystem. It rejected real Windows mirrors and accepted a nested Windows path containing a backslash in what it treated as a single name.

The dispatcher now delegates the same lexical check to `RunnerWorkspaceCwd`, selecting Windows semantics with `OperatingSystem.IsWindows()`. Windows compares case-insensitively after separator normalization; traversal and nested paths remain refused. Alternate streams and trailing dot/space aliases in the worktree name refuse. Unix retains its ordinal slash semantics. This is the only product behavior change.

The original check was extracted without behavioral change in `7d4f0d62d56dbba2fc20db5d6eaf394f76f281e3`, together with committed Windows-string test vectors. A gated PowerShell `Add-Type` witness compiled that committed helper source, using the exact 21 Windows argument vectors from `RunnerWorkspaceCwdWindowsTests`. It required no project graph build or native API. Six assertions failed. After `634f5fb22bfb791cfa9f6f8c328d6bda8fac2265`, all 21 passed. The final TUnit selection additionally passed all 27 Windows/Unix cases.

Essential retained red output, source `7d4f0d62d56dbba2fc20db5d6eaf394f76f281e3`:

```text
CWD WITNESS: 21 Windows string cases; 6 failed
root=C:\work\ cwd=C:\work expected=True actual=False
root=C:\work cwd=C:\work\worktrees\task-deadbeef expected=True actual=False
root=C:\work cwd=C:/work/worktrees/task-deadbeef expected=True actual=False
root=C:\work cwd=c:\WORK\WORKTREES\task-deadbeef expected=True actual=False
root=C:\work cwd=C:\work/worktrees/task\child expected=False actual=True
root=\\server\share\work cwd=\\SERVER\share\work\worktrees\task expected=True actual=False
```

Essential green output, source `2ebc5bd494bb03aaca63629171cc2485c370b17a`:

```text
CWD WITNESS: 21 Windows string cases; 0 failed
```

The source-compilation checks were also repeated into `cwd-red.log` and `cwd-green.log` under the task evidence directory. The red check exited 1 on assertion mismatches; the green check exited 0. They were not fixture/build errors or zero-test runs.

### Fixtures

Commit `2ebc5bd494bb03aaca63629171cc2485c370b17a`:

- `DelegateLaunchArgvIntegrityTests`: await provider disposal in the three recorded failing Windows methods. A cross-platform harness test resolves the async-only `CardSiblingInspectionQueue` and disposes its provider asynchronously. All existing argv assertions remain.
- `OrchestratorInstructionsRendererTests`: count line content independently of CRLF terminators. Both affected tests now force LF and CRLF on every host; byte limits, truncation and content assertions remain. Renderer product code is unchanged.
- `PhoneHomeTestHost`: when no explicit platform is requested, register the supplied capabilities' platform. An explicit mismatch still reaches the real 409 refusal. Wire witnesses pass for Windows and Linux, and the explicit mismatch still refuses.
- `UnixDelegateLaunchArgvTests`: skip on Windows, where the Unix PTY witness cannot run. Its two real Unix argv tests pass on Linux.
- `ChannelOutboundComposedTransportTests`: stage `Fixtures/Card0418` beside the assembly and resolve it there. The full composed broker/gateway/FakeSlack test passes, including its existing PDF hash assertion.

### Diagnostics

`NativePtyDiagnostics` captures test-owned sessions before teardown or after output waits expire. It logs session state/exit code, generation, sequence, dialog observation including input-history provenance, bounded raw/rendered tails and child handle count. Receipt-plumbing additionally captures forwarded writes and the bounded host-log tail, including backend and host errors. Observation failures are logged and do not replace receipt assertions. Each capture has a two-second cancellation bound.

Instrumentation covers receipt plumbing, remote-control recovery, Claude queue cleanup/output waits, Grok output waits, brief-ceiling cleanup and Grok delegate cleanup. `CodexCliDeliveryWorld` additionally logs framed remote refusals (`REMOTE_FIXTURE_REFUSAL`, operation/code/detail) before a missing terminal can mask their cause. Diagnostics compile; their native paths were not executed on Linux.

## Verification and performance

Exactly one isolated project graph build was run through the host gate:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug840-isolated-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-debug840/ --nologo
```

Observed: **0 errors, 1,196 warnings, 4m57.12s**, slot wait 0s, lease held 298s, granted max CPU count 6. The build wrapper lease log is retained; the full compiler output was observed in the task terminal rather than retained by that PowerShell redirection. The test DLL was 68,719,616 bytes, so the brief's direct gated `--no-build` path was used instead of the checkpoint runner.

The 18 named classes below were selected together, using the documented TUnit parenthesized OR operands with a suffix wildcard on each class. One invocation of `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-debug840/` ran under `scripts/build-slot.ps1`, with `--report-trx --report-trx-filename assigned.trx --results-directory .antiphon/840def81/assigned`.

| Class | Passed | Failed | Skipped |
|---|---:|---:|---:|
| ChannelOutboundComposedTransportTests | 1 | 0 | 0 |
| CodexCliObservationGapTests | 7 | 0 | 0 |
| CodexCliObservationTests | 8 | 0 | 0 |
| DelegateLaunchArgvIntegrityTests | 1 | 0 | 7 |
| DelegationBriefCeilingPtyTests | 1 | 0 | 2 |
| GrokDelegateEndToEndTests | 0 | 0 | 6 |
| GrokSignInRuntimeTests | 0 | 0 | 1 |
| HerdrAlwaysOnChannelParityTests | 19 | 0 | 6 |
| OrchestratorInstructionsRendererTests | 12 | 0 | 0 |
| PhoneHomeBackpressureTests | 5 | 1 | 0 |
| PhoneHomeFixturePlatformTests | 3 | 0 | 0 |
| RemoteControlRecoveryPtyIntegrationTests | 6 | 0 | 2 |
| RunnerCodexCliEvidenceTests | 6 | 0 | 0 |
| RunnerWorkspaceCwdWindowsTests | 27 | 0 | 0 |
| SessionMessageQueueGrokPtyIntegrationTests | 0 | 0 | 5 |
| SessionMessageQueuePtyIntegrationTests | 0 | 0 | 8 |
| SessionQueueReceiptPlumbingTests | 6 | 0 | 16 |
| UnixDelegateLaunchArgvTests | 2 | 0 | 0 |
| **Total** | **104** | **1** | **53** |

Batch outcome: exit 2, **12m51.197s** test duration, slot wait 0s, lease held 776s. Host: shared server2 Linux/Debian 12, .NET 9.0.21, SDK 10.0.402; other test/build drivers were active. These are individual observed samples, not p95 estimates. No production/test timeout was increased. TRX retains exact method membership and per-test durations; container/readiness/migration/clone and external-call phase attribution were not enabled for this direct invocation and remain unknown.

The one failure was `PhoneHomeBackpressureTests.Finite_stall_preserves_connection_order_and_delivery`, at `PhoneHomeBackpressureTests.cs:69`: the split mention did not reach the recipient queue within its existing bound. Duration **18.9757446s**. The exact same compiled method was then run alone through the gate:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug840-backpressure-isolated -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-debug840/ -- --treenode-filter '/*/*/PhoneHomeBackpressureTests/Finite_stall_preserves_connection_order_and_delivery' --report-trx --report-trx-filename isolated.trx --results-directory .antiphon/840def81/backpressure-isolated
```

Isolated outcome: **1 passed, 0 failed, 0 skipped**, exit 0; body **10.6818877s**, run **26.229s**, slot wait 0s, lease held 30s. This establishes an intermittent selection failure, not its cause. The initial red is preserved. There was no base-commit project rebuild under the one-build brief, so neither introduced nor inherited attribution is claimed. The mentioned routing code/test body was not repaired or its assertion weakened.

No Unit-lane sweep, native test, tag, release command or broad assembly sweep was run. All selected test execution used the committed `2ebc5bd494bb03aaca63629171cc2485c370b17a` output. Alternate `bin-debug840` outputs are removed before settlement. The evidence-history guard over base..code tip reported 3 commits, 0 evidence entries, 0 violations. `git merge-tree --write-tree origin/master 2ebc5bd494bb03aaca63629171cc2485c370b17a` was clean, tree `3ef6b41e4592100f65172ebc2da93daac0860b9a`; final branch publication is checked separately at settlement.

## Remaining native rows and exact diagnostic handoff

Native work belongs to the Windows qualification owner after its lane is available. Build Windows outputs there, then run the exact affected class or method with `--no-build` through that host's gate; preserve stdout, stderr and fresh TRX. The Linux outputs cannot be used for native credit.

1. **DelegateLaunchArgvIntegrityTests:** rerun the three dispatcher matrix/hop methods after async disposal. The recorded disposal exception is repaired, but any body assertion it masked is still a native question. Retain exact failing vector and argv lengths/indexes if any parser assertion fails.
2. **CodexCliObservationTests / CodexCliObservationGapTests:** rerun their remote vectors. Retain `REMOTE_FIXTURE_REFUSAL`, session status/failure reason and the exact allowed/launch cwd separator forms. Linux's 15 passing rows and forced Windows strings establish separate facts; actual native launch/delivery remains pending.
3. **PhoneHomeBackpressureTests / RunnerCodexCliEvidenceTests:** Windows capabilities now have a matching registration platform. Retain registration HTTP status/problem code plus registration and capabilities platform, without logging the registration secret. The unrelated Linux split-mention failure/pass pair above stays visible; if pursued, capture `MentionRouteDiagnostics` stages before cleanup and verify a failure at the base before assigning inherited attribution.
4. **SessionQueueReceiptPlumbingTests (16 native rows), SessionMessageQueuePtyIntegrationTests, RemoteControlRecoveryPtyIntegrationTests:** retain `PTY_DIAGNOSTIC`, forwarded write order, dialog state/kind/input-history provenance, generation/sequence/revision, child/host exit codes, raw/rendered tails, child JSONL and host backend log. Distinguish a pre-write guard hold from a body/Enter delivery loss. The old Claude fixtures often render only a ready banner; the current guard requires readable composer evidence. `ANTIPHON_FAKE_CLAUDE_COMPOSER` is already an opt-in fake feature used by the standing-recovery fixtures. Whether enabling it preserves these suites' raw-output assertions needs a native control; no assertion or guard was bypassed here.
5. **HerdrAlwaysOnChannelParityTests:** preserve failed session reason and queued-row hold/verdict alongside the snapshot. The current documented guard reports Unknown for Herdr without complete input custody; its recorded `observation-unknown` must not be relabeled a proven PTY write loss. The two failing channel/death arms still need native execution.
6. **SessionMessageQueueGrokPtyIntegrationTests / GrokDelegateEndToEndTests / DelegationBriefCeilingPtyTests:** capture backend, session status/exit, full recipient JSONL, body-versus-Enter write sequence and bounded raw/rendered output before teardown. The new diagnostics preserve these observations; the recorded native read/submit mechanisms are not established by Linux skips.
7. **GrokSignInRuntimeTests:** the exact method itself is Windows-skipped on Linux, despite the extraction's provisional Linux routing. The stored sign-in detection succeeded before queue-idle cancellation. At that cancellation capture the failed session's launch block, live child/host exit state, adapter exit-task status, launch queue pending-task states, and bounded host stdout/stderr before disposing the harness. This distinguishes authentication detection from post-failure shutdown or queue completion. No speculative queue fix was made.

For any new native deletion/access failure, retain the failing operation, owned path, exception/HResult, attributes and ACL owner/access entries plus the owned child/host identities and handle count; capture lock-owner evidence through the repository's owned diagnostics helper before teardown removes the evidence. Such a refusal is separate from prompt delivery and is not repaired by changing an assertion or retry deadline.

Generated evidence remains ignored at `/work/worktrees/task-840def81/.antiphon/840def81/`: `assigned.log`, `assigned/assigned.trx`, `backpressure-isolated.log`, `backpressure-isolated/isolated.trx`, `cwd-red.log`, `cwd-green.log`, and the local witness/selection scripts. Essential counts, outcomes, source identities and red/green lines are preserved in this committed report for worktree retirement.

--- next stage ---
next: land
handoff: Publish the verified repair commits under the commissioned Debug lane; preserve the batch-red/isolated-pass record and all native rows as pending qualification. This is not a clean Windows qualification receipt.
artifact: docs/investigations/2026-10-25-db2c2088-windows-pty-argv-debug.md
