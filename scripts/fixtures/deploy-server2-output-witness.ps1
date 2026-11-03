# Real-transport output witness for scripts/deploy-server2.ps1 (Linux only).
# Extracts the named functions verbatim from the wrapper AST, leaves the
# C727_TEST_VERIFY_STUB seam unset and puts a fake ssh first on PATH, so the
# stdin write the stub path skips really runs. Prints one OUTPUT row per case.
# HoldLift/HoldLiftRefused run the two deploy-temp server2-temp /drain/clear call
# sites, also taken verbatim, against a fake runner HTTP layer (CARD-0948 hold lift).
param(
    [Parameter(Mandatory)][ValidateSet('Census', 'HostJq', 'HoldLift', 'HoldLiftRefused')][string]$Case,
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [string]$Wrapper = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Wrapper) { $Wrapper = Join-Path $root 'scripts/deploy-server2.ps1' }
Remove-Item Env:C727_TEST_VERIFY_STUB, Env:C727_TEST_STATE -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$bin = Join-Path $ResultsDirectory 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$ssh = Join-Path $bin 'ssh'
# The FD8 rollout lock answers ready and holds until released; anything else is a census read.
[IO.File]::WriteAllText($ssh, "#!/bin/sh`ncase `"`$*`" in *rollout.lock*) printf 'C1008_LOCKED\n'; read -r release; exit 0 ;; esac`nprintf '%s\n' `"`$*`" > `"`$W_ROOT/argv`"`ncat > `"`$W_ROOT/stdin`"`ncat `"`$W_ROOT/reply`"`n")
& chmod 0755 $ssh
if ($LASTEXITCODE -ne 0) { throw 'chmod failed' }
$env:W_ROOT = $ResultsDirectory
$env:PATH = $bin + [IO.Path]::PathSeparator + $env:PATH

$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($Wrapper, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw "wrapper parse failed: $($errors[0].Message)" }
function Get-WrapperFunction([string]$Name) {
    $found = @($ast.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $Name }, $false))
    if ($found.Count -ne 1) { throw "wrapper function $Name found $($found.Count) times" }
    $found[0].Extent.Text
}
$Sha = 'a' * 40; $runId = 'c727witness00'; $Phase = 'deploy-temp'
$evidenceRoot = Join-Path $ResultsDirectory 'evidence'
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

function Describe-Output([object[]]$Items) {
    $types = @($Items | ForEach-Object { if ($null -eq $_) { 'null' } else { $_.GetType().FullName } }) -join ','
    "count=$($Items.Count) types=$types"
}

