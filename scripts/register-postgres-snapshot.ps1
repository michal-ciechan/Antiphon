#requires -Version 5.1
<#
.SYNOPSIS
    Preview or register the plain Postgres snapshot Windmill job.

    Preview is the default and contacts nothing. -Apply registers on server2.
    -EnableSchedule requires -Apply. Operator 2026-11-10 asked for the daily
    job, which is the human enable CARD-1405 kept off the script by default.
    The Windmill bearer is minted and deleted on server2. This process never
    reads it and never writes it.
    ASCII-only.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$EnableSchedule,
    [switch]$RunOnce,
    [string]$DefinitionRoot = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($DefinitionRoot)) { $DefinitionRoot = Join-Path $PSScriptRoot 'windmill' }
if ($EnableSchedule -and -not $Apply) {
    Write-Output 'REFUSED: -EnableSchedule requires -Apply.'
    exit 3
}
if ($RunOnce -and -not $Apply) {
    Write-Output 'REFUSED: -RunOnce requires -Apply.'
    exit 3
}

$pairs = @(
    @{ Script = 'antiphon-postgres-snapshot.json'; Schedule = 'antiphon-postgres-snapshot.schedule.json' },
    @{ Script = 'antiphon-postgres-snapshot-stale.json'; Schedule = 'antiphon-postgres-snapshot-stale.schedule.json' }
)
foreach ($pair in $pairs) {
    $scriptPath = Join-Path $DefinitionRoot $pair.Script
    $schedulePath = Join-Path $DefinitionRoot $pair.Schedule
    if (-not (Test-Path -LiteralPath $scriptPath) -or -not (Test-Path -LiteralPath $schedulePath)) {
        Write-Output ('REFUSED: missing definition ' + $pair.Script)
        exit 3
    }
    $scriptDef = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $scheduleDef = Get-Content -LiteralPath $schedulePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$scheduleDef.script_path -ne [string]$scriptDef.path) {
        Write-Output ('REFUSED: schedule does not name script ' + $scriptDef.path)
        exit 3
    }
    $enabledText = 'false'
    if ([bool]$scheduleDef.enabled) { $enabledText = 'true' }
    Write-Output ('preview path=' + $scheduleDef.path + ' schedule=' + $scheduleDef.schedule + ' timezone=' + $scheduleDef.timezone + ' enabled=' + $enabledText)
}
if (-not $Apply) { exit 0 }

$remote = @'
set -euo pipefail
ROOT=/tmp/antiphon-snapshot-reg
TOK=$(openssl rand -hex 20)
HASH=$(printf %s "$TOK" | sha256sum | cut -d' ' -f1)
PREFIX=${TOK:0:10}
cleanup() {
  docker exec windmill-windmill_db-1 psql -U windmill -d windmill -c "DELETE FROM token WHERE label='snapshot-reg' AND token_prefix='${PREFIX}';" >/dev/null 2>&1 || true
  unset TOK HASH WM_TOKEN
  rm -f /tmp/snapshot-reg.psql
  rm -rf "$ROOT"
}
trap cleanup EXIT
if ! docker exec windmill-windmill_db-1 psql -U windmill -d windmill -v ON_ERROR_STOP=1 -c "INSERT INTO token (token,token_hash,token_prefix,email,super_admin,label,expiration) VALUES ('${TOK}','${HASH}','${PREFIX}','lndcobra@hotmail.com',true,'snapshot-reg',now()+interval '2 hours');" >/tmp/snapshot-reg.psql 2>&1; then
  echo "psql=fail"
  exit 1
fi
echo "psql=ok"
export WM_TOKEN="$TOK"
set +e
python3 "$ROOT/register-postgres-snapshot.py" "$ROOT" "__MODE__" "__RUN__"
status=$?
set -e
unset WM_TOKEN
exit "$status"
'@
$mode = 'keep'
if ($EnableSchedule) { $mode = 'enable' }
$run = 'skip'
if ($RunOnce) { $run = 'run' }
$remote = $remote.Replace('__MODE__', $mode).Replace('__RUN__', $run)

$local = Join-Path ([System.IO.Path]::GetTempPath()) ('snapshot-reg-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $local | Out-Null
try {
    $remotePath = Join-Path $local 'register-remote.sh'
    $encoding = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($remotePath, ($remote -replace "`r`n", "`n"), $encoding)
    $names = @(
        'antiphon-postgres-snapshot.json',
        'antiphon-postgres-snapshot.schedule.json',
        'antiphon-postgres-snapshot-stale.json',
        'antiphon-postgres-snapshot-stale.schedule.json',
        'register-postgres-snapshot.py'
    )
    foreach ($name in $names) {
        Copy-Item -LiteralPath (Join-Path $DefinitionRoot $name) -Destination (Join-Path $local $name)
    }
    & ssh -o BatchMode=yes -o StrictHostKeyChecking=accept-new mc@server2 "rm -rf /tmp/antiphon-snapshot-reg && mkdir -p /tmp/antiphon-snapshot-reg"
    if ($LASTEXITCODE -ne 0) { Write-Output 'REFUSED: ssh mkdir failed'; exit 1 }
    $uploads = @(Get-ChildItem -LiteralPath $local -File | ForEach-Object { $_.FullName })
    & scp -o BatchMode=yes @uploads "mc@server2:/tmp/antiphon-snapshot-reg/"
    if ($LASTEXITCODE -ne 0) { Write-Output 'REFUSED: scp failed'; exit 1 }
    & ssh -o BatchMode=yes mc@server2 "bash /tmp/antiphon-snapshot-reg/register-remote.sh"
    $code = $LASTEXITCODE
    if ($code -ne 0) { Write-Output ('REFUSED: remote register exit=' + $code); exit $code }
    exit 0
} finally {
    Remove-Item -LiteralPath $local -Recurse -Force -ErrorAction SilentlyContinue
}
