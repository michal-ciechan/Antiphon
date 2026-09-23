#!/bin/bash
# CARD-0604 D-17: root-owned termination helper for the Linux custody backend `linux-cgroup-v1`.
#
# The seal is the only thing that may end a tracked execution, and it must end the WHOLE tree,
# not the pid the observer happens to remember. On cgroup v2 that is one atomic write to
# `cgroup.kill`; on cgroup v1 (server2, kernel 4.15) there is no such file, so the tree is
# frozen, every pid listed in it is SIGKILLed, and the tree is thawed -- freezing first is what
# stops a fork race from outrunning the kill.
#
# Every pid this script signals comes from the validated tree's own `cgroup.procs`. It never
# matches on a name, never signals a process group and never signals -1 (G-39).
#
# ASCII only. Never prints a secret or an argument vector.
set -euo pipefail

CUSTODY_ROOT=antiphon-custody
V2_ROOT="/sys/fs/cgroup/$CUSTODY_ROOT"
V1_PIDS_ROOT="/sys/fs/cgroup/pids/$CUSTODY_ROOT"
V1_FREEZER_ROOT="/sys/fs/cgroup/freezer/$CUSTODY_ROOT"
PROBE_ID=00000000-0000-0000-0000-00000000c604
DRAIN_SECONDS=30

refuse() {
  echo "C604_CUSTODY_REFUSED $1" >&2
  exit 3
}

# Same two-step validation as the enter shim: the execution id must be a GUID before it is
# concatenated, and the concatenated path must still resolve under the custody root. A helper
# that skipped either could be asked to empty an arbitrary cgroup (G-39).
require_guid() {
  if [[ ! "$1" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
    refuse InvalidExecutionId
  fi
}

require_under_root() {
  local candidate="$1" root="$2" resolved
  resolved="$(realpath -m -- "$candidate")"
  case "$resolved" in
    "$root"/*) : ;;
    *) refuse ExecutionPathOutsideCustodyRoot ;;
  esac
}

is_v2() {
  [ -f /sys/fs/cgroup/cgroup.controllers ]
}

# Never a fabricated zero: an unreadable or missing procs file is "unknown", which the caller
# maps to Draining, not to an empty tree.
tree_pids() {
  local procs="$1"
  [ -f "$procs" ] || return 1
  cat "$procs" 2>/dev/null || return 1
}

drain() {
  local procs="$1" waited=0
  while [ "$waited" -lt "$DRAIN_SECONDS" ]; do
    if [ ! -s "$procs" ]; then
      return 0
    fi
    sleep 1
    waited=$(( waited + 1 ))
  done
  [ -s "$procs" ] && return 1
  return 0
}

kill_v2() {
  local id="$1" dir="$V2_ROOT/$id"
  require_under_root "$dir" "$V2_ROOT"
  if [ ! -d "$dir/tree" ]; then
    echo "C604_CUSTODY_KILL_OK $id empty"
    return 0
  fi
  if [ -f "$dir/tree/cgroup.kill" ]; then
    echo 1 > "$dir/tree/cgroup.kill" || refuse CustodyKillFailed
  else
    refuse CgroupKillUnavailable
  fi
  if ! drain "$dir/tree/cgroup.procs"; then
    echo "C604_CUSTODY_KILL_DRAINING $id" >&2
    exit 4
  fi
  echo "C604_CUSTODY_KILL_OK $id"
}

kill_v1() {
  local id="$1" pids_dir="$V1_PIDS_ROOT/$id" freezer_dir="$V1_FREEZER_ROOT/$id" pid
  require_under_root "$pids_dir" "$V1_PIDS_ROOT"
  require_under_root "$freezer_dir" "$V1_FREEZER_ROOT"
  if [ ! -d "$pids_dir/tree" ]; then
    echo "C604_CUSTODY_KILL_OK $id empty"
    return 0
  fi
  if [ -f "$freezer_dir/tree/freezer.state" ]; then
    echo FROZEN > "$freezer_dir/tree/freezer.state" || refuse CustodyFreezeFailed
  fi
  for pid in $(tree_pids "$pids_dir/tree/cgroup.procs" || true); do
    kill -KILL "$pid" 2>/dev/null || true
  done
  if [ -f "$freezer_dir/tree/freezer.state" ]; then
    echo THAWED > "$freezer_dir/tree/freezer.state" || refuse CustodyThawFailed
  fi
  if ! drain "$pids_dir/tree/cgroup.procs"; then
    echo "C604_CUSTODY_KILL_DRAINING $id" >&2
    exit 4
  fi
  echo "C604_CUSTODY_KILL_OK $id"
}

remove_tree() {
  local id="$1" hierarchy
  if is_v2; then
    rmdir "$V2_ROOT/$id/tree" 2>/dev/null || true
    rmdir "$V2_ROOT/$id" 2>/dev/null || true
  else
    for hierarchy in "$V1_PIDS_ROOT" "$V1_FREEZER_ROOT"; do
      rmdir "$hierarchy/$id/tree" 2>/dev/null || true
      rmdir "$hierarchy/$id" 2>/dev/null || true
    done
  fi
}

# --probe is the other half of the runner's startup advertisement check (D-17): it proves this
# helper can reach and empty a tree under the custody root for the detected cgroup version.
if [ "${1:-}" = "--probe" ]; then
  if [ "$#" -ne 1 ]; then
    refuse ProbeTakesNoArguments
  fi
  if is_v2; then
    [ -d "$V2_ROOT" ] || refuse CustodyRootMissing
    mkdir -p "$V2_ROOT/$PROBE_ID/tree"
    [ -f "$V2_ROOT/$PROBE_ID/tree/cgroup.kill" ] || { remove_tree "$PROBE_ID"; refuse CgroupKillUnavailable; }
    kill_v2 "$PROBE_ID"
  else
    [ -d "$V1_PIDS_ROOT" ] && [ -d "$V1_FREEZER_ROOT" ] || refuse CustodyRootMissing
    mkdir -p "$V1_PIDS_ROOT/$PROBE_ID/tree" "$V1_FREEZER_ROOT/$PROBE_ID/tree"
    kill_v1 "$PROBE_ID"
  fi
  remove_tree "$PROBE_ID"
  echo "C604_CUSTODY_PROBE_OK kill"
  exit 0
fi

if [ "$#" -ne 1 ]; then
  refuse UsageExactlyOneExecutionId
fi
EXECUTION_ID="$1"
require_guid "$EXECUTION_ID"
if [ "$EXECUTION_ID" = "$PROBE_ID" ]; then
  refuse ProbeIdIsNotAnExecution
fi

if is_v2; then
  [ -d "$V2_ROOT" ] || refuse CustodyRootMissing
  kill_v2 "$EXECUTION_ID"
else
  [ -d "$V1_PIDS_ROOT" ] && [ -d "$V1_FREEZER_ROOT" ] || refuse CustodyRootMissing
  kill_v1 "$EXECUTION_ID"
fi
remove_tree "$EXECUTION_ID"
