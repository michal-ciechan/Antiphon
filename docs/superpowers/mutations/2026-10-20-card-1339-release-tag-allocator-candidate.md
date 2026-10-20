# CARD-1339 unbound SourceLanding Mutation candidate

Code task / original landing owner: b44512b9. Master-only.
Source binding is pending: bind exact landed bytes and SHA before execution.
This is an inventory and anchor handoff, not an executable mutation pack.

Every PC-1 through PC-77 is **pending**. No deliberate mutant was run in Code.
The [plan](../plans/2026-10-19-card-1339-release-tag-allocator-plan.md)
owns every recipe, variant, oracle and historical cost. Ordinary green cannot
discharge a PC. Mutation owns exact payload binding, red/restore/green execution
and discovery of any remaining missing controls.

| Pending IDs | Exact method in ReleaseTagAllocatorTests | Production anchors |
|---|---|---|
| PC-1..PC-17, PC-64..PC-65, PC-71..PC-73 | C1339_ExactRemoteRefs | ConvertFrom-ReleaseGateRemoteRef; Get-ReleaseGateRemoteHead/RemoteTag; candidate/publisher/promoter call sites; Push-ReleaseGateExactTag; fixture transport admission |
| PC-18..PC-32, PC-67..PC-70, PC-74 | C1339_RemoteTagAllocation | Get-ReleaseGateRemoteTagInventory; Get-ReleaseGatePreReleaseAllocation; Get-ReleaseGateTagProposal/New-ReleaseGateTagReservation; publisher rechecks and preview |
| PC-33..PC-37 | C1339_AllocationUtcDay | publisher attemptUtc and cutUtc selection; reservation reuse |
| PC-38..PC-42 | C1339_PromoteAuthorityHash | ReleaseAuthorityScripts; promoter authority/publication gate before success; Repository and AntiphonApiUrl forwarding |
| PC-43..PC-63, PC-66, PC-75..PC-77 | C1339_PromoteReservationAndTarget | Test-ReleaseGatePromotionReservation; Set-ReleaseGateReservationFinalTag; shared Update-PromoteFinalTag call after fresh/resumed verification |

All variants remain pending, including PC-72 publisher and promoter, PC-74
malformed record/short SHA/conflicting duplicate, PC-75 root-object/version-type/
supported-version, PC-76 scalar reservations, and PC-77 mismatched requested final.
PC-45 includes non-object row, wrapped candidate/SHA/tag, nonboolean published,
and the added releaseId/releaseUrl/finalTag type variants. PC-33 includes offset
UTC conversion; PC-34 uses a changing clock. PC-39/40 cover each independent
authority defect; PC-53 repeats every reservation defect against an existing final.
PC-58/59/60/66 use remove/throw/silent/replace persistence faults; PC-61
covers recovery after each, and PC-63 covers conflicting writer/before/after push.

For each cycle select only:
`/*/*/ReleaseTagAllocatorTests/<exact method>`.
Never substitute a whole class, zero execution, build error, fixture error or
earlier unrelated refusal for the named assertion red. Keep local test fixture
transports confined. SourceLanding uses inherited local children and its assigned
external evidence root; no snapshot commit/push or external executor.
