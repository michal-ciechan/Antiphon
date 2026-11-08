# CARD-1748: one journal reader for the checkpoint tool and run-checkpoint.ps1.
. (Join-Path $PSScriptRoot 'checkpoint-file-reader.ps1')
if (-not ('Antiphon.Checkpoints.PhaseJournalDiagnostic' -as [type])) {
    Add-Type -TypeDefinition (Read-CheckpointText (Join-Path $PSScriptRoot 'PhaseJournalDiagnostic.cs'))
}
