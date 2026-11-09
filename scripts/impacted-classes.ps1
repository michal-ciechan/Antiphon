# CARD-1782. Read-only selector from tests/impacted-classes.json.
# CARD-1783. -MergeSubset always emits the fixed high-radius classes and methods. Text mode prints
# one "filter=<project> <treenodeFilter>" line per filters entry.
# CARD-1801. -MergeSubset with -Range, -Path or -FileList also emits every row those paths select
# (the migrations row brings the downgrade selector), each filters entry with its CHECKPOINT row id.
# A kind with "windows": "required" also lands in windowsFilters ("windowsFilter=" lines): the
# native Windows lane owes that filter, and the Linux row of it still runs. A kind with
# "windows": "only" lands in windowsFilters only and is omitted from the Linux filters. An added block with
# "removed": true also matches a removed line that carries one of its literals.
# Under -MergeSubset a project row naming more than four land classes is split into <id>-land-<n> rows
# (CARD-1801 D-1); the non-merge output keeps each project selection in one row (R2-D-1).
# CARD-1820. A changed tests file selects the classes it declares. tests/derived-impact-index.json
# adds reverse edges. JSON gains derivedClasses, keywordIds, rosterIds and skippedByCap.
# A missing index adds no derived class. Keyword and roster lists stay empty in this slice.
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
foreach ($name in @('id', 'globs', 'classes', 'methods', 'added', 'derive', 'windows')) { [void]$allowedKind.Add($name) }
$allowedAdded = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($name in @('glob', 'literals', 'removed')) { [void]$allowedAdded.Add($name) }
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

    $windowsLane = ''
    if ($null -ne $node.PSObject.Properties['windows']) {
        $laneValue = $node.windows
        if (-not ($laneValue -is [string]) -or (($laneValue -cne 'required') -and ($laneValue -cne 'only'))) {
            Refuse-Table $where "windows must be 'required' or 'only'"
        }
        $windowsLane = [string]$laneValue
    }

    $addedGlob = ''
    $addedLiterals = New-Object System.Collections.Generic.List[string]
    $addedRemoved = $false
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
        if ($null -ne $node.added.PSObject.Properties['removed']) {
            if (-not ($node.added.removed -is [bool])) {
                Refuse-Table $where 'removed must be true or false'
            }
            $addedRemoved = [bool]$node.added.removed
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
        AddedRemoved = $addedRemoved
        HasDerive = $hasDerive
        Windows = $windowsLane
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
$script:classesByFile = New-Object 'System.Collections.Generic.Dictionary[string,System.Collections.Generic.List[string]]' ([StringComparer]::Ordinal)
$script:derivedIndexState = $null
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
                $declared = $null
                if (-not $script:classesByFile.TryGetValue($relative, [ref]$declared)) {
                    $declared = New-Object System.Collections.Generic.List[string]
                    $script:classesByFile[$relative] = $declared
                }
                if (-not $declared.Contains($className)) { [void]$declared.Add($className) }
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
    # Fixed subset only. A batch diff adds every row it selects.
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
    Write-Err "usage: impacted-classes.ps1 -Range <git-range> | -FileList <file> | -Path <repo-relative> | -MergeSubset [-Range <git-range> | -FileList <file> | -Path <repo-relative>] [-Json] [-Audit]"
    exit 2
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

function Test-LiteralLine([string]$Line, $Kind) {
    $isAdded = $Line.StartsWith('+') -and -not $Line.StartsWith('+++')
    $isRemoved = $Kind.AddedRemoved -and $Line.StartsWith('-') -and -not $Line.StartsWith('---')
    if (-not ($isAdded -or $isRemoved)) { return $false }
    foreach ($literal in $Kind.AddedLiterals) {
        if ($Line.Contains($literal)) { return $true }
    }
    return $false
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
                if (Test-LiteralLine $line $kind) { $literalHit = $true; break }
            }
        }
    }
    if ($pathHit -or $literalHit) { [void]$matched.Add($kind) }
}

function Add-ClassFile([string]$ClassName, $FileSet) {
    if ($null -eq $FileSet -or -not $filesByClass.ContainsKey($ClassName)) { return }
    foreach ($relative in $filesByClass[$ClassName]) { [void]$FileSet.Add($relative) }
}

