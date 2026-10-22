# CARD-1490 repair - Code task cd2c1ad3 (Review 6cd4edb3 D-1, D-2)

Chain: b180d6e2 (8d407e723 red, 520d2cd41 fix, 45e7e8775 report) + this repair. Land owner: cd2c1ad3.
Plan (amended): docs/superpowers/plans/2026-10-22-held-model-explicit-dispatch-plan.md
(Repair section, kill/requeue site table, V-4..V-7, R-2/R-3, CP-5/CP-6, PC-3..PC-5).

## Commits

- 5602e7bfdf883d05914c82cc117574194965d948 test: red witness (HeldModelEscalationTests +6 cases).
- 42e75c3d99374c427a81c8b72ac8c7788bd3bce8 fix + plan + docs/antiphon-api.md note.
- This report commit is the tested tip (checkpoint lines in the final Code message).

## Red witness (unlisted, stated: one narrow run-checkpoint.ps1 at the red commit)

CHECKPOINT C1490-red commit=5602e7bfdf883d05914c82cc117574194965d948 build=ok filter=/*/*/HeldModelEscalationTests/* executed=10 passed=5 failed=5 skipped=0 slot=granted waited=315s dirty=0 sourceState=clean buildSource=verified

Red for the stated reasons: Retry (Working, Failed) and pinned/specialist escalation threw no
ModelDisabledException (C1490-retry-held-refused x2, C1490-pinned-held-refused,
C1490-specialist-held-refused); the reverse pinned case was a needless 409 on fable. The 5 green
were the 4 pre-existing cases plus the list-governed Retry control.

## Fix

- D-2: the tick's alias rule moved from private AgentTaskDispatcher.ResolveDispatchAliasAsync to
  DispatchModelAlias.ForTaskAsync (specialist alias > pinned agent ModelId > tier ladder);
  AfterRequeueAsync applies it to the row a requeue leaves (pinned agent kept, ephemeral retired).
  Used by the tick, EscalateAsync, AutoEscalateStalledAsync skip, and the Retry refusal.
- D-1: RetryAsync refuses 409 model_disabled (create/escalate body) when that alias is held,
  before the repeat block, auth refusals, StopDelegateAsync or any row change. List-governed tasks
  (Complexity/RoutingPinId) keep the tick re-walk (ComplexityWallRerouteTests Retry->Reroute flow).
- model_disabled kept.

## Kill/requeue site enumeration

See the plan's "Kill / requeue / tier-change sites" table. Disclosed pre-existing residuals (not
kill-then-park of a running session onto a hold introduced here): list-governed Retry under a
Required pin that forbids reroute; wall reroute of a chain task on a pinned agent whose ModelId is
held; boot-wedge relaunch does not re-check the hold; RerouteAsync and released-seat Continue use
the tier alias but kill nothing.

## Landability

- git merge-tree --write-tree origin/master (9cf77dbad) HEAD: clean.
- origin/rc/2026-10-20 (ceb6e3d6b): 8d407e723, 520d2cd41, 5602e7bfd, 42e75c3d9 cherry-pick in
  sequence; the only conflict is the plan doc (modify/delete) in 42e75c3d9 - drop the plan doc
  from 520d2cd41 and 42e75c3d9 picks.

## PCs pending for SourceLanding Mutation

PC-1 (EscalateAsync RequireAsync), PC-2 (auto-clock skip; missing control), PC-3 (RetryAsync
RequireAsync -> V-4), PC-4 (AfterRequeueAsync returns tier ladder -> V-6/V-7), PC-5 (drop the
list-governed exemption -> V-5).

Unit lane not run (brief: minimal testing, plan CP rows only); deferred, not passed.
