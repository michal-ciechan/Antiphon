# CARD-1609. Authenticated public manual entry to the shared retirement gate.
# Dot-source only. Authority is commissioned on the host; credentials stay local.

function Fail-C1609Manual([string]$Reason) {
    [Console]::Error.WriteLine("TempRetirementManualRefused: $Reason")
    exit 2
}

function Copy-C1609ManualEvidence([string]$Name, [string]$Destination) {
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'scp'; $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    foreach ($arg in @('-o','BatchMode=yes','-o','ConnectTimeout=15',
        "mc@server2:/home/mc/antiphon-server2/retirement/$OperationId/$Name",$Destination)) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $proc = [Diagnostics.Process]::new(); $proc.StartInfo = $psi
    try {
        [void]$proc.Start()
        $stdout = $proc.StandardOutput.ReadToEndAsync(); $stderr = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit(30000)) { throw 'deadline' }
        if (-not [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout,$stderr)).Wait(2000) -or
            $proc.ExitCode -ne 0) { throw 'copy' }
    } catch { Fail-C1609Manual 'ReceiptUnavailable' }
    finally {
        if ($proc.Id -and -not $proc.HasExited) { $proc.Kill($true); $proc.WaitForExit() }
        $proc.Dispose()
    }
}

function New-C1609ManualPacket {
    param([string]$Mode, [string]$Actor, [string]$Authority, [string]$Attestation = '')
    $coordinator = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'c1008-retirement.sh'))
    $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'c590-remote.sh'))
    $functions = foreach ($name in @('c1008_volume', 'c1008_rollout_lock', 'c1008_lock', 'c849_lock')) {
        $match = [regex]::Match($source, '(?ms)^' + $name + '\(\) \{\n.*?^\}')
        if (-not $match.Success) { Fail-C1609Manual 'CurrentTooling' }
        $match.Value
    }
    $packet = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($coordinator + "`n" + ($functions -join "`n")))
    $request = [ordered]@{
        operationId=$OperationId; phase=$Mode; actor=$Actor; authorityRef=$Authority
        template=[bool]$Template; resume=[bool]$Resume; expectedDigest=$ExpectedProofDigest
        attestation=$Attestation
    } | ConvertTo-Json -Compress
    $request64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($request))
    # No secret is in this packet. Names and paths come from this script, never
    # from the attestation. The same locks exclude supported lifecycle writers.
    $body = @'
set -euo pipefail
umask 077
SERVER2_ROOT='/home/mc/antiphon-server2'
C1008_OPERATION='__OP__'
C1008_PROJECT=antiphon-runner-temp
C1008_TARGETS=(antiphon-runner-temp_runner-state antiphon-runner-temp_dind-data antiphon-runner-temp_runner-tmp antiphon-runner-temp_work)
C1008_LOCK_HELD=0; C849_LOCK_HELD=0; LANE=host
eval "$(printf '%s' '__PACKET__' | base64 -d)"
require_lane() { [ "$1" = host ]; }
write_result() { exit "$3"; }
c1609_custody "$SERVER2_ROOT" dir || exit 2
for p in "$SERVER2_ROOT/locks" "$SERVER2_ROOT/locks/rollout.lock" "$SERVER2_ROOT/locks/cache-maintenance.lock"; do
    if [ -e "$p" ] || [ -L "$p" ]; then
        if [ -d "$p" ]; then c1609_custody "$p" dir; else c1609_custody "$p" file; fi || exit 2
    fi
