#!/bin/bash
# Shared custody library. Helpers source the fixed install of this file.
# It is not a sudo command. Population is the file's content, never its size.

guid_ok() {
  [[ "${1:-}" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]
}

# 0 populated, 1 readable and empty, 2 missing or unreadable.
# A cgroup.procs seq_file can report size 0 while read() returns pids.
custody_procs_populated() {
  local procs="${1:-}" content
  if [ -L "$procs" ] || [ ! -f "$procs" ] || [ ! -r "$procs" ]; then
    return 2
  fi
  content="$(cat -- "$procs" 2>/dev/null)" || return 2
  if printf '%s\n' "$content" | grep -q '[^[:space:]]'; then
    return 0
  fi
  return 1
}

# Seconds is a caller argument. Production passes 30. No environment override.
# 0 observed empty, 1 still populated, other missing or unreadable.
custody_drain() {
  local procs="${1:-}" seconds="${2:-}" waited=0 status=0
  if ! [[ "$seconds" =~ ^[0-9]+$ ]]; then
    return 2
  fi
  while [ "$waited" -lt "$seconds" ]; do
    status=0
    if custody_procs_populated "$procs"; then
      status=0
    else
      status=$?
    fi
    if [ "$status" -eq 1 ]; then
      return 0
    fi
    if [ "$status" -ne 0 ]; then
      return "$status"
    fi
    sleep 1
    waited=$(( waited + 1 ))
  done
  status=0
  if custody_procs_populated "$procs"; then
    return 1
  else
    status=$?
  fi
  if [ "$status" -eq 1 ]; then
    return 0
  fi
  return "$status"
}

custody_read_namespace() {
  local marker=/run/antiphon-custody/namespace value
  if [ -L "$marker" ] || [ ! -f "$marker" ] || [ ! -r "$marker" ]; then
    return 1
  fi
  value="$(tr -d '[:space:]' < "$marker")" || return 1
  guid_ok "$value" || return 1
  printf '%s\n' "$value"
}

# Holds flock on fd 9. 0 acquired; 1 missing directory or the 2-second bound expired.
custody_acquire_probe_lock() {
  local dir=/run/antiphon-custody
  local lock="$dir/probe.lock"
  if [ -L "$dir" ] || [ ! -d "$dir" ]; then
    return 1
  fi
  if ! exec 9>"$lock"; then
    return 1
  fi
  if ! flock -w 2 9; then
    return 1
  fi
  return 0
}

# Removes only a regular marker. A symlink is not ours to follow or delete.
custody_invalidate_marker() {
  local run="${1:-}" marker
  if [ -z "$run" ]; then
    return 0
  fi
  if [ -L "$run" ] || [ ! -d "$run" ]; then
    return 1
  fi
  marker="$run/namespace"
  if [ -L "$marker" ]; then
    return 1
  fi
  if [ -e "$marker" ]; then
    rm -f -- "$marker" || return 1
  fi
  return 0
}

custody_publish_marker() {
  local run="${1:-}" guid="${2:-}" tmp
  if [ -z "$run" ]; then
    return 0
  fi
  if [ -L "$run" ] || [ ! -d "$run" ]; then
    return 1
  fi
  if [ -L "$run/namespace" ]; then
    return 1
  fi
  tmp="$run/namespace.tmp.$$"
  printf '%s\n' "$guid" > "$tmp" || return 1
  chmod 0444 "$tmp" || { rm -f -- "$tmp"; return 1; }
  mv -f -- "$tmp" "$run/namespace" || { rm -f -- "$tmp"; return 1; }
  return 0
}

# Entrypoint step 3b. state, run and library are the production paths when the
# entrypoint calls this. A portable test may pass a private filesystem. The
# installed entrypoint does not take those paths from the environment.
# A failed boot leaves custody unadvertised and still returns 0. Every command
# on that path is guarded: a read-only marker, a closed stderr, or a failed
# export must not stop the runner. Errexit is restored before the failure path
# when the caller had it, so the guards are what keep the runner going.
custody_namespace_entrypoint_step() {
  local state="${1:-}" run="${2:-}" library="${3:-}"
  local namespace_boot_status=0 namespace_boot_output errexit=0
  case $- in *e*) errexit=1 ;; esac
  set +e
  namespace_boot_output="$(bash -c '
source "$1" || exit 1
mkdir -p -- "$2" "$3" || exit 1
chmod 0700 -- "$2" || exit 1
chmod 0755 -- "$3" || exit 1
custody_namespace_boot "$2" "$3"
' bash "$library" "$state" "$run" 2>&1)"
  namespace_boot_status=$?
  if [ "$errexit" -eq 1 ]; then
    set -e
  fi
  if [ "$namespace_boot_status" -ne 0 ]; then
    echo "C604_ENTRYPOINT_NOTE CustodyNamespaceUnready" >&2 || true
    export SessionRunner__LinuxCustodyQualified=false || true
    if [ -n "$run" ] && [ -f "$run/namespace" ] && [ ! -L "$run/namespace" ]; then
      rm -f -- "$run/namespace" 2>/dev/null || true
    fi
  fi
  unset namespace_boot_output || true
  return 0
}

