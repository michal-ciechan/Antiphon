#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0270 contract tests for the am-service deploy parser and remote seam.

.DESCRIPTION
    Uses temporary Dockerfile fixtures and fake SSH/SCP runners only. It never contacts
    server2 or writes to a remote target. ASCII-only for Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'deploy-am-service.ps1')
$script:passed = 0; $script:failed = 0; $script:failures = @()
function Pass { param([string]$Name) $script:passed++; Write-Host "PASS $Name" }
function Fail { param([string]$Name, [string]$Detail) $script:failed++; $script:failures += "$Name : $Detail"; Write-Host "FAIL $Name - $Detail" }
function Assert-True { param([bool]$Condition, [string]$Name, [string]$Detail = '') if ($Condition) { Pass $Name } else { Fail $Name $Detail } }
function Assert-Throws { param([scriptblock]$Action, [string]$Name) try { & $Action; Fail $Name 'did not throw' } catch { Pass $Name } }

$root = Split-Path -Parent $PSScriptRoot; $context = Join-Path $root 'src'; $dockerfile = Join-Path $context 'Antiphon.Messaging.Service\Dockerfile'; $fixture = Join-Path ([IO.Path]::GetTempPath()) ('am-service-deploy-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Force $fixture | Out-Null
    foreach ($dir in @('one', 'two', 'three')) { New-Item -ItemType Directory -Force (Join-Path $fixture $dir) | Out-Null; [IO.File]::WriteAllText((Join-Path $fixture "$dir\file.txt"), $dir) }
    $fixtureDockerfile = Join-Path $fixture 'Dockerfile'
    [IO.File]::WriteAllText($fixtureDockerfile, "# comment`nCOPY one/ ./one/`nCOPY two/ \`n three/ ./target/`nCOPY --from=build /app ./`nCOPY one/ ./again/")
    $fixtureManifest = @(Get-AmServiceDockerfileManifest $fixtureDockerfile $fixture)
    Assert-True (($fixtureManifest.Path -join ',') -eq 'one,two,three') 'parser handles shell COPY, comments, continuations, order, and de-duplication' ($fixtureManifest.Path -join ',')
    Assert-True ($fixtureManifest.Count -eq 3) 'parser excludes final-stage COPY --from' "count=$($fixtureManifest.Count)"
    $unsafe = @(@('json', 'COPY ["one", "./"]'), @('variable', 'COPY ${SOURCE} ./'), @('glob', 'COPY one/* ./'), @('absolute', 'COPY /one ./'), @('parent', 'COPY ../one ./'), @('option', 'COPY --chown=1000 one ./'), @('missing', 'COPY absent ./'))
    foreach ($case in $unsafe) { [IO.File]::WriteAllText($fixtureDockerfile, $case[1]); Assert-Throws { Get-AmServiceDockerfileManifest $fixtureDockerfile $fixture | Out-Null } "unsafe syntax refuses before archive: $($case[0])" }

    $manifest = @(Get-AmServiceDockerfileManifest $dockerfile $context)
    Assert-True ($manifest.Count -gt 0) 'real Dockerfile derives source entries'
    $missing = @($manifest | Where-Object { -not (Test-Path -LiteralPath (Join-Path $context $_.Path)) })
    Assert-True ($missing.Count -eq 0) 'every real Dockerfile-derived source exists' ($missing.Path -join ',')
    $archive = New-AmServiceArchive $context $manifest
    try {
        Assert-True ($archive.Entries.Count -gt 0) 'real Dockerfile archive is non-empty'
        $forbidden = @($archive.Entries | Where-Object { ($_ -split '/') | Where-Object { $_ -eq 'bin' -or $_ -eq 'obj' -or $_ -like 'bin-*' } })
        Assert-True ($forbidden.Count -eq 0) 'real Dockerfile archive excludes bin/obj/bin-* at every depth' ($forbidden -join ',')
        $archiveMissing = @($manifest | Where-Object { $expected = $_.Path; -not ($archive.Entries | Where-Object { $_.TrimEnd('/') -eq $expected -or $_.StartsWith($expected + '/') }) })
        Assert-True ($archiveMissing.Count -eq 0) 'real Dockerfile archive contains every derived source' ($archiveMissing.Path -join ',')
    } finally { Remove-Item -LiteralPath $archive.Path -Force -ErrorAction SilentlyContinue }

    $script:commands = New-Object 'System.Collections.Generic.List[string]'; $script:scpCalls = New-Object 'System.Collections.Generic.List[string]'
    $ssh = { param($command) $script:commands.Add($command); if ($command -match 'docker compose config') { return [pscustomobject]@{ExitCode=0;Output='{"context":"/home/mc/antiphon-messaging/build/src","dockerfile":"Antiphon.Messaging.Service/Dockerfile","secret":"never-log-this"}'.Replace('\','')} }; if ($command -match 'docker inspect') { return [pscustomobject]@{ExitCode=0;Output=@('old-container old-image running','{"secret":"never-log-this"}'.Replace('\',''))} }; return [pscustomobject]@{ExitCode=0;Output=@()} }
    $scp = { param($source,$destination) $script:scpCalls.Add("$source -> $destination"); return [pscustomobject]@{ExitCode=0;Output=@()} }
    $preflightLog = @(Invoke-AmServiceDeployment $root $false $false 10 $ssh $scp 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    Assert-True ($script:scpCalls.Count -eq 0) 'remote seam makes no SCP call without -Deploy' "calls=$($script:scpCalls.Count)"
    Assert-True (-not (($script:commands -join "`n") -match 'tar xzf|docker compose build|docker compose up')) 'remote seam makes no write command without -Deploy' ($script:commands -join ' | ')
    Assert-True (-not $preflightLog.Contains('never-log-this')) 'remote seam narrows Compose output before logging' $preflightLog

    $script:commands.Clear(); $script:scpCalls.Clear()
    $failingSsh = { param($command) $script:commands.Add($command); if ($command -match 'docker compose config') { return [pscustomobject]@{ExitCode=0;Output='{"context":"/home/mc/antiphon-messaging/build/src","dockerfile":"Antiphon.Messaging.Service/Dockerfile"}'.Replace('\','')} }; if ($command -match 'docker inspect') { return [pscustomobject]@{ExitCode=0;Output=@('old-container old-image running','{}')} }; if ($command -match 'docker compose build') { return [pscustomobject]@{ExitCode=9;Output='secret=never-log-this'} }; return [pscustomobject]@{ExitCode=0;Output=@()} }
    $failureText = ''
    try { Invoke-AmServiceDeployment $root $true $true 10 $failingSsh $scp | Out-Null } catch { $failureText = $_.Exception.Message }
    Assert-True ($script:scpCalls.Count -eq 1) 'remote seam uploads only with -Deploy' "calls=$($script:scpCalls.Count)"
    Assert-True ($failureText -match 'retained source backup: /home/mc/antiphon-messaging/build/src\.bak-') 'remote seam retains backup evidence after remote failure' $failureText
    Assert-True (-not $failureText.Contains('never-log-this')) 'remote seam failure diagnostics do not leak remote output' $failureText

    $script:commands.Clear(); $script:scpCalls.Clear(); $migrationOutput = @(Get-AmServiceMigrationIds (Join-Path $context 'Antiphon.Messaging.Service\Migrations'))
    $successSsh = { param($command) $script:commands.Add($command); if ($command -match 'docker compose config') { return [pscustomobject]@{ExitCode=0;Output='{"context":"/home/mc/antiphon-messaging/build/src","dockerfile":"Antiphon.Messaging.Service/Dockerfile"}'.Replace('\','')} }; if ($command -match 'curl -fsS') { return [pscustomobject]@{ExitCode=0;Output=(@('[{"channel":"telegram"},{"channel":"slack"}]'.Replace('\','')) + $migrationOutput)} }; if ($command -match 'docker inspect') { return [pscustomobject]@{ExitCode=0;Output=@('old-container old-image running','{"State":"running"}'.Replace('\',''))} }; return [pscustomobject]@{ExitCode=0;Output=@()} }
    $success = Invoke-AmServiceDeployment $root $true $true 10 $successSsh $scp
    Assert-True (($success.AdapterNames -join ',') -eq 'telegram,slack') 'remote seam parses endpoint adapters after recreate' ($success.AdapterNames -join ',')
    Assert-True (((($script:commands -join "`n") -match 'docker compose build messaging-service') -and (($script:commands -join "`n") -match 'docker compose up -d --no-deps messaging-service'))) 'remote seam issues fixed-target build and recreate sequence' ($script:commands -join ' | ')
    $cleanupWasRequested = (($script:commands -join "`n") -match "rm -f -- '/tmp/am-service-src-")
    Assert-True $cleanupWasRequested 'remote seam removes transient upload after handled deployment' ($script:commands -join ' | ')
} catch { Fail 'test setup or execution' $_.Exception.Message } finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue } }
Write-Host ''; Write-Host ('CARD-0270 deploy-am-service: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed) { foreach ($item in $script:failures) { Write-Host ('  ' + $item) }; Write-Host 'DEPLOY-AM-SERVICE TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'; exit 1 }
Write-Host 'DEPLOY-AM-SERVICE TESTS EXIT CODE: 0  (PASS)'; exit 0
