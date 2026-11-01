# CARD-1609. Observation only. No lock is acquired here: a pre-clear caller
# already owns FD8 through its POST handshake; Apply reopens its own exact proof.
function New-C1609RetirementReadPacket {
    param([string]$Operation = '', [switch]$Clearable)
    if (-not $Clearable -and $Operation -cnotmatch '^c1008[0-9a-f]{32}$') { throw 'RecycleContextInvalid' }
    $coordinator = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'c1008-retirement.sh'))
    $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'c590-remote.sh'))
    $functions = foreach ($name in @('c1008_volume','c1609_temp_retirement_clearable')) {
        $match = [regex]::Match($source,'(?ms)^'+$name+'\(\) \{\n.*?^\}')
        if (-not $match.Success) { throw 'TempRetirementJournalUnknown' }
        $match.Value
    }
    $packet = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($coordinator+"`n"+($functions -join "`n")))
    $mode = if ($Clearable) { 'clear' } else { 'prior' }
    $body = @'
set -euo pipefail
umask 077
SERVER2_ROOT=/home/mc/antiphon-server2
C1008_PROJECT=antiphon-runner-temp
C1008_OPERATION='__OP__'
eval "$(printf '%s' '__PACKET__' | base64 -d)"
c1609_load_retirement() { :; }
c1609_custody "$SERVER2_ROOT" dir
exe="$(command -v docker)"; [[ "$exe" = /* ]]
docker() { command timeout --kill-after=2s 10s "$exe" "$@"; }
if [ '__MODE__' = clear ]; then
    if c1609_temp_retirement_clearable >/dev/null; then
        printf '%s\n' '{"volumes":[],"journals":[]}'
    else
        jq -cn --arg reason "${c1609_clear_reason:-TempRetirementJournalUnknown}" '{error:$reason}'
    fi
    exit 0
fi
root="$SERVER2_ROOT/retirement/$C1008_OPERATION"
file="$root/receipt.json"
[ ! -e "$root/closure.json" ] && [ ! -L "$root/closure.json" ]
[ ! -e "$root/close-pending.json" ] && [ ! -L "$root/close-pending.json" ]
c1609_read_canonical "$file" >/dev/null
c1609_close_receipt_valid "$file"
c1609_lifecycle_valid "$root/lifecycle.json"
jq -e '.active==false and .complete==true and all(.children[];.joined==true)' "$root/lifecycle.json" >/dev/null
pid="$(jq -r .owner.pid "$root/lifecycle.json")"
ticks="$(jq -r .owner.startTicks "$root/lifecycle.json")"
if [ -e "/proc/$pid" ]; then
    [ "$(awk '{sub(/^.*\) /, ""); print $20}' "/proc/$pid/stat")" != "$ticks" ]
fi
jq -e --arg op "$C1008_OPERATION" '.state=="Ready" and .mode=="scripted" and .consumed==false and .operationId==$op' "$file" >/dev/null
nonce="$(jq -er '.phaseNonce|select(test("^[0-9a-f]{32}$"))' "$file")"
preview="$root/preview-$nonce.json"
c1609_read_canonical "$preview" >/dev/null
cmp -s -- "$preview" "$file"
ready="$(date -u -d "$(jq -r .readyAt "$file")" +%s)"; now="$(date -u +%s)"
[ "$((now-ready))" -ge -60 ] && [ "$((now-ready))" -le 900 ]
[ "$(docker info --format '{{.ID}}')" = "$(jq -r .daemon "$file")" ]
digest="$(sha256sum "$file" | cut -d' ' -f1)"
jq -cn --arg op "$C1008_OPERATION" --arg digest "$digest" '{operationId:$op,digest:$digest,state:"Ready",mode:"scripted"}'
'@
    $body = $body.Replace('__OP__',$Operation).Replace('__MODE__',$mode).Replace('__PACKET__',$packet)
    return "timeout --kill-after=2s 25s bash <<'C1609_READ_PACKET_EOF'`n"+$body+"`nC1609_READ_PACKET_EOF`n"
}
