# CARD-1385: hash the dotnet-build definition files at HEAD and compare them
# with server/Application/Services/LandVerificationBuildGraph.manifest.txt.
# CARD-1390: the trailing NUL check is ordinal. Every ls-tree row is validated,
# not only definition rows.
# CARD-1400: -Check and -Update refuse an EmbeddedResource, Content, None,
# Compile, or AdditionalFiles include that can select docs/**/*.md or the root
# README.md. Matching is case-insensitive after one %XX decode. Every such
# include is resolved from the definition file's own directory and from each
# importing project's directory, including through another import. The nearest
# Directory.Build.props and Directory.Build.targets above a project count as
# imported. An Import Project path that starts with $(MSBuildThisFileDirectory)
# or $(MSBuildProjectDirectory) is expanded from that property and is not
# prefixed with the importing file's folder again. $(MSBuildThisFileDirectory)
# in an include is that file's directory. $(MSBuildProjectDirectory) is the
# directory being checked.
# CARD-1409 repair 4 (Review 937bb1c9): file items are classified by what they
# RESOLVE to, not by spelling. Every $(...) in a path-item Include or Update is
# expanded from the tracked definition files' own property values (every
# definition, where it is defined; the two directory properties; an output or
# intermediate root such as $(IntermediateOutputPath); a Pkg* path property with
# GeneratePathProperty; an MSBuild task output, also through
# @(Item->'%(RootDir)%(Directory)'), %(Item.Metadata) and GetDirectoryName). A
# property the audit cannot resolve statically refuses by name; a path rooted
# at an output or package root accepts any tail without '..'. A repo pattern is
# expanded against every blob tracked at HEAD with a real glob matcher (honouring
# Exclude): the root README.md, an exact docs/ path other than the one
# allow-listed input, a wildcard whose first segment can be docs/ (whatever it
# matches today), and a markdown or text match outside the document allow-list
# each refuse naming the resolved path. Item metadata names are allow-listed per
# item type (ProjectReference AdditionalProperties, Properties and every unknown
# name refuse). A property value that reads a file through a property function
# outside an output root, or whose literal part is a docs path, refuses.
# CARD-1409: an Import Project path that still contains $(, @( or %( after the
# two known properties are expanded is refused and is not followed; there is no
# allow-list. An Import with an attribute other than Project, Condition or Label
# is refused; Condition is not evaluated. An Import Project is split on ';' as
# MSBuild splits it, and every non-empty part is audited as its own import. Each
# part must be tracked at HEAD inside the repository as a *proj, *proj.user,
# .props or .targets file; an empty Project, a path outside the repository, a
# path absent from HEAD or a tracked file with another extension is refused. A
# part that contains * or ? is refused outright: land digests only definition
# files, so a later non-definition match would never be re-audited (Review
# 23aedc52). The nearest Directory.Packages.props above a project counts as
# imported like Directory.Build.props, and a tracked <project>.user beside a
# project is imported by it; both resolve includes from the project directory.
# ReadLinesFromFile, Copy, and property functions that read files are not
# inspected.
# CARD-1409 (Review 8989ece8 F1): Microsoft.Common.props and .targets import
# $(MSBuildProjectExtensionsPath)$(MSBuildProjectFile).*.props and .*.targets,
# obj/ by default, and the audit does not follow those wildcards. A tracked
# definition file under any obj/ path segment (case-insensitive) is refused by
# its path, whatever it contains. A definition file that sets
# BaseIntermediateOutputPath, MSBuildProjectExtensionsPath or
# IntermediateOutputPath is refused because a redirected extension directory
# cannot be evaluated; reading one of them inside another property is not
# setting it.
# CARD-1409 (Review 31c77116): the audit is default-deny. A definition file may
# set only an allow-listed property name, seeded from the tracked definition
# files (the plan keeps the inventory), wherever the PropertyGroup sits and
# including a task Output PropertyName; declare only an allow-listed item type,
# each under a rule: a path item (Compile, Content, None, EmbeddedResource,
# AdditionalFiles, EditorConfigFiles, GlobalAnalyzerConfigFiles and the
# repository's own target-time items) must stay inside the repository and never
# select docs markdown or the root README.md, a ProjectReference must resolve to
# a tracked *proj from every directory it is evaluated from, and a package-style
# item (PackageReference, FrameworkReference, InternalsVisibleTo) is a bare
# name; and hold under <Project> only PropertyGroup, ItemGroup, Import, Target
# and an allow-listed <Sdk>. The Project Sdk attribute is allow-listed too. An
# include that carries @( or %( refuses. A tracked .rsp file is parsed: each
# non-comment line is one switch among -nodeReuse, -maxcpucount, -verbosity and
# -nologo with a checked value; -p:, -property:, @file and every other switch
# refuse. Anything the audit cannot classify refuses by name. Target task
# parameters are not evaluated; item metadata names are (repair 4).
# ASCII-only: must parse under Windows PowerShell 5.1.
#
# Usage:
#   pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 [-Check]
#   pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update
#
# -Check is the default. It prints digest=<HEX> count=<N> and exits 0 only
# when both manifest lines match. -Update rewrites the manifest from HEAD.
# Exit 1 is a mismatch, a docs-input refusal, or bad usage. Exit 2 is a git failure.
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Update
)

$ErrorActionPreference = 'Stop'

function Exit-Audit([int]$Code, [string]$Message) {
    if (-not [string]::IsNullOrEmpty($Message)) {
        [Console]::Error.WriteLine($Message)
    }
    exit $Code
}

if ($Check.IsPresent -and $Update.IsPresent) {
    Exit-Audit 1 'Specify -Check or -Update, not both.'
}
if (-not $Update.IsPresent) {
    $Check = $true
}

if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    Exit-Audit 1 'script root is unknown'
}
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ManifestPath = [System.IO.Path]::Combine($RepoRoot, 'server', 'Application', 'Services', 'LandVerificationBuildGraph.manifest.txt')

# CARD-1409 (Review 31c77116): the allow-lists. Every entry is used by a tracked
# definition file; the plan's inventory lists where. A new name is a reviewed edit here.
$AllowedProjectSdks = @('Microsoft.NET.Sdk', 'Microsoft.NET.Sdk.Web')
$AllowedSdkElements = @('Aspire.AppHost.Sdk')
$AllowedProjectChildren = @('PropertyGroup', 'ItemGroup', 'Import', 'Target', 'Sdk')
$AllowedProperties = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($allowedProperty in @(
    '_AntiphonCleanFileChars', '_AntiphonRepeatAdditional', '_AntiphonRepeatFile', '_CustodyChildOutputDir',
    '_F5PtyHostNativeDir', '_FakeClaudeOutputDir', '_FakeGrokOutputDir', '_GitHeadSha', '_GitHeadShaTrimmed',
    '_GitRevParseExitCode', '_LandFakeGrokDir', '_PtyHostNativeDir', '_StagedHelperDest', '_StagedHelperDir',
    '_StagedHelperGlob', '_StagedHelperShared', 'AntiphonCleanFileMaxBytes', 'AntiphonMessagingVersion',
    'AssemblyName', 'Authors', 'ConcurrentGarbageCollection', 'DefaultItemExcludes', 'Description',
    'EnableMicrosoftTestingPlatformRunner', 'EnablePublicApiAnalyzers', 'FakeBrowserOutputDir',
    'FakeBrowserTargetPath', 'ImplicitUsings', 'InvariantGlobalization', 'IsAspireHost', 'IsPackable',
    'IsTestProject', 'Nullable', 'OutputType', 'PackageId', 'PackageLicenseExpression', 'PackageProjectUrl',
    'PackageReadmeFile', 'PackageTags', 'PackAsTool', 'PublishRepositoryUrl', 'RepositoryUrl', 'RollForward',
    'RootNamespace', 'ServerGarbageCollection', 'SourceControlInformationFeatureSupported', 'SourceRevisionId',
    'TargetFramework', 'ToolCommandName', 'UseAppHost', 'UsePublishedPackages', 'UserSecretsId', 'Version',
    'WarningsAsErrors')) {
    [void]$AllowedProperties.Add($allowedProperty)
}
# Item type -> rule. path: resolved like a file include and refused when it leaves the repository
# or selects docs markdown or the root README.md. project: a tracked *proj. name: a bare identifier.
$ItemRules = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($pathItem in @(
    'Compile', 'EmbeddedResource', 'Content', 'None', 'AdditionalFiles', 'EditorConfigFiles',
    'GlobalAnalyzerConfigFiles', '_StagedHelperFiles', '_StagedHelperTarget',
    'CustodyChildOutput', 'FakeBrowserFile', 'FakeClaudeOutput', 'FakeGrokOutput', 'LandFakeGrokOutput',
    'StagedHelper', '_CustodyChildTargetPath', '_FakeClaudeTargetPath', '_FakeGrokTargetPath',
    '_LandFakeGrokTarget')) {
    $ItemRules[$pathItem] = 'path'
}
# text: source lines handed to WriteLinesToFile inside a target, never a file path (CARD-1409 repair 4).
$ItemRules['_AntiphonRepeatLine'] = 'text'
$ItemRules['ProjectReference'] = 'project'
foreach ($nameItem in @('PackageReference', 'FrameworkReference', 'InternalsVisibleTo')) {
    $ItemRules[$nameItem] = 'name'
}
# CARD-1409 repair 4 (Review 937bb1c9 F3): item metadata names, as attributes or child elements, per
# item type, seeded from the tracked definition files. A type absent here allows none. The global-property
# metadata (AdditionalProperties, Properties, GlobalProperties, SetConfiguration, SetPlatform,
# SetTargetFramework, UndefineProperties) and every other name refuse: they reopen the R2 property imports.
$ItemMetadata = New-Object 'System.Collections.Generic.Dictionary[string,string[]]' ([System.StringComparer]::OrdinalIgnoreCase)
$ItemMetadata['PackageReference'] = @('Version', 'GeneratePathProperty', 'ExcludeAssets', 'PrivateAssets', 'IncludeAssets')
$ItemMetadata['ProjectReference'] = @('Aliases', 'ReferenceOutputAssembly', 'Private')
$ItemMetadata['Content'] = @('CopyToOutputDirectory', 'CopyToPublishDirectory', 'Link', 'TargetPath')
$ItemMetadata['Compile'] = @('Link')
$ItemMetadata['EmbeddedResource'] = @('LogicalName')
$ItemMetadata['None'] = @('Pack', 'PackagePath', 'CopyToOutputDirectory')
$ItemMetadata['StagedHelper'] = @('Folder', 'Glob')
# Item element attributes that are not metadata (compared ordinally, as MSBuild reads them).
$ItemElementAttributes = @('Include', 'Exclude', 'Update', 'Remove', 'Condition', 'Label')
# CARD-1409 repair 4 (Review 937bb1c9 F1/F2): properties whose value is the build's own output or
# intermediate directory. A path rooted at one is a build product, not a tracked input. A definition file
# cannot set one (the property allow-list and D-12 refuse), the parsed .rsp cannot, and the land build
# passes no -p:, so the root is the SDK's.
$OutputRootProperties = @('IntermediateOutputPath', 'BaseIntermediateOutputPath', 'MSBuildProjectExtensionsPath',
    'OutDir', 'OutputPath', 'BaseOutputPath', 'TargetDir', 'TargetPath', 'PublishDir')
