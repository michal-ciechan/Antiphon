# ASCII-only syntax witness; scripts requiring PS7 are parsed, never executed here.
param([switch]$AllowPwsh, [string]$SinglePath)
$ErrorActionPreference = 'Stop'
if (-not $AllowPwsh -and ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or $PSVersionTable.PSEdition -ne 'Desktop')) {
    throw 'requires Windows PowerShell 5.1 Desktop'
}
function Test-StampScript([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    foreach ($b in $bytes) { if ($b -gt 127) { throw "non-ASCII script: $Path" } }
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if ($null -ne $errors -and @($errors).Count -gt 0) { throw "parser errors: $Path" }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
    "PARSE path=$Path sha256=$hash ascii=true errors=0"
}
if (-not [string]::IsNullOrEmpty($SinglePath)) {
    Test-StampScript $SinglePath
    exit 0
}
$repo = Split-Path $PSScriptRoot -Parent
$paths = @('scripts/run-checkpoint.ps1', 'scripts/lib/checkpoint-file-reader.ps1', 'scripts/test-checkpoint-large-stamp.ps1', 'scripts/test-checkpoint-stamp-ps51-parse.ps1')
foreach ($path in $paths) { Test-StampScript (Join-Path $repo $path) }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('c1532-parse-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
try {
    $engine = (Get-Process -Id $PID).Path
    $bad = Join-Path $scratch 'malformed.ps1'
    [IO.File]::WriteAllText($bad, 'function Broken {')
    $nonAscii = Join-Path $scratch 'nonascii.ps1'
    [IO.File]::WriteAllBytes($nonAscii, [byte[]]@(35, 32, 195, 169))
    foreach ($copy in @($bad, $nonAscii)) {
        $childArgs = @('-NoProfile', '-NonInteractive', '-File', $PSCommandPath, '-SinglePath', $copy)
        if ($AllowPwsh) { $childArgs += '-AllowPwsh' }
        $savedPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $output = & $engine @childArgs 2>&1
            $childExit = $LASTEXITCODE
        } finally { $ErrorActionPreference = $savedPreference }
        if ($childExit -eq 0) { throw "invalid copied script accepted: $copy" }
        $expected = if ($copy -eq $bad) { 'parser errors' } else { 'non-ASCII script' }
        if (-not (($output | Out-String).Contains($expected))) { throw "negative control failed for wrong reason: $output" }
    }
} finally { [IO.Directory]::Delete($scratch, $true) }
"PARSER PASS version=$($PSVersionTable.PSVersion) edition=$($PSVersionTable.PSEdition) ascii=4 parsed=4 negative=2"