done
C1609_MANUAL_DOCKER_EXECUTABLE="$(command -v docker)"
[[ "$C1609_MANUAL_DOCKER_EXECUTABLE" = /* ]]
docker() { command timeout --kill-after=2s 10s "$C1609_MANUAL_DOCKER_EXECUTABLE" "$@"; }
c1008_lock
request="$(printf '%s' '__REQUEST__' | base64 -d)"
phase="$(printf '%s' "$request" | jq -er '.phase|select(.=="Preview" or .=="Apply")')"
fingerprints="$(c1609_target_snapshot)"
preview_digest=null
expected="$(printf '%s' "$request" | jq -r .expectedDigest)"
if [ "$phase" = Apply ]; then
    file="$SERVER2_ROOT/retirement/$C1008_OPERATION/receipt.json"
    c1609_read_canonical "$file" >/dev/null
    [[ "$expected" =~ ^[0-9a-f]{64}$ ]]
    [ "$(sha256sum "$file" | cut -d' ' -f1)" = "$expected" ]
    if [ "$(printf '%s' "$request" | jq -r .resume)" = true ]; then
        nonce="$(jq -er '.phaseNonce|select(test("^[0-9a-f]{32}$"))' "$file")"
        preview="$SERVER2_ROOT/retirement/$C1008_OPERATION/preview-$nonce.json"
        c1609_read_canonical "$preview" >/dev/null
        fingerprints="$(jq -cS .fingerprints "$preview")"
        preview_digest="$(sha256sum "$preview" | cut -d' ' -f1)"
    else
        jq -e '.schema==2 and .state=="Ready" and .consumed==false and .mode=="manual"' "$file" >/dev/null
        preview_digest="$expected"
    fi
fi
targets="$(printf '%s\n' "${C1008_TARGETS[@]}" | jq -R . | jq -sc .)"
now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
if [ "$(printf '%s' "$request" | jq -r .template)" = true ]; then
    jq -cnS --arg op "$C1008_OPERATION" --arg phase "$phase" --arg now "$now" \
      --arg digest "$preview_digest" --argjson fingerprints "$fingerprints" --argjson targets "$targets" \
      '{schema:1,mode:"manual",operationId:$op,phase:$phase,actor:"",authorityRef:"",reason:"",utc:$now,
        targets:$targets,fingerprints:$fingerprints,previewDigest:(if $digest=="null" then null else $digest end)}'
    exit 0
fi
authority_root="$SERVER2_ROOT/manual-authority"
[ ! -L "$authority_root" ]
mkdir -p -- "$authority_root"
c1609_custody "$authority_root" dir
attestation="$(printf '%s' "$request" | jq -er .attestation | base64 -d)"
attestation="$(printf '%s' "$attestation" | jq -cS .)"
id="$(printf '%s\n' "$attestation" | sha256sum | cut -d' ' -f1)"
[[ "$id" =~ ^[0-9a-f]{64}$ ]]
file="$authority_root/$C1008_OPERATION.$id.json"
if [ -e "$file" ] || [ -L "$file" ]; then
    [ "$(c1609_read_canonical "$file")" = "$attestation" ]
else
    printf '%s\n' "$attestation" | c1609_persist "$file"
fi
grant="$authority_root/$C1008_OPERATION.json"
actor="$(printf '%s' "$request" | jq -r .actor)"
authority="$(printf '%s' "$request" | jq -r .authorityRef)"
if [ -e "$grant" ] || [ -L "$grant" ]; then
    c1609_read_canonical "$grant" >/dev/null
    jq -e --arg actor "$actor" --arg authority "$authority" --arg op "$C1008_OPERATION" \
      --argjson targets "$targets" '.schema==1 and .actor==$actor and .authorityRef==$authority and
        .operationId==$op and (.targets|sort)==($targets|sort)' "$grant" >/dev/null
fi
issued="$(date -u -d "$now" +%s)"
expires="$(date -u -d "@$((issued + 900))" +%Y-%m-%dT%H:%M:%SZ)"
grant_body="$(jq -cnS --arg actor "$actor" --arg authority "$authority" --arg op "$C1008_OPERATION" \
  --arg now "$now" --arg expires "$expires" --argjson targets "$targets" \
  '{schema:1,actor:$actor,authorityRef:$authority,operationId:$op,issuedAt:$now,expiresAt:$expires,targets:$targets}')"
grant_digest="$(printf '%s\n' "$grant_body" | sha256sum | cut -d' ' -f1)"
history="$authority_root/$C1008_OPERATION.grant.$grant_digest.json"
if [ -e "$history" ] || [ -L "$history" ]; then
    [ "$(c1609_read_canonical "$history")" = "$grant_body" ]
else
    printf '%s\n' "$grant_body" | c1609_persist "$history"
fi
printf '%s\n' "$grant_body" | c1609_persist "$grant"
C1008_MODE=manual; C1008_MANUAL_AUTHORITY_FILE="$grant"; C1008_ATTESTATION_FILE="$file"
digest="$preview_digest"; [ "$digest" != null ] || digest=''
c1609_manual_attestation "$phase" "$fingerprints" "$digest" >/dev/null
jq -cnS --arg id "$id" '{attestationId:$id}'
'@
    $body = $body.Replace('__OP__', $OperationId).Replace('__PACKET__', $packet).Replace('__REQUEST__', $request64)
    return "timeout --kill-after=5s 75s bash <<'C1609_MANUAL_PACKET_EOF'`n" + $body + "`nC1609_MANUAL_PACKET_EOF`n"
}

function Invoke-C1609ManualRetirement {
    if (-not $Manual) { Fail-C1609Manual 'ManualRequired' }
    if ($DryRun) { $Preview = $true }
    if (-not (Test-OperationId $OperationId)) { Fail-C1609Manual 'OperationId' }
    if ($RetiredRunnerId -cne 'server2-temp' -or
        $ProjectId -cnotmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') { Fail-C1609Manual 'Identity' }
    $targets = @('antiphon-runner-temp_runner-state','antiphon-runner-temp_dind-data','antiphon-runner-temp_runner-tmp','antiphon-runner-temp_work')
    if ($VolumeName.Count -ne 4 -or @($VolumeName | Sort-Object -Unique).Count -ne 4 -or
        (Compare-Object ($targets | Sort-Object) ($VolumeName | Sort-Object) -CaseSensitive)) { Fail-C1609Manual 'Targets' }
    if ($Resume -and -not $Apply) { Fail-C1609Manual 'ResumeMode' }
    if ($Apply -and $ExpectedProofDigest -cnotmatch '^[0-9a-f]{64}$') { Fail-C1609Manual 'Digest' }
    $auth = Get-CloseAuthority
    . (Join-Path $PSScriptRoot 'c590-real.ps1')
    $phase = if ($Preview) { 'Preview' } else { 'Apply' }
    $encoded = ''
    if (-not $Template) {
        if (-not (Test-Path -LiteralPath $AttestationFile -PathType Leaf) -or (Test-Reparse $AttestationFile) -or
            (Get-Item -LiteralPath $AttestationFile).Length -gt 1048576) { Fail-C1609Manual 'Attestation' }
        try {
            $text = [IO.File]::ReadAllText($AttestationFile)
            Assert-C994RawManifest $text
            $value = $text | ConvertFrom-Json
            if ($value.actor -cne $auth.Actor -or $value.authorityRef -cne $auth.Authority -or
                $value.operationId -cne $OperationId -or $value.phase -cne $phase -or $value.mode -cne 'manual' -or
                [string]::IsNullOrWhiteSpace($value.reason)) { throw 'attestation' }
            $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($text))
        } catch { Fail-C1609Manual 'Attestation' }
    }
    $base = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else {
        Join-Path (Split-Path -Parent $PSScriptRoot) '.antiphon/rolling-server2'
    }
    [void][IO.Directory]::CreateDirectory($base)
    $root = Join-Path $base ('manual-retire-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($root)
    try {
        $response = Invoke-Server2Bash -Script (New-C1609ManualPacket -Mode $phase -Actor $auth.Actor -Authority $auth.Authority -Attestation $encoded) -TimeoutMs 90000
        Assert-C994RawManifest $response
        $prepared = $response | ConvertFrom-Json
    } catch { Fail-C1609Manual 'HostPreparation' }
    if ($Template) {
        [IO.File]::WriteAllText((Join-Path $root 'attestation-template.json'), $response, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $root 'proof.md'), "Unsigned manual attestation template. No Ready proof or deletion authority was issued.`n")
        Write-Output ('C1609_MANUAL_TEMPLATE ' + $root)
        exit 0
    }
    if ($prepared.attestationId -cnotmatch '^[0-9a-f]{64}$') { Fail-C1609Manual 'HostPreparation' }
    $api = $env:ANTIPHON_API
    if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
    . (Join-Path $PSScriptRoot 'lib/runner-operator-token.ps1')
    try {
        $status = Invoke-RestMethod -Method Get -Uri ($api.TrimEnd('/') + '/api/session-runners/server2-temp/status') `
            -Headers @{ 'X-Antiphon-Operator-Token' = (Get-RunnerOperatorToken) } -TimeoutSec 30
        $retired = ConvertTo-C994RetiredInstant $status.retiredAt
    } catch { Fail-C1609Manual 'RunnerStatus' }
    $context = [ordered]@{ version=1; project='antiphon-runner-temp'; operationId=$OperationId
        dryRun=[bool]$Preview; resume=[bool]$Resume; projectId=$ProjectId
        mode='manual'; attestationId=[string]$prepared.attestationId }
    if ($Apply) { $context.expectedProofDigest = $ExpectedProofDigest }
    $manifest = [ordered]@{ evidenceRoot=$root; runId=('c1609manual' + [guid]::NewGuid().ToString('N'))
        sourceSha=((& git -C (Split-Path -Parent $PSScriptRoot) rev-parse HEAD).Trim())
        tempRetiredAt=$retired; recycle=$context }
    $manifestPath = Join-Path $root 'request.json'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 20 -Compress), [Text.UTF8Encoding]::new($false))
    & pwsh -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'verify-docker-stack.ps1') -Case retire-temp-runner -Manifest $manifestPath
    if ($LASTEXITCODE -ne 0) { Fail-C1609Manual 'ContentOrLiveGuard' }
    Copy-C1609ManualEvidence 'receipt.json' (Join-Path $root 'manifest.json')
    Copy-C1609ManualEvidence 'proof.md' (Join-Path $root 'proof.md')
    try {
        $text = [IO.File]::ReadAllText((Join-Path $root 'manifest.json'))
        Assert-C994RawManifest $text
        $record = $text | ConvertFrom-Json
        $state = if ($Preview) { 'Ready' } else { 'Completed' }
        if ($record.schema -ne 2 -or $record.operationId -cne $OperationId -or $record.mode -cne 'manual' -or
            $record.project -cne 'antiphon-runner-temp' -or $record.state -cne $state -or
            $record.consumed -isnot [bool] -or $record.consumed -ne [bool]$Apply -or
            @($record.targets).Count -ne 4 -or
            (Compare-Object ($targets | Sort-Object) ($record.targets | Sort-Object) -CaseSensitive)) { throw 'receipt' }
    } catch { Fail-C1609Manual 'ReceiptUnavailable' }
    $digest = (Get-FileHash -LiteralPath (Join-Path $root 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output ("C1609_MANUAL state=" + $(if ($Preview) {'Ready'} else {'Completed'}) + " operation=$OperationId digest=$digest evidence=$root")
    exit 0
}
