# Run through the checkpoint tool or scripts/build-slot.ps1 (one outer lease).
# Local equivalent only: no Docker, live caches, image qualification or rollout.
#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (-not $IsLinux) { throw 'This probe requires the Linux image SDK and installed net9 packs.' }
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence = Join-Path $repo ('.antiphon/seed-pin-native-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
$variables = @('HOME', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'NUGET_SCRATCH',
    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE')
$saved = @{}
foreach ($name in $variables) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    git -C $repo rev-parse HEAD | Set-Content (Join-Path $evidence 'source.txt')
    dotnet --list-sdks | Tee-Object -FilePath (Join-Path $evidence 'sdks.txt')
    foreach ($relative in @('scripts/c590-remote.sh', 'docker/session-runner-grok/verify-codex-image.sh')) {
        $scriptPath = Join-Path $repo $relative
        bash -n $scriptPath
        $project = @([regex]::Matches([IO.File]::ReadAllText($scriptPath),
            '<Project Sdk="Microsoft.NET.Sdk">.*?</Project>', 'Singleline') |
            Where-Object { $_.Value.Contains('<TargetFramework>net9.0</TargetFramework>') })
        if ($project.Count -ne 1) { throw "Expected one net9 smoke project in $relative" }
        # Outside the checkout so its Directory.Build.props/targets cannot alter the probe.
        $temp = [IO.Directory]::CreateTempSubdirectory('antiphon-seed-smoke-').FullName
        $caseEvidence = Join-Path $evidence ([IO.Path]::GetFileNameWithoutExtension($relative))
        [void][IO.Directory]::CreateDirectory($caseEvidence)
        try {
            foreach ($directory in @('home/.nuget', 'packages', 'scratch')) {
                [void][IO.Directory]::CreateDirectory((Join-Path $temp $directory))
            }
            $env:HOME = $env:DOTNET_CLI_HOME = Join-Path $temp 'home'
            $env:NUGET_PACKAGES = Join-Path $temp 'packages'
            $env:NUGET_SCRATCH = Join-Path $temp 'scratch'
            $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
            $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
            $project[0].Value | Set-Content (Join-Path $temp 'Smoke.csproj')
            'var context = new Microsoft.AspNetCore.Http.DefaultHttpContext(); System.Console.WriteLine(context.Response.StatusCode == 200 ? "SEED_SMOKE_PACKS_OK" : "bad-context");' |
                Set-Content (Join-Path $temp 'Program.cs')
            '<configuration><packageSources><clear/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>' |
                Set-Content (Join-Path $temp 'NuGet.Config')
            Copy-Item (Join-Path $temp 'Smoke.csproj') $caseEvidence
            Push-Location $temp
            try {
                dotnet restore Smoke.csproj --configfile NuGet.Config --no-http-cache -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1 |
                    Tee-Object -FilePath (Join-Path $caseEvidence 'restore.log')
                dotnet build Smoke.csproj --no-restore -p:OutputPath=bin-seed-pin-native/ -p:UseAppHost=true -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1 |
                    Tee-Object -FilePath (Join-Path $caseEvidence 'build.log')
                $output = & (Join-Path $temp 'bin-seed-pin-native/Smoke')
                if ($output -cne 'SEED_SMOKE_PACKS_OK') { throw "Native stdout mismatch: $output" }
                if (Get-ChildItem $env:NUGET_PACKAGES -Force) { throw 'Offline restore populated the empty packages cache.' }
                Copy-Item (Join-Path $temp 'obj/project.assets.json') $caseEvidence
                "PASS $relative syntax=0 restore=0 build=0 run=0 stdout=$output packages=empty" |
                    Tee-Object -FilePath (Join-Path $caseEvidence 'result.txt')
            } finally { Pop-Location }
        } finally { Remove-Item -LiteralPath $temp -Recurse -Force }
    }
} finally {
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Write-Host "Native evidence: $evidence"
}
