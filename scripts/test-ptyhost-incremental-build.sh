#!/bin/sh
# Each invocation is one checkpoint command row and uses that row's build slot.
set -eu

case "${1:-}" in
    warm|verify) phase=$1 ;;
    *) echo "usage: $0 warm|verify" >&2; exit 2 ;;
esac
: "${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:?run this command through its checkpoint row}"
export ANTIPHON_LINUX_COMPILER_POLICY=0

read -r load rest < /proc/loadavg
if ! awk -v load="$load" 'BEGIN { exit !(load < 14) }'; then
    echo "REFUSING load=$load (requires Linux one-minute load below 14)" >&2
    exit 4
fi

project_dir=tests/Antiphon.Tests
evidence_dir=.antiphon/c2013-evidence
mkdir -p "$evidence_dir"
log=$(mktemp "$evidence_dir/$phase.XXXXXX.log")
echo "PtyHost incremental $phase source=$(git rev-parse HEAD) load=$load log=$log"
if dotnet build "$project_dir/Antiphon.Tests.csproj" -nologo -tl:off -v:q \
    -nodeReuse:false "-maxcpucount:$ANTIPHON_BUILD_SLOT_MAXCPUCOUNT" -p:UseAppHost=false \
    "-flp:logfile=$log;verbosity=normal"; then
    :
else
    exit "$?"
fi

# Normal MSBuild logging writes one compiler command line for each CoreCompile
# that ran. Keep duplicates so multiple project instances are counted.
compiles=$(sed -nE '/csc(\.dll(\.exe)?|\.exe)?[" ]/s@.* /out:"?[^" ]*/([^/" ]+\.dll)[" ].*@\1@p' "$log")
count=$(printf '%s\n' "$compiles" | awk 'NF { count++ } END { print count+0 }')
printf 'compiler_invocations=%s\n%s\n' "$count" "$compiles"
sed -n '/^Time Elapsed /p' "$log"

output="$project_dir/bin/Debug/net9.0"
test -s "$output/Antiphon.PtyHost.dll"
test -x "$output/Antiphon.PtyHost"
magic=$(od -An -tx1 -N4 "$output/Antiphon.PtyHost" | tr -d ' \n')
if [ "$magic" != 7f454c46 ]; then
    echo "expected a staged Linux apphost at $output/Antiphon.PtyHost" >&2
    exit 1
fi
if [ "$phase" = verify ] && [ "$count" -ne 0 ]; then
    echo "expected zero compiler invocations on the second unchanged build, found $count" >&2
    exit 1
fi
