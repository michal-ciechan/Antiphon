#requires -Version 7.0
param(
    [Parameter(Mandatory)] [string] $LowSpaceDirectory,
    [Parameter(Mandatory)] [string] $PowerShellEntry
)

$ErrorActionPreference = 'Stop'

function Test-WindowsCheckpointBootstrap {
    if (-not $IsWindows) { throw 'This witness requires native Windows.' }
    $lowSpace = (Get-Item -LiteralPath $LowSpaceDirectory).FullName
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($lowSpace))
    if ($drive.AvailableFreeSpace -ge 20GB) { throw 'The caller volume needs less than 20 GiB free.' }

    $fixture = [IO.Directory]::CreateTempSubdirectory('checkpoint-bootstrap-')
    try {
        $scripts = [IO.Directory]::CreateDirectory((Join-Path $fixture.FullName 'scripts'))
        $cmd = Join-Path $scripts.FullName 'antiphon.cmd'
        $exe = Join-Path $scripts.FullName 'antiphon.exe'
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'antiphon.cmd') -Destination $cmd
        Copy-Item -LiteralPath $PowerShellEntry -Destination $exe
        foreach ($launcher in @($cmd, $exe)) {
            Push-Location -LiteralPath $lowSpace
            try {
                $PSNativeCommandUseErrorActionPreference = $false
                $diagnostic = & $launcher checkpoints row 2>&1 | Out-String
                $exitCode = $LASTEXITCODE
            } finally {
                Pop-Location
            }
            if ($exitCode -ne 9 -or $diagnostic -notmatch 'workspace_disk_low') {
                throw "The launcher at $launcher did not give the low-disk refusal."
            }
            if ([IO.Directory]::Exists((Join-Path $fixture.FullName 'bin-launcher'))) {
                throw "The launcher at $launcher created a bootstrap cache."
            }
        }
        Write-Output 'PASS Test-WindowsCheckpointBootstrap'
    } finally {
        $fixture.Delete($true)
    }
}

Test-WindowsCheckpointBootstrap
