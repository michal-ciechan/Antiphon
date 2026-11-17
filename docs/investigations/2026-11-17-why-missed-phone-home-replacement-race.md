# Why missed: phone-home replacement race in ConnectPeerAsync

CARD-1960 Debug 233b0e13. Serial checkpoint run `20261117-102716-6a1f` at
`1e82c08aeb3877095d6430c42f7acec66bcf0610`, CP-5:
`PhoneHomeRecoveryEligibilityTests.Lease_recovery_rechecks_refusals_after_inventory("replacement")`
failed: `world.Host.Directory.SnapshotLive()` was the same `PhoneHomeLiveConnection` as `world.Live`.
The test and `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs` were unchanged from
`300c8a99d0d267a39791410b7540189ab702f251`. The CARD-1960 route was not involved.

## Mechanism

`SessionRunnerEndpoints` calls `AcceptWebSocketAsync` and only then `PhoneHomeRunnerDirectory.AcceptConnect`,
which supersedes the old connection and installs the new one under `_gate`. The client's `ConnectAsync`
returns once the 101 handshake is written, so for a moment the directory still holds the superseded
connection. `PhoneHomeTestHost.ConnectPeerAsync` then called `WaitLiveAsync`, which returned the first
non-null snapshot: on a replacement that is the old connection. The replacement peer was also given the old
epoch (`peer.Epoch = live.Epoch`). An assertion that the snapshot changed then fails, depending only on how
fast the server's endpoint continuation runs.

Fix (test support only): `ConnectPeerAsync` waits for the connection whose epoch is the one its own
registration minted (`RegisterAsync` returns `Epoch`; `AcceptConnect` stamps the ticket's epoch, and
`slot.Epoch` only increases). The bound stays 2 s; the timeout message names the expected and the observed
epoch. `WaitLiveAsync()` without `epoch` is unchanged for its other callers.

Witness: `PhoneHomeRecoveryEligibilityTests.Replacement_connect_waits_for_the_connection_minted_for_its_registration`.
`PhoneHomeTestHost.AfterHandshake` holds the server between the handshake and `AcceptConnect`. Before the fix
`ConnectPeerAsync` completed inside that window with the superseded connection (red 20 of 20 class runs);
after it, it waits for the release and returns the new connection (green 20 of 20).

## Introducing commit/card

introducing commit/card: e27d3d449e138f1b640b0135e558ecc39011d71c (CARD-0710: `ConnectPeerAsync` ends in a
first-non-null `WaitLiveAsync`) exposed by 6ae5f13ef16d97f601444b608ebab54ea256d820 (the commit names no card:
"reproduce phone-home eligibility latch after lease expiry", 2026-10-24), which added the `replacement` case
asserting the snapshot changed immediately after `ConnectPeerAsync`. Five other classes assert
`liveB.ShouldNotBeSameAs(liveA)` after a reconnect; most of them dispose or wait out the old socket first,
which closes the window.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- not established from this mirror; the case ran in every selection naming `PhoneHomeRecoveryEligibilityTests`
  since 2026-10-24 and passed. The race is narrow: 25 unfixed class runs here (20 at load 8-21, 5 with
  12 extra CPU-bound workers) did not reproduce it.

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was inside it. The failure needs the
server's endpoint continuation to be delayed past the client's first 20 ms poll. CARD-1960's serial receipt
(14 cold builds, 132 minutes, on oversubscribed server2) is the first recorded run where that happened.
A green run is no evidence for a test whose verdict depends on scheduling.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs` -> `/*/*/(PhoneHomeRecoveryEligibilityTests*)|(PhoneHomeEventPumpTests*)|(PhoneHomeDirectoryTests*)|(PhoneHomeLaunchTransportTests*)|(PhoneHomeStrandedQueueTests*)|(MultiRunnerDirectoryTests*)/*`

The guard that catches this class of race is a held window, not more repetitions: a fixture that can pause the
server between two steps the client cannot observe (here `AfterHandshake`) makes the race deterministic.
A test helper that waits for a state change should wait for the identity it caused (epoch, id), never for
"any" state.

## Exemption

same cause as an already written note: none.
