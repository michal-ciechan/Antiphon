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

## Unit shards so far (raw summary)
```
SHARD i-cp1 end rc=0 wall=43s waited=0s  total: 35  failed: 0  succeeded: 35  skipped: 0  duration: 40s 577ms 
SHARD i-cp2 end rc=0 wall=32s waited=0s  total: 18  failed: 0  succeeded: 18  skipped: 0  duration: 30s 300ms 
SHARD i-cp3 end rc=0 wall=230s waited=180s  total: 47  failed: 0  succeeded: 47  skipped: 0  duration: 46s 931ms 
SHARD i-cp4 end rc=0 wall=112s waited=75s  total: 24  failed: 0  succeeded: 24  skipped: 0  duration: 35s 537ms 
SHARD i-cp5 end rc=0 wall=230s waited=165s  total: 52  failed: 0  succeeded: 52  skipped: 0  duration: 1m 01s 826ms 
SHARD c1-C1 end rc=2 wall=957s waited=0s  total: 173  failed: 1  succeeded: 169  skipped: 3  duration: 15m 52s 975ms 
SHARD i-cp6 end rc=0 wall=665s waited=615s  total: 28  failed: 0  succeeded: 28  skipped: 0  duration: 47s 447ms 
SHARD c2-C2 end rc=0 wall=652s waited=390s  total: 201  failed: 0  succeeded: 201  skipped: 0  duration: 4m 20s 386ms 
SHARD i-cp7 end rc=0 wall=388s waited=300s  total: 28  failed: 0  succeeded: 28  skipped: 0  duration: 1m 25s 674ms 
SHARD b1-RollingVRS end rc=2 wall=1725s waited=0s  total: 77  failed: 1  succeeded: 76  skipped: 0  duration: 28m 42s 468ms 
SHARD i-cp8 end rc=0 wall=78s waited=15s  total: 29  failed: 0  succeeded: 29  skipped: 0  duration: 1m 00s 771ms 
SHARD b2-RollingOther end rc=0 wall=189s waited=30s  total: 13  failed: 0  succeeded: 13  skipped: 0  duration: 2m 35s 747ms 
SHARD a-rsc1 end rc=0 wall=413s waited=150s  total: 40  failed: 0  succeeded: 40  skipped: 0  duration: 4m 20s 893ms 
SHARD c3-AB end rc=0 wall=887s waited=60s  total: 553  failed: 0  succeeded: 550  skipped: 3  duration: 13m 43s 661ms 
SHARD b3-Retired end rc=0 wall=913s waited=30s  total: 38  failed: 0  succeeded: 38  skipped: 0  duration: 14m 40s 150ms 
SHARD a-rsc2 end rc=2 wall=881s waited=120s  total: 216  failed: 1  succeeded: 215  skipped: 0  duration: 12m 38s 563ms 
SHARD c4-D end rc=0 wall=860s waited=210s  total: 580  failed: 0  succeeded: 574  skipped: 6  duration: 10m 46s 744ms 
SHARD b4-Ch1 end rc=0 wall=1080s waited=135s  total: 255  failed: 0  succeeded: 255  skipped: 0  duration: 15m 42s 320ms 
```
## Red comparisons so far
```
RED c1364-master rc=0 wall=377s  total: 6  failed: 0  succeeded: 6  skipped: 0 
RED c1364-tip rc=0 wall=371s  total: 6  failed: 0  succeeded: 6  skipped: 0 
RED c1008legacy-master rc=0 wall=701s  total: 4  failed: 0  succeeded: 4  skipped: 0 
```
