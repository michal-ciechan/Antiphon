# CARD-1609. Audited close for one temp-retirement journal.
# Close records that the four temp-private volumes were observed absent.
# It never deletes a volume, never runs compose down -v, and never clears retiredAt.
# Preview and DryRun only print the census. Apply checks a Ready receipt, then
# calls deploy-server2.ps1 -Phase retire-temp. There is no -Force success path.
#requires -Version 7
[CmdletBinding()]
param(
    [switch]$Close,
    [switch]$DryRun,
    [switch]$Apply,
    [switch]$Preview,
    [switch]$Force,
    [string]$OperationId = '',
    [string]$ExpectedJournalDigest = '',
    [string]$ReasonFile = '',
    [string]$AuthorityFile = '',
    [string]$Sha = ''
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    [Console]::Error.WriteLine('TempRetirementCloseRefused: PowerShell7Required')
    exit 2
}

function Fail-Close([string]$Reason) {
    [Console]::Error.WriteLine("TempRetirementCloseRefused: $Reason")
    exit 2
}

if ($Force) { Fail-Close 'ForceRefused' }
$modeCount = 0
foreach ($flag in @($Close, $DryRun, $Apply, $Preview)) { if ($flag) { $modeCount++ } }
if ($modeCount -ne 1) { Fail-Close 'ModeExclusive' }

$script:TempVolumeNames = @(
    'antiphon-runner-temp_work',
    'antiphon-runner-temp_runner-tmp',
    'antiphon-runner-temp_dind-data',
    'antiphon-runner-temp_runner-state'
)

