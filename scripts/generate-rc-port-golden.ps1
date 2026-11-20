#requires -Version 7.0
# CARD-2010. Reproduce tests/Antiphon.Tests/Scripts/Fixtures/card-1836-s6c-rc-port-golden.json
# from the pre-port PowerShell libraries. ASCII-only.
# The libraries are read from -SourceCommit (default c6565bc95776, the golden source).
# -Prove replays every case and checks that System.Text.Json rewrite matches the file bytes.
# -AppendAdvisories rewrites the six CARD-2010 cases from those libraries.
param(
    [string]$SourceCommit = 'c6565bc95776c93a01109385d25420d19372afe8',
    [string]$Golden = '',
    [switch]$Prove,
    [switch]$AppendAdvisories)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Golden)) {
    $Golden = Join-Path $repo 'tests/Antiphon.Tests/Scripts/Fixtures/card-1836-s6c-rc-port-golden.json'
}
$Golden = [IO.Path]::GetFullPath($Golden)

function RcGolden-Text($Value) {
    if ($null -eq $Value) { return '' }
    if ($Value -is [string]) { return $Value }
    return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
}

function RcGolden-Scalar($Value) {
    if ($null -eq $Value) { return '' }
    if ($Value -is [bool]) { if ($Value) { return 'true' }; return 'false' }
    if ($Value -is [string]) { return $Value }
    return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
}

function RcGolden-Join($Value) {
    if ($null -eq $Value) { return '' }
    if ($Value -is [string]) { return $Value }
    $parts = [Collections.Generic.List[string]]::new()
    foreach ($item in @($Value)) {
        if ($null -ne $item) { $parts.Add((RcGolden-Scalar $item)) }
    }
    return ($parts -join '|')
}

