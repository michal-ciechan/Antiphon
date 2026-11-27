#requires -Version 7.0
param([Parameter(Mandatory)] [ValidateSet('Refusal', 'Relative')] [string] $Case)

$ErrorActionPreference = 'Stop'

function Test-LinuxHistoricalBuildPlacement {
    if (-not $IsLinux) { throw 'This witness requires Linux.' }
    $BuildSlot = Join-Path $PSScriptRoot 'antiphon'
    $fixture = [IO.Directory]::CreateTempSubdirectory('antiphon-historical-build-')
    $protected = '/work/evidence/' + $fixture.Name
    try {
        Set-Content -LiteralPath (Join-Path $fixture.FullName 'Witness.cs') -Value 'public sealed class PlacementWitness {}'
        Set-Content -LiteralPath (Join-Path $fixture.FullName 'Witness.csproj') -Value @(
            '<Project Sdk="Microsoft.NET.Sdk">',
            '  <PropertyGroup>',
            '    <TargetFramework>net9.0</TargetFramework>',
            '    <OutputPath>bin/placement/</OutputPath>',
            '  </PropertyGroup>',
            '</Project>'
        )
        if ($Case -eq 'Refusal') {
            if (Test-Path -LiteralPath $protected) { throw 'The evidence destination must be fresh.' }
            $props = '<Project><PropertyGroup><MSBuildProjectExtensionsPath>{0}/</MSBuildProjectExtensionsPath></PropertyGroup></Project>'
            Set-Content -LiteralPath (Join-Path $fixture.FullName 'Directory.Build.props') -Value ($props -f $protected)
            Set-Content -LiteralPath (Join-Path $fixture.FullName 'Directory.Build.targets') -Value @(
                '<Project><Target Name="RestoreSentinel" BeforeTargets="Restore">',
                '  <Error Text="historical_restore_reached" />',
                '</Target></Project>'
            )
        }

        Push-Location -LiteralPath $fixture.FullName
        try {
            $PSNativeCommandUseErrorActionPreference = $false
            $diagnostic = & $BuildSlot build-slot --label "historical-placement-$Case" -- dotnet build Witness.csproj --nologo 2>&1 | Out-String
            $exitCode = $LASTEXITCODE
            Write-Output $diagnostic.TrimEnd()
        } finally {
            Pop-Location
        }

        if ($Case -eq 'Refusal') {
            if ($exitCode -ne 1 -or $diagnostic -notmatch 'workspace_evidence_write: MSBuildProjectExtensionsPath' -or
                $diagnostic -match 'historical_restore_reached' -or (Test-Path -LiteralPath $protected)) {
                throw 'The current launcher must refuse the protected destination before historical restore.'
            }
        } elseif ($exitCode -ne 0 -or
                  -not (Test-Path -LiteralPath (Join-Path $fixture.FullName 'bin/placement/net9.0/Witness.dll')) -or
                  -not (Test-Path -LiteralPath (Join-Path $fixture.FullName 'obj/project.assets.json'))) {
            throw 'The current launcher must build the historical project using its safe relative output.'
        }
        Write-Output "PASS Test-LinuxHistoricalBuildPlacement $Case"
    } finally {
        $fixture.Delete($true)
    }
}

Test-LinuxHistoricalBuildPlacement
