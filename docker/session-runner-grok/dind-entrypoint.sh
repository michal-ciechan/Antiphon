#!/usr/bin/env bash
# CARD-0604 D-3: fail-together entrypoint for the persistent server2 session runner.
# tini is pid 1 (init: true). This script runs as root, starts the nested dockerd, then
# runs the session runner as uid/gid 1654 with the nested socket group 1656. Either child
# dying takes the container down; restart: unless-stopped brings both back.
# ASCII only. No secret is ever printed or hashed.
set -euo pipefail

DEPLOY_KEY_SOURCE="${ANTIPHON_DEPLOY_KEY_SOURCE:-/run/secrets/antiphon-deploy-key}"
PHONE_HOME_SECRET_PATH="${PhoneHome__SecretPath:-/run/secrets/phone-home}"
RUNTIME_DIR=/run/antiphon
DEPLOY_KEY_TARGET="$RUNTIME_DIR/deploy-key"
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
if [ ! -f "$PHONE_HOME_SECRET_PATH" ] || [ ! -s "$PHONE_HOME_SECRET_PATH" ]; then
  refuse PhoneHomeSecretMissing
fi

mkdir -p "$RUNTIME_DIR"
chmod 0700 "$RUNTIME_DIR"
chown "$APP_UID:$APP_GID" "$RUNTIME_DIR"
install -m 0400 -o "$APP_UID" -g "$APP_GID" "$DEPLOY_KEY_SOURCE" "$DEPLOY_KEY_TARGET"
# The phone-home secret is read by the runner process directly from its own path; it is
# never copied and never read here.

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
