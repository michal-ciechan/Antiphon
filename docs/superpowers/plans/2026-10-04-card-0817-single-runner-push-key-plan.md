# CARD-0817: one runner push key for admitted repositories

Plan and verification design, 2026-10-04. Source inspected:
`49981e134198957f8662f7573cbd6b7332ae690a`. Live CARD-0817 and CARD-0816 were read
with `scripts/card.ps1 get ... -Board Antiphon`.

## Outcome and boundaries

Use one dedicated GitHub machine-user SSH key for Antiphon and markdown-package,
reusing the existing file mount, tmpfs staging and SSH configuration. Add exact
repository entries to the existing runner admission setting so the production
allow-list can match the machine user's write grants. Keep anonymous HTTPS
fetches, the secondary receive-pack probe, and Antiphon's primary checkout
verification.

This artifact is complete under the defaults in D-1 through D-8. **Next: decide**:
the operator's single-key preference was relayed by the markdown-package
orchestrator and has not been confirmed in the Antiphon session. Confirm D-1 and
the initial repository set before commissioning Code/provisioning. The choice is
cheap to redirect: no secret, account, grant, deployment, or implementation is
created by this Plan. Verification design is folded in below; no separate
TestDesign stage is needed if these defaults are accepted.

Plan and Code must not create, request, read, print, hash, upload, download or
install a real credential. Provisioning, vault custody and account grants are
operator work under [agent-credentials.md](../../agent-credentials.md#server2-runner-credentials-card-0604).
Code tests use inert files and local repositories only. Live acceptance is a
separate, explicitly commissioned operator/rollout activity after provisioning.

## Ground truth

| Card assumption | What the inspected code or live card does | Consequence |
|---|---|---|
| CARD-0816 Done might supply credential support | Its close reason is “Duplicate of CARD-0817 ... Nothing shipped.” | There is no implementation to reuse from that card. |
| One GitHub deploy key can cover several repositories | GitHub attaches a deploy key to one repository; the official documentation forbids reuse across repositories. | Use a machine account, or redesign around a GitHub App. |
| A separate key route is needed for markdown-package | `docker/session-runner-grok/gitconfig` rewrites every GitHub HTTPS **push** to `git@github.com:`; `ssh_config` already selects one `IdentityFile /run/antiphon/deploy-key`. | Replacing the identity behind this slot is sufficient for Git transport; no per-repository aliases or credential map. |
| The runner allow-list is a finite repository inventory | `PhoneHomeSettings.AllowedCloneSources` defaults to `https://github.com/michal-ciechan/`; validation requires slash-terminated HTTPS prefixes. `RepositoryCloneSource.IsAdmitted` uses prefix matching plus an unconditional primary-repository exception. | Add exact entries and explicitly configure the two approved repositories. Preserve the primary exception and include Antiphon in the grant inventory. |
| Configuring an indexed list necessarily replaces defaults | Production uses the .NET configuration binder on a property initialized with a nonempty collection. Replacement has not been measured in this Plan. | Pin replacement with a binding test; avoid a populated collection initializer in the new binding design (D-3). |
| The blocker is anonymous fetch | `RunnerWorkspaceService.ProbePushAccessAsync` resolves the effective push URL and runs a unique receive-pack dry run before a secondary task worktree is created. A clone may already exist when it refuses. | Keep the probe enabled; public readability is insufficient. |
| Every mirror request reprobes credentials | Exact existing worktree/SHA reuse returns before the probe; primary repositories are also exempt. | A new secondary task/mirror is required for live admission proof; an existing session or primary health check is insufficient. |
| The refusal identifies only a bad credential | Any nonzero push probe, including network failure, maps to HTTP 409 `phone_home_repository_push_unauthorized`; raw probe stderr is not returned. | Retain the code and conservative refusal, improve only the remedy text, and distinguish operational causes during diagnosis. |
| deploy-parent proves primary write access | `scripts/c590-remote.sh::verify_runner_checkout` validates path, origin, anonymous fetch/FETCH_HEAD and mounted commit identity. The separate `case_git_smoke` makes a real Antiphon push. | Leave primary verification unchanged; explicitly verify both repositories' writes after migration. |
| Changing the host key file updates running containers | The entrypoint copies it to a tmpfs at boot. Read-only file binds can retain an old inode after atomic replacement. | Recreate each runner through the rolling phases; do not infer activation from the host file. |
| New mounts are harmless | `c1008_compose_model` pins the exact secret/mount roster for recycle/retire safety. | Reuse the existing key slot and paths, avoiding changes to rolling topology or cleanup authorization. |

Evidence owners: `src/Antiphon.SessionRunner/PhoneHomeSettings.cs:37`,
`src/Antiphon.SessionRunner.Contracts/RepositoryCloneSource.cs:78`,
`src/Antiphon.SessionRunner/RunnerWorkspaceService.cs:100` and `:185`,
`docker/session-runner-grok/dind-entrypoint.sh:45`,
`docker-compose.server2-runner.yml`, `scripts/c590-remote.sh::ensure_runner_boot_files`,
`verify_runner_checkout`, `case_git_smoke`, `c1008_compose_model`, and
[CARD-0812 D-8](2026-09-29-card-0812-per-repository-runner-mirror-plan.md#d-8-a-secondary-repository-must-be-pushable-before-its-mirror-exists).

## Decisions

### D-1: default to a dedicated machine-user SSH key, pending operator confirmation

A deploy key belongs to one repository. A machine user can hold one SSH key and
be granted access to several repositories. This is the smallest fit for the
existing SSH transport. Give the dedicated account write access only to Antiphon
and markdown-package initially; no personal operator account, organization-wide
team, administrative grant or ruleset bypass. Personal-repository collaborator
access includes read/write. These constraints and the deploy-key limitation are
documented by [GitHub](https://docs.github.com/en/authentication/connecting-to-github-with-ssh/managing-deploy-keys).

A GitHub App is the alternative if the operator wants finer permissions or
short-lived credentials. Its private key signs authentication requests; Git uses
installation tokens over HTTPS with Contents permission, not that private key as
an SSH identity. It would require token minting/refresh and a credential helper,
so it is not a drop-in replacement for this design. See
[GitHub App installation authentication](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation).

Rejected: registering the same deploy key on multiple repositories (unsupported),
per-repository keys (contrary to the relayed direction), a personal account key
(unrelated repository access), and an App implementation in this slice (larger
transport/custody change). If the operator chooses an App, retain the scope and
probe requirements but return this plan for a credential-transport amendment.

### D-2: reuse the existing key slot and non-secret Git/SSH wiring

New vault item: `antiphon/server2/github-push-key`, holding the private SSH key as
a protected file attachment, with account name, public fingerprint, repository
inventory and rotation date as metadata. No private material in ordinary notes.
The operator materializes it into the existing host `secrets/deploy_key` file;
the legacy basename and Compose secret name are compatibility names, not a
requirement that the key remain a GitHub deploy key.

| Hop | Contract |
|---|---|
| Vault to host | Operator-only approved vault relay/file transfer; owner-only host file, mode 0600. No key in argv, environment, transcripts, evidence or image context. |
| Host to container | Existing `ANTIPHON_DEPLOY_KEY_FILE` names a **path**; Compose file secret `antiphon-deploy-key` mounts read-only at `/run/secrets/antiphon-deploy-key`. |
| Container staging | Existing root entrypoint stages `/run/antiphon/deploy-key`, mode 0400 uid/gid 1654, on `/run/antiphon` tmpfs. Missing/empty source retains `DeployKeyMissing`, exit 3. |
| Git | `/etc/gitconfig`: `core.sshCommand = ssh -F /etc/antiphon/ssh_config`; `url."git@github.com:".pushInsteadOf = https://github.com/`. No `insteadOf` rule, credentialed URL or per-repo `pushurl`. |
| SSH | `/etc/antiphon/ssh_config`: `Host github.com`, `HostName ssh.github.com`, port 443, user git, the one staged `IdentityFile`, `IdentitiesOnly yes`, `BatchMode yes`, strict checking and existing pinned known-hosts file. |

Use the deploy-key staging pattern and the Claude file-mount/vault custody
pattern; **do not copy Claude's environment export** for the SSH private key.
The mounted global gitconfig remains the commit-identity owner and must not carry
overrides that replace this SSH command or rewrite rule. Detect any local
`remote.origin.pushurl`/`core.sshCommand` override during operator qualification;
resolve it explicitly rather than silently overwriting task configuration.

Rejected: a new secret mount, alias, runtime selector, vault-reading daemon, or
`RefreshPushKey` deploy switch. These add code and rolling-topology obligations
without making this single-identity route work better.

### D-3: extend the existing allow-list with exact sources and configure a finite set

Keep `PhoneHome:AllowedCloneSources` and its legacy slash-prefix semantics. Add
canonical anonymous HTTPS repository entries ending in `.git`, matched with
ordinal equality only. For example, `.../markdown-package.git` must not admit
`.../markdown-package.git-extra` or another repository under the same owner.
Validate exact entries with `RepositoryCloneSource.TryNormalize` and require
equality with the canonical result, HTTPS, no userinfo/query/fragment, and no
noncanonical port or escaped spelling. Keep slash-prefix compatibility and local
path-prefix support used by the existing isolated test seam.

Make the settings property nullable/unset by default and resolve the legacy
owner prefix only when it is unset, in the policy projection/validation helper.
An explicitly supplied collection replaces that default; an explicit empty
collection allows only the primary. Do not use a getter that exposes a populated
default to the binder. Pin omitted, explicit, empty and indexed configuration
binding in tests; preserve existing unconfigured deployments' behavior.

The shipped production Compose service sets exactly:

```yaml
PhoneHome__AllowedCloneSources__0: https://github.com/michal-ciechan/antiphon.git
PhoneHome__AllowedCloneSources__1: https://github.com/michal-ciechan/markdown-package.git
PhoneHome__ProbeSecondaryRepositoryPushAccess: "true"
```

The temp override inherits these entries. Effective admission is the exact set
plus the primary exception; include the normalized primary in the inventory and
in the account's grants. Do not change the primary clone URL's existing case or
path: normalization is for comparison, not a rewrite of deploy-parent constants.

Rejected: keeping the owner-wide prefix in the production deployment (does not
express the approved finite set), inventing another policy registry/API, or
granting the machine account every present/future repository owned by that user.

### D-4: reconcile grants and admission through an operator-owned inventory

The exact production Compose entries are the versioned desired inventory. Before
each change/rotation, the operator compares that effective set, including the
primary, with the machine account's complete repository write grants, including
team/organization-inherited grants. Record repository names, access levels and
the public key fingerprint only. Steady state requires equality. No new GitHub
admin credential is installed in the runner to automate this reconciliation.

For an addition: obtain operator approval, grant the same account write access
to that repository, commit/review the exact entry, roll out, then prove a fresh
mirror and branch push before accepting its work. Use the same SSH key. During
this transition the account can temporarily access the additional repository;
record and minimize that interval. For removal: stop new work for that
repository, finish/push its owned tasks, revoke its grant, then remove the entry
and roll out. A configured but ungranted repository fails closed at the existing
probe. An over-granted account requires grant revocation; admission alone does
not constrain an arbitrary Git command in an agent shell.

The initial two-repository set is a stated default, not a claim that today's
owner-prefix admits no other active projects. Before rollout, inventory active
secondary projects/tasks; any additional required repository needs an explicit
approved entry and matching account grant. Do not silently strand it.

### D-5: keep the receive-pack gate and stable refusal contract

No new probe, bypass, fallback identity or retry loop. The existing secondary
probe resolves the same effective push URL used by the task and executes:

```text
git push --dry-run --no-verify --porcelain -- <effective-push-url> HEAD:refs/heads/antiphon-push-access-probe-<guid>
```

Success permits branch fetch/tip verification and mirror creation. Nonzero
continues to return HTTP 409 `phone_home_repository_push_unauthorized`, with no
task worktree or raw stderr. Change only the remedy to:
`Runner cannot push to {identity}; check the runner push key and its repository access.`
This avoids telling operators to register a separate credential for every repo.
The existing backoff/Queued behavior is unchanged. An unlisted repository fails
earlier with `phone_home_repository_not_admitted`, before any Git process.

A dry run proves transport/receive-pack access; it does not establish that a
particular real update passes every server hook/ruleset. Acceptance also needs a
real fast-forward task-branch push. Reused worktrees are not newly probed; after
revocation/rotation inspect outstanding owners rather than assuming this gate
protects existing sessions. Committed-but-unpushed work remains the owner's
responsibility; cleanup redesign is outside this credential card.

### D-6: migrate all runner pushes, retain the primary verification and rollback

The operator generates a **new** machine-account key; do not try to re-register
the existing deploy public key while it remains attached to Antiphon. Keep the
old Antiphon key in an owner-only host rollback location, outside image/evidence
roots, retaining its original host-only custody. Replace the host key/public-key
pair together only after the new vault item and account grants are ready.

Keep `ensure_runner_boot_files`, its legacy missing-key generation behavior,
`verify_runner_checkout`, `RunnerRepository`, `RunnerCloneSource`, and
`RunnerVerificationWorkspace` behavior unchanged. Operator preflight must prove
the provisioned key file is nonempty **before** any deploy, so the legacy
bootstrap generator cannot silently create an unregistered replacement. Never
run that bootstrap path as a Plan/Code test.

After host replacement, the already-running primary retains its old staged key;
the newly deployed temp gets the new machine key. Qualify both repository writes
on temp before handoff. Follow every named phase and stop gate in
[the rolling runbook](../../docker-stack.md#staged-server2-rolling-rollout-card-0934),
then qualify upgraded main. No direct container restart, `-Phase all`, or
worktree deployment. Keep the old deploy registration during this bounded
overlap so existing primary tasks can finish. After all old containers/sessions
are retired and both live proofs pass, the operator revokes the old deploy key
on Antiphon and retires its host rollback copy.

Before revocation, rollback restores the old host key/public-key pair and uses
the documented rolling recovery; secondary admission returns to its previous
credential refusal. Old runner binaries cannot accept exact entries: restore
the prior reviewed Compose/configuration with the old image if rolling back
code too. After revocation, rollback needs explicit operator credential repair;
do not auto-generate a replacement or weaken the push gate.

### D-7: state blast radius and rotate the identity as a unit

All tasks under the runner UID can use the mounted key. A compromised runner or
task can write every repository granted to that account; the application
allow-list is an admission boundary, not a per-session credential sandbox. Both
rolling containers share this identity. Read access also follows the account's
grants, although normal fetch remains anonymous; this plan does not enable
private-repository fetch. Protected branches/rulesets still apply, and no bypass
is granted. A machine-account key does not supply branch-only privileges.

Rotation: operator creates/registers a replacement account SSH key, stores a new
version of the vault attachment, securely replaces the host key/public pair,
rolls temp then main with both-repository probes and branch proofs, then revokes
the previous account key. A temporary overlap is solely for rotation; one
active push identity remains afterward. Compromise response revokes the key
immediately, holds affected work, and reconciles every granted repository.
Record public fingerprints/dates, never private-key contents or hashes.

### D-8: lanes follow live defaults; provisioning is not a Code checkpoint

Read on 2026-10-04: `/api/runner-defaults` revision 2 had global `server2`, no
kind overrides; `/api/session-runners` showed eligible Linux main (9/10 seats),
eligible Windows desktop (0/2), and offline/drained temp. These are observations,
not dispatch pins or reservations. Re-read both routes before subsequent work.

S1 has an Any lane; S2's native Git/SSH/Compose tests use a Linux lane with
Compose CLI available and no Docker daemon required. Omit `-Runner` for ordinary
Code/Review; specify `-Platform Linux` only for the Linux-dependent slice (or
omit if already resolved there). `-Platform Any` clears a stale OS pin. The live
rollout alone pins each specific runner being qualified. No fleet address is
embedded in a test or checkpoint command.

## Implementation slices

### S1: exact repository admission and credential-neutral refusal (45-60 minutes)

Files:

- `src/Antiphon.SessionRunner.Contracts/RepositoryCloneSource.cs`: exact versus
  slash-prefix matching, retaining normalized primary and local test behavior.
- `src/Antiphon.SessionRunner/PhoneHomeSettings.cs`: exact-source validation,
  unset/default resolution and collection replacement semantics.
- `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs`: D-5 remedy text only;
  leave the probe, primary path and reuse ordering unchanged.
- `tests/Antiphon.SessionRunner.Tests/RepositoryCloneSourceTests.cs`: add
  `Exact_sources_admit_only_the_named_repository` (one unparameterized case).
- `tests/Antiphon.SessionRunner.Tests/RunnerPushRepositoryAdmissionTests.cs`
  (new): the four named settings/binding tests below.
- `tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests.cs`: add
  `Mirror_refuses_an_unlisted_sibling_before_any_git`; strengthen the existing
  probe-refusal test's diagnostic assertion. Use the existing process seam and
  assembly-local process limiter; no external GitHub traffic even under mutation.

Commit/push S1 before CP-1/CP-2. These checkpoints close this slice. No API, DB,
desktop dispatcher, runner capability or secret-handling change.

### S2: deployment inventory, custody runbook and narrow transport guards (45-60 minutes)

Files:

- `docker-compose.server2-runner.yml`: exact entries and explicit enabled probe
  from D-3. `docker-compose.server2-runner.temp.yml` inherits them unchanged.
- `docs/agent-credentials.md`: machine-key vault custody, legacy slot names,
  grant reconciliation, operator-only migration/rotation and acceptance commands.
- `docs/docker-stack.md`: migration preflight and both-runner qualification
  reference within the existing rolling sequence.
- `docker/stack.env.example`, `docker/session-runner-grok/gitconfig`,
  `docker/session-runner-grok/ssh_config`,
  `docker/session-runner-grok/dind-entrypoint.sh`: comments only where they claim
  the deployed credential must always be an Antiphon deploy key. No new mounts,
  export, runtime branch or file name. Keep daemon/script text ASCII.
- `tests/Antiphon.Tests/Infrastructure/RunnerSinglePushKeyContractTests.cs`
  (new): two unparameterized tests below. Reuse
  `scripts/fixtures/c994-production-compose-model.mjs` to materialize main and
  temp configurations with inert files; no running containers or real secrets.

Commit/push S2 before CP-3/CP-4/CP-5. No edit to `scripts/c590-remote.sh` or the
deploy-parent primary verification. Review the docs against the implementation;
do not add tests that merely assert the new prose exists. Existing topology and
mount paths remain compatible with recycle/retire guards.

## Operator provisioning and exact acceptance procedure

These steps are a future operator runbook, **not commands for Plan or Code to
execute**. The literal host root remains the one owned by the credential runbook;
`secrets/deploy_key` below is relative to that configured deployment root.

1. Confirm the relayed D-1 direction, the dedicated account, and complete initial
   inventory. Audit active secondary projects before narrowing the production
   list. Manually create/secure the machine account; add it as collaborator only
   on the approved repositories and accept invitations. Do not use an unrelated
   personal login or a broad inherited team grant.
2. In the operator's trusted credential lane, generate a new ed25519 SSH key
   for unattended runner use, with owner-only private-file permissions. Register
   only its public half under the machine account's **SSH authentication keys**,
   not repository Deploy keys. Authorize organization SSO if applicable.
3. Create `antiphon/server2/github-push-key` in the approved vault and attach the
   private file through the secure file/relay workflow. Record the public
   fingerprint and repository inventory as metadata. Transfer from that vault
   attachment directly to an owner-only staging file on the deployment host
   through the approved relay; never paste key bytes or include them in a
   command, environment value, brief or output. Verify access using the public
   fingerprint and file mode, not private-file contents.
4. Retain the original host-only key/public pair in a protected rollback
   location. Atomically replace `secrets/deploy_key` and its `.pub` companion
   with the new pair, owned by the deployment account, private mode 0600. Ensure
   the parent directory is 0700, the source is a regular nonempty file, and the
   matching `.pub` is present so existing deploy evidence names the right
   public identity. Do not delete the old GitHub deploy registration yet.
5. From the canonical reviewed/landed checkout run the existing named rolling
   phases. Before handoff from each runner, run the following command against
   that runner's container. Resolve `runner_container` from the runbook's Compose
   project/service labels; it is a container identifier, not a credential.
   `repository` is the secondary clone created by a fresh mirror attempt. If it
   does not exist yet, commission that attempt first; do not replace a foreign
   or occupied checkout to make the check pass.

```sh
test -n "${runner_container:-}" || exit 2
docker exec -i -u 1654:1654 "$runner_container" sh -s <<'VERIFY'
set -eu
export GIT_TERMINAL_PROMPT=0
test "$(stat -c '%u %a' /run/antiphon/deploy-key)" = '1654 400'
test -r /run/antiphon/deploy-key
for repository in /work/repos/antiphon /work/repos/markdown-package; do
    test -e "$repository/.git"
    fetch_url=$(git -C "$repository" remote get-url origin)
    case "$repository" in
        /work/repos/antiphon)
            case "$fetch_url" in
                https://github.com/michal-ciechan/Antiphon.git|https://github.com/michal-ciechan/antiphon.git) ;;
                *) echo 'UnexpectedPrimaryFetchUrl' >&2; exit 2 ;;
            esac ;;
        /work/repos/markdown-package)
            test "$fetch_url" = 'https://github.com/michal-ciechan/markdown-package.git' ;;
    esac
    push_url=$(git -C "$repository" remote get-url --push origin)
    test "$push_url" = "git@github.com:${fetch_url#https://github.com/}"
    probe_id=$(tr -d '-' </proc/sys/kernel/random/uuid)
    timeout --kill-after=5s 120s git -C "$repository" push --dry-run --no-verify --porcelain -- \
        "$push_url" "HEAD:refs/heads/antiphon-push-access-probe-$probe_id"
    test -z "$(git -C "$repository" ls-remote --heads origin "refs/heads/antiphon-push-access-probe-$probe_id")"
done
VERIFY
```

Each push exits 0 and creates no remote probe ref. Before running it, inspect
`ssh -F /etc/antiphon/ssh_config -G github.com` inside the container and the
repository's effective SSH command; require D-2's host, port, identity and strict
checking. This is configuration inspection only, no `ssh -v`, environment dump
or secret-file read. An SSH authentication banner alone is not push proof.

6. Commission one fresh bounded markdown-package Worktree canary on each rollout
   target when its gate calls for it. A new task/mirror must progress beyond the
   former Queued push refusal and run at its assigned SHA. In its assigned task
   branch, the canary makes one empty test commit and runs the exact sequence
   below. Supply `task_branch` from that task's branch contract; keep its normal
   no-rebase/no-force rule. This is a sanctioned live write, not an offline CP.

```sh
set -eu
test -n "${task_branch:-}"
test "$(git branch --show-current)" = "$task_branch"
git commit --allow-empty -m 'test: CARD-0817 runner push canary'
git push origin "HEAD:refs/heads/$task_branch"
expected=$(git rev-parse HEAD)
observed=$(git ls-remote origin "refs/heads/$task_branch" | cut -f1)
test "$observed" = "$expected"
printf 'CARD-0817 push verified branch=%s commit=%s\n' "$task_branch" "$expected"
```

Retain task ID, runner ID, deployed SHA, repository, branch/SHA, probe exit and
remote containment only. The caller owns normal canary branch/worktree cleanup;
do not land the empty canary commit into the product branch. Also run the
existing sanctioned Antiphon Git smoke or an equivalent assigned-branch push
through the same key. Primary checkout verification remains its original
deploy-parent obligation. Complete main/temp retirement before revoking the old
Antiphon deploy registration and retiring the rollback key.

No live success is claimed by a clean Code checkpoint. The card's deployment
acceptance remains pending until these operator receipts exist.

## Verification design

Offline verification is small enough to fold into this dispatch. Tests below
exercise real configuration binding, local Git and effective SSH/Compose
configuration; they require no real keys, vault, GitHub connection or standing
daemon. New methods are unparameterized unless explicitly stated otherwise.

### Coverage and witnesses

| ID | Test method(s) | Required result |
|---|---|---|
| V-1 | `RepositoryCloneSourceTests.Exact_sources_admit_only_the_named_repository` | Exact source accepts itself; rejects a same-owner sibling, a `.git-extra` suffix and case/noncanonical identity; existing prefix/primary behavior remains covered by the existing class. |
| V-2 | `RunnerPushRepositoryAdmissionTests.Settings_accept_canonical_exact_repository_sources` | Enabled valid settings accept the two exact sources and project exactly them to `RepositoryPolicy`. |
| V-3 | `RunnerPushRepositoryAdmissionTests.Settings_refuse_malformed_exact_repository_sources` | Invalid exact entries (userinfo, query, fragment, port, escaped/noncanonical spelling, missing `.git`, non-HTTPS) refuse with a setting-name diagnostic and no supplied secret-like sentinel. |
| V-4 | `RunnerPushRepositoryAdmissionTests.Binding_replaces_the_default_owner_prefix` | Bind the production-shaped indexed keys through the same configuration/options path; the resulting policy contains exactly two entries and no owner-wide default prefix. |
| V-5 | `RunnerPushRepositoryAdmissionTests.Unset_and_empty_sources_preserve_distinct_defaults` | Omitted configuration retains the legacy prefix; explicitly empty collection admits only primary. Exercise actual empty-array configuration as well as direct settings. |
| V-6 | `RunnerWorkspaceServiceTests.Mirror_refuses_an_unlisted_sibling_before_any_git` | Named same-owner sibling refused with RepositoryNotAdmitted; process-start list empty and no worktree created. A mutation cannot contact GitHub: any unexpected start is redirected to a bounded local test process. |
| V-7 | `RunnerWorkspaceServiceTests.Mirror_refuses_a_secondary_repository_the_deploy_key_cannot_push_to` | Existing readable local origin still fails the injected write denial with RepositoryPushUnauthorized; no task worktree; exact dry-run argv; new shared-key remedy, no raw stderr. |
| V-8 | `RunnerSinglePushKeyContractTests.Compose_binds_the_exact_inventory_for_main_and_temp` | Materialize both real Compose files with inert sources, bind each environment into PhoneHomeSettings, assert exact desired inventory, probe enabled and unchanged single key/phone-home read-only secret topology. Run Compose config only, never up/build. |
| V-9 | `RunnerSinglePushKeyContractTests.Git_push_uses_one_identity_while_fetch_stays_https` | Create two local scratch configs with the actual baked system gitconfig. Query effective fetch/push URLs without network and effective `ssh -G` with the actual SSH config. Both repositories select the same one IdentityFile, SSH host/443, batch/identities-only/strict checking; fetch URLs remain HTTPS. Isolate inherited user/system Git configuration. |
| R-1 | `RunnerWorkspaceServiceTests.Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds`, `Mirror_refuses_a_repository_outside_the_allowed_clone_sources`, `Mirror_creates_worktree_on_branch_at_sha`, `Publish_pushes_only_own_fast_forward_branch` | Existing receive-pack success creates no probe ref; outside scope refuses; primary mirror and task-branch publishing remain intact, using local origins only. |
| R-2 | `DindRunnerContractTests.Entrypoint_refuses_missing_deploy_key`, `Entrypoint_never_prints_the_key`, `Ssh_config_pins_identity_and_known_hosts`, `Ssh_config_uses_port_443`, `Gitconfig_pushes_over_ssh_only` | Existing file custody and fixed SSH transport regressions remain passing. |
| R-3 | `RemoteScriptContractTests.Deploy_parent_seeds_or_verifies_runner_checkout` | Primary source/path, anonymous fetch, identity and refusal checks remain unchanged. |

Live requirements in the operator procedure are separate from this offline
roster. Dry-run success alone cannot discharge the real task push requirement.

### Positive controls for Mutation

One mutation per behavior, restored before the next dependent mutation. Run
only the detecting method for its red/green cycle, with nonzero test counts and
the expected assertion failure. A timeout, fixture/build error or missing
executable is not red. Use per-cycle ignored receipts and the SourceLanding
custody/restoration rules when that stage is commissioned.

| PC | Change to production behavior | Detecting filter | Expected red |
|---|---|---|---|
| PC-1 | Match an exact source with StartsWith instead of equality | `/*/*/RepositoryCloneSourceTests/Exact_sources_admit_only_the_named_repository` | Suffix repository is incorrectly admitted. |
| PC-2 | Remove exact-source acceptance from settings validation | `/*/*/RunnerPushRepositoryAdmissionTests/Settings_accept_canonical_exact_repository_sources` | Valid exact inventory is rejected. |
| PC-3 | Bypass exact-source validation | `/*/*/RunnerPushRepositoryAdmissionTests/Settings_refuse_malformed_exact_repository_sources` | Invalid-source refusal assertion fails. |
| PC-4 | Append the legacy owner prefix when projecting an explicitly configured list | `/*/*/RunnerPushRepositoryAdmissionTests/Binding_replaces_the_default_owner_prefix` | Exact two-entry policy assertion fails. |
| PC-5 | Treat an empty list as unset | `/*/*/RunnerPushRepositoryAdmissionTests/Unset_and_empty_sources_preserve_distinct_defaults` | Secondary admission is incorrectly restored. |
| PC-6 | Bypass named-repository admission before Git | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_an_unlisted_sibling_before_any_git` | Admission-code or zero-start assertion fails through the bounded local seam. |
| PC-7 | Restore the old per-repository registration remedy | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_a_secondary_repository_the_deploy_key_cannot_push_to` | Shared-key diagnostic assertion fails. |
| PC-8 | Remove --dry-run from the existing probe | `/*/*/RunnerWorkspaceServiceTests/Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds` | Exact dry-run argv assertion fails; the local bare origin also contains a probe ref if that assertion is reached. |
| PC-9 | Replace production exact entries with the old owner prefix | `/*/*/RunnerSinglePushKeyContractTests/Compose_binds_the_exact_inventory_for_main_and_temp` | Effective inventory equality fails. |
| PC-10 | Replace pushInsteadOf with insteadOf in baked gitconfig | `/*/*/RunnerSinglePushKeyContractTests/Git_push_uses_one_identity_while_fetch_stays_https` | Fetch URL is incorrectly SSH. |

### Execution and cost

Commit and push each slice before its checkpoint group. Run the checkpoint tool
through the host build-slot gate, substituting that clean committed source SHA:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0817-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-04-card-0817-single-runner-push-key-plan.md --rows CP-1,CP-2 --expected-source-sha <S1-sha>
```

For S2 select CP-3,CP-4,CP-5 with its SHA. Await every run; if the tool exits 75,
use its `wait` command until complete. Do not edit source while a run is active.
Each row's driver obtains its own build slot; exit 4 is not permission to bypass
the gate. Retain unedited CHECKPOINT lines and SHA-qualified receipts outside
Git. Remove producer-owned `bin-c0817-*/` outputs when safe. Code/Review run
`scripts/check-evidence-diff.ps1` over the full task range.

This is a narrow Final/Full affected scope: no whole Unit, whole assembly, E2E,
Pty-provider or live credential runs. No unlisted rebuild or test run without a
stated reason. The shell/Compose child tests carry
`ParallelLimiter<ProcessSpawnLimit>` in Antiphon.Tests. No simultaneous Pty suite.

Estimated offline checkpoint floor: 12 minutes; authoring 90-120 minutes across
two 45-60 minute Code slices. Mutation: roughly 20-35 minutes for ten method-only
cycles, reported separately. Live rollout/provisioning has its own gates and is
not part of either Code slice's duration.

### Checkpoints

Lane is stated in each Group cell to keep the checkpoint import schema valid.
CP-1/CP-2: Any with Git; CP-3/CP-4/CP-5: Linux with Git, OpenSSH, Node and Compose
CLI. The second lane does not need Docker daemon access.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c0817-admission/` | admission-any | `/*/*/(RepositoryCloneSourceTests)\|(RunnerPushRepositoryAdmissionTests)/*` | V-1, V-2, V-3, V-4, V-5 | 26 results: 21 existing clone-source, 1 new exact-source, 4 new settings; 0 failed/skipped | 26 | 3 |
| CP-2 | S1 | CP-1 | workspace-any | `/*/*/RunnerWorkspaceServiceTests/(Mirror_refuses_an_unlisted_sibling_before_any_git)\|(Mirror_refuses_a_secondary_repository_the_deploy_key_cannot_push_to)\|(Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds)\|(Mirror_refuses_a_repository_outside_the_allowed_clone_sources)\|(Mirror_creates_worktree_on_branch_at_sha)\|(Publish_pushes_only_own_fast_forward_branch)` | V-6, V-7, R-1 | all 6 named methods; 0 failed/skipped | 6 | 1 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c0817-contracts/` | configuration-linux | `/*/*/RunnerSinglePushKeyContractTests/*` | V-8, V-9 | both named methods; 0 failed/skipped | 2 | 6 |
| CP-4 | S2 | CP-3 | custody-linux | `/*/*/DindRunnerContractTests/(Entrypoint_refuses_missing_deploy_key)\|(Entrypoint_never_prints_the_key)\|(Ssh_config_pins_identity_and_known_hosts)\|(Ssh_config_uses_port_443)\|(Gitconfig_pushes_over_ssh_only)` | R-2 | all 5 named methods; 0 failed/skipped | 5 | 1 |
| CP-5 | S2 | CP-3 | primary-checkout-linux | `/*/*/RemoteScriptContractTests/Deploy_parent_seeds_or_verifies_runner_checkout` | R-3 | 1 named method; 0 failed/skipped | 1 | 1 |

## Publication and handoff

Publish this plan on its assigned task branch first. The caller lands the settled
Plan task promptly using `scripts/delegate.ps1 -Land <plan-task-id>
-ExpectedSourceSha <pushed-plan-sha>` from its authorized lane; the Plan delegate
does not push master or rebase its assigned branch. A pushed plan is not a
deployment and does not establish GitHub grants.

After the operator confirms D-1 and the initial inventory, dispatch S1 then S2
using this verification design. Keep provisioning and live rollout as explicit
operator follow-up. If the direction changes to a GitHub App, amend D-1/D-2 and
their transport verification before Code; admission and live acceptance goals
remain useful. Final handoff for this Plan is `next: decide`, not blocked: the
artifact and executable offline verification design exist under stated defaults.
