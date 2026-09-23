#!/usr/bin/env bash
# CARD-0604 D-3: fail-together entrypoint for the persistent server2 session runner.
# tini is pid 1 (init: true). This script runs as root, starts the nested dockerd, then
# runs the session runner as uid/gid 1654 with the nested socket group 1656. Either child
# dying takes the container down; restart: unless-stopped brings both back.
# ASCII only. No secret is ever printed or hashed.
set -euo pipefail

DEPLOY_KEY_SOURCE="${ANTIPHON_DEPLOY_KEY_SOURCE:-/run/secrets/antiphon-deploy-key}"
PHONE_HOME_SECRET_SOURCE="${ANTIPHON_PHONE_HOME_SECRET_SOURCE:-/run/secrets/phone-home}"
RUNTIME_DIR=/run/antiphon
DEPLOY_KEY_TARGET="$RUNTIME_DIR/deploy-key"
PHONE_HOME_SECRET_TARGET="$RUNTIME_DIR/phone-home"
CLAUDE_OAUTH_TOKEN_SOURCE="$RUNTIME_DIR/claude-oauth-token"
APP_UID=1654
APP_GID=1654
NESTED_SOCKET_GID=1656
CUSTODY_ROOT=antiphon-custody
DOCKERD_LOG_DIR="${ANTIPHON_DOCKERD_LOG_DIR:-/state/logs}"
DOCKERD_LOG="$DOCKERD_LOG_DIR/dockerd.log"
DOCKERD_WAIT_SECONDS=30

refuse() {
  echo "C604_ENTRYPOINT_REFUSED $1" >&2
  exit 3
}

# --- step 1: root and privileged ------------------------------------------------------
if [ "$(id -u)" -ne 0 ]; then
  refuse NotRoot
fi

cap_eff="$(awk '/^CapEff:/ { print $2 }' /proc/self/status)"
if [ -z "$cap_eff" ]; then
  refuse NotPrivileged
fi
# CAP_SYS_ADMIN is bit 21. A non-privileged container never holds it, and dockerd cannot
# run without it, so its absence is the privileged check.
if [ "$(( 0x$cap_eff & (1 << 21) ))" -eq 0 ]; then
  refuse NotPrivileged
fi

# --- step 2: credential materialisation (D-8) -----------------------------------------
if [ ! -f "$DEPLOY_KEY_SOURCE" ] || [ ! -s "$DEPLOY_KEY_SOURCE" ]; then
  refuse DeployKeyMissing
fi
if [ ! -f "$PHONE_HOME_SECRET_SOURCE" ] || [ ! -s "$PHONE_HOME_SECRET_SOURCE" ]; then
  refuse PhoneHomeSecretMissing
fi

mkdir -p "$RUNTIME_DIR"
chmod 0700 "$RUNTIME_DIR"
chown "$APP_UID:$APP_GID" "$RUNTIME_DIR"
install -m 0400 -o "$APP_UID" -g "$APP_GID" "$DEPLOY_KEY_SOURCE" "$DEPLOY_KEY_TARGET"
# CARD-0604 D-1. The compose secret file is mounted with the HOST owner's uid (1000) at 0600, so
# uid 1654 cannot open it: the runner registered zero times in 304 attempts, each one an
# UnauthorizedAccessException reading /run/secrets/phone-home, while the container stayed
# "healthy". The secret is staged onto the same tmpfs as the deploy key, owned by the app uid,
# and the runner is pointed at the staged copy. It is never printed and never hashed.
install -m 0400 -o "$APP_UID" -g "$APP_GID" "$PHONE_HOME_SECRET_SOURCE" "$PHONE_HOME_SECRET_TARGET"
export PhoneHome__SecretPath="$PHONE_HOME_SECRET_TARGET"
# Staged is not the same as readable: prove the app uid can actually open it before dockerd is
# started, so an ownership regression refuses here instead of looping forever behind /health.
if ! setpriv --reuid="$APP_UID" --regid="$APP_GID" --clear-groups \
     /bin/sh -c 'head -c 1 "$1" >/dev/null 2>&1' antiphon-probe "$PHONE_HOME_SECRET_TARGET"; then
  refuse PhoneHomeSecretUnreadable
fi

