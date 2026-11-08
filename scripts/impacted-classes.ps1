# CARD-1782. Read-only selector from tests/impacted-classes.json.
# CARD-1783. -MergeSubset always emits the fixed high-radius classes. A -Range
# that touches server/Migrations/** also emits the migrations downgrade selector.
# No cache: every run reads the table, git, and the tests tree. Maximum staleness is zero.
# A malformed table exits 1 and names the row. -Range cannot combine with -Path or -FileList.
# -MergeSubset cannot combine with -Audit.
# PowerShell 5.1 compatible: no ternary, no ??, no ?. ASCII only.
[CmdletBinding()]
param(
    [string]$Range,
    [string]$FileList,
    [string[]]$Path,
    [string]$Repo,
    [string]$Table,
    [switch]$Json,
    [switch]$Audit,
    [switch]$MergeSubset
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Write-Err([string]$Message) {
    [Console]::Error.WriteLine($Message)
}

function Refuse-Table([string]$Where, [string]$Problem) {
    $message = 'impacted-classes table refused: ' + $Where + ': ' + $Problem
    Write-Err $message
    if ($script:Json) {
        Exit-Json ([pscustomobject]@{
            ok = $false
            emptyGlobs = @()
            missingClasses = @()
            kinds = @()
            globHits = @()
            derivedFiles = @()
            error = $message
        }) 1
    }
    exit 1
}

function Exit-Json($Payload, [int]$Code) {
    if ($Json) {
        $json = $Payload | ConvertTo-Json -Depth 8
        [Console]::Out.WriteLine($json)
    }
    exit $Code
}

if ([string]::IsNullOrWhiteSpace($Repo)) {
    $Repo = Split-Path -Parent $PSScriptRoot
}
$Repo = [IO.Path]::GetFullPath($Repo)
if ([string]::IsNullOrWhiteSpace($Table)) {
    $Table = Join-Path $Repo 'tests/impacted-classes.json'
}
elseif (-not [IO.Path]::IsPathRooted($Table)) {
    $Table = Join-Path $Repo $Table
}

# Switch clash is usage, not a table error, and it is decided before the table is read.
$rangeSet = -not [string]::IsNullOrWhiteSpace($Range)
$pathSet = $false
if ($null -ne $Path) {
    foreach ($pathItem in @($Path)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$pathItem)) { $pathSet = $true }
    }
}
$listSet = -not [string]::IsNullOrWhiteSpace($FileList)
if ($rangeSet -and ($pathSet -or $listSet)) {
    $switchMessage = 'impacted-classes refused: -Range cannot be combined with -Path or -FileList'
    Write-Err $switchMessage
    if ($Json) {
        Exit-Json ([pscustomobject]@{
            ok = $false
            emptyGlobs = @()
            missingClasses = @()
            kinds = @()
            globHits = @()
            derivedFiles = @()
            error = $switchMessage
        }) 2
    }
    exit 2
}
if ($MergeSubset -and $Audit) {
    $switchMessage = 'impacted-classes refused: -MergeSubset cannot be combined with -Audit'
    Write-Err $switchMessage
    if ($Json) {
        Exit-Json ([pscustomobject]@{
            ok = $false
            mode = 'merge-subset'
            emptyGlobs = @()
            missingClasses = @()
            kinds = @()
            globHits = @()
            derivedFiles = @()
            error = $switchMessage
        }) 2
    }
    exit 2
}

function As-List($Value) {
    $list = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Value) { return $list }
    foreach ($item in @($Value)) {
        if ($null -ne $item) {
            $text = [string]$item
            if (-not [string]::IsNullOrWhiteSpace($text)) { [void]$list.Add($text) }
        }
    }
    # The comma keeps a one-element list from unwrapping into a bare string.
    return ,$list
}

function Fill-Sorted($Items, [System.Collections.Generic.List[string]]$Into) {
    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    if ($null -ne $Items) {
        foreach ($item in @($Items)) {
            if ($null -eq $item) { continue }
            $text = [string]$item
            if (-not [string]::IsNullOrWhiteSpace($text)) { [void]$set.Add($text) }
        }
    }
    foreach ($name in $set) { [void]$Into.Add($name) }
}

function New-StringList {
    $list = New-Object System.Collections.Generic.List[string]
    return ,$list
}

