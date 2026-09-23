#!/bin/bash
# CARD-0604 V-28. The four containment measurements the Linux custody backend must pass BEFORE it
# is trusted, run inside the persistent runner as uid 1654 against the real helpers, on whichever
# cgroup version this kernel presents.
#
#   (a) a child launched through antiphon-custody-enter that double-forks, setsids, nohups and
#       daemonises leaves EVERY descendant listed in tree/cgroup.procs;
#   (b) that child cannot `sudo -n true` (no_new_privs makes sudo inert) and cannot write any
#       cgroup.procs (the files are root-owned);
#   (c) antiphon-custody-kill empties the tree within 5 s, and the observer is never in the tree;
#   (d) a reparented orphan -- its parent gone, adopted by init -- is still listed.
#
# Prints one `containment=ok` / `containment=no` line plus a labelled reason for each check.
# ASCII only. Never prints a secret.
set -uo pipefail

EXECUTION_ID="${1:-}"
if [ -z "$EXECUTION_ID" ]; then
  EXECUTION_ID="$(cat /proc/sys/kernel/random/uuid)"
fi
ENTER=/usr/local/bin/antiphon-custody-enter
KILL=/usr/local/bin/antiphon-custody-kill
WORK="$(mktemp -d)"
FAILURES=""

if [ -f /sys/fs/cgroup/cgroup.controllers ]; then
  CGROUP_VERSION=v2
  PROCS="/sys/fs/cgroup/antiphon-custody/$EXECUTION_ID/tree/cgroup.procs"
else
  CGROUP_VERSION=v1
  PROCS="/sys/fs/cgroup/pids/antiphon-custody/$EXECUTION_ID/tree/cgroup.procs"
fi
echo "cgroup_version=$CGROUP_VERSION"
echo "execution_id=$EXECUTION_ID"

fail() {
  echo "check_$1=no reason=$2"
  FAILURES="$FAILURES $1"
}

pass() {
  echo "check_$1=yes"
}

count_tree() {
  [ -f "$PROCS" ] || { echo 0; return; }
  grep -c '[0-9]' "$PROCS" 2>/dev/null || echo 0
}

# The payload the shim will exec. It fans out in every way a process can try to leave a process
# tree -- double fork, new session, nohup, a daemonised grandchild -- and records what it saw.
cat > "$WORK/payload.sh" <<'PAYLOAD'
#!/bin/bash
out="$1"
: > "$out.pids"
echo $$ >> "$out.pids"

# (b) the tracked tree must not be able to regain privilege or leave its own accounting.
if sudo -n true 2>/dev/null; then
  echo "sudo=allowed" >> "$out.escape"
else
  echo "sudo=denied" >> "$out.escape"
fi
if [ -f /sys/fs/cgroup/cgroup.controllers ]; then
  root_procs=/sys/fs/cgroup/cgroup.procs
else
  root_procs=/sys/fs/cgroup/pids/cgroup.procs
fi
if echo $$ > "$root_procs" 2>/dev/null; then
  echo "cgroup_write=allowed" >> "$out.escape"
else
  echo "cgroup_write=denied" >> "$out.escape"
fi

# (a) three different ways of detaching, all of which must stay in the tree.
( sleep 120 ) &
echo $! >> "$out.pids"
setsid bash -c 'sleep 120' &
echo $! >> "$out.pids"
nohup bash -c 'sleep 120' >/dev/null 2>&1 &
echo $! >> "$out.pids"
# A double fork: the middle process exits immediately, so the grandchild is reparented to init
# (d). Its pid is written by the grandchild itself, because the middle process cannot report it.
bash -c 'bash -c "echo \$\$ >> \"$0.pids\"; sleep 120" & exit 0' "$out" &
wait_for_pids=$!
sleep 2

sleep 30
PAYLOAD
chmod 0755 "$WORK/payload.sh"

echo "--- launching the tracked child ---"
sudo -n "$ENTER" "$EXECUTION_ID" -- "$WORK/payload.sh" "$WORK/probe" >/dev/null 2>&1 &
OBSERVER_CHILD=$!
sleep 6

# (a) every descendant is accounted for.
TREE_COUNT="$(count_tree)"
RECORDED="$(grep -c '[0-9]' "$WORK/probe.pids" 2>/dev/null || echo 0)"
echo "tree_count=$TREE_COUNT recorded_pids=$RECORDED"
if [ "$TREE_COUNT" -ge 5 ] && [ "$TREE_COUNT" -ge "$RECORDED" ]; then
  pass a_descendants
else
  fail a_descendants "tree=$TREE_COUNT recorded=$RECORDED"
fi

# Each recorded pid must actually be listed in the tree, not merely counted.
MISSING=0
while read -r pid; do
  [ -n "$pid" ] || continue
  grep -qx "$pid" "$PROCS" 2>/dev/null || MISSING=$(( MISSING + 1 ))
done < "$WORK/probe.pids"
if [ "$MISSING" -eq 0 ]; then
  pass a_every_pid_listed
else
  fail a_every_pid_listed "missing=$MISSING"
fi

# (d) the reparented orphan. Its parent is gone; it must still be in the tree.
ORPHANS=0
while read -r pid; do
  [ -n "$pid" ] || continue
  ppid="$(awk '/^PPid:/ { print $2 }' "/proc/$pid/status" 2>/dev/null)"
  if [ "${ppid:-0}" = "1" ] && grep -qx "$pid" "$PROCS" 2>/dev/null; then
    ORPHANS=$(( ORPHANS + 1 ))
  fi
done < "$WORK/probe.pids"
if [ "$ORPHANS" -ge 1 ]; then
  pass d_reparented_orphan
else
  fail d_reparented_orphan "no reparented pid found in the tree"
fi

# (b) no privilege regained, no accounting left.
if grep -q 'sudo=denied' "$WORK/probe.escape" 2>/dev/null; then
  pass b_sudo_denied
else
  fail b_sudo_denied "$(tr '\n' ' ' < "$WORK/probe.escape" 2>/dev/null)"
fi
if grep -q 'cgroup_write=denied' "$WORK/probe.escape" 2>/dev/null; then
  pass b_cgroup_write_denied
else
  fail b_cgroup_write_denied "$(tr '\n' ' ' < "$WORK/probe.escape" 2>/dev/null)"
fi

# (c) the observer must never have been in the tree, and the kill must empty it within 5 s.
if grep -qx "$$" "$PROCS" 2>/dev/null; then
  fail c_observer_outside "the probe itself is in the tree"
else
  pass c_observer_outside
fi

START="$(date +%s)"
sudo -n "$KILL" "$EXECUTION_ID" >/dev/null 2>&1
KILL_EXIT=$?
ELAPSED=$(( $(date +%s) - START ))
AFTER="$(count_tree)"
echo "kill_exit=$KILL_EXIT elapsed=${ELAPSED}s after=$AFTER"
if [ "$KILL_EXIT" -eq 0 ] && [ "$AFTER" -eq 0 ] && [ "$ELAPSED" -le 5 ]; then
  pass c_kill_empties_tree
else
  fail c_kill_empties_tree "exit=$KILL_EXIT after=$AFTER elapsed=$ELAPSED"
fi

wait "$OBSERVER_CHILD" 2>/dev/null
rm -rf "$WORK"

if [ -z "$FAILURES" ]; then
  echo "containment=ok"
  exit 0
fi
echo "containment=no failed=$FAILURES"
exit 1
