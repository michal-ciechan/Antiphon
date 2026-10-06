# RecycleComposeMismatch on redeploy-old at 5b713f68

Read-only diagnosis. No deploy phase, restart, or server2/desktop change was run from this task. No secret values were read or printed.

## Outcome

`redeploy-old` of `5b713f6855ee417737ff7d7e47cb340d66e3d9b8` invoked real `deploy-parent` and stopped inside `c1008_recycle`'s compose-model proof, before a recycle journal, `docker stop`, `docker rm`, volume removal, `ensure_checkout`, image build, or `compose up`. Old `server2` was not recreated. `server2-temp` was not addressed. `-ResumeRecycle` does not apply. Rerunning the same phase fails the same way. An empty token directory does not make this check pass.

## 1. The check that fired

`jq: error (at <stdin>:263): mount set` is `error("mount set")` in `c1008_compose_model` at `5b713f68` (`scripts/c590-remote.sh` around the `session-runner` roster). `c1008_recycle` runs that filter on `docker compose config --format json` and, on any jq failure, `c1008_refuse RecycleComposeMismatch` (exit 2). The desktop wrapper prints `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2`.

The filter's `mount set` clause is only this, per `state-init` and `session-runner`:

- `volumes` is not an array, or
- its length differs from the hardcoded roster, or
- two entries share a `target`, or
- the service has any `configs`.

It is not `error("declaration roster")` (top-level volume/secret keys), not `error("mount contract")` (source, mode, or bind options), and not `c1008_owned_mounts`' `error("mount count")` (live container inspect). jq 1.7.1 reports this `error()` at the last line of the compose document, not at the mismatched mount. Compose v2.32.4 renders the pre-0817 file as 259 lines and reports `<stdin>:259`; the host's 263 is that same end-of-document location under the host formatter.

`compose_host` always exports `RUNNER_GITHUB_TOKEN_DIR="$GITHUB_TOKEN_DIR_PATH"` (`/home/mc/antiphon-server2/secrets/github-token`) before `docker compose config`. An unset variable is not this failure: the new file's `${RUNNER_GITHUB_TOKEN_DIR:?...}` would make `compose config` fail, its stderr is discarded (`2>/dev/null`), and jq would never print `mount set`.

Reproduced here with inert documented paths and no token contents:

| Render | session-runner mounts | state-init | filter |
|---|---|---|---|
| `d985af05` compose (standing checkout generation) | 10 | 3 | `jq: error (at <stdin>:259): mount set` |
| `5b713f68` compose | 11 | 3 | exit 0 |

Top-level volume keys match on both (`work`, `runner-state`, `runner-tmp`, `dind-data`, `runner-nuget-packages`, `runner-nuget-scratch`, `runner-npm-content`), so the roster got past `declaration roster`. `state-init` is 3 on both and is checked first. The failing service is `session-runner`: expected length 11, rendered length 10.

Expected `session-runner` targets in the uploaded script (host project, not temp):

1. volume `work` → `/work`
2. volume `runner-state` → `/state`
3. volume `dind-data` → `/var/lib/docker`
4. volume `runner-tmp` → `/tmp`
5. volume `runner-nuget-packages` → `/home/app/.nuget/packages` (nocopy)
6. volume `runner-nuget-scratch` → `/var/cache/antiphon/nuget-scratch` (nocopy)
7. volume `runner-npm-content` → `/home/app/.npm/_cacache` (nocopy)
8. bind file, read-only → `/run/antiphon/claude-oauth-token`
9. bind directory, read-only → `/run/antiphon/github-token` (source `GITHUB_TOKEN_DIR_PATH`)
10. bind file, read-only → `/run/antiphon/gitconfig`
11. bind directory, read-write → `/state/codex`

Actual render of the only compose file this phase can see (checkout still at the last successful deploy, `d985af05`): the same list without item 9. `git diff d985af05 5b713f68 -- docker-compose.server2-runner.yml` is that one bind plus `PhoneHome__PushCredentialPolicyPath` (environment, not a mount). Temp's grok bind is not in the host model.

Why the rendered file is the old one: `deploy-parent` scp's the new `c590-remote.sh` and runs it with `C1008_CONTEXT=default`. That path does not `ensure_checkout` before the case. `c1008_recycle` calls `c1008_compose_model` as its first docker command. `SERVER2_COMPOSE` is `$CHECKOUT/docker-compose.server2-runner.yml`, and `C590_CHECKOUT` is unset, so the file is `/work/repos/antiphon/docker-compose.server2-runner.yml`. `ensure_checkout` runs only after `c1008_recycle` returns, inside `case_deploy_parent`. The last successful deploy that could have moved that checkout is `deploy-temp` of `d985af05` (temp is still serving that SHA). This failure returns before `ensure_checkout`, so the checkout cannot have been moved by this attempt. Any `redeploy-old` of a SHA that contains commit `99a50a377` (the bind) hits the same 11-versus-10 proof.

## 2. What changed before the failure

`check-census` is local HTTP only. It does not SSH and does not authorize recycling. The passed census in the brief stands.

This `deploy-parent` was not a dry run. `scripts/c590-real.ps1` prints `Claude token: keeping the existing server2 file (no vault refresh requested)` and returns without SSH when refresh was not requested. The Claude token file was not rewritten.