function Convert-GlobToRegex([string]$Glob) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('^')
    $i = 0
    while ($i -lt $Glob.Length) {
        if ((($i + 2) -lt $Glob.Length) -and ($Glob.Substring($i, 3) -eq '**/')) {
            [void]$sb.Append('(?:.*/)?')
            $i += 3
            continue
        }
        if ((($i + 1) -lt $Glob.Length) -and ($Glob.Substring($i, 2) -eq '**')) {
            [void]$sb.Append('.*')
            $i += 2
            continue
        }
        $c = $Glob[$i]
        if ($c -eq '*') {
            [void]$sb.Append('[^/]*')
        }
        elseif ('\.+()[]{}|^$?'.Contains($c)) {
            [void]$sb.Append('\')
            [void]$sb.Append($c)
        }
        else {
            [void]$sb.Append($c)
        }
        $i++
    }
    [void]$sb.Append('$')
    return $sb.ToString()
}

function Invoke-Git([string[]]$GitArgs) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.WorkingDirectory = $Repo
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $parts = New-Object System.Collections.Generic.List[string]
    [void]$parts.Add('-c')
    [void]$parts.Add('core.quotepath=false')
    foreach ($arg in $GitArgs) {
        if ($arg -match '[\s"]') {
            [void]$parts.Add('"' + ($arg.Replace('\', '\\').Replace('"', '\"')) + '"')
        }
        else {
            [void]$parts.Add($arg)
        }
    }
    $psi.Arguments = ($parts -join ' ')
    $proc = [Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    return [pscustomobject]@{ Exit = $proc.ExitCode; Stdout = $stdout; Stderr = $stderr }
}

function Split-Lines([string]$Text) {
    $list = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrEmpty($Text)) { return $list }
    foreach ($line in ($Text -split "`r?`n")) {
        if (-not [string]::IsNullOrWhiteSpace($line)) { [void]$list.Add($line.Trim()) }
    }
    return $list
}

function Get-Project([string]$Relative) {
    if ($Relative.StartsWith('tests/Antiphon.E2E/')) { return 'tests/Antiphon.E2E' }
    if ($Relative.StartsWith('tests/Antiphon.Messaging.Tests/')) { return 'tests/Antiphon.Messaging.Tests' }
    if ($Relative.StartsWith('tests/Antiphon.SessionRunner.Tests/')) { return 'tests/Antiphon.SessionRunner.Tests' }
    return 'tests/Antiphon.Tests'
}

function New-ClassFilter($Names) {
    $sorted = New-StringList
    Fill-Sorted $Names $sorted
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($name in $sorted) { [void]$parts.Add('(' + $name + '*)') }
    if ($parts.Count -eq 0) { return '' }
    return '/*/*/' + ($parts -join '|') + '/*'
}

function New-MethodFilter([string]$ClassName, $Methods) {
    $sorted = New-StringList
    Fill-Sorted $Methods $sorted
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($name in $sorted) { [void]$parts.Add('(' + $name + '*)') }
    if ($parts.Count -eq 0) { return '' }
    return '/*/*/' + $ClassName + '/' + ($parts -join '|')
}

if (-not (Test-Path -LiteralPath $Table)) {
    Write-Err "impacted-classes table was not found: $Table"
    if ($Json) {
        Exit-Json ([pscustomobject]@{
            ok = $false
            emptyGlobs = @()
            missingClasses = @()
            kinds = @()
            globHits = @()
            derivedFiles = @()
            error = "table not found: $Table"
        }) 1
    }
    exit 1
}

try {
    $raw = [IO.File]::ReadAllText($Table)
    $doc = $raw | ConvertFrom-Json
}
catch {
    Write-Err ("impacted-classes table is not valid JSON: " + $_.Exception.Message)
    if ($Json) {
        Exit-Json ([pscustomobject]@{
            ok = $false
            emptyGlobs = @()
            missingClasses = @()
            kinds = @()
            globHits = @()
            derivedFiles = @()
            error = $_.Exception.Message
        }) 1
    }
    exit 1
}

# Structural refusal happens before the tests walk. PowerShell 5.1 unwraps a one-element
# JSON array into the element, so a lone kind object is accepted as that one-element array.
$allowedRoot = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($name in @('schema', 'card', 'cache', 'maximumStaleness', 'kinds')) { [void]$allowedRoot.Add($name) }
$allowedKind = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($name in @('id', 'globs', 'classes', 'methods', 'added', 'derive')) { [void]$allowedKind.Add($name) }
$allowedAdded = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($name in @('glob', 'literals')) { [void]$allowedAdded.Add($name) }
$allowedDerive = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
[void]$allowedDerive.Add('patterns')

if (-not ($doc -is [System.Management.Automation.PSCustomObject])) {
    Refuse-Table 'root' 'expected an object with a kinds array'
}
foreach ($prop in $doc.PSObject.Properties) {
    if (-not $allowedRoot.Contains([string]$prop.Name)) {
        Refuse-Table 'root' ("unknown property '" + $prop.Name + "'")
    }
}
$schemaText = ''
$schemaProp = $doc.PSObject.Properties['schema']
if ($null -ne $schemaProp -and $null -ne $schemaProp.Value) { $schemaText = [string]$schemaProp.Value }
if ($schemaText -ne '1') {
    Refuse-Table 'root' 'schema must be 1'
}
$kindsProp = $doc.PSObject.Properties['kinds']
if ($null -eq $kindsProp -or $null -eq $kindsProp.Value) {
    Refuse-Table 'root' 'kinds must be an array'
}
$kindsValue = $kindsProp.Value
if ($kindsValue -is [System.Array]) {
    $kindNodes = @($kindsValue)
}
elseif ($kindsValue -is [System.Management.Automation.PSCustomObject]) {
    $kindNodes = @($kindsValue)
}
else {
    Refuse-Table 'root' 'kinds must be an array'
}

$parsed = New-Object System.Collections.Generic.List[object]
$derivePatterns = New-Object System.Collections.Generic.List[string]
$allGlobs = New-Object System.Collections.Generic.List[string]
$kindIndex = 0
foreach ($node in $kindNodes) {
    $where = 'kinds[' + $kindIndex + ']'
    $kindIndex++
    if (-not ($node -is [System.Management.Automation.PSCustomObject])) {
        Refuse-Table $where 'expected an object'
    }
    foreach ($prop in $node.PSObject.Properties) {
        if (-not $allowedKind.Contains([string]$prop.Name)) {
            Refuse-Table $where ("unknown property '" + $prop.Name + "'")
        }
    }
    $id = ''
    $idProp = $node.PSObject.Properties['id']
    if ($null -ne $idProp -and $null -ne $idProp.Value) { $id = [string]$idProp.Value }
    if ([string]::IsNullOrWhiteSpace($id)) {
        Refuse-Table $where 'missing id'
    }
    $where = $id

    $globs = New-Object System.Collections.Generic.List[string]
    if ($null -ne $node.PSObject.Properties['globs']) { $globs = As-List $node.globs }
    $classes = New-Object System.Collections.Generic.List[string]
    if ($null -ne $node.PSObject.Properties['classes']) { $classes = As-List $node.classes }
    $methods = New-Object System.Collections.Generic.List[string]
    if ($null -ne $node.PSObject.Properties['methods']) { $methods = As-List $node.methods }

    $addedGlob = ''
    $addedLiterals = New-Object System.Collections.Generic.List[string]
    $hasAdded = $false
    if ($null -ne $node.PSObject.Properties['added'] -and $null -ne $node.added) {
        if (-not ($node.added -is [System.Management.Automation.PSCustomObject])) {
            Refuse-Table $where 'added must be an object'
        }
        foreach ($prop in $node.added.PSObject.Properties) {
            if (-not $allowedAdded.Contains([string]$prop.Name)) {
                Refuse-Table $where ("unknown property '" + $prop.Name + "'")
            }
        }
        if ($null -ne $node.added.PSObject.Properties['glob'] -and $null -ne $node.added.glob) {
            $addedGlob = [string]$node.added.glob
        }
        if ($null -ne $node.added.PSObject.Properties['literals']) {
            $addedLiterals = As-List $node.added.literals
        }
        if (-not [string]::IsNullOrWhiteSpace($addedGlob) -or $addedLiterals.Count -gt 0) { $hasAdded = $true }
    }

    $hasDerive = $false
    if ($null -ne $node.PSObject.Properties['derive'] -and $null -ne $node.derive) {
        if (-not ($node.derive -is [System.Management.Automation.PSCustomObject])) {
            Refuse-Table $where 'derive must be an object'
        }
        foreach ($prop in $node.derive.PSObject.Properties) {
            if (-not $allowedDerive.Contains([string]$prop.Name)) {
                Refuse-Table $where ("unknown property '" + $prop.Name + "'")
            }
        }
        $patterns = New-Object System.Collections.Generic.List[string]
        if ($null -ne $node.derive.PSObject.Properties['patterns']) { $patterns = As-List $node.derive.patterns }
        foreach ($pattern in $patterns) {
            try {
                $null = [regex]::new($pattern, 'CultureInvariant')
            }
            catch {
                Refuse-Table $where ("derive pattern '" + $pattern + "' is not valid")
            }
            [void]$derivePatterns.Add($pattern)
        }
        if ($patterns.Count -gt 0) { $hasDerive = $true }
    }

    if ($globs.Count -lt 1 -and -not $hasAdded) {
        Refuse-Table $where 'no globs or added'
    }
    if ($classes.Count -lt 1 -and $methods.Count -lt 1 -and -not $hasDerive) {
        Refuse-Table $where 'no classes, methods, or derive'
    }
    foreach ($method in $methods) {
        $slash = $method.IndexOf('/')
        $last = $method.LastIndexOf('/')
        if ($slash -le 0 -or $slash -ge ($method.Length - 1) -or $slash -ne $last) {
            Refuse-Table $where ("methods entry '" + $method + "' is not Class/Method")
        }
        $methodClass = $method.Substring(0, $slash)
        if (-not $methodClass.EndsWith('Tests', [StringComparison]::Ordinal)) {
            Refuse-Table $where ("class '" + $methodClass + "' is not a *Tests class")
        }
    }
    foreach ($className in $classes) {
        if (-not $className.EndsWith('Tests', [StringComparison]::Ordinal)) {
            Refuse-Table $where ("class '" + $className + "' is not a *Tests class")
        }
    }

    foreach ($glob in $globs) { [void]$allGlobs.Add($glob) }
    if (-not [string]::IsNullOrWhiteSpace($addedGlob)) { [void]$allGlobs.Add($addedGlob) }
    $regexes = New-Object System.Collections.Generic.List[regex]
    foreach ($glob in $globs) { [void]$regexes.Add([regex]::new((Convert-GlobToRegex $glob), 'CultureInvariant')) }
    $addedRegex = $null
    if (-not [string]::IsNullOrWhiteSpace($addedGlob)) {
        $addedRegex = [regex]::new((Convert-GlobToRegex $addedGlob), 'CultureInvariant')
    }
    [void]$parsed.Add([pscustomobject]@{
        Id = $id
        Globs = $globs
        Classes = $classes
        Methods = $methods
        AddedRegex = $addedRegex
        AddedLiterals = $addedLiterals
        Regexes = $regexes
    })
}

function Test-Any([System.Collections.Generic.List[regex]]$Regexes, [string]$Relative) {
    foreach ($regex in $Regexes) {
        if ($regex.IsMatch($Relative)) { return $true }
    }
    return $false
}

# One walk: class declarations and downgrade-pattern files. bin/obj are build output.
$deriveRegexes = New-Object System.Collections.Generic.List[regex]
foreach ($pattern in $derivePatterns) { [void]$deriveRegexes.Add([regex]::new($pattern, 'CultureInvariant')) }
$classDecl = [regex]::new('(?m)^[ \t]*(?:(?:public|internal|file|private|protected)[ \t]+)?(?:(?:sealed|static|abstract|partial)[ \t]+)*class[ \t]+([A-Za-z_][A-Za-z0-9_]*)\b')
$classWord = @{}
$filesByClass = @{}
$derivedSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$testsRoot = Join-Path $Repo 'tests'
$repoSlash = $Repo.Replace('\', '/').TrimEnd('/')
if (Test-Path -LiteralPath $testsRoot) {
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($testsRoot)
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        foreach ($child in [IO.Directory]::EnumerateDirectories($dir)) {
            $name = [IO.Path]::GetFileName($child)
            if ($name -eq 'bin' -or $name -eq 'obj') { continue }
            $stack.Push($child)
        }
        foreach ($file in [IO.Directory]::EnumerateFiles($dir, '*.cs')) {
            $full = $file.Replace('\', '/')
            $relative = $full
            $prefix = $repoSlash + '/'
            if ($full.StartsWith($prefix)) { $relative = $full.Substring($prefix.Length) }
            if ($relative.Contains('/bin/') -or $relative.Contains('/obj/')) { continue }
            $text = [IO.File]::ReadAllText($file)
            foreach ($match in $classDecl.Matches($text)) {
                $className = $match.Groups[1].Value
                if (-not $classWord.ContainsKey($className)) {
                    $classWord[$className] = New-Object System.Collections.Generic.List[string]
                }
                [void]$classWord[$className].Add($relative)
            }
            $hit = $false
            foreach ($regex in $deriveRegexes) {
                if ($regex.IsMatch($text)) { $hit = $true; break }
            }
            if ($hit) { [void]$derivedSet.Add($relative) }
        }
    }
}
foreach ($className in @($classWord.Keys)) {
    $owned = New-StringList
    Fill-Sorted $classWord[$className] $owned
    $filesByClass[$className] = $owned
}

# Named classes are refused for audit and select. Hashtables ignore case, so the census is ordinal.
$census = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($className in @($filesByClass.Keys)) { [void]$census.Add([string]$className) }
foreach ($kind in $parsed) {
    $named = New-Object System.Collections.Generic.List[string]
    foreach ($className in $kind.Classes) { [void]$named.Add($className) }
    foreach ($method in $kind.Methods) {
        $slash = $method.IndexOf('/')
        if ($slash -gt 0) { [void]$named.Add($method.Substring(0, $slash)) }
    }
    foreach ($className in $named) {
        if (-not $census.Contains($className)) {
            Refuse-Table $kind.Id ("class '" + $className + "' was not found")
        }
    }
}

$tracked = $null
function Get-Tracked {
    if ($null -ne $script:tracked) { return $script:tracked }
    $ran = Invoke-Git @('ls-files', '-c')
    if ($ran.Exit -ne 0) {
        Write-Err ("git ls-files failed: " + $ran.Stderr.Trim())
        Exit-Json ([pscustomobject]@{
            ok = $false
            emptyGlobs = @()
            missingClasses = @()
            kinds = @()
            globHits = @()
            derivedFiles = @()
            error = $ran.Stderr.Trim()
        }) 2
    }
    $script:tracked = Split-Lines $ran.Stdout
    return $script:tracked
}

function Get-GlobHits([string]$Glob) {
    $regex = [regex]::new((Convert-GlobToRegex $Glob), 'CultureInvariant')
    $count = 0
    foreach ($path in (Get-Tracked)) {
        if ($regex.IsMatch($path)) { $count++ }
    }
    return $count
}

if ($Audit) {
    $empty = New-Object System.Collections.Generic.List[string]
    $globHits = New-Object System.Collections.Generic.List[object]
    $seenGlobs = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($glob in $allGlobs) {
        if (-not $seenGlobs.Add($glob)) { continue }
        $count = Get-GlobHits $glob
        [void]$globHits.Add([pscustomobject]@{ glob = $glob; count = $count })
        if ($count -lt 1) { [void]$empty.Add($glob) }
    }
    $missing = New-Object System.Collections.Generic.List[string]
    $seenClasses = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($kind in $parsed) {
        foreach ($className in $kind.Classes) {
            if (-not $seenClasses.Add($className)) { continue }
            if (-not $filesByClass.ContainsKey($className)) { [void]$missing.Add($className) }
        }
        foreach ($method in $kind.Methods) {
            $slash = $method.IndexOf('/')
            $className = $method
            if ($slash -gt 0) { $className = $method.Substring(0, $slash) }
            if (-not $seenClasses.Add($className)) { continue }
            if (-not $filesByClass.ContainsKey($className)) { [void]$missing.Add($className) }
        }
    }
    $emptySorted = New-StringList
    Fill-Sorted $empty $emptySorted
    $missingSorted = New-StringList
    Fill-Sorted $missing $missingSorted
    $kindIds = New-StringList
    foreach ($kind in $parsed) { if (-not [string]::IsNullOrWhiteSpace($kind.Id)) { [void]$kindIds.Add($kind.Id) } }
    $kindSorted = New-StringList
    Fill-Sorted $kindIds $kindSorted
    $derivedSorted = New-StringList
    Fill-Sorted $derivedSet $derivedSorted
    $ok = ($emptySorted.Count -eq 0 -and $missingSorted.Count -eq 0)
    $payload = [pscustomobject]@{
        ok = $ok
        emptyGlobs = $emptySorted
        missingClasses = $missingSorted
        kinds = $kindSorted
        globHits = @($globHits | Sort-Object -Property glob)
        derivedFiles = $derivedSorted
    }
    $code = 0
    if (-not $ok) {
        $code = 1
        Write-Err ("impacted-classes audit failed. emptyGlobs=" + ($emptySorted -join ',') + " missingClasses=" + ($missingSorted -join ','))
    }
    if ($Json) { Exit-Json $payload $code }
    Write-Output ("ok=" + $ok)
    Write-Output ("emptyGlobs=" + ($emptySorted -join ','))
    Write-Output ("missingClasses=" + ($missingSorted -join ','))
    exit $code
}

$changed = New-Object System.Collections.Generic.List[string]
$useAdded = $false
if (-not [string]::IsNullOrWhiteSpace($Range)) {
    $useAdded = $true
    $ran = Invoke-Git @('diff', '--name-only', $Range)
    if ($ran.Exit -ne 0) {
        Write-Err ("git diff failed: " + $ran.Stderr.Trim())
        if ($Json) {
            Exit-Json ([pscustomobject]@{
                ok = $false
                kinds = @()
                classes = @()
                files = @()
                treenodeFilter = ''
                filters = @()
                error = $ran.Stderr.Trim()
            }) 2
        }
        exit 2
    }
    foreach ($line in (Split-Lines $ran.Stdout)) { [void]$changed.Add($line.Replace('\', '/')) }
}
elseif ($MergeSubset -and [string]::IsNullOrWhiteSpace($FileList) -and ($null -eq $Path -or @($Path).Count -eq 0)) {
    # Fixed subset only. A migration diff is what adds the downgrade selector.
}
elseif (-not [string]::IsNullOrWhiteSpace($FileList) -or ($null -ne $Path -and $Path.Count -gt 0)) {
    if (-not [string]::IsNullOrWhiteSpace($FileList)) {
        if (-not (Test-Path -LiteralPath $FileList)) {
            Write-Err "file list was not found: $FileList"
            exit 2
        }
        foreach ($line in (Split-Lines ([IO.File]::ReadAllText($FileList)))) { [void]$changed.Add($line.Replace('\', '/')) }
    }
    if ($null -ne $Path) {
        foreach ($line in $Path) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { [void]$changed.Add($line.Replace('\', '/')) }
        }
    }
}
else {
    Write-Err "usage: impacted-classes.ps1 -Range <git-range> | -FileList <file> | -Path <repo-relative> | -MergeSubset [-Range <git-range>] [-Json] [-Audit]"
    exit 2
}

if ($MergeSubset) {
    $fixedNames = @(
        'AppDbContextModelTests',
        'CheckpointReceiptSafetyTests',
        'ContractSnapshotTests',
        'HostCleanupCheckpointReceiptTests',
        'TestTimingOperationLabelTests'
    )
    $wholeSubset = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($fixedName in $fixedNames) {
        if (-not $census.Contains($fixedName)) {
            $missingMessage = "merge-subset refused: class '" + $fixedName + "' was not found"
            Write-Err $missingMessage
            if ($Json) {
                Exit-Json ([pscustomobject]@{
                    ok = $false
                    mode = 'merge-subset'
                    migrations = $false
                    error = $missingMessage
                }) 1
            }
            exit 1
        }
        [void]$wholeSubset.Add($fixedName)
    }
    $migrationRegex = [regex]::new((Convert-GlobToRegex 'server/Migrations/**'), 'CultureInvariant')
    $migrationsHit = $false
    foreach ($changedPath in $changed) {
        if ($migrationRegex.IsMatch($changedPath)) { $migrationsHit = $true; break }
    }
    if ($migrationsHit) {
        foreach ($kind in $parsed) {
            if ($kind.Id -ne 'migrations') { continue }
            foreach ($className in $kind.Classes) {
                if ($className.EndsWith('Tests')) { [void]$wholeSubset.Add($className) }
            }
        }
        foreach ($className in @($filesByClass.Keys)) {
            $ownsDerived = $false
            foreach ($relative in $filesByClass[$className]) {
                if ($derivedSet.Contains($relative)) { $ownsDerived = $true; break }
            }
            if ($ownsDerived -and ([string]$className).EndsWith('Tests')) { [void]$wholeSubset.Add($className) }
        }
    }
    $subsetByProject = @{}
    foreach ($className in $wholeSubset) {
        $project = 'tests/Antiphon.Tests'
        if ($filesByClass.ContainsKey($className) -and $filesByClass[$className].Count -gt 0) {
            $project = Get-Project $filesByClass[$className][0]
        }
        if (-not $subsetByProject.ContainsKey($project)) {
            $subsetByProject[$project] = New-Object System.Collections.Generic.List[string]
        }
        [void]$subsetByProject[$project].Add($className)
    }
    $subsetFilters = New-Object System.Collections.Generic.List[object]
    $subsetProjects = New-StringList
    Fill-Sorted @($subsetByProject.Keys) $subsetProjects
    foreach ($project in $subsetProjects) {
        $filter = New-ClassFilter $subsetByProject[$project]
        [void]$subsetFilters.Add([pscustomobject]@{ project = $project; treenodeFilter = $filter })
    }
    $subsetTop = ''
    foreach ($row in $subsetFilters) {
        if ($row.project -eq 'tests/Antiphon.Tests') { $subsetTop = $row.treenodeFilter; break }
    }
    if ([string]::IsNullOrEmpty($subsetTop) -and $subsetFilters.Count -gt 0) { $subsetTop = $subsetFilters[0].treenodeFilter }
    $fixedOut = New-StringList
    Fill-Sorted $fixedNames $fixedOut
    $classOut = New-StringList
    Fill-Sorted $wholeSubset $classOut
    $subsetPayload = [pscustomobject]@{
        ok = $true
        mode = 'merge-subset'
        migrations = [bool]$migrationsHit
        fixedClasses = $fixedOut
        classes = $classOut
        treenodeFilter = [string]$subsetTop
        filters = @($subsetFilters.ToArray())
    }
    if ($Json) { Exit-Json $subsetPayload 0 }
    Write-Output 'mode=merge-subset'
    Write-Output ('migrations=' + $migrationsHit)
    Write-Output ('fixedClasses=' + ($fixedOut -join ','))
    Write-Output ('classes=' + ($classOut -join ','))
    Write-Output ('treenodeFilter=' + $subsetTop)
    exit 0
}

$addedText = $null
function Get-AddedText {
    if ($null -ne $script:addedText) { return $script:addedText }
    $ran = Invoke-Git @('diff', '-U0', '--no-color', $Range, '--', 'tests/')
    if ($ran.Exit -ne 0) {
        Write-Err ("git diff failed: " + $ran.Stderr.Trim())
        exit 2
    }
    $script:addedText = $ran.Stdout
    return $script:addedText
}

$matched = New-Object System.Collections.Generic.List[object]
foreach ($kind in $parsed) {
    $pathHit = $false
    foreach ($path in $changed) {
        if (Test-Any $kind.Regexes $path) { $pathHit = $true; break }
    }
    $literalHit = $false
    if ($useAdded -and $null -ne $kind.AddedRegex -and $kind.AddedLiterals.Count -gt 0) {
        $watched = $false
        foreach ($path in $changed) {
            if ($kind.AddedRegex.IsMatch($path)) { $watched = $true; break }
        }
        if ($watched) {
            $text = Get-AddedText
            foreach ($line in ($text -split "`n")) {
                if ($line.StartsWith('+') -and -not $line.StartsWith('+++')) {
                    foreach ($literal in $kind.AddedLiterals) {
                        if ($line.Contains($literal)) { $literalHit = $true; break }
                    }
                }
                if ($literalHit) { break }
            }
        }
    }
    if ($pathHit -or $literalHit) { [void]$matched.Add($kind) }
}

$whole = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$methodsByClass = @{}
$fileSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)

function Add-ClassFile([string]$ClassName) {
    if (-not $filesByClass.ContainsKey($ClassName)) { return }
    foreach ($relative in $filesByClass[$ClassName]) { [void]$fileSet.Add($relative) }
}

foreach ($kind in $matched) {
    foreach ($className in $kind.Classes) {
        if ($className.EndsWith('Tests')) { [void]$whole.Add($className) }
        Add-ClassFile $className
    }
    $derivedKind = $false
    foreach ($node in $kindNodes) {
        if ($null -eq $node) { continue }
        $id = ''
        if ($null -ne $node.PSObject.Properties['id']) { $id = [string]$node.id }
        if ($id -eq $kind.Id -and $null -ne $node.PSObject.Properties['derive'] -and $null -ne $node.derive) {
            $derivedKind = $true
        }
    }
    if ($derivedKind) {
        foreach ($relative in $derivedSet) {
            [void]$fileSet.Add($relative)
            if ($filesByClass.Count -gt 0) {
                # Class names are indexed by name, so scan this file's known declarations via the reverse map.
            }
        }
        foreach ($className in @($filesByClass.Keys)) {
            $owns = $false
            foreach ($relative in $filesByClass[$className]) {
                if ($derivedSet.Contains($relative)) { $owns = $true; break }
            }
            if ($owns -and $className.EndsWith('Tests')) { [void]$whole.Add($className) }
        }
    }
    foreach ($method in $kind.Methods) {
        $slash = $method.IndexOf('/')
        if ($slash -le 0 -or $slash -ge ($method.Length - 1)) { continue }
        $className = $method.Substring(0, $slash)
        $methodName = $method.Substring($slash + 1)
        if (-not $methodsByClass.ContainsKey($className)) {
            $methodsByClass[$className] = New-Object System.Collections.Generic.List[string]
        }
        [void]$methodsByClass[$className].Add($methodName)
        Add-ClassFile $className
    }
}

$classNames = New-Object System.Collections.Generic.List[string]
foreach ($name in $whole) { [void]$classNames.Add($name) }
foreach ($className in @($methodsByClass.Keys)) {
    if (-not $whole.Contains($className)) { [void]$classNames.Add($className) }
}

$byProjectClasses = @{}
foreach ($className in $whole) {
    $project = 'tests/Antiphon.Tests'
    if ($filesByClass.ContainsKey($className) -and $filesByClass[$className].Count -gt 0) {
        $project = Get-Project $filesByClass[$className][0]
    }
    if (-not $byProjectClasses.ContainsKey($project)) {
        $byProjectClasses[$project] = New-Object System.Collections.Generic.List[string]
    }
    [void]$byProjectClasses[$project].Add($className)
}

$filters = New-Object System.Collections.Generic.List[object]
$projectOrder = New-StringList
Fill-Sorted @($byProjectClasses.Keys) $projectOrder
foreach ($project in $projectOrder) {
    $filter = New-ClassFilter $byProjectClasses[$project]
    [void]$filters.Add([pscustomobject]@{ project = $project; treenodeFilter = $filter; shape = 'class' })
}
$methodProjects = New-Object System.Collections.Generic.List[object]
$methodOrder = New-StringList
Fill-Sorted @($methodsByClass.Keys) $methodOrder
foreach ($className in $methodOrder) {
    if ($whole.Contains($className)) { continue }
    $project = 'tests/Antiphon.Tests'
    if ($filesByClass.ContainsKey($className) -and $filesByClass[$className].Count -gt 0) {
        $project = Get-Project $filesByClass[$className][0]
    }
    $filter = New-MethodFilter $className $methodsByClass[$className]
    [void]$methodProjects.Add([pscustomobject]@{ project = $project; treenodeFilter = $filter; shape = 'method' })
}
foreach ($row in ($methodProjects | Sort-Object project, treenodeFilter)) {
    [void]$filters.Add([pscustomobject]@{ project = $row.project; treenodeFilter = $row.treenodeFilter })
}

$emitted = New-Object System.Collections.Generic.List[object]
foreach ($row in $filters) {
    [void]$emitted.Add([pscustomobject]@{ project = $row.project; treenodeFilter = $row.treenodeFilter })
}

$top = ''
foreach ($row in $emitted) {
    if ($row.project -eq 'tests/Antiphon.Tests' -and $row.treenodeFilter.StartsWith('/*/*/(')) {
        $top = $row.treenodeFilter
        break
    }
}
if ([string]::IsNullOrEmpty($top)) {
    foreach ($row in $emitted) {
        if ($row.project -eq 'tests/Antiphon.Tests') { $top = $row.treenodeFilter; break }
    }
}
if ([string]::IsNullOrEmpty($top) -and $emitted.Count -gt 0) { $top = $emitted[0].treenodeFilter }

$kindOut = New-Object System.Collections.Generic.List[string]
foreach ($kind in $matched) { if (-not [string]::IsNullOrWhiteSpace($kind.Id)) { [void]$kindOut.Add($kind.Id) } }

$k = New-StringList
Fill-Sorted $kindOut $k
$c = New-StringList
Fill-Sorted $classNames $c
$f = New-StringList
Fill-Sorted $fileSet $f
$payload = [pscustomobject]@{
    kinds = $k
    classes = $c
    files = $f
    treenodeFilter = [string]$top
    filters = @($emitted.ToArray())
}
if ($Json) { Exit-Json $payload 0 }
Write-Output ("kinds=" + ($k -join ','))
Write-Output ("treenodeFilter=" + $top)
foreach ($relative in $f) { Write-Output $relative }
exit 0
