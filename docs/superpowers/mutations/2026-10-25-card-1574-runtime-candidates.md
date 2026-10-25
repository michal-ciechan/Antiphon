# CARD-1574 pending Mutation inventory

All 28 PCs and every variant remain Pending for method-scoped SourceLanding Mutation. No deliberate mutant, red/restore/green cycle or loaded repetition was executed by this Code continuation. Discovery remains Mutation-owned. Ordinary red witnesses do not discharge PCs.

Source inspected/tested: 0166b5f488fcba7a73e98cd0cbc630ecba57f508. Original landing owner: 7c1cf5bb-2f0a-4bc7-983f-d2bab596da72.

| PC | Status | Compiling defect / variant | Exact method and decisive red oracle |
|---|---|---|---|
| PC-1 | Pending | Publish progress before processing the chunk | V-1 method: Ready remains false at the held processing boundary |
| PC-2 | Pending | Use remembered length after an append | V-1 method: old EOF is not Ready |
| PC-3 | Pending | Bypass revoked-claim check | V-2 method: revoked binding refuses Ready |
| PC-4 | Pending | Preserve progress across fork | V-2 method: new binding refuses old readiness |
| PC-5 | Pending | Bypass physical-file comparison | V-2 method: same-length replacement refuses Ready |
| PC-6 | Pending | Ignore pending bytes | V-2 method: partial line refuses Ready |
| PC-7 | Pending | Ignore parse failure | V-2 method: malformed line refuses Ready |
| PC-8 | Pending | Accept wrong snapshot session | V-3 method: wrong-session observation is Unknown |
| PC-9 | Pending | Accept wrong accepted/current generation | V-3 method: changed-generation observation is Unknown |
| PC-10 | Pending | Ignore a changed runner route | V-3 method: route-race observation is Unknown |
| PC-11 | Pending | Ignore a changed runner store | V-3 method: store-race observation is Unknown |
| PC-12 | Pending | Return Ready before awaiting persistence | V-3 method: held commit keeps observation incomplete |
| PC-13 | Pending | Treat failed/NeedsReload persistence as success | V-3 method: faulted commit cannot return Ready |
| PC-14 | Pending | Use direct Now for resumed note | V-4 method: durable keyed row exists before first input |
| PC-15 | Pending | Mint a fallback key after commit/response failure | V-6 method: exactly one original row and recipient prompt |
| PC-16 | Pending | Bypass flush readiness gate | V-5 method: no additional body/attempt while Behind |
| PC-17 | Pending | Bypass automated Now readiness gate | V-5 method: blocked automated Now writes no input |
| PC-18 | Pending | Bypass SendNow readiness gate | V-5 method: blocked SendNow writes no input |
| PC-19 | Pending | Bypass interrupted recovery readiness gate | V-6 method: no Enter or body before readiness/receipt |
| PC-20 | Pending | Allow destructive failure on non-Ready evidence | V-5 method: zero conditional kills beyond the virtual deadline |
| PC-21 | Pending | Accept a clipped body as complete | V-7 method: clipped receipt never confirms note |
| PC-22 | Pending | Ignore attempt floor | V-4 method: old identical restart note cannot confirm new row |
| PC-23 | Pending | Accept queued-command/screen evidence as receipt | V-7 method: neither surrogate confirms note |
| PC-24 | Pending | Retry before checking late receipt | V-5 method: exactly one submitted body after catch-up |
| PC-25 | Pending | Refund/clear the attempt record on defer | V-6 method: original attempt fields and row ID remain equal |
| PC-26 | Pending | Propagate observation timeout into launch cleanup | V-4 method: timed-out pull leaves completed live launch and retained note |
| PC-27 | Pending | Forget observed truncation when the same file regrows | `C1574_Truncation_regrowth_and_invalid_utf8_stay_unknown`: truncation-regrowth is never Ready without replay |
| PC-28 | Pending | Remove strict byte validation before replacement decoding | `C1574_Truncation_regrowth_and_invalid_utf8_stay_unknown`: invalid-utf8 refuses Ready |


## Runtime binding candidates

PC-8: ObserveReceiptReadinessAsync session/entry identity and ordered LastSequence checks; V-3 checks wrong session, mixed entry session, missing tail and bad order.
PC-9: accepted-generation check and ReceiptLaunchStamp equality before ingestion and at the SQL fence; V-3 changes generation at both boundaries.
PC-10: RunnerId in ReadReceiptLaunchStampAsync; V-3 changes route during the held pull.
PC-11 variants: RunnerStoreId and RunnerCwd in that stamp; V-3 changes each during the held pull and store again before the commit fence.
PC-12: awaiting PersistTranscriptCoreAsync through CommitAsync before publication/Ready; V-3 holds a real transaction commit and checks incomplete task plus invisible SQL row.
PC-13 variants: NeedsReload and retained persist-failure guard; V-3 covers commit failure, retained failure after duplicate replay, and clearing after a new successful commit. Collision-only NeedsReload isolation remains an unbound control candidate.
PC-26: runtime timeout/caller cancellation distinction is covered by V-3, but launch-cleanup containment remains unbound until V-4 exists.

All bind to StandingResumeReceiptReadinessTests.C1574_Transport_and_persistence_fence_readiness for the implemented runtime scope. Physical standing owner, current agent pointer, store revision/reset, structural binding/extent and post-persistence ownership checks are additional unbound discovery candidates; do not infer independently proven mutant coverage. PC-14..PC-25 and the launcher portion of PC-26 await S2 integration. Reader sites remain in the prior reader handoff. The 84-minute Mutation floor in the amended plan is provisional and excludes unpriced discovery additions.
