# CARD-1526: bounded declaration lint; never executes evidence commands or certifies coverage.
param([string]$BaseRef, [string]$HeadRef, [string]$EvidencePath, [string]$Plan,
    [string]$Repository = '.')
$ErrorActionPreference = 'Stop'
$script:errors = [Collections.Generic.List[string]]::new()
function Fail([string]$Message) { $script:errors.Add($Message); Write-Host "OBLIGATION $Message" }
function Need($Record, [string[]]$Fields, [string]$Context) {
    foreach ($field in $Fields) {
        if ($Record -isnot [Collections.IDictionary] -or $Record[$field] -isnot [string] -or [string]::IsNullOrWhiteSpace($Record[$field])) {
            Fail "$Context missing $field"
        }
    }
}
function Git([string[]]$Arguments) {
    $p = [Diagnostics.ProcessStartInfo]::new('git')
    $p.UseShellExecute=$false; $p.RedirectStandardOutput=$true; $p.RedirectStandardError=$true
    foreach ($arg in (@('-C',$Repository) + $Arguments)) { $p.ArgumentList.Add($arg) }
    $child=[Diagnostics.Process]::Start($p)
    try {
        $stdout=$child.StandardOutput.ReadToEndAsync(); $stderr=$child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(15000)) { $child.Kill($true); $child.WaitForExit(); throw 'git timeout' }
        $output=$stdout.GetAwaiter().GetResult(); $errorText=$stderr.GetAwaiter().GetResult()
        if ($child.ExitCode -ne 0) { throw "git failed: $errorText" }
        return $output
    } finally { $child.Dispose() }
}
function Valid-Path([string]$Path) {
    return $Path -and $Path -notmatch '(^/|\\|(^|/)\.\.?(/|$)|[\x00-\x1f])' -and $Path -notmatch '^[A-Za-z]:'
}
$script:blobs=@{}
function Blob([string]$Sha, [string]$Path) {
    if (-not (Valid-Path $Path)) { Fail "invalid path: $Path"; return $null }
    $key="$Sha`:$Path"
    if ($script:blobs.ContainsKey($key)) { return $script:blobs[$key] }
    $entry=Git @('--literal-pathspecs','ls-tree','-z',$Sha,'--',$Path)
    if (-not $entry -or $entry -notmatch '^100(644|755) blob ') { Fail "path missing or not regular: $Path at $Sha"; return $null }
    $value=Git @('cat-file','blob',$key)
    $script:blobs[$key]=$value
    return $value
}
function Test-Lexemes([string]$Text, [string]$Path) {
    # Preserve offsets and quoted arguments in Code; Tokens also masks literals.
    # A partial/ambiguous lexical view must never be used to accept a witness.
    $powershell = [IO.Path]::GetExtension($Path) -in @('.ps1','.psm1','.psd1')
    $code = $Text.ToCharArray(); $tokens = $Text.ToCharArray()
    $i = 0
    while ($i -lt $Text.Length) {
        $start = $i; $literal = $false
        $ch = $Text[$i]
        $pair = if ($i + 1 -lt $Text.Length) { $Text.Substring($i, 2) } else { '' }
        if ($pair -eq '/*' -or $pair -eq '<#') {
            $kind = $pair; $depth = 1; $i += 2
            while ($i -lt $Text.Length -and $depth -gt 0) {
                $pair = if ($i + 1 -lt $Text.Length) { $Text.Substring($i, 2) } else { '' }
                if ($kind -eq '<#' -and $pair -eq '<#') { $depth++; $i += 2 }
                elseif (($kind -eq '<#' -and $pair -eq '#>') -or ($kind -eq '/*' -and $pair -eq '*/')) { $depth--; $i += 2 }
                else { $i++ }
            }
            if ($depth -ne 0) { throw "unterminated $kind block comment in test source" }
        } elseif ($pair -eq '//' -or $ch -eq '#') {
            while ($i -lt $Text.Length -and $Text[$i] -ne "`r" -and $Text[$i] -ne "`n") { $i++ }
        } else {
            if ((-not $powershell -and $pair -in @('$"','$@','@$')) -or
                ($powershell -and $pair -in @('@"',"@'"))) {
                throw 'ambiguous string literal: interpolated C# and PowerShell here-strings are unsupported'
            }
            $verbatim = -not $powershell -and $pair -eq '@"'
            if ($verbatim) { $i++; $ch = $Text[$i] }
            if ($ch -ne '"' -and $ch -ne "'") { $i++; continue }
            if ($ch -eq '"' -and $i + 2 -lt $Text.Length -and $Text.Substring($i, 3) -eq '"""') {
                throw 'ambiguous string literal: C# raw strings are unsupported'
            }
            $literal = $true; $quote = $ch; $closed = $false; $i++
            while ($i -lt $Text.Length) {
                $ch = $Text[$i]
                if ($ch -eq $quote) {
                    if (($verbatim -or ($powershell -and $quote -eq "'")) -and
                        $i + 1 -lt $Text.Length -and $Text[$i + 1] -eq $quote) { $i += 2; continue }
                    $i++; $closed = $true; break
                }
                if ((-not $powershell -and -not $verbatim -and $ch -eq '\') -or
                    ($powershell -and $quote -eq '"' -and $ch -eq '`')) {
                    if ($i + 1 -ge $Text.Length) { break }
                    $i += 2; continue
                }
                if ($powershell -and $quote -eq '"' -and $ch -eq '$' -and
                    $i + 1 -lt $Text.Length -and $Text[$i + 1] -eq '(') {
                    throw 'ambiguous string literal: PowerShell subexpressions are unsupported'
                }
                if (-not $powershell -and -not $verbatim -and $ch -in @("`r","`n")) { break }
                $i++
            }
            if (-not $closed) { throw 'unterminated string literal in test source' }
        }
        for ($j = $start; $j -lt $i; $j++) {
            if ($Text[$j] -ne "`r" -and $Text[$j] -ne "`n") {
                $tokens[$j] = ' '
                if (-not $literal) { $code[$j] = ' ' }
            }
        }
    }
    return @{ Code=(-join $code); Tokens=(-join $tokens) }
}
function Code-Fragment($Lexemes, [string]$Fragment) {
    if ([string]::IsNullOrWhiteSpace($Fragment)) { return $false }
    $pattern = '(?<![\w])' + [regex]::Escape($Fragment.Trim()).Replace('\ ', '\s+') + '(?![\w])'
    foreach ($match in [regex]::Matches($Lexemes.Code, $pattern)) {
        # A fragment may contain string arguments, but must start outside a string.
        if (-not [char]::IsWhiteSpace($Lexemes.Tokens[$match.Index])) { return $true }
    }
    return $false
}
function Test-Declaration($Lexemes, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    $exact = [regex]::Escape($Name)
    # Bounded C# test-method and PowerShell function / named-case declarations.
    $method = '(?m)^\s*(?:\[[^\]\r\n]+\]\s*)+\s*(?:(?:public|private|protected|internal|static|async|virtual|override)\s+)*(?:[\w.<>,?\[\]]+\s+)'+$exact+'\s*\([^;{}]*\)\s*(?:\{|=>)'
    $function = '(?m)^\s*function\s+'+$exact+'\s*(?:\([^;{}]*\)\s*)?\{'
    foreach ($match in [regex]::Matches($Lexemes.Tokens, $method)) {
        if ($match.Value -match '\[(?:[\w]+\.)*(?:Test|Fact|Theory|TestMethod|DataTestMethod)(?:Attribute)?\s*(?:\(|\])') { return $true }
    }
    if ([regex]::IsMatch($Lexemes.Tokens, $function)) { return $true }
    $case = '(?m)^\s*(?<keyword>It|Run-Case|Test-Case)\s+(?:-Name\s+)?(?<quote>[''"])'+$exact+'\k<quote>(?=\s|$)'
    foreach ($match in [regex]::Matches($Lexemes.Code, $case)) {
        if (-not [char]::IsWhiteSpace($Lexemes.Tokens[$match.Groups['keyword'].Index])) { return $true }
    }
    return $false
}
function Native($Record, [string]$Context, [string]$Os, [bool]$Required) {
    if ($Record -isnot [Collections.IDictionary]) { Fail "$Context missing native obligation"; return }
    if ($Record.status -eq 'not-required') {
        Need $Record @('reason') $Context
        if ($Required) { Fail "$Context native obligation required" }
        return
    }
    Need $Record @('status','targetOS','sourceSha','command') $Context
    if ($Record.targetOS -ne $Os -or $Record.sourceSha -cne $script:head) { Fail "$Context native OS/source mismatch" }
    switch ($Record.status) {
        'pending' { Need $Record @('reason') $Context; Write-Host "NATIVE pending $Context" }
        'recorded' {
            Need $Record @('receiptReference','executionMode') $Context
            if ($Record.executionMode -ne 'native') { Fail "$Context native receipt requires native execution" }
        }
        default { Fail "$Context invalid native status" }
    }
}
try {
    foreach ($ref in @($BaseRef,$HeadRef)) {
        if ($ref -notmatch '^(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})$') { throw 'full commit SHAs required' }
    }
    try {
        $base=(Git @('rev-parse','--verify',"$BaseRef^{commit}")).Trim()
        $script:head=(Git @('rev-parse','--verify',"$HeadRef^{commit}")).Trim()
    } catch { throw "unresolved commit: $($_.Exception.Message)" }
    Write-Host "OS-WITNESS base=$base head=$script:head"
    $e=Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json -AsHashtable
    if ($e -isnot [Collections.IDictionary] -or $e.version -ne 1) { throw 'invalid version/object' }
    if ($e.baseSha -cne $base -or $e.headSha -cne $script:head) { throw 'SHA mismatch' }
    foreach ($field in @('inspectedPaths','witnesses')) {
        if ($e[$field] -isnot [array]) { throw "invalid array: $field" }
        foreach ($record in $e[$field]) { if ($record -isnot [Collections.IDictionary]) { throw "invalid record: $field" } }
    }
    if ($e.restartImpact -isnot [Collections.IDictionary]) { throw 'invalid restartImpact' }
    $planText=Blob $script:head $Plan
    if ($null -eq $planText) { throw 'invalid plan path' }
    $section=[regex]::Match($planText,'(?ms)^### Checkpoints\s*\r?\n(?<body>.*?)(?=^#{1,3} |\z)')
    $checkpointIds=@([regex]::Matches($section.Groups['body'].Value,'(?m)^\|\s*(CP-\d+)\s*\|') | ForEach-Object { $_.Groups[1].Value })
    if ($checkpointIds.Count -eq 0) { throw 'plan has no checkpoint rows' }
    # -z retains whitespace and both rename identities; no shell parsing of paths.
    $tokens=(Git @('diff','--name-status','-z','--find-renames',$base,$script:head,'--')).Split([char]0)
    $changed=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    for ($i=0; $i -lt $tokens.Length -and $tokens[$i];) {
        $status=$tokens[$i++]; $path=$tokens[$i++]
        if ($status -match '^[RC]') { $changed[$path]='removed'; $changed[$tokens[$i++]]='head' }
        else { $changed[$path]= $(if ($status -eq 'D') { 'removed' } else { 'head' }) }
    }
    $docPattern='(?i)\.(md|rst|txt)$'
    $sourcePattern='(?i)\.(cs|csx|ps1|psm1|psd1|sh|bash|zsh|js|jsx|mjs|cjs|ts|tsx)$'
    $inventory=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($entry in $e.inspectedPaths) {
        Need $entry @('path','disposition','reason') 'inventory'
        $path=[string]$entry.path
        if (-not (Valid-Path $path)) { Fail "invalid inventory path: $path"; continue }
        if ($inventory.ContainsKey($path)) { Fail "duplicate inventory: $path"; continue }
        $inventory[$path]=$entry
        if ($entry.disposition -notin @('os-gated','portable','test-only','removed')) { Fail "invalid disposition: $path" }
        $removed=$changed.ContainsKey($path) -and $changed[$path] -eq 'removed'
        if (($entry.disposition -eq 'removed') -ne $removed) { Fail "removed disposition mismatch: $path" }
        $null=Blob $(if ($removed) {$base} else {$script:head}) $path
        if ($entry.helpers -isnot [array] -or $entry.candidates -isnot [array]) { throw "invalid helpers/candidates arrays: $path" }
    }
    foreach ($path in $changed.Keys) {
        Write-Host "CHANGED $($changed[$path]) $path"
        if ($path -notmatch $docPattern -and -not $inventory.ContainsKey($path)) { Fail "inventory missing: $path" }
        if ($path -notmatch $docPattern -and $path -notmatch $sourcePattern) { Write-Host "CLASSIFY unrecognized $path" }
    }
    $witnesses=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($w in $e.witnesses) {
        Need $w @('id','sourcePath','branchAnchor','targetOS','witnessKind','productionType','factory','testPath','testCase','setup','outcome','executionMode','checkpointId') 'witness'
        if (-not $w.id) { continue }
        if ($witnesses.ContainsKey($w.id)) { Fail "duplicate witness: $($w.id)"; continue }
        $witnesses[$w.id]=$w
        if ($w.witnessKind -ne 'real-type') { Fail "$($w.id) real-type required" }
        if ($w.targetOS -notin @('Windows','Linux','macOS','FreeBSD','Android','iOS','Browser','Other')) { Fail "$($w.id) invalid targetOS" }
        if (-not $inventory.ContainsKey([string]$w.sourcePath) -or $inventory[$w.sourcePath].disposition -ne 'os-gated') { Fail "$($w.id) witness requires os-gated inventory" }
        $source=Blob $script:head $w.sourcePath
        $test=Test-Lexemes (Blob $script:head $w.testPath) $w.testPath
        if (-not (Test-Declaration $test $w.testCase)) { Fail "$($w.id) testCase declaration missing: $($w.testCase)" }
        foreach ($field in @('productionType','factory')) {
            if (-not (Code-Fragment $test $w[$field])) { Fail "$($w.id) $field missing from test code" }
        }
        if ($source -and $w.branchAnchor -and -not $source.Contains($w.branchAnchor)) { Fail "$($w.id) branch anchor missing" }
        if ($w.checkpointId -notin $checkpointIds) { Fail "$($w.id) checkpoint missing" }
        if ($w.executionMode -notin @('portable-forced','native')) { Fail "$($w.id) invalid execution mode" }
        Native $w.nativeObligation $w.id $w.targetOS ($w.executionMode -eq 'native')
        if ($w.input -isnot [Collections.IDictionary]) { Fail "$($w.id) missing input"; continue }
        if ($w.input.kind -eq 'optional-config') {
            Need $w.input @('key') "$($w.id) input"
            if ($w.input.cases -isnot [array]) { throw 'invalid input cases' }
            $states=@($w.input.cases | ForEach-Object { $_.state })
            if ('absent' -notin $states -or 'empty' -notin $states -or 'value' -notin $states) { Fail "$($w.id) input needs absent, empty and value" }
            $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($case in $w.input.cases) {
                if ($case -isnot [Collections.IDictionary]) { throw 'invalid input case' }
                Need $case @('state','setupAssertion') "$($w.id) input"
                if (-not (Code-Fragment $test $case.setupAssertion)) { Fail "$($w.id) $($case.state) setupAssertion missing from test code" }
                $caseValue = if ($case.ContainsKey('value')) { $case['value'] } else { $null }
                if (-not $seen.Add("$($case.state):$caseValue")) { Fail "$($w.id) duplicate input case" }
                if ($case.keyPresent -isnot [bool]) { Fail "$($w.id) keyPresent must be boolean" }
                switch ($case.state) {
                    'absent' { if ($case.keyPresent -ne $false -or $case.ContainsKey('value')) { Fail "$($w.id) absent requires removal and no value" } }
                    'empty' { if ($case.keyPresent -ne $true -or $caseValue -isnot [string] -or $caseValue -cne '') { Fail "$($w.id) empty requires present empty value" } }
                    'value' { if ($case.keyPresent -ne $true -or $caseValue -isnot [string] -or $caseValue.Length -eq 0) { Fail "$($w.id) value requires present nonempty value" } }
                    default { Fail "$($w.id) invalid input state" }
                }
            }
        } elseif ($w.input.kind -eq 'other') { Need $w.input @('description','setupAssertion') "$($w.id) input" }
        else { Fail "$($w.id) invalid input kind" }
    }
    # Bounded lexical candidates, including unchanged guards in changed files and named helpers.
    $osPattern='OperatingSystem\.Is\w+\s*\(|RuntimeInformation\.IsOSPlatform\s*\(|Environment\.OSVersion\.Platform|\$(?:IsWindows|IsLinux|IsMacOS)\b|\$env:OS\b|#\s*(?:if|elif|ifdef|ifndef).*\b(?:WINDOWS|WIN32|_WIN32|LINUX|__linux__|OSX|MACOS|__APPLE__)\b|\buname\b|\$\{?OSTYPE\b|process\.platform\b|\bos\.platform\s*\('
    foreach ($path in $inventory.Keys) {
        $entry=$inventory[$path]
        foreach ($helper in $entry.helpers) { if (-not $inventory.ContainsKey([string]$helper)) { Fail "helper not inspected: $helper" } }
        if ($entry.disposition -eq 'os-gated' -and @($e.witnesses | Where-Object sourcePath -CEQ $path).Count -eq 0) { Fail "missing witness: $path" }
        $actual=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($revision in @('base','head')) {
            $sha=$(if ($revision -eq 'base') {$base} else {$script:head})
            if (-not (Git @('--literal-pathspecs','ls-tree','-z',$sha,'--',$path))) { continue }
            if ($path -notmatch $sourcePattern) { continue }
            $lines=(Blob $sha $path) -split "`n"
            for ($line=0; $line -lt $lines.Length; $line++) {
                if ($lines[$line] -match $osPattern) {
                    $key="$revision`:$($line+1)"; $null=$actual.Add($key)
                    Write-Host "CANDIDATE $revision $path`:$($line+1)"
                }
            }
        }
        $declared=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($candidate in $entry.candidates) {
            if ($candidate -isnot [Collections.IDictionary]) { throw 'invalid candidate record' }
            $candidateWitnessId = if ($candidate.ContainsKey('witnessId')) { $candidate['witnessId'] } else { $null }
            $key="$($candidate.revision):$($candidate.line)"
            if (-not $declared.Add($key)) { Fail "duplicate candidate: $path $key" }
            if (-not $actual.Contains($key)) { Fail "candidate mismatch: $path $key" }
            if ($candidate.disposition -eq 'non-behavior') {
                Need $candidate @('reason') "candidate $path $key"
                if ($candidateWitnessId) { Fail "contradictory candidate: $path $key" }
            } elseif ($candidate.disposition -eq 'witness') {
                if (-not $candidateWitnessId -or -not $witnesses.ContainsKey([string]$candidateWitnessId)) { Fail "missing witness for candidate: $path $key" }
                elseif ($witnesses[$candidateWitnessId].sourcePath -cne $path) { Fail "candidate witness path mismatch: $path $key" }
            } else { Fail "candidate disposition invalid: $path $key" }
        }
        foreach ($key in $actual) { if (-not $declared.Contains($key)) { Fail "candidate unclassified: $path $key" } }
    }
    $restart=$e.restartImpact
    if ($restart.required -isnot [bool] -or $restart.paths -isnot [array] -or $restart.additionalPaths -isnot [array]) { throw 'invalid restartImpact fields' }
    Need $restart @('reason') 'restartImpact'
    $startup=@($changed.Keys | Where-Object { $_ -match '^(dev-aspire\.ps1|scripts/(restart-apphost\.ps1|build-slot\.ps1|apphost-common\.ps1|lib/.*)|Antiphon\.AppHost/.*)$' -and $_ -notmatch $docPattern })
    if (@(Compare-Object @($startup | Sort-Object -Unique) @($restart.paths | Sort-Object -Unique)).Count -gt 0 -or @($restart.paths | Select-Object -Unique).Count -ne $restart.paths.Count) { Fail 'restart path census mismatch' }
    foreach ($path in $restart.additionalPaths) {
        if (-not $inventory.ContainsKey([string]$path)) { Fail "restart dependency not inspected: $path" }
    }
    $impacted=@(@($startup) + @($restart.additionalPaths) | Where-Object { -not $inventory.ContainsKey($_) -or $inventory[$_].disposition -ne 'test-only' })
    if ($restart.required -ne ($impacted.Count -gt 0)) { Fail 'restart required mismatch' }
    if ($restart.required) { Native $restart.nativeObligation 'restartImpact' 'Windows' $true }
    elseif ($restart.ContainsKey('nativeObligation')) { Fail 'contradictory restart native obligation' }
    if ($script:errors.Count) { Write-Host "OS-WITNESS obligations=$($script:errors.Count)"; exit 1 }
    Write-Host 'OS-WITNESS complete syntax/inventory; execution and runtime identity require Review'
    exit 0
} catch { Write-Host "OS-WITNESS invalid input: $($_.Exception.Message)"; exit 2 }
