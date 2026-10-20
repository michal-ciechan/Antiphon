# CARD-1233 S3 unbound Mutation candidate

Original Code and landing owner: `f5131ca6-d23d-4b18-b852-19b0c5cff806`.
Branch: `feat/card-task-f5131ca6`. Target: master only. Restart: none.
Base: `196e0fb8d6e47aef8d8e2b8ce1fa3e8821aa4b62`.
Plan: [remaining bounded readers and child custody](../plans/2026-10-20-card-1233-bounded-helper-remaining-plan.md).
Inherited controls: [CARD-1199 plan](../plans/2026-10-12-card-1199-detect-report-plan.md).

This slice changes only R6's PowerShell source Git route. It loads the existing
`checkpoint-process-runner.ps1` loader and calls `RunBytes` with hardened Git and
the real 30,000 ms budget. The loader needs no change. Source framing and byte
identity, execution drivers and intentional holders retain their contracts.

All PCs and variants remain **pending and unbound**. Ordinary W3 red/green is
not a deliberate mutant or a SourceLanding cycle. Mutation owns binding to the
published source, compiling mutants, intended red, restoration, fresh green
and missing-control discovery after ordinary Review and caller landing.

## S3 candidates for binding and discovery

Use only `/*/*/CheckpointSourceGitBoundsTests/C1233_ScriptSource*`.
Preserve the 30-second Git budget, 32-second return assertion, independent
35-second watchdog and two-second descendant-death observation. The operation
clock starts after PowerShell imports; PID/start identity and nonce bind readiness.

| Candidate | Compiling defect to consider | Intended detector |
|---|---|---|
| Script source route | Restore the direct Git launch and untimed drains in `Get-CheckpointGitBytes`. | `blocked.Watchdog` must be false; the prior route requires rescue after its Git parent exits. |
| Raw-byte identity | Use the text runner or decode/re-encode the captured bytes. | Both script and tool healthy captures must match the independent fixed v1 fingerprint, including invalid UTF-8 diff bytes, NUL-delimited valid paths, CRLF and binary untracked content. |
| Descendant custody | Remove shared Linux process-group cleanup while retaining direct-child cleanup. | The recorded descendant must be dead before emergency fixture cleanup; the Git parent has already exited. |
| Error mapping | Lose timeout or nonzero-exit classification at the script adapter. | Actual source capture returns unknown/`git_timeout` or unknown/`git_exit`, respectively. |

W3 does not qualify installed-Git hardening, caller cancellation, a split
exit/drain budget or native Windows custody. Those obligations retain their
existing owners. S4-S16 remain outside this slice.

## Inherited pending PC inventory

Every variant below remains pending method-scoped SourceLanding Mutation.
Resolve exact methods and variant rosters from the inherited plan before binding.

| PC | Pending variants |
|---|---|
| PC-1 | Nine receipt verb/path pairs; remove precheck or nonblocking open. |
| PC-2 | Eighteen receipt verb/path/type pairs; remove type/link guards. |
| PC-3 | Required fields null, wrong-type or missing; element and empty variants; remove shape guards. |
| PC-4 | State/report row/build identities: empty, whitespace and null at every admitted consumer; valid controls and no-write assertions. |
| PC-5 | Tool/script tracked stamps: precheck, nonblocking-open, cap or cancellation removal; FIFO, symlink, directory, dangling and regular controls. |
| PC-6 | Import/coverage identity guards: null, missing, empty, whitespace and derived build syntax. |
| PC-7 | Malformed state crossed with verbs; exception/identity guard removal. |
| PC-8 | Project precheck/nonblocking-open bypass; tool Git, script Git, tool graph and script graph deadline/custody bypass; ordinary controls and Git config-driver discovery. |
| PC-9 | Disable Git hardening or restore direct launch in `SafeDelete.RunGit`; installed-Git tracked/untracked controls. |
| PC-10 | Separate exit/drain deadlines; remove process-group kill; healthy control. |
| PC-11 | Restore synchronous fixture drains; remove group kill; separate exit/drain limits; revert a listed fixture caller to direct launch. S15 retains actual-caller routing and split-budget sensitivity work. |

R-2 remains pending; this task performs no Windows qualification.
