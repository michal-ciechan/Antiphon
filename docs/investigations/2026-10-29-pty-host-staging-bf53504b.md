# Windows pty-host staging investigation

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
were not changed. E2E cold-start verification follows on the same product source.

--- next stage ---
next: review
handoff: Review the final staging repair and native evidence; original handle-owner attribution remains limited by retained logs.
artifact: docs/investigations/2026-10-29-pty-host-staging-bf53504b.md
