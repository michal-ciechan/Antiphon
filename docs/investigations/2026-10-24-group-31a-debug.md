# Group 31a Linux debug and fixture repair

All 64 selected cases pass on Linux after repairing the remote phone-home terminal fixture.
No production code changed. Native Windows reruns remain the caller's qualification work.

## Source and scope

Task `d55dd41d`, branch `feat/card-task-d55dd41d`, Debian 12 / .NET 9.0.21.
The initial checkout was `0e64a6aba1b4e28052faebfac88f01232b4cb66b` and was advanced
by fast-forward to fetched master `99bd60f861eab6ffdcb98d5a4a006719ff4d97bc` before building.
No rebase, target merge commit, desktop access, native lock, release script, tag or Unit lane.
The sibling delivery-witness/receipt classes and other in-flight source areas were untouched.
Review was excluded by the operator brief.

Verified repair source: `b3b3f4e77d6ba46bd6d239304ef67ccf33ddb38b`.
Test DLL SHA-256: `d3b21af22b345fa875e384e1a56a83b8edadd1cd8b1d25f4707efab6d823a74a`.
The final documentation commit changes no executable source.

## Results and attribution

| Class | Baseline pass/fail | Repaired pass/fail | Repaired case-duration sum |
| --- | --- | --- | --- |
| SpecialistExecutionIdentityTests | 48 / 0 | 48 / 0 | 85.757 s |
| PhoneHomeImmediateSendTests | 11 / 2 | 13 / 0 | 50.621 s |
| DeliveryUnverifiedDiagnosticTests | 3 / 0 | 3 / 0 | 7.775 s |

Both runs executed 64 cases with zero skips. The current phone-home class has 13 cases,
one more than the 12 failures in the supplied Windows summary. Baseline wall time was
177.349 s; repaired wall time was 193.962 s. Per-class sums are test durations, not full
run costs. One run per source is a provisional sample, not a measured timeout recommendation.

The reported specialist disposal cluster was already fixed by
`e39ecf3ad271d22fff661c02c223d55c8138a806`; its Claude fixture setup was already fixed by
`9b40b7248b0c93911b879a6636296b0e11e05067`. The delivery diagnostic setup was already fixed
by `053b0422b4512fe442d89de12a7b197286dd80d6`. Neither class required another edit.

The two inherited failures reproduced at the clean baseline were:

- `Recovered_remote_and_local_immediate_sends_deliver_once`: missing UserPrompt receipt.
- `WhenIdle_outage_remains_accepted_and_preserves_attempts`: missing recovered UserPrompt receipt.

The phone-home outage harness already enabled local Claude observations, but the recovered
remote peer still supplied a bare composer, unrelated generation, no dialog observation and
no advertised dialog-guard capability. The guard correctly withheld automated input.
This is one CARD-0680 fixture cluster, with no Windows path-semantics cluster and no product defect.
No pure-string Windows-path witness was needed.

`PhoneHomeOutageHarness.RecoverAsync` now offers an explicit Claude-observation opt-in for
these two scenarios. A separate shared `FakeAgentProtocolAdapter`, enabled through
`WithClaudeDialogObservation`, owns the remote composer, persisted generation, revisions and
conditional-input checks. It emits the existing transcript callback on submission. Other
harness callers retain the legacy peer path. The immediate-send matrix preserves all receipt,
delivery and refusal checks and now requires six guarded writes plus zero raw writes instead
of six raw writes. Existing outage, uncertain-attempt, no-mutation and local/Herdr controls pass.

## Execution and retained evidence

All build/test drivers used `scripts/build-slot.ps1`; all leases were granted immediately.
The checkpoint inventory reader's known 64 MiB assembly limit was bypassed only as explicitly
authorized: one isolated baseline build followed by direct `dotnet run --no-build` execution.
Two subsequent builds were necessary for the fixture repair and its explicit opt-in adjustment.
Build elapsed times were 277.17 s, 192.22 s and 125.36 s; all had zero errors.
Build-slot held times were 278 s, 194 s and 126 s. Test-slot held times were 181 s and 197 s.
Builds had 600-second hard bounds and tests 900-second hard bounds, each with a five-minute
slot admission bound. These are task containment limits; no test or product timeout changed.

The same exact filter was used for baseline and repaired execution:

```text
/*/Antiphon.Tests.Application/(SpecialistExecutionIdentityTests*)|(PhoneHomeImmediateSendTests*)|(DeliveryUnverifiedDiagnosticTests*)/*
```

Command shape (PowerShell, from the checkout; fresh result directory for each invocation):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group31a-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-group31a/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group31a-tests -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-group31a/ -- --treenode-filter '/*/Antiphon.Tests.Application/(SpecialistExecutionIdentityTests*)|(PhoneHomeImmediateSendTests*)|(DeliveryUnverifiedDiagnosticTests*)/*' --report-trx --report-trx-filename group31a.trx --results-directory .antiphon/group31a-fresh
```

Private generated evidence remains ignored under
`/work/worktrees/task-d55dd41d/.antiphon/d55dd41d/`: build/test logs, baseline/baseline.trx,
fixed/fixed.trx, source and binary hashes, clean-source status, timing-baseline and timing-fixed
JSONL shards, and the producer-owned output inventory. Phase timing was explicitly enabled
with run IDs `d55dd41d-baseline` and `d55dd41d-fixed`, group `31a`, attempt `1`, suite `antiphon`.
DB fixture/clone/drop phases and test durations were retained; uninstrumented calls/phases
remain unknown or remainder, not zero. No additional instrumentation or release coverage is claimed.
All 33 task-owned `bin-d55dd41d` roots were removed after joined execution.

## Landing and native handoff

Code commits, each pushed:

- `75218754ffec61d3bbc17b45f44cecdd3f38698f`: guarded terminal peer and guarded-write assertions.
- `b3b3f4e77d6ba46bd6d239304ef67ccf33ddb38b`: opt-in limited to the two affected recovery scenarios.

`git fetch origin master` followed by `git merge-tree --write-tree origin/master HEAD`
completed without conflict against `99bd60f861eab6ffdcb98d5a4a006719ff4d97bc`.
The executable-source merge tree was `fbde0e45622650ba1fec822b0887b9a54fe410ef`.
The evidence history guard passed for both code commits with zero violations.

After landing, rerun these three native Windows class rows, or their exact combined filter
above, under the caller's existing qualification ownership:

- `/*/Antiphon.Tests.Application/SpecialistExecutionIdentityTests/*` (48 cases).
- `/*/Antiphon.Tests.Application/PhoneHomeImmediateSendTests/*` (13 cases).
- `/*/Antiphon.Tests.Application/DeliveryUnverifiedDiagnosticTests/*` (3 cases).

Linux results do not replace those native receipts. The detached Windows qualification
supervisor and its lock were never accessed or modified.