# The one docs/ path a tracked definition file may select, by exact resolved path (never by pattern).
$AllowedDocsInputs = @('docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-captures.json')
# Markdown and text documents outside docs/ never skip a land build, but the audit still names what it
# accepts: the resolved patterns the tracked definition files use (from the item's own directory and from
# every importing project directory), three bare names the shared props use, a README.md beside a
# non-root project, and one exact path. Any other selected document refuses by its matched path.
$DocumentExtensions = @('.md', '.markdown', '.txt')
$AllowedDocumentPatterns = @(
    'server/Bundles/*.md', 'server/Bundles/Presets/*.md', 'server/Bundles/Fragments/*.md',
    'tests/Antiphon.Agents.Pty.Tests/golden/**/*', 'tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/**/*',
    'tests/Antiphon.Agents.Pty.Tests/probes/**/*', 'tests/Antiphon.Tests/Agents/Fixtures/**',
    'tests/Antiphon.Messaging.Tests/Fixtures/*')
$AllowedDocumentNames = @('PublicAPI.Shipped.txt', 'PublicAPI.Unshipped.txt', 'slow-tests-allowlist.txt')
$AllowedDocumentPaths = @('server/Application/Services/LandVerificationBuildGraph.manifest.txt')
# A property function that reads the file system; its first argument must be rooted at an output root.
$FileReadingFunctions = @('ReadAllText', 'ReadAllBytes', 'ReadAllLines', 'ReadLines', 'GetFiles', 'GetDirectories',
    'GetFileSystemEntries', 'EnumerateFiles', 'EnumerateDirectories', 'EnumerateFileSystemEntries')

