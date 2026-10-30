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

# Fixed install. Not a sudo command and not overridable by the caller.
if [ ! -r /usr/local/lib/antiphon-custody-common.sh ]; then
  echo "C604_CUSTODY_REFUSED CustodyLibraryMissing" >&2
  exit 3
fi
# shellcheck disable=SC1091
source /usr/local/lib/antiphon-custody-common.sh

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
  custody_drain "$1" "$DRAIN_SECONDS"
}

kill_v2() {
  local id="$1"
  local ns dir
  ns="$(custody_read_namespace)" || refuse NamespaceUnavailable
  dir="$V2_ROOT/$ns/$id"
  require_under_root "$dir" "$V2_ROOT"
  if [ ! -d "$dir/tree" ]; then
    echo "C604_CUSTODY_REFUSED CustodyTreeMissing" >&2
    exit 4
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
  local id="$1"
  local ns pids_dir freezer_dir pid pids read_failed=0 thaw_failed=0
  ns="$(custody_read_namespace)" || refuse NamespaceUnavailable
  pids_dir="$V1_PIDS_ROOT/$ns/$id"
  freezer_dir="$V1_FREEZER_ROOT/$ns/$id"
  require_under_root "$pids_dir" "$V1_PIDS_ROOT"
  require_under_root "$freezer_dir" "$V1_FREEZER_ROOT"
  if [ ! -d "$pids_dir/tree" ]; then
    echo "C604_CUSTODY_REFUSED CustodyTreeMissing" >&2
    exit 4
  fi
  # Freeze must stick before any pid is read or signalled. A missing freezer, or a
  # freeze write that fails, refuses with the tree still running. A later read or
  # thaw failure still thaws a freeze that did stick, and still signals nothing
  # when the pid list itself could not be read.
  if [ ! -f "$freezer_dir/tree/freezer.state" ]; then
    refuse CustodyFreezerMissing
  fi
  if ! echo FROZEN > "$freezer_dir/tree/freezer.state"; then
    refuse CustodyFreezeFailed
  fi
  if ! pids="$(tree_pids "$pids_dir/tree/cgroup.procs")"; then
    read_failed=1
    pids=""
  fi
  if [ "$read_failed" -eq 0 ]; then
    for pid in $pids; do
      kill -KILL "$pid" 2>/dev/null || true
    done
  fi
  if ! echo THAWED > "$freezer_dir/tree/freezer.state"; then
    thaw_failed=1
  fi
  if [ "$read_failed" -ne 0 ]; then
    refuse CustodyTreeUnreadable
  fi
  if [ "$thaw_failed" -ne 0 ]; then
    refuse CustodyThawFailed
  fi
  if ! drain "$pids_dir/tree/cgroup.procs"; then
    echo "C604_CUSTODY_KILL_DRAINING $id" >&2
    exit 4
  fi
  echo "C604_CUSTODY_KILL_OK $id"
}

remove_tree() {
  local id="$1" ns hierarchy
  ns="$(custody_read_namespace)" || return 0
  if is_v2; then
    rmdir "$V2_ROOT/$ns/$id/tree" 2>/dev/null || true
    rmdir "$V2_ROOT/$ns/$id" 2>/dev/null || true
  else
    for hierarchy in "$V1_PIDS_ROOT" "$V1_FREEZER_ROOT"; do
      rmdir "$hierarchy/$ns/$id/tree" 2>/dev/null || true
      rmdir "$hierarchy/$ns/$id" 2>/dev/null || true
    done
  fi
}

# --probe is the other half of the runner's startup advertisement check (D-17): it proves this
# helper can reach and empty a tree under the custody root for the detected cgroup version.
if [ "${1:-}" = "--probe" ]; then
  if [ "$#" -ne 1 ]; then
    refuse ProbeTakesNoArguments
  fi
  ns="$(custody_read_namespace)" || refuse NamespaceUnavailable
  custody_acquire_probe_lock || refuse ProbeLockUnavailable
  if is_v2; then
    [ -d "$V2_ROOT" ] || refuse CustodyRootMissing
    mkdir -p "$V2_ROOT/$ns/$PROBE_ID/tree"
    [ -f "$V2_ROOT/$ns/$PROBE_ID/tree/cgroup.kill" ] || { remove_tree "$PROBE_ID"; refuse CgroupKillUnavailable; }
    kill_v2 "$PROBE_ID"
  else
    [ -d "$V1_PIDS_ROOT" ] && [ -d "$V1_FREEZER_ROOT" ] || refuse CustodyRootMissing
    mkdir -p "$V1_PIDS_ROOT/$ns/$PROBE_ID/tree" "$V1_FREEZER_ROOT/$ns/$PROBE_ID/tree"
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
