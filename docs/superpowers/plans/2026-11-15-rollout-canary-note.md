# Rollout canary: nine ordered gates (CARD-0934)
Source: [staged rollout](../../docker-stack.md#staged-server2-rolling-rollout-card-0934); preserve an accepting runner throughout; run named phases against the current rc tip and retain refusal receipts.
1. Deploy beside old (`deploy-temp`): stop if temp fails, status is unknown/wrong SHA, or old container ID/start time changes; leave old accepting.
2. Smoke temp: tools/version and sanctioned Plan canary pinned to temp must pass; any failure blocks `drain-old`, with old accepting.
3. Mark temp primary: no separate promotion phase; require healthy accepting temp and successful canary, read defaults; stop if drain redirect cannot safely switch automatic placement; explicit pins do not move.
4. Drain old (`drain-old -WaitIdleMinutes 240`): require redirect to temp and temp accepting; stop/report refusal; rollback clears old drain only while old is healthy, then verify acceptance.
5. Wait up to four hours: require fresh non-null zero sessions/runnerSessions/queuedTasks; at `OldRunnerStillBusy`, only explicitly authorized exact old-runner seat stops, then recheck/rerun drain; any remaining work/refusal stops redeploy.
6. Upgrade old (`redeploy-old`): require zero-work gate; verify mounts/cache/SHA before drain clear; on refusal keep temp accepting and old drained, using reviewed recovery only.
7. Smoke upgraded old: require tools/version, pinned Plan canary and cache `Both`; on failure immediately drain old toward accepting temp and stop/report.
8. Return scheduling and drain temp (`drain-temp -WaitIdleMinutes 240`): require old gate-7 success/acceptance and prove temp retirement/exit/container absence; refusal leaves old accepting; exit timeout removes nothing live.
9. Retire temp: fresh dry-run then matching enabled-token apply within 900 seconds; stop/report dirty or unpublished work or missing trust registry; other refusals get one fresh preview/apply then Debug; retain receipts and resolve journal/volume absence.
