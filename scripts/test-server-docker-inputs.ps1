#requires -Version 7.0
# Offline fixtures for the linked-source guard. No Docker, network or application host.
$ErrorActionPreference = 'Stop'
$guard = Join-Path $PSScriptRoot 'check-server-docker-inputs.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('card1586-' + [guid]::NewGuid().ToString('N'))
$passed = 0
function Run-Case([string]$Name, [string]$Copy, [int]$Expected, [string]$Ignore = '', [string]$Before = '', [string]$After = '', [switch]$MissingSource) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $fixture = Join-Path $root $Name
    foreach ($directory in @('server', 'src/Helper', 'scripts/lib')) {
        [void](New-Item -ItemType Directory -Path (Join-Path $fixture $directory) -Force)
    }
    '<Project><ItemGroup><Compile Include="../scripts/lib/Server.cs" Link="Server.cs" /></ItemGroup></Project>' | Set-Content (Join-Path $fixture 'server/Server.csproj')
    '<Project><ItemGroup><Compile Include="../../scripts/lib/Helper.cs"><Link>Helper.cs</Link></Compile></ItemGroup></Project>' | Set-Content (Join-Path $fixture 'src/Helper/Helper.csproj')
    'class Server {}' | Set-Content (Join-Path $fixture 'scripts/lib/Server.cs')
    if (-not $MissingSource) { 'class Helper {}' | Set-Content (Join-Path $fixture 'scripts/lib/Helper.cs') }
    $Ignore | Set-Content (Join-Path $fixture '.dockerignore')
    @($Before, 'FROM sdk AS server-build', 'WORKDIR /src', 'COPY server/ server/', 'COPY src/ src/', $Copy,
        'RUN dotnet publish server/Server.csproj', $After) | Set-Content (Join-Path $fixture 'Dockerfile')
    $output = @(& pwsh -NoProfile -File $guard -Repository $fixture 2>&1)
    $exit = $LASTEXITCODE
    if ($exit -ne $Expected -or ($Expected -eq 1 -and ($output -join "`n") -notmatch 'DOCKER INPUT missing scripts/lib/')) {
        throw "$Name expected exit=$Expected actual=$exit`n$($output -join "`n")"
    }
    $script:passed++
    Write-Host "PASS $Name exit=$exit elapsedMs=$($watch.ElapsedMilliseconds)"
}
try {
    Run-Case 'missing-copy' '' 1
    Run-Case 'glob-copy' 'COPY scripts/lib/*.cs scripts/lib/' 0
    Run-Case 'directory-copy' 'COPY scripts/lib/ scripts/lib/' 0
    Run-Case 'json-copy' 'COPY ["scripts/lib/Server.cs", "scripts/lib/Helper.cs", "scripts/lib/"]' 0
    Run-Case 'wrong-destination' 'COPY scripts/lib/*.cs other/' 1
    Run-Case 'wrong-stage' '' 1 -Before "FROM sdk AS other`nWORKDIR /src`nCOPY scripts/lib/*.cs scripts/lib/"
    Run-Case 'after-publish' '' 1 -After 'COPY scripts/lib/*.cs scripts/lib/'
    Run-Case 'ignored-source' 'COPY scripts/lib/*.cs scripts/lib/' 1 -Ignore '**/*.cs'
    Run-Case 'reincluded-source' 'COPY scripts/lib/*.cs scripts/lib/' 0 -Ignore "**/*.cs`n!scripts/lib/*.cs"
    Run-Case 'excluded-parent' 'COPY scripts/lib/*.cs scripts/lib/' 1 -Ignore "scripts/`n!scripts/lib/*.cs"
    Run-Case 'missing-source' 'COPY scripts/lib/*.cs scripts/lib/' 1 -MissingSource
    Run-Case 'unsupported-copy-option' 'COPY --exclude=*.cs scripts/lib/ scripts/lib/' 2
    Write-Host "SERVER DOCKER GUARD tests=$passed failed=0"
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
exit 0
