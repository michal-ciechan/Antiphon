# 2026-11-21 gate 9 retire-temp: `worktree-status status=127` on a review scratch worktree

Debug task 51244fe8. Read-only diagnosis. Nothing was deleted, restarted, deployed, or mounted. No ssh. The production database was not opened.

- Checkout: `/work/worktrees/ws-03-d3`, branch `feat/card-task-51244fe8`, base `910018c2d0f9728a6706d4475adc9b6a0cce9f93`.
- Rollout SHA named by the refusal: `493bee0ee3af2052575bc7a26e9a313669b2fc2d`. The `worktree-status` lines cited below are the same at that SHA and at the base.
- Container this diagnosis could see: hostname `9c46ef549e65`, uid 1654, `git version 2.47.3`. It is not the temp runner. `docker volume ls` in this container has no `antiphon-runner-temp_*` volume. The path below is absent here (`test -e` exit 1).

## Verdict

The refusal is `RecycleGitAuditUnknown` from the temp content audit, not `registry-missing`, not `RecycleWorktreeDirty`, and not `RecycleUnpublishedWork`.

`worktree-status` is not a git subcommand. It is the check name `consider_linked` sets when a linked worktree's `.git` pointer and the admin `gitdir` agree and the checkout directory exists. The command that then runs, and the one whose exit is 127, is:

```text
git -C /tmp/claude-1654/-work-worktrees-ws-01/933a0cf5-8b24-498c-800d-f67966c4382a/scratchpad/cp11-wt status --porcelain --untracked-files=all
```

That runs inside the retire-temp helper container, after `git -C <same> rev-parse --git-path index` has already succeeded. Exit 127 is the shell's command-not-found status. Git is on the helper image, so this is not "git is missing." The temp volume's `config`, `config.worktree`, and `hooks/` are not mounted in this container, so the missing argv is not named here. Stderr is discarded, which is why the receipt has only `status=127`.

The 04:07Z Ready preview used the same scan. Why that pass did not refuse this path is not visible from here. Three shapes fit the code. They are listed below and are not a timeline.

## What ran

Preview and apply of temp both call `c1008_audit_readonly` (`scripts/c590-remote.sh`). Dry-run prints `C1609_RETIREMENT state=Ready` only after that scan returns 0 and the publication gate passes (`C1609_CONTENT_READY=1`). Inventory mode exits before the dirty drain and is not the Ready path. Apply of temp calls the same helper again (`c1008_audit` to `c1008_audit_readonly`). `HostCaseFailed retire-temp-runner exit=2` is the helper's exit 2.

Each phase resolves the host file `audit-current.json` through `c1609_resolve_current_audit` (`scripts/c1008-retirement.sh`) and stores the image digest on the journal. A later phase resolves that file again. This container cannot read the host file, so an image change between 04:07Z and 04:15Z is possible and unproven. It is not required: a missing `git` would have failed the earlier `rev-parse` as `audit check=index`, and the 04:07Z scan did get as far as Ready.

Helper (`c1008_audit_readonly`), temp project only:

- `docker run --name c1008-preview-<operation>-<token> --rm --cap-drop ALL --network none --read-only --security-opt no-new-privileges --user 1654:1654 --init --entrypoint /bin/bash`
- image: the digest from `audit-current.json` (protocol 2). The session-testing image is built from `docker/session-runner-grok/Dockerfile`, git 2.47.3 at `/usr/local/bin/git` (`NO_GETTEXT=1 NO_TCLTK=1 NO_PERL=1 NO_PYTHON=1 NO_OPENSSL=1`).
- mounts: temp work volume at `/work` (`readonly,volume-nocopy`); temp `runner-tmp` at `/tmp` (`readonly,volume-nocopy`, `C1008_TMP_MOUNT=/tmp`); private tmpfs `/audit-scratch` (`TMPDIR=/audit-scratch`, 2 GiB). No tty.
- environment inside the program: `GIT_OPTIONAL_LOCKS=0 GIT_NO_LAZY_FETCH=1 GIT_TERMINAL_PROMPT=0 GIT_NO_REPLACE_OBJECTS=1`, `GIT_CONFIG_SYSTEM=/dev/null GIT_CONFIG_GLOBAL=/dev/null`, and `GIT_CONFIG_COUNT=2` forcing only `core.commitGraph=false` and `gc.writeCommitGraph=false`. Local repository config is still read. The audit does not pass `-c core.fsmonitor=false`, `-c core.hooksPath=/dev/null`, or `-c diff.external=`.

