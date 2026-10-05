# CARD-0817: one HTTPS token for every non-Antiphon push from server2

Plan with folded verification design, 2026-10-05. Source inspected at
`d985af05b5ace2f13ec3f0d886c15992e9c13c3f` (branch base; `origin/master` was
`ffb819a6f` at the time, with no change to the files below). Live CARD-0817 was read in
full through `GET /api/cards/7db9a5bc-...?boardId=8988ca03-...`; its final addendum is the
credential decision this plan implements. It supersedes
[the 2026-10-04 SSH machine-user plan](2026-10-04-card-0817-single-runner-push-key-plan.md),
whose S1/S2 were never implemented (only the document landed, `f205e8e98`).

## Outcome and boundaries

server2 and server2-temp push to every admitted non-Antiphon repository over HTTPS with ONE
classic personal access token (vault item "GitHub PAT - server2 push", id
`97819ca3-710f-43f8-99ce-b4da013e32c1`, expiry 2027-10-05). The token lives in one host file
owned by the runner uid, bind-mounted read-only into both runner containers, and is read only
by a baked git credential helper at the moment git asks for it. Antiphon keeps its SSH deploy
key, its `deploy-parent` seeding and verification, and its push smoke unchanged. The runner
allow-list stays the only gate on where server2 may push, and the credential helper enforces
that same list, so a push to a repository outside it gets no credential at all.

