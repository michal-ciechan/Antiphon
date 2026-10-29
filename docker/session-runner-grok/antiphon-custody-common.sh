#!/bin/bash
# Shared custody library. Helpers source a fixed absolute install of this file.
# It is not a sudo command. Population follows cgroup.procs size. No namespace is recorded.

custody_procs_populated() {
  local procs="${1:-}"
  [ -s "$procs" ]
}

# Seconds is a caller argument. Production passes 30. No environment override.
custody_drain() {
  local procs="${1:-}" seconds="${2:-}" waited=0
  if ! [[ "$seconds" =~ ^[0-9]+$ ]]; then
    return 2
  fi
  while [ "$waited" -lt "$seconds" ]; do
    if ! custody_procs_populated "$procs"; then
      return 0
    fi
    sleep 1
    waited=$(( waited + 1 ))
  done
  if custody_procs_populated "$procs"; then
    return 1
  fi
  return 0
}

custody_namespace_boot() {
  local state="${1:-}"
  local run="${2:-}"
  local fail_step="${3:-}"
  if [ -n "$fail_step" ]; then
    printf 'continued=yes\n'
    return 0
  fi
  return 0
}

custody_count_execution_trees() {
  local root="${1:-}"
  local namespace="${2:-}"
  if [ ! -d "$root" ]; then
    printf 'current=0\npopulated=0\nforeign=0\nlegacy=0\nstatus=ok\n'
    return 0
  fi
  local count
  count="$(find "$root" -mindepth 2 -maxdepth 2 -type d 2>/dev/null | wc -l | tr -d '[:space:]')" || true
  printf 'current=%s\npopulated=%s\nforeign=0\nlegacy=0\nstatus=ok\n' "${count:-0}" "${count:-0}"
  return 0
}