function Test-AllowedName($Allowed, [string]$Name, [bool]$IgnoreCase) {
    foreach ($candidate in $Allowed) {
        if ($IgnoreCase) {
            if ([string]$candidate -ieq $Name) { return $true }
        }
        elseif ([string]::Equals([string]$candidate, $Name, [System.StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

function Test-DefinitionPath([string]$Path) {
    $name = [System.IO.Path]::GetFileName($Path)
    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension.EndsWith('proj', [System.StringComparison]::Ordinal)) { return $true }
    switch ($extension) {
        '.props' { return $true }
        '.targets' { return $true }
        '.sln' { return $true }
        '.slnx' { return $true }
        '.slnf' { return $true }
        '.rsp' { return $true }
    }
    if ($name.StartsWith('Directory.Build.', [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    if (Test-ProjectUserFile $Path) { return $true }
    switch ($name.ToLowerInvariant()) {
        'global.json' { return $true }
        'nuget.config' { return $true }
        'packages.lock.json' { return $true }
        'packages.config' { return $true }
        'directory.packages.props' { return $true }
    }
    return $false
}

# CARD-1409: Microsoft.Common.targets imports $(MSBuildProjectFullPath).user when it exists.
function Test-ProjectUserFile([string]$Path) {
    $name = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()
    return $name.EndsWith('proj.user', [System.StringComparison]::Ordinal)
}

# CARD-1409 (Review 8989ece8 F1): obj/ is where Microsoft.Common.props/.targets import
# $(MSBuildProjectFile).*.props/.targets from by wildcard; the audit does not follow them.
function Test-PathUnderObjDirectory([string]$Path) {
    $segments = $Path.Replace('\', '/').Split('/')
    for ($i = 0; $i -lt ($segments.Length - 1); $i++) {
        if ([string]::Equals($segments[$i], 'obj', [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

# CARD-1409 (Review 8989ece8 F1): an element that sets a redirect of the extension or intermediate
# directory, wherever it sits; returns the element name as written, or $null.
function Get-IntermediateRedirectElement($Doc) {
    foreach ($node in $Doc.GetElementsByTagName('*')) {
        $local = [string]$node.LocalName
        foreach ($name in @('BaseIntermediateOutputPath', 'MSBuildProjectExtensionsPath', 'IntermediateOutputPath')) {
            if ($local.Equals($name, [System.StringComparison]::OrdinalIgnoreCase)) { return $local }
        }
    }
    return $null
}

function Test-ProjectXml([string]$Path) {
    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension.EndsWith('proj', [System.StringComparison]::Ordinal)) { return $true }
    if ($extension -eq '.props' -or $extension -eq '.targets') { return $true }
    if (Test-ProjectUserFile $Path) { return $true }
    return $false
}

# CARD-1409 (Review 31c77116 R1): MSBuild reads Directory.Build.rsp as command-line switches; every tracked .rsp is parsed.
function Test-ResponseFile([string]$Path) {
    return [System.IO.Path]::GetExtension($Path).ToLowerInvariant() -eq '.rsp'
}

function Test-FullObjectId([string]$Value) {
    if ($Value.Length -ne 40 -and $Value.Length -ne 64) { return $false }
    foreach ($c in $Value.ToCharArray()) {
        $ok = ($c -ge '0' -and $c -le '9') -or ($c -ge 'a' -and $c -le 'f')
        if (-not $ok) { return $false }
    }
    $zeros = '0' * $Value.Length
    if ($Value -ceq $zeros) { return $false }
    return $true
}

function Test-SafePath([string]$Path) {
    if ([string]::IsNullOrEmpty($Path)) { return $false }
    if ($Path.IndexOf('\') -ge 0) { return $false }
    if ($Path.IndexOf([char]0xFFFD) -ge 0) { return $false }
    foreach ($c in $Path.ToCharArray()) {
        if ([char]::IsControl($c)) { return $false }
    }
    foreach ($component in $Path.Split('/')) {
        if ($component -eq '' -or $component -eq '.' -or $component -eq '..') { return $false }
    }
    return $true
}

function ConvertTo-RegexReplacement([string]$Value) {
    return $Value.Replace('$', '$$')
}

function ConvertTo-RepoRelative([string]$Absolute, [string]$Root) {
    $full = $Absolute.Replace('\', '/')
    $prefix = $Root.Replace('\', '/').TrimEnd('/')
    $head = $prefix + '/'
    if ($full.StartsWith($head, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($head.Length)
    }
    return $null
}

function New-SegmentList([string]$Pattern) {
    $list = New-Object 'System.Collections.Generic.List[string]'
    foreach ($part in $Pattern.Split('/')) {
        if ($part.Length -gt 0) { [void]$list.Add($part) }
    }
    return ,$list
}

function Test-SegmentMatch([string]$Glob, [string]$Text) {
    $body = [regex]::Escape($Glob).Replace('\*', '[^/]*').Replace('\?', '[^/]')
    return [regex]::IsMatch($Text, '^' + $body + '$', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

function Test-PatternHasWildcard([string]$Pattern) {
    return (($Pattern.IndexOf('*') -ge 0) -or ($Pattern.IndexOf('?') -ge 0))
}

# CARD-1409 repair 4 (Review 937bb1c9 F1): a wildcard pattern can reach docs/ when its first segment is
# '**' or can match the name 'docs'. What it matches today does not matter: a docs file added later would
# be a docs-only land, so the pattern itself is refused.
function Test-PatternCanReachDocs([string]$Pattern) {
    $segs = New-SegmentList $Pattern
    if ($segs.Count -eq 0) { return $false }
    $first = [string]$segs[0]
    if ($first -eq '**') { return $true }
    return (Test-SegmentMatch $first 'docs')
}

function Test-PathUnderDocs([string]$Path) {
    if ([string]::Equals($Path, 'docs', [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $Path.StartsWith('docs/', [System.StringComparison]::OrdinalIgnoreCase)
}

# A real glob matcher over repository-relative paths: * and ? stay inside a segment, ** spans segments.
function Get-GlobRegex([string]$Pattern) {
    $segs = New-SegmentList $Pattern
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('^')
    for ($i = 0; $i -lt $segs.Count; $i++) {
        $seg = [string]$segs[$i]
        $last = ($i -eq ($segs.Count - 1))
        if ($seg -eq '**') {
            if ($last) { [void]$sb.Append('.*') } else { [void]$sb.Append('(?:[^/]+/)*') }
            continue
        }
        $body = [regex]::Escape($seg).Replace('\*\*', '[^/]*').Replace('\*', '[^/]*').Replace('\?', '[^/]')
        [void]$sb.Append($body)
        if (-not $last) { [void]$sb.Append('/') }
    }
    [void]$sb.Append('$')
    return New-Object System.Text.RegularExpressions.Regex ($sb.ToString(), [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

function Get-GlobLiteralPrefix([string]$Pattern) {
    $cut = $Pattern.Length
    $star = $Pattern.IndexOf('*')
    if ($star -ge 0 -and $star -lt $cut) { $cut = $star }
    $question = $Pattern.IndexOf('?')
    if ($question -ge 0 -and $question -lt $cut) { $cut = $question }
    $prefix = $Pattern.Substring(0, $cut)
    $slash = $prefix.LastIndexOf('/')
    if ($slash -lt 0) { return '' }
    return $prefix.Substring(0, $slash + 1)
}

# Every path tracked at HEAD that the pattern selects, minus the Exclude patterns.
function Get-TrackedMatches([string]$Pattern, $ExcludePatterns) {
    $hits = New-Object 'System.Collections.Generic.List[string]'
    $regex = Get-GlobRegex $Pattern
    $prefix = Get-GlobLiteralPrefix $Pattern
    $excludes = New-Object 'System.Collections.Generic.List[object]'
    foreach ($exclude in $ExcludePatterns) { [void]$excludes.Add((Get-GlobRegex ([string]$exclude))) }
    foreach ($tracked in $script:TrackedPaths) {
        if ($prefix.Length -gt 0 -and -not $tracked.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        if (-not $regex.IsMatch($tracked)) { continue }
        $excluded = $false
        foreach ($ex in $excludes) {
            if ($ex.IsMatch($tracked)) { $excluded = $true; break }
        }
        if (-not $excluded) { [void]$hits.Add($tracked) }
    }
    return ,$hits
}

function Test-DocumentPath([string]$Path) {
    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    return ($DocumentExtensions -contains $extension)
}

function Test-DocumentAllowed([string]$Pattern, [string]$Path) {
    if (Test-AllowedName $AllowedDocumentPatterns $Pattern $true) { return $true }
    if (Test-AllowedName $AllowedDocumentPaths $Path $true) { return $true }
    $name = [System.IO.Path]::GetFileName($Path)
    if (Test-AllowedName $AllowedDocumentNames $name $true) { return $true }
    if ([string]::Equals($name, 'README.md', [System.StringComparison]::OrdinalIgnoreCase) -and ($Path.IndexOf('/') -ge 0)) { return $true }
    return $false
}

function Test-GlobWalk($Pattern, [int]$Index, $Target, [int]$TargetIndex) {
    while ($true) {
        if ($Index -ge $Pattern.Count) { return ($TargetIndex -ge $Target.Count) }
        $seg = [string]$Pattern[$Index]
        if ($seg -eq '**') {
            if (Test-GlobWalk $Pattern ($Index + 1) $Target $TargetIndex) { return $true }
            if ($TargetIndex -ge $Target.Count) { return $false }
            $TargetIndex = $TargetIndex + 1
            continue
        }
        if ($TargetIndex -ge $Target.Count) { return $false }
        if (-not (Test-SegmentMatch $seg ([string]$Target[$TargetIndex]))) { return $false }
        $Index = $Index + 1
        $TargetIndex = $TargetIndex + 1
    }
}

function Test-PatternMatchesReadme([string]$Pattern) {
    $target = New-Object 'System.Collections.Generic.List[string]'
    [void]$target.Add('README.md')
    $segs = New-SegmentList $Pattern
    return Test-GlobWalk $segs 0 $target 0
}

function Normalize-RepoPattern([string]$Combined) {
    $stack = New-Object 'System.Collections.Generic.List[string]'
    foreach ($part in $Combined.Replace('\', '/').Split('/')) {
        if ($part -eq '' -or $part -eq '.') { continue }
        if ($part -eq '..') {
            if ($stack.Count -eq 0) { return 'outside' }
            $top = [string]$stack[$stack.Count - 1]
            if ($top.IndexOf('*') -ge 0 -or $top.IndexOf('?') -ge 0) { return 'escape' }
            $stack.RemoveAt($stack.Count - 1)
            continue
        }
        [void]$stack.Add($part)
    }
    if ($stack.Count -eq 0) { return 'outside' }
    return ($stack -join '/')
}

function Get-RepoDirectory([string]$Path) {
    $base = [System.IO.Path]::GetDirectoryName($Path.Replace('\', '/'))
    if ([string]::IsNullOrEmpty($base)) { return '' }
    return $base.Replace('\', '/')
}

function Test-ProjFile([string]$Path) {
    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    return $extension.EndsWith('proj', [System.StringComparison]::Ordinal)
}

function ConvertFrom-HexByte([string]$Hex) {
    if ($Hex.Length -ne 2) { return -1 }
    $n = 0
    foreach ($c in $Hex.ToCharArray()) {
        $d = [int]$c
        if ($d -ge 48 -and $d -le 57) { $v = $d - 48 }
        elseif ($d -ge 65 -and $d -le 70) { $v = $d - 55 }
        elseif ($d -ge 97 -and $d -le 102) { $v = $d - 87 }
        else { return -1 }
        $n = ($n * 16) + $v
    }
    return $n
}

function ConvertFrom-PercentEncoding([string]$Value) {
    $bytes = New-Object 'System.Collections.Generic.List[byte]'
    $i = 0
    while ($i -lt $Value.Length) {
        $c = $Value[$i]
        if (($c -eq '%') -and (($i + 2) -lt $Value.Length)) {
            $n = ConvertFrom-HexByte $Value.Substring($i + 1, 2)
            if ($n -ge 0) {
                if ($n -eq 0) { return $null }
                [void]$bytes.Add([byte]$n)
                $i = $i + 3
                continue
            }
        }
        $encoded = [System.Text.Encoding]::UTF8.GetBytes([string]$c)
        foreach ($b in $encoded) { [void]$bytes.Add([byte]$b) }
        $i = $i + 1
    }
    return [System.Text.Encoding]::UTF8.GetString($bytes.ToArray())
}

# CARD-1409 repair 4 (Review 937bb1c9 F2): the census of every parsed definition file, filled before any
# include is classified: property definitions, task outputs, declared items and package path properties.
$script:PropertyDefinitions = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:TaskPropertyOutputs = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:TaskItemOutputs = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:ItemDeclarations = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:PackagePathProperties = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:TrackedPaths = New-Object 'System.Collections.Generic.List[string]'
$script:UnresolvedReference = ''

function Add-CensusEntry($Map, [string]$Key, $Entry) {
    if (-not $Map.ContainsKey($Key)) { $Map[$Key] = New-Object 'System.Collections.Generic.List[object]' }
    [void]$Map[$Key].Add($Entry)
}

function Get-ItemMetadataValues($Node) {
    $values = @{}
    foreach ($attribute in $Node.Attributes) {
        $attributeName = [string]$attribute.Name
        if (Test-AllowedName $ItemElementAttributes $attributeName $false) { continue }
        $values[$attributeName] = [string]$attribute.Value
    }
    foreach ($child in $Node.ChildNodes) {
        if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $values[[string]$child.LocalName] = ([string]$child.InnerText).Trim()
    }
    return $values
}

function Register-DefinitionFacts($Doc, [string]$Path) {
    foreach ($group in $Doc.GetElementsByTagName('PropertyGroup')) {
        $groupConditional = -not [string]::IsNullOrWhiteSpace([string]$group.GetAttribute('Condition'))
        foreach ($child in $group.ChildNodes) {
            if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            $conditional = $groupConditional -or (-not [string]::IsNullOrWhiteSpace([string]$child.GetAttribute('Condition')))
            Add-CensusEntry $script:PropertyDefinitions ([string]$child.LocalName) @{ File = $Path; Value = ([string]$child.InnerText).Trim(); Conditional = $conditional }
        }
    }
    foreach ($output in $Doc.GetElementsByTagName('Output')) {
        $task = ''
        if ($null -ne $output.ParentNode) { $task = [string]$output.ParentNode.LocalName }
        $propertyName = ([string]$output.GetAttribute('PropertyName')).Trim()
        if ($propertyName.Length -gt 0) { Add-CensusEntry $script:TaskPropertyOutputs $propertyName $task }
        $itemName = ([string]$output.GetAttribute('ItemName')).Trim()
        if ($itemName.Length -gt 0) { Add-CensusEntry $script:TaskItemOutputs $itemName $task }
    }
    foreach ($group in $Doc.GetElementsByTagName('ItemGroup')) {
        foreach ($node in $group.ChildNodes) {
            if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            $include = [string]$node.GetAttribute('Include')
            if ([string]::IsNullOrWhiteSpace($include)) { continue }
            $metadata = Get-ItemMetadataValues $node
            Add-CensusEntry $script:ItemDeclarations ([string]$node.LocalName) @{ File = $Path; Include = $include; Metadata = $metadata }
            if ([string]$node.LocalName -ieq 'PackageReference') {
                $generate = ''
                if ($metadata.ContainsKey('GeneratePathProperty')) { $generate = [string]$metadata['GeneratePathProperty'] }
                if ($generate -ieq 'true') {
                    foreach ($id in $include.Split(';')) {
                        $trimmed = $id.Trim()
                        if ($trimmed.Length -gt 0) { [void]$script:PackagePathProperties.Add('Pkg' + $trimmed.Replace('.', '_')) }
                    }
                }
            }
        }
    }
}

# True when every task Output that fills the name comes from the MSBuild task, whose TargetOutputs are
# other projects' build products. Any other task (Exec, ReadLinesFromFile) is not statically resolvable.
function Test-TaskOutputOnly($Map, [string]$Name) {
    if (-not $Map.ContainsKey($Name)) { return $false }
    foreach ($task in $Map[$Name]) {
        if (-not [string]::Equals([string]$task, 'MSBuild', [System.StringComparison]::Ordinal)) { return $false }
    }
    return $true
}

function New-Candidate([string]$Kind, [string]$Text, [bool]$Anchored) {
    return @{ Kind = $Kind; Text = $Text; Anchored = $Anchored }
}

function Set-Unresolved([string]$Reference) {
    $script:UnresolvedReference = $Reference
    return $null
}

function Get-FileDirectoryPrefix([string]$Path) {
    $base = Get-RepoDirectory $Path
    if ([string]::IsNullOrEmpty($base)) { return './' }
    return $base + '/'
}

# Expands one text (an include part or a property value) into every value it can take: candidates
# tagged repo (a path inside the tree), output (rooted at a build output or intermediate directory) or
# package (rooted at a restored package folder). $FileDir is what $(MSBuildThisFileDirectory) means in
# the file holding the text. Returns $null, with $script:UnresolvedReference set, when a reference cannot be
# resolved statically: a property no tracked file defines, a non-MSBuild task output, a property function
# other than GetDirectoryName over an MSBuild task output, an item transform other than
# @(MSBuildOutputItem->'%(RootDir)%(Directory)'), %(Item.Metadata) of an undeclared item, or a rooted
# reference that is not the first token.
function Expand-Candidates([string]$Text, [string]$FileDir, [string]$ProjectDir, [int]$Depth) {
    $result = New-Object 'System.Collections.Generic.List[object]'
    if ($Depth -gt 8) { return (Set-Unresolved $Text) }
    $transform = [regex]::Match($Text, '^@\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*->\s*''%\(RootDir\)%\(Directory\)''\s*\)$')
    if ($transform.Success) {
        $itemName = $transform.Groups[1].Value
        if ((Test-TaskOutputOnly $script:TaskItemOutputs $itemName) -and (-not $script:ItemDeclarations.ContainsKey($itemName))) {
            [void]$result.Add((New-Candidate 'output' '' $false))
            return ,$result
        }
        return (Set-Unresolved $Text)
    }
    $batch = [regex]::Match($Text, '^%\(\s*([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*\)$')
    if ($batch.Success) {
        $itemName = $batch.Groups[1].Value
        $metadataName = $batch.Groups[2].Value
        if (-not $script:ItemDeclarations.ContainsKey($itemName)) { return (Set-Unresolved $Text) }
        foreach ($declaration in $script:ItemDeclarations[$itemName]) {
            $value = ''
            if ($declaration.Metadata.ContainsKey($metadataName)) { $value = [string]$declaration.Metadata[$metadataName] }
            $inner = Expand-Candidates $value (Get-FileDirectoryPrefix ([string]$declaration.File)) $ProjectDir ($Depth + 1)
            if ($null -eq $inner) { return $null }
            foreach ($candidate in $inner) { [void]$result.Add($candidate) }
        }
        return ,$result
    }
    if (($Text.IndexOf('@(') -ge 0) -or ($Text.IndexOf('%(') -ge 0)) { return (Set-Unresolved $Text) }
    $directoryOf = [regex]::Match($Text, '^\$\(\[System\.IO\.Path\]::GetDirectoryName\(''\$\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)''\)\)$')
    if ($directoryOf.Success) {
        $inner = Expand-Candidates ('$(' + $directoryOf.Groups[1].Value + ')') $FileDir $ProjectDir ($Depth + 1)
        if ($null -eq $inner) { return $null }
        foreach ($candidate in $inner) {
            if ([string]$candidate.Kind -ne 'output') { return (Set-Unresolved $Text) }
            [void]$result.Add((New-Candidate 'output' '' $false))
        }
        return ,$result
    }
    if ($Text.IndexOf('$([') -ge 0) { return (Set-Unresolved $Text) }
    $tokens = New-Object 'System.Collections.Generic.List[object]'
    $position = 0
    foreach ($m in [regex]::Matches($Text, '\$\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)')) {
        if ($m.Index -gt $position) { [void]$tokens.Add(@{ Literal = $Text.Substring($position, $m.Index - $position) }) }
        [void]$tokens.Add(@{ Property = $m.Groups[1].Value })
        $position = $m.Index + $m.Length
    }
    if ($position -lt $Text.Length) { [void]$tokens.Add(@{ Literal = $Text.Substring($position) }) }
    foreach ($token in $tokens) {
        if ($token.ContainsKey('Literal') -and (([string]$token['Literal']).IndexOf('$(') -ge 0)) { return (Set-Unresolved $Text) }
    }
    [void]$result.Add((New-Candidate 'repo' '' $false))
    $first = $true
    foreach ($token in $tokens) {
        if ($token.ContainsKey('Literal')) {
            foreach ($candidate in $result) { $candidate.Text = [string]$candidate.Text + [string]$token['Literal'] }
            $first = $false
            continue
        }
        $name = [string]$token['Property']
        $values = Get-PropertyCandidates $name $FileDir $ProjectDir $Depth
        if ($null -eq $values) { return $null }
        $next = New-Object 'System.Collections.Generic.List[object]'
        foreach ($candidate in $result) {
            foreach ($value in $values) {
                $rooted = ([string]$value.Kind -ne 'repo') -or [bool]$value.Anchored
                if ($rooted -and -not $first) { return (Set-Unresolved ('$(' + $name + ')')) }
                if ($rooted) { [void]$next.Add((New-Candidate ([string]$value.Kind) ([string]$value.Text) ([bool]$value.Anchored))) }
                else { [void]$next.Add((New-Candidate ([string]$candidate.Kind) ([string]$candidate.Text + [string]$value.Text) ([bool]$candidate.Anchored))) }
            }
        }
        if ($next.Count -gt 64) { return (Set-Unresolved ('$(' + $name + ')')) }
        $result = $next
        $first = $false
    }
    return ,$result
}

# The values one $(Name) can take: the two directory properties, an output root, a package path property,
# an MSBuild task output, or the union of every PropertyGroup definition across the tracked files (each
# expanded where it is defined), plus the empty value when a definition is conditional.
function Get-PropertyCandidates([string]$Name, [string]$FileDir, [string]$ProjectDir, [int]$Depth) {
    $values = New-Object 'System.Collections.Generic.List[object]'
    if ($Name -ieq 'MSBuildThisFileDirectory') {
        [void]$values.Add((New-Candidate 'repo' $FileDir $true))
        return ,$values
    }
    if ($Name -ieq 'MSBuildProjectDirectory') {
        $projectText = $ProjectDir
        if ([string]::IsNullOrEmpty($projectText)) { $projectText = '.' }
        [void]$values.Add((New-Candidate 'repo' $projectText $true))
        return ,$values
    }
    if (Test-AllowedName $OutputRootProperties $Name $true) {
        [void]$values.Add((New-Candidate 'output' '' $false))
        return ,$values
    }
    if ($script:PackagePathProperties.Contains($Name)) {
        [void]$values.Add((New-Candidate 'package' '' $false))
        return ,$values
    }
    $defined = $script:PropertyDefinitions.ContainsKey($Name)
    if ($script:TaskPropertyOutputs.ContainsKey($Name)) {
        if ($defined -or -not (Test-TaskOutputOnly $script:TaskPropertyOutputs $Name)) { return (Set-Unresolved ('$(' + $Name + ')')) }
        [void]$values.Add((New-Candidate 'output' '' $false))
        return ,$values
    }
    if (-not $defined) { return (Set-Unresolved ('$(' + $Name + ')')) }
    $conditional = $false
    foreach ($definition in $script:PropertyDefinitions[$Name]) {
        if ([bool]$definition.Conditional) { $conditional = $true }
        $inner = Expand-Candidates ([string]$definition.Value) (Get-FileDirectoryPrefix ([string]$definition.File)) $ProjectDir ($Depth + 1)
        if ($null -eq $inner) { return $null }
        foreach ($candidate in $inner) { [void]$values.Add($candidate) }
    }
    if ($conditional) { [void]$values.Add((New-Candidate 'repo' '' $false)) }
    return ,$values
}

# Resolves one repo candidate, evaluated from $ProjectDirectory, to a repository-relative pattern,
# 'outside' or 'escape'. An anchored candidate already starts at a known directory.
function Resolve-CandidatePattern([string]$Root, $Candidate, [string]$ProjectDirectory) {
    $text = ([string]$Candidate.Text).Replace('\', '/')
    if ($text -match '^[A-Za-z]:/' -or $text.StartsWith('/')) {
        $relative = ConvertTo-RepoRelative $text $Root
        if ($null -eq $relative) { return 'outside' }
        $text = $relative
    }
    elseif ([bool]$Candidate.Anchored) {
        if ($text.StartsWith('./')) { $text = $text.Substring(2) }
    }
    elseif (-not [string]::IsNullOrEmpty($ProjectDirectory)) {
        $text = $ProjectDirectory + '/' + $text
    }
    return Normalize-RepoPattern $text
}

function Find-NearestDirectoryBuild($Known, [string]$ProjectDirectory, [string]$FileName) {
    $dir = $ProjectDirectory
    while ($true) {
        if ([string]::IsNullOrEmpty($dir)) { $candidate = $FileName }
        else { $candidate = $dir + '/' + $FileName }
        if ($Known.ContainsKey($candidate)) { return $candidate }
        if ([string]::IsNullOrEmpty($dir)) { return $null }
        $slash = $dir.LastIndexOf('/')
        if ($slash -lt 0) { $dir = '' }
        else { $dir = $dir.Substring(0, $slash) }
    }
}

# CARD-1409: resolves one ';'-separated part of an Import Project attribute to a tracked,
# parsed project path or refuses. $ProjectAttr is the whole attribute text for messages.
function Resolve-ImportPart([string]$Root, [string]$FromFile, [string]$ProjectDirectory, [string]$ProjectAttr, [string]$Part, $Known) {
    $decoded = ConvertFrom-PercentEncoding $Part
    if ($null -eq $decoded) {
        Exit-Audit 1 (Format-ApprovalRefusal $FromFile 'Import' $ProjectAttr)
    }
    $text = $decoded.Replace('\', '/').Trim()
    $fromDir = Get-RepoDirectory $FromFile
    if ([string]::IsNullOrEmpty($fromDir)) { $fileDir = './' }
    else { $fileDir = $fromDir + '/' }
    if ([string]::IsNullOrEmpty($ProjectDirectory)) { $projectDir = '.' }
    else { $projectDir = $ProjectDirectory }
    $startsAnchored = $text -match '^(?i)\$\(\s*MSBuild(ThisFile|Project)Directory\s*\)'
    $text = [regex]::Replace($text, '(?i)\$\(\s*MSBuildThisFileDirectory\s*\)', (ConvertTo-RegexReplacement $fileDir))
    $text = [regex]::Replace($text, '(?i)\$\(\s*MSBuildProjectDirectory\s*\)', (ConvertTo-RegexReplacement $projectDir))
    if (($text.IndexOf('$(') -ge 0) -or ($text.IndexOf('@(') -ge 0) -or ($text.IndexOf('%(') -ge 0)) {
        Exit-Audit 1 (Format-UnresolvedImportRefusal $FromFile $ProjectAttr)
    }
    if (($text.IndexOf('*') -ge 0) -or ($text.IndexOf('?') -ge 0)) {
        Exit-Audit 1 (Format-WildcardImportRefusal $FromFile $ProjectAttr)
    }
    if ($text -match '^[A-Za-z]:/' -or $text.StartsWith('/')) {
        $relative = ConvertTo-RepoRelative $text $Root
        if ($null -eq $relative) {
            Exit-Audit 1 (Format-MissingImportRefusal $FromFile $ProjectAttr 'outside the repository')
        }
        $text = $relative
    }
    elseif ($startsAnchored) {
        if ($text.StartsWith('./')) { $text = $text.Substring(2) }
    }
    elseif (-not [string]::IsNullOrEmpty($fromDir)) {
        $text = $fromDir + '/' + $text
    }
    $norm = Normalize-RepoPattern $text
    if ($norm -eq 'outside' -or $norm -eq 'escape') {
        Exit-Audit 1 (Format-MissingImportRefusal $FromFile $ProjectAttr 'outside the repository')
    }
    if (-not $Known.ContainsKey($norm)) {
        Exit-Audit 1 (Format-MissingImportRefusal $FromFile $ProjectAttr $norm)
    }
    return $norm
}

function Add-ImporterBase($Map, [string]$Path, [string]$ProjectDirectory) {
    if (-not $Map.ContainsKey($Path)) {
        $Map[$Path] = New-Object 'System.Collections.Generic.List[string]'
    }
    $list = $Map[$Path]
    if (-not $list.Contains($ProjectDirectory)) { [void]$list.Add($ProjectDirectory) }
}

function Add-ImportQueue($Doc, [string]$FromFile, [string]$ProjectDirectory, $Queue, $Known, [string]$Root) {
    foreach ($node in $Doc.GetElementsByTagName('*')) {
        if ($node.LocalName -ne 'Import') { continue }
        $project = $node.GetAttribute('Project')
        foreach ($attribute in $node.Attributes) {
            $attributeName = [string]$attribute.Name
            $supported = $false
            foreach ($allowed in @('Project', 'Condition', 'Label')) {
                if ($attributeName.Equals($allowed, [System.StringComparison]::Ordinal)) { $supported = $true }
            }
            if (-not $supported) {
                Exit-Audit 1 (Format-UnsupportedImportAttributeRefusal $FromFile $project $attributeName ([string]$attribute.Value))
            }
        }
        # CARD-1409: MSBuild splits Project on ';' into several imports; every non-empty part is audited.
        $parts = New-Object 'System.Collections.Generic.List[string]'
        foreach ($part in ([string]$project).Split(';')) {
            $trimmed = $part.Trim()
            if ($trimmed.Length -gt 0) { [void]$parts.Add($trimmed) }
        }
        if ($parts.Count -eq 0) {
            Exit-Audit 1 (Format-MissingImportRefusal $FromFile $project '(empty)')
        }
        foreach ($part in $parts) {
            $target = Resolve-ImportPart $Root $FromFile $ProjectDirectory $project $part $Known
            if ([string]::IsNullOrEmpty([string]$target)) { continue }
            $Queue.Enqueue([string]$target)
        }
    }
}

function Get-ImportedProjectDirectories([string]$Root, $Known) {
    $bases = @{}
    foreach ($path in @($Known.Keys)) {
        if (-not (Test-ProjFile ([string]$path))) { continue }
        $projectDir = Get-RepoDirectory ([string]$path)
        $queue = New-Object 'System.Collections.Generic.Queue[string]'
        $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
        # CARD-1409: Microsoft.Common.props imports the nearest Directory.Packages.props (Review 23aedc52 D3).
        foreach ($name in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
            $hit = Find-NearestDirectoryBuild $Known $projectDir $name
            if ($null -ne $hit) { $queue.Enqueue([string]$hit) }
        }
        # CARD-1409: Microsoft.Common.targets imports a tracked <project>.user (Review 23aedc52 D4).
        $userFile = [string]$path + '.user'
        if ($Known.ContainsKey($userFile)) { $queue.Enqueue($userFile) }
        Add-ImportQueue $Known[$path] ([string]$path) $projectDir $queue $Known $Root
        while ($queue.Count -gt 0) {
            $current = [string]$queue.Dequeue()
            if (-not $seen.Add($current)) { continue }
            Add-ImporterBase $bases $current $projectDir
            if ($Known.ContainsKey($current)) {
                Add-ImportQueue $Known[$current] $current $projectDir $queue $Known $Root
            }
        }
    }
    return $bases
}

function Format-ApprovalRefusal([string]$Path, [string]$Item, [string]$Include, [string]$Resolved) {
    $text = 'definition file includes docs markdown or the root README.md; -Update cannot approve it: ' + $Path + ' ' + $Item + ' Include=' + $Include
    if (-not [string]::IsNullOrEmpty($Resolved)) { $text = $text + ' -> ' + $Resolved }
    return $text
}

function Format-DocumentRefusal([string]$Path, [string]$Item, [string]$Include, [string]$Matched) {
    return 'definition file item selects a markdown or text document the audit does not allow; -Update cannot approve it: ' + $Path + ' ' + $Item + ' Include=' + $Include + ' -> ' + $Matched
}

function Format-UnresolvedPropertyRefusal([string]$Path, [string]$Item, [string]$Include, [string]$Reference) {
    return 'definition file item include uses a property the audit cannot resolve; -Update cannot approve it: ' + $Path + ' ' + $Item + ' Include=' + $Include + ' ' + $Reference
}

function Format-MetadataRefusal([string]$Path, [string]$Item, [string]$Label, [string]$Name) {
    return 'definition file item carries metadata the audit does not allow; -Update cannot approve it: ' + $Path + ' ' + $Item + ' ' + $Label + ' ' + $Name
}

function Format-PropertyValueRefusal([string]$Path, [string]$Name, [string]$Detail) {
    return 'definition file property value names a docs path or reads a file the audit cannot classify; -Update cannot approve it: ' + $Path + ' ' + $Name + ' ' + $Detail
}

function Format-UnresolvedImportRefusal([string]$Path, [string]$ProjectAttr) {
    return 'definition file import cannot be resolved; -Update cannot approve it: ' + $Path + ' Import Project=' + $ProjectAttr
}

function Format-UnsupportedImportAttributeRefusal([string]$Path, [string]$ProjectAttr, [string]$Name, [string]$Value) {
    return 'definition file import carries an attribute the audit does not support; -Update cannot approve it: ' + $Path + ' Import Project=' + $ProjectAttr + ' ' + $Name + '=' + $Value
}

function Format-MissingImportRefusal([string]$Path, [string]$ProjectAttr, [string]$Resolved) {
    return 'definition file import target is not a tracked project file; -Update cannot approve it: ' + $Path + ' Import Project=' + $ProjectAttr + ' -> ' + $Resolved
}

function Format-WildcardImportRefusal([string]$Path, [string]$ProjectAttr) {
    return 'definition file import uses a wildcard the audit does not follow; -Update cannot approve it: ' + $Path + ' Import Project=' + $ProjectAttr
}

function Format-ObjDefinitionRefusal([string]$Path) {
    return 'definition file is tracked under an obj/ directory that MSBuild imports by wildcard; -Update cannot approve it: ' + $Path
}

function Format-IntermediateRedirectRefusal([string]$Path, [string]$Name) {
    return 'definition file redirects the intermediate directory, which the audit cannot evaluate; -Update cannot approve it: ' + $Path + ' ' + $Name
}

function Format-PropertyRefusal([string]$Path, [string]$Name) {
    return 'definition file sets a property the audit does not allow; -Update cannot approve it: ' + $Path + ' ' + $Name
}

function Format-ItemTypeRefusal([string]$Path, [string]$Item) {
    return 'definition file declares an item type the audit does not allow; -Update cannot approve it: ' + $Path + ' ' + $Item
}

function Format-UnclassifiedIncludeRefusal([string]$Path, [string]$Item, [string]$Include) {
    return 'definition file item include cannot be classified; -Update cannot approve it: ' + $Path + ' ' + $Item + ' Include=' + $Include
}

function Format-ProjectReferenceRefusal([string]$Path, [string]$Item, [string]$Include, [string]$Resolved) {
    return 'definition file project reference is not a tracked project file; -Update cannot approve it: ' + $Path + ' ' + $Item + ' Include=' + $Include + ' -> ' + $Resolved
}

function Format-ElementRefusal([string]$Path, [string]$Element) {
    return 'definition file contains an element the audit does not classify; -Update cannot approve it: ' + $Path + ' ' + $Element
}

function Format-SdkRefusal([string]$Path, [string]$Sdk) {
    return 'definition file uses an SDK the audit does not allow; -Update cannot approve it: ' + $Path + ' Sdk=' + $Sdk
}

function Format-ResponseFileRefusal([string]$Path, [string]$Line) {
    return 'response file switch the audit does not allow; -Update cannot approve it: ' + $Path + ' ' + $Line
}

# CARD-1409 (Review 31c77116): the root must be <Project> with an allow-listed Sdk attribute, and its
# children must be PropertyGroup, ItemGroup, Import, Target or an allow-listed <Sdk> element.
function Assert-ClassifiedStructure($Doc, [string]$Path) {
    $root = $Doc.DocumentElement
    if ($null -eq $root) { Exit-Audit 1 (Format-ElementRefusal $Path '(none)') }
    if (-not [string]::Equals([string]$root.LocalName, 'Project', [System.StringComparison]::Ordinal)) {
        Exit-Audit 1 (Format-ElementRefusal $Path ([string]$root.LocalName))
    }
    $sdkAttribute = [string]$root.GetAttribute('Sdk')
    if (-not [string]::IsNullOrWhiteSpace($sdkAttribute)) {
        foreach ($part in $sdkAttribute.Split(';')) {
            $name = $part.Trim()
            if ($name.Length -eq 0) { continue }
            if (-not (Test-AllowedName $AllowedProjectSdks $name $true)) { Exit-Audit 1 (Format-SdkRefusal $Path $sdkAttribute) }
        }
    }
    foreach ($child in $root.ChildNodes) {
        if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $local = [string]$child.LocalName
        if (-not (Test-AllowedName $AllowedProjectChildren $local $false)) { Exit-Audit 1 (Format-ElementRefusal $Path $local) }
        if ($local -eq 'Sdk') {
            $sdkName = ([string]$child.GetAttribute('Name')).Trim()
            if (-not (Test-AllowedName $AllowedSdkElements $sdkName $true)) { Exit-Audit 1 (Format-SdkRefusal $Path $sdkName) }
        }
    }
}

# CARD-1409 (Review 31c77116 R2): every PropertyGroup child, wherever the group sits, and every task
# Output PropertyName must be an allow-listed property; an Output ItemName must be an allow-listed item type.
function Assert-AllowedProperties($Doc, [string]$Path) {
    foreach ($group in $Doc.GetElementsByTagName('PropertyGroup')) {
        foreach ($child in $group.ChildNodes) {
            if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            $name = [string]$child.LocalName
            if (-not $AllowedProperties.Contains($name)) { Exit-Audit 1 (Format-PropertyRefusal $Path $name) }
        }
    }
    foreach ($output in $Doc.GetElementsByTagName('Output')) {
        $propertyName = ([string]$output.GetAttribute('PropertyName')).Trim()
        if ($propertyName.Length -gt 0 -and -not $AllowedProperties.Contains($propertyName)) {
            Exit-Audit 1 (Format-PropertyRefusal $Path $propertyName)
        }
        $itemName = ([string]$output.GetAttribute('ItemName')).Trim()
        if ($itemName.Length -gt 0 -and -not $ItemRules.ContainsKey($itemName)) {
            Exit-Audit 1 (Format-ItemTypeRefusal $Path $itemName)
        }
    }
}

# CARD-1409 (Review 31c77116 R3): every ItemGroup child, wherever the group sits, must be an allow-listed item type.
function Assert-AllowedItemTypes($Doc, [string]$Path) {
    foreach ($group in $Doc.GetElementsByTagName('ItemGroup')) {
        foreach ($child in $group.ChildNodes) {
            if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            $name = [string]$child.LocalName
            if (-not $ItemRules.ContainsKey($name)) { Exit-Audit 1 (Format-ItemTypeRefusal $Path $name) }
        }
    }
}

# A package-style include is a bare identifier: no path separator, property, wildcard, parent segment or drive.
function Test-ItemNameIsPathLike([string]$Segment) {
    $decoded = ConvertFrom-PercentEncoding $Segment
    if ($null -eq $decoded) { return $true }
    foreach ($needle in @('/', '\', '$(', '*', '?', '..', ':')) {
        if ($decoded.IndexOf($needle) -ge 0) { return $true }
    }
    return $false
}

# CARD-1409 repair 4 (Review 937bb1c9 F3): every attribute that is not an item-element attribute, and every
# child element, is metadata and must be allow-listed for the item type.
function Assert-ItemMetadataAllowed($Node, [string]$Path, [string]$Item, [string]$Label) {
    $allowed = @()
    if ($ItemMetadata.ContainsKey($Item)) { $allowed = $ItemMetadata[$Item] }
    foreach ($attribute in $Node.Attributes) {
        $attributeName = [string]$attribute.Name
        if (Test-AllowedName $ItemElementAttributes $attributeName $false) { continue }
        if (-not (Test-AllowedName $allowed $attributeName $true)) { Exit-Audit 1 (Format-MetadataRefusal $Path $Item $Label $attributeName) }
    }
    foreach ($child in $Node.ChildNodes) {
        if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $childName = [string]$child.LocalName
        if (-not (Test-AllowedName $allowed $childName $true)) { Exit-Audit 1 (Format-MetadataRefusal $Path $Item $Label $childName) }
    }
}

function Get-IncludeLabel($Node) {
    foreach ($name in @('Include', 'Update', 'Remove')) {
        $value = [string]$Node.GetAttribute($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { return ($name + '=' + $value) }
    }
    return 'Include='
}

# CARD-1409 repair 4 (Review 937bb1c9 F1): one expanded path-item candidate, evaluated from one directory.
# An output or package root accepts any tail that does not climb out of it. A repo path becomes a
# repository-relative pattern: the root README.md refuses; an exact path under docs/ refuses unless it is
# the allow-listed input; a wildcard pattern that can reach docs/ refuses whatever it matches today; the
# pattern is then expanded against the tracked tree, minus Exclude, and a match under docs/ or a
# markdown/text document outside the document allow-list refuses naming that match.
function Assert-PathCandidateAllowed([string]$Root, [string]$Path, [string]$Item, [string]$Segment, $Candidate, [string]$Directory, $ExcludeCandidates) {
    $kind = [string]$Candidate.Kind
    if ($kind -eq 'output' -or $kind -eq 'package') {
        $tail = ([string]$Candidate.Text).Replace('\', '/')
        if ($tail -match '^[A-Za-z]:') { Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment) }
        foreach ($part in $tail.Split('/')) {
            if ($part -eq '..') { Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment) }
        }
        return
    }
    $pattern = [string](Resolve-CandidatePattern $Root $Candidate $Directory)
    if ($pattern -eq 'escape') { Exit-Audit 1 (Format-ApprovalRefusal $Path $Item $Segment) }
    if ($pattern -eq 'outside') { Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment) }
    if (Test-PatternMatchesReadme $pattern) { Exit-Audit 1 (Format-ApprovalRefusal $Path $Item $Segment 'README.md') }
    if (-not (Test-PatternHasWildcard $pattern)) {
        if (Test-PathUnderDocs $pattern) {
            if (-not (Test-AllowedName $AllowedDocsInputs $pattern $true)) { Exit-Audit 1 (Format-ApprovalRefusal $Path $Item $Segment $pattern) }
            return
        }
        if ((Test-DocumentPath $pattern) -and -not (Test-DocumentAllowed $pattern $pattern)) {
            Exit-Audit 1 (Format-DocumentRefusal $Path $Item $Segment $pattern)
        }
        return
    }
    $excludePatterns = New-Object 'System.Collections.Generic.List[string]'
    foreach ($exclude in $ExcludeCandidates) {
        if ([string]$exclude.Kind -ne 'repo') { continue }
        $excludePattern = [string](Resolve-CandidatePattern $Root $exclude $Directory)
        if ($excludePattern -eq 'escape' -or $excludePattern -eq 'outside') { continue }
        [void]$excludePatterns.Add($excludePattern)
    }
    $hits = Get-TrackedMatches $pattern $excludePatterns
    if (Test-PatternCanReachDocs $pattern) {
        $named = $pattern
        foreach ($hit in $hits) {
            if ((Test-PathUnderDocs ([string]$hit)) -and -not (Test-AllowedName $AllowedDocsInputs ([string]$hit) $true)) { $named = [string]$hit; break }
        }
        Exit-Audit 1 (Format-ApprovalRefusal $Path $Item $Segment $named)
    }
    if (Test-AllowedName $AllowedDocumentPatterns $pattern $true) { return }
    foreach ($hit in $hits) {
        $hitPath = [string]$hit
        if (Test-PathUnderDocs $hitPath) { Exit-Audit 1 (Format-ApprovalRefusal $Path $Item $Segment $hitPath) }
        if ((Test-DocumentPath $hitPath) -and -not (Test-DocumentAllowed $pattern $hitPath)) {
            Exit-Audit 1 (Format-DocumentRefusal $Path $Item $Segment $hitPath)
        }
    }
}

# CARD-1409 (Review 31c77116): one ';'-separated include or update part under its item type's rule,
# expanded (repair 4) and resolved from every directory the item is evaluated from. Every refusal names
# the file, the item type and the part.
function Assert-ItemIncludeAllowed([string]$Root, [string]$Path, [string]$Item, [string]$Rule, [string]$Segment, $Directories, $Known, [string]$Exclude) {
    if (($Segment.IndexOf('@(') -ge 0) -or ($Segment.IndexOf('%(') -ge 0)) {
        Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment)
    }
    if ($Rule -eq 'text') { return }
    if ($Rule -eq 'name') {
        if (Test-ItemNameIsPathLike $Segment) { Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment) }
        return
    }
    $decoded = ConvertFrom-PercentEncoding $Segment
    if ($null -eq $decoded) { Exit-Audit 1 (Format-ApprovalRefusal $Path $Item $Segment) }
    $fileDir = Get-FileDirectoryPrefix $Path
    foreach ($directory in $Directories) {
        $candidates = Expand-Candidates $decoded $fileDir ([string]$directory) 0
        if ($null -eq $candidates) { Exit-Audit 1 (Format-UnresolvedPropertyRefusal $Path $Item $Segment $script:UnresolvedReference) }
        $excludeCandidates = New-Object 'System.Collections.Generic.List[object]'
        if (-not [string]::IsNullOrWhiteSpace($Exclude)) {
            foreach ($excludePart in $Exclude.Split(';')) {
                $excludeText = $excludePart.Trim()
                if ($excludeText.Length -eq 0) { continue }
                if (($excludeText.IndexOf('@(') -ge 0) -or ($excludeText.IndexOf('%(') -ge 0)) { continue }
                $excludeDecoded = ConvertFrom-PercentEncoding $excludeText
                if ($null -eq $excludeDecoded) { continue }
                $expanded = Expand-Candidates $excludeDecoded $fileDir ([string]$directory) 0
                if ($null -eq $expanded) { continue }
                foreach ($candidate in $expanded) { [void]$excludeCandidates.Add($candidate) }
            }
        }
        foreach ($candidate in $candidates) {
            if ($Rule -eq 'project') {
                if ([string]$candidate.Kind -ne 'repo') { Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment) }
                $resolved = [string](Resolve-CandidatePattern $Root $candidate ([string]$directory))
                if (($resolved -eq 'escape') -or ($resolved -eq 'outside') -or (Test-PatternHasWildcard $resolved)) {
                    Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment)
                }
                if ((-not (Test-ProjFile $resolved)) -or (-not $Known.ContainsKey($resolved))) {
                    Exit-Audit 1 (Format-ProjectReferenceRefusal $Path $Item $Segment $resolved)
                }
                continue
            }
            Assert-PathCandidateAllowed $Root $Path $Item $Segment $candidate ([string]$directory) $excludeCandidates
        }
    }
}

# CARD-1409 repair 4 (Review 937bb1c9, D-18 items closed cheaply): a property value that reads the file
# system through a property function must read under an output root, and a value part that is a literal
# path (after the two directory properties) must not be a docs/ path, the root README.md, or a wildcard
# that can reach them. Other references in a value are left alone: a value is not an input by itself,
# and an include that uses it is expanded and classified where it is used.
function Assert-PropertyValuesAllowed([string]$Root, $Doc, [string]$Path, $Directories) {
    $fileDir = Get-FileDirectoryPrefix $Path
    $outputRooted = '^\$\(\s*(' + ($OutputRootProperties -join '|') + ')\s*\)'
    foreach ($group in $Doc.GetElementsByTagName('PropertyGroup')) {
        foreach ($child in $group.ChildNodes) {
            if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            $name = [string]$child.LocalName
            $value = ([string]$child.InnerText).Trim()
            if ($value.Length -eq 0) { continue }
            foreach ($call in [regex]::Matches($value, '::\s*([A-Za-z]+)\s*\(\s*(?:''([^'']*)''|([^,)]*))')) {
                $function = $call.Groups[1].Value
                if (-not (Test-AllowedName $FileReadingFunctions $function $true)) { continue }
                $argument = $call.Groups[2].Value
                if ($argument.Length -eq 0) { $argument = $call.Groups[3].Value.Trim() }
                if (-not ($argument -match $outputRooted)) {
                    Exit-Audit 1 (Format-PropertyValueRefusal $Path $name ($function + '(' + $argument + ')'))
                }
            }
            foreach ($part in $value.Split(';')) {
                $text = $part.Trim()
                if ($text.Length -eq 0) { continue }
                if (($text.IndexOf('@(') -ge 0) -or ($text.IndexOf('%(') -ge 0) -or ($text.IndexOf('$([') -ge 0)) { continue }
                $stripped = [regex]::Replace($text, '(?i)\$\(\s*MSBuild(ThisFile|Project)Directory\s*\)', '')
                if ($stripped.IndexOf('$(') -ge 0) { continue }
                # A wildcard in a value is an exclude list or a glob some include expands where it is used,
                # and a bare word (true, Exe, net10.0) is not a file reference; only a part with a path
                # separator or a document extension is resolved, from the file's own directory. An include
                # that uses the value is expanded from every importing project directory where it is used.
                if (Test-PatternHasWildcard $stripped) { continue }
                $normalizedPart = $stripped.Replace('\', '/')
                if (($normalizedPart.IndexOf('/') -lt 0) -and -not (Test-DocumentPath $normalizedPart)) { continue }
                $decoded = ConvertFrom-PercentEncoding $text
                if ($null -eq $decoded) { continue }
                $candidates = Expand-Candidates $decoded $fileDir (Get-RepoDirectory $Path) 0
                if ($null -eq $candidates) { continue }
                foreach ($candidate in $candidates) {
                    if ([string]$candidate.Kind -ne 'repo') { continue }
                    $pattern = [string](Resolve-CandidatePattern $Root $candidate (Get-RepoDirectory $Path))
                    if ($pattern -eq 'escape' -or $pattern -eq 'outside') { continue }
                    $docs = (Test-PathUnderDocs $pattern) -or ($pattern -ieq 'README.md')
                    if ($docs -and -not (Test-AllowedName $AllowedDocsInputs $pattern $true)) {
                        Exit-Audit 1 (Format-PropertyValueRefusal $Path $name ('-> ' + $pattern))
                    }
                }
            }
        }
    }
}

# CARD-1409 (Review 31c77116 R1): one response-file line is one switch. Comments start with '#'.
function Test-ResponseSwitchAllowed([string]$Line) {
    if (-not ($Line.StartsWith('-') -or $Line.StartsWith('/'))) { return $false }
    $body = $Line.Substring(1)
    $colon = $body.IndexOf(':')
    if ($colon -lt 0) {
        $name = $body
        $value = $null
    }
    else {
        $name = $body.Substring(0, $colon)
        $value = $body.Substring($colon + 1)
    }
    switch ($name.ToLowerInvariant()) {
        'nodereuse' { return (($value -eq 'true') -or ($value -eq 'false')) }
        'nr' { return (($value -eq 'true') -or ($value -eq 'false')) }
        'maxcpucount' { return (($null -eq $value) -or ($value -match '^[1-9][0-9]{0,3}$')) }
        'm' { return (($null -eq $value) -or ($value -match '^[1-9][0-9]{0,3}$')) }
        'verbosity' { return (@('q', 'quiet', 'm', 'minimal', 'n', 'normal', 'd', 'detailed', 'diag', 'diagnostic') -contains $value) }
        'v' { return (@('q', 'quiet', 'm', 'minimal', 'n', 'normal', 'd', 'detailed', 'diag', 'diagnostic') -contains $value) }
        'nologo' { return ($null -eq $value) }
    }
    return $false
}

function Assert-ResponseFileAllowed([string]$Path, [string]$Content) {
    $text = $Content
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
    foreach ($raw in $text.Split([char]10)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0) { continue }
        if ($line.StartsWith('#')) { continue }
        if (-not (Test-ResponseSwitchAllowed $line)) { Exit-Audit 1 (Format-ResponseFileRefusal $Path $line) }
    }
}

function Get-GitBytes([string]$Root, [string]$Arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.Arguments = $Arguments
    $psi.WorkingDirectory = $Root
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    try {
        $started = $proc.Start()
    }
    catch {
        return $null
    }
    if (-not $started) { return $null }
    $errTask = $proc.StandardError.ReadToEndAsync()
    $ms = New-Object System.IO.MemoryStream
    try {
        $proc.StandardOutput.BaseStream.CopyTo($ms)
        $proc.WaitForExit()
        $null = $errTask.Result
    }
    catch {
        try { $proc.Kill() } catch { }
        return $null
    }
    if ($proc.ExitCode -ne 0) { return $null }
    return ,$ms.ToArray()
}

function Assert-NoDocumentationInput([string]$Root, [string[]]$Rows) {
    $utf = New-Object System.Text.UTF8Encoding $false
    $known = @{}
    foreach ($row in $Rows) {
        $tab = $row.IndexOf([char]9)
        $meta = $row.Substring(0, $tab).Split(' ')
        $path = $row.Substring($tab + 1)
        # CARD-1409 (Review 31c77116 R1): a tracked response file is parsed, not only hashed.
        if (Test-ResponseFile $path) {
            $responseBytes = Get-GitBytes $Root ('cat-file blob ' + $meta[2])
            if ($null -eq $responseBytes) { Exit-Audit 2 'git cat-file failed' }
            Assert-ResponseFileAllowed $path ($utf.GetString($responseBytes))
            continue
        }
        if (-not (Test-ProjectXml $path)) { continue }
        $bytes = Get-GitBytes $Root ('cat-file blob ' + $meta[2])
        if ($null -eq $bytes) { Exit-Audit 2 'git cat-file failed' }
        $content = $utf.GetString($bytes)
        $doc = New-Object System.Xml.XmlDocument
        $doc.XmlResolver = $null
        try {
            $doc.LoadXml($content)
        }
        catch {
            Exit-Audit 1 ('definition file is not readable project XML and cannot be approved: ' + $path)
        }
        $redirect = Get-IntermediateRedirectElement $doc
        if ($null -ne $redirect) {
            Exit-Audit 1 (Format-IntermediateRedirectRefusal $path ([string]$redirect))
        }
        # CARD-1409 (Review 31c77116): default-deny over the structure, the property names and the item types.
        Assert-ClassifiedStructure $doc $path
        Assert-AllowedProperties $doc $path
        Assert-AllowedItemTypes $doc $path
        # CARD-1409 repair 4: the census every include expansion reads; complete before any file is classified.
        Register-DefinitionFacts $doc $path
        $known[$path] = $doc
    }
    $bases = Get-ImportedProjectDirectories $Root $known
    foreach ($path in @($known.Keys)) {
        $doc = $known[$path]
        $directories = New-Object 'System.Collections.Generic.List[string]'
        [void]$directories.Add((Get-RepoDirectory ([string]$path)))
        if ($bases.ContainsKey([string]$path)) {
            foreach ($dir in $bases[[string]$path]) {
                if (-not $directories.Contains([string]$dir)) { [void]$directories.Add([string]$dir) }
            }
        }
        Assert-PropertyValuesAllowed $Root $doc ([string]$path) $directories
        foreach ($group in $doc.GetElementsByTagName('ItemGroup')) {
            foreach ($node in $group.ChildNodes) {
                if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
                $local = [string]$node.LocalName
                $rule = [string]$ItemRules[$local]
                Assert-ItemMetadataAllowed $node ([string]$path) $local (Get-IncludeLabel $node)
                # Include and Update are classified; Remove only subtracts and is left alone.
                foreach ($attributeName in @('Include', 'Update')) {
                    $include = $node.GetAttribute($attributeName)
                    if ([string]::IsNullOrWhiteSpace($include)) { continue }
                    $exclude = ''
                    if ($attributeName -eq 'Include') { $exclude = [string]$node.GetAttribute('Exclude') }
                    foreach ($segment in $include.Split(';')) {
                        $segment = $segment.Trim()
                        if ($segment.Length -eq 0) { continue }
                        Assert-ItemIncludeAllowed $Root ([string]$path) $local $rule $segment $directories $known $exclude
                    }
                }
            }
        }
    }
}

$bytes = Get-GitBytes $RepoRoot 'ls-tree -r -z HEAD'
if ($null -eq $bytes) {
    Exit-Audit 2 'git ls-tree failed'
}

$utf8 = New-Object System.Text.UTF8Encoding $false
$text = $utf8.GetString($bytes)
$trailing = [string]([char]0)
if ($text.Length -eq 0 -or -not $text.EndsWith($trailing, [System.StringComparison]::Ordinal)) {
    Exit-Audit 1 'ls-tree output is empty or missing its trailing NUL'
}
if ($text.IndexOf([char]0xFFFD) -ge 0) {
    Exit-Audit 1 'ls-tree output is not valid UTF-8'
}

$parts = $text.Split([char]0)
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$selected = New-Object 'System.Collections.Generic.List[string]'
$last = $parts.Length - 1
for ($i = 0; $i -lt $last; $i++) {
    $row = $parts[$i]
    $tab = $row.IndexOf([char]9)
    if ($tab -lt 0) { Exit-Audit 1 'ls-tree row has no tab' }
    $meta = $row.Substring(0, $tab).Split(' ')
    $path = $row.Substring($tab + 1)
    if ($meta.Length -ne 3) { Exit-Audit 1 'ls-tree metadata is not mode type object' }
    if (-not (Test-SafePath $path)) { Exit-Audit 1 'ls-tree path is not safe' }
    if (-not $seen.Add($path)) { Exit-Audit 1 'duplicate path' }
    $mode = $meta[0]
    $kind = $meta[1]
    $oid = $meta[2]
    $blob = ($kind -eq 'blob') -and ($mode -eq '100644' -or $mode -eq '100755' -or $mode -eq '120000')
    $gitlink = ($kind -eq 'commit') -and ($mode -eq '160000')
    if ((-not $blob) -and (-not $gitlink)) { Exit-Audit 1 'ls-tree row is not a blob or commit' }
    if (-not (Test-FullObjectId $oid)) { Exit-Audit 1 'ls-tree object id is not a full id' }
    # CARD-1409 repair 4: every tracked blob is what an item glob is expanded against.
    if ($blob) { [void]$script:TrackedPaths.Add($path) }
    if (-not (Test-DefinitionPath $path)) { continue }
    if (Test-PathUnderObjDirectory $path) { Exit-Audit 1 (Format-ObjDefinitionRefusal $path) }
    $regular = ($mode -eq '100644' -or $mode -eq '100755') -and ($kind -eq 'blob')
    if (-not $regular) { Exit-Audit 1 'definition path is not a regular blob' }
    [void]$selected.Add($row)
}

$arr = $selected.ToArray()
[System.Array]::Sort($arr, [System.StringComparer]::Ordinal)
Assert-NoDocumentationInput $RepoRoot $arr
$payload = [string]::Join($trailing, $arr) + $trailing
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($payload))
}
finally {
    $sha.Dispose()
}
$hex = ([System.BitConverter]::ToString($hash)).Replace('-', '')
$count = $arr.Length
Write-Output ("digest=" + $hex + " count=" + $count)

if ($Update.IsPresent) {
    $body = $hex + "`n" + $count.ToString([System.Globalization.CultureInfo]::InvariantCulture) + "`n"
    $manifestDir = [System.IO.Path]::GetDirectoryName($ManifestPath)
    if (-not [System.IO.Directory]::Exists($manifestDir)) {
        [void][System.IO.Directory]::CreateDirectory($manifestDir)
    }
    [System.IO.File]::WriteAllText($ManifestPath, $body, [System.Text.Encoding]::ASCII)
    exit 0
}

if (-not [System.IO.File]::Exists($ManifestPath)) {
    Exit-Audit 1 'manifest missing'
}
$lines = [System.IO.File]::ReadAllLines($ManifestPath)
if ($lines.Length -ne 2) { Exit-Audit 1 'manifest must be two lines' }
$manifestDigest = $lines[0]
$manifestCountText = $lines[1]
if ($manifestDigest -cnotmatch '^[0-9A-F]{64}$') {
    Exit-Audit 1 'manifest digest is not 64 uppercase hex characters'
}
if ($manifestCountText -notmatch '^[0-9]+$') {
    Exit-Audit 1 'manifest count is not a decimal integer'
}
$manifestCount = [int]::Parse($manifestCountText, [System.Globalization.CultureInfo]::InvariantCulture)
if ($manifestDigest -cne $hex -or $manifestCount -ne $count) {
    Exit-Audit 1 'manifest does not match the selected graph'
}
exit 0
