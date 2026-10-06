# Offline curl seam: execute the production c1008_http classifier and both census callers.
curl() {
    local path="${@: -1}" budget='' fault body status=200 key kind
    path="/${path#*://*/}"
    printf '%s\n' "$path" >> "$C1008_FIXTURE_ROOT/http-trace"
    while [ "$#" -gt 0 ]; do
        if [ "$1" = --max-time ]; then budget="$2"; fi
        shift
    done
    printf '%s\n' "$budget" >> "$C1008_FIXTURE_ROOT/http-budgets"
    fault="$(jq -r --arg path "$path" '[.faults // {} | to_entries[] | select($path|contains(.key))][0].value // empty' "$C1008_FIXTURE_ROOT/tasks.json")"
    case "$fault" in
      timeout) return 28 ;;
      transport) return 7 ;;
      http) printf 'SENTINEL_HTTP_BODY\n503'; return 0 ;;
      empty) printf '\n200'; return 0 ;;
      malformed) printf '{SENTINEL_BAD_JSON\n200'; return 0 ;;
    esac
    if [[ "$path" == *projectId=* ]]; then
        key="${path#*projectId=}"; key="${key%%&*}"; kind=all
        [[ "$path" == *'&status=Queued,Dispatched,Working,Blocked' ]] && kind=open
        [[ "$path" == *'&landPending=true' ]] && kind=land
        body="$(jq -ec --arg key "$key" --arg kind "$kind" '
            .scopes[$key] | if has($kind) then .[$kind] else
            if (.items|type)=="array" then .items |= map(select(
              if $kind=="open" then (.status as $s | ["Queued","Dispatched","Working","Blocked"]|index($s)!=null)
              elif $kind=="land" then .landRequestedAt!=null or .landStartedAt!=null else true end)) else . end end' "$C1008_FIXTURE_ROOT/tasks.json")" || status=503
    else
        body="$(jq -ec --arg key "${path##*/}" '.details[$key]' "$C1008_FIXTURE_ROOT/tasks.json")" || status=503
    fi
    printf '%s\n%s' "$body" "$status"
}
