# Why the post-land lane lock flake escaped review

## Introducing commits

CARD-1784 introduced the two-start witness in
`e5ca48a2d51c58b8bda512c101fcac3874904d53` and the lock's two-argument
move in `12706fc265f3ffe097717feee39525e7bb5058ec`.
`653d135d2881ca3f210670c65437508d9e6bebd9` carried both into the C# lane.

## Approving review

Full review `c270da97-c8b6-4017-9a3c-419c18a474d2` ran CP-1
(`/*/*/PostLandEarlyWarningTests/*`) on
`6fa1d709ecbbe1991404dbecbbf14d6a21930c72` and passed 20 of 20. The
review report also records a guard batch with the class. Its reviewed
source predates the C# port. The approval record for the port was unavailable
in the evidence used here.

## Why the selected test missed it

The method was in CP-1 under the class's Unit category. Both test parents
started independently. Each detached child ran Git preflight before
attempting `lock.json`. The test's two-second plan delay ran only after
a child took the lock and made it through the busy, hourly and completed
checks. The first parent to start could lose acquisition. If the other
child reached acquisition after the winner released, both lanes could
write `started` even when locking worked.

The Linux implementation of `File.Move(source, destination)` also leaves
an interval between checking that the destination is absent and calling
`rename`, which can overwrite a destination created in that interval.
This gives two overlapping contenders a separate way to report successful
acquisition. A two-entrant probe against the base Antiphon.Nightly assembly
on Linux returned true twice on the first trial. Neither caller released
the lock before both results had been recorded. [Source for .NET 9.0.23](https://github.com/dotnet/runtime/blob/v9.0.23/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Unix.cs#L168-L214).

The CARD-2015 brief supplies one pass at `e0e747772967f8ed4e2085e79aac21c4b056fa3b`,
then one pass and one failure at `7aa85e039d9d25ddcae37694d73bd636c98aad8f`
with the same assembly hash. The test and lane code have no diff on that
range. The recorded `started, started` assertion alone cannot distinguish
sequential acquisitions from overlapping acquisitions that overwrite the
same owner record.

## Cheapest pre-land check

`tests/Antiphon.Tests/Scripts/PostLandEarlyWarningTests.cs` and
`scripts/lib/Antiphon.Nightly/PostLandLane.cs` ->
`/*/*/PostLandEarlyWarningTests/Second_run_refuses_while_the_lane_holds_the_lock`

The fixture starts the contender only after the winning lane reaches its selector.
The first lane stays at that selector until the contender returns its marker. The
assertions name each parent, check lock cleanup and require one build. A separate
two-entrant acquisition probe checks the initial owner-publication race directly.
The lane serializes publication, stale reclamation and owner-checked release with
a named mutex keyed by the full lock path. The mutex is released before planning
and running the tests.

## Exemption

None. The earlier post-land note covers Windows fixture deletion and culture-sensitive
lock timestamps. [Earlier post-land failure note](2026-11-10-why-missed-post-land-early-warning-g130.md).

## Linux reproduction

Checkpoint `20261122-024124-65a9` ran the exact method ten times at
`d8a65be7bcecd1eeff9dc2e655083fb95ba770d1`. Two passed and eight failed
with `started, started`. The single native build started at load 13.48.
Two processes in separate sessions also both acquired the same lock
without an intervening release on trial four.
