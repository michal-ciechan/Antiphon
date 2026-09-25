#!/bin/sh
# Initialize only the named volumes this stack owns. Refuse a foreign owner.
set -eu
uid=1654
gid=1654
# Onboarding and /work/worktrees trust are merged by the runner entrypoint
# (antiphon-seed-claude-onboarding.mjs), which sees this directory as /state/claude.
for d in \
  /state /state/logs /state/keyring /state/check-interpreter /state/diagnose \
  /work /work/repos /work/worktrees \
  /runner-state /runner-state/session-runner /runner-state/pty-hosts /runner-state/logs /runner-state/grok \
  /runner-state/claude
do
  mkdir -p "$d"
done
for d in /state /work /runner-state; do
  owner=$(stat -c %u "$d")
  if [ "$owner" != "0" ] && [ "$owner" != "$uid" ]; then
    echo "ForeignStateOwner path=$d uid=$owner" >&2
    exit 42
  fi
done
# CARD-0660 D-3/D-4, amended: the runner's Codex home is a DIRECTORY on the server2 host
# (RUNNER_CODEX_HOME_DIR), bind-mounted read-write here and at CODEX_HOME in the runner, never a
# volume directory. deploy-parent creates it for uid 1654 at 0700; a mount Docker had to create
# itself is root-owned and is taken over, any other owner refuses. Only the directory itself is
# chowned -- never recursively and never its contents, which hold the operator's sign-in.
# Only the server2 compose mounts it, and only its state-init sets CODEX_HOME_REQUIRED=1: there a
# missing mount refuses, because a seed into a plain container directory would vanish with the
# container. The base stack (and the nested child built from it) has no Codex home; antiphon and
# session-runner wait on this container succeeding, so there a missing mount is skipped.
codex_home=/codex-home
if [ -L "$codex_home" ] || [ ! -d "$codex_home" ]; then
  if [ "${CODEX_HOME_REQUIRED:-}" = "1" ]; then
    echo "CodexHomeNotMounted path=$codex_home" >&2
    exit 43
  fi
  echo "state-init skipped codex home: not mounted"
else
  owner=$(stat -c %u "$codex_home")
  if [ "$owner" != "0" ] && [ "$owner" != "$uid" ]; then
    echo "ForeignStateOwner path=$codex_home uid=$owner" >&2
    exit 42
  fi
  chown "$uid:$gid" "$codex_home"
  chmod 0700 "$codex_home"
  # The non-secret config is seeded only when absent -- an existing file or link is never
  # replaced -- and credentials and conversation state are never touched. Trust is keyed on the
  # repository root, which covers every linked runner worktree; sign-in stays the operator's.
  codex_config="$codex_home/config.toml"
  if [ ! -e "$codex_config" ] && [ ! -L "$codex_config" ]; then
    codex_seed="$codex_home/.config.toml.state-init"
    rm -f "$codex_seed"
    saved_umask=$(umask)
    umask 077
    cat > "$codex_seed" <<'CODEX_CONFIG'
check_for_update_on_startup = false
cli_auth_credentials_store = "file"
forced_login_method = "chatgpt"

[projects."/work/repos/antiphon"]
trust_level = "trusted"
CODEX_CONFIG
    umask "$saved_umask"
    chown "$uid:$gid" "$codex_seed"
    chmod 0600 "$codex_seed"
    ln "$codex_seed" "$codex_config"
    rm -f "$codex_seed"
    echo "state-init seeded codex config"
  fi
fi
chown -R "$uid:$gid" /state /work /runner-state
echo "state-init owned uid=$uid"