function Test-OperationId([string]$Value) {
    return $Value -cmatch '^c1008[0-9a-f]{32}$'
}

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Test-Reparse([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $info = [System.IO.FileInfo]::new($Path)
    return (($info.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Get-CloseAuthority {
    $operator = $env:ANTIPHON_OPERATOR_TOKEN_FILE
    if (-not [string]::IsNullOrWhiteSpace($operator) -and (Test-Path -LiteralPath $operator -PathType Leaf)) {
        $token = [System.IO.File]::ReadAllText($operator).Trim()
        if (-not [string]::IsNullOrEmpty($token)) {
            return [pscustomobject]@{ Actor = 'operator'; Authority = 'operator-token' }
        }
    }
    $task = $env:ANTIPHON_TASK_TOKEN
    if (-not [string]::IsNullOrWhiteSpace($task) -and -not [string]::IsNullOrWhiteSpace($AuthorityFile) -and
        (Test-Path -LiteralPath $AuthorityFile -PathType Leaf)) {
        $body = [System.IO.File]::ReadAllText($AuthorityFile)
        if (-not [string]::IsNullOrWhiteSpace($body)) {
            return [pscustomobject]@{ Actor = 'delegate'; Authority = 'task-token' }
        }
    }
    Fail-Close 'Authority'
}

function Read-CensusVolumes {
    if (-not [string]::IsNullOrWhiteSpace($env:C1609_VOLUME_CENSUS_FILE)) {
        if (-not (Test-Path -LiteralPath $env:C1609_VOLUME_CENSUS_FILE -PathType Leaf)) { Fail-Close 'CensusMissing' }
        if (Test-Reparse $env:C1609_VOLUME_CENSUS_FILE) { Fail-Close 'JournalUnknown' }
        try { $census = Get-Content -LiteralPath $env:C1609_VOLUME_CENSUS_FILE -Raw | ConvertFrom-Json }
        catch { Fail-Close 'JournalUnknown' }
        $names = @()
        foreach ($name in @($census.volumes)) {
            if ($null -eq $name) { continue }
            $text = [string]$name
            if ([string]::IsNullOrWhiteSpace($text)) { Fail-Close 'JournalUnknown' }
            $names += $text
        }
        return $names
    }
    if (-not [string]::IsNullOrWhiteSpace($env:C1609_RETIREMENT_ROOT)) { Fail-Close 'CensusMissing' }
    $remote = @'
set -eu
names="antiphon-runner-temp_work antiphon-runner-temp_runner-tmp antiphon-runner-temp_dind-data antiphon-runner-temp_runner-state"
vols=""
for name in $names; do
  if docker volume inspect "$name" >/dev/null 2>&1; then
    vols="$vols\"$name\","
  fi
done
printf '{"volumes":[%s]}\n' "${vols%,}"
'@
    $text = Invoke-Server2Bash -Script $remote
    try { $census = $text | ConvertFrom-Json } catch { Fail-Close 'JournalUnknown' }
    $names = @()
    foreach ($name in @($census.volumes)) { if ($null -ne $name) { $names += [string]$name } }
    return $names
}

function Invoke-Server2Bash([string]$Script) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'ssh'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    foreach ($token in @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15', 'mc@server2', 'bash', '-s')) {
        [void]$psi.ArgumentList.Add($token)
    }
    $proc = [System.Diagnostics.Process]::Start($psi)
    try {
        $proc.StandardInput.Write($Script)
        $proc.StandardInput.Close()
        $stdout = $proc.StandardOutput.ReadToEndAsync()
        $stderr = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit(30000)) { $proc.Kill($true); $proc.WaitForExit(); Fail-Close 'JournalUnknown' }
        $text = $stdout.GetAwaiter().GetResult()
        [void]$stderr.GetAwaiter().GetResult()
        if ($proc.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($text)) { Fail-Close 'JournalUnknown' }
        return $text
    }
    finally { $proc.Dispose() }
}

function Get-OperationDirectory([string]$Id) {
    if (-not (Test-OperationId $Id)) { Fail-Close 'OperationId' }
    if (-not [string]::IsNullOrWhiteSpace($env:C1609_RETIREMENT_ROOT)) {
        $root = $env:C1609_RETIREMENT_ROOT
        if (Test-Reparse $root) { Fail-Close 'JournalUnknown' }
        return (Join-Path $root $Id)
    }
    return $null
}

function Read-ReceiptFile([string]$Id) {
    $dir = Get-OperationDirectory $Id
    if ($null -ne $dir) {
        $path = Join-Path $dir 'receipt.json'
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Fail-Close 'ReceiptMissing' }
        if (Test-Reparse $path -or (Test-Reparse $dir)) { Fail-Close 'JournalUnknown' }
        return [System.IO.File]::ReadAllBytes($path)
    }
    $remote = "set -eu`n" + "id='$Id'`n" + @'
root=/home/mc/antiphon-server2/retirement
file="$root/$id/receipt.json"
if [ -L "$root" ] || [ -L "$root/$id" ] || [ -L "$file" ] || [ ! -f "$file" ]; then
  exit 2
fi
cat -- "$file"
'@
    $text = Invoke-Server2Bash -Script $remote
    return [System.Text.Encoding]::UTF8.GetBytes($text)
}

function Assert-VolumesAbsent([string[]]$Names) {
    foreach ($name in $Names) {
        if ($script:TempVolumeNames -contains $name) { Fail-Close "TempVolumesRetained $name" }
        Fail-Close 'JournalUnknown'
    }
}

function Read-ReceiptObject([byte[]]$Bytes) {
    try {
        $text = [System.Text.Encoding]::UTF8.GetString($Bytes)
        return ($text | ConvertFrom-Json)
    }
    catch { Fail-Close 'JournalUnknown' }
}

if ($DryRun -or $Preview) {
    [void](Get-CloseAuthority)
    $names = @(Read-CensusVolumes)
    Assert-VolumesAbsent $names
    $root = $env:C1609_RETIREMENT_ROOT
    $journals = 0
    if (-not [string]::IsNullOrWhiteSpace($root) -and (Test-Path -LiteralPath $root -PathType Container)) {
        if (Test-Reparse $root) { Fail-Close 'JournalUnknown' }
        $journals = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue).Count
    }
    Write-Output ("TempRetirementCensus volumes=0 journals=" + $journals)
    exit 0
}

if (-not (Test-OperationId $OperationId)) { Fail-Close 'OperationId' }
if ($ExpectedJournalDigest -cnotmatch '^[0-9a-f]{64}$') { Fail-Close 'Digest' }
$bytes = Read-ReceiptFile $OperationId
$digest = Get-Sha256Hex $bytes
if ($digest -cne $ExpectedJournalDigest) { Fail-Close 'DigestMismatch' }
$receipt = Read-ReceiptObject $bytes
$schema = 0
try { $schema = [int]$receipt.schema } catch { Fail-Close 'JournalUnknown' }
if ($schema -ne 2) { Fail-Close 'JournalUnknown' }
$owner = [string]$receipt.owner
if (-not [string]::IsNullOrWhiteSpace($owner)) { Fail-Close 'TempRetirementOwnerActive' }
if ($Apply) {
    if ($Sha -cnotmatch '^[0-9a-fA-F]{40}$') { Fail-Close 'ShaRequired' }
    $state = [string]$receipt.state
    $consumed = $false
    if ($null -ne $receipt.consumed) { $consumed = [bool]$receipt.consumed }
    if ($state -ne 'Ready' -or $consumed) { Fail-Close 'ReceiptNotReady' }
    [void](Get-CloseAuthority)
    $deploy = Join-Path $PSScriptRoot 'deploy-server2.ps1'
    & $deploy -Rolling -Sha $Sha.ToLowerInvariant() -Phase retire-temp -ResumeRecycle $OperationId
    exit $LASTEXITCODE
}

