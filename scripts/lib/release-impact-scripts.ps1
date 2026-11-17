# CARD-1820 S1R5. Cross-language script closure over a supplied immutable inventory; never runs what it reads.
# Pure analysis: no Git, no checkout enumeration, no execution. Positive edges are additive; any
# dynamic, ambiguous, missing, unparsed, external, escaping or budget-cut closure keeps a
# path-specific reason. The result is always incomplete with fallback Full (S1R6 composes it).
# ASCII-only; runs under Windows PowerShell 5.1 and PowerShell 7.
Set-StrictMode -Version 2.0

$script:ReleaseImpactScriptSchema = 'antiphon-release-script-closure-v1'
$script:ReleaseImpactScriptAnchor = '|R|'
$script:ReleaseImpactScriptOutside = '|outside|'
$script:ReleaseImpactScriptExtensions = @('.ps1', '.psm1', '.psd1', '.sh', '.bash', '.js', '.mjs', '.cjs', '.py')
$script:ReleaseImpactScriptValueCap = 64
$script:ReleaseImpactScriptDepthCap = 24
$script:ReleaseImpactScriptDefaultVisits = 10000
# A reason detail longer than the cap keeps a fixed prefix plus a digest of the whole detail.
$script:ReleaseImpactScriptDetailCap = 160
$script:ReleaseImpactScriptDetailPrefix = 128
# Switch parameters of the commands read below; a switch never consumes the next element.
$script:ReleaseImpactScriptSwitches = @('force', 'global', 'passthru', 'disablenamechecking', 'noclobber', 'ascustomobject',
    'skipeditioncheck', 'usewindowspowershell', 'ignorewarnings', 'noprofile', 'nologo', 'noninteractive', 'noexit',
    'sta', 'mta', 'interactive', 'login', 'wait', 'nonewwindow')

# C# string literals, comments and char literals. A lexer hint only: a seed it misses is
# never a negative proof, because the result is never complete.
$script:ReleaseImpactCSharpLexer = [regex]::new(
    '(?<c>//[^\n]*|/\*[\s\S]*?\*/)' +
    '|(?<raw>\$*(?<q>"{3,})[\s\S]*?\k<q>)' +
    '|(?<verb>(?:\$+@|@\$+|@)"(?:[^"]|"")*")' +
    '|(?<reg>\$*"(?:[^"\\\n]|\\.)*")' +
    "|(?<ch>'(?:[^'\\\n]|\\.){1,8}')",
    [Text.RegularExpressions.RegexOptions]::CultureInvariant)

function Get-ReleaseImpactCSharpLiterals {
    param([string]$Text)
    $values = New-Object System.Collections.Generic.List[string]
    foreach ($match in $script:ReleaseImpactCSharpLexer.Matches($Text)) {
        $value = $null
        $interpolated = $false
        if ($match.Groups['raw'].Success) {
            $all = $match.Value
            $interpolated = $all.StartsWith('$')
            $quotes = $match.Groups['q'].Value.Length
            $start = $all.IndexOf('"')
            $value = $all.Substring($start + $quotes, $all.Length - $start - 2 * $quotes)
        }
        elseif ($match.Groups['verb'].Success) {
            $all = $match.Value
            $interpolated = $all.Contains('$')
            $start = $all.IndexOf('"')
            $value = $all.Substring($start + 1, $all.Length - $start - 2).Replace('""', '"')
        }
        elseif ($match.Groups['reg'].Success) {
            $all = $match.Value
            $interpolated = $all.StartsWith('$')
            $start = $all.IndexOf('"')
            $body = $all.Substring($start + 1, $all.Length - $start - 2)
            $value = [regex]::Replace($body, '\\(.)', {
                    param($m)
                    switch -CaseSensitive ($m.Groups[1].Value) { 'n' { "`n" } 't' { "`t" } 'r' { "`r" } '0' { ' ' } default { $m.Groups[1].Value } }
                })
        }
        if ($null -eq $value) { continue }
        if ($interpolated) {
            # A hole is unknown text. It separates tokens and never joins them into a path.
            $value = [regex]::Replace($value, '\{\{|\}\}', ' ')
            $value = [regex]::Replace($value, '\{[^{}]*\}', ' ')
        }
        $values.Add($value)
    }
    return , $values.ToArray()
}

function Get-ReleaseImpactScriptTokens {
    param([string]$Value)
    $tokens = New-Object System.Collections.Generic.List[string]
    $tokens.Add($Value)
    foreach ($match in [regex]::Matches($Value, "'([^'\r\n]+)'|""([^""\r\n]+)""")) {
        if ($match.Groups[1].Success) { $tokens.Add($match.Groups[1].Value) } else { $tokens.Add($match.Groups[2].Value) }
    }
    foreach ($piece in [regex]::Split($Value, '[\s()\[\];,=|&<>`''"]+')) {
        if ($piece.Length -gt 0) { $tokens.Add($piece) }
    }
    return , $tokens.ToArray()
}

function Get-ReleaseImpactScriptSorted {
    param($Values)
    $array = [string[]]@($Values)
    [Array]::Sort($array, [StringComparer]::Ordinal)
    return $array
}

function Get-ReleaseImpactScriptDirectory {
    param([string]$Path)
    $index = $Path.LastIndexOf('/')
    if ($index -lt 0) { return '' }
    return $Path.Substring(0, $index)
}

function Get-ReleaseImpactScriptLeaf {
    param([string]$Path)
    $index = $Path.LastIndexOf('/')
    if ($index -lt 0) { return $Path }
    return $Path.Substring($index + 1)
}

function Test-ReleaseImpactScriptExtension {
    param([string]$Path)
    $leaf = (Get-ReleaseImpactScriptLeaf $Path).ToLowerInvariant()
    foreach ($extension in $script:ReleaseImpactScriptExtensions) { if ($leaf.EndsWith($extension)) { return $true } }
    return $false
}

