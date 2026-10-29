#requires -Version 7.0
# CARD-1608. ASCII-only. Loads the production checkpoint helpers and checks two contracts:
# a legacy console code page must not hide a non-ASCII content change, and invalid UTF-8
# from git must be refused. The legacy page is applied on the helper's ProcessStartInfo,
# which is how Windows decodes a redirected pipe when StandardOutputEncoding is unset.
param([string]$Root, [string]$Repository, [string]$Mode)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($Root) -or [string]::IsNullOrWhiteSpace($Repository) -or [string]::IsNullOrWhiteSpace($Mode)) {
    throw 'Root, Repository, and Mode are required'
}
$root = Join-Path $Root 'git-encoding-fixture'
[void][IO.Directory]::CreateDirectory($root)
. (Join-Path $Repository 'scripts/lib/checkpoint-file-reader.ps1')
. (Join-Path $Repository 'scripts/lib/checkpoint-process-runner.ps1')
if (-not [string]::IsNullOrEmpty([Antiphon.Checkpoints.Coverage.ConfinedFileReader].Assembly.Location)) { throw 'embedded reader required' }
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $Repository 'scripts/run-checkpoint.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'production parse failed' }
foreach ($definition in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
$script:Utf8 = [Text.UTF8Encoding]::new($false, $true)

function Get-LegacyCheckpointGitDefinition {
    # Returned text is dot-sourced by the caller. Dot-sourcing inside this function would
    # bind the patched helper only for this call, then restore the console-default reader.
    $definition = (Get-Command Invoke-CheckpointGit).ScriptBlock.Ast.Extent.Text
    $needle = '$psi.CreateNoWindow = $true'
    $found = ([regex]::Matches($definition, [regex]::Escape($needle))).Count
    if ($found -ne 1) { throw "legacy capture seam count=$found" }
    $insert = $needle + "`n    `$psi.StandardOutputEncoding = [Text.Encoding]::GetEncoding('iso-8859-1')`n    `$psi.StandardErrorEncoding = [Text.Encoding]::GetEncoding('iso-8859-1')"
    return $definition.Replace($needle, $insert)
}

function New-CheckpointEncodingFixture {
    param([string]$Path)
    [void][IO.Directory]::CreateDirectory($Path)
    & git -C $Path init -q -b fixture
    if ($LASTEXITCODE) { throw 'git init failed' }
    & git -C $Path config core.quotePath false
    & git -C $Path config user.name Fixture
    & git -C $Path config user.email fixture@example.invalid
    & git -C $Path config commit.gpgsign false
    [IO.File]::WriteAllText((Join-Path $Path 'seed.txt'), "seed`n")
    & git -C $Path add -- seed.txt
    if ($LASTEXITCODE) { throw 'git add failed' }
    & git -C $Path commit -qm fixture
    if ($LASTEXITCODE) { throw 'git commit failed' }
}

function Get-NonAsciiRelative {
    # cafe.txt with U+00E9 encoded as UTF-8, kept out of this ASCII source file.
    return $script:Utf8.GetString([byte[]](0x63, 0x61, 0x66, 0xC3, 0xA9, 0x2E, 0x74, 0x78, 0x74))
}

function Assert-TamperedPath {
    param($Settled, [string]$Relative, [string]$Label)
    $detail = [string]$Settled.Detail
    $marker = 'TAMPERED ' + $Relative
    if ($Settled.Kind -cne 'tampered' -or $detail.IndexOf($marker, [StringComparison]::Ordinal) -lt 0) {
        $hex = ''
        if (-not [string]::IsNullOrEmpty($detail)) { $hex = [Convert]::ToHexString($script:Utf8.GetBytes($detail)) }
        throw "$Label missed kind=$($Settled.Kind) detailHex=$hex"
    }
}

function Assert-ContentChangeIsTampered {
    param([string]$Relative, [string]$Label)
    $full = Join-Path $root $Relative
    $baseline = Get-CheckpointTrackedSnapshot -Projects @()
    if ($null -ne $baseline.Error) { throw "$Label baseline $($baseline.Error)" }
    [IO.File]::WriteAllText($full, "changed-content`n")
    $settled = Complete-CheckpointBaseline $baseline
    Assert-TamperedPath $settled $Relative $Label
}

if ($Mode -eq 'tamper') {
    New-CheckpointEncodingFixture $root
    . ([scriptblock]::Create((Get-LegacyCheckpointGitDefinition)))
    $listed = Invoke-CheckpointGit $root @('ls-files', '-z', '--', 'seed.txt')
    if ($listed.Reason -or [int]$listed.Exit -ne 0) { throw "ascii ls-files failed reason=$($listed.Reason) exit=$($listed.Exit)" }
    $expected = $script:Utf8.GetBytes("seed.txt$([char]0)")
    $actual = $script:Utf8.GetBytes([string]$listed.Stdout)
    if ([Convert]::ToHexString([byte[]]$actual) -cne [Convert]::ToHexString($expected)) { throw 'ascii path was not byte-identical' }
    Assert-ContentChangeIsTampered 'seed.txt' 'ascii'
    [IO.File]::WriteAllText((Join-Path $root 'seed.txt'), "seed`n")
    $clean = Complete-CheckpointBaseline (Get-CheckpointTrackedSnapshot -Projects @())
    if ($null -ne $clean.Kind) { throw "fixture not clean after ascii restore kind=$($clean.Kind)" }
    $relative = Get-NonAsciiRelative
    [IO.File]::WriteAllText((Join-Path $root $relative), "before`n")
    Assert-ContentChangeIsTampered $relative 'untracked-non-ascii'
    [IO.File]::WriteAllText((Join-Path $root $relative), "before`n")
    & git -C $root add -- $relative
    if ($LASTEXITCODE) { throw 'git add non-ascii failed' }
    & git -C $root commit -qm 'track non-ascii'
    if ($LASTEXITCODE) { throw 'git commit non-ascii failed' }
    Assert-ContentChangeIsTampered $relative 'tracked-non-ascii'
    Write-Host 'C1608 tamper PASS'
} elseif ($Mode -eq 'encoding') {
    [void][IO.Directory]::CreateDirectory($root)
    $shims = Join-Path $Root 'shims'
    [void][IO.Directory]::CreateDirectory($shims)
    # Production starts FileName 'git' without a shell. Windows needs git.exe;
    # a git.cmd shim would be bypassed in favour of the installed native executable.
    # File.SetUnixFileMode stays in-process so the witness does not need chmod.
    if ($IsWindows) {
        $shimPath = Join-Path $shims 'git.exe'
        $sourcePath = Join-Path $shims 'GitShim.cs'
        [IO.File]::WriteAllText($sourcePath, @'
public static class GitShim {
    public static int Main(string[] args) {
        foreach (string arg in args) if (arg == "config") return 0;
        System.Console.OpenStandardOutput().WriteByte(255);
        return 0;
    }
}
'@)
        # Windows PowerShell's compiler emits a directly runnable .NET Framework exe.
        $compilerPath = Join-Path $shims 'compile.ps1'
        [IO.File]::WriteAllText($compilerPath, @'
param([string]$Source, [string]$Output)
$ErrorActionPreference = 'Stop'
Add-Type -Path $Source -OutputAssembly $Output -OutputType ConsoleApplication
'@)
        $compiler = [Diagnostics.ProcessStartInfo]::new(
            (Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell/v1.0/powershell.exe'))
        foreach ($arg in @('-NoProfile','-NonInteractive','-File',$compilerPath,'-Source',$sourcePath,'-Output',$shimPath)) {
            $compiler.ArgumentList.Add($arg)
        }
        [void]$compiler.Environment.Remove('PSModulePath')
        $built = [Antiphon.Checkpoints.CheckpointProcessRunner]::Run($compiler, 30000, $false)
        if ($built.Exit -ne 0 -or -not [IO.File]::Exists($shimPath)) {
            throw "native git shim build failed: $($built.Stdout) $($built.Stderr)"
        }
    } else {
        $shim = @'
#!/bin/sh
set -eu
while [ "$1" = -c ]; do shift 2; done
if [ "$1" = config ]; then exit 0; fi
printf '\377'
'@
        $shimPath = Join-Path $shims 'git'
        [IO.File]::WriteAllText($shimPath, $shim.Replace("`r`n", "`n").Replace("`r", "`n"))
        $mode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute
        [IO.File]::SetUnixFileMode($shimPath, [IO.UnixFileMode]$mode)
        $set = [IO.File]::GetUnixFileMode($shimPath)
        if (($set -band [IO.UnixFileMode]::UserExecute) -eq 0) { throw 'git shim is not executable' }
    }
    $env:PATH = $shims + [IO.Path]::PathSeparator + $env:PATH
    $listed = Invoke-CheckpointGit $root @('status')
    $stdout = [string]$listed.Stdout
    if ($listed.Reason -cne 'git-encoding' -or -not [string]::IsNullOrEmpty($stdout)) {
        $hex = ''
        if (-not [string]::IsNullOrEmpty($stdout)) { $hex = [Convert]::ToHexString($script:Utf8.GetBytes($stdout)) }
        throw "invalid UTF-8 accepted reason=$($listed.Reason) exit=$($listed.Exit) stdoutHex=$hex"
    }
    $manifest = Get-CheckpointUntrackedManifest $root
    $expected = 'git-ls-files git output is not valid UTF-8 (a non-UTF-8 file name?)'
    if ([string]$manifest.Error -cne $expected) { throw "encoding refusal text: $($manifest.Error)" }
    Write-Host 'C1608 encoding PASS'
} else {
    throw "unknown mode $Mode"
}
