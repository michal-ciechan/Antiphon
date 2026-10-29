# CARD-1593 S3 Round A r3 Final Review (cbd4b6fc) - PARTIAL, in progress

Subject: feat/card-task-6dd8c417 tip 6564c5757dd0ec7c347fe50275f1334498f9e073 (owner task 6dd8c417; Round A owner a8eb1f8b, r2 fe424853). Base fe73cfd2e. Started 12:48 UTC 2026-10-29.

- merge-tree origin/master e30ba1237 HEAD: exit 0, tree 6b0efc2b8.
- check-evidence-diff fe73cfd2e..6564c5757: commits=17 entries=0 violations=0.
- (a) D-1 (round 2): CLOSED by 0040616e7. Both stand-ins poll pg_stat_activity on a fresh connection with no open transaction; per-test cloned database (IsolatedTestSchema, Database=test_...), so the datname filter cannot see another test's waiter. 6564c5757 only adds a Console.WriteLine. Product code unchanged since r2 review.
- (c) adversarial read of the whole S3 product diff: no regression found (detail at the end).

## Unit lane shards (one build bin-rcbd4b6f, lease 3cfb96b3, 152 s, 0 errors)
Lanes A/B/C run concurrently, shards within each lane sequential, each shard `timeout`-capped, each lane one build-slot lease.
(per-shard rows appended as they finish)

| Shard | Lane | Filter (short) | Result | Wall |
|---|---|---|---|---|
| c1-int | C | 12 named integration classes (CP-1..CP-6 union) | 332/332 green (Lifecycle 28, LockOrder 17 incl CP-6, WallReroute 15, ReceiptFailure 21, BaseGuard 97, RecoverySupervision 24, StandingRestartEscalation 52, AgentSupervision 10, StandingRestartAccounting 21, AgentControlServiceIntegration 33, RecoveryQueue 8, RecoveryTask 6). D-1 witness under 3-lane load: `C1593_S3_REREAD tick=2.05s held=2.04s cleared=True` | 236 s |
