# CARD-0660 V-9 image qualification (Q-1 runtime, Q-2 session-testing; CP-10/11). Evidence
# tooling, not product. Builds ONE image target in the foreground, then grades eight rows by
# running docker/session-runner-grok/verify-codex-image.sh inside throwaway containers of that
# image: phone-home disabled, --network none, no published port, no Docker socket, not
# privileged, and only this run's own volumes. session-testing's DinD entrypoint is overridden
# so no credential-dependent entrypoint boots. The production init-state script runs read-only
# from this checkout, mounted as state-init mounts it, and the runner-side rows observe the same
# volume at /state, with a throwaway volume standing in for the host Codex home at /codex-home
# (state-init) and /state/codex (runner). No external provider is contacted and no real CODEX_HOME
# is touched.
#
# Rows: (1) version (2) layout (3) install-readonly (4) no-baked-auth (5) fresh-home (6) trust
# (7) preserve (8) config-accepted.
# Exit codes: 0 all 8 rows ok, 1 one or more rows not ok, 2 setup failed (dirty or mismatched
# source, reused tag, non-fresh results root, no Docker, failed build).
param(
    [Parameter(Mandatory = $true)][ValidateSet('runtime', 'session-testing')][string] $Target,
    [Parameter(Mandatory = $true)][string] $Image,
    [Parameter(Mandatory = $true)][string] $SourceRevision,
    [Parameter(Mandatory = $true)][string] $ResultsRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent $PSScriptRoot
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$owner = "c660q-$stamp-" + [guid]::NewGuid().ToString('N').Substring(0, 6)
$stateVolume = "$owner-state"
$workVolume = "$owner-work"
# CARD-0660 (amended): stands in for the server2 host directory RUNNER_CODEX_HOME_DIR, mounted where
# docker-compose.server2-runner.yml binds it: /codex-home in state-init, /state/codex in the runner.
$codexVolume = "$owner-codex"
$rows = [ordered]@{
    'version' = 'unknown'; 'layout' = 'unknown'; 'install-readonly' = 'unknown'; 'no-baked-auth' = 'unknown'
    'fresh-home' = 'unknown'; 'trust' = 'unknown'; 'preserve' = 'unknown'; 'config-accepted' = 'unknown'
}
$imageId = 'none'
$volumesCreated = $false

$resultsPath = if ([System.IO.Path]::IsPathRooted($ResultsRoot)) { $ResultsRoot } else { Join-Path $repoRoot $ResultsRoot }

function Write-Evidence([string] $name, [string] $text) {
    Set-Content -LiteralPath (Join-Path $resultsPath $name) -Value $text -Encoding ascii
}

function Invoke-Docker([string[]] $dockerArgs, [string] $evidenceName) {
    $output = & docker @dockerArgs 2>&1 | Out-String
    $code = $LASTEXITCODE
    if ($evidenceName) {
        Write-Evidence $evidenceName ("docker " + ($dockerArgs -join ' ') + "`n--- exit $code ---`n" + $output)
    }
    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Exit-Qualification([int] $code, [string] $reason) {
    if ($script:volumesCreated) {
        foreach ($volume in @($stateVolume, $workVolume, $codexVolume)) { & docker volume rm -f $volume 2>&1 | Out-Null }
    }
    $okCount = @($rows.Values | Where-Object { $_ -eq 'ok' }).Count
    $summary = @()
    if ($reason) { $summary += "C660 IMAGE NOTE: $reason" }
    $summary += ("C660 IMAGE: target={0} image={1} imageId={2} source={3} rows={4}/8 " -f $Target, $Image, $imageId, $SourceRevision, $okCount) +
        (($rows.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' ')
    $summary += "C660 IMAGE EXIT CODE: $code"
    if ($resultsPath -and (Test-Path -LiteralPath $resultsPath)) { Write-Evidence 'result.txt' ($summary -join "`n") }
    $summary | ForEach-Object { Write-Host $_ }
    exit $code
}

# --- setup gates ------------------------------------------------------------------------------
if (Test-Path -LiteralPath $resultsPath) {
    if (@(Get-ChildItem -LiteralPath $resultsPath -Force).Count -gt 0) {
        $resultsPath = $null
        Exit-Qualification 2 'ResultsRoot must be a fresh directory.'
    }
}
New-Item -ItemType Directory -Force -Path $resultsPath | Out-Null

if ($SourceRevision -notmatch '^[0-9a-f]{40}$') { Exit-Qualification 2 'SourceRevision must be a full 40-hex commit SHA.' }
Push-Location $repoRoot
try {
    $head = (& git rev-parse HEAD 2>$null | Out-String).Trim()
    $dirty = (& git status --porcelain --untracked-files=no 2>$null | Out-String).Trim()
} finally { Pop-Location }
if ($head -ne $SourceRevision) { Exit-Qualification 2 "SourceRevision $SourceRevision is not checkout HEAD $head." }
if ($dirty) { Exit-Qualification 2 'Tracked changes are uncommitted; the build context would not be SourceRevision.' }

if ((Invoke-Docker @('version', '--format', '{{.Server.Version}}') 'docker-version.txt').ExitCode -ne 0) {
    Exit-Qualification 2 'Docker daemon is not reachable.'
}
if ((Invoke-Docker @('image', 'inspect', '--format', '{{.Id}}', $Image) $null).ExitCode -eq 0) {
    Exit-Qualification 2 "Image tag $Image already exists; qualification needs a unique tag."
}

# --- build (foreground, the one build this run owns) -----------------------------------------
$build = Invoke-Docker @('build', '--file', (Join-Path $repoRoot 'docker/session-runner-grok/Dockerfile'),
    '--target', $Target, '--build-arg', "SOURCE_REVISION=$SourceRevision", '--tag', $Image, $repoRoot) 'build.log'
if ($build.ExitCode -ne 0) { Exit-Qualification 2 'docker build failed; see build.log.' }
$imageId = (Invoke-Docker @('image', 'inspect', '--format', '{{.Id}}', $Image) 'image-id.txt').Output.Trim()

# --- throwaway volumes and probes -------------------------------------------------------------
foreach ($volume in @($stateVolume, $workVolume, $codexVolume)) {
    if ((Invoke-Docker @('volume', 'create', '--label', "antiphon.c660.owner=$owner", $volume) $null).ExitCode -ne 0) {
        Exit-Qualification 2 "cannot create volume $volume"
    }
    $script:volumesCreated = $true
}

$initScript = Join-Path $repoRoot 'docker/stack/init-state.sh'
$probeScript = Join-Path $repoRoot 'docker/session-runner-grok/verify-codex-image.sh'
$isolation = @('--rm', '--network', 'none', '--env', 'PhoneHome__Enabled=false', '--entrypoint', '/bin/bash')

function Invoke-Init([string] $evidenceName) {
    $initArgs = @('run', '--rm', '--network', 'none', '--user', '0:0', '--entrypoint', '/bin/sh',
        '--mount', "type=volume,source=$workVolume,target=/work",
        '--mount', "type=volume,source=$stateVolume,target=/runner-state",
        '--mount', "type=volume,source=$codexVolume,target=/codex-home",
        '--mount', "type=bind,source=$initScript,target=/stack/init-state.sh,readonly",
        $Image, '/stack/init-state.sh')
    return (Invoke-Docker $initArgs $evidenceName).ExitCode
}

function Invoke-Probe([string] $row, [string] $user, [string[]] $extra) {
    $probeArgs = @('run') + $isolation + @('--user', $user,
        '--tmpfs', '/c660-home:uid=1654,gid=1654,mode=0700',
        '--mount', "type=volume,source=$workVolume,target=/work",
        '--mount', "type=volume,source=$stateVolume,target=/state",
        '--mount', "type=volume,source=$codexVolume,target=/state/codex",
        '--mount', "type=bind,source=$probeScript,target=/c660/verify-codex-image.sh,readonly",
        $Image, '/c660/verify-codex-image.sh', $row) + $extra
    $result = Invoke-Docker $probeArgs "row-$row.txt"
    $line = ($result.Output -split "`n" | Where-Object { $_ -like "C660_ROW $row *" } | Select-Object -Last 1)
    if ($result.ExitCode -eq 0 -and $line -like "C660_ROW $row ok *") { return 'ok' }
    return 'fail'
}

if ((Invoke-Init 'init-1.txt') -ne 0) { Exit-Qualification 2 'first init-state run failed; see init-1.txt.' }

$rows['version'] = Invoke-Probe 'version' '1654:1654' @()
$rows['layout'] = Invoke-Probe 'layout' '1654:1654' @()
$rows['install-readonly'] = Invoke-Probe 'install-readonly' '1654:1654' @()
$rows['no-baked-auth'] = Invoke-Probe 'no-baked-auth' '0:0' @()
$rows['fresh-home'] = Invoke-Probe 'fresh-home' '1654:1654' @()
$rows['trust'] = Invoke-Probe 'trust' '1654:1654' @()
$rows['config-accepted'] = Invoke-Probe 'config-accepted' '1654:1654' @()

# Row 7: sentinel config and auth, a second init, then the same bytes, owner and mode.
$nonce = [guid]::NewGuid().ToString('N')
$armed = Invoke-Probe 'preserve-arm' '1654:1654' @($nonce)
if ($armed -eq 'ok' -and (Invoke-Init 'init-2.txt') -eq 0) {
    $rows['preserve'] = Invoke-Probe 'preserve-check' '1654:1654' @()
} else {
    $rows['preserve'] = 'fail'
}

$failed = @($rows.Values | Where-Object { $_ -ne 'ok' }).Count
if ($failed -gt 0) { Exit-Qualification 1 "$failed row(s) not ok; see row-*.txt." }
Exit-Qualification 0 ''
