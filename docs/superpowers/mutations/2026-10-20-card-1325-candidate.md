# CARD-1325 unbound mutation candidate

Original Code / landing owner: `2029a2bb-4b42-4d99-9785-fcd8ac617e99`.
Branch: `feat/card-task-2029a2bb`. Target: master only. Restart: none.
Plan: [nightly diagnostic follow-ups](../plans/2026-10-19-card-1325-nightly-diagnostic-follow-ups-plan.md).
There is no bound landing SHA or publication operation yet. Ordinary Review
must precede landing and SourceLanding Mutation. This file binds inputs and
implementation sites, not executed positive controls.

## Bound seams and payloads

`scripts/lib/nightly-coverage.ps1` now uses:

- `Get-NightlyDiagnosticStateProperty`: the TestNode.Properties envelope,
  complete identifier/optional-location transition, and second-candidate refusal.
  Its selected header is shared by classification and message extraction through
  `ConvertFrom-NightlyDiagnosticLog` and `ConvertFrom-NightlyDiagnosticRecord`.
- `Get-NightlyDiagnosticFailureMessage`: state-specific suffix removal,
  enumeration of every exception delimiter, and exactly-one-candidate admission.
- `Test-NightlyDiagnosticExceptionTail`: null, ordinary exception/stack shape,
  and ordinal TUnit echo agreement after the literal `[Test Failure] ` prefix.

The executable raw-input recipes and independent literal oracles are in
`scripts/fixtures/nightly/c1325-diagnostic-records.ps1`. The original
`d13-error-pair.ps1` remains unchanged. E02/E03 include a literal continuation
line so CRLF normalization has a discriminating exact-message oracle; S01/S09
likewise include a continuation line. The case and assertion inventories remain
22 EB rows, 27 SB rows, and the original six classification rows.

E11's LF raw record has two valid exception delimiter offsets: **3916** and
**4001**, body-relative **19** and **104**. The first explanation is
`[Test Failure] boom`. Its echo is followed by LF plus `   at Fixture.Frame()`;
the remaining delimiter-looking text is indistinguishable from stack text.
The second explanation is the complete literal
`[Test Failure] boom, Exception = TUnit.Engine.Exceptions.TestFailedException: boom`
followed by LF and `   at Fixture.Frame()`. Its complete echo precedes the
original captured stack. The third delimiter (4064/body 167) does not agree
with its proposed explanation. Both valid interpretations use the supported
echo/stack shape. The oracle is Failed with no Message property. PC-11 targets
the exactly-one-candidate admission, not a malformed-echo substitute.

`Get-C1325AmbiguousStateRecord` inserts a complete competing transition only
inside the exception stack. Identifier offsets are **3268** and **6557**;
state-header offsets are **3852** (Error) and **7066** (Passed). Both identifiers
name the original class/method; the original UID and message echo remain intact.
Interpretation one treats the inserted transition as exception text; the other
treats the final transition as the actual state with earlier material as free
text. The oracle is the original identity, Failed and no Message. PC-10 targets
the selector's second-candidate refusal. An ordinary quoted header is not used
as this ambiguity control.

Raw materializations and offsets are retained under
`/work/worktrees/task-2029a2bb/.antiphon/c1325-evidence/` as `E11.raw.txt`,
`E11-candidates.json`, `ambiguous-state.raw.txt`, and
`ambiguous-state-candidates.json` (ignored evidence).

## Bounded producer observation P-1

Ignored supervisor:
`/work/worktrees/task-2029a2bb/.antiphon/c1325-probe/run-probes.ps1`.
SHA-256: `9a903ba73bf477889b3b6436f1b000b51ed6e1bfa7861d3ff1bcd751b868e09c`.
Scratch source, project and Directory.Build hashes are in
`.antiphon/c1325-evidence/probe-input-hashes.txt`; complete source, raw
diagnostics, TRX and custody receipts remain under `.antiphon/c1325-probe/`.
The repository's actual isolated output layout places Probe.dll directly in
`bin-c1325-probe/`; the supervisor uses that observed path.

