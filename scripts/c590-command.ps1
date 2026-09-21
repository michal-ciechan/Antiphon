# Shared command boundary for CARD-0590 scripts. Dot-source only.
$ErrorActionPreference = 'Stop'

function Invoke-C590 {
    param(
        [Parameter(Mandatory = $true)][string]$Exe,
        [string[]]$ArgumentList = @()
    )
    $record = [ordered]@{ exe = $Exe; args = @($ArgumentList) }
    $json = $record | ConvertTo-Json -Compress
    $sink = $env:ANTIPHON_C590_SINK
    if ($sink) {
        Add-Content -LiteralPath $sink -Value $json -Encoding ascii
    }
    if ($env:ANTIPHON_C590_STUB) {
        $stubPath = $env:ANTIPHON_C590_STUB
        if ($stubPath -and (Test-Path -LiteralPath $stubPath)) {
            $stub = Get-Content -Raw -LiteralPath $stubPath | ConvertFrom-Json
            $joined = ($Exe + ' ' + ($ArgumentList -join ' '))
            foreach ($row in @($stub.commands)) {
                if ($joined -like [string]$row.match) {
                    return [pscustomobject]@{ ExitCode = [int]$row.exit; Stdout = [string]$row.stdout }
                }
            }
            if ($null -ne $stub.defaultExit) {
                return [pscustomobject]@{ ExitCode = [int]$stub.defaultExit; Stdout = '' }
            }
        }
        return [pscustomobject]@{ ExitCode = 0; Stdout = '' }
    }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in $ArgumentList) { [void]$psi.ArgumentList.Add($arg) }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    return [pscustomobject]@{ ExitCode = [int]$proc.ExitCode; Stdout = ($stdout + $stderr) }
}

function Write-C590Result {
    param(
        [Parameter(Mandatory = $true)][string]$EvidenceRoot,
        [Parameter(Mandatory = $true)][bool]$Accepted,
        [string]$Diagnosis = '',
        [int]$ExitCode = 0,
        [hashtable]$Extra
    )
    New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
    $obj = [ordered]@{ accepted = $Accepted; diagnosis = $Diagnosis; exit = $ExitCode }
    if ($Extra) {
        foreach ($key in $Extra.Keys) { $obj[$key] = $Extra[$key] }
    }
    $path = Join-Path $EvidenceRoot 'c590-result.json'
    ($obj | ConvertTo-Json -Compress) | Set-Content -LiteralPath $path -Encoding ascii
    Write-Output ('DIAGNOSIS=' + $Diagnosis)
    exit $ExitCode
}
