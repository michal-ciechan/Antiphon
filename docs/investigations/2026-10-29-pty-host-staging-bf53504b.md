# Windows pty-host staging investigation

Reproduced and repaired a native Windows publication denial. The forced native
lock/release witness passed all 15 cycles, the affected class passed 14 tests
(one Linux-only skip), and six consecutive split-46 cold starts passed.

Task bf53504b starts at `5e9514d0253aad1ee3a14a5b2766fb39702bd824` on
`feat/card-task-bf53504b`. Scope: the native Windows split-46 staging failure.
The unrelated split-55 sibling-inspection timeout is outside this repair.

## Evidence and hypothesis

The c490e425 report and its retained split-46 TRX/server log show HTTP 500 at
11:32:47 UTC, reporting access denied on the unique `.copying` directory.
The runner's own output directory has already been removed, so the original
filesystem stack and handle owner are unavailable. Antivirus involvement is
unproven. Each E2E fixture owns a random runner port and unique staging root;
the production runner and canonical checkout are outside this investigation.

`EnsureCurrent` creates a GUID-qualified staging directory, copies the payload,
then publishes it with one `Directory.Move`. Its only recovery handles an
`IOException` when another publisher already created the destination. A native
Windows payload-file handle without delete sharing can instead make the rename
throw access denied on the staging directory. The red witness extracts that exact
rename into an internal method without changing behavior, acquires a real Windows file
handle, proves the native denial, and requires publication after that handle is
released. It does not inject filesystem results or exceptions.

The minimal repair retries only publication, only Windows IOException HRESULT
`0x80070005` (access denied), preserving the completed private copy and atomic rename.
Ten attempts add at most nine 100 ms waits. Other errors are immediately propagated.
Persistent errors must still throw; no caller timeout or gate assertion changes.
Concurrent staging uses unique paths; destination collision recovery remains in
`EnsureCurrent`. Cleanup is inspected separately, not blamed without evidence.

## Verification scope and budget

All build/test drivers use `scripts/build-slot.ps1`, alternate output
`bin-bf53504b/`, and fresh ignored results under
`.antiphon/task-bf53504b-evidence/`. This Custom Debug/fix task uses the brief's
minimal native selection rather than a broad Unit/E2E lane. First run the exact
new transient-handle method red, then the affected `ShadowCopyStoreTests` class
green (copy/hash/reuse/publication invariants), and split 46
`DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningWaitsForCaller` repeatedly
within the 90-minute task budget, at most 15 attempts. The staging witness itself
contains 15 native release/publication cycles.

The direct filesystem tests need no processes, database or web host. Each E2E
invocation is a deliberate cold-start/staging witness and therefore needs its
own runner and fixture; historical split-46 passing duration is 134.616 seconds
(one sample, not p95). Budget roughly 3 minutes per E2E run plus build/slot time;
stop repetition before spending 30 minutes on one batch. No existing timeout is
widened. TRX records per-test duration; opt-in phase records will be retained for
E2E, with unavailable phases explicitly unknown. Each run records source SHA,
selection, timing, platform and build identity. Native Windows is required for
the two file-handle cases; a non-Windows skip is not qualification.

## Results

Initial native probe at `cfbf06753365ba65cbee4c7cdd99fd77a0fad2ff`: build passed
(one pre-existing nullable warning), test 0 passed / 1 failed. The failure was the
fixture premise: a metadata-only (desired access zero) directory handle did not
block rename. This is not product-red evidence. The corrected probe requests
GENERIC_READ while withholding delete sharing. That second probe at
`32bef229940eede3dbe5b0c4541b058e26cbc79b` failed the expected exception premise
too: a directory read handle yields an IOException sharing violation, not access
denied. Both fixture-premise failures remain recorded and are not product-red.

A gated native probe holding a *payload file* with `File.OpenRead` reproduced
`System.IO.IOException`, HRESULT `80070005`, and the original message naming the
parent `.copying` directory. The regression now uses this real native condition.
The original handle owner remains unknown; a matching failure mechanism is
established, not antivirus or competing-runner attribution.

The corrected test at `1985698d5aa56bc2a9a17adb605097a9905e5c49` produced a valid
product red: 0 passed / 1 failed / 0 skipped, 275 ms, with the native premise
passing and `PublishStagingDirectory` throwing IOException access denied. Its
single `Directory.Move` is the unchanged base operation, extracted for direct
native testing. No failure was hidden by an assertion or timeout adjustment.

Repair `d08054d7bf35060f19336cab981870c9a6559475` passed the affected class:
14 passed / 0 failed / 1 skipped (the existing Linux execute-mode assertion).
The native transient test completed all 15 cycles in 4.123 seconds; the persistent
denial remained an IOException with the original HRESULT in 1.106 seconds.
The destination-overwrite control passed in 0.110 seconds. Green build: 0 errors,
one pre-existing nullable warning. Fresh TRX, logs and SHA-256 hashes are under
`.antiphon/task-bf53504b-evidence/green*`. Client install and rebuild also passed
through the slot gate; npm audit advisories and the existing Vite chunk warning
were not changed.

## E2E cold-start results

The E2E build and all six attempts used clean source
`3a99d4acc6d4edeace80c7f40c40fd295eb78e4a` (only the investigation narrative
changed after the native green source). The build passed with 0 errors and 39
existing warnings in 4m 50.07s; its slot was held for 292s. Native environment:
Windows 10.0.19045, win-x64, .NET 9.0.22, TUnit 1.44.0, MTP 2.2.2.

