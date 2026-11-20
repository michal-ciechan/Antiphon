#requires -Version 7.0
# Index-file answers to run-checkpoint.ps1's tracked-path guards (CheckpointIndexPaths.cs): no git process when the
# index is a plain version-2 index. Without the type (a compile or load failure, or a test that extracts a guard
# function alone) the guards ask git, as before. Keep this file ASCII-only.
. (Join-Path $PSScriptRoot 'checkpoint-file-reader.ps1')
if (-not ('Antiphon.Checkpoints.CheckpointIndexPaths' -as [type])) {
    try {
        Import-CheckpointCompiledSource -Source (Read-CheckpointText (Join-Path $PSScriptRoot 'CheckpointIndexPaths.cs')) `
            -TypeName 'Antiphon.Checkpoints.CheckpointIndexPaths' -Name 'CheckpointIndexPaths'
    } catch { }
}