function Add-MethodEntry([string]$Method, $MethodsByClass, $FileSet) {
    $slash = $Method.IndexOf('/')
    if ($slash -le 0 -or $slash -ge ($Method.Length - 1)) { return }
    $className = $Method.Substring(0, $slash)
    if (-not $MethodsByClass.ContainsKey($className)) {
        $MethodsByClass[$className] = New-Object System.Collections.Generic.List[string]
    }
    [void]$MethodsByClass[$className].Add($Method.Substring($slash + 1))
    Add-ClassFile $className $FileSet
}

# One kind's whole classes, derived downgrade callers and method entries.
function Add-KindSelection($Kind, $Whole, $MethodsByClass, $FileSet) {
    foreach ($className in $Kind.Classes) {
        if ($className.EndsWith('Tests')) { [void]$Whole.Add($className) }
        Add-ClassFile $className $FileSet
    }
    if ($Kind.HasDerive) {
        if ($null -ne $FileSet) {
            foreach ($relative in $derivedSet) { [void]$FileSet.Add($relative) }
        }
        foreach ($className in @($filesByClass.Keys)) {
            $owns = $false
            foreach ($relative in $filesByClass[$className]) {
                if ($derivedSet.Contains($relative)) { $owns = $true; break }
            }
            if ($owns -and ([string]$className).EndsWith('Tests')) { [void]$Whole.Add([string]$className) }
        }
    }
    foreach ($method in $Kind.Methods) { Add-MethodEntry $method $MethodsByClass $FileSet }
}

function Get-ClassProject([string]$ClassName) {
    if ($filesByClass.ContainsKey($ClassName) -and $filesByClass[$ClassName].Count -gt 0) {
        return (Get-Project $filesByClass[$ClassName][0])
    }
    return 'tests/Antiphon.Tests'
}

function Get-ProjectSlug([string]$Project) {
    if ($Project -eq 'tests/Antiphon.Tests') { return 'tests' }
    if ($Project -eq 'tests/Antiphon.E2E') { return 'e2e' }
    if ($Project -eq 'tests/Antiphon.Messaging.Tests') { return 'messaging' }
    if ($Project -eq 'tests/Antiphon.SessionRunner.Tests') { return 'sessionrunner' }
    return $Project.Replace('tests/', '').ToLowerInvariant()
}

