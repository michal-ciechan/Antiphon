# CARD-1906 compiler-policy stack environment plumbing

Original Code task and landing owner: `28eecdf5-2364-4811-934c-f9fbdc10ef55`.
Branch: `feat/card-task-28eecdf5`.
Worktree: `/work/worktrees/task-28eecdf5`.
Task base/current master at start: `d100f8fa2c679a5c74b6b235087308776a8f5d50`.
Plan/checkpoint manifest: `docs/superpowers/plans/2026-11-15-card-1906-compiler-policy-plumbing.md`.

Implementation preserves **default OFF**. The deployment reader is actually in
`scripts/c590-remote.sh`, invoked by `scripts/deploy-server2.ps1`; the coordinator
needs no change. Both Compose wrappers read their own project's saved file;
both deployment phase functions validate before effects and preserve the value
on rewrite. The temp overlay inherits the base service environment mapping.
Saved grammar is exact 0/1, LF, at most once, with a distinct sanitized refusal.
The stack file is never sourced. No BuildDriverEnvironment behavior is changed.

The two named defaults are Compose anchor `RunnerLinuxCompilerPolicyDefault` and
shell local readonly `RUNNER_LINUX_COMPILER_POLICY_DEFAULT`, both 0. A future
operator-approved default-on change edits those two nodes and their pinned absent
input expectations. The example's explicit 0 remains an opt-out after such a flip.

## Activation and rollback

Activation was explicitly authorized by the operator on 2026-11-15; this Code task
only makes the setting deliverable. No additional approval was requested.
Place the exact line:

```text
ANTIPHON_LINUX_COMPILER_POLICY=1
```

in `/home/mc/antiphon-server2/secrets/stack.env` before the next `redeploy-old`.
The caller's named rollout recreates the runner. Verify the non-secret setting:

```sh
docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' <runner-container-id> | grep '^ANTIPHON_LINUX_COMPILER_POLICY='
```

Expected output is exactly `ANTIPHON_LINUX_COMPILER_POLICY=1`. Then check new
build-driver diagnostics for `LINUX COMPILER policy=c1885-v1`, and record source/
runtime SHAs. Saved temp intent is independently in `secrets/stack.temp.env` and
applies through `deploy-temp`. Existing task processes and compilers are unchanged;
compilers drain naturally and must never be shut down. Rollback is exact saved 0
and recreation through the same named phase, affecting future builds.

The operator's 24-hour trial measures (1) host CPU/run queue/compiler minute-cores,
(2) matched completed build+test rows per hour and p50/p95 wall with sample counts
and wait/execution separated, (3) timeout and rerun counts. Preserve workload and
concurrency context plus original failures. This task performs no rollout, host
secret edit, compiler shutdown, Docker lifecycle operation or service restart.
Restart target for eventual activation: runner recreation by the named phase;
current Code restart: none.

## Environment custody

`docker/session-runner-grok/dind-entrypoint.sh` launches through `setpriv` without
clearing this variable. `PtyHostLauncher.SpawnAsync` uses an inherited
`ProcessStartInfo` environment; `RunnerSession.StartAsync` passes `request.Env` as
launch overrides. `HostSession` constructs the real `PtyAgentRunner`, which passes
these overrides to Porta.Pty on Linux. There is no runner launch environment
allowlist filtering this key. The Windows modern backend explicitly merges the
parent environment and overrides; this task changes no Windows policy or backend.
The narrow environment clearing in Codex version/auth probes is not session launch.
`scripts/build-slot.ps1` creates an inherited ProcessStartInfo, applies the shared
build policy and launches the requested command; its only module-path removal is
for direct Windows PowerShell 5.1 children. No allowlist addition is needed.

`RunnerLinuxCompilerPolicyTests.Rendered_policy_inherits_through_session_and_build_wrapper`
uses a real rendered Compose value, the production PtyAgentRunner, an override map
that explicitly lacks the policy key, and the real build wrapper with its existing
fixture slot broker. The observed final child value must equal the saved input for
both 0 and 1. It launches no build or live runner and restores parent test environment.

## Red-first evidence

Committed test source: `9f0b62cea551501728c6b2df365e0151e6a73a15`.
Checkpoint run: `.antiphon/c1906/red/20261115-114322-32ff/`.
Fresh TRX: 33 executed, 0 passed, 33 failed, 0 skipped; all 32 refusal shapes
failed their four status assertions (91 instead of 2), and the Compose matrix
failed on missing key (`undefined` instead of `0`). No build/fixture error is
credited as an intended red. Build was clean and verified, 0 compiler errors.
Build execution 423.978s, row execution 18.877s, full run wall 468.361s.
Admission waits were 0s. Loadavg observations during that build ranged from
`5.39 8.66 8.00` to `27.81 13.65 9.76`; these are contextual samples, not a p95.
An initial expected-SHA typo refused before building. Earlier observer calls used
a relative --run path and returned 75 on a nonexistent default-root path; the
absolute-path wait returned the real terminal exit 1. None launched another build.

Final qualification and source-bound checkpoint lines will be appended after
execution. Generated TRX, JSON receipts and logs remain ignored.