# Classifies an evaluated string: anchored repo path, escaping/absolute, or unanchored text.
function ConvertTo-ReleaseImpactScriptPath {
    param([string]$Value)
    $text = $Value.Replace([char]92, '/')
    if ($text.StartsWith($script:ReleaseImpactScriptOutside)) { return @{ Kind = 'outside'; Path = $text } }
    if ($text.StartsWith($script:ReleaseImpactScriptAnchor)) {
        $rest = $text.Substring($script:ReleaseImpactScriptAnchor.Length)
        if ($rest.Contains($script:ReleaseImpactScriptAnchor) -or $rest.Contains($script:ReleaseImpactScriptOutside)) {
            return @{ Kind = 'unknown'; Path = $text }
        }
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($segment in $rest.Split('/')) {
            if ($segment -eq '' -or $segment -eq '.') { continue }
            if ($segment -eq '..') {
                if ($parts.Count -eq 0) { return @{ Kind = 'outside'; Path = $text } }
                $parts.RemoveAt($parts.Count - 1)
                continue
            }
            $parts.Add($segment)
        }
        return @{ Kind = 'anchored'; Path = ($parts -join '/') }
    }
    if ($text.Contains($script:ReleaseImpactScriptAnchor)) { return @{ Kind = 'unknown'; Path = $text } }
    if ($text.StartsWith('/') -or $text.StartsWith('~') -or $text -match '^[A-Za-z]:') { return @{ Kind = 'outside'; Path = $text } }
    return @{ Kind = 'relative'; Path = $text }
}

function Join-ReleaseImpactScriptValue {
    param([string]$Left, [string]$Right)
    $right = $Right.Replace([char]92, '/')
    if ($right.StartsWith($script:ReleaseImpactScriptAnchor) -or $right.StartsWith($script:ReleaseImpactScriptOutside)) { return $right }
    $left = $Left.Replace([char]92, '/')
    if ($left.EndsWith('/') -or $left.EndsWith($script:ReleaseImpactScriptAnchor)) { return $left + $right.TrimStart('/') }
    return $left + '/' + $right.TrimStart('/')
}

function Get-ReleaseImpactScriptParent {
    param([string]$Value)
    $path = ConvertTo-ReleaseImpactScriptPath $Value
    if ($path.Kind -ne 'anchored') { return $null }
    if ($path.Path -eq '') { return $script:ReleaseImpactScriptOutside }
    return $script:ReleaseImpactScriptAnchor + (Get-ReleaseImpactScriptDirectory $path.Path)
}

function Join-ReleaseImpactScriptProduct {
    param($Left, $Right, [scriptblock]$Combine)
    if ($null -eq $Left -or $null -eq $Right) { return $null }
    $values = New-Object System.Collections.Generic.List[string]
    foreach ($a in $Left.V) {
        foreach ($b in $Right.V) {
            $values.Add([string](& $Combine $a $b))
            if ($values.Count -gt $script:ReleaseImpactScriptValueCap) { return $null }
        }
    }
    return @{ V = $values.ToArray() }
}

function Add-ReleaseImpactScriptValues {
    param($Target, $Values)
    if ($null -eq $Values) { return $false }
    foreach ($value in $Values.V) { $Target.Add([string]$value) }
    return ($Target.Count -le $script:ReleaseImpactScriptValueCap)
}

function New-ReleaseImpactScriptContext {
    param([string]$Path, $Nodes)
    $assignments = @{}
    $parameters = @{}
    foreach ($node in $Nodes) {
        if ($node -is [System.Management.Automation.Language.ParameterAst]) { $parameters[(Get-ReleaseImpactScriptVariableName $node.Name)] = $true; continue }
        if ($node -isnot [System.Management.Automation.Language.AssignmentStatementAst]) { continue }
        $left = $node.Left
        if ($left -is [System.Management.Automation.Language.ConvertExpressionAst]) { $left = $left.Child }
        if ($left -isnot [System.Management.Automation.Language.VariableExpressionAst]) { continue }
        $name = Get-ReleaseImpactScriptVariableName $left
        if (-not $assignments.ContainsKey($name)) { $assignments[$name] = New-Object System.Collections.Generic.List[object] }
        $assignments[$name].Add($node)
    }
    return @{
        Path = $Path; Directory = (Get-ReleaseImpactScriptDirectory $Path)
        Assignments = $assignments; Parameters = $parameters; Resolving = @{}; Depth = 0
        Memo = @{}; Loops = (New-Object 'System.Collections.Generic.Dictionary[object,object]')
    }
}

function Get-ReleaseImpactScriptVariableName {
    param($Variable)
    $name = $Variable.VariablePath.UserPath
    foreach ($scope in @('script:', 'global:', 'local:', 'private:')) {
        if ($name.StartsWith($scope, [StringComparison]::OrdinalIgnoreCase)) { $name = $name.Substring($scope.Length) }
    }
    return $name.ToLowerInvariant()
}

function Resolve-ReleaseImpactScriptVariable {
    param($Context, $Variable)
    $name = Get-ReleaseImpactScriptVariableName $Variable
    if ($name -eq 'psscriptroot') { return @{ V = @($script:ReleaseImpactScriptAnchor + $Context.Directory) } }
    if ($name -eq 'pscommandpath') { return @{ V = @($script:ReleaseImpactScriptAnchor + $Context.Path) } }
    # A constant foreach binds its loop variable to the bounded array it walks.
    $parent = $Variable.Parent
    while ($null -ne $parent) {
        if ($parent -is [System.Management.Automation.Language.ForEachStatementAst] -and
            (Get-ReleaseImpactScriptVariableName $parent.Variable) -eq $name -and -not [object]::ReferenceEquals($parent.Variable, $Variable)) {
            if (-not $Context.Loops.ContainsKey($parent)) { $Context.Loops[$parent] = Get-ReleaseImpactScriptValue $Context $parent.Condition }
            return $Context.Loops[$parent]
        }
        $parent = $parent.Parent
    }
    if ($Context.Parameters.ContainsKey($name) -or -not $Context.Assignments.ContainsKey($name) -or $Context.Resolving.ContainsKey($name)) { return $null }
    if ($Context.Memo.ContainsKey($name)) { return $Context.Memo[$name] }
    $Context.Resolving[$name] = $true
    $result = $null
    try {
        $values = New-Object System.Collections.Generic.List[string]
        $known = $true
        foreach ($assignment in $Context.Assignments[$name]) {
            if ($assignment.Operator -ne [System.Management.Automation.Language.TokenKind]::Equals -or
                -not (Add-ReleaseImpactScriptValues $values (Get-ReleaseImpactScriptValue $Context $assignment.Right))) { $known = $false; break }
        }
        if ($known) { $result = @{ V = $values.ToArray() } }
    }
    finally { $Context.Resolving.Remove($name) }
    # Every assignment in the file is unioned; one unknown assignment makes the variable unknown.
    $Context.Memo[$name] = $result
    return $result
}

