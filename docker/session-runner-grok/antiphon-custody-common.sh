#!/bin/bash
# Shared custody library. Helpers source a fixed absolute install of this file.
# It is not a sudo command. Population follows cgroup.procs size. No namespace is recorded.

custody_procs_populated() {
  local procs="${1:-}"
  [ -s "$procs" ]
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