# Class rows first, one per project in ordinal order, then one method row per class.
# A whole class wins over a method row of the same class. Id is the CHECKPOINT row name.
# CARD-1801 D-1: a land class is a class name containing "Land" (case-sensitive), which is what
# the checkpoint importer counts. Only under -MergeSubset ($FirstLand not null), when a project's class
# row would name more than four, its land classes leave that row and go four at a time, in ordinal
# order, to rows <id>-land-1, -land-2, ...;
# the other classes stay in the project row, which is omitted when none remain. Land classes named in
# $FirstLand (the -MergeSubset fixed classes) fill the first rows and the others start a new row, so a
# batch class never shifts a fixed land row and each fixed row keeps its measured floor.
# With $FirstLand null (the non-merge output a Code or Review stage runs) each project keeps its whole
# class selection in one row, so the printed treenodeFilter names every class (R2-D-1).
function New-FilterRows($Whole, $MethodsByClass, [string]$IdPrefix, $FirstLand) {
    $rows = New-Object System.Collections.Generic.List[object]
    $byProject = @{}
    foreach ($className in $Whole) {
        $project = Get-ClassProject $className
        if (-not $byProject.ContainsKey($project)) {
            $byProject[$project] = New-Object System.Collections.Generic.List[string]
        }
        [void]$byProject[$project].Add($className)
    }
    $projectOrder = New-StringList
    Fill-Sorted @($byProject.Keys) $projectOrder
    foreach ($project in $projectOrder) {
        $projectId = $IdPrefix + (Get-ProjectSlug $project)
        $sortedNames = New-StringList
        Fill-Sorted $byProject[$project] $sortedNames
        $firstNames = New-StringList
        $laterNames = New-StringList
        $otherNames = New-StringList
        foreach ($className in $sortedNames) {
            if (-not $className.Contains('Land')) { [void]$otherNames.Add($className) }
            elseif ($null -ne $FirstLand -and [Array]::IndexOf([string[]]@($FirstLand), $className) -ge 0) { [void]$firstNames.Add($className) }
            else { [void]$laterNames.Add($className) }
        }
        if ($null -eq $FirstLand -or $firstNames.Count + $laterNames.Count -le 4) {
            [void]$rows.Add([pscustomobject]@{ Id = $projectId; Project = $project; Filter = (New-ClassFilter $sortedNames) })
            continue
        }
        if ($otherNames.Count -gt 0) {
            [void]$rows.Add([pscustomobject]@{ Id = $projectId; Project = $project; Filter = (New-ClassFilter $otherNames) })
        }
        $landRow = 0
        foreach ($group in @($firstNames, $laterNames)) {
            for ($at = 0; $at -lt $group.Count; $at += 4) {
                $chunk = New-StringList
                for ($i = $at; $i -lt [Math]::Min($at + 4, $group.Count); $i++) { [void]$chunk.Add($group[$i]) }
                $landRow++
                [void]$rows.Add([pscustomobject]@{
                    Id = $projectId + '-land-' + $landRow
                    Project = $project
                    Filter = (New-ClassFilter $chunk)
                })
            }
        }
    }
    $methodRows = New-Object System.Collections.Generic.List[object]
    $methodOrder = New-StringList
    Fill-Sorted @($MethodsByClass.Keys) $methodOrder
    foreach ($className in $methodOrder) {
        if ($Whole.Contains($className)) { continue }
        [void]$methodRows.Add([pscustomobject]@{
            Id = $IdPrefix + $className
            Project = (Get-ClassProject $className)
            Filter = (New-MethodFilter $className $MethodsByClass[$className])
        })
    }
    foreach ($row in ($methodRows | Sort-Object Project, Filter)) { [void]$rows.Add($row) }
    return ,$rows
}

function Get-ClassNames($Whole, $MethodsByClass) {
    $names = New-StringList
    foreach ($name in $Whole) { [void]$names.Add($name) }
    foreach ($className in @($MethodsByClass.Keys)) {
        if (-not $Whole.Contains([string]$className)) { [void]$names.Add([string]$className) }
    }
    $sorted = New-StringList
    Fill-Sorted $names $sorted
    return ,$sorted
}

function Get-TopFilter($Rows) {
    foreach ($row in $Rows) {
        if ($row.Project -eq 'tests/Antiphon.Tests' -and $row.Filter.StartsWith('/*/*/(')) { return $row.Filter }
    }
    foreach ($row in $Rows) {
        if ($row.Project -eq 'tests/Antiphon.Tests') { return $row.Filter }
    }
    foreach ($row in $Rows) { return $row.Filter }
    return ''
}

function Convert-Rows($Rows, [bool]$WithId) {
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($row in $Rows) {
        if ($WithId) {
            [void]$out.Add([pscustomobject][ordered]@{ id = $row.Id; project = $row.Project; treenodeFilter = $row.Filter })
        }
        else {
            [void]$out.Add([pscustomobject][ordered]@{ project = $row.Project; treenodeFilter = $row.Filter })
        }
    }
    return ,$out
}

function Refuse-Index([string]$Problem) {
    $message = 'derived-impact index refused: ' + $Problem
    Write-Err $message
    if ($script:Json) {
        Exit-Json ([pscustomobject]@{
            ok = $false
            error = $message
            derivedClasses = (New-Object System.Collections.Generic.List[string])
            keywordIds = (New-Object System.Collections.Generic.List[string])
            rosterIds = (New-Object System.Collections.Generic.List[string])
            skippedByCap = (New-Object System.Collections.Generic.List[string])
        }) 1
    }
    exit 1
}

