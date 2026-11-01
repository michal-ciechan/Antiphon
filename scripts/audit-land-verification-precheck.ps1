# CARD-1483 S5: internal emulation pre-check. This is not evaluation approval.
# The production entry point is audit-land-verification-graph.ps1, which requires both checks.
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
# CARD-1409 repair 5 (Review f0fb9035): one path normaliser (ConvertTo-CanonicalPath) is the only
# way resolved text becomes a repository path, and every comparison consumes its result: include and
# update parts, Exclude parts, import targets, project references, literal property-value paths, the
# tail of an output- or package-rooted path, every tracked match a wildcard selects and every tracked
# definition path. Each segment is held to the Windows land host's rules first: a trailing '.' or
# space, a segment of only dots or spaces, a ':' anywhere (the drive prefix is converted before this
# point) and an 8.3 short name ('~' then a digit) refuse naming the segment; '.' and '..' collapse only
# afterwards; letter case is kept and compared ordinal-ignore-case. A property value is free text, so
# there a divergent segment is read as the host reads it and refuses only when that reading is docs/
# or the root README.md. The MSBuild item-operation surface is a closed table (plan D-29): Include and
# Update are censused, including Update metadata for %(Item.Metadata) and Pkg* expansion; Exclude
# subtracts only; Remove subtracts only and may carry no metadata; KeepMetadata, RemoveMetadata,
# KeepDuplicates, MatchOnMetadata and MatchOnMetadataOptions refuse by name; an element with no
# operation refuses; any other attribute is metadata and must be allow-listed. A path item whose
# resolved pattern reaches a tracked symbolic link (mode 120000), as a match or as a directory it
# passes through, refuses naming the link.
# CARD-1409 repair 6 (Review c64987ea): one pipeline (Resolve-ItemPathText) turns every item or import text
# into path specs in MSBuild's order, read from a scratch 'dotnet msbuild -getItem' run: expand properties
# and metadata (values spliced in their escaped form), split the expanded text on ';' (an escaped %3B is not
# a separator; a ';' inside an expanded value is), decode %XX once per part (two hex digits in either case
# become one character; %252E is the literal %2E; a malformed %X or %GG stays literal; %00 refuses by name),
# then normalise (D-28). A decoded $( @( %( is literal text, never a reference. An escaped * or ? is literal
# for MSBuild; the audit reads the decoded include as a wildcard, which selects at least the literal name,
# and drops an Exclude part whose decoded text gained a wildcard character. A root belongs to the first part
# only. Every consumer runs through the pipeline and it is the only ';' split: Include, Update, Exclude,
# Import Project, ProjectReference, name items, Pkg* ids, literal property-value paths, the tail of a
# file-reading function argument (which may not climb out of its output root; a property no tracked file
# may set is the SDK's own, read as one opaque segment) and output-root tails. CARD-1409 repair 7 (Review
# 7db88932): the D-25 skip of a value part that carries an item, metadata or property-function reference is
# per part, so the other parts of the same value are still classified. CARD-1409 repair 8 (Review d43d1e4a):
# with the two directory properties only (imports, values, name items, Pkg* ids), the text is split on ';'
# first and a directory property at the start of each part is that directory, as MSBuild expands then splits.
# CARD-1409 repair 9 (Review 4ecdb753): MSBuild escapes % $ @ ; ' ( ) * ? in $(MSBuildProjectDirectory) and not
# in $(MSBuildThisFileDirectory), so a tracked definition path holding one refuses by name (char=<c>) before any
# file is read; every spliced directory and every import or reference target is such a path.
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
    [switch]$Update,
    [string]$ManifestPath
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
if ([string]::IsNullOrEmpty($ManifestPath)) {
    $ManifestPath = [System.IO.Path]::Combine($RepoRoot, 'server', 'Application', 'Services', 'LandVerificationBuildGraph.manifest.txt')
}

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
    '_StagedHelperFramework', '_StagedHelperGlob', '_StagedHelperShared', 'AntiphonCleanFileMaxBytes', 'AntiphonMessagingVersion',
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
# CARD-1483 (Review cff23aba D-1): property -> the only element texts (ordinal) it may be set to. The value
# reaches an MSBuild task Properties string, which splits on ';'; a task Output may not set it.
$PinnedPropertyValues = @{ '_StagedHelperFramework' = @('%(StagedHelper.HelperTargetFramework)', '$(TargetFramework)') }
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
# SDK internals are not general item types. Only the four exact removals in the
# separately pinned fake-provider target are admitted; task outputs still refuse.
$SdkRemovalItems = @('_SourceItemsToCopyToOutputDirectory', '_SourceItemsToCopyToOutputDirectoryAlways',
    '_SourceItemsToCopyToOutputDirectoryIfDifferent', 'AllItemsFullPathWithTargetPath')