# A composite path expression is evaluated once, at its outermost composing node.
function Test-ReleaseImpactScriptOutermost {
    param($Node)
    $parent = $Node.Parent
    while ($parent -is [System.Management.Automation.Language.ParenExpressionAst] -or $parent -is [System.Management.Automation.Language.PipelineAst] -or
        $parent -is [System.Management.Automation.Language.CommandExpressionAst] -or $parent -is [System.Management.Automation.Language.ArrayLiteralAst]) { $parent = $parent.Parent }
    if ($parent -is [System.Management.Automation.Language.BinaryExpressionAst] -or $parent -is [System.Management.Automation.Language.ExpandableStringExpressionAst] -or
        $parent -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -or $parent -is [System.Management.Automation.Language.ConvertExpressionAst]) { return $false }
    if ($parent -is [System.Management.Automation.Language.CommandAst]) {
        $name = $parent.GetCommandName()
        if ($null -ne $name -and ($name -eq 'Join-Path' -or $name -eq 'Split-Path')) { return $false }
    }
    return $true
}

function Get-ReleaseImpactScriptArguments {
    param($Command, [string[]]$Switches = @())
    $named = @{}
    $positional = New-Object System.Collections.Generic.List[object]
    $elements = $Command.CommandElements
    for ($i = 1; $i -lt $elements.Count; $i++) {
        $element = $elements[$i]
        if ($element -is [System.Management.Automation.Language.CommandParameterAst]) {
            $key = $element.ParameterName.ToLowerInvariant()
            $switch = $false
            foreach ($candidate in $Switches) { if ($candidate.StartsWith($key)) { $switch = $true; break } }
            if ($null -ne $element.Argument) { $named[$key] = $element.Argument }
            elseif (-not $switch -and $i + 1 -lt $elements.Count -and $elements[$i + 1] -isnot [System.Management.Automation.Language.CommandParameterAst]) {
                $named[$key] = $elements[$i + 1]; $i++
            }
            else { $named[$key] = $true }
            continue
        }
        $positional.Add($element)
    }
    return @{ Named = $named; Positional = $positional }
}

function Find-ReleaseImpactScriptArgument {
    param($Arguments, [string[]]$Names, [int]$Position)
    foreach ($name in $Names) {
        foreach ($key in $Arguments.Named.Keys) {
            if ($name.StartsWith($key) -and $Arguments.Named[$key] -isnot [bool]) { return $Arguments.Named[$key] }
        }
    }
    if ($Position -ge 0 -and $Position -lt $Arguments.Positional.Count) { return $Arguments.Positional[$Position] }
    return $null
}

function Get-ReleaseImpactScriptCommandValue {
    param($Context, $Command)
    $name = $Command.GetCommandName()
    if ($null -eq $name -or $Command.InvocationOperator -ne [System.Management.Automation.Language.TokenKind]::Unknown) { return $null }
    $arguments = Get-ReleaseImpactScriptArguments $Command @('resolve', 'parent', 'leaf', 'leafbase', 'extension', 'qualifier', 'noqualifier', 'isabsolute')
    switch ($name.ToLowerInvariant()) {
        'join-path' {
            $left = Find-ReleaseImpactScriptArgument $arguments @('path') 0
            $right = Find-ReleaseImpactScriptArgument $arguments @('childpath') 1
            if ($null -eq $left -or $null -eq $right -or $arguments.Positional.Count -gt 2) { return $null }
            return (Join-ReleaseImpactScriptProduct (Get-ReleaseImpactScriptValue $Context $left) (Get-ReleaseImpactScriptValue $Context $right) {
                    param($a, $b) Join-ReleaseImpactScriptValue $a $b })
        }
        'split-path' {
            if ($arguments.Named.ContainsKey('leaf') -or $arguments.Named.ContainsKey('qualifier') -or $arguments.Named.ContainsKey('noqualifier')) { return $null }
            $target = Find-ReleaseImpactScriptArgument $arguments @('path', 'literalpath') 0
            if ($null -eq $target) { return $null }
            $values = Get-ReleaseImpactScriptValue $Context $target
            if ($null -eq $values) { return $null }
            $parents = New-Object System.Collections.Generic.List[string]
            foreach ($value in $values.V) {
                $parent = Get-ReleaseImpactScriptParent $value
                if ($null -eq $parent) { return $null }
                $parents.Add($parent)
            }
            return @{ V = $parents.ToArray() }
        }
    }
    return $null
}

function Get-ReleaseImpactScriptValue {
    param($Context, $Ast)
    if ($null -eq $Ast) { return $null }
    if ($Context.Depth -ge $script:ReleaseImpactScriptDepthCap) { return $null }
    $Context.Depth++
    try { return (Get-ReleaseImpactScriptValueCore $Context $Ast) }
    finally { $Context.Depth-- }
}

