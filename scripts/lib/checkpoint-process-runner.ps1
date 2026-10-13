# Shared process implementation; load its source through the regular-file boundary.
. (Join-Path $PSScriptRoot 'checkpoint-file-reader.ps1')
if (-not ('Antiphon.Checkpoints.CheckpointProcessRunner' -as [type])) {
    Add-Type -TypeDefinition (Read-CheckpointText (Join-Path $PSScriptRoot 'CheckpointProcessRunner.cs'))
}
