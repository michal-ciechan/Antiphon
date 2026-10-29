# CARD-1603 Final Review (8ef7bb9e) - PARTIAL, in progress

Subject: feat/card-task-1d5d34a3 at a05fa77c05e759a0024a99b411fde005aed235ce (product verified at 63ed1c99c). Base e65343132.

## Candidate defect under verification

D-1 (candidate): `CapacityRecoveryService.cs:158` checks `ObsoleteSelectionAttachedAsync(current)` on the outstanding grant before `StillHoldsGrant`/expiry, and `ObsoleteSelectionAttachedAsync` (`CapacityRecoveryService.ObsoleteConsumers.cs:543-562`) does not require the wait to be unfinished. Cleanup discovery only reads `Unfinished` waits (`:376`). A granted wait that finished (e.g. `ObserveTranscriptAsync` Progressed, which never clears the grant, `CapacityRecoveryService.cs:1027-1036`) while its selected Channel/Scheduled row is Canceled or parked keeps `skipGrant = true` every tick: no D-12 expiry, no grant for that provider kind until a new wall wave. Scratch reproduction being built.

## Shards so far (one build bin-r8ef7bb9/, dotnet exec --no-build per shard via build-slot)

See the final report for the complete table. Interim: CP-1 35/35, CP-2 18/18, CP-3 47/47, CP-4 24/24, CP-5 52/52; c1-C1 173 total, 1 failed (C1364BoundLandWaitProofTests.C1364_Wrapper_retire_volumes_reports_waited: script exit 139, under investigation).