function Get-ReleaseImpactScriptValueCore {
    param($Context, $Ast)
    if ($Ast -is [System.Management.Automation.Language.StringConstantExpressionAst]) { return @{ V = @([string]$Ast.Value) } }
    if ($Ast -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
        $text = $Ast.Extent.Text
        if ($Ast.NestedExpressions.Count -eq 0) { return @{ V = @([string]$Ast.Value) } }
        if ($text.StartsWith('@')) { return $null }
        $quoted = $text.StartsWith('"')
        $offset = $Ast.Extent.StartOffset
        $cursor = 0
        if ($quoted) { $cursor = 1 }
        $values = @{ V = @('') }
        foreach ($nested in @($Ast.NestedExpressions | Sort-Object { $_.Extent.StartOffset })) {
            $start = $nested.Extent.StartOffset - $offset
            $literal = $text.Substring($cursor, $start - $cursor)
            if ($literal.Contains('`')) { return $null }
            $values = Join-ReleaseImpactScriptProduct $values @{ V = @($literal) } { param($a, $b) $a + $b }
            $values = Join-ReleaseImpactScriptProduct $values (Get-ReleaseImpactScriptValue $Context $nested) { param($a, $b) $a + $b }
            if ($null -eq $values) { return $null }
            $cursor = $nested.Extent.EndOffset - $offset
        }
        $end = $text.Length
        if ($quoted) { $end-- }
        $tail = $text.Substring($cursor, $end - $cursor)
        if ($tail.Contains('`')) { return $null }
        return (Join-ReleaseImpactScriptProduct $values @{ V = @($tail) } { param($a, $b) $a + $b })
    }
    if ($Ast -is [System.Management.Automation.Language.VariableExpressionAst]) { return (Resolve-ReleaseImpactScriptVariable $Context $Ast) }
    if ($Ast -is [System.Management.Automation.Language.ParenExpressionAst]) { return (Get-ReleaseImpactScriptValue $Context $Ast.Pipeline) }
    if ($Ast -is [System.Management.Automation.Language.ConvertExpressionAst]) { return (Get-ReleaseImpactScriptValue $Context $Ast.Child) }
    if ($Ast -is [System.Management.Automation.Language.CommandExpressionAst]) { return (Get-ReleaseImpactScriptValue $Context $Ast.Expression) }
    if ($Ast -is [System.Management.Automation.Language.PipelineAst]) {
        if ($Ast.PipelineElements.Count -ne 1) { return $null }
        return (Get-ReleaseImpactScriptValue $Context $Ast.PipelineElements[0])
    }
    if ($Ast -is [System.Management.Automation.Language.CommandAst]) { return (Get-ReleaseImpactScriptCommandValue $Context $Ast) }
    if ($Ast -is [System.Management.Automation.Language.ArrayLiteralAst]) {
        $values = New-Object System.Collections.Generic.List[string]
        foreach ($element in $Ast.Elements) { if (-not (Add-ReleaseImpactScriptValues $values (Get-ReleaseImpactScriptValue $Context $element))) { return $null } }
        return @{ V = $values.ToArray() }
    }
    if ($Ast -is [System.Management.Automation.Language.ArrayExpressionAst] -or $Ast -is [System.Management.Automation.Language.SubExpressionAst]) {
        $block = $Ast.SubExpression
        if ($null -ne $block.Traps -and @($block.Traps).Count -gt 0) { return $null }
        $values = New-Object System.Collections.Generic.List[string]
        foreach ($statement in $block.Statements) { if (-not (Add-ReleaseImpactScriptValues $values (Get-ReleaseImpactScriptValue $Context $statement))) { return $null } }
        return @{ V = $values.ToArray() }
    }
    if ($Ast -is [System.Management.Automation.Language.BinaryExpressionAst]) {
        if ($Ast.Operator -ne [System.Management.Automation.Language.TokenKind]::Plus) { return $null }
        return (Join-ReleaseImpactScriptProduct (Get-ReleaseImpactScriptValue $Context $Ast.Left) (Get-ReleaseImpactScriptValue $Context $Ast.Right) { param($a, $b) $a + $b })
    }
    if ($Ast -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $Ast.Static -and
        $Ast.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
        $Ast.Expression.TypeName.FullName -match '^(System\.)?IO\.Path$' -and $Ast.Member -is [System.Management.Automation.Language.StringConstantExpressionAst]) {
        $member = $Ast.Member.Value.ToLowerInvariant()
        $arguments = @($Ast.Arguments)
        if ($member -eq 'combine' -and $arguments.Count -ge 2) {
            $values = Get-ReleaseImpactScriptValue $Context $arguments[0]
            for ($i = 1; $i -lt $arguments.Count; $i++) {
                $values = Join-ReleaseImpactScriptProduct $values (Get-ReleaseImpactScriptValue $Context $arguments[$i]) { param($a, $b) Join-ReleaseImpactScriptValue $a $b }
            }
            return $values
        }
        if ($member -eq 'getdirectoryname' -and $arguments.Count -eq 1) {
            $values = Get-ReleaseImpactScriptValue $Context $arguments[0]
            if ($null -eq $values) { return $null }
            $parents = New-Object System.Collections.Generic.List[string]
            foreach ($value in $values.V) {
                $parent = Get-ReleaseImpactScriptParent $value
                if ($null -eq $parent) { return $null }
                $parents.Add($parent)
            }
            return @{ V = $parents.ToArray() }
        }
    }
    return $null
}

function New-ReleaseImpactScriptGraph {
    param($Files)
    $directories = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $projects = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $leaves = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    # Case-folded views exist only to name a casing miss; they never resolve a target silently.
    $foldedFiles = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
    $foldedLeaves = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Files.Keys) {
        $directory = Get-ReleaseImpactScriptDirectory $path
        if ($path.EndsWith('.csproj')) { [void]$projects.Add($directory) }
        while ($directory -ne '') {
            if (-not $directories.Add($directory)) { break }
            $directory = Get-ReleaseImpactScriptDirectory $directory
        }
        $leaf = Get-ReleaseImpactScriptLeaf $path
        if (-not $leaves.ContainsKey($leaf)) { $leaves[$leaf] = New-Object System.Collections.Generic.List[string] }
        $leaves[$leaf].Add($path)
        if (-not $foldedFiles.ContainsKey($path)) { $foldedFiles[$path] = New-Object System.Collections.Generic.List[string] }
        $foldedFiles[$path].Add($path)
        if (-not $foldedLeaves.ContainsKey($leaf)) { $foldedLeaves[$leaf] = New-Object System.Collections.Generic.List[string] }
        $foldedLeaves[$leaf].Add($path)
    }
    return @{
        Files = $Files; Directories = $directories; Projects = $projects; Leaves = $leaves
        FoldedFiles = $foldedFiles; FoldedLeaves = $foldedLeaves
        Reasons = (New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal))
        Dependencies = (New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal))
    }
}

