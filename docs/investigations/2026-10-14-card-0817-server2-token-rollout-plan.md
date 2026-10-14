# CARD-0817 investigation: server2 token rollout plan (2026-10-14)

Investigate task `d1c8d8e2`, run inside the live `server2` runner container (`fc2983ea4991`,
uid 1654), branch `feat/card-task-d1c8d8e2` from `899511125`. Read-only: no code change, no
deploy, no secret read. Budget 45 minutes.

## Outcome

**Confirmed. No rolling upgrade is needed and no code change is needed.** Every CARD-0817 slice
(S1 probe classification and policy file, S2 credential helper, compose bind and deploy custody,
S3 refresh script and docs) is landed on `master` and is already running on the standing
`server2` container at build `7507b3cfe`. The host directory the token belongs in exists,
is bound read-only into the container, and is **empty**. The one missing step is the operator
provisioning step the plan reserved for a human: stream the vault item onto
`/home/mc/antiphon-server2/secrets/github-token/token` with
`scripts/refresh-server2-github-token.ps1`. The directory bind was chosen so that exactly this
write becomes visible to every runner container on its next `open()` without a restart or
rollout. `server2-temp` is retired and absent, so "temp first" would require a full rollout
that recreates the same bind that is already live.

Live symptom reproduced from stored evidence: two `slides` Worktree tasks (`73233c64`,
`1d5f5588`) have been Queued on `server2` since 2026-10-11 with 218 consecutive remote
preparation failures, each `Runner repository clone failed: fatal: could not read Password for
'https://x-access-token@github.com/michal-ciechan/slides.git': terminal prompts disabled`. The
`x-access-token` username in that message proves the helper wiring is active; the missing
password proves only the token file is absent.

## Evidence

### Live container state (observed 2026-10-14, uid 1654, no secret read)

| Observation | Value |
|---|---|
| `PhoneHome__RunnerId` | `server2` |
| `GET /api/session-runners/server2/status` | `buildVersion 7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, `available/dispatchEligible/acceptingNewWork true`, `draining false`, `sessions 7`, `runnerSessions 7`, `queuedTasks 3` |
| `GET /api/session-runners/server2-temp/status` | `available false`, `draining true`, `redirectTo server2`, `retireWhenIdle true`, `retiredAt 2026-10-11T14:23:12Z`, `buildVersion null` (retired placeholder, no container) |
| `/proc/self/mountinfo` | `/home/mc/antiphon-server2/secrets/github-token` → `/run/antiphon/github-token` `ro`; `/home/mc/antiphon-server2/secrets/deploy_key` → `/run/secrets/antiphon-deploy-key` `ro` |
| `stat /run/antiphon/github-token` | `app:app 700`, directory, **no `token` file** |
| `stat /run/antiphon/deploy-key` | `app:app 400` (tmpfs copy staged by the entrypoint) |
| `/run/secrets/antiphon-deploy-key` | `-rw------- 1000 1000` (host owner, unreadable to uid 1654, as designed) |
| `/run/antiphon/push-allow-list` | two lines: `https://github.com/michal-ciechan/antiphon.git`, `https://github.com/michal-ciechan/` |
| `git config --system --list` | `url.git@github.com:michal-ciechan/Antiphon.git.pushinsteadof=https://github.com/michal-ciechan/Antiphon.git`; `credential.https://github.com.helper=/usr/local/bin/antiphon-github-credential`; `usehttppath=true` |
| `/usr/local/bin/antiphon-github-credential` | present, root 0755, dated 2026-10-07 |
| `git remote get-url --push origin` (Antiphon worktree) | `git@github.com:michal-ciechan/Antiphon.git` (SSH, deploy key) |
| helper `get` for `michal-ciechan/markdown-package.git` | 0 bytes printed (token file absent) |
| `git push --dry-run` to `michal-ciechan/markdown-package.git` | exit 128, `could not read Password for 'https://x-access-token@github.com/...': terminal prompts disabled` = `PushProbeCategory.CredentialMissing` |
| `git push --dry-run` to `octocat/Hello-World.git` | exit 128, same text: the helper released nothing for a path outside the allow-list |

