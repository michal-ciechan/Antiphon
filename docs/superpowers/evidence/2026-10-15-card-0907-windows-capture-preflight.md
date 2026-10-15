# CARD-0907 Windows capture preflight

Outcome: blocked before C907-DESKTOP-STUB-1 launch. No capture or compatibility
qualification was performed. The required nonloopback egress denial could not be
established with the available Windows facilities under the recipe's restrictions.
The single TUI submission budget remains entirely unused.

Original Code task / landing owner: 13d64ff4-cc8e-4ceb-9f71-fae4e426a897.
Assigned branch: feat/card-task-13d64ff4.
Worktree: C:\Antiphon\worktrees\card-task-13d64ff4.
Task base: 14f077fb578e5262b5768fc3bafaa77fb7989764.
Read-only preflight date: 2026-10-15 UTC.

Plans: [parent capture and verification design](../plans/2026-10-02-card-0907-codex-0160-screens-plan.md)
and [preceding raw-parser slice](../plans/2026-10-15-card-0907-raw-parser-slice.md).
The [preceding slice's evidence](2026-10-15-card-0907-raw-parser-slice.md) is retained
unchanged; its Linux results are historical, not new Windows receipts or tests at
this task's HEAD. The brief is C:\src\Antiphon\.antiphon\task-13d64ff4-brief.md.

## Required gate and observed facilities

The parent recipe, step 1, requires an allowlisted child environment and an
outbound policy permitting only the local stub, and says: "If this isolation
cannot be established, leave capture pending." It also prohibits altering the
operator's global environment, firewall, credential store or provider home.
Step 2 explicitly says that provider redirection alone does not isolate every
network destination. The task brief requires stopping when capture needs something
the task cannot do. These requirements, rather than a new precaution, stop launch.

Read-only observations in this worktree/session:

| Observation | Actual result / limitation |
|---|---|
| Windows version | Microsoft Windows NT 10.0.19045.0 |
| Current token in elevated Administrators role | False |
| WindowsSandbox.exe command and System32 binary | Both absent |
| vmrun.exe / VBoxManage.exe commands | Both absent from command resolution; this does not prove no VM exists elsewhere |
| Hyper-V VM-management service | vmms not returned; vmcompute and hns are Running |
| hcsdiag.exe | Present; presence does not establish an isolated Windows guest |
| New-NetFirewallRule | Available command; no rule was created and no elevation was requested |
| Active firewall profiles | Domain, Private and Public enabled, all DefaultOutboundAction=Allow; profile defaults are not proof of an effective per-process rule |
| Codex launcher | codex.ps1 resolves in the installed npm launcher directory; no CLI process was started |
| Runner catalogue | Desktop advertises Windows, available, Codex 0.160.0, checked at 2026-10-15T18:08:12.0202131+01:00; this is runner-reported version evidence, not this capture's executable probe |
| Runner defaults | Global default server2, revision 2; no placement settings were changed |

GET /api/runner-defaults and GET /api/session-runners were read through the
documented API. No runner was dispatched, restarted or reconfigured. Desktop seat
occupancy was not used as proof of a network-isolation barrier.

Source inspection explains why the existing helper cannot establish that barrier:

- RealCliStubEnv.ForCodex supplies the scratch home and provider overrides, seeds
  synthetic bootstrap metadata, disables apps and selects the unelevated sandbox.
  It does not install an outbound deny policy for the interactive CLI process.
- RealCliStubProcess.RunAsync copies the inherited environment before applying its
  overlay. The recipe already prohibits using it wholesale for this capture.
- ModernConPtyConnection.Spawn calls MergeEnvironment; that method merges inherited
  variables with supplied overrides. A separate helper process with a cleared,
  allowlisted environment could address inheritance, but that would not deny
  nonloopback traffic. The production Windows launch path inspected here has no
  task-scoped AppContainer/network-isolation option.
- FakeLlmApiServer's loopback listener constrains its listening interface; it does
  not constrain the Codex client's other outbound destinations.

No assertion is made that every possible Windows isolation technology is absent.
What is missing is an available, verified task-scoped isolation mechanism compatible
with the required modern ConPTY capture and local stub, without changing shared
host policy. A proxy-only environment is not the required outbound denial.

## Required caller action

Provide an approved isolated Windows execution environment for both the capture
helper and loopback stub, with modern ConPTY support and enforced denial of all
nonloopback egress. For example, an operator-provisioned disposable Windows VM or
Sandbox with external networking disabled and the required CLI/runtime artifacts
available locally could satisfy the gate after verification. This task did not
provision or verify such an environment. Resume Code there with the same one-turn,
scratch-home, exact-model and provenance requirements. No interactive account
login, credential copying, firewall exception or extra submission is requested.

## Verification and remaining work

Builds: 0. Test invocations: 0. Executed/passed/failed/skipped tests: 0/0/0/0.
Checkpoint runs: 0; CP-1 through CP-5 remain not run. No CHECKPOINT line, TRX,
clean-source test receipt or verified build provenance exists for this task.
Build slot: slot=not-requested; waited=0s. No unlisted build/test driver, full
assembly run, Unit run, repeat-proof run or deliberate mutation was launched.
No output directories or capture processes require cleanup.

| Required ID | Actual outcome in this Windows task |
|---|---|
| C907-DESKTOP-STUB-1 | Blocked at isolation preflight; zero launches/submissions |
| V-1 | Not run; native readiness capture and replay pending |
| V-2 | Not run; modal, compaction and lifecycle fixtures/tests pending |
| V-3 | Not run; ready/submit adapter additions pending |
| V-4 | Not run; captured rollout/completion and lifecycle additions pending |
| V-5 | Not run on Windows; prior Linux raw-parser results remain attributed to their original SHA |
| V-6 | Not run; vendor wall/classifier/error additions pending |
| V-7 | Not run; real scratch TUI JSONL, normalizer and tailer evidence pending |
| V-8 | Not run; normalized vendor error through hold/recovery acceptance pending |
| R-1 | Not run; full selected Pty classes pending |
| R-2 | Not run; full selected adapter/working classes pending |
| R-3 | Not run on Windows; prior Linux evidence unchanged |
| R-4 | Not run; full selected normalizer/tailer/quota classes pending |
| R-5 | Not run; full quota acceptance class pending |

The seven Windows CodexAdapterLocalShellTests cases identified by the preceding
slice also remain pending. No deferred selection is counted as passed. The Final
gate is incomplete; this is not an Interim pass or a reduction of required scope.

PC-1 pending; PC-2 pending; PC-3 pending; PC-4 pending; PC-5 pending;
PC-6 pending; PC-7 pending; PC-8 pending; PC-9 pending; PC-10 pending;
PC-11 pending; PC-12 pending. Every variant, deliberate mutant, red/restore/green
cycle and missing-control discovery remains owned by later SourceLanding Mutation.

This task changes only this narrative evidence file. No implementation, fixtures,
test selections, assertions, deadlines or prior evidence were changed. No new
product regression is demonstrated by this preflight; missing required acceptance
is a substantive incomplete gate under the D-2 advisory policy. Ordinary Review
must follow completion of implementation and ordinary V/R; no land is requested.
Restart: none for this evidence-only task. The preceding parser slice retains its
own server-restart requirement after eventual landing.