function Add-ReleaseImpactScriptReason {
    param($Graph, [string]$Caller, [string]$Detail)
    if ($Detail.Length -gt $script:ReleaseImpactScriptDetailCap) {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Detail)) } finally { $sha.Dispose() }
        $hex = -join @($bytes | ForEach-Object { $_.ToString('x2') })
        $Detail = $Detail.Substring(0, $script:ReleaseImpactScriptDetailPrefix) + '~' + $hex.Substring(0, 16)
    }
    [void]$Graph.Reasons.Add('script-target-unresolved:' + $Caller + ':' + $Detail)
}

# A project directory stands for every input under it except build outputs. Compile items outside
# it come from the project file itself; MSBuild evaluation is never claimed.
function Get-ReleaseImpactScriptProjectFiles {
    param($Graph, [string]$Directory)
    $prefix = $Directory + '/'
    $files = New-Object System.Collections.Generic.List[string]
    foreach ($path in $Graph.Files.Keys) {
        if (-not $path.StartsWith($prefix, [StringComparison]::Ordinal)) { continue }
        $rest = $path.Substring($prefix.Length)
        if ($rest.StartsWith('bin/') -or $rest.StartsWith('obj/') -or $rest.Contains('/bin/') -or $rest.Contains('/obj/')) { continue }
        $files.Add($path)
    }
    return , $files.ToArray()
}

# Records a dependency target and returns whether it reached an inventory path.
function Add-ReleaseImpactScriptTarget {
    param($Graph, $Targets, [string]$Caller, [string]$Value, [string]$Kind, [bool]$Required)
    $path = ConvertTo-ReleaseImpactScriptPath $Value
    switch ($path.Kind) {
        'outside' { if ($Required) { Add-ReleaseImpactScriptReason $Graph $Caller 'outside' }; return $false }
        'unknown' { if ($Required) { Add-ReleaseImpactScriptReason $Graph $Caller 'dynamic' }; return $false }
        'anchored' {
            if ($Graph.Files.ContainsKey($path.Path)) { $Targets[$path.Path] = $Kind; return $true }
            if ($Graph.Projects.Contains($path.Path)) {
                foreach ($file in (Get-ReleaseImpactScriptProjectFiles $Graph $path.Path)) { $Targets[$file] = $Kind }
                return $true
            }
            if ($Required) { Add-ReleaseImpactScriptReason $Graph $Caller ('missing:' + $path.Path) }
            return $false
        }
    }
    # Unanchored text: a bare command or module name is not a path here.
    $text = $path.Path
    if (-not $text.Contains('/') -and -not (Test-ReleaseImpactScriptExtension $text)) {
        if ($Required -and $Kind -eq 'import') { Add-ReleaseImpactScriptReason $Graph $Caller ('external:' + $text) }
        return $false
    }
    if (-not $Required) {
        $relative = ConvertTo-ReleaseImpactScriptPath ($script:ReleaseImpactScriptAnchor + $text)
        if ($relative.Kind -eq 'anchored' -and $Graph.Files.ContainsKey($relative.Path)) { $Targets[$relative.Path] = $Kind; return $true }
        return $false
    }
    # The working directory is not proven. Both plausible bases are positives; the reason stays.
    Add-ReleaseImpactScriptReason $Graph $Caller ('cwd:' + $text)
    $found = $false
    $callerDirectory = Get-ReleaseImpactScriptDirectory $Caller
    foreach ($base in @('', $callerDirectory)) {
        $candidate = ConvertTo-ReleaseImpactScriptPath ($script:ReleaseImpactScriptAnchor + $base + '/' + $text)
        if ($candidate.Kind -eq 'anchored' -and $Graph.Files.ContainsKey($candidate.Path)) { $Targets[$candidate.Path] = $Kind; $found = $true }
    }
    return $found
}

function Add-ReleaseImpactScriptRequired {
    param($Graph, $Targets, $Context, $Ast, [string]$Kind)
    if ($null -eq $Ast) { Add-ReleaseImpactScriptReason $Graph $Context.Path 'dynamic'; return }
    if ($Ast -is [System.Management.Automation.Language.ScriptBlockExpressionAst]) { return }
    $values = Get-ReleaseImpactScriptValue $Context $Ast
    if ($null -eq $values) { Add-ReleaseImpactScriptReason $Graph $Context.Path 'dynamic'; return }
    foreach ($value in $values.V) { [void](Add-ReleaseImpactScriptTarget $Graph $Targets $Context.Path $value $Kind $true) }
}