# CARD-1409 repair 4 (Review 937bb1c9 F3): item metadata names, as attributes or child elements, per
# item type, seeded from the tracked definition files. A type absent here allows none. The global-property
# metadata (AdditionalProperties, Properties, GlobalProperties, SetConfiguration, SetPlatform,
# SetTargetFramework, UndefineProperties) and every other name refuse: they reopen the R2 property imports.
# CARD-1483: the exceptions are the pinned entries below, each allowed only as its one tree literal
# (the LandGraphAudit tool is built for net10.0); any other value, the child-element form or a Remove refuses.
$ItemMetadata = New-Object 'System.Collections.Generic.Dictionary[string,string[]]' ([System.StringComparer]::OrdinalIgnoreCase)
$ItemMetadata['PackageReference'] = @('Version', 'GeneratePathProperty', 'ExcludeAssets', 'PrivateAssets', 'IncludeAssets')
# Aspire uses IsAspireProjectResource only for application-resource registration/validation;
# it neither changes the referenced project nor supplies global properties to its evaluation.
$ItemMetadata['ProjectReference'] = @('Aliases', 'ReferenceOutputAssembly', 'Private', 'IsAspireProjectResource')
$ItemMetadata['Content'] = @('CopyToOutputDirectory', 'CopyToPublishDirectory', 'Link', 'TargetPath')
$ItemMetadata['Compile'] = @('Link')
$ItemMetadata['EmbeddedResource'] = @('LogicalName')
$ItemMetadata['None'] = @('Pack', 'PackagePath', 'CopyToOutputDirectory')
$ItemMetadata['StagedHelper'] = @('Folder', 'Glob')
# Item type -> metadata attribute name -> the only value (ordinal) under which that name is allowed.
# HelperTargetFramework and SetTargetFramework reach an MSBuild Properties string, which splits on ';'
# (Review cff23aba D-1); SkipGetTargetFrameworkProperties is held to its tree value as well.
$PinnedItemMetadata = @{
    'ProjectReference' = @{ 'SetTargetFramework' = 'TargetFramework=net10.0'; 'SkipGetTargetFrameworkProperties' = 'true' }
    'StagedHelper' = @{ 'HelperTargetFramework' = 'net10.0' }
    # CARD-1613: *.cs.txt is Czech to AssignCulture. The fixture include and the post-split
    # target hold culture off. Only these exact attribute values are admitted; any other value,
    # the child-element form, or a Remove still refuses.
    'EmbeddedResource' = @{ 'WithCulture' = 'false'; 'Culture' = '' }
}
# Item element attributes that are not metadata (compared ordinally, as MSBuild reads them).
$ItemElementAttributes = @('Include', 'Exclude', 'Update', 'Remove', 'Condition', 'Label')
# CARD-1409 repair 5 (plan D-29, O-table): item operations the audit does not evaluate, refused by name.
# Every other attribute is metadata and must be allow-listed above, so no operation is left unclassified.
$RefusedItemOperations = @('KeepMetadata', 'RemoveMetadata', 'KeepDuplicates', 'MatchOnMetadata', 'MatchOnMetadataOptions')
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
    'tests/Antiphon.Messaging.Tests/Fixtures/*',
    'tests/Antiphon.Tests/Fixtures/NativeLinuxPolicy/*.cs.txt')
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

