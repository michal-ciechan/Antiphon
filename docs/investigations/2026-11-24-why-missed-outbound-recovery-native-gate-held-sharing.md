# Why Group 64 missed the outbound recovery fixture's marker race

Windows Full run `acca11ba` on `rc/2026-12-04` at
`6ed4cb58cd75fe386e94965165d96752452431a8` reported an `IOException` for
`antiphon-outbound-admission-<guid>\native-gate.held` in
`ChannelOutboundRecoveryTests.Running_converter_reconciles_after_dispatcher_death` after
22.2 seconds. The qualification brief supplied the exception and identified
`antiphon-all-group-64.trx` under
`C:\Antiphon\releases\candidates\rc-2026-12-04-6ed4cb58cd75\logs\2026-11-24-0922-acca11ba`.
The desktop TRX stack and handle owner were unavailable on server2. Source identifies a
pre-kill read that can explain the exception. Recursive teardown also touches the marker
and already has its own bounded delete retry.

## Introducing commit and card

CARD-0418 commit `afa7b0e6a5ca6f576e6a5621ff724684e7a33e1c` introduced the marker
and the matching test read. FakeGrok writes the delivery ID to the final `.held` path with
`File.WriteAllText`, which opens for write with `FileShare.Read` and closes before FakeGrok
waits for `.release`. The parent polls `File.Exists` and then calls `File.ReadAllTextAsync`
with default `FileShare.Read`. Windows checks sharing in both directions, so the reader
can be rejected while the writer has write access. The file can also exist before its
contents are published. [Fixture writer](../../src/Antiphon.FakeGrok/OutboundRequestMode.cs#L66)
· [.NET file implementation](https://github.com/dotnet/runtime/blob/v9.0.11/src/libraries/System.Private.CoreLib/src/System/IO/File.cs#L979)
· [Windows file-sharing rules](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)

The test verifies the marker's exact delivery ID before terminating the dispatcher
probe with `Kill(entireProcessTree: true)` and awaiting `WaitForExitAsync`. The
converter is started separately by the test runner and intentionally survives the
dispatcher cut. The gate is a fixture protocol. The server's admission stages input
under a temporary directory and moves the closed snapshot into place. Its recovery
reads the frozen reply and, after the converter task reports success, validates its
manifest and output files. An exclusive third-party handle could still interfere with
those ordinary reads. They have no equivalent marker-exists-then-read dependency on a
live or just-killed dispatcher. The fixture's live `dispatch-ready` marker is the
closest analogue and already uses the shared-read helper.
[Recovery fixture](../../tests/Antiphon.Tests/Application/ChannelOutboundRecoveryTests.cs#L685)
· [Snapshot staging](../../server/Infrastructure/Files/ChannelOutboundFileStore.cs#L123)
· [Conversion observation](../../server/Application/Services/ChannelOutboundDeliveryPump.cs#L396)

## Approving reviews

The original approving Review task and its scope are unverified. The retained CARD-0418
round 38 verification selected the recovery class as part of CP-6 and passed all 31
cases, including the running converter. CARD-1910 CP-6 later passed the exact method on
Linux with `/*/*/ChannelOutboundRecoveryTests/Running_converter_reconciles_after_dispatcher_death`.
[CARD-0418 CP-6](2026-09-30-card-0418-round-38-evidence.md#final-affected-row-receipts)
· [CARD-1910 receipts](../superpowers/reports/2026-11-14-card-1910-code.md)

## Outside the selection

The short Merge lane builds and runs named witnesses and selected impact rows. It does
not exercise the Windows share-mask mismatch on Linux, even when it selects this exact
Integration method. On Linux .NET uses advisory `flock`. The old reader and ordinary
writer both take shared locks, so their overlap alone does not produce a sharing
violation. Windows enforces the access and share masks on open. A native run still has
to hit the interval between marker creation and writer close.
[Merge verification](../orchestration-loop.md#merge-verification-is-short-2026-11-18)
· [.NET Unix locking](https://github.com/dotnet/runtime/blob/v9.0.11/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Unix.cs#L322)

The prior Windows Full analysis of `557cdb63` lists no Group 64 failure or coverage gap.
That suggests a previous pass, pending its method-level TRX. The fixture, FakeGrok and
probe sources are identical across the two pins. Run `3528e6b1` recorded this method
failing in Group 64 with a different transient database exception. The late position in
`acca11ba` says the group was outside the failed-first seed. It does not date the marker
race or establish that load caused it.
[Prior Full analysis at its published commit](https://github.com/michal-ciechan/Antiphon/blob/10587f33e98e5d3ae97b2a3a90e9b532448bd114/docs/investigations/2026-11-23-rc-run-557cdb63-final-analysis.md)
· [Earlier Group 64 failure](2026-11-22-rc-run-3528e6b1-final-analysis.md)

## Cheapest pre-land check

`tests/Antiphon.Tests/Application/ChannelOutboundRecoveryTests.cs` and
`src/Antiphon.FakeGrok/OutboundRequestMode.cs` →
`/*/*/ChannelOutboundRecoveryTests/Native_gate_read_retries_a_sharing_violation`.

This witness holds a real `FileShare.None` handle, then releases it after the first open
attempt. It translates Linux's observed `EWOULDBLOCK` (11) to
`ERROR_SHARING_VIOLATION` (`0x80070020`) at the injected open. Before the fix the first
open escaped as an `IOException` on `a5c3f853a504021f5651fec5f51dc7e2378b16dd`
(CP-1, 1 executed, 1 failed). The fixture now uses `LiveFixtureText.ReadPublishedAsync`
with the existing readiness cancellation token. The helper shares read, write and delete,
waits at most five seconds for nonempty content and retries only file-not-found, sharing
violations and lock violations. Other I/O errors propagate. The test still asserts the
exact delivery ID before the dispatcher cut.

The same witness uses the real sharing violation on Windows, so a separate Windows-only
test would duplicate the retry check. Replay this witness and the original method on the
next integrated Windows candidate for native coverage.

## Exemption

None.