Then the desktop scp's `c590-remote.sh` to `/home/mc/antiphon-c590/c590-remote.sh` and runs it. On the host, before the jq failure, the script only creates the evidence directory (`c849_evidence_dir` / `write_result` under `/work/test-evidence/<run>/deploy-parent/`). `docker compose config` does not create containers. `c1008_refuse` runs with `C1008_ACTIVE` unset, so it does not write `/home/mc/antiphon-server2/recycle/<operation>.json`.

Not reached, all still inside `c1008_recycle` or after it returns:

- `c1008_owned_mounts` (live container proof)
- `mkdir` of the recycle journal and `c1008_save` (first save is after that proof)
- `docker stop` / `docker rm`
- private volume removal
- `ensure_checkout`, `ensure_runner_boot_files` / `ensure_runner_github_token_dir`
- `stack.env` rewrite, image build, cache prepare, `compose up`

`Assert-NoIncompleteRecycle` runs only when `buildVersion` already equals `-Sha`. Old `server2` is still `4358939ecd85`, so a later rerun does not look for a journal. This attempt's operation id was new and no journal was created. Do not pass `-ResumeRecycle`.

The desktop phase also created its own `C:\src\Antiphon\.antiphon\rolling-server2\<run-id>\` receipt. Those Windows records were not readable from this mirror. Nothing in the script path replaces the old container or its volumes, so the drained idle `server2` container (build `4358939ecd85`, `redirectTo=server2-temp`, counters zero, `retireWhenIdle=false`) and its volumes stay as they were. This mirror did not re-query live status.

## 3. What the host must satisfy

For this phase to pass, the compose file rendered before removal has to declare the same `session-runner` mounts as the uploaded script, and the running container's mounts have to match that model. Today those three are not the same generation.

The directory and variable, once the proof can see the new compose file:

- Host directory `/home/mc/antiphon-server2/secrets/github-token`, owner `1654:1654`, mode `0700`.
- `GITHUB_TOKEN_DIR_PATH` in `c590-remote.sh` is that path. `compose_host` / `compose_temp` export it as `RUNNER_GITHUB_TOKEN_DIR`. `stack.env` is rewritten to the same value only after recycle returns.
- Compose bind: `${RUNNER_GITHUB_TOKEN_DIR:?...}:/run/antiphon/github-token:ro` on `session-runner` only. Not a compose secret and not an environment value.

Who creates it: `ensure_runner_github_token_dir`, called from `ensure_runner_boot_files` during `deploy-parent` after a successful recycle. It creates the directory only when missing, refuses a symlink or non-directory, and does not read or list it. A missing or empty `token` file records `github-token-present.txt` as false and warns `GithubTokenAbsent`. The deploy continues. The credential helper then fails closed until a token exists.

An empty directory satisfies the bind (`file:false` in the roster means directory, not regular file) and leaves the credential inert. It does not satisfy the check that failed. That check never stats the directory.

The operator step that writes the secret is separate and later: `pwsh -NoProfile -File scripts/refresh-server2-github-token.ps1` with an unlocked vault relay (`BW_SESSION`, else `~/.bw-session`), streaming vault item `97819ca3-710f-43f8-99ce-b4da013e32c1` (GitHub PAT - server2 push). Success prints only `1654:1654 400` and `present=true`. That script is human-only because it handles the vault secret. It is not a precondition of `redeploy-old`. CARD-0817 D-8 says the file can be provisioned before or after the rollout; the helper reads it live.

## 4. Options

(a) Creating an empty token directory and rerunning `redeploy-old` does not pass. The rendered mount count is still 10 against a roster of 11, and the directory is not consulted. Do not hand-edit the live container or the server checkout to force the new compose file into view: after the file matches, the next statement is `c1008_owned_mounts`, which stats `/home/mc/antiphon-server2/secrets/github-token` and then requires the running container to already have that mount. The drained container was created without it. That second refusal is still before the journal and before `docker stop`, but it does not deploy.

(b) Required. A Code task on the rollout line that contains `5b713f68` (this diagnosis branch is `c91cad85`, not an ancestor of that SHA). The pre-removal proof has to accept the running container's current 10 mounts as the custody of the container being removed, and the post-recreate proof has to require the 11th bind. `ensure_checkout` and creation of the empty token directory have to happen before any proof that renders or stats the new bind. `c1008_record_recreated` stays strict: the replacement container must mount `/run/antiphon/github-token` read-only from that directory. Do not drop the length check for every mismatch.

(c) Leave `server2-temp` on `d985af05` as the accepting rollback. Do not `retire-temp`, do not start a fresh `deploy-temp` of `5b713f68`, and do not `-ResumeRecycle`. After the Code fix is in the SHA named by `-Sha`, rerun `check-census`, then `redeploy-old`. The deploy creates the empty directory itself.

Human-only, and not this gate: `scripts/refresh-server2-github-token.ps1` after a runner at the new SHA exists, when secondary HTTPS push should work. Vault unlock stays with the operator.

## 5. server2-temp

Unaffected by this failure. `redeploy-old` calls `c1008_recycle` for `antiphon-runner` only. `compose_temp`, temp drain, and temp removal were not entered. Temp keeps serving `d985af05`, including its current sessions.

--- next stage ---
next: decide
handoff: No human call is required to unblock. Do not rerun redeploy-old on 5b713f68. Dispatch Code on the rollout line for the pre-removal roster deadlock in section 4(b). Vault token refresh stays operator-only and is not a precondition.
artifact: .antiphon/task-281b29ce.md