### Landed commits and their presence in the live build

| Commit | Content | On `master` | In live `7507b3cfe` |
|---|---|---|---|
| `4308947db` | feat(runner): classify push probes and publish credential policy (S1) | yes | yes |
| `99a50a377` | feat(runner): mounted HTTPS push helper and deploy custody (S2) | yes | yes |
| `f23332d6c` | feat(ops): atomic GitHub token refresh and custody docs (S3) | yes | yes |
| `82b033edf` | fix(ops): refresh script continuation syntax | yes | yes |
| `5cfffc92f`, `fead34620` | fixture repair and its report (task f3fc782f) | yes | yes |

`7507b3cfe` is an ancestor of `master`; 15 commits follow it on `master`, none touching the
files above.

### Card and task state

`card.ps1 history CARD-0817`: Plan `716721c3` (2026-10-04) and Plan `d47edbbf` (2026-10-05)
Succeeded; Code `2a876d8a` and repair Code `f3fc782f` Succeeded on `server2-temp` (2026-10-06);
Review `0dfbe070` Succeeded 2026-10-06T01:27Z; the card has sat in the Review column with no
open task since then until this Investigate (revision 14). The brief's statement that "only a
Plan task exists" is not what the board shows: five tasks exist and all succeeded. The card is
not Done because the plan's operator acceptance steps 2 to 5 (provision, verify, canary,
retain receipts) have no receipts yet
(`docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md`, "Operator
provisioning and acceptance procedure").

## Question 1: where the deploy key lives and how both runners reference it

| Hop | Deploy key (SSH, Antiphon only) | GitHub token (HTTPS, everything else) |
|---|---|---|
| Host path | `/home/mc/antiphon-server2/secrets/deploy_key`, 0600, owner `mc` (`scripts/c590-remote.sh:902`; `docs/agent-credentials.md:251`) | directory `/home/mc/antiphon-server2/secrets/github-token/`, `1654:1654` 0700; file `token` `1654:1654` 0400 (`scripts/c590-remote.sh:919-920`, `:1174-1200`; `docs/agent-credentials.md:254`) |
| Compose | file secret `antiphon-deploy-key: file: ${ANTIPHON_DEPLOY_KEY_FILE}` (`docker-compose.server2-runner.yml:191-193`), attached at `:145-146`; arrives at `/run/secrets/antiphon-deploy-key`, read-only, host uid 1000 0600 | bind `${RUNNER_GITHUB_TOKEN_DIR}:/run/antiphon/github-token:ro` (`docker-compose.server2-runner.yml:137-138`); not a `secrets:` entry, not `environment:` |
| In-container staging | `ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key` (`:90`); `dind-entrypoint.sh:82` runs `install -m 0400 -o 1654 -g 1654` onto the `/run/antiphon` tmpfs at `/run/antiphon/deploy-key`, once at boot | none: `dind-entrypoint.sh:132-139` only notes `GithubTokenPresent`/`GithubTokenAbsent`; the helper opens the file live on each git request |
| Reader | `ssh -F /etc/antiphon/ssh_config` (`IdentityFile /run/antiphon/deploy-key`, `ssh.github.com:443`), used only because `/etc/gitconfig:7-8` rewrites the exact URL `https://github.com/michal-ciechan/Antiphon.git` to SSH | `/etc/gitconfig:9-12` names `/usr/local/bin/antiphon-github-credential` for `https://github.com` with `useHttpPath = true`, `username = x-access-token`; the helper (`docker/session-runner-grok/github-credential.sh`) checks the allow-list then prints `password=` on git's pipe |
| server2 | `compose_host` sets `ANTIPHON_DEPLOY_KEY_FILE="$DEPLOY_KEY"` and `RUNNER_GITHUB_TOKEN_DIR="$GITHUB_TOKEN_DIR_PATH"` (`scripts/c590-remote.sh:949`, `:954`); `stack.env` heredoc writes both (`:7562`, `:7567`) | same |
| server2-temp | `compose_temp` sets the same two values (`scripts/c590-remote.sh:972`, `:977`); `stack.temp.env` heredoc writes both (`:7763`, `:7768`); `docker-compose.server2-runner.temp.yml` overrides neither (it changes only `restart`, two environment keys, networks and adds the Grok store bind) | same |

Both runners therefore read **one** host file for each credential, by construction. There is no
per-runner mapping to change: `GITHUB_TOKEN_DIR_PATH` is a single constant used by both compose
wrappers, both env heredocs and the CARD-1008 recycle roster (`scripts/c590-remote.sh:5354`,
`:5561`).

## Question 2: what is implemented, what is not, and the minimal change set

Implemented and live (file:line on `master`):

- Probe classification: `src/Antiphon.SessionRunner/PushProbeOutcome.cs:18-43`, used by
  `RunnerWorkspaceService.ProbePushAccessAsync` (`src/Antiphon.SessionRunner/RunnerWorkspaceService.cs:488-507`)
  which keeps the receive-pack dry run (`git push --dry-run --no-verify --porcelain -- <push-url> HEAD:refs/heads/antiphon-push-access-probe-<guid>`, `:497-499`) and the 409 code
  `phone_home_repository_push_unauthorized` (`:505`; constant `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs:472`).
- Published allow-list: `PushCredentialPolicyService` (`src/Antiphon.SessionRunner/PushCredentialPolicyService.cs:8-14`),
  `PushCredentialPolicyFile.Render/Write` (`src/Antiphon.SessionRunner/PushCredentialPolicyFile.cs:8-34`),
  setting `PhoneHome:PushCredentialPolicyPath` (`src/Antiphon.SessionRunner/PhoneHomeSettings.cs:39`, validation `:127-128`),
  compose `PhoneHome__PushCredentialPolicyPath: /run/antiphon/push-allow-list` (`docker-compose.server2-runner.yml:63`).
- Credential helper: `docker/session-runner-grok/github-credential.sh:1-48`, baked by
  `docker/session-runner-grok/Dockerfile:228-231`; gitconfig `docker/session-runner-grok/gitconfig:5-12`.
- Deploy custody: `ensure_runner_github_token_dir` (`scripts/c590-remote.sh:1174-1200`), called from
  `ensure_runner_boot_files` (`:1337`) and `persistent-restart` (`:8184`); env example
  `docker/stack.env.example:27-28`.
- Rotation script: `scripts/refresh-server2-github-token.ps1:1-83`.
- Docs: `docs/agent-credentials.md:246-310`, `docs/docker-stack.md:793`, `:825-830`.
- Tests present on `master`: `tests/Antiphon.SessionRunner.Tests/PushProbeOutcomeTests.cs`,
  `PushCredentialPolicyFileTests.cs`, `RunnerWorkspaceServiceTests.cs:253`, `:335`, `:357`;
  `tests/Antiphon.Tests/Infrastructure/GithubCredentialHelperTests.cs:14-66`,
  `DindRunnerContractTests.cs:258`, `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`
  (V-15), `RefreshGithubTokenScriptTests.cs`, `RunnerPushCredentialDocsTests.cs`,
  `RetiredTempContainerHostTests.cs:181-196` (token bind in the production mount roster).
  The Code report `.antiphon/task-2a876d8a.md` records CP-1..CP-6 54/54 and the repair report
  `.antiphon/task-f3fc782f.md` records 52 passing plus one inherited red
  (`Nested_lane_never_uses_sudo_or_python`, unrelated `c1008_owned_mounts` helper).

Not implemented, by design: `gh` in the runner image and any `GH_TOKEN`/`GITHUB_TOKEN` export
(plan D-6); a `pushInsteadOf` for `https://github.com/michal-ciechan/` (it would route every
repository to the Antiphon-only deploy key and is exactly what `99a50a377` narrowed away).

**Code change needed: none.** Non-Antiphon HTTPS pushes work as soon as a non-empty
`/home/mc/antiphon-server2/secrets/github-token/token` exists. The mounted-file credential
helper, the `x-access-token` username and the allow-list are already in the live image and
compose. If the operator nevertheless wants a differently named host folder, the change is a
rename of one constant plus its guards and would need a rollout to take effect:
`scripts/c590-remote.sh:919`, `scripts/refresh-server2-github-token.ps1:39` and `:56`,
`docker/stack.env.example:28`, `docs/agent-credentials.md:254`, `docs/docker-stack.md:793`,
plus `RemoteScriptContractTests`, `RefreshGithubTokenScriptTests`,
`RunnerPushCredentialDocsTests` and `scripts/fixtures/c994-production-compose-model.mjs`. Not
recommended: the existing folder is already dedicated to this token and already bound.

## Question 3: copy-without-reading procedure

`scripts/refresh-server2-github-token.ps1` already implements the brief's design:

1. Vault read (the approved tool): the Bitwarden CLI through the relay session,
   `bw get password 97819ca3-710f-43f8-99ce-b4da013e32c1 --nointeraction` with `BW_SESSION`
   taken from the environment or `~/.bw-session` (`:18-28`). This is the `bitwarden` skill's
   `bw` path named in `docs/external-site-operations.md:34-37`; the skill file itself lives on
   the desktop (`C:\Users\lndco\.claude\skills\bitwarden\SKILL.md`) and is not readable from
   server2. Unlocking the vault (phone-tap relay) is the only manual step.
2. Transport: the value goes to `ssh -o BatchMode=yes mc@server2 "sudo -n sh -c '...'"` on
   **stdin** (`:47`), never argv, never a desktop file. On the host (`:36-44`): `umask 077`,
   `mktemp "$d/token.tmp.XXXXXXXX"` beside the target, `cat > "$tmp"`, `test -s`,
   `chown 1654:1654`, `chmod 0400`, `mv -f` onto `$d/token` (atomic replace; the old inode
   stays until the new file is complete). The directory must already exist and not be a
   symlink (`test -d && test ! -L`), which the deploy guaranteed on 2026-10-11.
3. Verification (`:54-69`): a second SSH prints only `stat -c '%u:%g %a'` and
   `present=true|false`; success requires exactly `1654:1654 400` and `present=true`. Every
   error path warns in fixed words and clears the variable (`:72-83`); stdout can carry only
   those two lines.

What the script does not do, and why the brief's extras are unnecessary: it does not hash the
value on either end. Plan D-2 and `docs/agent-credentials.md` keep the token "never printed or
hashed"; a byte-length comparison would add little (a classic PAT is 40 characters by format),
and the real end-to-end check is functional, run as uid 1654 inside the container with no
secret in argv: `git ls-remote --exit-code https://github.com/michal-ciechan/gym-stat.git HEAD`
(private, succeeds only through the helper) and the dry-run push probe below. If a byte-count
is still wanted, append `wc -c < "$d/token"` to the verify heredoc and compare with
`$token.Length + 1` on the desktop; that prints a number, not the value.

Where to run it: the desktop operator or ClaudeBot lane (needs `bw`, the relay session and
`ssh mc@server2` with passwordless `sudo -n`). Never from an Antiphon delegate: "handling
secrets" is outside the orchestrator autonomy grant (`docs/orchestration-loop.md:52`).
Fallback source if the relay is locked: desktop `gh` is logged in with the same PAT, and
`gh auth token | ssh ... 'sudo -n sh -c "<same heredoc>"'` has the same custody shape; that is
the operator's choice, not the script's default.

## Question 4: rollout choreography

The operator's description assumed the token folder is new and must be mapped into temp first,
then into main at its next deploy. On the evidence both halves are already done:

- The folder is already the dedicated `secrets/github-token/` directory, created empty with
  owner `1654:1654` 0700 by the 2026-10-11 rollout (`ensure_runner_github_token_dir`), and bound
  read-only into the standing `server2` container (mountinfo above).
- `server2-temp` has no container (`retiredAt 2026-10-11T14:23:12Z`, `buildVersion null`). A
  "temp first" step would mean `deploy-temp` at a new SHA, a canary, `drain-old` with up to a
  240-minute wait for the seven live sessions, `redeploy-old`, `drain-temp`, `retire-temp`:
  about half a day to recreate a bind that main already has. Plan D-7 states rotation and first
  provisioning need "no restart, no rollout, no compose up".

Recommended runbook (replaces the rolling upgrade for this card):

| Step | Who | Exact command | Stop gate / verification |
|---|---|---|---|
| 0. Preflight (read-only) | delegate or operator | `pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2` | `buildVersion=7507b3cfe...` (or later), `acceptingNewWork=true`; any SHA older than `99a50a377` means stop, the bind is not there |
| 1. Provision | **human / ClaudeBot lane only** | `pwsh -NoProfile -File scripts/refresh-server2-github-token.ps1` (desktop, vault unlocked) | must print exactly `1654:1654 400` and `present=true`; exit 2 = vault locked, nothing changed; exit 1 = unconfirmed, inspect metadata before retrying |
| 2. Live verification | operator from desktop, or a delegate on server2 as uid 1654 | the plan's step-3 block with `<project>` = `antiphon-runner` (`docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md`, "Operator provisioning and acceptance procedure"): `stat` 1654 400, `test -s push-allow-list`, Antiphon push URL still SSH, `git ls-remote` on private gym-stat, blobless clone of markdown-package, `git push --dry-run --porcelain` probe, `ls-remote` proves no probe ref, `octocat/Hello-World` push refused with `terminal prompts disabled` | last line must be `CARD-0817 credential live, probe passed, outside-allow-list refused`; any other outcome stops before the canary |
| 3. Canary | orchestrator | nothing to dispatch: the two queued `slides` tasks `73233c64` and `1d5f5588` retry remote preparation within 15 minutes (backoff cap 900 s); watch `GET /api/agent-tasks/<id>` for the Warning to stop and the mirror to be recorded. Alternatively `scripts/delegate.ps1 -Worktree -Runner server2 ...` one bounded markdown-package task | the task leaves Queued without a `phone_home_repository_push_unauthorized` Warning, runs at its SHA, pushes its branch; record task id, SHA, branch and the `git ls-remote` SHA |
| 4. Retain receipts, close the card | orchestrator | attach step 1 metadata lines, step 2 output and step 3 facts to CARD-0817 | card can move to Done |
| Rotation (future) | human for the vault, then step 1 to 2 again | ClaudeBot skill `github-push-token`, then the refresh script | no rollout; both runners see the new inode on the next git call |

If a rolling upgrade is run anyway (for the 15 newer master commits, or by operator insistence),
the token step is independent of it: do step 1 first, then the ordinary gates from
`docs/docker-stack.md` "Staged server2 rolling rollout (CARD-0934)" with named phases only
(`check-census`, `check-host-jq`, `deploy-temp`, smoke, `drain-old -WaitIdleMinutes 240`,
`redeploy-old`, smoke, `drain-temp -WaitIdleMinutes 240`, `retire-temp`; never `-Phase all`).
Human-only in that path: `-KillSessions`, Docker prune, `Reset`, recycling runner-state or cache
volumes, deleting deployment markers or donor tars, changing routing pins or budgets, and any
secret handling (`docs/orchestration-loop.md:26`, `:52-56`). The only sanctioned automatic stop of
sessions is the CARD-0934 four-hour drain cap on `server2`, by exact seat
(`docs/docker-stack.md`, gate 5).

## Question 5: risks

- **Blast radius.** The classic PAT reaches every repository the `michal-ciechan` account can push
  to, including future, organisation and collaborator repositories, and can edit workflow files
  (card addendum; `docs/agent-credentials.md:283-289`). Two gates exist and both are tested:
  - C# admission before any git process: `RunnerWorkspaceService.ResolveRepository`
    (`src/Antiphon.SessionRunner/RunnerWorkspaceService.cs:462-467`) via
    `RepositoryCloneSource.IsAdmitted` (`src/Antiphon.SessionRunner.Contracts/RepositoryCloneSource.cs:82-84`)
    against `PhoneHome:AllowedCloneSources` (default `https://github.com/michal-ciechan/`,
    `src/Antiphon.SessionRunner/PhoneHomeSettings.cs:37`). Test:
    `RunnerWorkspaceServiceTests.Mirror_refuses_a_repository_outside_the_allowed_clone_sources`
    (`tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests.cs:253-266`, asserts
    `RepositoryNotAdmitted` and zero processes started).
  - Credential release: the helper admits only an identity equal to a policy line or prefixed
    by a line ending in `/` (`docker/session-runner-grok/github-credential.sh:29-41`). Test:
    `GithubCredentialHelperTests.Helper_answers_nothing_for_a_path_outside_the_allow_list`
    (`tests/Antiphon.Tests/Infrastructure/GithubCredentialHelperTests.cs:35-46`). Reproduced live
    above with `octocat/Hello-World`.
  - Honest limit, already documented: any process running as uid 1654 in the container can read
    the mounted file directly and can edit the policy file on the writable tmpfs
    (`/run/antiphon/push-allow-list` is `app:app 644`). The gates stop mistaken and automated
    pushes, not a hostile session. Protected branches and rulesets still apply.
- **Expiry and rotation.** Expires 2027-10-05, Todoist reminder 2027-09-14
  (`docs/agent-credentials.md:254`). A revoked or expired token surfaces as
  `CredentialRejected` in the task's Queued Warning and the runner log
  (`src/Antiphon.SessionRunner/PushProbeOutcome.cs:24`, remedy `:39`). Rotation is the
  `github-push-token` skill plus the refresh script; no rollout.
- **In-flight tasks.** Provisioning touches no container, process or volume. The atomic `mv`
  replaces the directory entry; a git process that already opened the old inode keeps reading
  it. The eight Dispatched tasks on `server2` at the time of this investigation (`c491a627`,
  `59dcf45c`, `c604f0d3`, `a6d0f871`, `47b93e01`, `d098a30f`, `586e863b`, this task) are
  unaffected. **The provisioning can run now.** A rolling upgrade, if one were still chosen,
  could not start its `drain-old` without waiting for those seven sessions (up to 240 minutes)
  and should wait for them to settle rather than hit the cap.
- **Clone path for private repositories.** `EnsureRepositoryAsync`
  (`src/Antiphon.SessionRunner/RunnerWorkspaceService.cs:509-534`) fails before the probe when
  the anonymous blobless clone of a private repository gets GitHub's 401 and the helper has no
  token; the refusal is `UnsupportedTarget` with the tail of git's stderr, which is what the
  `slides` events show. The URL in that message carries the non-secret `x-access-token`
  username only. After provisioning, the same clone authenticates through the helper
  (plan ground truth "Fetches stay anonymous").

## Remaining uncertainties

- Host-side metadata was observed through the bind (owner/mode of the directory inode); the
  host `stack.env` was not readable from the container and is inferred from the live mounts.
- The desktop lane (vault relay state, `gh` login, the `bitwarden` skill file) could not be
  inspected from server2.
- Whether `gym-stat` and `slides` are still private was inferred from the 401 challenge in the
  `slides` events, not checked against GitHub.

## Not done, noted

- No fix: nothing to code. The operator step is `scripts/refresh-server2-github-token.ps1`
  from the desktop lane, then the plan's step-3 verification and the canary.
