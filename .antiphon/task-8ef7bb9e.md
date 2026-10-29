# CARD-1603 Final Review (8ef7bb9e) - PARTIAL, in progress

Subject: feat/card-task-1d5d34a3 at a05fa77c05e759a0024a99b411fde005aed235ce (product verified at 63ed1c99c). Base e65343132.

## D-1 confirmed (scratch reproduction)

Granted LiveSession wait W, Channel X selected (not redeemed); a successful turn progresses W through ObserveTranscriptAsync (no grant clear); human Drop cancels X. At the tip the provider grant stays on the Progressed W across three 121 s windows and a due competitor is never admitted (`R8EF7_D1 window=0..2 grantedWait=W(progressed)`). The same test passes with every CARD-1603 branch disabled (pre-fix behaviour). Cause: `CapacityRecoveryService.cs:158` and `ObsoleteSelectionAttachedAsync` (`ObsoleteConsumers.cs:543-562`) have no unfinished/expiry guard; cleanup discovery reads only Unfinished waits.

## Integration rows (fresh, tip build bin-r8ef7bb9)

CP-1 35/35, CP-2 18/18, CP-3 47/47, CP-4 24/24, CP-5 52/52, CP-6 28/28, CP-7 28/28, CP-8 29/29 = 261/261.

## Mutation witnesses (scratch, env-gated, one build)

Pre-fix (all new branches off): CP-1 26/35 red incl. V-1 4/4; CP-2 9/18 red incl. V-6 4/4.
Stayed GREEN (cannot go red): noexempt (cleanup ignores receipts/redeemable task) 35/35; nocas alone Q 35/35 and ST 18/18; noreread alone; nocas+noreread Q 35/35 (ST: only V-10 red); noloop (in-loop grant checks) 35/35.
Red as expected: noqueueclean 19, nostanding 9, noobsgrant 2, nodemandloss 3, demandloss-deferred 3, nofilter 3, noattempts 3, nohold 1, broadstatus 8, noparked 4, norecheck 3, notake 1, blanket 29, standingagentexists 7, providerfirst 1.

## Unit shards: in progress (c1-C1 1 red C1364 exit 139; b1-RollingVRS 1 red C1008_Legacy_rolling_and_jq_rosters_remain; master comparison running)