`fail` prints `audit check=<audit_check> status=<exit> repo=<rel>` and `RecycleGitAuditUnknown`, then exits 2. `c1008_audit_checked` (and the dry-run copy of that case) turns the first `audit check=` line into `RecycleGitAuditUnknown <that line>`. `rel_repo` prints a checkout under the tmp mount by its runner path (`/tmp/...`), which is why the receipt names `/tmp/claude-1654/.../cp11-wt`.

## Why this path is `worktree-status`

`consider_linked` audits every `worktrees/<id>` whether or not `git worktree list` would show it. The `gitdir` file records `<checkout>/.git`. A checkout recorded under the runner's `/tmp` is translated into the read-only tmp mount (`tmp_resolve`) and must stay inside it; otherwise the check is `worktree-confine`. This receipt is not that check, and it is not `registry-missing`.

When the translated checkout exists, is a real directory, and its `.git` file names this same admin, the code reaches:

```text
queue_dirty "$checkout" worktree-status
```

(`scripts/c590-remote.sh`, `consider_linked`, the `agreed=1` branch). An absent checkout does not call `git status`. It only runs `consider_stale_index` (`git --git-dir=<admin> diff-index --cached --quiet HEAD`). A disagreed pointer queues `orphan-status`, and a 127 from that `git status` would be labeled `orphan-status` by `fail`. The observed label is `worktree-status`, so at 04:15Z and 04:22Z the checkout existed and the two pointers agreed.

`queue_dirty` runs `consider_dirty` in a background subshell. Stdout goes to a scratch file. Stderr is discarded. Up to 8 run at once; `reap_dirty` still reports the first failure in queue order, so the pool cannot hide a 127.

Inside `consider_dirty` the order is fixed:

1. `audit_check=index`, then `git ... rev-parse --git-path index`. A missing `git` fails here as `check=index status=127`.
2. `audit_check="$kind"` (`worktree-status`), then `git ... status --porcelain --untracked-files=all`. Failure calls `fail $?`.
3. Only after that does the check become `index-flags` (`ls-files -v`) and `index-content`.

A non-empty porcelain result is `RecycleWorktreeDirty` with `status=0`, not 127. So the tree was not classified dirty. The 127 is the exit of that `git status` (or of a command in the few lines of shell before the next `audit_check=` assignment; with an index present, that command is the status invocation).

`reap_dirty`'s fallback also prints `audit check=worktree-status`, but only when the child exits non-zero and its stdout contains no `audit check=` line. `fail` prints that line and exits 2, so the child stdout matches and is reprinted. The receipt's `status=127` is the argument `fail` was given, not the child's wait status. A local bash 5.2 reproduction of a missing command after `audit_check=worktree-status`, with the same `fail` and the same subshell redirection, prints this exact diagnosis shape.

## What does not reproduce 127

