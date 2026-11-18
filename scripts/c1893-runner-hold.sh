#!/usr/bin/env bash
# CARD-1893 activation plan section 7.0: the SERVER2 RUNNER HOLD check for one rollout commit.
# Usage: bash scripts/c1893-runner-hold.sh <40-hex rollout sha>
# Run `git fetch origin` in this checkout first; this script never fetches. It reads only the
# commit's own compose files (git show), never the working tree.
#
# stdout is exactly one word and the exit is 0:
#   NOT-AFFECTED  the commit does not contain the collector 9cf3e4917.
#   RELEASED      S1: the base runner sets HistoryPath /state/host-load and no HistoryEnabled line,
#                 and the temp override sets HistoryEnabled "false"; or S0 (Q-8 stop-gap): the
#                 base sets HistoryEnabled "false" and no HistoryPath, and the temp override sets
#                 no history key (CARD-1985 A-2).
#   HOLD          anything else, including S1 lines beside a base that still disables history
#                 (CARD-1985 A-1). Do not run the held phase.
# Any argument or object that does not resolve, or any other git failure, exits 2 with empty
# stdout: an unfetched commit is never NOT-AFFECTED.
set -u

collector=9cf3e491705247bc03814e6afed77a6534805dfd

if [ "$#" -ne 1 ] || [[ ! $1 =~ ^[0-9a-f]{40}$ ]]; then
  echo "usage: c1893-runner-hold.sh <40-hex rollout sha>" >&2
  exit 2
fi
sha=$1

git cat-file -e "$sha^{commit}" || exit 2
git cat-file -e "$collector^{commit}" || exit 2
git merge-base --is-ancestor "$collector" "$sha"
case $? in
  0) ;;
  1) echo NOT-AFFECTED; exit 0 ;;
  *) exit 2 ;;
esac

base=$(git show "$sha:docker-compose.server2-runner.yml") || exit 2
temp=$(git show "$sha:docker-compose.server2-runner.temp.yml") || exit 2

has() { printf '%s\n' "$1" | grep -Eq "$2"; }
path_line='^ +SessionRunner__HostStats__HistoryPath: /state/host-load$'
disable_line='^ +SessionRunner__HostStats__HistoryEnabled: "false"$'

if has "$base" "$path_line" && has "$temp" "$disable_line" \
    && ! has "$base" '^ +SessionRunner__HostStats__HistoryEnabled:'; then
  echo RELEASED
elif has "$base" "$disable_line" && ! has "$base" '^ +SessionRunner__HostStats__HistoryPath:' \
    && ! has "$temp" '^ +SessionRunner__HostStats__History'; then
  echo RELEASED
else
  echo HOLD
fi