# --- step 3: cgroup preparation (D-3 step 3, D-17 custody root) -----------------------
if [ -f /sys/fs/cgroup/cgroup.controllers ]; then
  # cgroup v2 (Docker Desktop). Block attributed to the upstream docker:dind entrypoint:
  # move this process out of the root cgroup so controllers can be delegated.
  mkdir -p /sys/fs/cgroup/init
  echo 0 > /sys/fs/cgroup/init/cgroup.procs 2>/dev/null || true
  for controller in cpu memory pids io; do
    echo "+$controller" > /sys/fs/cgroup/cgroup.subtree_control 2>/dev/null || true
  done
  mkdir -p "/sys/fs/cgroup/$CUSTODY_ROOT"
  chmod 0755 "/sys/fs/cgroup/$CUSTODY_ROOT"
  echo "+pids" > "/sys/fs/cgroup/$CUSTODY_ROOT/cgroup.subtree_control" 2>/dev/null || true
else
  # cgroup v1 (server2, kernel 4.15).
  mkdir -p "/sys/fs/cgroup/pids/$CUSTODY_ROOT" "/sys/fs/cgroup/freezer/$CUSTODY_ROOT"
  chmod 0755 "/sys/fs/cgroup/pids/$CUSTODY_ROOT" "/sys/fs/cgroup/freezer/$CUSTODY_ROOT"
fi
# Nothing under the custody root is chowned to the app uid: the child must not be able to
# leave the cgroup it is placed in.

# --- step 4: iptables backend (D-4) ---------------------------------------------------
if ! iptables -nL >/dev/null 2>&1; then
  refuse IptablesUnusable
fi

# --- step 5: the nested daemon --------------------------------------------------------
mkdir -p "$DOCKERD_LOG_DIR"
dockerd --config-file /etc/docker/daemon.json >>"$DOCKERD_LOG" 2>&1 &
DOCKERD_PID=$!

waited=0
while [ "$waited" -lt "$DOCKERD_WAIT_SECONDS" ]; do
  if docker info >/dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$DOCKERD_PID" 2>/dev/null; then
    refuse NestedDaemonUnavailable
  fi
  sleep 1
  waited=$(( waited + 1 ))
done
if ! docker info >/dev/null 2>&1; then
  refuse NestedDaemonUnavailable
fi

# --- step 5b: the Claude setup-token (CARD-0628 D-1) ---------------------------------
# Compose mounts the deploy's token file read-only; it is never a compose environment entry, so
# `docker inspect` never shows it. Exported here, after dockerd has started, so only the runner
# (and through it every pty child) carries it. The file is the only source. Missing or empty is
# not a refusal: the runner reports Claude as signed out. Never printed, logged or hashed.
unset CLAUDE_CODE_OAUTH_TOKEN
if [ -f "$CLAUDE_OAUTH_TOKEN_SOURCE" ] && [ -s "$CLAUDE_OAUTH_TOKEN_SOURCE" ]; then
  claude_oauth_token="$(tr -d '[:space:]' < "$CLAUDE_OAUTH_TOKEN_SOURCE")"
  if [ -n "$claude_oauth_token" ]; then
    export CLAUDE_CODE_OAUTH_TOKEN="$claude_oauth_token"
  fi
  unset claude_oauth_token
fi

# --- step 6: the runner, as the app uid ----------------------------------------------
export HOME=/home/app
setpriv --reuid="$APP_UID" --regid="$APP_GID" --groups="$NESTED_SOCKET_GID" "$@" &
RUNNER_PID=$!

# --- step 7: fail together ------------------------------------------------------------
forward_term() {
  kill -TERM "$DOCKERD_PID" 2>/dev/null || true
  kill -TERM "$RUNNER_PID" 2>/dev/null || true
}
trap forward_term TERM INT

set +e
wait -n "$DOCKERD_PID" "$RUNNER_PID"
first_status=$?
set -e

if kill -0 "$DOCKERD_PID" 2>/dev/null; then
  echo "C604_ENTRYPOINT_EXIT runner status=$first_status" >&2
  kill -TERM "$DOCKERD_PID" 2>/dev/null || true
else
  echo "C604_ENTRYPOINT_EXIT dockerd status=$first_status" >&2
  kill -TERM "$RUNNER_PID" 2>/dev/null || true
fi
wait "$DOCKERD_PID" 2>/dev/null || true
wait "$RUNNER_PID" 2>/dev/null || true
exit "$first_status"
