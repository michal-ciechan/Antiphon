# CARD-0817 Investigate d1c8d8e2: no rollout and no code change needed; only the host token file is missing

Full investigation: `docs/investigations/2026-10-14-card-0817-server2-token-rollout-plan.md`
(same branch, `feat/card-task-d1c8d8e2`). Read-only task run inside the live `server2`
container; no secret read, nothing deployed.

## Outcome

Every CARD-0817 slice is landed on `master` (`4308947db`, `99a50a377`, `f23332d6c`,
`82b033edf`, `5cfffc92f`, `fead34620`) and is in the live `server2` build `7507b3cfe` (all six
are ancestors). Inside the container: the credential helper is baked, `/etc/gitconfig` routes
only the exact Antiphon URL to the SSH deploy key and everything else on `https://github.com`
through the helper, the policy file `/run/antiphon/push-allow-list` is published, and
`/home/mc/antiphon-server2/secrets/github-token/` is bound read-only at
`/run/antiphon/github-token` (owner uid 1654, mode 700) and is **empty**. `server2-temp` is a
retired placeholder (`retiredAt 2026-10-11T14:23:12Z`, no container).

Live symptom: `slides` tasks `73233c64` and `1d5f5588` are Queued on `server2` with 218
consecutive remote-prep failures, `could not read Password for
'https://x-access-token@github.com/michal-ciechan/slides.git': terminal prompts disabled`. That
is `CredentialMissing`: the wiring works, the file is absent. Reproduced from this container
with a dry-run push to markdown-package (exit 128, same text) and the allow-list negative
(`octocat/Hello-World`, same refusal, helper released nothing).

The card's board state: Plan 716721c3, Plan d47edbbf, Code 2a876d8a, Code f3fc782f and Review
0dfbe070 all Succeeded; it waits only on the plan's operator acceptance receipts.

## Answers in brief

1. Deploy key: host `secrets/deploy_key` 0600 `mc` → compose file secret
   `/run/secrets/antiphon-deploy-key` (host uid, unreadable to 1654) → `dind-entrypoint.sh:82`
   stages `/run/antiphon/deploy-key` 0400 uid 1654 on tmpfs at boot → `ssh_config`
   `IdentityFile`. Token: host directory `secrets/github-token/` 1654:1654 0700 →
   `${RUNNER_GITHUB_TOKEN_DIR}:/run/antiphon/github-token:ro` → helper reads `token` live. One
   constant for both runners (`scripts/c590-remote.sh:919`, used at `:954` and `:977`, written
   into both env files at `:7567` and `:7768`); the temp override redefines nothing.
2. Code change: none. The operator's "new folder" already exists and is already mapped into
   main. A folder rename would be `scripts/c590-remote.sh:919`, the refresh script `:39`/`:56`,
   `docker/stack.env.example:28`, two docs rows, four test classes and one fixture, and would
   need a rollout; not recommended.
3. Copy without reading: `scripts/refresh-server2-github-token.ps1` does it: `bw get password
   97819ca3-710f-43f8-99ce-b4da013e32c1 --nointeraction` under the relay `BW_SESSION`
   (the `bitwarden` skill's `bw`), piped on ssh **stdin** into `sudo -n sh -c` with `umask 077`,
   `mktemp` sibling, `chown 1654:1654`, `chmod 0400`, atomic `mv -f`; prints only
   `1654:1654 400` and `present=true`. No hashing (the custody rule says never hash); the
   functional check is `git ls-remote` on private gym-stat plus the dry-run probe as uid 1654.
   Runs on the desktop/ClaudeBot lane only: secret handling is outside delegate autonomy
   (`docs/orchestration-loop.md:52`).
4. Choreography: skip the rolling upgrade. Runbook: (0) `runner-drain.ps1 status -RunnerId
   server2` shows a SHA at or after `99a50a377`; (1) human runs the refresh script; (2) the
   plan's step-3 verification block against project `antiphon-runner`, last line `CARD-0817
   credential live, probe passed, outside-allow-list refused`; (3) canary = the two queued
   `slides` tasks retry within 15 minutes, or one bounded markdown-package task `-Runner
   server2`; (4) retain receipts and close the card. If a rollout is run anyway for other
   reasons, provision first; the token step is independent. Human-only: `-KillSessions`,
   prune, `Reset`, state/cache volume recycling, routing pins, secrets.
5. Risks: token reaches the whole account and workflows; gates are C# admission
   (`RunnerWorkspaceService.cs:462-467`, test `RunnerWorkspaceServiceTests.cs:253`) and the
   helper allow-list (`github-credential.sh:29-41`, test `GithubCredentialHelperTests.cs:35`),
   both verified; a uid-1654 session can still read the file directly (documented limit).
   Expiry 2027-10-05, reminder 2027-09-14, `CredentialRejected` surfaces a stale token.
   Provisioning touches no container: the eight Dispatched server2 tasks are unaffected and it
   can run now; a rollout would have to drain seven live sessions (up to 240 minutes).

## Decision for the operator

The standing authority says "do a rolling upgrade of server2 with the new token". The evidence
says the upgrade recreates a bind that is already live and would cost a half-day drain. Default
if no answer: provision the file with the refresh script and verify; no rollout.

## Not done, noted

- Nothing to fix in code. The next action is the human provisioning step, then verification
  receipts on the card.
