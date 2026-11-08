# Shared process implementation; load its source through the regular-file boundary.
#
# PowerShell 7 compiles it once per source version into a cache under the user's temp directory, keyed by
# the SHA-256 of the source read through that boundary plus the PowerShell/.NET versions, and each later
# process only loads the cached assembly (the script tests start many PowerShell processes, and the
# compile costs most of a second in each). A cache that cannot be written or loaded falls back to
# compiling in memory, as Windows PowerShell 5.1 always does. Keep this file ASCII-only.
. (Join-Path $PSScriptRoot 'checkpoint-file-reader.ps1')
if (-not ('Antiphon.Checkpoints.CheckpointProcessRunner' -as [type])) {
    $checkpointRunnerSource = Read-CheckpointText (Join-Path $PSScriptRoot 'CheckpointProcessRunner.cs')
    $checkpointRunnerLoaded = $false
    if ($PSVersionTable.PSVersion.Major -ge 7) {
        try {
            $checkpointRunnerKey = [Text.Encoding]::UTF8.GetBytes($checkpointRunnerSource + "`n" + $PSVersionTable.PSVersion.ToString() + "`n" + [Environment]::Version.ToString())
            $checkpointRunnerHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($checkpointRunnerKey)).Substring(0, 24).ToLowerInvariant()
            $checkpointRunnerDir = Join-Path ([IO.Path]::GetTempPath()) 'antiphon-checkpoint-runner'
            $checkpointRunnerDll = Join-Path $checkpointRunnerDir ('CheckpointProcessRunner-' + $checkpointRunnerHash + '.dll')
            if (-not [IO.File]::Exists($checkpointRunnerDll)) {
                [void][IO.Directory]::CreateDirectory($checkpointRunnerDir)
                $checkpointRunnerWork = Join-Path $checkpointRunnerDir ('build-' + [guid]::NewGuid().ToString('N') + '.dll')
                try {
                    Add-Type -TypeDefinition $checkpointRunnerSource -OutputAssembly $checkpointRunnerWork
                    # Concurrent first runs each compile; the first rename wins and the rest use it.
                    try { [IO.File]::Move($checkpointRunnerWork, $checkpointRunnerDll) } catch [IO.IOException] { }
                } finally {
                    if ([IO.File]::Exists($checkpointRunnerWork)) { try { [IO.File]::Delete($checkpointRunnerWork) } catch { } }
                }
                # Assemblies of earlier source versions: best effort, a week old (one still loaded elsewhere stays).
                foreach ($checkpointRunnerOld in @([IO.Directory]::GetFiles($checkpointRunnerDir, 'CheckpointProcessRunner-*.dll'))) {
                    if ($checkpointRunnerOld -eq $checkpointRunnerDll -or [IO.File]::GetLastWriteTimeUtc($checkpointRunnerOld) -gt [DateTime]::UtcNow.AddDays(-7)) { continue }
                    try { [IO.File]::Delete($checkpointRunnerOld) } catch { }
                }
            }
            Add-Type -Path $checkpointRunnerDll
            $checkpointRunnerLoaded = [bool]('Antiphon.Checkpoints.CheckpointProcessRunner' -as [type])
        } catch {
            $checkpointRunnerLoaded = [bool]('Antiphon.Checkpoints.CheckpointProcessRunner' -as [type])
        }
    }
    if (-not $checkpointRunnerLoaded) { Add-Type -TypeDefinition $checkpointRunnerSource }
    Remove-Variable -Name checkpointRunner* -ErrorAction SilentlyContinue
}