function Get-ReleaseImpactPowerShellDependencies {
    param($Graph, [string]$Path, [string]$Text, $Targets)
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, $Path, [ref]$tokens, [ref]$errors)
    if ($null -ne $errors -and @($errors).Count -gt 0) { Add-ReleaseImpactScriptReason $Graph $Path 'parse' }
    $nodes = $ast.FindAll({ $true }, $true)
    $context = New-ReleaseImpactScriptContext $Path $nodes
    foreach ($command in $nodes) {
        if ($command -isnot [System.Management.Automation.Language.CommandAst]) { continue }
        $operator = $command.InvocationOperator
        $first = $command.CommandElements[0]
        if ($operator -eq [System.Management.Automation.Language.TokenKind]::Dot -or $operator -eq [System.Management.Automation.Language.TokenKind]::Ampersand) {
            $kind = 'invoke'
            if ($operator -eq [System.Management.Automation.Language.TokenKind]::Dot) { $kind = 'dotSource' }
            if ($first -is [System.Management.Automation.Language.ScriptBlockExpressionAst]) { continue }
            if ($first -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
                -not $first.Value.Contains('/') -and -not $first.Value.Contains([string][char]92) -and -not (Test-ReleaseImpactScriptExtension $first.Value)) { continue }
            Add-ReleaseImpactScriptRequired $Graph $Targets $context $first $kind
            continue
        }
        $name = $command.GetCommandName()
        if ($null -eq $name) { continue }
        $lower = $name.ToLowerInvariant()
        # Module-qualified command names (Module\Command) are commands, not paths.
        if ($lower -match '^[a-z][a-z0-9_.]*\\[a-z][a-z0-9_-]*$') { continue }
        if ($lower.Contains('/') -or $lower.Contains([string][char]92) -or ($lower.Contains('.') -and (Test-ReleaseImpactScriptExtension $lower))) {
            Add-ReleaseImpactScriptRequired $Graph $Targets $context $first 'invoke'
            continue
        }
        if ($lower -notmatch '^(import-module|ipmo|invoke-expression|iex|add-type|pwsh|pwsh\.exe|powershell|powershell\.exe|bash|sh|node|python|python3)$') { continue }
        $arguments = Get-ReleaseImpactScriptArguments $command $script:ReleaseImpactScriptSwitches
        switch -Regex ($lower) {
            '^(import-module|ipmo)$' {
                Add-ReleaseImpactScriptRequired $Graph $Targets $context (Find-ReleaseImpactScriptArgument $arguments @('name', 'path') 0) 'import'
            }
            '^(invoke-expression|iex)$' { Add-ReleaseImpactScriptReason $Graph $Path 'dynamic' }
            '^add-type$' {
                $source = Find-ReleaseImpactScriptArgument $arguments @('path', 'literalpath') -1
                if ($null -ne $source) { Add-ReleaseImpactScriptRequired $Graph $Targets $context $source 'compile'; break }
                $definition = Find-ReleaseImpactScriptArgument $arguments @('typedefinition', 'memberdefinition') 0
                if ($null -eq $definition -or $definition -is [System.Management.Automation.Language.StringConstantExpressionAst]) { break }
                if ($definition -is [System.Management.Automation.Language.ExpandableStringExpressionAst] -and $definition.NestedExpressions.Count -eq 0) { break }
                # Inline source read from a resolved file is a positive; anything else is unproven.
                $before = $Targets.Count
                foreach ($node in $definition.FindAll({ param($n) $true }, $true)) {
                    $values = Get-ReleaseImpactScriptValue $context $node
                    if ($null -ne $values) { foreach ($value in $values.V) { [void](Add-ReleaseImpactScriptTarget $Graph $Targets $Path $value 'compile' $false) } }
                }
                if ($Targets.Count -eq $before) { Add-ReleaseImpactScriptReason $Graph $Path 'compile' }
            }
            '^(pwsh|pwsh\.exe|powershell|powershell\.exe)$' {
                $file = Find-ReleaseImpactScriptArgument $arguments @('file') -1
                if ($null -ne $file) { Add-ReleaseImpactScriptRequired $Graph $Targets $context $file 'process'; break }
                $code = Find-ReleaseImpactScriptArgument $arguments @('command', 'encodedcommand') -1
                if ($null -ne $code -and $code -isnot [System.Management.Automation.Language.ScriptBlockExpressionAst] -and
                    $code -isnot [System.Management.Automation.Language.StringConstantExpressionAst]) { Add-ReleaseImpactScriptReason $Graph $Path 'dynamic' }
            }
            '^(bash|sh|node|python|python3)$' {
                foreach ($argument in $arguments.Positional) {
                    $values = Get-ReleaseImpactScriptValue $context $argument
                    if ($null -eq $values) { Add-ReleaseImpactScriptReason $Graph $Path 'dynamic'; break }
                    $value = [string]$values.V[0]
                    if ($values.V.Count -eq 1 -and $value.StartsWith('-')) { continue }
                    foreach ($each in $values.V) { [void](Add-ReleaseImpactScriptTarget $Graph $Targets $Path $each 'process' $true) }
                    break
                }
            }
        }
    }
    # Any constant path expression elsewhere (reads, compiled helper projects, tool inputs) is a positive.
    foreach ($node in $nodes) {
        if ($node -is [System.Management.Automation.Language.StringConstantExpressionAst]) {
            $value = ([string]$node.Value).Replace([char]92, '/')
            if ($value.Contains('/') -and $Graph.Files.ContainsKey($value)) { $Targets[$value] = 'reference' }
            continue
        }
        if ($node -is [System.Management.Automation.Language.CommandAst]) {
            $name = $node.GetCommandName()
            if ($null -eq $name -or ($name -ne 'Join-Path' -and $name -ne 'Split-Path')) { continue }
        }
        elseif ($node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) { if ($node.NestedExpressions.Count -eq 0) { continue } }
        elseif ($node -is [System.Management.Automation.Language.BinaryExpressionAst]) { if ($node.Operator -ne [System.Management.Automation.Language.TokenKind]::Plus) { continue } }
        elseif ($node -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst] -or -not $node.Static) { continue }
        # Only variables can carry an anchored root into an expression.
        if (-not $node.Extent.Text.Contains('$') -or -not (Test-ReleaseImpactScriptOutermost $node)) { continue }
        $values = Get-ReleaseImpactScriptValue $context $node
        if ($null -eq $values) { continue }
        foreach ($value in $values.V) {
            if ((ConvertTo-ReleaseImpactScriptPath $value).Kind -eq 'anchored') { [void](Add-ReleaseImpactScriptTarget $Graph $Targets $Path $value 'reference' $false) }
        }
    }
}

function Get-ReleaseImpactProjectFileDependencies {
    param($Graph, [string]$Path, [string]$Text, $Targets)
    $directory = Get-ReleaseImpactScriptDirectory $Path
    foreach ($file in (Get-ReleaseImpactScriptProjectFiles $Graph $directory)) { if ($file -ne $Path) { $Targets[$file] = 'project' } }
    $items = '<(?:Compile|Content|None|EmbeddedResource|ProjectReference|Import)\b[^>]*?\b(?:Include|Project)\s*=\s*"([^"]*)"'
    foreach ($match in [regex]::Matches($Text, $items)) {
        $value = $match.Groups[1].Value
        if ($value -match '[*?$%@]' -or $value.Contains(';')) { Add-ReleaseImpactScriptReason $Graph $Path 'project-input'; continue }
        [void](Add-ReleaseImpactScriptTarget $Graph $Targets $Path ($script:ReleaseImpactScriptAnchor + $directory + '/' + $value) 'project' $true)
    }
    # SDK, Directory.Build and property evaluation are not performed here.
    Add-ReleaseImpactScriptReason $Graph $Path 'project-evaluation'
}

