#requires -Version 5.1
# CARD-1514 S1. History is a scheduling hint, never a receipt or execution credit.
# ASCII-only. No process, network, live-run or candidate-policy operations.
. (Join-Path $PSScriptRoot 'nightly-coverage.ps1')

function Assert-NightlyHistoryNoLink {
    param([string]$Path)
    $part = [IO.Path]::GetFullPath($Path)
    while ($part) {
        $item = Get-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
        if ($null -ne $item) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'evidence-link' }
        }
        $parent = [IO.Path]::GetDirectoryName($part)
        if ($parent -ceq $part) { break }
        $part = $parent
    }
}

function Test-NightlyHistoryUnderRoot {
    param([string]$Path, [string]$Root)
    $comparison = [StringComparison]::Ordinal
    if ([IO.Path]::DirectorySeparatorChar -eq '\') { $comparison = [StringComparison]::OrdinalIgnoreCase }
    return ($Path.Equals($Root, $comparison) -or $Path.StartsWith(($Root.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar), $comparison))
}

function Resolve-NightlyHistoryArtifact {
    param([string]$Root, [string]$Path, $Summary = $null)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '[\x00-\x1f]') { throw 'evidence-path-escape' }
    $relative = $Path.Replace('\','/')
    if (@($relative.Split('/')) -ccontains '..') { throw 'evidence-path-escape' }
    $rebound = $false
    if ($relative.StartsWith('/') -or $relative -match '^[A-Za-z]:/') {
        # Never open the historical absolute path. Rebind only an exact retained
        # logDir prefix or run-id component to this explicitly supplied root.
        $nativeRoot = $Root.Replace('\','/').TrimEnd('/')
        $prefixes = @($nativeRoot)
        if ($null -ne $Summary -and $Summary.logDir) { $prefixes += ([string]$Summary.logDir).Replace('\','/').TrimEnd('/') }
        $found = $false
        foreach ($prefix in $prefixes) {
            if ($relative.StartsWith(($prefix+'/'),[StringComparison]::Ordinal)) {
                $relative=$relative.Substring($prefix.Length+1); $found=$true; break
            }
        }
        if (-not $found -and $null -ne $Summary -and $Summary.runId) {
            $needle='/'+[string]$Summary.runId+'/'
            $at=$relative.IndexOf($needle,[StringComparison]::Ordinal)
            if ($at -ge 0 -and $relative.IndexOf($needle,$at+1,[StringComparison]::Ordinal) -lt 0) {
                $relative=$relative.Substring($at+$needle.Length); $found=$true
            }
        }
        if (-not $found) { throw 'evidence-path-escape: unbound-absolute-path' }
        $rebound=$true
    }
    if (-not $relative -or $relative.StartsWith('/') -or $relative.Contains(':') -or @($relative.Split('/')) -contains '' -or @($relative.Split('/')) -contains '.') { throw 'evidence-path-escape' }
    $full=[IO.Path]::GetFullPath((Join-Path $Root $relative))
    if (-not (Test-NightlyHistoryUnderRoot $full $Root)) { throw 'evidence-path-escape' }
    Assert-NightlyHistoryNoLink $full
    [pscustomobject]@{ FullPath=$full; RelativePath=$relative; Exists=(Test-Path -LiteralPath $full); Rebound=$rebound; Original=$Path }
}

function Get-NightlyHistoryDigest {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
}

function Get-NightlyHistoryOrdinalStrings {
    param([object[]]$Values)
    $set=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $Values) { [void]$set.Add([string]$value) }
    $list=[Collections.Generic.List[string]]::new()
    foreach ($value in $set) { $list.Add($value) }
    $list.Sort([StringComparer]::Ordinal)
    return $list.ToArray()
}

function Assert-NightlyHistoryFields {
    param($Object, [string[]]$Names)
    $keys=@($Object.PSObject.Properties.Name)
    if ($Object -is [Collections.IDictionary]) { $keys=@($Object.Keys) }
    foreach ($name in $Names) { if ($keys -cnotcontains $name) { throw ('history-schema: missing '+$name) } }
}

function Assert-NightlyHistoryClass {
    param([string]$ClassName)
    if ($ClassName -cnotmatch '^[A-Za-z_][A-Za-z0-9_+`]*(\.[A-Za-z_][A-Za-z0-9_+`]*)+$') { throw 'history-class-identity' }
}

function Assert-NightlyHistorySchema {
    param($History)
    Assert-NightlyHistoryFields $History @('format','sourceRunId','sourceAttempt','sourceSha','profile','platform','policyHash','createdAtUtc','complete','evidence','members','fallbackChunks','membershipComplete','unresolved')
    if ($History.format -cne 'antiphon-nightly-prior-failures-v1' -or $History.complete -isnot [bool] -or -not $History.complete -or $History.membershipComplete -isnot [bool] -or [string]$History.sourceSha -cnotmatch '^[a-f0-9]{40}$' -or [string]$History.policyHash -cnotmatch '^[a-f0-9]{64}$' -or [string]::IsNullOrWhiteSpace([string]$History.sourceRunId) -or [string]::IsNullOrWhiteSpace([string]$History.sourceAttempt) -or $History.profile -cnotin @('rc','nightly') -or $History.platform -cnotin @('windows','linux')) { throw 'history-schema' }
    $date=[datetimeoffset]::MinValue
    if (-not [datetimeoffset]::TryParse([string]$History.createdAtUtc,[ref]$date)) { throw 'history-schema: createdAtUtc' }
    foreach ($name in 'evidence','members','fallbackChunks','unresolved') {
        if ($null -eq $History.$name -or $History.$name -isnot [array]) { throw ('history-schema: array '+$name) }
    }
    $paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($e in @($History.evidence)) {
        Assert-NightlyHistoryFields $e @('relativePath','sha256','kind')
        if ([string]$e.relativePath -match '(^[/\\]|:|(^|[/\\])\.\.([/\\]|$))' -or -not $paths.Add([string]$e.relativePath) -or [string]$e.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $e.kind -cnotin @('summary','trx','diagnostic','discovery','planned','planned-index','timing')) { throw 'history-schema: evidence' }
    }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($m in @($History.members)) {
        Assert-NightlyHistoryFields $m @('suite','className','reasons','observations')
        Assert-NightlyHistoryClass ([string]$m.className)
        if ($m.suite -cnotin @('antiphon','session-runner','pty-host','agents-pty','messaging','e2e','markdown-pdf','observability')) { throw 'unsupported-history-suite' }
        if (-not $seen.Add(([string]$m.suite+'/'+[string]$m.className)) -or $m.reasons -isnot [array] -or $m.observations -isnot [array] -or @($m.reasons).Count -eq 0) { throw 'history-schema: member' }
        foreach ($o in @($m.observations)) {
            Assert-NightlyHistoryFields $o @('artifact','chunk','uid','state','reason','kind')
            if (-not $paths.Contains([string]$o.artifact) -or [string]::IsNullOrWhiteSpace([string]$o.state) -or $o.kind -cnotin @('trx','diagnostic')) { throw 'history-schema: observation' }
        }
    }
    foreach ($u in @($History.unresolved)) {
        Assert-NightlyHistoryFields $u @('suite','reason')
        if ([string]::IsNullOrWhiteSpace([string]$u.reason)) { throw 'history-schema: unresolved' }
    }
    if (-not $History.membershipComplete -and @($History.unresolved).Count -eq 0) { throw 'history-schema: missing unresolved membership' }
    foreach ($f in @($History.fallbackChunks)) {
        Assert-NightlyHistoryFields $f @('suite','chunk','reason','classes','membershipResolved')
        if ($f.membershipResolved -isnot [bool] -or ($f.membershipResolved -and @($f.classes).Count -eq 0) -or (-not $f.membershipResolved -and $null -ne $f.classes)) { throw 'history-schema: fallback' }
        foreach ($c in @($f.classes)) { if ($null -ne $c) { Assert-NightlyHistoryClass ([string]$c) } }
    }
}

function Get-NightlyHistoryFailureNodes {
    param([string]$Path, $Discovery = $null, [switch]$IncludeCompleteness)
    $text=[IO.File]::ReadAllText($Path)
    $byUid=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $discoveryByUid=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    if ($null -ne $Discovery) { foreach ($n in @($Discovery.Nodes)) { $discoveryByUid.Add([string]$n.Uid,$n) } }
    $complete=$true
    $rows=@()
    try {
        if ($text.TrimStart().StartsWith('{')) {
            $doc=$text | ConvertFrom-Json
            Assert-NightlyHistoryFields $doc @('format','tunitVersion','mtpVersion','nodes')
            if ($doc.format -cne 'antiphon-tunit-execution-v1' -or $doc.tunitVersion -cne '1.44.0' -or $doc.mtpVersion -cne '2.2.2') { throw 'unsupported-execution-format' }
            foreach ($r in @($doc.nodes)) {
                Assert-NightlyHistoryFields $r @('uid','state')
                if (-not $discoveryByUid.ContainsKey([string]$r.uid)) { throw 'history-identity-conflict: execution UID not in discovery' }
                $known=$discoveryByUid[[string]$r.uid]
                if (($r.className -and [string]$r.className -cne [string]$known.ClassName) -or ($r.type -and [string]$r.type -cne [string]$known.Type) -or ($r.method -and [string]$r.method -cne [string]$known.Method) -or ($r.namespace -and [string]$r.namespace -cne [string]$known.Namespace)) { throw 'history-identity-conflict: execution mapping' }
                $rows += [pscustomobject]@{Uid=[string]$r.uid; ClassName=[string]$known.ClassName; Method=[string]$known.Method; State=[string]$r.state}
            }
        } else {
            Test-NightlyPinnedDiagnosticVersions $text
            foreach ($record in @(Get-NightlyDiagnosticRecords $text)) {
                $property=Get-NightlyDiagnosticStateProperty $record
                if (-not $property.Name) { $complete=$false; continue }
                try {
                    $n=ConvertFrom-NightlyDiagnosticRecord -Record $record -State $property.Name -StateProperty $property
                    $rows += $n
                } catch { $complete=$false }
            }
            # Every structural update must have been framed and parsed. A torn
            # tail cannot silently make a diagnostic a complete observation set.
            $markers=[regex]::Matches($text,'TestNodeUpdateMessage \{').Count
            if ($markers -eq 0 -or $markers -ne $rows.Count) { $complete=$false }
        }
    } catch {
        if ($_.Exception.Message -clike 'history-identity-conflict*') { throw }
        $complete=$false
    }
    foreach ($n in $rows) {
        $uid=[string]$n.Uid
        if (-not $uid -or -not $n.State) { $complete=$false; continue }
        if ($discoveryByUid.Count -gt 0) {
            if (-not $discoveryByUid.ContainsKey($uid)) { throw 'history-identity-conflict: diagnostic UID not in discovery' }
            $known=$discoveryByUid[$uid]
            if ([string]$known.ClassName -cne [string]$n.ClassName -or [string]$known.Method -cne [string]$n.Method) { throw 'history-identity-conflict: diagnostic mapping' }
        }
        if ($byUid.ContainsKey($uid)) {
            $old=$byUid[$uid]
            if ([string]$old.ClassName -cne [string]$n.ClassName -or [string]$old.Method -cne [string]$n.Method -or $old.State -cnotin @('InProgress','Discovered')) { throw 'history-identity-conflict: duplicate terminal UID' }
        }
        $byUid[$uid]=$n
    }
    $result=[pscustomobject]@{Nodes=@($byUid.Values); Complete=$complete}
    if ($IncludeCompleteness) { return $result }
    return $result.Nodes
}

function Export-NightlyPriorFailures {
    [CmdletBinding()]
    param([string]$RunDirectory, [string]$OutputPath, [switch]$PassThru)
    if ([string]::IsNullOrWhiteSpace($RunDirectory) -or [string]::IsNullOrWhiteSpace($OutputPath)) { throw 'history-path-required' }
    $root=[IO.Path]::GetFullPath($RunDirectory).TrimEnd('\','/')
    if (-not $root -or $root -ceq [IO.Path]::GetPathRoot($root)) { throw 'evidence-path-escape: root' }
    Assert-NightlyHistoryNoLink $root
    $output=[IO.Path]::GetFullPath($OutputPath)
    Assert-NightlyHistoryNoLink $output
    if (Test-NightlyHistoryUnderRoot $output $root) { throw 'history-output-inside-run' }
    if (Test-Path -LiteralPath $output) { throw 'history-output-exists' }
    $evidence=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $rebindings=[Collections.Generic.List[object]]::new()
    function Read-HistoryInput([string]$Path,[string]$Kind,$Summary=$null) {
        $artifact=Resolve-NightlyHistoryArtifact $root $Path $Summary
        if (-not $artifact.Exists) { return $null }
        if (-not (Test-Path -LiteralPath $artifact.FullPath -PathType Leaf)) { throw 'evidence-not-file' }
        $digest=Get-NightlyHistoryDigest $artifact.FullPath
        if ($evidence.ContainsKey($artifact.RelativePath)) {
            if ($evidence[$artifact.RelativePath].sha256 -cne $digest) { throw 'evidence-changed' }
        } else {
            $evidence.Add($artifact.RelativePath,[ordered]@{relativePath=$artifact.RelativePath;sha256=$digest;kind=$Kind})
        }
        if ($artifact.Rebound) { $rebindings.Add([ordered]@{original=$artifact.Original;relativePath=$artifact.RelativePath}) }
        return $artifact
    }
    $summaryArtifact=Read-HistoryInput 'summary.json' 'summary'
    if ($null -eq $summaryArtifact) { throw 'run-not-closed: summary missing' }
    try { $summary=[IO.File]::ReadAllText($summaryArtifact.FullPath) | ConvertFrom-Json } catch { throw 'history-schema: summary' }
    if (-not $summary.runId -and $summary.invocationId) { $summary | Add-Member -NotePropertyName runId -NotePropertyValue ([string]$summary.invocationId) -Force }
    if ($summary.invocationId -and $summary.runId -cne $summary.invocationId) { throw 'history-provenance: run identity' }
    Assert-NightlyHistoryFields $summary @('runId','sha','profile','platform','policyHash','suites','completedAt')
    $date=[datetimeoffset]::MinValue
    if (-not [datetimeoffset]::TryParse([string]$summary.completedAt,[ref]$date) -or $summary.outcome -cin @('Working','Running') -or [string]::IsNullOrWhiteSpace([string]$summary.runId)) { throw 'run-not-closed' }
    $attempt='unknown'
    if ($summary.attempt) { $attempt=[string]$summary.attempt }
    elseif ($summary.selection -and $summary.selection.attempt) { $attempt=[string]$summary.selection.attempt }
    if ([string]$summary.sha -cnotmatch '^[a-f0-9]{40}$' -or [string]$summary.policyHash -cnotmatch '^[a-f0-9]{64}$') { throw 'history-provenance: summary' }
    $members=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $fallback=[Collections.Generic.List[object]]::new()
    $unresolved=[Collections.Generic.List[object]]::new()
    $seenChunks=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $discoveries=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    function Add-HistoryMember([string]$Suite,[string]$Class,[string]$Reason,$Observation=$null) {
        $class=Get-NightlyBareClassIdentity $Class
        Assert-NightlyHistoryClass $class
        $key=$Suite+'/'+$class
        if (-not $members.ContainsKey($key)) { $members.Add($key,[ordered]@{suite=$Suite;className=$class;reasons=@();observations=@()}) }
        if (@($members[$key].reasons) -cnotcontains $Reason) { $members[$key].reasons += $Reason }
        if ($null -ne $Observation) { $members[$key].observations += $Observation }
    }
    foreach ($chunk in @($summary.suites)) {
        $suite=[string]$chunk.id
        if ($suite -cnotin @('antiphon','session-runner','pty-host','agents-pty','messaging','e2e','markdown-pdf','observability')) {
            if ($chunk.trx -or $chunk.execution) { throw 'unsupported-history-suite' }
            continue
        }
        $chunkId=[string]$chunk.chunk
        if (-not $chunkId) { continue } # prerequisite-only suite rows have no native chunk
        if ($chunkId -cnotmatch '^[A-Za-z0-9_-]+$' -or -not $seenChunks.Add($suite+'/'+$chunkId)) { throw 'history-identity-conflict: chunk' }
        if (-not $discoveries.ContainsKey($suite)) {
            $disc=Read-HistoryInput ($suite+'-profile.discovery.json') 'discovery' $summary
            $parsed=$null
            if ($null -ne $disc) {
                try {
                    $doc=[IO.File]::ReadAllText($disc.FullPath) | ConvertFrom-Json
                    Assert-NightlyHistoryFields $doc @('format','tunitVersion','mtpVersion','assemblyHash','nodes')
                    if ($doc.format -cne 'antiphon-tunit-discovery-v1' -or $doc.tunitVersion -cne '1.44.0' -or $doc.mtpVersion -cne '2.2.2') { throw 'discovery-version' }
                    foreach ($n in @($doc.nodes)) { Assert-NightlyHistoryFields $n @('uid','namespace','type','method') }
                    $parsed=ConvertFrom-NightlyDiscoveryDocument $doc 'antiphon-tunit-discovery-v1'
                }
                catch { throw 'history-identity-conflict: discovery' }
            }
            $discoveries.Add($suite,$parsed)
        }
        $discovery=$discoveries[$suite]
        $incomplete=$false; $why=[Collections.Generic.List[string]]::new(); $observed=@()
        $trxName=[string]$chunk.trx
        if (-not $trxName) { $trxName=$suite+'-'+$chunkId+'.trx' }
        $trx=Read-HistoryInput $trxName 'trx' $summary
        if ($null -eq $trx) { $incomplete=$true; $why.Add('missing-trx') }
        else {
            try {
                [xml]$trxDocument=[IO.File]::ReadAllText($trx.FullPath)
                $definitions=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($unit in $trxDocument.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']")) {
                    if (-not $definitions.Add($unit.GetAttribute('id'))) { throw 'history-identity-conflict: trx definition' }
                }
                $trxRows=@(Read-NightlyTrxIdentities $trx.FullPath)
                if ($trxRows.Count -eq 0) { $incomplete=$true; $why.Add('empty-trx') }
                foreach ($r in $trxRows) {
                    $normalized=ConvertTo-NightlyNormalizedOutcome $r.Outcome
                    if ($normalized -cin @('NotExecuted','Unknown')) { $incomplete=$true; $why.Add('incomplete-trx') }
                    $observed += [pscustomobject]@{ClassName=$r.ClassName;Uid=$r.TestId;State=$r.Outcome;Artifact=$trx.RelativePath;Kind='trx'}
                }
            } catch {
                if ($_.Exception.Message -match '^duplicate identity|^missing definition|^unresolved identity|^history-identity-conflict') { throw 'history-identity-conflict: trx' }
                $incomplete=$true; $why.Add('malformed-trx')
            }
        }
        if ($chunk.timedOut -eq $true) { $incomplete=$true; $why.Add('timed-out') }
        $executionName=[string]$chunk.execution
        if (-not $executionName) {
            $directory=Resolve-NightlyHistoryArtifact $root ($suite+'-'+$chunkId+'-execution') $summary
            if ($directory.Exists) {
                $files=@(Get-ChildItem -LiteralPath $directory.FullPath -Filter '*.diag' -File)
                if ($files.Count -gt 1) { throw 'ambiguous-invocation' }
                if ($files.Count -eq 1) { $executionName=$directory.RelativePath+'/'+$files[0].Name }
            }
        }
        if ($executionName) {
            $execution=Read-HistoryInput $executionName 'diagnostic' $summary
            if ($null -eq $execution) { $incomplete=$true; $why.Add('missing-diagnostic') }
            else {
                $parsed=Get-NightlyHistoryFailureNodes -Path $execution.FullPath -Discovery $discovery -IncludeCompleteness
                if (-not $parsed.Complete) { $incomplete=$true; $why.Add('incomplete-diagnostic') }
                foreach ($r in @($parsed.Nodes)) {
                    if ($r.State -ceq 'InProgress') { $incomplete=$true; $why.Add('final-in-progress') }
                    $observed += [pscustomobject]@{ClassName=$r.ClassName;Uid=$r.Uid;State=$r.State;Artifact=$execution.RelativePath;Kind='diagnostic'}
                }
            }
        }
        foreach ($o in $observed) {
            if ($o.State -cin @('InProgress','Discovered') -and -not $incomplete) { continue }
            if ($o.State -ceq 'Discovered' -or (ConvertTo-NightlyNormalizedOutcome $o.State) -cin @('Passed','Skipped','NotExecuted')) { continue }
            $reason='prior-failure'
            if ($o.State -ceq 'InProgress') { $reason='final-in-progress' }
            Add-HistoryMember $suite $o.ClassName $reason ([ordered]@{artifact=$o.Artifact;chunk=$chunkId;uid=$o.Uid;state=$o.State;reason=$reason;kind=$o.Kind})
        }
        if (-not $incomplete) { continue }
        # A retained suite-qualified plan is usable only when its old provenance
        # and UID/class/assembly membership all bind. The last singleton may
        # belong to a completely different suite and supplies no fallback.
        $plan=$null; $planArtifact=$null
        $timing=[string]$summary.timingRoot
        if (-not $timing) { $timing='timing' }
        $timingArtifact=$null
        try { $timingArtifact=Resolve-NightlyHistoryArtifact $root $timing $summary }
        catch { if ($_.Exception.Message -cnotlike 'evidence-path-escape: unbound-absolute-path*') { throw } }
        if ($null -ne $timingArtifact -and $timingArtifact.Exists) {
            $planArtifact=Read-HistoryInput ($timingArtifact.RelativePath+'/groups-planned-'+$suite+'.json') 'planned' $summary
            $perSuite=($null -ne $planArtifact)
            if (-not $perSuite) { $planArtifact=Read-HistoryInput ($timingArtifact.RelativePath+'/groups-planned.json') 'planned' $summary }
            if ($null -ne $planArtifact) {
                try { $candidate=[IO.File]::ReadAllText($planArtifact.FullPath) | ConvertFrom-Json } catch { $candidate=$null }
                if ($null -ne $candidate -and $candidate.suite -ceq $suite) {
                    if ($candidate.version -ne 1) { throw 'history-schema: planned version' }
                    if ($null -ne $discovery -and $candidate.assemblyDigest -and $candidate.assemblyDigest -cne $discovery.AssemblyHash) { throw 'history-provenance: planned assembly' }
                    if ($candidate.runId -cne $summary.runId -or ($attempt -cne 'unknown' -and $candidate.attempt -cne $attempt) -or ($candidate.sourceDigest -and $candidate.sourceDigest -cne $summary.sha) -or ($candidate.policyDigest -and $candidate.policyDigest -cne $summary.policyHash)) { throw 'history-provenance: planned manifest' }
                    $plan=$candidate
                } elseif ($perSuite -and $null -ne $candidate) { throw 'history-provenance: planned suite' }
            }
            $indexArtifact=Read-HistoryInput ($timingArtifact.RelativePath+'/groups-planned-index.json') 'planned-index' $summary
            if ($null -ne $indexArtifact) {
                $index=[IO.File]::ReadAllText($indexArtifact.FullPath) | ConvertFrom-Json
                if ($index.version -ne 1) { throw 'history-schema: planned index version' }
                if ($index.runId -cne $summary.runId -or ($attempt -cne 'unknown' -and $index.attempt -cne $attempt)) { throw 'history-provenance: planned index' }
                $rows=@($index.suites | Where-Object { $_.suite -ceq $suite })
                if ($rows.Count -ne 1 -or $rows[0].runId -cne $summary.runId -or ($rows[0].sourceDigest -and $rows[0].sourceDigest -cne $summary.sha) -or ($rows[0].policyDigest -and $rows[0].policyDigest -cne $summary.policyHash)) { throw 'history-provenance: planned index suite' }
            }
        }
        $classes=@(); $resolved=$false
        if ($null -ne $plan -and $null -ne $discovery -and $plan.sourceDigest -ceq $summary.sha -and $plan.policyDigest -ceq $summary.policyHash -and $plan.assemblyDigest -ceq $discovery.AssemblyHash) {
            $groups=@($plan.groups | Where-Object { $_.chunk -ceq $chunkId })
            if ($groups.Count -gt 1) { throw 'history-identity-conflict: planned chunk' }
            if ($groups.Count -eq 1) {
                $group=$groups[0]
                $classes=@(Get-NightlyHistoryOrdinalStrings @($group.classes))
                $uidMap=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
                foreach ($n in @($discovery.Nodes)) { $uidMap.Add([string]$n.Uid,$n) }
                $uids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                $foundClasses=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($uid in @($group.plannedTestIds)) {
                    if (-not $uids.Add([string]$uid) -or -not $uidMap.ContainsKey([string]$uid)) { throw 'history-identity-conflict: planned UID' }
                    $bare=Get-NightlyBareClassIdentity $uidMap[[string]$uid].ClassName
                    if ($classes -cnotcontains $bare) { throw 'history-identity-conflict: planned class' }
                    [void]$foundClasses.Add($bare)
                }
                foreach ($o in $observed) {
                    if ($classes -cnotcontains (Get-NightlyBareClassIdentity $o.ClassName) -or ($o.Kind -ceq 'diagnostic' -and -not $uids.Contains([string]$o.Uid))) { throw 'history-identity-conflict: observed planned membership' }
                }
                $resolved=($classes.Count -gt 0 -and $uids.Count -gt 0 -and $foundClasses.Count -eq $classes.Count)
            }
        }
        if ($resolved) {
            foreach ($class in $classes) { Add-HistoryMember $suite $class 'incomplete-chunk-fallback' }
        } else {
            $classes=$null
            $unresolved.Add([ordered]@{suite=$suite;chunk=$chunkId;reason='historical-membership-unavailable';evidence=@($summaryArtifact.RelativePath);missingInputs=@($why.ToArray())})
        }
        $fallback.Add([ordered]@{suite=$suite;chunk=$chunkId;reason=(@(Get-NightlyHistoryOrdinalStrings $why.ToArray()) -join ',');classes=$classes;membershipResolved=$resolved})
    }
    $orderedMembers=@(foreach ($key in @(Get-NightlyHistoryOrdinalStrings @($members.Keys))) {
        $m=$members[$key]; $m.reasons=@(Get-NightlyHistoryOrdinalStrings $m.reasons); $m
    })
    $orderedEvidence=@(foreach ($key in @(Get-NightlyHistoryOrdinalStrings @($evidence.Keys))) { $evidence[$key] })
    $history=[ordered]@{
        format='antiphon-nightly-prior-failures-v1';sourceRoot=$root;sourceRunId=[string]$summary.runId;sourceAttempt=$attempt;sourceSha=[string]$summary.sha
        profile=[string]$summary.profile;platform=[string]$summary.platform;policyHash=[string]$summary.policyHash;createdAtUtc=[datetime]::UtcNow.ToString('o');complete=$true
        evidence=$orderedEvidence;members=$orderedMembers;fallbackChunks=@($fallback.ToArray());membershipComplete=($unresolved.Count -eq 0);unresolved=@($unresolved.ToArray());rebindings=@($rebindings.ToArray())
    }
    $document=$history | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    Assert-NightlyHistorySchema $document
    foreach ($e in $orderedEvidence) {
        $a=Resolve-NightlyHistoryArtifact $root $e.relativePath
        if ((Get-NightlyHistoryDigest $a.FullPath) -cne $e.sha256) { throw 'evidence-changed' }
    }
    # Validate everything before the first write. File.Move has no overwrite
    # overload here, including on Windows PowerShell 5.1 / .NET Framework.
    $parent=[IO.Path]::GetDirectoryName($output)
    [void][IO.Directory]::CreateDirectory($parent)
    Assert-NightlyHistoryNoLink $parent
    $temp=$output+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try {
        $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($history | ConvertTo-Json -Depth 30))
        $stream=[IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        [IO.File]::Move($temp,$output)
    } finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
    $digest=Get-NightlyHistoryDigest $output
    Write-Host ('HISTORY SHA256 '+$digest)
    if ($PassThru) { return [pscustomobject]@{Path=$output;Digest=$digest;History=$document} }
}

function Read-NightlyPriorFailures {
    param([string]$Path, [string]$ExpectedDigest = '', [string]$RunDirectory = '')
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'empty-history-path' }
    Assert-NightlyHistoryNoLink $Path
    if ($PSBoundParameters.ContainsKey('ExpectedDigest') -and ([string]::IsNullOrWhiteSpace($ExpectedDigest) -or $ExpectedDigest -cnotmatch '^[a-f0-9]{64}$')) { throw 'history-digest-mismatch' }
    $digest=Get-NightlyHistoryDigest $Path
    if ($ExpectedDigest -and $digest -cne $ExpectedDigest) { throw 'history-digest-mismatch' }
    try { $history=[IO.File]::ReadAllText($Path) | ConvertFrom-Json } catch { throw 'history-schema: JSON' }
    Assert-NightlyHistorySchema $history
    if (-not $RunDirectory) { $RunDirectory=[string]$history.sourceRoot }
    if (-not $RunDirectory) { throw 'history-provenance: source root required' }
    $root=[IO.Path]::GetFullPath($RunDirectory).TrimEnd('\','/')
    Assert-NightlyHistoryNoLink $root
    foreach ($e in @($history.evidence)) {
        $artifact=Resolve-NightlyHistoryArtifact $root ([string]$e.relativePath)
        if (-not $artifact.Exists -or (Get-NightlyHistoryDigest $artifact.FullPath) -cne $e.sha256) { throw 'evidence-digest-mismatch' }
    }
    if (@($history.evidence | Where-Object { $_.relativePath -ceq 'summary.json' -and $_.kind -ceq 'summary' }).Count -ne 1) { throw 'history-provenance: summary missing' }
    $summary=[IO.File]::ReadAllText((Join-Path $root 'summary.json')) | ConvertFrom-Json
    if (-not $summary.runId -and $summary.invocationId) { $summary | Add-Member -NotePropertyName runId -NotePropertyValue ([string]$summary.invocationId) -Force }
    $attempt='unknown'
    if ($summary.attempt) { $attempt=[string]$summary.attempt }
    elseif ($summary.selection -and $summary.selection.attempt) { $attempt=[string]$summary.selection.attempt }
    if ($summary.runId -cne $history.sourceRunId -or $summary.sha -cne $history.sourceSha -or $summary.policyHash -cne $history.policyHash -or $summary.profile -cne $history.profile -or $summary.platform -cne $history.platform -or $attempt -cne $history.sourceAttempt) { throw 'history-provenance' }
    if ((Get-NightlyHistoryDigest $Path) -cne $digest) { throw 'history-digest-mismatch: changed during read' }
    return $history
}

function Resolve-NightlyPriorFailures {
    param($History, [object[]]$Suites, [ValidateSet('Full','PriorFailureSweep')][string]$QualificationMode = 'Full')
    if ($QualificationMode -cnotin @('Full','PriorFailureSweep')) { throw 'history-qualification-mode' }
    Assert-NightlyHistorySchema $History
    $unresolved=[Collections.Generic.List[object]]::new()
    foreach ($u in @($History.unresolved)) { $unresolved.Add($u) }
    $matches=[Collections.Generic.List[object]]::new()
    $bySuite=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($suite in $Suites) {
        if ($bySuite.ContainsKey([string]$suite.id)) { throw 'history-identity-conflict: current suite' }
        $index=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach ($n in @($suite.nodes)) {
            $bare=Get-NightlyBareClassIdentity ([string]$n.ClassName)
            if (-not $index.ContainsKey($bare)) { $index.Add($bare,[Collections.Generic.List[object]]::new()) }
            $index[$bare].Add($n)
        }
        $chunkIndex=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        $allChunks=[Collections.Generic.List[object]]::new()
        foreach ($chunk in @($suite.chunks)) {
            if ($null -eq $chunk.classes -or @($chunk.classes).Count -eq 0) { $allChunks.Add($chunk); continue }
            foreach ($class in @($chunk.classes)) {
                if (-not $chunkIndex.ContainsKey([string]$class)) { $chunkIndex.Add([string]$class,[Collections.Generic.List[object]]::new()) }
                $chunkIndex[[string]$class].Add($chunk)
            }
        }
        $bySuite.Add([string]$suite.id,[pscustomobject]@{Suite=$suite;Nodes=$index;Chunks=$chunkIndex;AllChunks=$allChunks})
    }
    foreach ($member in @($History.members)) {
        $reason=''; $required=@(); $chunks=@()
        if (-not $bySuite.ContainsKey([string]$member.suite)) { $reason='current-suite-missing' }
        else {
            $entry=$bySuite[[string]$member.suite]
            $suite=$entry.Suite
            $nodes=@()
            if ($entry.Nodes.ContainsKey([string]$member.className)) { $nodes=@($entry.Nodes[[string]$member.className].ToArray()) }
            $required=@($nodes | Where-Object { -not (Test-NightlyDiscoveryExcluded -Node $_ -ProfileExclusions @($suite.exclusions) -ProfileAware) })
            if ($nodes.Count -eq 0) { $reason='current-class-missing' }
            elseif ($required.Count -eq 0) { $reason='current-class-excluded' }
            else {
                $needed=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($n in $required) { [void]$needed.Add([string]$n.Uid) }
                $covered=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                $candidateChunks=@($entry.AllChunks.ToArray())
                if ($entry.Chunks.ContainsKey([string]$member.className)) { $candidateChunks += $entry.Chunks[[string]$member.className].ToArray() }
                foreach ($chunk in $candidateChunks) {
                    $assigned=@($required)
                    if ($null -ne $chunk.nodes -and @($chunk.nodes).Count -gt 0) {
                        $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                        foreach ($n in @($chunk.nodes)) { [void]$ids.Add([string]$n.Uid) }
                        $assigned=@($required | Where-Object { $ids.Contains([string]$_.Uid) })
                    } elseif (@($chunk.classes).Count -gt 0 -and @($chunk.classes) -cnotcontains $member.className) { $assigned=@() }
                    if ($assigned.Count -gt 0) {
                        $chunks += [string]$chunk.id
                        foreach ($n in $assigned) { [void]$covered.Add([string]$n.Uid) }
                    }
                }
                if ($needed.Count -eq 0 -or -not $needed.SetEquals($covered)) { $reason='current-class-unplanned' }
            }
        }
        if ($reason) {
            $oldChunks=@($member.observations | ForEach-Object { $_.chunk } | Select-Object -Unique)
            $unresolved.Add([ordered]@{suite=[string]$member.suite;className=[string]$member.className;chunk=($oldChunks -join ',');reason=$reason;evidence=@()})
        } else {
            $matches.Add([pscustomobject]@{suite=[string]$member.suite;className=[string]$member.className;chunks=@($chunks);requiredUids=@(Get-NightlyHistoryOrdinalStrings @($required.Uid))})
        }
    }
    if ($QualificationMode -ceq 'PriorFailureSweep') {
        if ($unresolved.Count -gt 0) { throw ('prior-failure-sweep-unresolved: '+($unresolved.ToArray() | ConvertTo-Json -Depth 10 -Compress)) }
        if (-not $History.membershipComplete) { throw 'historical-membership-unavailable' }
        if ($matches.Count -eq 0) { throw 'empty-history' }
    } else {
        foreach ($u in $unresolved) { Write-Warning -WarningAction Continue ('UNRESOLVED PRIOR FAILURE '+($u | ConvertTo-Json -Depth 10 -Compress)) }
    }
    return [pscustomobject]@{members=@($matches.ToArray());unresolved=@($unresolved.ToArray())}
}