custody_namespace_boot() {
  local state="${1:-}" run="${2:-}"
  local record value mode owner guid
  if [ -z "$state" ] || [ -L "$state" ] || [ ! -d "$state" ]; then
    return 1
  fi
  if [ -n "$run" ]; then
    custody_invalidate_marker "$run" || return 1
  fi
  record="$state/namespace"
  if [ -L "$record" ]; then
    return 1
  fi
  if [ -e "$record" ]; then
    if [ ! -f "$record" ] || [ ! -r "$record" ]; then
      return 1
    fi
    mode="$(stat -c %a -- "$record" 2>/dev/null)" || return 1
    mode="${mode#0}"
    owner="$(stat -c %u -- "$record" 2>/dev/null)" || return 1
    value="$(tr -d '[:space:]' < "$record")" || return 1
    if [ "$mode" != "444" ] || [ "$owner" != "$(id -u)" ] || ! guid_ok "$value"; then
      return 1
    fi
    guid="$value"
  else
    if [ ! -r /proc/sys/kernel/random/uuid ]; then
      return 1
    fi
    guid="$(tr -d '[:space:]' < /proc/sys/kernel/random/uuid)" || return 1
    guid_ok "$guid" || return 1
    printf '%s\n' "$guid" > "$record" || return 1
    chmod 0444 "$record" || return 1
    mode="$(stat -c %a -- "$record" 2>/dev/null)" || return 1
    mode="${mode#0}"
    owner="$(stat -c %u -- "$record" 2>/dev/null)" || return 1
    if [ "$mode" != "444" ] || [ "$owner" != "$(id -u)" ]; then
      return 1
    fi
  fi
  printf 'qualified=false\n' > "$state/capability" || return 1
  if [ -n "$run" ]; then
    custody_publish_marker "$run" "$guid" || return 1
  fi
  printf 'namespace=%s\n' "$guid"
  return 0
}

custody_count_execution_trees() {
  local root="${1:-}" namespace="${2:-}"
  local current=0 populated=0 foreign=0 legacy=0
  local ns_path ns_name exec_path exec_name kind procs content
  if [ -L "$root" ] || [ ! -d "$root" ] || [ ! -r "$root" ] || [ ! -x "$root" ]; then
    printf 'status=read-error\n'
    return 1
  fi
  for ns_path in "$root"/*; do
    if [ ! -e "$ns_path" ] && [ ! -L "$ns_path" ]; then
      continue
    fi
    if [ -L "$ns_path" ] || [ ! -d "$ns_path" ] || [ ! -r "$ns_path" ] || [ ! -x "$ns_path" ]; then
      printf 'status=read-error\n'
      return 1
    fi
    ns_name="${ns_path##*/}"
    kind=legacy
    if guid_ok "$ns_name"; then
      if [ "$ns_name" = "$namespace" ]; then
        kind=current
      else
        kind=foreign
      fi
    fi
    for exec_path in "$ns_path"/*; do
      if [ ! -e "$exec_path" ] && [ ! -L "$exec_path" ]; then
        continue
      fi
      if [ -L "$exec_path" ] || [ ! -d "$exec_path" ]; then
        printf 'status=read-error\n'
        return 1
      fi
      exec_name="${exec_path##*/}"
      if ! guid_ok "$exec_name"; then
        continue
      fi
      procs="$exec_path/tree/cgroup.procs"
      if [ -L "$procs" ] || [ ! -f "$procs" ] || [ ! -r "$procs" ]; then
        printf 'status=read-error\n'
        return 1
      fi
      content="$(cat -- "$procs" 2>/dev/null)" || {
        printf 'status=read-error\n'
        return 1
      }
      case "$kind" in
        current) current=$(( current + 1 )) ;;
        foreign) foreign=$(( foreign + 1 )) ;;
        legacy) legacy=$(( legacy + 1 )) ;;
      esac
      if [ "$kind" = "current" ] && printf '%s\n' "$content" | grep -q '[^[:space:]]'; then
        populated=$(( populated + 1 ))
      fi
    done
  done
  printf 'current=%s\npopulated=%s\nforeign=%s\nlegacy=%s\nstatus=ok\n' \
    "$current" "$populated" "$foreign" "$legacy"
  return 0
}
