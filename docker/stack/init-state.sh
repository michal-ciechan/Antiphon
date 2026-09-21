#!/bin/sh
# Initialize only the named volumes this stack owns. Refuse a foreign owner.
set -eu
uid=1654
gid=1654
for d in \
  /state /state/logs /state/keyring /state/check-interpreter /state/diagnose \
  /work /work/repos /work/worktrees \
  /runner-state /runner-state/session-runner /runner-state/pty-hosts /runner-state/logs /runner-state/grok
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
chown -R "$uid:$gid" /state /work /runner-state
echo "state-init owned uid=$uid"