function Get-DerivedIndex {
    if ($null -ne $script:derivedIndexState) { return $script:derivedIndexState }
    $edges = New-Object 'System.Collections.Generic.Dictionary[string,System.Collections.Generic.List[string]]' ([StringComparer]::Ordinal)
    $capped = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $allow = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $state = [pscustomobject]@{ Edges = $edges; Capped = $capped; Allow = $allow }
    $indexPath = Join-Path $Repo 'tests/derived-impact-index.json'
    if (-not (Test-Path -LiteralPath $indexPath)) {
        $script:derivedIndexState = $state
        return $state
    }
    try {
        $doc = [IO.File]::ReadAllText($indexPath) | ConvertFrom-Json
    }
    catch {
        Refuse-Index $_.Exception.Message
    }
    if (-not ($doc -is [System.Management.Automation.PSCustomObject])) {
        Refuse-Index 'expected an object'
    }
    $schemaText = ''
    $schemaProp = $doc.PSObject.Properties['schema']
    if ($null -ne $schemaProp -and $null -ne $schemaProp.Value) { $schemaText = [string]$schemaProp.Value }
    if ($schemaText -ne '1') { Refuse-Index 'schema must be 1' }
    $edgesProp = $doc.PSObject.Properties['edges']
    $cappedProp = $doc.PSObject.Properties['cappedPaths']
    if ($null -eq $edgesProp -or $null -eq $edgesProp.Value -or $null -eq $cappedProp -or $null -eq $cappedProp.Value) {
        Refuse-Index 'edges and cappedPaths are required'
    }
    foreach ($edge in @($doc.edges)) {
        if ($null -eq $edge) { continue }
        if (-not ($edge -is [System.Management.Automation.PSCustomObject])) { continue }
        $path = ''
        $pathProp = $edge.PSObject.Properties['path']
        if ($null -ne $pathProp -and $null -ne $pathProp.Value) { $path = ([string]$pathProp.Value).Replace('\', '/') }
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $names = New-Object System.Collections.Generic.List[string]
        $classProp = $edge.PSObject.Properties['classes']
        if ($null -ne $classProp -and $null -ne $classProp.Value) {
            foreach ($name in @($classProp.Value)) {
                if ($null -eq $name) { continue }
                $text = [string]$name
                if (-not [string]::IsNullOrWhiteSpace($text)) { [void]$names.Add($text) }
            }
        }
        $edges[$path] = $names
    }
    foreach ($row in @($doc.cappedPaths)) {
        if ($null -eq $row) { continue }
        if (-not ($row -is [System.Management.Automation.PSCustomObject])) { continue }
        $path = ''
        $pathProp = $row.PSObject.Properties['path']
        if ($null -ne $pathProp -and $null -ne $pathProp.Value) { $path = ([string]$pathProp.Value).Replace('\', '/') }
        if (-not [string]::IsNullOrWhiteSpace($path)) { [void]$capped.Add($path) }
    }
    $allowProp = $doc.PSObject.Properties['allowlist']
    if ($null -ne $allowProp -and $null -ne $allowProp.Value) {
        foreach ($item in @($allowProp.Value)) {
            if ($null -eq $item) { continue }
            $text = ([string]$item).Replace('\', '/')
            if (-not [string]::IsNullOrWhiteSpace($text)) { [void]$allow.Add($text) }
        }
    }
    $script:derivedIndexState = $state
    return $state
}

function Test-LandGraphToolOnly($Paths) {
    $any = $false
    foreach ($path in @($Paths)) {
        if ([string]::IsNullOrWhiteSpace([string]$path)) { continue }
        $any = $true
        $norm = ([string]$path).Replace('\', '/')
        $core = $norm.StartsWith('tools/Antiphon.LandGraphAudit.Core/')
        $audit = $norm -cmatch '^scripts/audit-land-verification.*\.ps1$'
        if (-not ($core -or $audit)) { return $false }
    }
    return $any
}

function Resolve-Derived($Paths) {
    $whole = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $methods = New-Object System.Collections.Generic.List[string]
    $skippedSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $index = Get-DerivedIndex
    $toolOnly = Test-LandGraphToolOnly $Paths
    foreach ($path in @($Paths)) {
        if ([string]::IsNullOrWhiteSpace([string]$path)) { continue }
        $norm = ([string]$path).Replace('\', '/')
        $isTestFile = $norm.StartsWith('tests/') -and $norm.EndsWith('.cs')
        if ($isTestFile -and $script:classesByFile.ContainsKey($norm)) {
            foreach ($className in $script:classesByFile[$norm]) {
                if ($className.EndsWith('Tests')) { [void]$whole.Add($className) }
            }
        }
        $capped = $index.Allow.Contains($norm) -or $index.Capped.Contains($norm)
        if ($capped) { [void]$skippedSet.Add($norm) }
        if ($capped) { continue }
        if (-not $index.Edges.ContainsKey($norm)) { continue }
        foreach ($className in $index.Edges[$norm]) {
            if (-not $census.Contains($className)) { continue }
            $methodOnly = $toolOnly -and ($className -eq 'LandGraphEvaluationTests') -and (-not $whole.Contains($className))
            if ($methodOnly) {
                [void]$methods.Add('LandGraphEvaluationTests/EvaluationBudgetRefusesSlowDotnet')
            }
            else {
                [void]$whole.Add($className)
            }
        }
    }
    $derived = New-Object System.Collections.Generic.List[string]
    Fill-Sorted @($whole) $derived
    foreach ($method in $methods) {
        $slash = $method.IndexOf('/')
        if ($slash -gt 0) {
            $className = $method.Substring(0, $slash)
            if (-not $whole.Contains($className)) { [void]$derived.Add($className) }
        }
    }
    if ($methods.Count -gt 0) {
        $resorted = New-Object System.Collections.Generic.List[string]
        Fill-Sorted $derived $resorted
        $derived = $resorted
    }
    $skipped = New-Object System.Collections.Generic.List[string]
    Fill-Sorted @($skippedSet) $skipped
    return [pscustomobject]@{
        Whole = $whole
        Methods = $methods
        Derived = $derived
        Skipped = $skipped
    }
}

function Apply-Derived($Selection, $Whole, $MethodsByClass) {
    foreach ($className in @($Selection.Whole)) { [void]$Whole.Add([string]$className) }
    foreach ($method in @($Selection.Methods)) { Add-MethodEntry ([string]$method) $MethodsByClass $null }
}

function New-EmptyNameList {
    $list = New-Object System.Collections.Generic.List[string]
    return ,$list
}

$derivedSelection = Resolve-Derived $changed

# The Windows lane: required and only both land here. only is omitted from the Linux selection.
$windowsWhole = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$windowsMethods = @{}
$kindOut = New-StringList
foreach ($kind in $matched) {
    if (-not [string]::IsNullOrWhiteSpace($kind.Id)) { [void]$kindOut.Add($kind.Id) }
    if ($kind.Windows) { Add-KindSelection $kind $windowsWhole $windowsMethods $null }
}
$k = New-StringList
Fill-Sorted $kindOut $k

if ($MergeSubset) {
    $fixedNames = @(
        'AgentTaskLandHeldNotificationTests',
        'AgentTaskLandNotificationParkFollowUpTests',
        'AgentTaskLandNotificationPersistenceTests',
        'AgentTaskLandNotificationReconcileLoadTests',
        'AgentTaskLandNotificationRecoveryTests',
        'AgentTaskLandNotificationRescanTests',
        'AgentTaskLandReceiptWatermarkSafetyTests',
        'AgentTaskLandReceiptWatermarkTests',
        'AppDbContextModelTests',
        'CheckpointReceiptSafetyTests',
        'ContractSnapshotTests',
        'HostCleanupCheckpointReceiptTests',
        'ReceiptScanDestinationIntegrationTests',
        'TestTimingOperationLabelTests'
    )
    # The A5 pair: the Linux-only census cases (CARD-1801).
    $fixedMethods = @(
        'ReleaseGatePlatformTests/C1686_LinuxOnlyCensus',
        'ReleaseGatePlatformTests/LinuxOnlyWindowsExclusionPolicy'
    )
    $wholeSubset = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $subsetMethods = @{}
    $fixedClassNames = New-StringList
    foreach ($fixedName in $fixedNames) { [void]$fixedClassNames.Add($fixedName) }
    foreach ($fixedMethod in $fixedMethods) { [void]$fixedClassNames.Add($fixedMethod.Substring(0, $fixedMethod.IndexOf('/'))) }
    foreach ($fixedName in $fixedClassNames) {
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
    }
    foreach ($fixedName in $fixedNames) { [void]$wholeSubset.Add($fixedName) }
    foreach ($fixedMethod in $fixedMethods) { Add-MethodEntry $fixedMethod $subsetMethods $null }
    $migrationsHit = $false
    foreach ($kind in $matched) {
        if ($kind.Id -eq 'migrations') { $migrationsHit = $true }
        if ($kind.Windows -eq 'only') { continue }
        Add-KindSelection $kind $wholeSubset $subsetMethods $null
    }
    Apply-Derived $derivedSelection $wholeSubset $subsetMethods
    $subsetRows = New-FilterRows $wholeSubset $subsetMethods 'CP-merge-subset-' $fixedNames
    $windowsRows = New-FilterRows $windowsWhole $windowsMethods 'CP-merge-windows-' $fixedNames
    $subsetTop = Get-TopFilter $subsetRows
    $fixedOut = New-StringList
    Fill-Sorted $fixedNames $fixedOut
    $fixedMethodOut = New-StringList
    Fill-Sorted $fixedMethods $fixedMethodOut
    $classOut = Get-ClassNames $wholeSubset $subsetMethods
    $subsetPayload = [pscustomobject]@{
        ok = $true
        mode = 'merge-subset'
        migrations = [bool]$migrationsHit
        kinds = $k
        fixedClasses = $fixedOut
        fixedMethods = $fixedMethodOut
        classes = $classOut
        derivedClasses = $derivedSelection.Derived
        keywordIds = (New-EmptyNameList)
        rosterIds = (New-EmptyNameList)
        skippedByCap = $derivedSelection.Skipped
        treenodeFilter = [string]$subsetTop
        filters = @((Convert-Rows $subsetRows $true).ToArray())
        windowsFilters = @((Convert-Rows $windowsRows $true).ToArray())
    }
    if ($Json) { Exit-Json $subsetPayload 0 }
    Write-Output 'mode=merge-subset'
    Write-Output ('migrations=' + $migrationsHit)
    Write-Output ('kinds=' + ($k -join ','))
    Write-Output ('fixedClasses=' + ($fixedOut -join ','))
    Write-Output ('fixedMethods=' + ($fixedMethodOut -join ','))
    Write-Output ('classes=' + ($classOut -join ','))
    Write-Output ('treenodeFilter=' + $subsetTop)
    foreach ($row in $subsetRows) { Write-Output ('filter=' + $row.Project + ' ' + $row.Filter) }
    foreach ($row in $windowsRows) { Write-Output ('windowsFilter=' + $row.Project + ' ' + $row.Filter) }
    exit 0
}

$whole = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$methodsByClass = @{}
$fileSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($kind in $matched) {
    if ($kind.Windows -eq 'only') { continue }
    Add-KindSelection $kind $whole $methodsByClass $fileSet
}
Apply-Derived $derivedSelection $whole $methodsByClass

$rows = New-FilterRows $whole $methodsByClass '' $null
$windowsRows = New-FilterRows $windowsWhole $windowsMethods '' $null
$top = Get-TopFilter $rows
$c = Get-ClassNames $whole $methodsByClass
$f = New-StringList
Fill-Sorted $fileSet $f
$payload = [pscustomobject]@{
    kinds = $k
    classes = $c
    files = $f
    derivedClasses = $derivedSelection.Derived
    keywordIds = (New-EmptyNameList)
    rosterIds = (New-EmptyNameList)
    skippedByCap = $derivedSelection.Skipped
    treenodeFilter = [string]$top
    filters = @((Convert-Rows $rows $false).ToArray())
    windowsFilters = @((Convert-Rows $windowsRows $false).ToArray())
}
if ($Json) { Exit-Json $payload 0 }
Write-Output ("kinds=" + ($k -join ','))
Write-Output ("treenodeFilter=" + $top)
foreach ($row in $windowsRows) { Write-Output ('windowsFilter=' + $row.Project + ' ' + $row.Filter) }
foreach ($relative in $f) { Write-Output $relative }
exit 0