if ($Case -ceq 'Census') {
    . (Join-Path $root 'scripts/c590-real.ps1')
    . (Join-Path $root 'scripts/c1609-read-packet.ps1')
    . ([scriptblock]::Create((Get-WrapperFunction 'Get-TempRetirementCensusRaw')))
    . ([scriptblock]::Create((Get-WrapperFunction 'Assert-TempRetirementClearable')))
    [IO.File]::WriteAllText((Join-Path $ResultsDirectory 'reply'), "{`"volumes`":[],`"journals`":[]}`n")
    $census = @(Get-TempRetirementCensusRaw)
    $streamed = [IO.File]::ReadAllText((Join-Path $ResultsDirectory 'stdin')) -ceq (New-C1609RetirementReadPacket -Operation '' -Clearable)
    Write-Output "OUTPUT census $(Describe-Output $census) streamed=$streamed"
    $proof = @(Get-TempRetirementCensusRaw -ProofOperation 'c100800000000000000000000000000000001')
    Write-Output "OUTPUT proof $(Describe-Output $proof)"
    $clearable = try { $null = @(Assert-TempRetirementClearable); 'ok' } catch { "threw:$($_.Exception.Message)" }
    Write-Output "OUTPUT clearable $clearable"
} elseif ($Case -cin @('HoldLift', 'HoldLiftRefused')) {
    . (Join-Path $root 'scripts/c590-real.ps1')
    . (Join-Path $root 'scripts/c1609-read-packet.ps1')
    foreach ($name in @('Get-TempRetirementCensusRaw', 'Assert-TempRetirementClearable', 'Enter-RolloutAdmissionLock',
        'Invoke-RunnerRequest', 'Invoke-RunnerRequestCore')) { . ([scriptblock]::Create((Get-WrapperFunction $name))) }
    # The two deploy-temp clears, verbatim: the retired-placeholder reactivation and the hold lift.
    $clears = @($ast.FindAll({ param($n) $n -is [Management.Automation.Language.CommandAst] -and
        $n.GetCommandName() -ceq 'Invoke-RunnerRequest' -and $n.Extent.Text.Contains("-RunnerId 'server2-temp' -Suffix '/drain/clear'") }, $true))
    if ($clears.Count -ne 2) { throw "server2-temp clear call sites found $($clears.Count) times" }
    $reactivate = @($clears | Where-Object { $_.Extent.Text.Contains('$clearReason') })
    $holdLift = @($clears | Where-Object { $_.Extent.Text.Contains('CARD-0849 cache verification passed') })
    if ($reactivate.Count -ne 1 -or $holdLift.Count -ne 1) { throw 'clear call sites are not one reactivation and one hold lift' }
    $http = Join-Path $ResultsDirectory 'fake-http.ps1'
    [IO.File]::WriteAllText($http, @'
param([string]$Method, [string]$RunnerId, [string]$Suffix, [string]$BodyJson)
if ($Method -ceq 'POST') { Add-Content -LiteralPath (Join-Path $env:W_ROOT 'posts') -Value "$RunnerId$Suffix $BodyJson" }
else { [IO.File]::ReadAllText((Join-Path $env:W_ROOT 'status.json')) }
exit 0
'@)
    $env:C727_TEST_HTTP_STUB = $http
    $clearReason = 'CARD-0948 retired temp placeholder reactivation'
    $none = "{`"volumes`":[],`"journals`":[]}`n"
    $four = "{`"volumes`":[`"antiphon-runner-temp_work`",`"antiphon-runner-temp_runner-tmp`",`"antiphon-runner-temp_dind-data`",`"antiphon-runner-temp_runner-state`"],`"journals`":[]}`n"
    function Set-Status([string]$RetiredAt, [string]$BuildVersion) {
        $status = [ordered]@{ runnerId = 'server2-temp'; buildVersion = $BuildVersion; available = $true; dispatchEligible = $true
            acceptingNewWork = $false; draining = $true; redirectTo = 'server2'; retireWhenIdle = $false; retiredAt = $null }
        if ($RetiredAt) { $status.retiredAt = $RetiredAt }
        [IO.File]::WriteAllText((Join-Path $ResultsDirectory 'status.json'), ($status | ConvertTo-Json -Compress))
    }
    function Invoke-Clear([string]$Label, $Call, [string]$Census) {
        [IO.File]::WriteAllText((Join-Path $ResultsDirectory 'reply'), $Census)
        $result = try { $null = @(. ([scriptblock]::Create($Call.Extent.Text))); 'ok' } catch { "threw:$($_.Exception.Message)" }
        $posts = Join-Path $ResultsDirectory 'posts'
        $count = if (Test-Path -LiteralPath $posts) { @(Get-Content -LiteralPath $posts).Count } else { 0 }
        Write-Output "OUTPUT $Label $result posts=$count"
    }
    if ($Case -ceq 'HoldLift') {
        # Retired start: no temp volumes yet. deploy-temp-runner then creates all four.
        Set-Status -RetiredAt '2026-10-30T00:45:02Z' -BuildVersion ('b' * 40)
        Invoke-Clear 'reactivate' $reactivate[0] $none
        Set-Status -RetiredAt '' -BuildVersion $Sha
        Invoke-Clear 'holdlift' $holdLift[0] $four
        Set-Status -RetiredAt '2026-10-30T00:45:02Z' -BuildVersion ('b' * 40)
        Invoke-Clear 'reactivate-retained' $reactivate[0] $four
    } else {
        Set-Status -RetiredAt '2026-11-03T09:45:00Z' -BuildVersion $Sha
        Invoke-Clear 'holdlift-retired' $holdLift[0] $four
        Set-Status -RetiredAt '' -BuildVersion ('b' * 40)
        Invoke-Clear 'holdlift-other-build' $holdLift[0] $four
    }
} else {
    # The extracted function reads its helper beside its own file, as the wrapper does.
    $scripts = Join-Path $ResultsDirectory 'scripts'
    New-Item -ItemType Directory -Force -Path $scripts | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'scripts/server2-host-jq.sh') -Destination $scripts
    $extracted = Join-Path $scripts 'invoke-host-jq.ps1'
    [IO.File]::WriteAllText($extracted, (Get-WrapperFunction 'Invoke-HostJq'))
    . $extracted
    $proofJson = [ordered]@{ schema=1; lane='host'; mode='check'; lookupPath='/usr/local/bin/jq'; path='/usr/local/bin/jq';
        version='jq-1.7.1'; digest=('5' * 64); uid=0; gid=0; permissions='755'; trueExit=0; falseExit=1; installed=$false; outcome='existing' } |
        ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $ResultsDirectory 'reply'), $proofJson + "`n")
    $result = @(Invoke-HostJq -Mode check -ExecutingPhase 'check-host-jq')
    $streamed = [IO.File]::ReadAllText((Join-Path $ResultsDirectory 'stdin')) -ceq [IO.File]::ReadAllText((Join-Path $scripts 'server2-host-jq.sh')).Replace("`r`n", "`n")
    $receipt = Test-Path -LiteralPath (Join-Path $evidenceRoot 'host-jq-check-host-jq.json') -PathType Leaf
    Write-Output "OUTPUT hostjq $(Describe-Output $result) streamed=$streamed receipt=$receipt"
}
