param(
    [string]$Case,
    [string]$ResultsDirectory,
    [string]$LinkPath,
    [string]$TargetPath
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $Case -cne 'Create') { throw 'Native Windows junction creation required.' }
foreach ($value in @($LinkPath, $TargetPath)) {
    if (-not [System.IO.Path]::IsPathFullyQualified($value) -or $value -match '[%!\x00-\x1f]' -or
        $value.Contains('"') -or $value.Contains('^') -or $value.Contains('&') -or
        $value.Contains('|') -or $value.Contains('<') -or $value.Contains('>')) {
        throw 'Unsupported junction fixture path.'
    }
}
# Only this owned foreground child inherits the job and both output pipes.
# Paths are whole quoted arguments; expansion/control characters were rejected above.
$command = 'mklink /J "' + $LinkPath + '" "' + $TargetPath + '"'
& (Join-Path ([System.Environment]::SystemDirectory) 'cmd.exe') /d /v:off /c $command
exit $LASTEXITCODE
