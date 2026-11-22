# Server2 gate 1 blocked by a partial temp retirement

2026-11-22. Rollout source `ac8339c754a84ab53ca07708c091d4e79bd1fb5b`.
The standing runner remained in place. The desktop operator supplied the
host census and Close result. SSH from the investigation worktree failed
host-key verification. The requested 2026-11-21 Close advisories review file
was absent from `origin/master`. The analysis uses the shipped source, the
runbook and the checked-in Close fixture.

## Journal and deployment gate

The desktop counted 34 journals under `/home/mc/antiphon-server2/retirement`.
There were 7 Completed, 21 without receipts, 2 PartialRefused, 3 Ready and
1 Refused. The desktop identified one blocker,
`c100871bc894857c24fb9a7031a723123f1eb`. Its state is PartialRefused with two
effects and two intents. Both closure files are absent. Its lifecycle reports
`complete=true`, `readyPublished=true` and all children joined. The temp
project container census returned no IDs, including for exited containers.

This was the first Apply, which reported `RecycleReceiptUnavailable`. A fresh
preview and Apply of `c1008c0662d69f7d34c9b856e1d49223bce5f` exited 0 at
08:36 and left all four exact temp volumes absent. Completion is per operation.
The second Apply leaves the first journal unresolved.

The [desktop deployment check](../../scripts/deploy-server2.ps1#L1214) requires
an available container census with no temp-project IDs and a valid retirement
census response. The [host predicate](../../scripts/c590-remote.sh#L9967)
checks typed absence of all four named volumes, retirement-root and journal
custody, operation directory names, valid complete lifecycles, joined children
and an exited owner (PID/start ticks). It exempts eligible receipt-less
previews. Other journals need canonical schema-2 receipts. A valid
per-operation closure clears its unresolved receipt. Any pending closure
blocks, including when an appendix exists. Completed receipts and Ready,
Refused or Pending receipts without effects or intents pass. Applying,
Partial and PartialRefused receipts require a valid closure to pass in those
states. An unknown observation stops the gate. The host repeats the check
under the rollout lock. Given the desktop census, a valid Close for the
identified operation clears the current retirement blocker if the observed
conditions hold. The remaining deploy checks still apply.

## What the Close refusal proves

Close expects SHA-256 over the exact on-disk bytes of the mutable
`retirement/<operation>/receipt.json`, including its final newline as written.
The [PowerShell entry point](../../scripts/retire-runner-volumes.ps1#L169) passes
`-ExpectedJournalDigest` through an internal argument named
`ExpectedProofDigest`. The [host](../../scripts/c1008-retirement.sh#L1279)
validates canonical form, then compares `sha256sum "$file"` with the supplied
value. It checks the same hash again before persisting the appendix. The
hash covers only the stored receipt. The desktop's raw hash was
`7d1bc4eb18a50a6a331cd724c7e028e3dd347c5fd2bb95cdeca162b87edc3968`.
The reserialized hash `e9df6cf49d64fa46cfa729d62b2fe9f3edc046c7cca64ea930b6d7f2f3880440`
was different. Reaching the census establishes that the digest comparison,
receipt, preview binding, daemon, lifecycle, owner and sibling checks passed.
`-Close -DryRun` returns `ModeExclusive`. The separate
[read packet](../../scripts/c1609-read-packet.ps1#L35) hashes a Ready receipt to
bind scripted Apply. Close has no preview mode.

`New-C1609CloseScript` sends this checkout's coordinator and selected helpers
to `bash -s`. The [packet](../../scripts/retire-runner-volumes.ps1#L108) takes
the rollout/cache locks and caps each Docker call at ten seconds. The wrapper
surfaces `CensusUnknown` from the remote exit. There are three sites in
[`c1609_close_host`](../../scripts/c1008-retirement.sh#L1326).

| Site | Failure that emits `CensusUnknown` |
| --- | --- |
| `c1008-retirement.sh:1327` | The initial exact-name volume probe returns nonzero for any target. This includes a failed list, an unrecognized or inconsistent inspect error, or a present result with unexpected identity or shape. A valid present result reports `TempVolumesRetained <name>`. |
| `c1008-retirement.sh:1330` | The first entire-host container/reference census fails. This includes a failed list or inspect, invalid or duplicate IDs, invalid inspect data, any temp-project container or helper labelled for this operation, a named reference to a target, or an unprovable or overlapping bind source. Exited and other projects' containers are included. |
| `c1008-retirement.sh:1334` | The final exact-name volume probe yields anything other than literal `null`, including an unreadable result or a volume that appeared since the first check. |

A failure of the second container census returns 2 without a recognized
reason, which the wrapper reports as `JournalUnknown`. Two successful
container censuses with different digests report `CensusDrift`.

[`c1008_volume`](../../scripts/c590-remote.sh#L6113) treats an unlisted name plus
inspect exit 1 and a `no such volume` diagnostic as successful `null`. Close
requires four such absences twice. The
[container check](../../scripts/c1008-retirement.sh#L1174) uses old mount
paths only to exclude bind references. It uses `readlink -m` for the former
volume path, which may be missing, and `readlink -e` for every current bind
source, including those on unrelated containers. The
[closure fixture](../../scripts/fixtures/c1609-close-cases.mjs) covers a
PartialRefused receipt with original fingerprints and all target names
absent, including a sibling journal cleared independently.

The refusal establishes a failed census precondition. The source does not
establish which branch fired on this host. The confirmed defect is diagnostic.
The same token covers materially different failures with no phase, target,
container ID or predicate. For example, an otherwise ordinary foreign
container with `Config.Labels=null` fails the shape check, and a foreign
exited container with a missing bind source fails canonicalization. Neither
observation was supplied for this host.

## Why Apply #1 reached PartialRefused

The [Ready preflight](../../scripts/c1609-read-packet.ps1#L47) admits a matching
scripted Ready receipt at most 900 seconds old. The
[host admission/consume check](../../scripts/c1008-retirement.sh#L769) binds
the admitted timestamp and digest. Failure at those checks yields
`TempRetirementProofUnknown` at the desktop or
`RecycleGitAuditUnknown reason=preview-required` in host Apply. The
[controller-root readlink](../../scripts/c590-remote.sh#L9555) is also before
any intent. The recorded intents and effects establish that the original
preview was consumed and Apply reached the effect loop. Expiry, a missing
initial preview receipt and the early controller-root readlink would have
stopped it earlier. An `alreadyAbsent` observation also records an effect.

[The bridge](../../scripts/c590-real.ps1#L828) stages and hashes a private
tools bundle per retirement invocation. The shared
`/home/mc/antiphon-c590/c590-remote.sh` modification time does not establish
what either retirement ran. Comparing the invocations' sealed tool manifests
would establish the actual tool versions.

After admission, [effect handling](../../scripts/c590-remote.sh#L9719) can
report `RecycleReceiptUnavailable` if persisting an intent, effect, recycle
record or completion fails. [The refusal handler](../../scripts/c590-remote.sh#L6097)
can also substitute it when persisting PartialRefused or the recycle ledger
fails. [Result finalization](../../scripts/c590-remote.sh#L96) can substitute it
on lifecycle persistence failure. The exact failing write and underlying
system error remain unknown. The best remaining historical evidence is the
original Apply's desktop `remote-output.txt` and exchange result, plus the
`phase`, `outcome` and per-volume outcomes in
`/home/mc/antiphon-server2/recycle/c100871bc894857c24fb9a7031a723123f1eb.json`.
The legacy recycle ledger and Close receipt are separate files.

## Supported recovery

Run the read-only desktop probe below. If both observation passes succeed
and agree, retry the audited Close once with a fresh raw receipt hash and
operator authority. If the probe identifies a persistent host issue, restore
the ordinary census precondition through the owning service's runbook. If
Close refuses valid input, commission a narrow reviewed Close repair.

The [rollout authority](../orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout)
covers the documented rollout and retire operations. The [Close runbook](../docker-stack.md#staged-server2-rolling-rollout-card-0934)
requires the operator credential plus, for a commissioned delegate, an
operation-bound authority file. The caller already used operator authority.
There is no separate operator override. Preserve gate 1 and the standing
runner while the census remains unresolved. A fresh retirement does not
resolve this operation. Resume is a deletion-capable continuation, and
[`c1609_prepare_resume`](../../scripts/c1008-retirement.sh#L903) refuses the
absence of a previously present volume without this operation's durable
intent. The manual volume-removal fallback still requires Close for a
PartialRefused receipt. The volumes are already absent.

After a passing probe, use the supported desktop rollout checkout. Run
`deploy-temp` only after Close reports `TempRetirementClosed` for the operation
and the read-only gate check succeeds. The reason text below applies to the
observed journal with no existing appendix. Finishing an existing pending
appendix requires the original actor, authority and reason.

```powershell
$op = 'c100871bc894857c24fb9a7031a723123f1eb'
$hashLine = ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 "sha256sum -- /home/mc/antiphon-server2/retirement/$op/receipt.json"
if ($LASTEXITCODE -ne 0 -or $hashLine -cnotmatch '^([0-9a-f]{64})  /home/mc/antiphon-server2/retirement/') {
    throw 'Receipt hash unavailable'
}
$digest = $Matches[1]
$reasonFile = Join-Path ([IO.Path]::GetTempPath()) "$op-close-reason.txt"
Set-Content -LiteralPath $reasonFile -Encoding utf8 -Value 'Reconcile the first 2026-11-22 PartialRefused temp retirement after operation c1008c0662d69f7d34c9b856e1d49223bce5f completed. Observe all four temp private volumes absent and retain the original intent and effect record.'
$close = pwsh -NoProfile -File scripts/retire-runner-volumes.ps1 -Close -OperationId $op -ExpectedJournalDigest $digest -ReasonFile $reasonFile
if ($LASTEXITCODE -ne 0 -or $close -cnotmatch "^TempRetirementClosed operation=$op idempotent=(true|false)$") {
    throw 'Audited Close did not confirm closure'
}
$close
. ./scripts/c1609-read-packet.ps1
$packet = New-C1609RetirementReadPacket -Clearable
$census = ($packet.Replace("`r", '') + "`n#") |
    ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 'bash -s'
if ($LASTEXITCODE -ne 0 -or $census -cne '{"volumes":[],"journals":[]}') {
    throw "Retirement admission still refused: $census"
}
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha ac8339c754a84ab53ca07708c091d4e79bd1fb5b -Phase deploy-temp
```

### Read-only desktop probe

Run from the checkout used by Close. This runs only the volume and container
observation functions, with failure markers inserted in memory. It shows
the source line and a validated container ID or exact target where available.
It suppresses raw inspect data and stderr. The probe takes no rollout lock,
so its output is a diagnostic snapshot. Local checks exercised typed volume
absence, an unknown volume inspection, and a rejected container-label shape
with mock Docker responses.

```powershell
function Get-RetirementProbeFunction($path, $name, $failureStatus) {
    $source = [IO.File]::ReadAllText((Join-Path (Get-Location) $path)).Replace("`r", '')
    $match = [regex]::Match($source, '(?ms)^' + $name + '\(\) \{\n.*?^\}')
    if (-not $match.Success) { throw "Cannot read $name from $path" }
    $lineNumber = [regex]::Matches($source.Substring(0, $match.Index), "`n").Count + 1
    $lines = foreach ($line in ($match.Value -split "`n")) {
        $failure = "{ probe_failure '$path`:$lineNumber'; return $failureStatus; }"
        $line.Replace("return $failureStatus", $failure)
        $lineNumber++
    }
    $lines -join "`n"
}

$volumes = Get-RetirementProbeFunction scripts/c590-remote.sh c1008_volume 2
$containers = Get-RetirementProbeFunction scripts/c1008-retirement.sh c1609_close_census 1
$probe = @'
set -euo pipefail
exec 3>&1
C1008_OPERATION=c100871bc894857c24fb9a7031a723123f1eb
C1008_PROJECT=antiphon-runner-temp
receipt="/home/mc/antiphon-server2/retirement/$C1008_OPERATION/receipt.json"
[ -f "$receipt" ] && [ ! -L "$receipt" ] || exit 2
sha256sum -- "$receipt"
jq -ce '{state,consumed,readyAt,intents,effects}' "$receipt"
exe="$(command -v docker)"
[[ "$exe" = /* ]]
docker() { command timeout --kill-after=2s 10s "$exe" "$@"; }
probe_failure() {
    local safe_id=none
    if [[ "${id:-}" =~ ^[0-9a-f]{64}$ ]]; then safe_id="$id"; fi
    printf 'failure=%s containerId=%s volumeInspectExit=%s\n' "$1" "$safe_id" "${code:-none}" >&3
}
probe_volumes() {
    local name facts
    for name in antiphon-runner-temp_{dind-data,runner-state,runner-tmp,work}; do
        printf 'volume=%s\n' "$name"
        if facts="$(c1008_volume "$name" 2>/dev/null)"; then
            if [ "$facts" = null ]; then printf 'status=absent\n'; else printf 'status=present\n'; fi
        else
            printf 'status=unknown\n'
        fi
    done
}
printf 'first volume pass\n'
probe_volumes
for pass in 1 2; do
    printf 'container pass=%s\n' "$pass"
    if digest="$(c1609_close_census "$receipt" 2>/dev/null)"; then
        printf 'censusDigest=%s\n' "$digest"
    else
        printf 'status=unknown\n'
    fi
done
printf 'final volume pass\n'
probe_volumes
'@

(($volumes + "`n" + $containers + "`n" + $probe).Replace("`r", '') + "`n#") |
    ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 'bash -s'
```

## CARD-2043 follow-up draft

**Report which Close census check blocks server2 temp retirement**

On 2026-11-22, Apply `c100871bc894857c24fb9a7031a723123f1eb` left a
PartialRefused receipt with two effects and two intents. A later retirement
completed, all four temp volumes were absent and the temp project had no
containers. Gate 1 returned `TempRetirementUnresolved`. The authorized Close
with the verified raw receipt digest reached `CensusUnknown` and did not
close the older journal. The failing host predicate is still to be captured.

Make Close report the observation phase and safe reason for volume lookup,
container list/inspect/shape, target reference, bind canonicalization and
second-scan failures. Include only the exact target or validated container ID
where applicable. Retain a distinct drift diagnostic. Reproduce the host
input once captured and repair any false refusal while preserving custody,
absence, all-container reference and double-observation checks. Exercise a
PartialRefused operation with two effects and two intents, a later Completed
operation, all four volumes absent and a foreign-container census. Verify
that Close preserves the original bytes, records only the audited appendix,
performs no volume or drain effects, and lets the gate pass when the census
is safe. Confirm that real references and unreadable observations still
refuse with actionable reasons.