Every attempt selected exactly
`/*/*/DispatchBaseWarningDeliveryE2ETests/C508_MismatchWarningWaitsForCaller`.
The fixture checked its runner port was not 17204, used native FakeGrok, and
owned its PostgreSQL container, runner and detached hosts. No staging denial
occurred in these repetitions; no original unforced flake was observed here.

| Attempt | Passed / failed / skipped | Test seconds | Wall seconds, including admission |
|---|---|---:|---:|
| e2e-01 | 1 / 0 / 0 | 134.619 | 142.224 |
| e2e-02 | 1 / 0 / 0 | 144.423 | 152.329 |
| e2e-03 | 1 / 0 / 0 | 130.757 | 137.942 |
| e2e-04 | 1 / 0 / 0 | 144.675 | 853.166 |
| e2e-05 | 1 / 0 / 0 | 181.100 | 196.675 |
| e2e-06 | 1 / 0 / 0 | 155.467 | 233.829 |

Total: **6 passed / 0 failed / 0 skipped**. The repetition batch ran from
12:14:57 to 12:43:37 UTC (28m 40s). Its existing budget stopped further repetitions
after the sixth finished, rather than starting another beyond the 26-minute
admission cutoff. Attempt 4 waited 701s for a slot; attempt 6 waited 61s, initially
behind the memory floor. A host sample during the fourth admission wait recorded
100% CPU and 6464 MB free physical memory. One attempt to stop the still-believed
queued fourth repetition refused its ownership precondition because the test
child had already started; **that stop attempt stopped no process**, and the test passed.

These are bounded flake diagnostics, not a full rc qualification or a measured
p95. Phase recording was requested through the documented environment variables
but this E2E build emitted no phase JSONL; fixture/IO phase totals therefore
remain unknown. TRX durations, individual slot waits/holds and run wall times
are retained. Neither production nor test timeouts were widened.

## Evidence, cleanup and review limits

Absolute evidence root:
`C:\Antiphon\worktrees\card-task-bf53504b\.antiphon\task-bf53504b-evidence`.
Each `e2e-01` through `e2e-06` directory contains a fresh `result.trx`; sibling
logs retain its gated invocation. `e2e-runs.jsonl`, `e2e-results-summary.json`,
source SHA/status files, build logs, platform/load records and build hashes
preserve provenance. `red`, `red2` and `red3` preserve both invalid fixture probes
and the valid product red; none is relabeled as passing.

All nine recorded build-artifact SHA-256 hashes matched after verification.
Tracked source remained clean throughout the E2E batch. Every foreground driver
completed and released its slot. Cleanup found no worktree executable running,
verified containment and absence of reparse points, and removed all 18 exact
literal `bin-bf53504b` paths. Generated evidence remains ignored and uncommitted.
The production runner was used only as the mandated build-slot broker; it and
the canonical checkout were not restarted, reconfigured, or used for test sessions.
Existing caller authority covered the repair and task-branch pushes; no further
approval was requested. No tag, release publication, merge, rebase or force push occurred.

The repair covers the reproduced native rename failure. Because the original
runner-side stack and handle-owner evidence were deleted before this task,
attribution of the historical failure to a scanner, another process, or the exact
filesystem call remains unproved. It is not a claim that every access-denied
staging failure has this cause. Persistent denial still fails with its original
exception; unrelated errors are not retried and a competing destination is not
overwritten. The split-55 sibling-inspection issue remains outside this repair.

At the verified source, `merge-tree` is clean against RC base
`cf1a124035f45857e3e2bf205ab9f8b05cf7a8e4`. Master
`f8bddb6efda7b0e8f50f3b45744087e52188bc66` has 20 conflicts; their messages exactly
match the c490e425 report's retained list. This is not master land approval.
The evidence-history guard passed with zero violations and is rerun for the
final report commit. Initial ordinary Review is still required.

## Rerun

Rebuild the client bundle before E2E if absent; every driver takes a slot.
Use fresh result directories for both selections:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label staging-build -- dotnet build tests/Antiphon.PtyHost.Tests --property:OutputPath=bin-staging-review/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label staging-tests -- dotnet run --project tests/Antiphon.PtyHost.Tests --no-build --property:OutputPath=bin-staging-review/ -- --treenode-filter '/*/*/ShadowCopyStoreTests/*' --report-trx --report-trx-filename result.trx --results-directory .antiphon/staging-review-native
pwsh -NoProfile -File scripts/build-slot.ps1 -Label staging-e2e-build -- dotnet build tests/Antiphon.E2E --property:OutputPath=bin-staging-review/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label staging-e2e -- dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-staging-review/ -- --treenode-filter '/*/*/DispatchBaseWarningDeliveryE2ETests/C508_MismatchWarningWaitsForCaller' --report-trx --report-trx-filename result.trx --results-directory .antiphon/staging-review-e2e
```

--- next stage ---
next: review
handoff: Review the bounded Windows publication repair, valid native red, 15 forced green cycles, 14 passing affected tests and six passing cold-start E2E attempts. Original handle-owner attribution remains unresolved; master conflicts are unchanged.
artifact: docs/investigations/2026-10-29-pty-host-staging-bf53504b.md
