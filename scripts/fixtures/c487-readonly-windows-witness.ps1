# String-only Windows cleanup witness. No real filesystem or process is changed.
param([string]$SourceRef = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($SourceRef) {
    $source = (& git -C $repo show ($SourceRef + ':scripts/lib/c487-harness.ps1')) -join "\n"
    if ($LASTEXITCODE -ne 0) { throw 'source ref unavailable' }
} else { $source = [IO.File]::ReadAllText((Join-Path $repo 'scripts/lib/c487-harness.ps1')) }
. ([scriptblock]::Create($source))
$tokens = $null; $errors = $null
$tree = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'harness source did not parse' }
$complete = $tree.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Complete-C487Harness' }, $true)
if ($null -eq $complete) { throw 'completion function missing' }
# Keep the actual completion body; intercept only its process exit for this observer.
Invoke-Expression ($complete.Extent.Text.Replace('exit 0', 'return').Replace('exit 1', "throw 'unexpected harness failure'"))
$root = "C:\Users\fixture's home\AppData\Local\Temp\c487-owned"
$outside = 'D:\outside-results\keep.txt'
$nodes = @{}; $children = @{}; $enumerated = [Collections.Generic.List[string]]::new()
function Add-Node($path, $attributes, [bool]$readOnly = $false) {
    $nodes[$path] = [pscustomobject]@{ FullName = $path; Attributes = $attributes; IsReadOnly = $readOnly }
    $parent = $path.Substring(0, $path.LastIndexOf('\'))
    $children[$parent] = @($children[$parent]) + @($path) | Where-Object { $null -ne $_ }
}
Add-Node $root ([IO.FileAttributes]::Directory)
foreach ($relative in @('checkout', 'checkout\.git', 'checkout\.git\objects', 'checkout\.git\objects\ab',
        'remote.git', 'remote.git\objects', 'remote.git\objects\ab', 'repository with spaces',
        'repository with spaces\.git', 'repository with spaces\.git\objects', 'repository with spaces\.git\objects\ab')) {
    Add-Node ($root + '\' + $relative) ([IO.FileAttributes]::Directory)
}
$owned = @('checkout\.git\objects\ab\cdef', 'remote.git\objects\ab\cdef', 'repository with spaces\.git\objects\ab\cdef')
foreach ($relative in $owned) { Add-Node ($root + '\' + $relative) ([IO.FileAttributes]::ReadOnly) $true }
Add-Node $outside ([IO.FileAttributes]::ReadOnly) $true
$directoryLink = $root + '\linked-directory'; $fileLink = $root + '\linked-file'
Add-Node $directoryLink ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Directory)
Add-Node $fileLink ([IO.FileAttributes]::ReparsePoint)
$rootLink = 'C:\linked-results'
Add-Node $rootLink ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Directory)
$before = $nodes.Count
function Get-Item {
    [CmdletBinding()] param([string]$LiteralPath, [switch]$Force)
    if (-not $nodes.ContainsKey($LiteralPath)) { throw 'unexpected item lookup' }
    return $nodes[$LiteralPath]
}
function Get-ChildItem {
    [CmdletBinding()] param([string]$LiteralPath, [switch]$Force)
    if ($LiteralPath -in @($outside, $directoryLink, $fileLink, $rootLink)) { throw 'external link was followed' }
    $enumerated.Add($LiteralPath)
    foreach ($name in $children[$LiteralPath]) { $nodes[$name] }
}
$script:C487Passed = 1; $script:C487Rows = 1; $script:C487Failed = 0
Complete-C487Harness -ResultsDirectory $root -ExpectedRows 1
# Model the Windows Directory.Delete precondition after the real completion body.
foreach ($relative in $owned) {
    if ($nodes[$root + '\' + $relative].IsReadOnly) { throw ('Windows Directory.Delete refused read-only Git object: ' + $relative) }
}
if (-not $nodes[$outside].IsReadOnly) { throw 'outside file attributes changed' }
if ($nodes.Count -ne $before) { throw 'completion deleted a file before the owner joined its processes' }
if ($enumerated.Count -ne 12 -or $enumerated[0] -cne $root) { throw 'owned Windows directory inventory changed' }
Complete-C487Harness -ResultsDirectory $rootLink -ExpectedRows 1
if (-not $nodes[$outside].IsReadOnly -or $enumerated.Contains($rootLink)) { throw 'linked root was traversed' }
if ($script:C487Passed -ne 1 -or $script:C487Rows -ne 1 -or $script:C487Failed -ne 0) { throw 'assertion inventory changed' }
Write-Host 'C487 Windows cleanup witness passed: three owned Git objects normalized; file/directory/root links untouched; assertion inventory preserved.'
