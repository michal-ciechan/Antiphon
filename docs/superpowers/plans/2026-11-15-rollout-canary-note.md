# Rollout canary: CARD-0934
Source: [nine-gate owner](../../docker-stack.md#staged-server2-rolling-rollout-card-0934); keep one runner accepting throughout; use the current rc SHA and one named phase per call.
1. Deploy temp beside old: stop if temp fails or old container ID/start time changes; leave old accepting.
2. Smoke temp and run a pinned Plan canary: version, tool or canary failure stops handoff; do not drain old.
3. Verify accepting temp, successful canary and runner defaults: no separate promotion phase; stop if a healthy redirect cannot be established, leaving old accepting.
4. Drain old toward temp with a 240-minute wait: stop/report refusals; rollback clears healthy old's drain and verifies acceptance before considering temp drain.
5. Require fresh zero sessions, runnerSessions and queuedTasks: at 240 minutes only authorized exact old seats may be stopped; record/recheck/rerun drain; any remainder/refusal stops upgrade.
6. Redeploy old after the zero gate and verify mounts, cache and SHA: refusal keeps temp accepting and old drained; use only reviewed rollback recovery.
7. Smoke old, run its pinned Plan canary and cache Both check: failure immediately drains old toward accepting temp; stop/report.
8. After old passes, return scheduling and drain temp with 240-minute shared retirement/exit deadline: prove owned container absence; refusal leaves old accepting, and exit timeout removes nothing live.
9. Retire temp: preview then enabled matching-token apply within 900 seconds; dirty/unpublished work or missing trust registry stops for operator; other refusals get one fresh preview/apply, then Debug; retain receipts.
Scope: documentation canary only; no builds, tests or rollout mutations. Performance and ground truth: [canonical plan](2026-11-15-card-0934-rollout-canary-plan.md).
