# Offline filtered API data shared by the host gate tests and the HTTP classifier tests.
c1087_fixture_body() {
    local path="$1" key kind=all
    if [[ "$path" == *projectId=* ]]; then
        key="${path#*projectId=}"; key="${key%%&*}"
        [[ "$path" == *'&status=Queued,Dispatched,Working,Blocked' ]] && kind=open
        [[ "$path" == *'&landPending=true' ]] && kind=land
        jq -ec --arg key "$key" --arg kind "$kind" '
            .scopes[$key] | if has($kind) then .[$kind] else
            if (.items|type)=="array" then .items |= map(select(
              if $kind=="open" then (.status as $s | ["Queued","Dispatched","Working","Blocked"]|index($s)!=null)
              elif $kind=="land" then .landRequestedAt!=null or .landStartedAt!=null else true end)) else . end end' "$C1008_FIXTURE_ROOT/tasks.json"
    else
        jq -ec --arg key "${path##*/}" '.details[$key]' "$C1008_FIXTURE_ROOT/tasks.json"
    fi
}

# V-6 retains the production reader and injects curl so classification, budget and
# receipt assertions exercise c1008_http itself. Other host gates keep their original
# HTTP-level fixture boundary; repeating the transport parser there adds no coverage.
if [ "${C1087_REAL_HTTP:-0}" != 1 ]; then
    c1008_http() {
        printf '%s\n' "$1" >> "$C1008_FIXTURE_ROOT/http-trace"
        C1008_HTTP_BODY="$(c1087_fixture_body "$1")" || {
            C1008_TASK_ERROR="RecycleTaskCensusUnknown cause=Transport path=$1"; return 2;
        }
    }
fi

curl() {
    local path="${@: -1}" budget='' fault body status=200
    path="/${path#*://*/}"
    printf '%s\n' "$path" >> "$C1008_FIXTURE_ROOT/http-trace"
    while [ "$#" -gt 0 ]; do
        if [ "$1" = --max-time ]; then budget="$2"; fi
        shift
    done
    printf '%s\n' "$budget" >> "$C1008_FIXTURE_ROOT/http-budgets"
    fault="$(jq -r --arg path "$path" '[.faults // {} | to_entries[] | select(.key as $key | $path|contains($key))][0].value // empty' "$C1008_FIXTURE_ROOT/tasks.json")"
    case "$fault" in
      timeout) return 28 ;;
      transport) return 7 ;;
      http) printf 'SENTINEL_HTTP_BODY\n503'; return 0 ;;
      empty) printf '\n200'; return 0 ;;
      malformed) printf '{SENTINEL_BAD_JSON\n200'; return 0 ;;
    esac
    body="$(c1087_fixture_body "$path")" || status=503
    printf '%s\n%s' "$body" "$status"
}
