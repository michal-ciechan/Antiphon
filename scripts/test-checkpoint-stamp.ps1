# Handwritten native source-stamping regression; the caller owns a four-second child watchdog.
param([string]$Root, [string]$Repository, [string]$ToolDll, [string]$Entry, [string]$Shape)
$ErrorActionPreference = 'Stop'
# Keep the scoped test custody marker outside the scratch Git worktree.
$Root = Join-Path $Root 'stamp-fixture'
[void][Reflection.Assembly]::LoadFrom($ToolDll)
[void][IO.Directory]::CreateDirectory($Root)
$path = Join-Path $Root 'user.txt'
[IO.File]::WriteAllText($path, 'original user bytes')
& git -C $Root init -q -b fixture
& git -C $Root add user.txt
& git -C $Root -c user.name=Fixture -c user.email=fixture@example.invalid commit -q -m fixture
if ($LASTEXITCODE) { throw 'fixture git failed' }
$index = & git -C $Root ls-files -s
$headSha = & git -C $Root rev-parse HEAD
$entries = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$entries['user.txt'] = $index.Split("`t")[0]
$contents = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$contents['user.txt'] = 'file|19|' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('original user bytes'))).ToLowerInvariant()
$baseline = [Antiphon.Checkpoints.BuildConfinement+Baseline]::new($headSha, $Root, $entries, [Collections.Generic.Dictionary[string,string]]::new(), $contents)
if ($Entry -eq 'script') {
    $reader = Join-Path $Repository 'scripts/lib/checkpoint-file-reader.ps1'
    if (Test-Path $reader) { . $reader }
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $Repository 'scripts/run-checkpoint.ps1'),[ref]$tokens,[ref]$errors)
    foreach ($name in @('Get-CheckpointSha256','Get-CheckpointStampLink','Get-CheckpointContentStamp')) {
        $definition=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$false))
        . ([scriptblock]::Create($definition[0].Extent.Text))
    }
    $control = Get-CheckpointContentStamp $path
    if ($control -cne $contents['user.txt']) { throw "healthy script control: $control" }
} else {
    $control = [Antiphon.Checkpoints.BuildConfinement]::Settle($baseline)
    if ($control.Kind) { throw "healthy tool control: $($control.Detail)" }
}
if ($Shape -ne 'regular') { [IO.File]::Delete($path) }
switch ($Shape) {
    fifo { & mkfifo $path; if ($LASTEXITCODE) { throw 'mkfifo failed' } }
    symlink { [IO.File]::WriteAllText("$Root/target",'target bytes'); [void][IO.File]::CreateSymbolicLink($path,"$Root/target") }
    dangling { [void][IO.File]::CreateSymbolicLink($path,"$Root/missing") }
    directory { [void][IO.Directory]::CreateDirectory($path) }
}
"$Entry/$Shape STAMP starting"
if ($Entry -eq 'script') {
    $result = Get-CheckpointContentStamp $path
    if ($Shape -eq 'regular') { if ($result -cne $contents['user.txt']) { throw "regular stamp: $result" } }
    elseif ($Shape -eq 'fifo') { if (-not $result.StartsWith('unreadable|')) { throw "FIFO must be unprovable with its reason: $result" } }
    elseif ($result -eq $contents['user.txt']) { throw "special shape certified: $result" }
} else {
    $result = [Antiphon.Checkpoints.BuildConfinement]::Settle($baseline)
    if ($Shape -eq 'regular') { if ($result.Kind) { throw $result.Detail } }
    elseif ($result.Kind -ne 'tampered' -or !$result.Detail.Contains('TAMPERED user.txt')) { throw "missing named/path refusal: $($result.Detail)" }
}
if ((& git -C $Root ls-files -s) -cne $index) { throw 'index changed' }
if ($Shape -eq 'symlink' -and [IO.File]::ReadAllText("$Root/target") -cne 'target bytes') { throw 'link target changed' }
if ($Shape -in @('symlink','dangling') -and $null -eq [IO.FileInfo]::new($path).LinkTarget) { throw 'link removed' }
if ($Shape -eq 'directory' -and ![IO.Directory]::Exists($path)) { throw 'directory removed' }
if ($Shape -eq 'fifo') { & test -p $path; if ($LASTEXITCODE) { throw 'FIFO removed or replaced' } }
# Survival/index assertions above precede disposal of our explicitly created special-file fixtures.
if ($Shape -in @('fifo','symlink','dangling')) { [IO.File]::Delete($path) }
"$Entry/$Shape PASS"