function RcGolden-Read($Object, [string]$Name) {
    if ($null -eq $Object -or [string]::IsNullOrEmpty($Name)) { return $null }
    if ($Object -is [Collections.IDictionary]) {
        foreach ($key in @($Object.get_Keys())) {
            if ($key -is [string] -and [string]::Equals($key, $Name, [StringComparison]::Ordinal)) {
                return $Object[$key]
            }
        }
    }
    foreach ($property in @($Object.PSObject.Properties)) {
        if ([string]::Equals([string]$property.Name, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            return $property.Value
        }
    }
    return $null
}

function RcGolden-Launch($Value) {
    if ($null -eq $Value -or $Value -is [string]) { return '' }
    $parts = [Collections.Generic.List[string]]::new()
    foreach ($item in @($Value)) {
        if ($null -eq $item) { continue }
        $parts.Add((RcGolden-Text (RcGolden-Read $item 'key')) + ':' + (RcGolden-Text (RcGolden-Read $item 'band')) + ':' + (RcGolden-Text (RcGolden-Read $item 'disposition')))
    }
    return ($parts -join ',')
}

function RcGolden-Project($Op, $Result, [string]$ErrorText, [string]$Candidate, [string]$SecondError) {
    $map = [ordered]@{}
    $map['error'] = $ErrorText
    if (-not [string]::IsNullOrEmpty($SecondError)) { $map['secondError'] = $SecondError }
    if (-not [string]::IsNullOrEmpty($ErrorText)) { return $map }
    if ($Op -eq 'pilot') {
        $map['eligible'] = $(if ($Result -is [bool] -and $Result) { 'true' } else { 'false' })
        return $map
    }
    $keys = @()
    if ($Op -eq 'proposal') {
        $keys = @('bindingState','carriesInvalidated','cchunk','effectiveMode','expandedToFull','format','gateCredit','incrementalValidation','launched','n','pilotClaim','sample','seed','selected','shadowPilot','skips','stratumCount','trustGranted','trustRecordWritten')
    } elseif ($Op -eq 'trust') {
        $keys = @('carriesInvalidated','effectiveMode','expandedToFull','failureChunk','failureKind','failureOutcome','gateCredit','incrementalValidation','invalidatedCarries','observedPilots','reason','reasons','requiredPilots','shadowPilot','trustGranted','trustRecordWritten')
    } elseif ($Op -eq 'dirty') {
        $keys = @('activation','candidateEcho','effectiveMode','format','gateCredit','launch','omitted','shadow','trustTransition')
    }
    foreach ($key in $keys) {
        switch ($key) {
            'reasons' { $map[$key] = RcGolden-Join (RcGolden-Read $Result 'reasons') }
            'invalidatedCarries' { $map[$key] = RcGolden-Join (RcGolden-Read $Result 'invalidatedCarries') }
            'selected' { $map[$key] = RcGolden-Join (RcGolden-Read $Result 'selected') }
            'omitted' { $map[$key] = RcGolden-Join (RcGolden-Read $Result 'omitted') }
            'skips' { $map[$key] = RcGolden-Join (RcGolden-Read $Result 'expectedSkipOnly') }
            'launch' { $map[$key] = RcGolden-Launch (RcGolden-Read $Result 'launch') }
            'failureKind' { $map[$key] = RcGolden-Text (RcGolden-Read (RcGolden-Read $Result 'preservedSampleFailure') 'kind') }
            'failureChunk' { $map[$key] = RcGolden-Text (RcGolden-Read (RcGolden-Read $Result 'preservedSampleFailure') 'chunkId') }
            'failureOutcome' { $map[$key] = RcGolden-Text (RcGolden-Read (RcGolden-Read $Result 'preservedSampleFailure') 'outcome') }
            'candidateEcho' {
                $echo = RcGolden-Text (RcGolden-Read $Result 'candidateSha')
                if ([string]::Equals($echo, $Candidate, [StringComparison]::Ordinal)) { $map[$key] = 'present' } else { $map[$key] = $echo }
            }
            default { $map[$key] = RcGolden-Scalar (RcGolden-Read $Result $key) }
        }
    }
    return $map
}

function RcGolden-Shape($Node) {
    if ($null -eq $Node) { return $null }
    $kind = $Node['t'].GetValue[string]()
    switch ($kind) {
        'null' { return $null }
        'str' { return $Node['v'].GetValue[string]() }
        'bool' { return $Node['v'].GetValue[bool]() }
        'i64' { return $Node['v'].GetValue[long]() }
        'f64' { return $Node['v'].GetValue[double]() }
        'dt' { return [DateTimeOffset]::Parse($Node['v'].GetValue[string](), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind) }
        'map' {
            $cmp = ''
            if ($null -ne $Node['cmp']) { $cmp = $Node['cmp'].GetValue[string]() }
            if ($cmp -eq 'ignore') { $table = [hashtable]::new([StringComparer]::OrdinalIgnoreCase) }
            else { $table = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal) }
            foreach ($row in $Node['e']) { $table[$row['k'].GetValue[string]()] = RcGolden-Shape $row['v'] }
            return $table
        }
        'ps' {
            $custom = New-Object System.Management.Automation.PSObject
            foreach ($row in $Node['e']) {
                $note = New-Object System.Management.Automation.PSNoteProperty($row['k'].GetValue[string](), (RcGolden-Shape $row['v']))
                $custom.PSObject.Properties.Add($note)
            }
            return $custom
        }
        'pswrap' { return [System.Management.Automation.PSObject]::AsPSObject((RcGolden-Shape $Node['v'])) }
        'expando' {
            $expando = [System.Dynamic.ExpandoObject]::new()
            foreach ($row in $Node['e']) {
                $name = $row['k'].GetValue[string]()
                $expando.$name = RcGolden-Shape $row['v']
            }
            return $expando
        }
        'arr' {
            $items = [Collections.Generic.List[object]]::new()
            foreach ($item in $Node['items']) { $items.Add((RcGolden-Shape $item)) }
            return ,$items.ToArray()
        }
        'list' {
            $list = [Collections.Generic.List[object]]::new()
            foreach ($item in $Node['items']) { $list.Add((RcGolden-Shape $item)) }
            return $list
        }
        'arraylist' {
            $arrayList = [Collections.ArrayList]::new()
            foreach ($item in $Node['items']) { [void]$arrayList.Add((RcGolden-Shape $item)) }
            return $arrayList
        }
        'hashset' {
            $set = [Collections.Generic.HashSet[object]]::new()
            foreach ($item in $Node['items']) { [void]$set.Add((RcGolden-Shape $item)) }
            return $set
        }
        default { throw ('shape ' + $kind) }
    }
}

function RcGolden-Message($ErrorRecord) {
    $cursor = $ErrorRecord.Exception
    while ($null -ne $cursor.InnerException -and -not [string]::IsNullOrEmpty($cursor.InnerException.Message)) {
        $cursor = $cursor.InnerException
    }
    return [string]$cursor.Message
}

