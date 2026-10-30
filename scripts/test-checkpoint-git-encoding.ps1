#requires -Version 7.0
# CARD-1608. ASCII-only. Loads the production checkpoint helpers and checks two contracts:
# a legacy console code page must not hide a non-ASCII content change, and invalid UTF-8
# from git must be refused. The legacy page is applied on the helper's ProcessStartInfo,
# which is how Windows decodes a redirected pipe when StandardOutputEncoding is unset.
param([string]$Root, [string]$Repository, [string]$Mode, [string]$GitExecutable)
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
    # CreateProcess appends .exe to an extensionless name; a PATH git.cmd is not
    # the program selected by UseShellExecute=false. Bind only the executable in
    # this imported helper, retaining production hardening, byte capture and decode.
    if (-not [IO.Path]::IsPathFullyQualified($GitExecutable) -or -not [IO.File]::Exists($GitExecutable)) {
        throw 'a staged native GitExecutable is required for the encoding witness'
    }
    $definition = (Get-Command Invoke-CheckpointGit).ScriptBlock.Ast.Extent.Text
    $needle = '$psi.FileName = ''git'''
    if (([regex]::Matches($definition, [regex]::Escape($needle))).Count -ne 1) { throw 'git executable seam changed' }
    $literal = "'" + $GitExecutable.Replace("'", "''") + "'"
    . ([scriptblock]::Create($definition.Replace($needle, '$psi.FileName = ' + $literal)))
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