function Get-ReleaseImpactScriptDependencies {
    param($Graph, [string]$Path)
    if ($Graph.Dependencies.ContainsKey($Path)) { return $Graph.Dependencies[$Path] }
    $targets = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    $text = $Graph.Files[$Path]
    $lower = $Path.ToLowerInvariant()
    if ($null -eq $text) {
        if ((Test-ReleaseImpactScriptExtension $lower) -or $lower.EndsWith('.csproj')) { Add-ReleaseImpactScriptReason $Graph $Path 'content-unavailable' }
    }
    elseif ($lower.EndsWith('.ps1') -or $lower.EndsWith('.psm1')) {
        Get-ReleaseImpactPowerShellDependencies $Graph $Path ([string]$text) $targets
    }
    elseif ($lower.EndsWith('.csproj')) {
        Get-ReleaseImpactProjectFileDependencies $Graph $Path ([string]$text) $targets
    }
    elseif (Test-ReleaseImpactScriptExtension $lower) {
        # Other languages: exact repo-relative tokens are positives; their closure is not proven.
        foreach ($token in (Get-ReleaseImpactScriptTokens ([string]$text))) {
            $candidate = $token.Replace([char]92, '/')
            if ($candidate -ne $Path -and $Graph.Files.ContainsKey($candidate)) { $targets[$candidate] = 'reference' }
        }
        Add-ReleaseImpactScriptReason $Graph $Path 'language'
    }
    [void]$targets.Remove($Path)
    $ordered = @(Get-ReleaseImpactScriptSorted $targets.Keys | ForEach-Object { @{ Path = $_; Kind = $targets[$_] } })
    $Graph.Dependencies[$Path] = $ordered
    return $ordered
}

# A path-shaped seed token: its leaf names a script, or it walks a '..' segment. A token with
# whitespace is covered by its split pieces; prose, filters and bare words are not paths.
function Test-ReleaseImpactScriptSeedShape {
    param([string]$Token)
    if ($Token -match '\s') { return $false }
    if (Test-ReleaseImpactScriptExtension $Token) { return $true }
    return ($Token.Contains('/') -and ('/' + $Token + '/').Contains('/../'))
}

# Classifies a path-shaped seed token that missed an exact inventory key; it never drops one.
# C# seed literals are repository-relative (the exact-key convention), so '.' and '..' resolve
# lexically against the repository root. Nothing is read, followed through a link or guessed.
function Add-ReleaseImpactScriptSeedPath {
    param($Graph, $Seeds, [string]$Caller, [string]$Token, [string]$Kind)
    if ($Token -match '[*?$%{}]') { Add-ReleaseImpactScriptReason $Graph $Caller 'dynamic'; return }
    if ($Token -match '^[A-Za-z][A-Za-z0-9+.-]*:') { Add-ReleaseImpactScriptReason $Graph $Caller 'outside'; return }
    $raw = ConvertTo-ReleaseImpactScriptPath $Token
    if ($raw.Kind -eq 'outside') { Add-ReleaseImpactScriptReason $Graph $Caller 'outside'; return }
    if ($raw.Kind -ne 'relative') { Add-ReleaseImpactScriptReason $Graph $Caller 'dynamic'; return }
    $normal = ConvertTo-ReleaseImpactScriptPath ($script:ReleaseImpactScriptAnchor + $raw.Path)
    if ($normal.Kind -eq 'outside') { Add-ReleaseImpactScriptReason $Graph $Caller 'outside'; return }
    if ($normal.Kind -ne 'anchored') { Add-ReleaseImpactScriptReason $Graph $Caller 'dynamic'; return }
    $target = $normal.Path
    if ($Graph.Files.ContainsKey($target)) {
        if (-not $Seeds.ContainsKey($target)) { $Seeds[$target] = $Kind }
        return
    }
    # An ordinal miss: a casing-only match is named, never silently picked; every match stays a positive.
    $found = @()
    if (-not $Token.Contains('/')) { if ($Graph.FoldedLeaves.ContainsKey($target)) { $found = @($Graph.FoldedLeaves[$target]) } }
    elseif ($Graph.FoldedFiles.ContainsKey($target)) { $found = @($Graph.FoldedFiles[$target]) }
    if ($found.Count -eq 0) { Add-ReleaseImpactScriptReason $Graph $Caller ('missing:' + $target); return }
    if ($found.Count -gt 1) { Add-ReleaseImpactScriptReason $Graph $Caller ('ambiguous:' + $target) }
    else { Add-ReleaseImpactScriptReason $Graph $Caller ('case:' + $target) }
    foreach ($match in $found) { if (-not $Seeds.ContainsKey($match)) { $Seeds[$match] = $Kind } }
}