# Walks pattern segments against target segments. With $Prefix the walk succeeds as soon as every target
# segment is consumed, whatever pattern remains: the pattern then selects the target itself or something
# beneath it (CARD-1409 repair 5, D-30).
function Test-GlobWalk($Pattern, [int]$Index, $Target, [int]$TargetIndex, [bool]$Prefix) {
    while ($true) {
        if ($Prefix -and ($TargetIndex -ge $Target.Count)) { return $true }
        if ($Index -ge $Pattern.Count) { return ($TargetIndex -ge $Target.Count) }
        $seg = [string]$Pattern[$Index]
        if ($seg -eq '**') {
            if (Test-GlobWalk $Pattern ($Index + 1) $Target $TargetIndex $Prefix) { return $true }
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

# CARD-1409 repair 5 (D-30): the first tracked symbolic link the canonical pattern selects or passes through.
function Find-SymlinkReached([string]$Pattern) {
    if ($script:SymlinkPaths.Count -eq 0) { return $null }
    $segs = New-SegmentList $Pattern
    foreach ($link in $script:SymlinkPaths) {
        $target = New-SegmentList ([string]$link)
        if (Test-GlobWalk $segs 0 $target 0 $true) { return [string]$link }
    }
    return $null
}

function Test-PatternMatchesReadme([string]$Pattern) {
    $target = New-Object 'System.Collections.Generic.List[string]'
    [void]$target.Add('README.md')
    $segs = New-SegmentList $Pattern
    return Test-GlobWalk $segs 0 $target 0
}

# CARD-1409 repair 5 (Review f0fb9035 F1, plan D-28): the one path normaliser. Every path the audit
# compares goes through here before any comparison: an include or update part, an Exclude part, an
# import target, a project reference, a literal property-value path, the tail of a path rooted at an
# output or package root, every tracked match a wildcard selects and every tracked definition path. The
# text is split on '/' and '\' (equal) and each segment is held to the Windows land host's rules: a
# trailing '.' or space, a segment of only dots or spaces, a ':' anywhere (the drive prefix is handled
# by the caller, so it is the only legal colon) and an 8.3 short-name shape ('~' then a digit) each
# REFUSE naming the segment; nothing is silently rewritten. Only then are '.' and empty segments
# dropped and '..' popped. Letter case is kept; every comparison of a canonical path is
# ordinal-ignore-case. Returns Status ok with Path, outside ('..' above the root), escape ('..' after a
# wildcard segment) or refused with Segment and Reason. With $Fold (property values only, D-25: a value
# is free text, not an input) a divergent segment is read as the host reads it instead of refusing:
# trailing dots and spaces are dropped, a colon cuts the segment to the name before it, and the first
# divergence is returned as Divergent so a docs or README reading can still be refused by segment; an
# 8.3 short name refuses even then, because its long name is unknowable.
function ConvertTo-CanonicalPath([string]$Text, [bool]$Fold) {
    $stack = New-Object 'System.Collections.Generic.List[string]'
    $divergent = $null
    foreach ($part in $Text.Replace('\', '/').Split('/')) {
        if ($part.Length -eq 0 -or $part -eq '.') { continue }
        if ($part -eq '..') {
            if ($stack.Count -eq 0) { return @{ Status = 'outside' } }
            $top = [string]$stack[$stack.Count - 1]
            if (Test-PatternHasWildcard $top) { return @{ Status = 'escape' } }
            $stack.RemoveAt($stack.Count - 1)
            continue
        }
        if ($part -match '~[0-9]') { return @{ Status = 'refused'; Segment = $part; Reason = '8.3 short name' } }
        $colon = $part.IndexOf(':')
        if ($colon -ge 0) {
            if (-not $Fold) { return @{ Status = 'refused'; Segment = $part; Reason = 'colon' } }
            if ($null -eq $divergent) { $divergent = @{ Segment = $part; Reason = 'colon' } }
            $part = $part.Substring(0, $colon)
        }
        $trimmed = $part.TrimEnd('.', ' ')
        if ($trimmed.Length -ne $part.Length) {
            $reason = 'trailing dot or space'
            if ($trimmed.Length -eq 0) { $reason = 'only dots or spaces' }
            if (-not $Fold) { return @{ Status = 'refused'; Segment = $part; Reason = $reason } }
            if ($null -eq $divergent) { $divergent = @{ Segment = $part; Reason = $reason } }
            $part = $trimmed
        }
        if ($part.Length -eq 0) { continue }
        [void]$stack.Add($part)
    }
    if ($stack.Count -eq 0) { return @{ Status = 'outside' } }
    return @{ Status = 'ok'; Path = ($stack -join '/'); Divergent = $divergent }
}

function Test-CanonicalRefused($Canonical) {
    return ([string]$Canonical.Status -eq 'refused')
}

function Format-SegmentDetail($Canonical) {
    return 'segment=' + [string]$Canonical.Segment + ' (' + [string]$Canonical.Reason + ')'
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

# CARD-1409 repair 6 (Review c64987ea F1, plan D-33): MSBuild's unescape, read from a scratch 'dotnet msbuild
# -getItem' run. Two hex digits after '%', in either case, become one character of that code (%C3%A9 is two
# characters); anything else after '%' stays literal; %252E becomes the literal %2E. Returns $null for %00,
# which MSBuild keeps as a NUL character and the audit refuses by name. Called from Resolve-ItemPathText
# only, so every path text is decoded exactly once, after expansion and after the ';' split, as MSBuild does.
function ConvertFrom-PercentEncoding([string]$Value) {
    $sb = New-Object System.Text.StringBuilder
    $i = 0
    while ($i -lt $Value.Length) {
        $c = $Value[$i]
        if (($c -eq '%') -and (($i + 2) -lt $Value.Length)) {
            $n = ConvertFrom-HexByte $Value.Substring($i + 1, 2)
            if ($n -ge 0) {
                if ($n -eq 0) { return $null }
                [void]$sb.Append([char]$n)
                $i = $i + 3
                continue
            }
        }
        [void]$sb.Append($c)
        $i = $i + 1
    }
    return $sb.ToString()
}

# CARD-1409 repair 4 (Review 937bb1c9 F2): the census of every parsed definition file, filled before any
# include is classified: property definitions, task outputs, declared items and package path properties.
$script:PropertyDefinitions = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:TaskPropertyOutputs = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:TaskItemOutputs = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:ItemDeclarations = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:PackagePathProperties = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$script:TrackedPaths = New-Object 'System.Collections.Generic.List[string]'
$script:SymlinkPaths = New-Object 'System.Collections.Generic.List[string]'
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
            # CARD-1409 repair 5 (Review f0fb9035 F2): an Update sets metadata on the items it names, static
            # or inside a Target, so its values are declared values too; Remove sets nothing.
            $include = [string]$node.GetAttribute('Include')
            if ([string]::IsNullOrWhiteSpace($include)) { $include = [string]$node.GetAttribute('Update') }
            if ([string]::IsNullOrWhiteSpace($include)) { continue }
            $metadata = Get-ItemMetadataValues $node
            Add-CensusEntry $script:ItemDeclarations ([string]$node.LocalName) @{ File = $Path; Include = $include; Metadata = $metadata }
            if ([string]$node.LocalName -ieq 'PackageReference') {
                $generate = ''
                if ($metadata.ContainsKey('GeneratePathProperty')) { $generate = [string]$metadata['GeneratePathProperty'] }
                if ($generate -ieq 'true') {
                    # D-33: the id is item text, split and decoded once by the pipeline before it names a property.
                    foreach ($id in (Resolve-ItemPathText $include '' '' $true)) {
                        if (([string]$id.Kind -ne 'repo') -or [bool]$id.Anchored) { continue }
                        [void]$script:PackagePathProperties.Add('Pkg' + ([string]$id.Text).Replace('.', '_'))
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

# Expands one text (an include or a property value) into every value it can take: candidates tagged repo
# (a path inside the tree), output (rooted at a build output or intermediate directory) or package (rooted
# at a restored package folder). The text and every spliced value stay in their escaped form; the pipeline
# (Resolve-ItemPathText, D-33) splits and decodes afterwards. $FileDir is what $(MSBuildThisFileDirectory) means in
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

# CARD-1409 repair 6 (Review c64987ea F1, plan D-33): the one pipeline that turns item or import text into
# path specs, in MSBuild's order, which a scratch 'dotnet msbuild -getItem' run confirmed: expand properties
# and metadata (values are spliced in their escaped form, as MSBuild stores them), split the expanded text
# on ';' (an escaped %3B is not a separator; a ';' inside an expanded value is), decode %XX once per part,
# and only then compare through ConvertTo-CanonicalPath. A decoded '$(' '@(' '%(' is literal text, never a
# reference. A root (a directory property, an output or package root) belongs to the first part of its
# expanded text; later parts are ordinary project-relative paths. With $DirectoryPropertiesOnly the
# expansion knows $(MSBuildThisFileDirectory) and $(MSBuildProjectDirectory) at the start of each part and
# nothing else (imports, D-1; literal property values, D-25; name items; Pkg* ids): a part that still carries
# a reference is returned with Kind unresolved for the consumer to refuse or skip. Returns $null with
# $script:UnresolvedReference set when the full expansion cannot resolve a reference; otherwise a list of
# specs with Kind (repo, output, package, unresolved, or nul for a part that decodes to a NUL character),
# Text (decoded), Anchored and Source (the escaped part, for messages).
function Resolve-ItemPathText([string]$Text, [string]$FileDir, [string]$ProjectDir, [bool]$DirectoryPropertiesOnly) {
    if ($DirectoryPropertiesOnly) {
        # CARD-1409 repair 8 (Review d43d1e4a R2, D-39): the directory values carry no ';', so splitting first
        # and expanding a directory property at the start of each part below is MSBuild's expand-then-split.
        $candidates = New-Object 'System.Collections.Generic.List[object]'
        [void]$candidates.Add((New-Candidate 'repo' $Text $false))
        $fileText = $FileDir
        if ([string]::IsNullOrEmpty($fileText)) { $fileText = './' }
        $projectText = $ProjectDir
        if ([string]::IsNullOrEmpty($projectText)) { $projectText = '.' }
    }
    else {
        $candidates = Expand-Candidates $Text $FileDir $ProjectDir 0
        if ($null -eq $candidates) { return $null }
    }
    $specs = New-Object 'System.Collections.Generic.List[object]'
    foreach ($candidate in $candidates) {
        $parts = ([string]$candidate.Text).Split(';')
        for ($i = 0; $i -lt $parts.Length; $i++) {
            $part = $parts[$i].Trim()
            if ($part.Length -eq 0) { continue }
            $kind = 'repo'
            $anchoredPart = $false
            if ($i -eq 0) {
                $kind = [string]$candidate.Kind
                $anchoredPart = [bool]$candidate.Anchored
            }
            if ($DirectoryPropertiesOnly) {
                $directory = [regex]::Match($part, '^(?i)\$\(\s*MSBuild(ThisFile|Project)Directory\s*\)')
                if ($directory.Success) {
                    $root = $fileText
                    if ($directory.Groups[1].Value -ieq 'Project') { $root = $projectText }
                    $part = $root + $part.Substring($directory.Length)
                    $anchoredPart = $true
                }
                if (($part.IndexOf('$(') -ge 0) -or ($part.IndexOf('@(') -ge 0) -or ($part.IndexOf('%(') -ge 0)) {
                    $kind = 'unresolved'
                }
            }
            $decoded = ConvertFrom-PercentEncoding $part
            if ($null -eq $decoded) { $kind = 'nul'; $decoded = $part }
            [void]$specs.Add(@{ Kind = $kind; Text = $decoded; Anchored = $anchoredPart; Source = $part })
        }
    }
    return ,$specs
}

function Measure-WildcardCharacters([string]$Text) {
    $n = 0
    foreach ($c in $Text.ToCharArray()) {
        if (($c -eq '*') -or ($c -eq '?')) { $n = $n + 1 }
    }
    return $n
}

# CARD-1409 repair 4 and 6: the tail of a path rooted at an output or package root (an include's text after
# the root, or a file-reading function's argument after it) is a build product only while it stays under
# the root: a drive letter or a '..' segment is unclassified, and a divergent segment (D-28) refuses naming
# it. $Tail is decoded text. Returns $null, or a hashtable with Problem 'unclassified' or 'segment' (with Canonical).
function Get-OutputTailProblem([string]$Tail) {
    $text = $Tail.Replace('\', '/')
    if ($text -match '^[A-Za-z]:') { return @{ Problem = 'unclassified' } }
    foreach ($part in $text.Split('/')) {
        if ($part -eq '..') { return @{ Problem = 'unclassified' } }
    }
    $canonical = ConvertTo-CanonicalPath $text
    if (Test-CanonicalRefused $canonical) { return @{ Problem = 'segment'; Canonical = $canonical } }
    return $null
}

# A property a tracked definition file may set or fill: allow-listed (D-13), defined in the census, one of the
# two directory properties, an output or package root, or a task output. Any other name is the SDK's own.
function Test-PropertyIsTrackable([string]$Name) {
    if (($Name -ieq 'MSBuildThisFileDirectory') -or ($Name -ieq 'MSBuildProjectDirectory')) { return $true }
    if ($AllowedProperties.Contains($Name)) { return $true }
    if ($script:PropertyDefinitions.ContainsKey($Name)) { return $true }
    if (Test-AllowedName $OutputRootProperties $Name $true) { return $true }
    if ($script:PackagePathProperties.Contains($Name)) { return $true }
    if ($script:TaskPropertyOutputs.ContainsKey($Name)) { return $true }
    return $false
}

# CARD-1409 repair 6 (D-33): in the tail of a file-reading function argument, a $(Name) no tracked definition
# file may set, such as the SDK's $(CleanFile) in the real Directory.Build.targets, is replaced by one opaque
# segment so the pipeline expands what the tracked files do define and refuses what it cannot resolve.
function Convert-SdkOwnedReferences([string]$Text) {
    $sb = New-Object System.Text.StringBuilder
    $position = 0
    foreach ($m in [regex]::Matches($Text, '\$\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)')) {
        [void]$sb.Append($Text.Substring($position, $m.Index - $position))
        if (Test-PropertyIsTrackable $m.Groups[1].Value) { [void]$sb.Append($m.Value) }
        else { [void]$sb.Append('sdk-owned') }
        $position = $m.Index + $m.Length
    }
    [void]$sb.Append($Text.Substring($position))
    return $sb.ToString()
}

# The tail of a file-reading function argument after its output root, through the pipeline: a reference the
# audit cannot resolve, a NUL escape, a second root, a drive letter, a '..' segment or a divergent segment
# is a problem. Each ';'-piece is held to the rule. Returns $null or the detail text for the message.
function Get-ExpandedTailProblem([string]$Tail, [string]$FileDir, [string]$ProjectDir) {
    if ($Tail.Length -eq 0) { return $null }
    $specs = Resolve-ItemPathText $Tail $FileDir $ProjectDir $false
    if ($null -eq $specs) { return ('cannot resolve ' + $script:UnresolvedReference) }
    foreach ($spec in $specs) {
        if ([string]$spec.Kind -eq 'nul') { return 'decodes to a NUL character' }
        if (([string]$spec.Kind -ne 'repo') -or [bool]$spec.Anchored) { return 'has a second root' }
        $problem = Get-OutputTailProblem ([string]$spec.Text)
        if ($null -eq $problem) { continue }
        if ([string]$problem.Problem -eq 'segment') { return (Format-SegmentDetail $problem.Canonical) }
        return 'climbs out of the output root'
    }
    return $null
}

# Resolves one repo candidate, evaluated from $ProjectDirectory, to a canonical result (D-28): Status ok
# with the repository-relative pattern, outside, escape, or refused with the segment. A drive-letter or
# rooted path is converted to a repository path first, so the drive colon is the only colon that may pass.
# An anchored candidate already starts at a known directory.
function Resolve-CandidatePattern([string]$Root, $Candidate, [string]$ProjectDirectory, [bool]$Fold) {
    $text = ([string]$Candidate.Text).Replace('\', '/')
    if ($text -match '^[A-Za-z]:/' -or $text.StartsWith('/')) {
        $relative = ConvertTo-RepoRelative $text $Root
        if ($null -eq $relative) { return @{ Status = 'outside' } }
        $text = $relative
    }
    elseif ([bool]$Candidate.Anchored) {
        if ($text.StartsWith('./')) { $text = $text.Substring(2) }
    }
    elseif (-not [string]::IsNullOrEmpty($ProjectDirectory)) {
        $text = $ProjectDirectory + '/' + $text
    }
    return ConvertTo-CanonicalPath $text $Fold
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

# CARD-1409: resolves an Import Project attribute through the pipeline (D-7, D-33: the two directory
# properties at the start, the ';' split after that expansion, one decode) to the tracked, parsed project
# paths it names, or refuses naming the whole attribute text and the resolved part.
function Resolve-ImportTargets([string]$Root, [string]$FromFile, [string]$ProjectDirectory, [string]$ProjectAttr, $Known) {
    $targets = New-Object 'System.Collections.Generic.List[string]'
    $fromDir = Get-RepoDirectory $FromFile
    $specs = Resolve-ItemPathText $ProjectAttr (Get-FileDirectoryPrefix $FromFile) $ProjectDirectory $true
    if ($specs.Count -eq 0) {
        Exit-Audit 1 (Format-MissingImportRefusal $FromFile $ProjectAttr '(empty)')
    }
    foreach ($spec in $specs) {
        $kind = [string]$spec.Kind
        if (($kind -eq 'unresolved') -or ($kind -eq 'nul')) {
            Exit-Audit 1 (Format-UnresolvedImportRefusal $FromFile $ProjectAttr)
        }
        if (Test-PatternHasWildcard ([string]$spec.Text)) {
            Exit-Audit 1 (Format-WildcardImportRefusal $FromFile $ProjectAttr)
        }
        $canonical = Resolve-CandidatePattern $Root $spec $fromDir $false
        if (Test-CanonicalRefused $canonical) {
            Exit-Audit 1 (Format-PathSegmentRefusal $FromFile ('Import Project=' + $ProjectAttr) $canonical)
        }
        if ([string]$canonical.Status -ne 'ok') {
            Exit-Audit 1 (Format-MissingImportRefusal $FromFile $ProjectAttr 'outside the repository')
        }
        $norm = [string]$canonical.Path
        if (-not $Known.ContainsKey($norm)) {
            Exit-Audit 1 (Format-MissingImportRefusal $FromFile $ProjectAttr $norm)
        }
        [void]$targets.Add($norm)
    }
    return ,$targets
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
        # CARD-1409: MSBuild splits Project on ';' into several imports, after expansion (D-7, D-33); the pipeline
        # inside Resolve-ImportTargets does that split, and every non-empty part is audited as its own import.
        foreach ($target in (Resolve-ImportTargets $Root $FromFile $ProjectDirectory ([string]$project) $Known)) {
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

# CARD-1409 repair 9 (Review 4ecdb753 R3, D-42): MSBuild escapes these characters in $(MSBuildProjectDirectory)
# and leaves them as written in $(MSBuildThisFileDirectory); the audit does not re-implement that escaping.
$MSBuildEscapedCharacters = @('%', '$', '@', ';', "'", '(', ')', '*', '?')

function Get-EscapedDefinitionCharacter([string]$Path) {
    foreach ($c in $MSBuildEscapedCharacters) {
        if ($Path.IndexOf($c, [System.StringComparison]::Ordinal) -ge 0) { return $c }
    }
    return $null
}

function Format-EscapedDefinitionPathRefusal([string]$Path, [string]$Character) {
    return 'definition file path holds a character MSBuild escapes in $(MSBuildProjectDirectory), so the audit cannot read a directory spliced from it; -Update cannot approve it: ' + $Path + ' char=' + $Character
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

function Format-PinnedPropertyRefusal([string]$Path, [string]$Name) {
    return 'definition file sets a pinned property to a value the audit does not allow; -Update cannot approve it: ' + $Path + ' ' + $Name
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

# CARD-1409 repair 5 (D-28): $Detail is the construct the path came from ('EmbeddedResource Include=...',
# 'Import Project=...', 'Content Include=... -> <match>', or '' for a tracked definition path).
function Format-PathSegmentRefusal([string]$Path, [string]$Detail, $Canonical) {
    $text = 'definition file path has a segment the Windows land host would normalise; -Update cannot approve it: ' + $Path
    if (-not [string]::IsNullOrEmpty($Detail)) { $text = $text + ' ' + $Detail }
    return $text + ' ' + (Format-SegmentDetail $Canonical)
}

function Format-ItemOperationRefusal([string]$Path, [string]$Item, [string]$Label, [string]$Operation) {
    return 'definition file item uses an operation the audit does not evaluate; -Update cannot approve it: ' + $Path + ' ' + $Item + ' ' + $Label + ' ' + $Operation
}

function Format-SymlinkRefusal([string]$Path, [string]$Item, [string]$Include, [string]$Link) {
    return 'definition file item path reaches a tracked symbolic link the audit cannot follow; -Update cannot approve it: ' + $Path + ' ' + $Item + ' Include=' + $Include + ' -> ' + $Link
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
            if ($PinnedPropertyValues.ContainsKey($name) -and -not (Test-AllowedName $PinnedPropertyValues[$name] ([string]$child.InnerXml) $false)) {
                Exit-Audit 1 (Format-PinnedPropertyRefusal $Path $name)
            }
        }
    }
    foreach ($output in $Doc.GetElementsByTagName('Output')) {
        $propertyName = ([string]$output.GetAttribute('PropertyName')).Trim()
        if ($propertyName.Length -gt 0 -and -not $AllowedProperties.Contains($propertyName)) {
            Exit-Audit 1 (Format-PropertyRefusal $Path $propertyName)
        }
        if ($PinnedPropertyValues.ContainsKey($propertyName)) { Exit-Audit 1 (Format-PinnedPropertyRefusal $Path $propertyName) }
        $itemName = ([string]$output.GetAttribute('ItemName')).Trim()
        if ($itemName.Length -gt 0 -and -not $ItemRules.ContainsKey($itemName)) {
            Exit-Audit 1 (Format-ItemTypeRefusal $Path $itemName)
        }
    }
}

# CARD-1409 (Review 31c77116 R3): every ItemGroup child, wherever the group sits, must be an allow-listed item type.
function Test-ReviewedSdkRemoval($Node, [string]$Path, [string]$Name) {
    if ($Path -cne 'tests/Shared/FakeProviderOutput.targets') { return $false }
    $target = $Node.ParentNode.ParentNode
    if ($target.LocalName -cne 'Target' -or $target.GetAttribute('Name') -cne 'ExcludeRootFakeProviderAppHosts' -or
        $target.ParentNode -ne $Node.OwnerDocument.DocumentElement) { return $false }
    $condition = "'%($Name.TargetPath)' == 'fakeclaude' or '%($Name.TargetPath)' == 'fakegrok'"
    return $Node.Attributes.Count -eq 2 -and -not $Node.HasChildNodes -and
        $Node.GetAttribute('Remove') -ceq "@($Name)" -and $Node.GetAttribute('Condition') -ceq $condition
}

function Assert-AllowedItemTypes($Doc, [string]$Path) {
    foreach ($group in $Doc.GetElementsByTagName('ItemGroup')) {
        foreach ($child in $group.ChildNodes) {
            if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            $name = [string]$child.LocalName
            if (Test-AllowedName $SdkRemovalItems $name $false) {
                if (-not (Test-ReviewedSdkRemoval $child $Path $name)) {
                    Exit-Audit 1 ('unreviewed SDK item removal; -Update cannot approve it: ' + $Path + ' ' + $name)
                }
                continue
            }
            if (-not $ItemRules.ContainsKey($name)) { Exit-Audit 1 (Format-ItemTypeRefusal $Path $name) }
        }
    }
}

# A package-style name is a bare identifier: after the pipeline's one decode (D-33) it carries no path
# separator, reference text, wildcard, parent segment or drive.
function Test-ItemNameIsPathLike([string]$Decoded) {
    foreach ($needle in @('/', '\', '$(', '*', '?', '..', ':')) {
        if ($Decoded.IndexOf($needle) -ge 0) { return $true }
    }
    return $false
}

# CARD-1409 repair 4 (Review 937bb1c9 F3) and repair 5 (plan D-29, O-table): an item element must carry
# Include, Update or Remove; a named operation the audit does not evaluate (KeepMetadata, RemoveMetadata,
# KeepDuplicates, MatchOnMetadata, MatchOnMetadataOptions) refuses by name; every other attribute that is
# not an item-element attribute, and every child element, is metadata: it must be allow-listed for the
# item type, and a Remove element may carry none.
function Assert-ItemOperationsAllowed($Node, [string]$Path, [string]$Item, [string]$Label) {
    $hasOperation = $false
    foreach ($name in @('Include', 'Update', 'Remove')) {
        if (-not [string]::IsNullOrWhiteSpace([string]$Node.GetAttribute($name))) { $hasOperation = $true }
    }
    if (-not $hasOperation) { Exit-Audit 1 (Format-ItemOperationRefusal $Path $Item $Label '(none)') }
    $isRemove = [string]::IsNullOrWhiteSpace([string]$Node.GetAttribute('Include')) -and [string]::IsNullOrWhiteSpace([string]$Node.GetAttribute('Update'))
    $allowed = @()
    if ($ItemMetadata.ContainsKey($Item) -and -not $isRemove) { $allowed = $ItemMetadata[$Item] }
    foreach ($attribute in $Node.Attributes) {
        $attributeName = [string]$attribute.Name
        if (Test-AllowedName $ItemElementAttributes $attributeName $false) { continue }
        if (Test-AllowedName $RefusedItemOperations $attributeName $true) { Exit-Audit 1 (Format-ItemOperationRefusal $Path $Item $Label $attributeName) }
        if (-not $isRemove -and $PinnedItemMetadata.ContainsKey($Item) -and $PinnedItemMetadata[$Item].ContainsKey($attributeName) -and
            [string]::Equals([string]$PinnedItemMetadata[$Item][$attributeName], [string]$attribute.Value, [System.StringComparison]::Ordinal)) { continue }
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
    $detail = $Item + ' Include=' + $Segment
    $kind = [string]$Candidate.Kind
    if ($kind -eq 'output' -or $kind -eq 'package') {
        # CARD-1409 repair 5 (D-28) and 6 (D-33): the decoded tail is compared like every other path.
        $problem = Get-OutputTailProblem ([string]$Candidate.Text)
        if ($null -ne $problem) {
            if ([string]$problem.Problem -eq 'segment') { Exit-Audit 1 (Format-PathSegmentRefusal $Path $detail $problem.Canonical) }
            Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment)
        }
        return
    }
    $canonical = Resolve-CandidatePattern $Root $Candidate $Directory
    if (Test-CanonicalRefused $canonical) { Exit-Audit 1 (Format-PathSegmentRefusal $Path $detail $canonical) }
    if ([string]$canonical.Status -eq 'escape') { Exit-Audit 1 (Format-ApprovalRefusal $Path $Item $Segment) }
    if ([string]$canonical.Status -eq 'outside') { Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment) }
    $pattern = [string]$canonical.Path
    # CARD-1409 repair 5 (D-29, O-3): Exclude only subtracts; each part still goes through the normaliser.
    $excludePatterns = New-Object 'System.Collections.Generic.List[string]'
    foreach ($exclude in $ExcludeCandidates) {
        if ([string]$exclude.Kind -ne 'repo') { continue }
        $excludeCanonical = Resolve-CandidatePattern $Root $exclude $Directory
        if (Test-CanonicalRefused $excludeCanonical) { Exit-Audit 1 (Format-PathSegmentRefusal $Path ($Item + ' Exclude=' + [string]$exclude.Source) $excludeCanonical) }
        if ([string]$excludeCanonical.Status -ne 'ok') { continue }
        [void]$excludePatterns.Add([string]$excludeCanonical.Path)
    }
    # CARD-1409 repair 5 (D-30): a tracked symbolic link the pattern reaches, as a match or as a directory
    # it passes through, refuses whatever the link points at today.
    $link = Find-SymlinkReached $pattern
    if ($null -ne $link) { Exit-Audit 1 (Format-SymlinkRefusal $Path $Item $Segment ([string]$link)) }
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
    $hits = Get-TrackedMatches $pattern $excludePatterns
    # CARD-1409 repair 5 (D-28, N-12): a tracked match is compared like every other path.
    foreach ($hit in $hits) {
        $hitCanonical = ConvertTo-CanonicalPath ([string]$hit)
        if (Test-CanonicalRefused $hitCanonical) { Exit-Audit 1 (Format-PathSegmentRefusal $Path ($detail + ' -> ' + [string]$hit) $hitCanonical) }
    }
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

# CARD-1409 (Review 31c77116): one include or update text under its item type's rule. The text runs through
# the pipeline (D-33: expanded, split on ';', decoded once) and every spec is resolved from every directory
# the item is evaluated from. Every refusal names the file, the item type and the attribute text.
function Assert-ItemIncludeAllowed([string]$Root, [string]$Path, [string]$Item, [string]$Rule, [string]$Segment, $Directories, $Known, [string]$Exclude) {
    if (($Segment.IndexOf('@(') -ge 0) -or ($Segment.IndexOf('%(') -ge 0)) {
        Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment)
    }
    if ($Rule -eq 'text') { return }
    $fileDir = Get-FileDirectoryPrefix $Path
    if ($Rule -eq 'name') {
        foreach ($name in (Resolve-ItemPathText $Segment $fileDir '' $true)) {
            if (([string]$name.Kind -ne 'repo') -or [bool]$name.Anchored -or (Test-ItemNameIsPathLike ([string]$name.Text))) {
                Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment)
            }
        }
        return
    }
    foreach ($directory in $Directories) {
        $candidates = Resolve-ItemPathText $Segment $fileDir ([string]$directory) $false
        if ($null -eq $candidates) { Exit-Audit 1 (Format-UnresolvedPropertyRefusal $Path $Item $Segment $script:UnresolvedReference) }
        # CARD-1409 repair 5 (D-29, O-3): an Exclude only subtracts, so a text the audit cannot resolve is dropped
        # whole, and so is a part whose decoded text gained a wildcard character (D-33: MSBuild reads an escaped
        # '*' or '?' literally, so the decoded glob would subtract more than the build does).
        $excludeCandidates = New-Object 'System.Collections.Generic.List[object]'
        $excludeText = $Exclude.Trim()
        if (($excludeText.Length -gt 0) -and ($excludeText.IndexOf('@(') -lt 0) -and ($excludeText.IndexOf('%(') -lt 0)) {
            $expanded = Resolve-ItemPathText $excludeText $fileDir ([string]$directory) $false
            if ($null -ne $expanded) {
                foreach ($candidate in $expanded) {
                    if ([string]$candidate.Kind -eq 'nul') { continue }
                    if ((Measure-WildcardCharacters ([string]$candidate.Text)) -ne (Measure-WildcardCharacters ([string]$candidate.Source))) { continue }
                    [void]$excludeCandidates.Add($candidate)
                }
            }
        }
        foreach ($candidate in $candidates) {
            if ([string]$candidate.Kind -eq 'nul') { Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment) }
            if ($Rule -eq 'project') {
                if ([string]$candidate.Kind -ne 'repo') { Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment) }
                $canonical = Resolve-CandidatePattern $Root $candidate ([string]$directory)
                if (Test-CanonicalRefused $canonical) { Exit-Audit 1 (Format-PathSegmentRefusal $Path ($Item + ' Include=' + $Segment) $canonical) }
                if ([string]$canonical.Status -ne 'ok') { Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment) }
                $resolved = [string]$canonical.Path
                if (Test-PatternHasWildcard $resolved) { Exit-Audit 1 (Format-UnclassifiedIncludeRefusal $Path $Item $Segment) }
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
# system through a property function must read under an output root and may not climb out of it (D-33),
# and a value part that is a literal path (after the two directory properties) must not be a docs/ path,
# the root README.md, or a wildcard that can reach them. Other references in a value are left alone: a
# value is not an input by itself, and an include that uses it is expanded and classified where it is used.
function Assert-PropertyValuesAllowed([string]$Root, $Doc, [string]$Path, $Directories) {
    $fileDir = Get-FileDirectoryPrefix $Path
    $projectDir = Get-RepoDirectory $Path
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
                $rootMatch = [regex]::Match($argument, $outputRooted)
                if (-not $rootMatch.Success) {
                    Exit-Audit 1 (Format-PropertyValueRefusal $Path $name ($function + '(' + $argument + ')'))
                }
                # CARD-1409 repair 6 (D-33): the tail after the root is path text like an output-rooted include's
                # and goes through the pipeline; a property no tracked file may set is the SDK's own (opaque).
                $problem = Get-ExpandedTailProblem (Convert-SdkOwnedReferences $argument.Substring($rootMatch.Length)) $fileDir $projectDir
                if ($null -ne $problem) {
                    Exit-Audit 1 (Format-PropertyValueRefusal $Path $name ($function + '(' + $argument + ') ' + $problem))
                }
            }
            # The pipeline splits the value on ';' and decodes each part once (D-33). CARD-1409 repair 7 (Review
            # 7db88932 R1, D-36): the D-25 skip is per part, as it was before repair 6: a part whose escaped text
            # carries an item, metadata or property-function reference is left alone on its own, and so is a part
            # the pipeline returns unresolved (a reference other than a directory property at the start of its own
            # part, CARD-1409 repair 8, D-39); every other part of the same value is still classified.
            foreach ($spec in (Resolve-ItemPathText $value $fileDir $projectDir $true)) {
                $source = [string]$spec.Source
                if (($source.IndexOf('@(') -ge 0) -or ($source.IndexOf('%(') -ge 0) -or ($source.IndexOf('$([') -ge 0)) { continue }
                if ([string]$spec.Kind -ne 'repo') { continue }
                # A wildcard in a value is an exclude list or a glob some include expands where it is used (an
                # escaped '*' is a literal character, so the escaped part decides), and a bare word (true, Exe,
                # net10.0) is not a file reference; only a decoded part with a path separator or a document
                # extension is resolved, from the file's own directory. An include that uses the value is
                # expanded from every importing project directory where it is used.
                if (Test-PatternHasWildcard ([string]$spec.Source)) { continue }
                $decodedText = ([string]$spec.Text).Replace('\', '/')
                if (($decodedText.IndexOf('/') -lt 0) -and -not (Test-DocumentPath $decodedText)) { continue }
                # CARD-1409 repair 5 (D-28): a value is free text, so it is read as the land host would read it
                # (folded) and refuses only when that reading is docs/ or the root README.
                $canonical = Resolve-CandidatePattern $Root $spec $projectDir $true
                if (Test-CanonicalRefused $canonical) { Exit-Audit 1 (Format-PropertyValueRefusal $Path $name (Format-SegmentDetail $canonical)) }
                if ([string]$canonical.Status -ne 'ok') { continue }
                $pattern = [string]$canonical.Path
                $docs = (Test-PathUnderDocs $pattern) -or ($pattern -ieq 'README.md')
                if ($docs -and -not (Test-AllowedName $AllowedDocsInputs $pattern $true)) {
                    $valueDetail = '-> ' + $pattern
                    if ($null -ne $canonical.Divergent) { $valueDetail = $valueDetail + ' ' + (Format-SegmentDetail $canonical.Divergent) }
                    Exit-Audit 1 (Format-PropertyValueRefusal $Path $name $valueDetail)
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
                # Already checked as an exact metadata-free Remove; not an Include/Update allowance.
                if (Test-AllowedName $SdkRemovalItems $local $false) { continue }
                $rule = [string]$ItemRules[$local]
                Assert-ItemOperationsAllowed $node ([string]$path) $local (Get-IncludeLabel $node)
                # Include and Update are classified; Remove only subtracts and is left alone. The attribute text
                # goes in whole: the pipeline splits it on ';' after expansion (D-33).
                foreach ($attributeName in @('Include', 'Update')) {
                    $include = ([string]$node.GetAttribute($attributeName)).Trim()
                    if ([string]::IsNullOrWhiteSpace($include)) { continue }
                    $exclude = ''
                    if ($attributeName -eq 'Include') { $exclude = [string]$node.GetAttribute('Exclude') }
                    Assert-ItemIncludeAllowed $Root ([string]$path) $local $rule $include $directories $known $exclude
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
    # CARD-1409 repair 5 (D-30): a tracked symbolic link is recorded so a path item that reaches it refuses.
    if ($blob -and $mode -eq '120000') { [void]$script:SymlinkPaths.Add($path) }
    if (-not (Test-DefinitionPath $path)) { continue }
    # CARD-1409 repair 5 (D-28, N-15): a definition path is compared like every other path, so an 'obj.'
    # segment the land host would read as obj/ refuses by name instead of slipping past the obj/ rule.
    $definitionCanonical = ConvertTo-CanonicalPath $path
    if (Test-CanonicalRefused $definitionCanonical) { Exit-Audit 1 (Format-PathSegmentRefusal $path '' $definitionCanonical) }
    if (Test-PathUnderObjDirectory $path) { Exit-Audit 1 (Format-ObjDefinitionRefusal $path) }
    # CARD-1409 repair 9 (D-42): every directory the audit splices, and every import or reference target, is a
    # tracked definition path, so one holding a character MSBuild escapes refuses before any file is read.
    $escapedCharacter = Get-EscapedDefinitionCharacter $path
    if ($null -ne $escapedCharacter) { Exit-Audit 1 (Format-EscapedDefinitionPathRefusal $path $escapedCharacter) }
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