function RcGolden-Git([string]$Dir) {
    $gitArgs = @($args)
    $start = [Diagnostics.ProcessStartInfo]::new('git')
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.UseShellExecute = $false
    $start.ArgumentList.Add('-C')
    $start.ArgumentList.Add($Dir)
    foreach ($arg in $gitArgs) { $start.ArgumentList.Add([string]$arg) }
    $process = [Diagnostics.Process]::Start($start)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw ('git ' + ($gitArgs -join ' ') + ' exit ' + $process.ExitCode + ' ' + $stderr.Trim()) }
    $line = ''
    foreach ($part in ($stdout -split "`n")) { if (-not [string]::IsNullOrWhiteSpace($part)) { $line = $part.Trim(); break } }
    return $line
}

function RcGolden-BuildRepo($Steps) {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('rc-port-repo-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($dir)
    $roles = @{}
    RcGolden-Git $dir init -q -b main | Out-Null
    RcGolden-Git $dir config user.name Fixture | Out-Null
    RcGolden-Git $dir config user.email fixture@example.invalid | Out-Null
    RcGolden-Git $dir config commit.gpgsign false | Out-Null
    foreach ($step in @($Steps)) {
        $part = ([string]$step).Split('|')
        switch ($part[0]) {
            'commit' {
                [IO.File]::WriteAllText((Join-Path $dir 'note'), ($part[2] + "`n"))
                RcGolden-Git $dir add note | Out-Null
                RcGolden-Git $dir commit -qm $part[2] | Out-Null
                $roles[$part[1]] = RcGolden-Git $dir rev-parse HEAD
            }
            'branch' { RcGolden-Git $dir checkout -q -b $part[1] | Out-Null }
            'branch-from' { RcGolden-Git $dir checkout -q -b $part[1] $roles[$part[2]] | Out-Null }
            'trailer' {
                [IO.File]::WriteAllText((Join-Path $dir 'note'), ($part[2] + "`n"))
                RcGolden-Git $dir add note | Out-Null
                RcGolden-Git $dir commit -qm $part[2] -m ('(cherry picked from commit ' + $roles[$part[3]] + ')') | Out-Null
                $roles[$part[1]] = RcGolden-Git $dir rev-parse HEAD
            }
            'empty' {
                RcGolden-Git $dir commit --allow-empty -qm $part[2] | Out-Null
                $roles[$part[1]] = RcGolden-Git $dir rev-parse HEAD
            }
            default { throw ('plan ' + $part[0]) }
        }
    }
    return @{ Dir = $dir; Roles = $roles }
}

function RcGolden-Resolve([string]$Token, $Roles) {
    if ($Token.StartsWith('role:', [StringComparison]::Ordinal)) { return [string]$Roles[$Token.Substring(5)] }
    if ($Token.StartsWith('lit:', [StringComparison]::Ordinal)) { return $Token.Substring(4) }
    if ($Token -eq 'unknown') { return ('deadbeef' * 5) }
    return $Token
}

function RcGolden-Substitute($Value, $Roles, [string]$Field) {
    if ($null -eq $Value -or $Value -is [string]) { return $Value }
    $copy = [Collections.Generic.List[object]]::new()
    foreach ($item in @($Value)) {
        if ($null -eq $item) { $copy.Add($null); continue }
        $token = RcGolden-Text (RcGolden-Read $item $Field)
        if ([string]::IsNullOrEmpty($token)) { $copy.Add($item); continue }
        $resolved = RcGolden-Resolve $token $Roles
        if ($item -is [hashtable]) {
            $row = [hashtable]::new($item, [StringComparer]::OrdinalIgnoreCase)
            $row[$Field] = $resolved
            $copy.Add($row)
        } elseif ($item -is [Collections.Generic.Dictionary[string,object]]) {
            $row = [Collections.Generic.Dictionary[string,object]]::new($item, $item.Comparer)
            $row[$Field] = $resolved
            $copy.Add($row)
        } else { throw ('field row ' + $item.GetType().FullName) }
    }
    return ,$copy.ToArray()
}

function RcGolden-Invoke($Script) {
    try {
        $value = & $Script
        return @{ Value = $value; Error = '' }
    } catch {
        return @{ Value = $null; Error = (RcGolden-Message $_) }
    }
}

function RcGolden-Replay($Case) {
    $op = $Case['op'].GetValue[string]()
    $input = $Case['input']
    $second = ''
    $candidate = ''
    if ($op -eq 'proposal') {
        $chunks = RcGolden-Shape $input['chunks']
        $seed = $input['seed'].GetValue[string]()
        $ran = RcGolden-Invoke { New-RcShadowProposal -Chunks $chunks -Seed $seed }.GetNewClosure()
        $expectNode = $Case['expect']
        if ($null -ne $expectNode -and $null -ne $expectNode['secondError']) {
            $dir = Join-Path ([IO.Path]::GetTempPath()) ('rc-port-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($dir)
            $path = Join-Path $dir 'proposal.json'
            try {
                $first = RcGolden-Invoke { New-RcShadowProposal -Chunks $chunks -Seed $seed -ProposalPath $path }.GetNewClosure()
                if (-not [string]::IsNullOrEmpty($first.Error)) { throw $first.Error }
                $again = RcGolden-Invoke { New-RcShadowProposal -Chunks $chunks -Seed $seed -ProposalPath $path }.GetNewClosure()
                $second = $again.Error
            } finally {
                if ([IO.Directory]::Exists($dir)) { [IO.Directory]::Delete($dir, $true) }
            }
        }
        return RcGolden-Project $op $ran.Value $ran.Error '' $second
    }
    if ($op -eq 'pilot') {
        $pilot = RcGolden-Shape $input['pilot']
        $digest = $input['contractDigest'].GetValue[string]()
        $ran = RcGolden-Invoke { Test-RcShadowPilotEligible -Pilot $pilot -ContractDigest $digest }.GetNewClosure()
        return RcGolden-Project $op $ran.Value $ran.Error '' ''
    }
    if ($op -eq 'trust') {
        $current = RcGolden-Shape $input['currentContract']
        $baseline = RcGolden-Shape $input['baselineContract']
        $evidence = RcGolden-Shape $input['evidence']
        $anchor = RcGolden-Shape $input['fullAnchor']
        $utc = [DateTimeOffset]::Parse($input['utcNow'].GetValue[string](), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
        $pilots = RcGolden-Shape $input['pilots']
        $carries = RcGolden-Shape $input['carries']
        $failure = RcGolden-Shape $input['sampleFailure']
        $roster = $input['rosterChanged'].GetValue[bool]()
        $record = RcGolden-Shape $input['operatorRecord']
        $sample = $input['currentSample'].GetValue[string]()
        $digest = $input['contractDigest'].GetValue[string]()
        $ran = RcGolden-Invoke {
            Get-RcShadowTrustDecision -CurrentContract $current -BaselineContract $baseline -Evidence $evidence -FullAnchor $anchor -UtcNow $utc -Pilots $pilots -Carries $carries -SampleFailure $failure -RosterChanged $roster -OperatorTrustRecord $record -CurrentSample $sample -ContractDigest $digest -TrustRecordPath '/tmp/rc-port-trust-unused.json'
        }.GetNewClosure()
        return RcGolden-Project $op $ran.Value $ran.Error '' ''
    }
    if ($op -eq 'dirty') {
        $plan = @()
        if ($null -ne $input['repoPlan']) {
            foreach ($step in $input['repoPlan']) { $plan += $step.GetValue[string]() }
        }
        $obligations = RcGolden-Shape $input['obligations']
        $attempts = RcGolden-Shape $input['attempts']
        $fixes = RcGolden-Shape $input['fixes']
        $candidate = $input['candidate'].GetValue[string]()
        $repo = ''
        if ($plan.Count -gt 0) {
            $built = RcGolden-BuildRepo $plan
            $repo = $built.Dir
            try {
                $candidate = RcGolden-Resolve $candidate $built.Roles
                $attempts = RcGolden-Substitute $attempts $built.Roles 'sourceSha'
                $fixes = RcGolden-Substitute $fixes $built.Roles 'fixSha'
                $ran = RcGolden-Invoke {
                    Get-RcDirtyLaunchOrder -Obligations $obligations -Attempts $attempts -Fixes $fixes -RepositoryRoot $repo -CandidateSha $candidate
                }.GetNewClosure()
            } finally {
                if ([IO.Directory]::Exists($repo)) { [IO.Directory]::Delete($repo, $true) }
            }
        } else {
            $ran = RcGolden-Invoke {
                Get-RcDirtyLaunchOrder -Obligations $obligations -Attempts $attempts -Fixes $fixes -RepositoryRoot '' -CandidateSha $candidate
            }.GetNewClosure()
        }
        return RcGolden-Project $op $ran.Value $ran.Error $candidate ''
    }
    throw ('op ' + $op)
}

function RcGolden-ExpectNode($Projected, $Stored) {
    $node = [System.Text.Json.Nodes.JsonObject]::new()
    if ($null -eq $Stored) {
        foreach ($key in @($Projected.Keys | Sort-Object)) {
            $node[$key] = [System.Text.Json.Nodes.JsonValue]::Create([string]$Projected[$key])
        }
        return ,$node
    }
    foreach ($pair in $Stored) {
        $key = [string]$pair.Key
        if (-not $Projected.Contains($key)) { throw ('projected missing ' + $key) }
        $node[$key] = [System.Text.Json.Nodes.JsonValue]::Create([string]$Projected[$key])
    }
    return ,$node
}

function RcGolden-Compare($Id, $Projected, $Stored) {
    $mismatches = [Collections.Generic.List[string]]::new()
    foreach ($pair in $Stored) {
        $key = [string]$pair.Key
        $want = $pair.Value.GetValue[string]()
        $got = ''
        if ($Projected.Contains($key)) { $got = [string]$Projected[$key] }
        if (-not [string]::Equals($got, $want, [StringComparison]::Ordinal)) {
            $mismatches.Add($Id + ' ' + $key + ' expected [' + $want + '] actual [' + $got + ']')
        }
    }
    return $mismatches
}

function RcGolden-Put($Node, [string]$Name, $Value) {
    $nodeType = if ($null -eq $Node) { 'null' } else { $Node.GetType().FullName }
    $valueType = if ($null -eq $Value) { 'null' } else { $Value.GetType().FullName }
    if ($Node -isnot [System.Text.Json.Nodes.JsonObject] -or ($null -ne $Value -and $Value -isnot [System.Text.Json.Nodes.JsonNode])) {
        throw ('put ' + $Name + ' node=' + $nodeType + ' value=' + $valueType)
    }
    [void]$Node.Add($Name, [System.Text.Json.Nodes.JsonNode]$Value)
}
function RcGolden-Str([string]$Value) {
    $node = [System.Text.Json.Nodes.JsonObject]::new()
    $node['t'] = [System.Text.Json.Nodes.JsonValue]::Create('str')
    $node['v'] = [System.Text.Json.Nodes.JsonValue]::Create($Value)
    return ,$node
}
function RcGolden-Bool([bool]$Value) {
    $node = [System.Text.Json.Nodes.JsonObject]::new()
    $node['t'] = [System.Text.Json.Nodes.JsonValue]::Create('bool')
    $node['v'] = [System.Text.Json.Nodes.JsonValue]::Create($Value)
    return ,$node
}
function RcGolden-I64([long]$Value) {
    $node = [System.Text.Json.Nodes.JsonObject]::new()
    $node['t'] = [System.Text.Json.Nodes.JsonValue]::Create('i64')
    $node['v'] = [System.Text.Json.Nodes.JsonValue]::Create($Value)
    return ,$node
}
function RcGolden-F64([double]$Value) {
    $node = [System.Text.Json.Nodes.JsonObject]::new()
    $node['t'] = [System.Text.Json.Nodes.JsonValue]::Create('f64')
    $node['v'] = [System.Text.Json.Nodes.JsonValue]::Create($Value)
    return ,$node
}
function RcGolden-NullShape {
    $node = [System.Text.Json.Nodes.JsonObject]::new()
    $node['t'] = [System.Text.Json.Nodes.JsonValue]::Create('null')
    return ,$node
}
function RcGolden-Arr {
    $node = [System.Text.Json.Nodes.JsonObject]::new()
    $node['t'] = [System.Text.Json.Nodes.JsonValue]::Create('arr')
    $list = [System.Text.Json.Nodes.JsonArray]::new()
    foreach ($item in $args) { $list.Add($item) }
    RcGolden-Put $node 'items' $list
    return ,$node
}
function RcGolden-Map($Entries) {
    $node = [System.Text.Json.Nodes.JsonObject]::new()
    $node['t'] = [System.Text.Json.Nodes.JsonValue]::Create('map')
    $node['cmp'] = [System.Text.Json.Nodes.JsonValue]::Create('ignore')
    $list = [System.Text.Json.Nodes.JsonArray]::new()
    # A key named Keys shadows .Keys. Enumerate entries instead.
    foreach ($pair in @($Entries.GetEnumerator())) {
        $row = [System.Text.Json.Nodes.JsonObject]::new()
        $row['k'] = [System.Text.Json.Nodes.JsonValue]::Create([string]$pair.Key)
        RcGolden-Put $row 'v' $pair.Value
        $list.Add($row)
    }
    RcGolden-Put $node 'e' $list
    return ,$node
}
function RcGolden-Wrap($Inner) {
    $node = [System.Text.Json.Nodes.JsonObject]::new()
    $node['t'] = [System.Text.Json.Nodes.JsonValue]::Create('pswrap')
    RcGolden-Put $node 'v' $Inner
    return ,$node
}
function RcGolden-Contract([switch]$KeysEntry) {
    $entries = [ordered]@{}
    $entries['format'] = RcGolden-Str 'antiphon-rc-input-contract-v1'
    $entries['inputMapVersion'] = RcGolden-Str '1'
    $entries['sdkVersion'] = RcGolden-Str '10.0.204'
    $entries['runtimeVersion'] = RcGolden-Str '9.0.10'
    $entries['platform'] = RcGolden-Str 'linux'
    $entries['executionImplementation'] = RcGolden-Str 'compiled'
    foreach ($field in @('policyHash','catalogHash','selectorHash','skipResolverHash','discoveryHash','parserHash','coverageHash','launcherHash','custodyHash','buildHash','harnessHash','inputMapHash','sdkHash','runtimeHash')) {
        $entries[$field] = RcGolden-Str ('a' * 64)
    }
    if ($KeysEntry) { $entries['Keys'] = RcGolden-Str 'extra' }
    return RcGolden-Map $entries
}
function RcGolden-Case([string]$Id, [string]$Witness, [string]$Op, $CaseInput, $Expect) {
    $case = [System.Text.Json.Nodes.JsonObject]::new()
    $case['id'] = [System.Text.Json.Nodes.JsonValue]::Create($Id)
    $case['witness'] = [System.Text.Json.Nodes.JsonValue]::Create($Witness)
    $case['op'] = [System.Text.Json.Nodes.JsonValue]::Create($Op)
    if ($null -ne $CaseInput) { RcGolden-Put $case 'input' $CaseInput }
    if ($null -ne $Expect) { RcGolden-Put $case 'expect' $Expect }
    return ,$case
}

$extract = Join-Path ([IO.Path]::GetTempPath()) ('rc-port-golden-src-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($extract)
try {
    $tar = Join-Path $extract 'src.tar'
    & git -C $repo archive --format=tar -o $tar $SourceCommit -- scripts/lib/rc-shadow-sample.ps1 scripts/lib/rc-dirty-order.ps1 scripts/lib/rc-forced-full.ps1
    if ($LASTEXITCODE -ne 0) { throw 'git archive failed' }
    & tar -xf $tar -C $extract
    if ($LASTEXITCODE -ne 0) { throw 'tar extract failed' }
    . (Join-Path $extract 'scripts/lib/rc-shadow-sample.ps1')
    . (Join-Path $extract 'scripts/lib/rc-dirty-order.ps1')

    $options = [System.Text.Json.JsonSerializerOptions]::new()
    $options.WriteIndented = $true
    $root = [System.Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($Golden))
    $cases = $root['cases']

    if ($AppendAdvisories) {
        $replace = @{
            'pswrap-array-of-one' = $true
            'pswrap-chunk-id' = $true
            'pswrap-attempt' = $true
            'contract-keys-entry' = $true
            'non-integer-sequence' = $true
            'array-valued-key' = $true
        }
        for ($index = $cases.Count - 1; $index -ge 0; $index--) {
            $existing = $cases[$index]['id'].GetValue[string]()
            if ($replace.ContainsKey($existing)) { $cases.RemoveAt($index) }
        }
        $seen = @{}
        foreach ($case in $cases) { $seen[$case['id'].GetValue[string]()] = $true }
        $seed = 'ab' * 32
        $additions = [Collections.Generic.List[object]]::new()
        $chunk = RcGolden-Map ([ordered]@{ id = (RcGolden-Str 'c1'); suite = (RcGolden-Str 'alpha'); boundary = (RcGolden-Str 'north'); completelyCarryable = (RcGolden-Bool $true) })
        $proposalInput = [System.Text.Json.Nodes.JsonObject]::new()
        RcGolden-Put $proposalInput 'chunks' (RcGolden-Wrap (RcGolden-Arr $chunk))
        $proposalInput['seed'] = [System.Text.Json.Nodes.JsonValue]::Create($seed)
        $proposalInput['once'] = [System.Text.Json.Nodes.JsonValue]::Create($false)
        $additions.Add((RcGolden-Case 'pswrap-array-of-one' 'PsObjectWrappedArrayOfOne' 'proposal' $proposalInput $null))

        $row = RcGolden-Map ([ordered]@{ chunkId = (RcGolden-Wrap (RcGolden-Str 'c1')); outcome = (RcGolden-Str 'failed'); provenance = (RcGolden-Str 'verified') })
        $trustInput = [System.Text.Json.Nodes.JsonObject]::new()
        RcGolden-Put $trustInput 'evidence' (RcGolden-Map ([ordered]@{ sampled = (RcGolden-Arr (RcGolden-Str 'c1')); results = (RcGolden-Arr $row) }))
        RcGolden-Put $trustInput 'carries' (RcGolden-Arr (RcGolden-Str 'c1'))
        $trustInput['currentSample'] = [System.Text.Json.Nodes.JsonValue]::Create('roster')
        RcGolden-Put $trustInput 'pilots' (RcGolden-NullShape)
        RcGolden-Put $trustInput 'sampleFailure' (RcGolden-NullShape)
        $trustInput['rosterChanged'] = [System.Text.Json.Nodes.JsonValue]::Create($false)
        RcGolden-Put $trustInput 'currentContract' (RcGolden-NullShape)
        RcGolden-Put $trustInput 'baselineContract' (RcGolden-NullShape)
        RcGolden-Put $trustInput 'fullAnchor' (RcGolden-NullShape)
        $trustInput['utcNow'] = [System.Text.Json.Nodes.JsonValue]::Create('2026-11-13T12:00:00.0000000+00:00')
        $trustInput['contractDigest'] = [System.Text.Json.Nodes.JsonValue]::Create('')
        RcGolden-Put $trustInput 'operatorRecord' (RcGolden-NullShape)
        $additions.Add((RcGolden-Case 'pswrap-chunk-id' 'PsObjectWrappedChunkId' 'trust' $trustInput $null))

        $attempt = RcGolden-Map ([ordered]@{ sequence = (RcGolden-I64 1); sourceSha = (RcGolden-Str ('a' * 40)); results = (RcGolden-Arr (RcGolden-Map ([ordered]@{ key = (RcGolden-Str 'carry'); outcome = (RcGolden-Str 'failed') }))) })
        $dirtyInput = [System.Text.Json.Nodes.JsonObject]::new()
        RcGolden-Put $dirtyInput 'obligations' (RcGolden-Arr (RcGolden-Map ([ordered]@{ key = (RcGolden-Str 'carry'); disposition = (RcGolden-Str 'Carry') })))
        RcGolden-Put $dirtyInput 'attempts' (RcGolden-Arr (RcGolden-Wrap $attempt))
        RcGolden-Put $dirtyInput 'fixes' (RcGolden-Arr)
        $dirtyInput['candidate'] = [System.Text.Json.Nodes.JsonValue]::Create('candidate-not-in-history')
        $dirtyInput['candidateToken'] = [System.Text.Json.Nodes.JsonValue]::Create('')
        RcGolden-Put $dirtyInput 'repoPlan' ([System.Text.Json.Nodes.JsonArray]::new())
        $additions.Add((RcGolden-Case 'pswrap-attempt' 'PsObjectWrappedAttempt' 'dirty' $dirtyInput $null))

        $keysInput = [System.Text.Json.Nodes.JsonObject]::new()
        RcGolden-Put $keysInput 'evidence' (RcGolden-NullShape)
        RcGolden-Put $keysInput 'carries' (RcGolden-Arr)
        $keysInput['currentSample'] = [System.Text.Json.Nodes.JsonValue]::Create('')
        RcGolden-Put $keysInput 'pilots' (RcGolden-NullShape)
        RcGolden-Put $keysInput 'sampleFailure' (RcGolden-NullShape)
        $keysInput['rosterChanged'] = [System.Text.Json.Nodes.JsonValue]::Create($false)
        RcGolden-Put $keysInput 'currentContract' (RcGolden-Contract -KeysEntry)
        RcGolden-Put $keysInput 'baselineContract' (RcGolden-Contract)
        RcGolden-Put $keysInput 'fullAnchor' (RcGolden-NullShape)
        $keysInput['utcNow'] = [System.Text.Json.Nodes.JsonValue]::Create('2026-11-13T12:00:00.0000000+00:00')
        $keysInput['contractDigest'] = [System.Text.Json.Nodes.JsonValue]::Create('')
        RcGolden-Put $keysInput 'operatorRecord' (RcGolden-NullShape)
        $additions.Add((RcGolden-Case 'contract-keys-entry' 'ContractDictionaryKeysEntry' 'trust' $keysInput $null))

        $seqInput = [System.Text.Json.Nodes.JsonObject]::new()
        RcGolden-Put $seqInput 'obligations' (RcGolden-Arr (RcGolden-Map ([ordered]@{ key = (RcGolden-Str 'dirty'); disposition = (RcGolden-Str 'Dirty') })))
        RcGolden-Put $seqInput 'attempts' (RcGolden-Arr (RcGolden-Map ([ordered]@{ sequence = (RcGolden-F64 1.9); sourceSha = (RcGolden-Str 's'); results = (RcGolden-Arr (RcGolden-Map ([ordered]@{ key = (RcGolden-Str 'dirty'); outcome = (RcGolden-Str 'passed') }))) })))
        RcGolden-Put $seqInput 'fixes' (RcGolden-Arr)
        $seqInput['candidate'] = [System.Text.Json.Nodes.JsonValue]::Create('candidate-not-in-history')
        $seqInput['candidateToken'] = [System.Text.Json.Nodes.JsonValue]::Create('')
        RcGolden-Put $seqInput 'repoPlan' ([System.Text.Json.Nodes.JsonArray]::new())
        $additions.Add((RcGolden-Case 'non-integer-sequence' 'NonIntegerSequence' 'dirty' $seqInput $null))

        $arrayKey = RcGolden-Map ([ordered]@{ key = (RcGolden-Arr (RcGolden-Str 'a') (RcGolden-Str 'b')); disposition = (RcGolden-Str 'Dirty') })
        $keyInput = [System.Text.Json.Nodes.JsonObject]::new()
        RcGolden-Put $keyInput 'obligations' (RcGolden-Arr $arrayKey)
        RcGolden-Put $keyInput 'attempts' (RcGolden-Arr)
        RcGolden-Put $keyInput 'fixes' (RcGolden-Arr)
        $keyInput['candidate'] = [System.Text.Json.Nodes.JsonValue]::Create('candidate-not-in-history')
        $keyInput['candidateToken'] = [System.Text.Json.Nodes.JsonValue]::Create('')
        RcGolden-Put $keyInput 'repoPlan' ([System.Text.Json.Nodes.JsonArray]::new())
        $additions.Add((RcGolden-Case 'array-valued-key' 'ArrayValuedKey' 'dirty' $keyInput $null))

        $added = 0
        foreach ($case in $additions) {
            $id = $case['id'].GetValue[string]()
            if ($seen.ContainsKey($id)) { continue }
            $projected = RcGolden-Replay $case
            RcGolden-Put $case 'expect' (RcGolden-ExpectNode $projected $null)
            $cases.Add($case)
            $added++
            Write-Output ('ADDED ' + $id)
        }
        if ($added -gt 0) {
            $text = $root.ToJsonString($options) + "`n"
            [IO.File]::WriteAllText($Golden, $text)
            Write-Output ('WROTE ' + $added)
        } else {
            Write-Output 'ADVISORIES-PRESENT'
        }
    }

    if ($Prove) {
        $mismatches = [Collections.Generic.List[string]]::new()
        $count = 0
        foreach ($case in $cases) {
            $count++
            $id = $case['id'].GetValue[string]()
            $projected = RcGolden-Replay $case
            foreach ($row in (RcGolden-Compare $id $projected $case['expect'])) { $mismatches.Add($row) }
        }
        $written = $root.ToJsonString($options) + "`n"
        $raw = [IO.File]::ReadAllText($Golden)
        $bytes = [string]::Equals($raw, $written, [StringComparison]::Ordinal)
        Write-Output ('CASES ' + $count + ' MISMATCHES ' + $mismatches.Count + ' BYTES ' + $(if ($bytes) { 'match' } else { 'differ' }))
        foreach ($row in ($mismatches | Select-Object -First 20)) { Write-Output $row }
        if ($mismatches.Count -gt 0 -or -not $bytes) { exit 1 }
        Write-Output 'PROVED'
    }
} finally {
    if ([IO.Directory]::Exists($extract)) { [IO.Directory]::Delete($extract, $true) }
}