# Seeds from a consumer's C# declaration: whole-literal paths, quoted/embedded command paths, and
# script basenames (the ScriptHarness convention) resolved against the supplied inventory only.
function Get-ReleaseImpactScriptSeeds {
    param($Graph, [string]$DeclarationPath)
    $seeds = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    $text = $Graph.Files[$DeclarationPath]
    if ($null -eq $text) { return $seeds }
    foreach ($literal in (Get-ReleaseImpactCSharpLiterals ([string]$text))) {
        foreach ($token in (Get-ReleaseImpactScriptTokens $literal)) {
            $candidate = $token.Replace([char]92, '/')
            if ($candidate.StartsWith('./')) { $candidate = $candidate.Substring(2) }
            if ($candidate.Length -eq 0 -or $candidate -eq $DeclarationPath) { continue }
            if ($Graph.Files.ContainsKey($candidate)) {
                if (-not $seeds.ContainsKey($candidate)) {
                    $kind = 'script:embedded'
                    if ($token -eq $literal) { $kind = 'script:path' }
                    $seeds[$candidate] = $kind
                }
                continue
            }
            $basename = -not $candidate.Contains('/')
            if ($basename -and (Test-ReleaseImpactScriptExtension $candidate) -and $Graph.Leaves.ContainsKey($candidate)) {
                $found = @($Graph.Leaves[$candidate])
                if ($found.Count -gt 1) { Add-ReleaseImpactScriptReason $Graph $DeclarationPath ('ambiguous:' + $candidate) }
                foreach ($match in $found) { if (-not $seeds.ContainsKey($match)) { $seeds[$match] = 'script:basename' } }
                continue
            }
            if (-not (Test-ReleaseImpactScriptSeedShape $candidate)) { continue }
            $kind = 'script:embedded'
            if ($basename) { $kind = 'script:basename' } elseif ($token -eq $literal) { $kind = 'script:path' }
            Add-ReleaseImpactScriptSeedPath $Graph $seeds $DeclarationPath $candidate $kind
        }
    }
    return $seeds
}

function Get-ReleaseImpactScriptClosure {
    param([Parameter(Mandatory = $true)]$Files, [Parameter(Mandatory = $true)]$Consumers, [int]$MaxVisits = 0)
    if ($MaxVisits -le 0) { $MaxVisits = $script:ReleaseImpactScriptDefaultVisits }
    $inventory = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($key in @($Files.Keys)) {
        $path = ([string]$key).Replace([char]92, '/')
        $normal = ConvertTo-ReleaseImpactScriptPath ($script:ReleaseImpactScriptAnchor + $path)
        if ($normal.Kind -ne 'anchored' -or $normal.Path -ne $path) { throw ('release-impact script inventory path is not repo-relative: ' + $path) }
        $inventory[$path] = $Files[$key]
    }
    $graph = New-ReleaseImpactScriptGraph $inventory
    $edges = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($consumer in @($Consumers)) {
        $identity = [string]$consumer.project + '::' + [string]$consumer.fullName
        $visited = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
        $queue = New-Object System.Collections.Generic.Queue[object]
        $record = {
            param([string]$Target, [string]$Kind, [string[]]$Chain)
            if (-not $edges.ContainsKey($Target)) { $edges[$Target] = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal) }
            $classes = $edges[$Target]
            if (-not $classes.ContainsKey($identity)) {
                $classes[$identity] = @{
                    project = [string]$consumer.project; fullName = [string]$consumer.fullName; chain = $Chain
                    kinds = (New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal))
                }
            }
            [void]$classes[$identity].kinds.Add($Kind)
        }
        foreach ($declaration in @(Get-ReleaseImpactScriptSorted @($consumer.declarationPaths))) {
            $declaration = ([string]$declaration).Replace([char]92, '/')
            if (-not $inventory.ContainsKey($declaration)) { Add-ReleaseImpactScriptReason $graph $declaration 'content-unavailable'; continue }
            $seeds = Get-ReleaseImpactScriptSeeds $graph $declaration
            foreach ($seed in @(Get-ReleaseImpactScriptSorted $seeds.Keys)) {
                & $record $seed $seeds[$seed] @($declaration, $seed)
                if ($visited.Add($seed)) { $queue.Enqueue(@{ Path = $seed; Chain = @($declaration, $seed) }) }
            }
        }
        $expanded = 0
        while ($queue.Count -gt 0) {
            $node = $queue.Dequeue()
            if ($expanded -ge $MaxVisits) {
                # Bound exhaustion: this node's own closure is unknown, never truncated-complete.
                Add-ReleaseImpactScriptReason $graph $node.Path 'budget'
                continue
            }
            $expanded++
            foreach ($dependency in (Get-ReleaseImpactScriptDependencies $graph $node.Path)) {
                $chain = @($node.Chain) + @($dependency.Path)
                & $record $dependency.Path 'script:closure' $chain
                if ($visited.Add($dependency.Path)) { $queue.Enqueue(@{ Path = $dependency.Path; Chain = $chain }) }
            }
        }
    }
    $reasons = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    [void]$reasons.Add('script-closure-uncertified')
    foreach ($reason in $graph.Reasons) { [void]$reasons.Add($reason) }
    $edgeList = New-Object System.Collections.Generic.List[object]
    foreach ($path in $edges.Keys) {
        $classes = New-Object System.Collections.Generic.List[object]
        foreach ($entry in $edges[$path].Values) {
            $classes.Add([ordered]@{ project = $entry.project; fullName = $entry.fullName; kinds = @($entry.kinds); chain = @($entry.chain) })
        }
        $edgeList.Add([ordered]@{ path = $path; classes = $classes.ToArray() })
    }
    return [ordered]@{
        schema = $script:ReleaseImpactScriptSchema; complete = $false; fallback = 'Full'
        incompleteReasons = @($reasons); edges = $edgeList.ToArray()
    }
}

# JSON adapter: {"cases":[{"name","files":{path:content|null},"consumers":[{project,fullName,declarationPaths}],"maxVisits"}]}.
function Invoke-ReleaseImpactScriptClosureJson {
    param([Parameter(Mandatory = $true)][string]$InputPath)
    $document = [IO.File]::ReadAllText($InputPath) | ConvertFrom-Json
    $results = [ordered]@{}
    foreach ($case in @($document.cases)) {
        $files = @{}
        foreach ($property in $case.files.PSObject.Properties) { $files[$property.Name] = $property.Value }
        $visits = 0
        if ($null -ne $case.PSObject.Properties['maxVisits'] -and $null -ne $case.maxVisits) { $visits = [int]$case.maxVisits }
        $results[[string]$case.name] = Get-ReleaseImpactScriptClosure -Files $files -Consumers @($case.consumers) -MaxVisits $visits
    }
    return ([ordered]@{ cases = $results } | ConvertTo-Json -Depth 12 -Compress)
}
