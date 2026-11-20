# Shared process implementation; load its source through the regular-file boundary.
#
# PowerShell 7 compiles it once per source version into the per-user cache of Import-CheckpointCompiledSource
# (checkpoint-file-reader.ps1), keyed by the SHA-256 of the source read through that boundary plus the
# PowerShell/.NET versions, and each later process only loads the cached assembly (the script tests start many
# PowerShell processes, and the compile costs most of a second in each). A cache that cannot be written or
# loaded falls back to compiling in memory. Keep this file ASCII-only.
. (Join-Path $PSScriptRoot 'checkpoint-file-reader.ps1')
if (-not ('Antiphon.Checkpoints.CheckpointProcessRunner' -as [type])) {
    Import-CheckpointCompiledSource -Source (Read-CheckpointText (Join-Path $PSScriptRoot 'CheckpointProcessRunner.cs')) `
        -TypeName 'Antiphon.Checkpoints.CheckpointProcessRunner' -Name 'CheckpointProcessRunner'
}

# One filter/textconv driver lookup for a run of git reads (CheckpointProcessRunner.DriverSettingsScope), or
# $null when the loaded runner predates it (a long-lived session that loaded an older build): the reads then
# ask before each one, as before. Dispose the result in a finally: if ($scope) { $scope.Dispose() }.
function New-CheckpointDriverScope {
    $runner = [Antiphon.Checkpoints.CheckpointProcessRunner]
    if ($null -eq $runner.GetMethod('DriverSettingsScope', [Type]::EmptyTypes)) { return $null }
    return $runner::DriverSettingsScope()
}