The supervisor uses literal ProcessStartInfo.ArgumentList, observes only its
own dotnet descendant identities, and bounds root exit plus EOF to 30 seconds.
Expiry kills the owned tree and joins the root and observed descendants under
a further five-second cleanup deadline; failed join/drain stops the probes.
This is observation tooling, not production custody qualification.

Build and observation each took a granted host slot, waited 0 seconds, and
finished inside the ten-minute preparation/build/observation cap. Resolved
packages and native headers are TUnit 1.44.0 and MTP 2.2.2. Each exact method
executed once, failed intentionally, and exited 2, with zero passes/skips:

| Method / trigger | Seconds | Observed terminal property | Cleanup |
|---|---:|---|---|
| ErrorEcho: thrown delimiter-bearing Exception | 2.044 | Error | root joined, both streams drained, no observed descendants |
| TimeoutProbe: Timeout(25), bounded 5-second token-aware delay | 1.663 | Error | root joined, both streams drained, no observed descendants |
| CancellationProbe: OperationCanceledException with cancelled local token | 1.911 | Error | root joined, both streams drained, no observed descendants |

No process expired. Timeout and Cancelled remain unavailable real producer
captures; their fixtures remain explicitly reflected shapes. No live Windmill
job or release authority was changed. The existing authority script-digest
mismatch remains correct for old bound bytes.

## Ordinary evidence and pending controls

CP-1 at `dc8f8ffb53e27a6d88cff5123e1144b17d1f7a4a`: intended red,
1 executed/failed, E01 message decisive; 16/22 harness rows passed, 6 failed.
CP-2 at `be1735995caa58921e5e6b4aea61970f966a1e0e`: 5/5 methods and
45 harness rows green. Its first run had a cleanup refusal from the retained
CP-1 output. After deletion, the single authorized rerun
`20261020-014629-15c8` exited 0 and its receipt validated for that exact SHA.
The refused historical run `20261020-013634-1f2a` remains preserved.
CP-3 at `70616d55fe9a5de518e72096391ac48397938793`: intended red,
1 executed/failed, S01 identity/message decisive; 8/27 harness rows passed,
19 failed. Its task-owned red output was removed before CP-4.
CP-4 qualification belongs to the committed implementation tip and its generated
receipt, recorded in `.antiphon/task-2029a2bb.md`; this pre-run candidate does
not claim its outcome.

The first CP-4 run, `20261020-020332-7991`, tested
`2c0dfd011d72d4292ca92f189cc8efd96bd42655`: 8 methods passed, 1 failed.
The introduced regression was the existing `C1044 partial cannot borrow identity`
row: the new selector reported missing state instead of the specific truncated
identity refusal. The repair validates identity solely to refine that discovery
refusal, then still refuses; it cannot select/admit a state from free text.
No assertion or timeout changed. The final CP-4 run repeats the full two-class
selection at the repaired committed SHA; retain the failed run and its costs.

Each variant below is **pending**, with zero deliberate mutants run by Code.
Use the exact method and decisive assertion in the plan's PC table; SourceLanding
Mutation owns baseline/red/restore/fresh-green and missing-control discovery.

| Controls (one variant each) | Status |
|---|---|
| PC-1, PC-2, PC-3, PC-4, PC-5 | pending |
| PC-6, PC-7, PC-8, PC-9, PC-10, PC-11 | pending; new sites and ambiguity payloads bound above |
| PC-12, PC-13, PC-14, PC-15, PC-16, PC-17 | pending |
| PC-18, PC-19, PC-20, PC-21, PC-22, PC-23 | pending |
| PC-24, PC-25, PC-26, PC-27, PC-28, PC-29, PC-30, PC-31 | pending |

Mutation's commissioning estimate remains the plan's 300 minutes. No ordinary
green run, local producer failure, or nightly result discharges a PC.