$names = @(Read-CensusVolumes)
Assert-VolumesAbsent $names

if ([string]::IsNullOrWhiteSpace($ReasonFile) -or -not (Test-Path -LiteralPath $ReasonFile -PathType Leaf)) { Fail-Close 'ReasonFile' }
if (Test-Reparse $ReasonFile) { Fail-Close 'ReasonFile' }
$reason = [System.IO.File]::ReadAllText($ReasonFile)
if ([string]::IsNullOrWhiteSpace($reason) -or $reason.Length -gt 4096) { Fail-Close 'ReasonFile' }
$auth = Get-CloseAuthority
$observed = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
$reasonJson = ConvertTo-Json -InputObject $reason.Trim() -Compress
$volumeJson = '["antiphon-runner-temp_work","antiphon-runner-temp_runner-tmp","antiphon-runner-temp_dind-data","antiphon-runner-temp_runner-state"]'
$prefix = '{"schema":2,"state":"Closed","operationId":"' + $OperationId + '","priorDigest":"' + $ExpectedJournalDigest +
    '","actor":"' + $auth.Actor + '","authority":"' + $auth.Authority + '","reason":' + $reasonJson +
    ',"observedAt":"' + $observed + '","volumesAbsent":' + $volumeJson + '}'
$closureDigest = Get-Sha256Hex ([System.Text.Encoding]::UTF8.GetBytes($prefix))
$json = $prefix.TrimEnd('}') + ',"digest":"' + $closureDigest + '"}'
$dir = Get-OperationDirectory $OperationId
if ($null -eq $dir) {
    $payload = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($json))
    $remote = @"
set -eu
id='$OperationId'
digest='$ExpectedJournalDigest'
payload='$payload'
root=/home/mc/antiphon-server2/retirement
dir="`$root/`$id"
file="`$dir/receipt.json"
closure="`$dir/closure.json"
if [ -L "`$root" ] || [ -L "`$dir" ] || [ -L "`$file" ] || [ ! -f "`$file" ]; then exit 2; fi
got=`$(sha256sum "`$file" | cut -d' ' -f1)
[ "`$got" = "`$digest" ] || exit 4
for name in antiphon-runner-temp_work antiphon-runner-temp_runner-tmp antiphon-runner-temp_dind-data antiphon-runner-temp_runner-state; do
  if docker volume inspect "`$name" >/dev/null 2>&1; then exit 3; fi
done
if [ -e "`$closure" ]; then
  if [ -L "`$closure" ] || [ ! -f "`$closure" ]; then exit 2; fi
  if jq -e --arg id "`$id" --arg digest "`$digest" '.schema==2 and .state=="Closed" and .operationId==`$id and .priorDigest==`$digest' "`$closure" >/dev/null; then
    printf 'TempRetirementClosed operation=%s idempotent=true\n' "`$id"
    exit 0
  fi
  exit 5
fi
tmp=`$(mktemp "`$dir/closure.XXXXXX")
printf '%s' "`$payload" | base64 -d > "`$tmp"
mv -f -- "`$tmp" "`$closure"
printf 'TempRetirementClosed operation=%s idempotent=false\n' "`$id"
"@
    $text = Invoke-Server2Bash -Script $remote
    Write-Output $text.Trim()
    exit 0
}
$closurePath = Join-Path $dir 'closure.json'
if (Test-Path -LiteralPath $closurePath) {
    if (Test-Reparse $closurePath) { Fail-Close 'JournalUnknown' }
    try { $existing = Get-Content -LiteralPath $closurePath -Raw | ConvertFrom-Json }
    catch { Fail-Close 'JournalUnknown' }
    $existingSchema = 0
    try { $existingSchema = [int]$existing.schema } catch { Fail-Close 'JournalUnknown' }
    if ($existingSchema -eq 2 -and [string]$existing.state -eq 'Closed' -and
        [string]$existing.operationId -eq $OperationId -and [string]$existing.priorDigest -eq $ExpectedJournalDigest) {
        Write-Output "TempRetirementClosed operation=$OperationId idempotent=true"
        exit 0
    }
    Fail-Close 'ClosureConflict'
}
$tmp = Join-Path $dir ('closure.' + [guid]::NewGuid().ToString('N') + '.tmp')
try {
    [System.IO.File]::WriteAllText($tmp, $json, (New-Object System.Text.UTF8Encoding $false))
    [System.IO.File]::Move($tmp, $closurePath)
}
catch {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
    Fail-Close 'WriteFailed'
}
if (-not (Test-Path -LiteralPath $closurePath -PathType Leaf)) { Fail-Close 'WriteFailed' }
Write-Output "TempRetirementClosed operation=$OperationId idempotent=false"
exit 0