Probes in this container (git 2.47.3, the image's git; `python3` absent, `/usr/bin/perl` present, `git-lfs` absent). `git status --porcelain --untracked-files=all` exited 0 in each of these, including when stderr said the helper was missing:

- `core.fsmonitor` pointing at a missing binary
- a `post-index-change` hook and an fsmonitor hook whose shebang is `python3`
- `diff.external` pointing at a missing binary (`git diff` fails; `git status` does not run it)
- a required clean filter pointing at a missing binary (`git status` does not run it; `git diff` does)
- `alias.status` set to a shell command (status stays the builtin)
- a partial clone and `remote.origin.url=nosuchscheme://...`, with and without `GIT_NO_LAZY_FETCH=1`

`git-status` in this image is a symlink to the git binary, not a perl script. The `NO_PERL=1` helpers print `fatal: git was built without support for ...` and exit 128, not 127. A git fatal (dubious ownership, missing object) is 128. Those probes show the class: repository config can name a program the image does not have, the audit runs `git status` with that config in force, and the audit throws the message away. They do not identify which program this worktree named. Do not treat a guessed binary as the cause.

`InstallPreviewHook` in `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs` installs `core.fsmonitor` and expects the preview helper to execute it. The audit running repository-configured commands is current behavior, not an accident of this incident.

## Why 04:07Z was Ready and 04:15Z / 04:22Z were not

The 04:07Z dry-run printed `C1609_RETIREMENT state=Ready operation=c10087a20669a75ff4e35bb65eae1660d5a23`. That requires the full content scan, not inventory. The scratch worktree is reported to have existed since about 03:15Z. This container cannot see the temp volume, so what changed by 04:15Z is not proven. The code allows only these shapes:

1. The checkout directory was absent on the tmp mount at 04:07Z, so only `consider_stale_index` ran, and it was present at 04:15Z. `git worktree remove` is not required for absence: deleting the directory and leaving the admin is the prunable state. A directory removed and recreated between the two runs fits. Continuous presence since 03:15Z does not.
2. The pointers disagreed at 04:07Z (that path is `orphan-status` or stale-index) and agreed later. A 127 on the orphan path would be labeled `orphan-status` if `fail` ran. The later label `worktree-status` means the agreed path.
3. `git status` exited 0 at 04:07Z and 127 at 04:15Z because repository-local config, a hook, or another program status execs appeared in between. That state is on the temp volume.

Scan order and the 8-wide pool cannot drop a 127. A full scratch tmpfs was a different incident (`for-each-ref` status 128, `docs/investigations/2026-10-17-redeploy-audit-for-each-ref-128.md`). `docs/investigations/2026-11-18-retire-temp-git-audit-refusal.md` is `withdrawn-ref` / `host-ls-remote`. Neither is this line.

## Unpublished, and the sanctioned clear

This refusal died in the content scan, before `consider_common`. It is not an unpublished verdict.

Publication, when the scan gets that far, is `consider_tips` plus the reflog walk: every object from `for-each-ref` and `HEAD` of the common dir and of each linked admin (detached HEAD included), and both sides of every reflog entry, must be contained in the host proof's advertised heads. Temp v2 does not fetch. `c1609_unbundle_host_proof` is the graph. `rev-list --count` of tips not reachable from those heads must be 0, or `refuse_unpublished` prints `RecycleUnpublishedWork`. Salvage refs `refs/antiphon/salvage/<guid>/<oid>` count only in v2, and only when the suffix equals the advertised OID. Equal trees, patch-ids, and task completion do not publish (`docs/docker-stack.md`, the temp/v2 paragraphs under the retire-temp audit).

In this clone, with no fetch during this task, `5e8d13105048f2467514ba0a6688c52ecb7ba04e` is the tip of `origin/feat/card-task-c8aa09fc` (commit time 2026-11-21 02:16:52 +0000). `git merge-base --is-ancestor` exits 0. `git branch -r --contains` also lists `origin/feat/card-task-c1840169` (tip `ca343bc7f18ad544af97db8552e8993497184e3c`) and `origin/feat/card-task-e72b1802` (tip `6946e9a15dc4472881ba8f1f5b805312a9705485`). The loose ref file for `origin/feat/card-task-c8aa09fc` was updated 2026-11-21 04:23:45 +0000. That is this clone's remote-tracking ref. The audit does not consult it. Containment here does not prove the host proof bundle advertised that OID. The worktree's reflog was not readable here; another tip in that admin would still be a publication root.

These do not clear the audit:

- `POST /api/agent-tasks/c1840169/worktree-retirement` records release and does not delete (`docs/ops-http.md`, "Release a settled worktree"). The path is not the task worktree. It is a linked worktree the review delegate created under its scratchpad.
- Runner temp sweep (`RunnerTempSweep`, ships off). Family `claude-scratch` matches only `^claude-1654/-work-worktrees-task-[0-9a-f]{8}\z`, depth 2, `DefaultEnabled: false`, `RequiresMirrorAbsent: true`. This path is slot `ws-01` and is nested under `scratchpad/cp11-wt`. Unregistered names are reported and never removed.
- `scripts/remove-review-scratch.ps1` removes one container that script created, refuses a `.git` that is not directly under a top-level child, and will not remove this nested worktree.

Sanctioned removal is `git worktree remove` with no `--force`, from the owning repository, as uid 1654, on the temp volume (the server2 host, volumes `antiphon-runner-temp_work` and `antiphon-runner-temp_runner-tmp`). Not from this container. Discover the admin from the checkout; do not guess the worktree id:

```text
checkout=/tmp/claude-1654/-work-worktrees-ws-01/933a0cf5-8b24-498c-800d-f67966c4382a/scratchpad/cp11-wt
git -C "$checkout" rev-parse --git-path hooks
git -C "$checkout" rev-parse --absolute-git-dir
git -C "$checkout" rev-parse --git-common-dir
```

The last command is the owning common dir. Its main worktree is `git --git-dir=<common> rev-parse --show-toplevel` when that common dir is a main `.git`. Before remove:

- `git --git-dir=<admin> diff-index --cached --quiet HEAD` must exit 0. That is the audit's check for an absent checkout.
- Read `HEAD` and both reflog sides (`git --git-dir=<admin> reflog show --all`, and the same for any linked admin that records this checkout). Every commit must be reachable from the host proof's advertised heads, the same comparison `rev-list` uses. This clone's `origin/feat/card-task-c8aa09fc` is not that proof. If any tip is absent from the proof, stop. That next refusal would be `RecycleUnpublishedWork`, which stays stopped.
- Then, from another worktree of that same repository (not from `cp11-wt` itself):

```text
git -C <main-or-other-worktree> worktree remove -- "$checkout"
```

No `--force`. If that command exits 127, stop. Do not delete the directory and do not pass `--force`. The same missing program would be blocking the sanctioned tool, and removing the admin would drop its HEAD and reflog from the publication set.

After a successful remove, preview again from the canonical desktop checkout:

```text
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d -Phase retire-temp -DryRun
```

Ready expires after 900 seconds. Apply uses that preview's token. A code change on this branch does not enter the helper until `audit-current.json` names an image that contains it.

## Other scratch worktrees

The temp runner's registry and `cp11-wt` are not visible from this container. The next `worktree-status` victim on that volume cannot be listed here. This container's `/tmp/claude-1654` is a different volume (this runner). Nested scratchpad checkouts on it are evidence that delegates create them. They are not the temp audit's queue. A `.git` directory sitting under a registered tmp checkout refuses `dot-git` / `git-dir-shape` (`consider_tmp_git`), which is a different check from `worktree-status`.

## Proposed card

Title: retire-temp content audit hides a `git status` command-not-found as `worktree-status status=127`.

The temp helper's `consider_dirty` (`scripts/c590-remote.sh`, the `git status --porcelain --untracked-files=all` line, 7216 at `493bee0ee3af2052575bc7a26e9a313669b2fc2d` and at `910018c2d0f9728a6706d4475adc9b6a0cce9f93`) runs repository hooks, fsmonitor, attributes, external diff, and submodule recursion, and discards stderr twice: on that git invocation and on the `queue_dirty` subshell. `fail` records only the number. Runner inspection already disables those commands (`RunnerWorkspaceService` inspection block: `core.fsmonitor=false`, `core.hooksPath=/dev/null`, `core.attributesFile=/dev/null`, `diff.external` empty, `submodule.recurse=false`). `CheckpointProcessRunner.HardenGit` passes the same overrides as `git -c`. The audit's `GIT_CONFIG_COUNT=2` sets only the two commit-graph keys.

Smallest change:

- On the `consider_dirty` git invocations (status, `ls-files`, `hash-object`), pass `-c core.fsmonitor=false -c core.hooksPath=/dev/null -c diff.external= -c core.attributesFile=/dev/null -c submodule.recurse=false` so repository config cannot exec a program the image does not have. Command-line `-c` is what the checkpoint tool uses; do not only append `GIT_CONFIG_COUNT`, which the audit already uses for commit-graph.
- Keep a short redacted stderr tail on the `fail` suffix (the credential-shaped redaction `rel_repo` already applies to paths). `queue_dirty` must not discard that tail. Cap it and strip newlines.
- Do not skip a scratchpad linked worktree. It is a real `worktrees/<id>` entry. Skipping it would hide unpublished tips and dirty trees. `consider_tmp_git` already refuses a nested `.git` directory.

Tests: `InstallPreviewHook` uses `core.fsmonitor` as the read-only witness that the helper cannot remount the volume. After fsmonitor is disabled that hook does not run; move the witness off repository hooks. Add a fixture whose `core.fsmonitor` or hook names a missing interpreter and assert the audit no longer ends as `worktree-status status=127`. Add a fixture where `git status` fails and the receipt keeps the redacted stderr token.

This incident's clear is the `git worktree remove` above, not this card. The card stops the next scratch worktree from failing the gate with no command name. It does not authorize deleting unpublished tips.