Plan and Code never create, read, print, hash, copy or install the real token. Code tests use
inert sentinel files and local scratch repositories. Provisioning the host file is operator
work (the operator, or the ClaudeBot session with vault access and an unlocked Bitwarden),
done after land and recorded in [agent-credentials.md](../../agent-credentials.md). The
Antiphon rollout is a separate operator activity under the
[rolling runbook](../../docker-stack.md#staged-server2-rolling-rollout-card-0934).

## Ground truth

| Card assumption | What the code or live state does | Verdict |
|---|---|---|
| Mount the token "the way the deploy key is mounted" | The deploy key is a Compose file secret that arrives host-owned at 0600; `dind-entrypoint.sh` step 2 copies it as root onto the `/run/antiphon` tmpfs once, at boot. A boot-time copy cannot be refreshed without recreating the container, and a single-file read-only bind keeps the old inode after an atomic replace. | Deviates. The Codex home (`secrets/codex/`, a host directory owned by uid 1654, bound at `/state/codex`) is the pattern that refreshes live; D-2 uses it. |
| A git credential helper can read a mounted file without the token appearing anywhere else | Image git is 2.47.3 (`git-build` stage). Verified locally on this runner's git 2.47.3: `credential.https://github.com.helper` plus `useHttpPath = true` hands the helper `protocol`, `host` and `path`; a config `username` is passed to the helper and emitted; a request for another host matches no helper and fails `could not read Username for 'https://gitlab.com': terminal prompts disabled` (exit 128) under `GIT_TERMINAL_PROMPT=0`. | Confirmed. The helper output travels on a pipe git reads; nothing is written to disk, argv or the environment. |
| The push probe can simply run over HTTPS | `RunnerWorkspaceService.ProbePushAccessAsync` (`:185`) reads `remote get-url --push origin` and runs `git push --dry-run --no-verify --porcelain -- <push-url> HEAD:refs/heads/antiphon-push-access-probe-<guid>`; `GitAsync` (`:639`) already sets `GIT_TERMINAL_PROMPT=0`. The baked `/etc/gitconfig` rewrites EVERY `https://github.com/` push to `git@github.com:`, so today the probe and every session push go over SSH with the deploy key. | Card is incomplete: the rewrite must be narrowed or the token is never used. Verified locally: `url."git@github.com:michal-ciechan/Antiphon.git".pushInsteadOf = https://github.com/michal-ciechan/Antiphon.git` rewrites exactly that URL (case-sensitive, `.git` included) and leaves `.../markdown-package.git` on HTTPS. |
| The allow-list is the only gate on where server2 may push | `PhoneHomeSettings.AllowedCloneSources` defaults to `["https://github.com/michal-ciechan/"]`; `RepositoryCloneSource.IsAdmitted` is exact-primary or ordinal prefix; `ResolveRepository` refuses `phone_home_repository_not_admitted` before any git process. It gates the runner's mirror machinery only: a session's own `git push <other url>` is gated by nothing today, and the token reaches organisation and collaborator repositories outside that prefix. | Card is right about the gate's position and silent about its reach. D-4 makes the same list govern the credential helper. |
| Keep the Antiphon deploy-parent SSH key and primary checkout verification unchanged | `scripts/c590-remote.sh::verify_runner_checkout` requires origin `https://github.com/michal-ciechan/Antiphon.git` and an anonymous fetch; `case_git_smoke` requires the SSH banner and pushes a throwaway branch over SSH; `RunnerWorkspaceService.DefaultCloneSource` is the same URL string. | Confirmed. The narrowed rewrite keeps exactly this URL on SSH; CP-4 pins the two strings together. |
| The same credential for server2 and server2-temp | `docker-compose.server2-runner.temp.yml` overrides only `restart`, two environment keys, networks and adds the Grok store bind; every other bind is inherited. `compose_host` and `compose_temp` both take their host paths from the same `c590-remote.sh` constants. | Confirmed by construction: one host directory, two bind mounts. |
| A rollout must not recreate containers without the credential mount | Every host path is a `${VAR:?required}` interpolation, so `compose config`/`up` refuse when `stack.env` lacks it. `ensure_runner_boot_files` runs in `case_deploy_parent`, `case_deploy_temp_runner` and `case_persistent_restart` before compose, and both `stack.env` heredocs are rewritten by the same script. `c1008_compose_model` (`:3799`) pins the exact bind/secret/tmpfs roster; a new bind that is not in the roster is `RecycleComposeMismatch`. | Confirmed, with the roster as an obligation: D-8 changes compose, script and roster in one slice. |
| A stale or revoked token is detected and reported | Any nonzero probe is one message, `Runner cannot push to {identity}; register a push credential for it on server2.`; raw stderr is dropped. `server/Application/Services/RemoteWorkspacePreparer.cs` records `The runner could not mirror the task branch (<code>: <message>); the task stays Queued.` as a Warning event and backs off (base 30 s, cap 900 s), which is the "93 consecutive failures, every ~15 minutes" the card cites. | Card is incomplete: the probe cannot tell a revoked token from a network failure. D-5 classifies stderr into a category and remedy; the code and the preparer's behaviour stay. |
| `gh` on the runner is a free choice | `docker/session-runner-grok/Dockerfile` installs no `gh`; nothing in the runner, dispatcher or landing path calls it. Landing runs on the desktop. | Confirmed absent. D-6 decides against it for this card. |
| Fetches stay anonymous | `EnsureRepositoryAsync` clones `--filter=blob:none --no-checkout` over HTTPS; GitHub answers a private repository's fetch with a 401 challenge, at which git consults the credential helper. `gym-stat` and `slides` are private (anonymous 404, CARD-0812 plan). | Consequence: with D-3 the runner can also mirror an admitted private repository. Antiphon's public fetches never trigger the helper. |
| Projects that would use this | `GET /api/projects`: `markdown-package`, `gym-stat` and `slides` carry `gitRepositoryUrl` under `https://github.com/michal-ciechan/`; the rest are empty. | The owner prefix admits all three and every future repository under the account, which is the operator's stated intent. |
| Rollout in progress | `GET /api/session-runners/{id}/status` at 2026-10-05T22:59Z: `server2` buildVersion `4358939e` (4 sessions, accepting), `server2-temp` buildVersion `d985af05` (1 session, accepting), neither draining. | This change needs its own rollout after the current one finishes. Until then markdown-package stays `-Runner desktop`. |
| Evidence logs could leak a token | `c590-remote.sh::scrub_file` already redacts `ghp_`, `gho_`, `ghu_`, `ghs_`, `ghr_` and `github_pat_` tokens in every case log (`RemoteScriptContractTests.Scrub_covers_github_token_prefixes`). | Confirmed; the operator acceptance evidence is safe even if a tool echoes. |

Platform facts read on 2026-10-05: `GET /api/runner-defaults` revision 2, `globalRunnerId: server2`,
no kind overrides; `GET /api/session-runners`: `desktop` (windows, 2/2 delegated tasks),
`server2` (linux, 6/10 sessions), `server2-temp` (linux, 0/10 sessions). Observations only; no lane
below embeds a runner.

## Decisions

### D-1: HTTPS token for every non-Antiphon push; Antiphon stays on the SSH deploy key

The baked `/etc/gitconfig` narrows its rewrite to the primary's exact URL:

```ini
[core]
	sshCommand = ssh -F /etc/antiphon/ssh_config
[url "git@github.com:michal-ciechan/Antiphon.git"]
	pushInsteadOf = https://github.com/michal-ciechan/Antiphon.git
[credential "https://github.com"]
	helper = /usr/local/bin/antiphon-github-credential
	useHttpPath = true
	username = x-access-token
```

Antiphon mirrors and the primary checkout keep pushing over `ssh.github.com:443` with
`/run/antiphon/deploy-key`; `ensure_runner_boot_files`, `verify_runner_checkout`,
`case_git_smoke`, `RunnerVerificationWorkspace` and `DefaultCloneSource` do not change. Every
other `https://github.com/...` push stays HTTPS and authenticates through the helper. The
`username` is the documented token username and is non-secret; GitHub authenticates a PAT by
the password alone, and the ClaudeBot report confirmed `x-access-token` works for this account.

**Blast radius, stated for the operator.** The token can push to every repository the
`michal-ciechan` account can push to, including future ones, organisation and collaborator
repositories, and it can change workflow files. Every process running as uid 1654 inside
either runner container can read the mounted file, so a compromised session can use it
directly, exactly as it could use the deploy key today. The allow-list (D-4) is the gate on
where a mistaken or automated push goes; it is not a sandbox against a hostile session.
Protected branches and rulesets still apply.

Rejected: moving Antiphon onto the token too (the brief keeps the deploy key path unchanged,
and the smoke's SSH banner check would lose meaning); per-repository SSH aliases and
`pushInsteadOf` entries (superseded by the operator's one-credential decision); a GitHub App
(the operator chose a PAT); a `url.*.insteadOf` rule (would send fetches through the helper
unconditionally).

### D-2: a host directory owned by the runner uid, bind-mounted read-only into both runners

| Hop | Contract |
|---|---|
| Vault | Item "GitHub PAT - server2 push", id `97819ca3-710f-43f8-99ce-b4da013e32c1`, token in the password field; fields Expires, GitHub token name, Type, Scopes. |
| Host | Directory `/home/mc/antiphon-server2/secrets/github-token/`, owner `1654:1654`, mode 0700. File `token` inside it, owner `1654:1654`, mode 0400, one line, trailing newline tolerated. The deploy creates the empty directory; only the operator lane (D-7) ever writes the file. |
| Compose | `${RUNNER_GITHUB_TOKEN_DIR:?RUNNER_GITHUB_TOKEN_DIR is required}:/run/antiphon/github-token:ro` on `session-runner` in `docker-compose.server2-runner.yml`; the temp override inherits it. `stack.env` gets `RUNNER_GITHUB_TOKEN_DIR=/home/mc/antiphon-server2/secrets/github-token`. Not a Compose `secrets:` entry, not an `environment:` entry. |
| Container | `/run/antiphon/github-token/token`, readable by uid 1654 only; the mount is read-only so no session can replace or truncate it. The entrypoint stages nothing and exports nothing; it only writes `C604_ENTRYPOINT_NOTE GithubTokenPresent` or `GithubTokenAbsent` to stderr after the identity check (presence only, non-fatal, never printed or hashed). |

Why a directory and not a file: an atomic `mv` of a new `token` into a bind-mounted directory
is visible to both containers on the next `open()`, which is what rotation without a rollout
(D-7) needs; a file bind would keep serving the old inode. Why owned by uid 1654 and not
staged by root: staging happens once at boot, so a rotated host file would not reach a running
container; the Codex home already establishes the 1654-owned host directory under
`sudo -n install -d -o 1654 -g 1654 -m 0700`. Why not a tmpfs copy: same reason.

`deploy-parent`, `deploy-temp-runner` and `persistent-restart` gain
`ensure_runner_github_token_dir` inside `ensure_runner_boot_files`, modelled line for line on
`ensure_runner_codex_home`: refuse a symlink (`GithubTokenDirPathIsSymlink`) or a
non-directory (`GithubTokenDirPathIsNotDirectory`) before touching the path, create only when
missing, re-assert owner and mode on the directory alone, never `-R`, never read or list it,
record `github-token-present.txt` from `sudo -n test -s "$GITHUB_TOKEN_DIR_PATH/token"` and
warn `WARN GithubTokenAbsent: secondary repositories will refuse the push probe until the
operator provisions the token (docs/agent-credentials.md)`.

Rejected: a Compose file secret (host-owned, needs root staging, boot-time only); a
`GIT_CONFIG_GLOBAL`-level credential entry in the mounted identity file (that file is
non-secret and operator-edited; the helper path belongs with the image that ships the helper).

### D-3: a baked POSIX credential helper that reads the mount and answers only for admitted paths

`docker/session-runner-grok/github-credential.sh`, copied to
`/usr/local/bin/antiphon-github-credential` (root-owned, 0755, ASCII, `sh -n` checked in the
Dockerfile `RUN`). Behaviour, which the tests in V-6..V-11 pin:

- `get`: read `key=value` lines from stdin until the blank line. Require `protocol=https` and
  `host=github.com`. Build the identity `https://github.com/<path lowercased>`, appending
  `.git` when absent (the same normalisation as `RepositoryCloneSource.TryNormalize`). Read the
  policy file (D-4); admit when a line equals the identity or when a line ending in `/` is an
  ordinal prefix of it. Read the token file, strip whitespace. If and only if all of that
  holds and the token is non-empty, print `password=<token>` followed by a newline, nothing
  else. Otherwise print nothing and exit 0, which leaves git to fail closed under
  `GIT_TERMINAL_PROMPT=0`.
- `store`, `erase` and any other verb: consume stdin, print nothing, exit 0. The helper never
  writes a file, never prints to stderr while a token is in a variable, and unsets it before
  exit.
- Paths come from constants `/run/antiphon/github-token/token` and
  `/run/antiphon/push-allow-list`, overridable through `ANTIPHON_GITHUB_TOKEN_FILE` and
  `ANTIPHON_PUSH_ALLOW_LIST` for the tests. Both are paths, never values; a session that sets
  them gains nothing it could not already read.

Rejected: `git credential-store --file <mount>` (its on-disk format is a credentialed URL,
which the card forbids; `store` rewrites the file on approval; it cannot scope by repository);
`GIT_ASKPASS` or a `GH_TOKEN`/`GITHUB_TOKEN` environment export (environment dumps,
`/proc/<pid>/environ`, transcripts); baking path-scoped
`[credential "https://github.com/michal-ciechan/"]` keys instead of D-4 (verified to work on
git 2.47, but it is a second copy of the allow-list inside the image that drifts from
`PhoneHome:AllowedCloneSources`).

### D-4: the runner publishes its effective allow-list; the helper refuses anything outside it

New setting `PhoneHome:PushCredentialPolicyPath` (default null; must be a POSIX absolute path
when set). Compose sets `PhoneHome__PushCredentialPolicyPath: /run/antiphon/push-allow-list`.
A hosted service `PushCredentialPolicyService`, registered before
`PhoneHomeConnectionService`, writes the file once at startup when `PhoneHome:Enabled` and the
path is set: the normalised primary identity on the first line, then each
`AllowedCloneSources` entry, one per line, written to a sibling temp file and moved into place.
`PushCredentialPolicyFile.Render(RunnerRepositoryPolicy)` is the pure part the tests pin.
With today's defaults the file is:

```text
https://github.com/michal-ciechan/antiphon.git
https://github.com/michal-ciechan/
```

So the C# admission (`ResolveRepository`) and the credential release (helper) read one
setting. A push to `https://github.com/some-org/repo.git` from any shell in the container gets
no credential and fails with `could not read Username ... terminal prompts disabled`, which is
a visible, testable narrowing of the token's actual reach. A missing policy file (runner not
yet started, or the path unset) fails closed. The file holds no secret.

Honest limitation: the tmpfs is owned by uid 1654, so a session could edit the policy file. It
guards against mistaken and automated pushes, not against a hostile agent; see D-1.

Rejected: the entrypoint deriving the list from `PhoneHome__AllowedCloneSources__*` (it would
have to duplicate the C# default); the helper calling the runner's HTTP API per credential
request (couples every git auth to runner liveness); exact repository entries replacing the
owner prefix (the operator wants future repositories covered without a change; an operator may
still configure exact `.git` entries later, since the helper treats a line without a trailing
`/` as exact).

### D-5: keep the receive-pack probe and its 409 code; classify the failure

The probe command, the unique probe ref and the `--dry-run` stay as they are. Over HTTPS git
receives a 401 on `git-receive-pack`, asks the helper, and retries; a dry run with write
access exits 0 and creates no ref. New pure `PushProbeOutcome.Classify(exitCode, stderr)`:

| Category | git/GitHub stderr it recognises | Remedy text |
|---|---|---|
| `Authorized` | exit 0, whatever stderr says | none |
| `CredentialMissing` | `terminal prompts disabled`, `could not read Username`, `could not read Password` | the runner has no credential for this repository: provision the token file (`docs/agent-credentials.md`) or check `PhoneHome:AllowedCloneSources` |
| `CredentialRejected` | `Authentication failed`, `Invalid username or token`, `Bad credentials`, `error: 401` | GitHub rejected the runner's token (expired, revoked or rotated): rotate it with the ClaudeBot `github-push-token` skill and refresh the server2 file |
| `Forbidden` | `Permission to .* denied`, `error: 403`, `Permission denied (publickey)`, `deploy key` | the credential cannot push to this repository: grant the account access, or the SSH key is the wrong identity |
| `Unreachable` | `Could not resolve host`, `Failed to connect`, `Connection timed out`, `Connection refused`, `ssh: connect to host`, `SSL` | GitHub was not reachable from server2; the dispatcher retries after its backoff |
| `Unknown` | anything else | check the runner log |

The refusal becomes `Runner cannot push to {identity} ({category}): {remedy}`, code
`phone_home_repository_push_unauthorized`, HTTP 409, no raw stderr, no mirror created. The
runner logs one Warning with identity and category only. `RemoteWorkspacePreparer` is not
changed: the category now reaches the task's Queued Warning through the message it already
records, which is how a stale token is seen by the orchestrator (D-7).

Rejected: retrying inside the probe (the dispatcher already backs off and a revoked token does
not return); surfacing stderr (URLs carry no userinfo here, but the conservative contract
stays); a new problem code per category (the server and preparer key on the one code).

### D-6: no `gh` in the runner image for this card

| Consideration | `gh` installed, `GH_TOKEN` from the mount | git credential helper only (chosen) |
|---|---|---|
| Image | a pinned ~45 MB binary with its own SHA-256 pin to maintain | one 40-line POSIX script |
| Where the token appears | `GH_TOKEN` in the `gh` process environment (readable in `/proc/<pid>/environ`, and `gh auth login` writes a plaintext `hosts.yml` under `GH_CONFIG_DIR`) | a pipe between git and the helper, for the duration of one request |
| Consumers of the token | two, with two custody shapes | one |
| Need today | none: landing, PR creation and issue reads run on the desktop; runner sessions push branches | covered |

If a runner workflow needs `gh`, file a card for a `gh` wrapper that reads the same mounted
file into the child's environment only, pinned and verified like Claude and Codex. Not this
card.

### D-7: expiry, rotation without a rollout, and how a stale token is seen

- Expiry 2027-10-05; Todoist reminder due 2027-09-14. Both go into the `agent-credentials.md`
  table row, with the vault item name and id, the token name and the scope list, and nothing
  secret.
- Rotation has two halves. GitHub and vault: the ClaudeBot skill `github-push-token`
  regenerates the token, updates the vault item in place and re-logs desktop `gh`; unlocking
  Bitwarden is its one manual step. server2: a new desktop script
  `scripts/refresh-server2-github-token.ps1`, modelled on `Send-C628ClaudeOAuthToken`, reads
  the relay session (`BW_SESSION`, else `~/.bw-session`), runs
  `bw get password 97819ca3-710f-43f8-99ce-b4da013e32c1 --nointeraction`, and streams the
  value over SSH **stdin** into

  ```sh
  sudo -n sh -c 'umask 077; d=/home/mc/antiphon-server2/secrets/github-token; cat > "$d/token.tmp" && chown 1654:1654 "$d/token.tmp" && chmod 0400 "$d/token.tmp" && mv -f "$d/token.tmp" "$d/token"'
  ```

  then prints only `stat -c '%u:%g %a' .../token` (expected `1654:1654 400`) and whether the
  file is non-empty. Never argv, never echoed, never a desktop file; a locked vault or a
  failed SSH write leaves the previous file unchanged and says so. Both containers see the new
  inode on their next git invocation. No restart, no rollout, no `compose up`.
- Detection. (1) The next secondary mirror's probe reports `CredentialRejected` in the task's
  Queued Warning and the runner log (D-5). (2) The operator liveness check in the acceptance
  procedure: `git ls-remote --exit-code https://github.com/michal-ciechan/gym-stat.git HEAD`
  inside the container, which only succeeds through the helper because that repository is
  private; no secret in argv. (3) The deploy's `github-token-present.txt` records presence. A
  runner status field for the credential is not part of this card.

### D-8: interaction with the staged rolling rollout

- This change lands as source; it activates only when a rollout at the landed SHA recreates
  the runner containers. The rollout in progress (temp at `d985af05`, main at `4358939e`) is
  not this card's to direct; its operator decides whether to finish it first or to restart
  `deploy-temp` at the landed SHA. Until a runner carries this SHA, markdown-package keeps
  `-Runner desktop`.
- `case_deploy_parent` and `case_deploy_temp_runner` call `ensure_runner_boot_files` before
  writing `stack.env` and before `compose ... up`, so the directory exists and the variable is
  present before any container is created; `${RUNNER_GITHUB_TOKEN_DIR:?...}` refuses an
  older `stack.env`. `case_persistent_restart` ensures the directory before `compose_host stop`.
- `c1008_compose_model` gets `bind($token;"/run/antiphon/github-token";false;false)` in the
  `session-runner` roster and `--arg token "$GITHUB_TOKEN_DIR_PATH"`, in the same commit as the
  compose change, so `redeploy-old`, `drain-temp` and `retire-temp` keep their
  `RecycleComposeMismatch` guard honest. `scripts/fixtures/c994-production-compose-model.mjs`
  and `c849_fixture_compose` add the variable with an inert directory.
- The host token file can be provisioned before or after that rollout; the helper reads it
  live. The acceptance procedure needs a runner at the landed SHA and the file present.

### D-9: lanes follow live defaults; provisioning is not a Code checkpoint

Omit `-Runner` for Code and Review. S1 is platform-neutral (`Any`). S2 and S3 execute `sh`,
`node` and `docker compose config`, so their checkpoints state a Linux lane; pass
`-Platform Linux` for those dispatches only, and `-Platform Any` to clear a stale pin. No
checkpoint reaches server2's host, GitHub or the vault.

## Implementation slices

### S1: probe classification and the published allow-list (runner, 45-60 minutes)

Files:

- `src/Antiphon.SessionRunner/PushProbeOutcome.cs` (new): the enum, `Classify`, `Remedy`.
- `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs`: `ProbePushAccessAsync` formats the
  D-5 message from the classifier; argv, ordering and the primary exemption unchanged.
- `src/Antiphon.SessionRunner/PushCredentialPolicyFile.cs` (new): `Render` and an atomic
  `Write`.
- `src/Antiphon.SessionRunner/PushCredentialPolicyService.cs` (new hosted service).
- `src/Antiphon.SessionRunner/PhoneHomeSettings.cs`: `PushCredentialPolicyPath` and its
  validation line.
- `src/Antiphon.SessionRunner/Program.cs`: register the service before
  `PhoneHomeConnectionService`.
- `tests/Antiphon.SessionRunner.Tests/PushProbeOutcomeTests.cs` (new): five unparameterised
  methods, V-1.
- `tests/Antiphon.SessionRunner.Tests/PushCredentialPolicyFileTests.cs` (new): three methods,
  V-2..V-4.
- `tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests.cs`: rename
  `Mirror_refuses_a_secondary_repository_the_deploy_key_cannot_push_to` to
  `Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to` and assert the
  category and the absence of raw stderr (V-5). The existing `failProbe` seam
  (`protocol.file.allow=never`) stays; it yields `Unknown`.

Commit and push S1 before CP-1 and CP-2. No server, API, contracts or desktop change.

### S2: image, compose, deploy script and fixtures (Linux, 45-60 minutes)

Files:

- `docker/session-runner-grok/github-credential.sh` (new, ASCII, POSIX sh).
- `docker/session-runner-grok/Dockerfile`: `COPY` the helper to
  `/usr/local/bin/antiphon-github-credential`, `chmod 0755`, `sh -n` it, in the
  `session-testing` stage beside the existing `ssh_config`/`gitconfig` copies.
- `docker/session-runner-grok/gitconfig`: the D-1 text.
- `docker/session-runner-grok/dind-entrypoint.sh`: step 2d presence note (D-2), ASCII only.
- `docker-compose.server2-runner.yml`: the bind and `PhoneHome__PushCredentialPolicyPath`.
- `docker/stack.env.example`: `RUNNER_GITHUB_TOKEN_DIR=/home/mc/antiphon-server2/secrets/github-token`.
- `scripts/c590-remote.sh`: `GITHUB_TOKEN_DIR_PATH="$SERVER2_ROOT/secrets/github-token"`,
  `GITHUB_TOKEN_DIR_OWNER="1654:1654"`, `ensure_runner_github_token_dir`, its call in
  `ensure_runner_boot_files`, the variable in `compose_host`, `compose_temp`,
  `c849_fixture_compose` and both `stack.env` heredocs, the `c1008_compose_model` roster line.
- `scripts/fixtures/c994-production-compose-model.mjs`: `githubToken` directory and
  `RUNNER_GITHUB_TOKEN_DIR`.
- `tests/Antiphon.Tests/Infrastructure/GithubCredentialHelperTests.cs` (new,
  `[ParallelLimiter<ProcessSpawnLimit>]`, guarded for Linux the way
  `RemoteScriptContractTests` guards its `sh` runs): six methods, V-6..V-11. Each runs the real
  script with `sh`, a scratch policy file, a scratch token file holding the sentinel
  `inert-c0817-token`, and a scripted `get`/`store`/`erase` conversation on stdin.
- `tests/Antiphon.Tests/Infrastructure/DindRunnerContractTests.cs`: replace
  `Gitconfig_pushes_over_ssh_only` with
  `Gitconfig_pushes_antiphon_over_ssh_and_other_repositories_over_https` (V-12: text plus real
  `git remote get-url --push` under `GIT_CONFIG_SYSTEM` set to the baked file and
  `GIT_CONFIG_GLOBAL` empty, for the Antiphon URL, its lowercase spelling and
  markdown-package); add `Entrypoint_notes_github_token_presence_without_printing_it` (V-13)
  and `Server2_compose_binds_the_github_token_directory_read_only` (V-14, also pins
  `stack.env.example` and that the temp override does not redefine the bind); extend
  `Entrypoint_never_prints_the_key` with `GITHUB_TOKEN_SOURCE`.
- `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`: add
  `Deploy_parent_creates_the_github_token_directory_without_reading_it` (V-15, modelled on the
  Codex home guard: symlink refusal first, `install -d -o 1654 -g 1654 -m 0700`, no `-R`, no
  reader of the path, presence file, `WARN GithubTokenAbsent`, both `stack.env` heredocs,
  `compose_host`/`compose_temp`, restart order, and the `c1008_compose_model` roster line).

Commit and push S2 before CP-3, CP-4 and CP-5. The Dockerfile change is text-guarded here;
the image is built by the rollout, not by a checkpoint.

### S3: rotation script and documentation (30-45 minutes)

Files:

- `scripts/refresh-server2-github-token.ps1` (new, ASCII): D-7, with the vault item id as a
  constant, `-Server` defaulting to `mc@server2`, and an exit code of 2 for a skipped refresh.
- `docs/agent-credentials.md` §5: a table row for `secrets/github-token/token` (vault item,
  id, token name, scopes, expiry, reminder, rotation skill, refresh script), the D-1 blast
  radius paragraph, and the replacement of "Each secondary repository needs a server2 push
  credential" with the one-token description.
- `docs/docker-stack.md`: the identity-files table row for `secrets/github-token/` and a
  sentence in "Runner checkout: lazy clone and deploy verification" that non-Antiphon pushes
  are HTTPS through the helper.
- `docs/testing-and-build.md` (the CARD-0812 paragraph near line 668): same correction.
- `tests/Antiphon.Tests/Scripts/RefreshGithubTokenScriptTests.cs` (new): two text guards,
  V-16, V-17.
- `tests/Antiphon.Tests/Infrastructure/RunnerPushCredentialDocsTests.cs` (new): one guard,
  V-18, in the shape of `CodexRunnerImageContractTests.Stack_env_and_docs_name_the_host_codex_home`.

Commit and push S3 before CP-6.

## Operator provisioning and acceptance procedure

These are operator steps after land, never Plan or Code steps.

1. Land; then run the rolling phases at the landed SHA from the canonical desktop checkout.
   `deploy-temp` creates `secrets/github-token/` empty and warns `GithubTokenAbsent`; that is
   expected before step 2.
2. From the ClaudeBot session or the operator's lane with an unlocked vault:
   `pwsh -NoProfile -File scripts/refresh-server2-github-token.ps1`. It must print
   `1654:1654 400` and `present=true` and nothing else about the file. (Before S3 lands, the
   equivalent is the D-7 `ssh ... sudo -n sh -c` command fed by `bw get password <id>` on its
   stdin.)
3. Liveness and probe inside the runner being qualified (`<project>` is `antiphon-runner-temp`
   or `antiphon-runner`); every command exits 0 and the last prints the literal line:

   ```sh
   c=$(docker ps -q --filter label=com.docker.compose.project=<project> --filter label=com.docker.compose.service=session-runner)
   docker exec -u 1654:1654 -e GIT_TERMINAL_PROMPT=0 "$c" sh -s <<'VERIFY'
   set -eu
   test "$(stat -c '%u %a' /run/antiphon/github-token/token)" = '1654 400'
   test -s /run/antiphon/push-allow-list
   test "$(git -C /work/repos/antiphon remote get-url --push origin)" = 'git@github.com:michal-ciechan/Antiphon.git'
   git ls-remote --exit-code https://github.com/michal-ciechan/gym-stat.git HEAD >/dev/null
   rm -rf /tmp/c0817-probe
   git clone -q --filter=blob:none --no-checkout https://github.com/michal-ciechan/markdown-package.git /tmp/c0817-probe
   cd /tmp/c0817-probe
   test "$(git remote get-url --push origin)" = 'https://github.com/michal-ciechan/markdown-package.git'
   probe=antiphon-push-access-probe-$(tr -d '-' </proc/sys/kernel/random/uuid)
   timeout --kill-after=5s 120s git push --dry-run --no-verify --porcelain -- "$(git remote get-url --push origin)" "HEAD:refs/heads/$probe"
   test -z "$(git ls-remote --heads origin "refs/heads/$probe")"
   if git push --dry-run --no-verify -- https://github.com/octocat/Hello-World.git HEAD:refs/heads/c0817 2>/tmp/c0817-neg; then exit 2; fi
   grep -q 'terminal prompts disabled' /tmp/c0817-neg
   cd / && rm -rf /tmp/c0817-probe /tmp/c0817-neg
   echo 'CARD-0817 credential live, probe passed, outside-allow-list refused'
   VERIFY
   ```

   The `gym-stat` line proves the helper and token (private repository); the `octocat` line
   proves the allow-list refuses the credential for a repository outside it (git never
   reaches GitHub with a credential, so the failure text is the disabled prompt, not a 403).
4. Canary: dispatch one bounded markdown-package Worktree task pinned to the qualified runner
   with `scripts/delegate.ps1 -Worktree -Runner <runner> ...`. It must leave Queued without a
   `phone_home_repository_push_unauthorized` Warning, run at its assigned SHA, make one empty
   commit on its task branch and push it; record task id, runner, deployed SHA, branch and the
   `git ls-remote origin refs/heads/<branch>` SHA. Then remove the project's `-Runner desktop`
   pin.
5. Retain: the `github-token-present.txt` from the deploy, the step 3 output, the canary
   facts. Nothing else. Code's clean checkpoints claim no live success; the card's acceptance
   is pending until these receipts exist.

Rotation (any time, no rollout): skill `github-push-token`, then step 2, then step 3.

## Verification design

Offline only: real `git`, real `sh`, real configuration binding, inert sentinels, local
scratch repositories, no network, no vault, no server2. New methods are unparameterised. Code
can run every row below; Review reads the receipts and `scripts/check-evidence-diff.ps1` over
the task range; neither runs anything in the acceptance procedure.

### Coverage and witnesses

| ID | Test method | Required result |
|---|---|---|
| V-1 | `PushProbeOutcomeTests.Classifies_a_disabled_prompt_as_a_missing_credential`, `..._an_authentication_failure_as_a_rejected_credential`, `..._a_permission_denial_as_forbidden`, `..._a_network_failure_as_unreachable`, `Zero_exit_is_authorized_whatever_stderr_says` | Each stderr fixture maps to its category; the remedy names the rotation skill only for `CredentialRejected`; exit 0 is `Authorized` even with noisy stderr. |
| V-2 | `PushCredentialPolicyFileTests.Renders_the_primary_identity_then_each_prefix` | Default policy renders the two D-4 lines in order with `\n` endings and nothing else. |
| V-3 | `PushCredentialPolicyFileTests.Write_creates_the_parent_and_replaces_an_existing_file` | Atomic write into a scratch path; a stale file is replaced; no temp file remains. |
| V-4 | `PushCredentialPolicyFileTests.Settings_refuse_a_relative_policy_path` | `PhoneHomeSettings.Validate` with `Enabled` and `PushCredentialPolicyPath = "run/x"` throws naming the setting; null passes. |
| V-5 | `RunnerWorkspaceServiceTests.Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to` | Still `RepositoryPushUnauthorized`, no task worktree, exact dry-run argv; message contains the identity and `(Unknown)` and does not contain `transport 'file'`; the probe-disabled service then mirrors. |
| V-6 | `GithubCredentialHelperTests.Helper_answers_an_admitted_github_path_with_the_mounted_token` | `get` for `michal-ciechan/markdown-package.git` prints exactly `password=inert-c0817-token\n`; no `username=` line; trailing newline in the token file stripped; stderr empty. |
| V-7 | `GithubCredentialHelperTests.Helper_answers_nothing_for_another_host` | `host=gitlab.com` prints nothing, exit 0. |
| V-8 | `GithubCredentialHelperTests.Helper_answers_nothing_for_a_path_outside_the_allow_list` | `some-org/repo.git` and `michal-ciechan-fork/repo.git` print nothing; `Michal-Ciechan/Repo` (case) is admitted. |
| V-9 | `GithubCredentialHelperTests.Helper_answers_nothing_when_the_policy_file_is_missing` | Absent policy file prints nothing, exit 0. |
| V-10 | `GithubCredentialHelperTests.Helper_answers_nothing_when_the_token_file_is_missing_or_empty` | Absent and whitespace-only token files print nothing. |
| V-11 | `GithubCredentialHelperTests.Helper_ignores_store_and_erase_and_writes_nothing` | `store` and `erase` print nothing; the scratch directory's file list is unchanged after all verbs. |
| V-12 | `DindRunnerContractTests.Gitconfig_pushes_antiphon_over_ssh_and_other_repositories_over_https` | Real git resolves `.../Antiphon.git` to `git@github.com:michal-ciechan/Antiphon.git`, and `.../antiphon.git` and `.../markdown-package.git` unchanged; text pins the credential section (`helper`, `useHttpPath = true`, `username = x-access-token`), no `insteadOf` without `push`, and the `pushInsteadOf` value equals the literal `DefaultCloneSource` URL. |
| V-13 | `DindRunnerContractTests.Entrypoint_notes_github_token_presence_without_printing_it` | `GITHUB_TOKEN_SOURCE="$RUNTIME_DIR/github-token/token"`; both note lines present; no `refuse GithubToken*`; no `install`, `cat`, `tr` or export of that variable; `Entrypoint_never_prints_the_key` passes with the extended list. |
| V-14 | `DindRunnerContractTests.Server2_compose_binds_the_github_token_directory_read_only` | Base `session-runner` volumes contain the exact `${RUNNER_GITHUB_TOKEN_DIR:?...}:/run/antiphon/github-token:ro`; env `PhoneHome__PushCredentialPolicyPath` is `/run/antiphon/push-allow-list`; the temp override contains neither; `state-init` does not bind it; `stack.env.example` names the host path; no compose file lists `GH_TOKEN`/`GITHUB_TOKEN`. |
| V-15 | `RemoteScriptContractTests.Deploy_parent_creates_the_github_token_directory_without_reading_it` | The D-2 guard list; `c1008_compose_model` contains the `/run/antiphon/github-token` bind line with `--arg token`. |
| V-16 | `RefreshGithubTokenScriptTests.Refresh_script_streams_the_token_over_stdin_and_never_prints_it` | The script pipes `bw get password <id>` into `ssh`, names `sudo -n sh -c`, `chown 1654:1654`, `chmod 0400`, `mv -f`; no `Write-Host`/`Write-Output` line references the token variable; the token variable is cleared in `finally`; the file is ASCII. |
| V-17 | `RefreshGithubTokenScriptTests.Refresh_script_skips_without_a_relay_session_and_reports_mode_only` | Missing `BW_SESSION` and `~/.bw-session` path warns and exits 2 before `ssh`; the verification prints `stat -c '%u:%g %a'` and a `present=` line only. |
| V-18 | `RunnerPushCredentialDocsTests.Credentials_doc_records_the_token_custody_expiry_and_rotation` | `agent-credentials.md` row names the vault item and id, `2027-10-05`, `2027-09-14`, `github-push-token`, `refresh-server2-github-token.ps1`, `RUNNER_GITHUB_TOKEN_DIR`, `0400`, `1654`; `docker-stack.md` has the identity-table row; neither file contains a `ghp_` token shape. |
| R-1 | `RunnerWorkspaceServiceTests.Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds`, `Mirror_refuses_a_repository_outside_the_allowed_clone_sources`, `Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout`, `Mirror_creates_worktree_on_branch_at_sha`, `Publish_pushes_only_own_fast_forward_branch` | Probe success creates no ref; admission, secondary cloning, primary mirroring and publishing unchanged. |
| R-2 | The other 22 `DindRunnerContractTests` methods | Key staging, tmpfs, healthcheck, SSH pins and identity mount unchanged. |
| R-3 | `RemoteScriptContractTests.Deploy_parent_creates_the_codex_home_directory_without_reading_it`, `Deploy_parent_seeds_or_verifies_runner_checkout`, `Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner`, `C1008_Recycle_exact_default_volumes`, `Scrub_covers_github_token_prefixes` | Sibling boot-file guards still pass; the recycle model renders both compose projects with the new variable and matches the roster; the scrubber still covers `ghp_`. |

### Positive controls for Mutation

One mutation per behaviour, restored before the next. Run only the detecting method with a
`--treenode-filter "/*/*/Class/Method"`; zero tests, a build error or a missing `sh` is not red.

| PC | Change to production behaviour | Detecting filter | Expected red |
|---|---|---|---|
| PC-1 | Map `terminal prompts disabled` to `Unknown` | `/*/*/PushProbeOutcomeTests/Classifies_a_disabled_prompt_as_a_missing_credential` | Category assertion fails. |
| PC-2 | Map `Authentication failed` to `Forbidden` | `/*/*/PushProbeOutcomeTests/Classifies_an_authentication_failure_as_a_rejected_credential` | Category and remedy assertions fail. |
| PC-3 | Map `Permission to ... denied` to `Unreachable` | `/*/*/PushProbeOutcomeTests/Classifies_a_permission_denial_as_forbidden` | Category assertion fails. |
| PC-4 | Map `Could not resolve host` to `Unknown` | `/*/*/PushProbeOutcomeTests/Classifies_a_network_failure_as_unreachable` | Category assertion fails. |
| PC-5 | Classify stderr before checking the exit code | `/*/*/PushProbeOutcomeTests/Zero_exit_is_authorized_whatever_stderr_says` | `Authorized` assertion fails. |
| PC-6 | Drop the primary line from `Render` | `/*/*/PushCredentialPolicyFileTests/Renders_the_primary_identity_then_each_prefix` | Content equality fails. |
| PC-7 | Accept a relative `PushCredentialPolicyPath` | `/*/*/PushCredentialPolicyFileTests/Settings_refuse_a_relative_policy_path` | Expected exception missing. |
| PC-8 | Put the raw probe stderr into the refusal message | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to` | `ShouldNotContain("transport 'file'")` fails. |
| PC-9 | Remove `--dry-run` from the probe | `/*/*/RunnerWorkspaceServiceTests/Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds` | Exact argv assertion fails; the local bare origin gains a probe ref. |
| PC-10 | Helper skips the allow-list check | `/*/*/GithubCredentialHelperTests/Helper_answers_nothing_for_a_path_outside_the_allow_list` | A password line appears for `some-org/repo.git`. |
| PC-11 | Helper answers for any host | `/*/*/GithubCredentialHelperTests/Helper_answers_nothing_for_another_host` | A password line appears for `gitlab.com`. |
| PC-12 | Helper treats a missing policy file as "admit all" | `/*/*/GithubCredentialHelperTests/Helper_answers_nothing_when_the_policy_file_is_missing` | A password line appears. |
| PC-13 | Helper prints the password on `store` | `/*/*/GithubCredentialHelperTests/Helper_ignores_store_and_erase_and_writes_nothing` | Non-empty stdout for `store`. |
| PC-14 | Helper also prints `username=` and does not strip whitespace | `/*/*/GithubCredentialHelperTests/Helper_answers_an_admitted_github_path_with_the_mounted_token` | Exact stdout equality fails. |
| PC-15 | Restore `pushInsteadOf = https://github.com/` | `/*/*/DindRunnerContractTests/Gitconfig_pushes_antiphon_over_ssh_and_other_repositories_over_https` | markdown-package push URL becomes SSH. |
| PC-16 | Drop `:ro` from the compose bind | `/*/*/DindRunnerContractTests/Server2_compose_binds_the_github_token_directory_read_only` | Exact volume entry missing. |
| PC-17 | Add `cat "$GITHUB_TOKEN_SOURCE"` to the entrypoint note | `/*/*/DindRunnerContractTests/Entrypoint_never_prints_the_key` | Secret-path print assertion fails. |
| PC-18 | Remove the symlink refusal from `ensure_runner_github_token_dir` | `/*/*/RemoteScriptContractTests/Deploy_parent_creates_the_github_token_directory_without_reading_it` | Symlink-first ordering assertion fails. |
| PC-19 | Remove the roster line from `c1008_compose_model` | `/*/*/RemoteScriptContractTests/C1008_Recycle_exact_default_volumes` | `RecycleComposeMismatch`. |
| PC-20 | `Write-Host $token` in the refresh script | `/*/*/RefreshGithubTokenScriptTests/Refresh_script_streams_the_token_over_stdin_and_never_prints_it` | Print-guard assertion fails. |

### Execution and cost

Commit and push each slice before its checkpoint group, then run the checkpoint tool through
the build-slot gate with that slice's committed SHA:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0817-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md --rows CP-1,CP-2 --expected-source-sha <S1-sha>
```

S2 runs `--rows CP-3,CP-4,CP-5`; S3 runs `--rows CP-6`. If `run` exits 75, call `wait` until it
does not; do not edit source while a run is active; exit 4 from the gate is a timeout to report.
Retain unedited `CHECKPOINT` lines and receipts outside Git; remove the `bin-c0817-*/` outputs
when done. No whole-Unit, whole-assembly, E2E, Pty or live run. CP-5's `C1008` row shells out
to `node` and `docker compose config` and needs the Docker CLI on the lane (present on the
server2 runners); `GithubCredentialHelperTests` and the `sh` rows need `/bin/sh`.

Estimated authoring: S1 45-60, S2 45-60, S3 30-45 minutes. Checkpoint floor: 26 minutes.
Mutation: about 30-45 minutes for twenty method-scoped cycles, reported separately.

### Checkpoints

CP-1 and CP-2 run on any lane with git. CP-3 to CP-6 run on a Linux lane with `sh`, `git`,
`node` and the Docker CLI; no daemon access is needed.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c0817-runner/` | probe-policy-any | `/*/*/(PushProbeOutcomeTests)\|(PushCredentialPolicyFileTests)/*` | V-1, V-2, V-3, V-4 | all 8 methods; 0 failed/skipped | 8 | 4 |
| CP-2 | S1 | CP-1 | workspace-any | `/*/*/RunnerWorkspaceServiceTests/(Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to)\|(Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds)\|(Mirror_refuses_a_repository_outside_the_allowed_clone_sources)\|(Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout)\|(Mirror_creates_worktree_on_branch_at_sha)\|(Publish_pushes_only_own_fast_forward_branch)` | V-5, R-1 | all 6 methods; 0 failed/skipped | 6 | 2 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c0817-image/` | helper-linux | `/*/*/GithubCredentialHelperTests/*` | V-6, V-7, V-8, V-9, V-10, V-11 | all 6 methods; 0 failed/skipped | 6 | 8 |
| CP-4 | S2 | CP-3 | custody-linux | `/*/*/DindRunnerContractTests/*` | V-12, V-13, V-14, R-2 | all 25 methods; 0 failed/skipped | 25 | 1 |
| CP-5 | S2 | CP-3 | deploy-script-linux | `/*/*/RemoteScriptContractTests/(Deploy_parent_creates_the_github_token_directory_without_reading_it)\|(Deploy_parent_creates_the_codex_home_directory_without_reading_it)\|(Deploy_parent_seeds_or_verifies_runner_checkout)\|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner)\|(C1008_Recycle_exact_default_volumes)\|(Scrub_covers_github_token_prefixes)` | V-15, R-3 | all 6 methods; 0 failed/skipped | 6 | 3 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c0817-docs/` | rotation-docs-linux | `/*/*/(RefreshGithubTokenScriptTests)\|(RunnerPushCredentialDocsTests)/*` | V-16, V-17, V-18 | all 3 methods; 0 failed/skipped | 3 | 8 |

## Publication and handoff

This plan is pushed on its task branch; the caller lands the Plan task. Code runs S1, S2, S3
with the table above; Review checks the receipts against it. Provisioning (step 2 of the
acceptance procedure) and the rollout are operator work after land, and the card stays open
until the step 3 and step 4 receipts exist. Follow-ups to file only if wanted: a `gh` wrapper
(D-6); a runner status field for the push credential (D-7); retiring the Antiphon deploy key
in favour of the token (out of scope by the brief).
