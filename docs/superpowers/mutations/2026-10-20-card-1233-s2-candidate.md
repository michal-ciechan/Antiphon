# CARD-1233 S2 unbound Mutation candidate

Original Code and landing owner: `622bbe98-a431-4e9a-9f2b-00e1e869c248`.
Branch: `feat/card-task-622bbe98`. Target: master only. Restart: none.
Base: `1bf39811311a1f2f3f0ada2fde672592b6a701c4`.
Plan: [remaining bounded readers and child custody](../plans/2026-10-20-card-1233-bounded-helper-remaining-plan.md).
Inherited controls: [CARD-1199 plan](../plans/2026-10-12-card-1199-detect-report-plan.md).

This artifact covers S2 only: raw-byte capture in
`scripts/lib/CheckpointProcessRunner.cs` and the default Git route in
`tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs`.
All PCs and variants below remain **pending and unbound**. The ordinary W2
tests-first red is not a deliberate mutant or a completed SourceLanding cycle.
Mutation owns binding to the published source, compiling mutants, intended red,
restoration, fresh green and missing-control discovery after ordinary Review and
caller landing. This document confers no publication or execution authority.

## S2 candidates for binding and discovery

Use only `/*/*/CheckpointSourceGitBoundsTests/C1233_ToolSource*` for these
candidate cycles. Preserve the real 30-second Git budget, the 32-second return
assertion, the independent 35-second watchdog and the two-second descendant
death observation. Time the operation after PowerShell startup. PID, start
identity and nonce establish the probe's owned processes.

| Candidate | Compiling defect to consider | Intended detector |
|---|---|---|
| Default source route | Restore the prior direct Git launch and untimed stderr drain in `SourceSnapshot.RunGit`. | `blocked.Watchdog` must remain false; the old route requires rescue after its parent exits. |
| Raw-byte identity | Decode/re-encode stdout in `RunBytes` instead of preserving `StandardOutput.BaseStream` bytes. | The healthy fingerprint must equal the independent fixed v1 vector, including invalid UTF-8 diff bytes, NUL-delimited valid paths, CRLF and an untracked binary file. |
| Descendant custody | Remove the Linux process-group kill while preserving the direct-parent cleanup. | `blocked.DescendantDead` must be true before emergency fixture cleanup; the recorded direct parent already exited. |
| Timeout result | Return a non-timeout result on deadline expiry. | The actual default capture must return unknown with `git_timeout`. |

These are discovery candidates, not new numbered PC claims. W2 does not establish
sensitivity to a fresh drain budget after a delayed parent exit: S15/B15 retains
that obligation. W2's shim supports config inspection, but is not an installed-Git
hardening detector. Caller-cancellation and native Windows pre-assignment custody
qualification remain outside this slice. S3 still owns the PowerShell source
capture route; S5 still owns reusable execution custody.

## Inherited pending PC inventory

Every listed variant remains pending method-scoped SourceLanding Mutation.
Resolve exact methods and variant rosters from the inherited plan before binding;
do not run a class wildcard for a PC cycle.

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

R-2 and the remaining S3-S16 witnesses retain their commissioned-slice obligations.
No Windows qualification, broader fixture migration or CARD-1368-dependent work
is claimed by S2.
