#requires -Version 5.1
# CARD-1842 S1 witnesses. ASCII-only. No network, no git, no qualification launch.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$env:ANTIPHON_NIGHTLY_CORE = 'off'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('cm-' + [guid]::NewGuid().ToString('N'))
}
if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    New-Item -ItemType Directory -Path $ResultsDirectory | Out-Null
}
$script:RefreshScript = Join-Path $PSScriptRoot 'refresh-nightly-class-costs.ps1'
if (-not (Test-Path -LiteralPath $script:RefreshScript)) { throw 'refresh-nightly-class-costs.ps1 is absent' }
$script:CmErrors = New-Object System.Collections.Generic.List[string]
function Note([bool]$Ok, [string]$Detail) {
    if (-not $Ok) { $script:CmErrors.Add($Detail) }
}
function Assert-Contract {
    Assert-C487 ($script:CmErrors.Count -eq 0) ('CM ' + $Case + '.contract') ($script:CmErrors -join '; ')
}
function Write-Fixture([string]$Path, [string]$Body) {
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    $text = $Body.Replace("`r`n", "`n")
    if (-not $text.EndsWith("`n")) { $text += "`n" }
    [System.IO.File]::WriteAllText($Path, $text)
}
function Write-Trx([string]$Path, [object[]]$Rows) {
    $defs = ''
    $results = ''
    $index = 0
    foreach ($row in @($Rows)) {
        $index++
        $id = 't' + $index
        $className = [string]$row.Class
        $method = [string]$row.Method
        $duration = [string]$row.Duration
        $defs += ('<UnitTest id="{0}" name="{1}.{2}"><TestMethod className="{1}" name="{2}" /></UnitTest>' -f $id, $className, $method)
        $results += ('<UnitTestResult testId="{0}" testName="{1}.{2}" duration="{3}" outcome="Passed" />' -f $id, $className, $method, $duration)
    }
    $xml = '<?xml version="1.0" encoding="utf-8"?><TestRun><TestDefinitions>' + $defs + '</TestDefinitions><Results>' + $results + '</Results></TestRun>'
    Write-Fixture $Path $xml
}
function Write-CostFixture([string]$Path) {
    $json = @'
{
  "format": "antiphon-nightly-class-costs-v1",
  "platform": "windows",
  "source": "document-source",
  "refresh": "old-refresh",
  "classes": {
    "Antiphon.Tests.AbsentTests": {
      "nodes": 3,
      "seconds": 30,
      "basis": "pace",
      "source": "absent-row-source",
      "hostId": "old-host",
      "os": "windows",
      "measuredRunId": "old-run"
    },
    "Antiphon.Tests.KeptTests": {
      "nodes": 4,
      "seconds": 100,
      "basis": "measured",
      "source": "kept-row-source"
    }
  }
}
'@
    Write-Fixture $Path $json
}
function Invoke-Refresh {
    param([string[]]$RefreshArgs)
    $lines = & pwsh -NoProfile -File $script:RefreshScript @RefreshArgs 2>&1
    $code = $LASTEXITCODE
    $text = @($lines) -join "`n"
    return [pscustomobject]@{ Code = [int]$code; Text = [string]$text }
}
function Read-Cost([string]$Path) {
    return ([System.IO.File]::ReadAllText($Path) | ConvertFrom-Json)
}
function Get-Row($Doc, [string]$Name) {
    if ($null -eq $Doc -or $null -eq $Doc.classes) { return $null }
    $prop = $Doc.classes.PSObject.Properties[$Name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}
function Test-Has($Obj, [string]$Name) {
    if ($null -eq $Obj) { return $false }
    return $null -ne $Obj.PSObject.Properties[$Name]
}
function Test-Near([object]$Actual, [double]$Expected) {
    if ($null -eq $Actual) { return $false }
    return ([Math]::Abs([double]$Actual - $Expected) -lt 0.001)
}

switch ($Case) {
    'RefreshKeepsRowSource' {
        $dir = Join-Path $ResultsDirectory 'keeps-source'
        $cost = Join-Path $dir 'costs.json'
        $trx = Join-Path $dir 'sample.trx'
        $out = Join-Path $dir 'out.json'
        Write-CostFixture $cost
        Write-Trx $trx @(
            [pscustomobject]@{ Class = 'Antiphon.Tests.KeptTests'; Method = 'One'; Duration = '00:00:02' },
            [pscustomobject]@{ Class = 'Antiphon.Tests.KeptTests'; Method = 'Two'; Duration = '00:00:03' },
            [pscustomobject]@{ Class = 'Antiphon.Tests.FreshTests'; Method = 'Only'; Duration = '00:00:09' }
        )
        Copy-Item -LiteralPath $cost -Destination $out
        $run = Invoke-Refresh @(
            '-TrxPath', $trx, '-OutputPath', $out,
            '-HostId', 'fixture-host-c1842', '-Os', 'windows', '-MeasuredRunId', 'run-9'
        )
        Note ($run.Code -eq 0) ('refresh exit ' + $run.Code + ' ' + $run.Text)
        if ($run.Code -eq 0 -and (Test-Path -LiteralPath $out)) {
            $doc = Read-Cost $out
            Note ([string]$doc.format -ceq 'antiphon-nightly-class-costs-v1') ('format ' + $doc.format)
            Note ([string]$doc.platform -ceq 'windows') ('platform ' + $doc.platform)
            $kept = Get-Row $doc 'Antiphon.Tests.KeptTests'
            $absent = Get-Row $doc 'Antiphon.Tests.AbsentTests'
            $fresh = Get-Row $doc 'Antiphon.Tests.FreshTests'
            Note ($null -ne $kept) 'kept row missing'
            Note ($null -ne $absent) 'absent row missing'
            Note ($null -ne $fresh) 'fresh row missing'
            if ($null -ne $kept) {
                Note ((Test-Has $kept 'source') -and [string]$kept.source -ceq 'kept-row-source') ('kept source ' + [string]$kept.source)
                Note ([int]$kept.nodes -eq 2) ('kept nodes ' + $kept.nodes)
                Note (Test-Near $kept.seconds 5) ('kept seconds ' + $kept.seconds)
                Note ([string]$kept.basis -ceq 'trx') ('kept basis ' + $kept.basis)
                Note ([string]$kept.hostId -ceq 'fixture-host-c1842') ('kept host ' + $kept.hostId)
                Note ([string]$kept.os -ceq 'windows') ('kept os ' + $kept.os)
                Note ([string]$kept.measuredRunId -ceq 'run-9') ('kept run ' + $kept.measuredRunId)
            }
            if ($null -ne $absent) {
                Note ([string]$absent.source -ceq 'absent-row-source') ('absent source ' + [string]$absent.source)
                Note ([int]$absent.nodes -eq 3) ('absent nodes ' + $absent.nodes)
                Note (Test-Near $absent.seconds 30) ('absent seconds ' + $absent.seconds)
                Note ([string]$absent.basis -ceq 'pace') ('absent basis ' + $absent.basis)
                Note ([string]$absent.hostId -ceq 'old-host') ('absent host ' + $absent.hostId)
                Note ([string]$absent.os -ceq 'windows') ('absent os ' + $absent.os)
                Note ([string]$absent.measuredRunId -ceq 'old-run') ('absent run ' + $absent.measuredRunId)
            }
            if ($null -ne $fresh) {
                Note (-not (Test-Has $fresh 'source')) 'fresh row invented a source'
                Note ([int]$fresh.nodes -eq 1) ('fresh nodes ' + $fresh.nodes)
                Note (Test-Near $fresh.seconds 9) ('fresh seconds ' + $fresh.seconds)
                Note ([string]$fresh.basis -ceq 'trx') ('fresh basis ' + $fresh.basis)
                Note ([string]$fresh.hostId -ceq 'fixture-host-c1842') ('fresh host ' + $fresh.hostId)
                Note ([string]$fresh.os -ceq 'windows') ('fresh os ' + $fresh.os)
                Note ([string]$fresh.measuredRunId -ceq 'run-9') ('fresh run ' + $fresh.measuredRunId)
            }
        }
        Assert-Contract
    }
    'RefreshRequiresHostOsAndRun' {
        $dir = Join-Path $ResultsDirectory 'requires-identity'
        $cost = Join-Path $dir 'costs.json'
        $trx = Join-Path $dir 'sample.trx'
        Write-CostFixture $cost
        Write-Trx $trx @([pscustomobject]@{ Class = 'Antiphon.Tests.KeptTests'; Method = 'One'; Duration = '00:00:05' })
        $savedComputer = $env:COMPUTERNAME
        $savedHost = $env:HOSTNAME
        $env:COMPUTERNAME = 'decoy-machine-c1842'
        $env:HOSTNAME = 'decoy-machine-c1842'
        try {
            $outOmit = Join-Path $dir 'omit-host.json'
            Copy-Item -LiteralPath $cost -Destination $outOmit
            $omitHost = Invoke-Refresh @('-TrxPath', $trx, '-OutputPath', $outOmit, '-Os', 'windows', '-MeasuredRunId', 'run-9')
            Note ($omitHost.Code -ne 0 -and $omitHost.Text.Contains('HostId')) ('omitted HostId ' + $omitHost.Code + ' ' + $omitHost.Text)
            $outOs = Join-Path $dir 'omit-os.json'
            Copy-Item -LiteralPath $cost -Destination $outOs
            $omitOs = Invoke-Refresh @('-TrxPath', $trx, '-OutputPath', $outOs, '-HostId', 'fixture-host-c1842', '-MeasuredRunId', 'run-9')
            Note ($omitOs.Code -ne 0 -and $omitOs.Text.Contains('Os')) ('omitted Os ' + $omitOs.Code + ' ' + $omitOs.Text)
            $outRun = Join-Path $dir 'omit-run.json'
            Copy-Item -LiteralPath $cost -Destination $outRun
            $omitRun = Invoke-Refresh @('-TrxPath', $trx, '-OutputPath', $outRun, '-HostId', 'fixture-host-c1842', '-Os', 'windows')
            Note ($omitRun.Code -ne 0 -and $omitRun.Text.Contains('MeasuredRunId')) ('omitted MeasuredRunId ' + $omitRun.Code + ' ' + $omitRun.Text)
            $outBad = Join-Path $dir 'bad-os.json'
            Copy-Item -LiteralPath $cost -Destination $outBad
            $badOs = Invoke-Refresh @('-TrxPath', $trx, '-OutputPath', $outBad, '-HostId', 'fixture-host-c1842', '-Os', 'unix', '-MeasuredRunId', 'run-9')
            Note ($badOs.Code -ne 0 -and $badOs.Text.Contains('class-cost-os')) ('unix os ' + $badOs.Code + ' ' + $badOs.Text)
            $outBlank = Join-Path $dir 'blank-host.json'
            Copy-Item -LiteralPath $cost -Destination $outBlank
            $blank = Invoke-Refresh @('-TrxPath', $trx, '-OutputPath', $outBlank, '-HostId', ' ', '-Os', 'windows', '-MeasuredRunId', 'run-9')
            Note ($blank.Code -ne 0 -and $blank.Text.Contains('class-cost-host')) ('blank host ' + $blank.Code + ' ' + $blank.Text)
            $out = Join-Path $dir 'out.json'
            Copy-Item -LiteralPath $cost -Destination $out
            $run = Invoke-Refresh @('-TrxPath', $trx, '-OutputPath', $out, '-HostId', 'fixture-host-c1842', '-Os', 'windows', '-MeasuredRunId', 'run-9')
            Note ($run.Code -eq 0) ('refresh exit ' + $run.Code + ' ' + $run.Text)
            if ($run.Code -eq 0 -and (Test-Path -LiteralPath $out)) {
                $doc = Read-Cost $out
                $kept = Get-Row $doc 'Antiphon.Tests.KeptTests'
                Note ($null -ne $kept) 'kept row missing'
                if ($null -ne $kept) {
                    Note ([string]$kept.hostId -ceq 'fixture-host-c1842') ('host ' + $kept.hostId)
                    Note ([string]$kept.os -ceq 'windows') ('os ' + $kept.os)
                    Note ([string]$kept.measuredRunId -ceq 'run-9') ('run ' + $kept.measuredRunId)
                    Note ([string]$kept.hostId -cne 'decoy-machine-c1842') ('host followed COMPUTERNAME ' + $kept.hostId)
                    Note ([string]$kept.hostId -cne [Environment]::MachineName) ('host followed machine name ' + $kept.hostId)
                    Note (Test-Near $kept.seconds 5) ('seconds ' + $kept.seconds)
                }
                Note ([string]$doc.platform -ceq 'windows') ('platform ' + $doc.platform)
            }
        } finally {
            $env:COMPUTERNAME = $savedComputer
            $env:HOSTNAME = $savedHost
        }
        Assert-Contract
    }
    'ForeignOsDoesNotReplaceSeconds' {
        $dir = Join-Path $ResultsDirectory 'foreign-os'
        $trx = Join-Path $dir 'sample.trx'
        $out = Join-Path $dir 'out.json'
        Write-CostFixture (Join-Path $dir 'costs.json')
        Copy-Item -LiteralPath (Join-Path $dir 'costs.json') -Destination $out
        Write-Trx $trx @(
            [pscustomobject]@{ Class = 'Antiphon.Tests.KeptTests'; Method = 'One'; Duration = '00:00:08' },
            [pscustomobject]@{ Class = 'Antiphon.Tests.FreshTests'; Method = 'Only'; Duration = '00:00:09' }
        )
        $run = Invoke-Refresh @(
            '-TrxPath', $trx, '-OutputPath', $out,
            '-HostId', 'linux-host-c1842', '-Os', 'linux', '-MeasuredRunId', 'linux-run'
        )
        Note ($run.Code -eq 0) ('refresh exit ' + $run.Code + ' ' + $run.Text)
        if ($run.Code -eq 0 -and (Test-Path -LiteralPath $out)) {
            $doc = Read-Cost $out
            Note ([string]$doc.platform -ceq 'windows') ('platform ' + $doc.platform)
            $kept = Get-Row $doc 'Antiphon.Tests.KeptTests'
            $absent = Get-Row $doc 'Antiphon.Tests.AbsentTests'
            $fresh = Get-Row $doc 'Antiphon.Tests.FreshTests'
            Note ($null -ne $kept) 'kept row missing'
            Note ($null -ne $absent) 'absent row missing'
            Note ($null -eq $fresh) 'foreign refresh added a primary row'
            if ($null -ne $kept) {
                Note (Test-Near $kept.seconds 100) ('primary seconds ' + $kept.seconds)
                Note ([int]$kept.nodes -eq 4) ('primary nodes ' + $kept.nodes)
                Note ([string]$kept.basis -ceq 'measured') ('primary basis ' + $kept.basis)
                Note ([string]$kept.source -ceq 'kept-row-source') ('primary source ' + [string]$kept.source)
                Note (-not (Test-Has $kept 'hostId')) 'foreign refresh wrote primary hostId'
                Note (-not (Test-Has $kept 'measuredRunId')) 'foreign refresh wrote primary measuredRunId'
                $sample = $null
                if (Test-Has $kept 'otherSample') { $sample = $kept.otherSample }
                Note ($null -ne $sample) 'otherSample missing'
                if ($null -ne $sample) {
                    Note (Test-Near $sample.seconds 8) ('other seconds ' + $sample.seconds)
                    Note ([int]$sample.nodes -eq 1) ('other nodes ' + $sample.nodes)
                    Note ([string]$sample.basis -ceq 'trx') ('other basis ' + $sample.basis)
                    Note ([string]$sample.hostId -ceq 'linux-host-c1842') ('other host ' + $sample.hostId)
                    Note ([string]$sample.os -ceq 'linux') ('other os ' + $sample.os)
                    Note ([string]$sample.measuredRunId -ceq 'linux-run') ('other run ' + $sample.measuredRunId)
                }
            }
            if ($null -ne $absent) {
                Note (Test-Near $absent.seconds 30) ('absent seconds ' + $absent.seconds)
                Note ([string]$absent.source -ceq 'absent-row-source') ('absent source ' + [string]$absent.source)
                Note ([string]$absent.hostId -ceq 'old-host') ('absent host ' + $absent.hostId)
                Note (-not (Test-Has $absent 'otherSample')) 'absent row gained otherSample'
            }
        }
        Assert-Contract
    }
    'PackerStillReadsPrimarySeconds' {
        $dir = Join-Path $ResultsDirectory 'packer'
        $cost = Join-Path $dir 'costs.json'
        $json = @'
{
  "format": "antiphon-nightly-class-costs-v1",
  "platform": "windows",
  "source": "document-source",
  "refresh": "old-refresh",
  "classes": {
    "Antiphon.Tests.SampleTests": {
      "nodes": 2,
      "seconds": 100,
      "basis": "trx",
      "source": "row-source",
      "hostId": "fixture-host-c1842",
      "os": "windows",
      "measuredRunId": "run-1",
      "otherSample": { "nodes": 2, "seconds": 999, "basis": "trx", "hostId": "linux-host", "os": "linux", "measuredRunId": "linux-run" }
    }
  }
}
'@
        Write-Fixture $cost $json
        $impl = Join-Path $PSScriptRoot 'lib/nightly-tests-impl.ps1'
        if (-not (Test-Path -LiteralPath $impl)) { throw 'nightly-tests-impl.ps1 is absent' }
        . $impl
        $script:NightlyClassCostPath = $cost
        $script:NightlyClassCostTable = $null
        $table = Get-NightlyClassCostTable
        $per = $null
        if ($null -ne $table -and $table.ContainsKey('Antiphon.Tests.SampleTests')) { $per = $table['Antiphon.Tests.SampleTests'] }
        Note (Test-Near $per 50) ('table per-node ' + $per)
        $node = [pscustomobject]@{ Categories = @() }
        $priced = Get-NightlyLaunchClassCost -Nodes @($node, $node) -TypeName 'SampleTests' -Bare 'Antiphon.Tests.SampleTests'
        Note ([int]$priced.NodeCount -eq 2) ('node count ' + $priced.NodeCount)
        Note (Test-Near $priced.MeasuredPerNode 50) ('measured per-node ' + $priced.MeasuredPerNode)
        Note (Test-Near $priced.Seconds 100) ('priced seconds ' + $priced.Seconds)
        Assert-Contract
    }
    default { throw ('unknown case ' + $Case) }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
