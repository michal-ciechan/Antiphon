#!/bin/bash
# CARD-0604 D-17: root-owned placement shim for the Linux custody backend `linux-cgroup-v1`.
#
# The pty-host runs as uid 1654 and must not be able to place, move or release the tracked
# child itself -- CARD-0598 asks for containment the child cannot leave, the way a Windows job
# object denies breakaway in the kernel. So the observer never launches `<exe> <args>`; it
# launches `sudo -n /usr/local/bin/antiphon-custody-enter <id> -- <exe> <args>`. This script is
# root-owned 0755, the custody root and every cgroup under it are root-owned, and the tree is
# entered with PR_SET_NO_NEW_PRIVS set, which makes `sudo` and every other setuid binary inert
# for the whole descendant tree. Nothing inside the tree can therefore write the root-owned
# `cgroup.procs` that would move it out of its own accounting.
#
# ASCII only. Never prints a secret, an argument vector or an environment value.
set -euo pipefail

# Fixed install. Not a sudo command and not overridable by the caller.
if [ ! -r /usr/local/lib/antiphon-custody-common.sh ]; then
  echo "C604_CUSTODY_REFUSED CustodyLibraryMissing" >&2
  exit 3
fi
# shellcheck disable=SC1091
source /usr/local/lib/antiphon-custody-common.sh

CUSTODY_ROOT=antiphon-custody
APP_UID=1654
APP_GID=1654
V2_ROOT="/sys/fs/cgroup/$CUSTODY_ROOT"
V1_PIDS_ROOT="/sys/fs/cgroup/pids/$CUSTODY_ROOT"
V1_FREEZER_ROOT="/sys/fs/cgroup/freezer/$CUSTODY_ROOT"
PROBE_ID=00000000-0000-0000-0000-00000000c604

refuse() {
  echo "C604_CUSTODY_REFUSED $1" >&2
  exit 3
}

# The execution id is the only caller-supplied path component, so it is validated as a GUID
# before it is ever concatenated, and the concatenated path is then checked again against the
# custody root (G-39). Neither check alone is enough: the first rejects traversal, the second
# rejects a symlinked root.
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

# cgroup v2 (Docker Desktop, kernel 5.14+) exposes /sys/fs/cgroup/cgroup.controllers at the
# unified root; cgroup v1 (server2, kernel 4.15) does not and has one directory per controller.
is_v2() {
  [ -f /sys/fs/cgroup/cgroup.controllers ]
}

# Creates the execution's tree if it is absent and returns once it exists and is empty. The
# custody root is root-owned 0755 so uid 1654 can never do this for itself.
require_empty_tree() {
  local procs="$1" pop=0
  if custody_procs_populated "$procs"; then
    pop=0
  else
    pop=$?
  fi
  if [ "$pop" -eq 0 ]; then
    refuse CustodyTreeNotEmpty
  fi
  if [ "$pop" -ne 1 ]; then
    refuse CustodyTreeUnreadable
  fi
}

prepare_tree_v2() {
  local id="$1"
  local ns dir
  ns="$(custody_read_namespace)" || refuse NamespaceUnavailable
  dir="$V2_ROOT/$ns/$id"
  require_under_root "$dir" "$V2_ROOT"
  mkdir -p "$dir"
  chmod 0755 "$dir"
  echo "+pids" > "$dir/cgroup.subtree_control" 2>/dev/null || true
  mkdir -p "$dir/tree"
  chmod 0755 "$dir/tree"
  if [ ! -f "$dir/tree/cgroup.procs" ]; then
    refuse CustodyTreeUnavailable
  fi
  require_empty_tree "$dir/tree/cgroup.procs"
}

prepare_tree_v1() {
  local id="$1"
  local ns hierarchy dir
  ns="$(custody_read_namespace)" || refuse NamespaceUnavailable
  for hierarchy in "$V1_PIDS_ROOT" "$V1_FREEZER_ROOT"; do
    dir="$hierarchy/$ns/$id"
    require_under_root "$dir" "$hierarchy"
    mkdir -p "$dir/tree"
    chmod 0755 "$dir" "$dir/tree"
    if [ ! -f "$dir/tree/cgroup.procs" ]; then
      refuse CustodyTreeUnavailable
    fi
    require_empty_tree "$dir/tree/cgroup.procs"
  done
}

# Writing our own pid, not the child's: the child inherits cgroup membership at fork, so there
# is no window in which a started process is outside the tree. This is the race-free half of
# D-17 -- a post-hoc "place the child once it exists" would have one.
enter_tree_v2() {
  local id="$1"
  local ns
  ns="$(custody_read_namespace)" || refuse NamespaceUnavailable
  echo $$ > "$V2_ROOT/$ns/$id/tree/cgroup.procs" || refuse CustodyPlacementFailed
}

enter_tree_v1() {
  local id="$1" ns hierarchy
  ns="$(custody_read_namespace)" || refuse NamespaceUnavailable
  for hierarchy in "$V1_PIDS_ROOT" "$V1_FREEZER_ROOT"; do
    echo $$ > "$hierarchy/$ns/$id/tree/cgroup.procs" || refuse CustodyPlacementFailed
  done
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

# --probe is what LinuxCgroupCustodyProbe calls at runner startup (D-17 advertisement). It
# proves the custody root exists for the detected cgroup version and that this helper can
# create and remove an empty tree under it; it never execs anything and never places a process.
if [ "${1:-}" = "--probe" ]; then
  if [ "$#" -ne 1 ]; then
    refuse ProbeTakesNoArguments
  fi
  custody_acquire_probe_lock || refuse ProbeLockUnavailable
  if is_v2; then
    [ -d "$V2_ROOT" ] || refuse CustodyRootMissing
    prepare_tree_v2 "$PROBE_ID"
  else
    [ -d "$V1_PIDS_ROOT" ] && [ -d "$V1_FREEZER_ROOT" ] || refuse CustodyRootMissing
    prepare_tree_v1 "$PROBE_ID"
  fi
  remove_tree "$PROBE_ID"
  echo "C604_CUSTODY_PROBE_OK enter"
  exit 0
fi

if [ "$#" -lt 3 ]; then
  refuse UsageIdSeparatorCommand
fi
EXECUTION_ID="$1"
shift
if [ "$1" != "--" ]; then
  refuse UsageIdSeparatorCommand
fi
shift
require_guid "$EXECUTION_ID"
if [ "$EXECUTION_ID" = "$PROBE_ID" ]; then
  refuse ProbeIdIsNotAnExecution
fi

if is_v2; then
  [ -d "$V2_ROOT" ] || refuse CustodyRootMissing
  prepare_tree_v2 "$EXECUTION_ID"
  enter_tree_v2 "$EXECUTION_ID"
else
  [ -d "$V1_PIDS_ROOT" ] && [ -d "$V1_FREEZER_ROOT" ] || refuse CustodyRootMissing
  prepare_tree_v1 "$EXECUTION_ID"
  enter_tree_v1 "$EXECUTION_ID"
fi

# --clear-groups drops the docker-nested supplementary group (D-18): a tracked Mutation session
# cannot reach the nested daemon's socket, so "do not hand the snapshot to Docker" is an
# enforced property rather than a brief rule. --no-new-privs is what makes the containment
# non-escapable; it is inherited by every descendant across fork and exec.
exec setpriv --reuid="$APP_UID" --regid="$APP_GID" --clear-groups --no-new-privs -- "$@"
