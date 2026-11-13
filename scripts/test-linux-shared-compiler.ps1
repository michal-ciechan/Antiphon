#requires -Version 7.0
# CARD-1885. Offline adapters use a fake dotnet; Native owns one private pipe/build.
param([ValidateSet('Adapter', 'Native')][string]$Case = 'Adapter')
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib/build-driver-environment.ps1')
if (-not ('Antiphon.Build.BuildDriverEnvironment' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'lib/BuildDriverEnvironment.cs') }
$script:assertions = 0
function Assert-Compiler([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL C1885 $Name" }
    $script:assertions++
    Write-Host "PASS C1885 $Name"
}
function New-CompilerStart([string]$Executable, [string[]]$Tokens, [string]$Directory) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $Directory
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($token in $Tokens) { $start.ArgumentList.Add($token) }
    $start.Environment['ANTIPHON_LINUX_COMPILER_POLICY'] = '1'
    return $start
}
function Invoke-CompilerChild($Start, [int]$Milliseconds = 30000) {
    $child = [Diagnostics.Process]::Start($Start)
    $out = $child.StandardOutput.ReadToEndAsync(); $err = $child.StandardError.ReadToEndAsync()
    try {
        if (-not $child.WaitForExit($Milliseconds)) { throw 'C1885 child deadline' }
        if (-not $out.Wait(1000) -or -not $err.Wait(1000)) { throw 'C1885 output drain deadline' }
        return @{ Exit = $child.ExitCode; Out = $out.Result; Err = $err.Result }
    } finally {
        if (-not $child.HasExited) { $child.Kill($true); [void]$child.WaitForExit(1000) }
        $child.Dispose()
    }
}

function Test-C1885Adapter {
    # No compiler, SDK mutation, native affinity change, or live broker here.
    # Linux preparation is tested on the real ProcessStartInfo through its PS adapter.
    $start = New-CompilerStart 'dotnet' @('build', 'literal project.csproj', '-maxcpucount:3') $repo
    $before = @($start.ArgumentList)
    # Deliberately unsupported selected SDK/utility is a fail-open path, not a build failure.
    $start.Environment['PATH'] = '/c1885-missing-utilities'
    Set-AntiphonBuildDriverEnvironment -StartInfo $start
    Assert-Compiler (($start.ArgumentList -join "`n") -ceq ($before -join "`n")) 'missing utility preserves literal stock argv'
    foreach ($name in @('DOTNET_PROCESSOR_COUNT', 'DOTNET_GCHeapHardLimit')) {
        Assert-Compiler (-not $start.Environment.ContainsKey($name)) "driver has no $name override"
    }
    if ($IsWindows) {
        Assert-Compiler ($start.Environment['UseSharedCompilation'] -ceq 'false') 'Windows keeps its established sharing policy'
        Write-Host "C1885 cases=Adapter assertions=$script:assertions failed=0 nativeLinux=pending"
        return
    }
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ('c1885-adapter-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($scratch)
    $fakeSdk = Join-Path $scratch '10.0.404'
    [void][IO.Directory]::CreateDirectory((Join-Path $fakeSdk 'Roslyn/bincore'))
    foreach ($file in @('Microsoft.Build.Tasks.CodeAnalysis.dll', 'Microsoft.CSharp.Core.targets', 'bincore/VBCSCompiler', 'bincore/VBCSCompiler.dll', 'bincore/VBCSCompiler.runtimeconfig.json')) {
        [IO.File]::WriteAllText((Join-Path $fakeSdk "Roslyn/$file"), 'fixture fake compiler - never native evidence')
    }
    $fake = Join-Path $scratch 'dotnet'
    [IO.File]::WriteAllText($fake, "#!/bin/sh`nif [ `"`$1`" = '--info' ]; then`nprintf ' Base Path: %s\n' '`$C1885_FAKE_SDK'`nexit 0`nfi`nprintf 'ARG:%s\n' `"`$@`"`nexit 7`n".Replace("'`$C1885_FAKE_SDK'", "'" + $fakeSdk + "'"))
    & chmod 700 -- $scratch $fake
    if ($LASTEXITCODE -ne 0) { throw 'fixture chmod failed' }
    $tokens = @('build', 'literal project.csproj', '-maxcpucount:3', '--', '/*/*/literal/*', '$(never execute)', '')
    $prepared = New-CompilerStart $fake $tokens $scratch
    $prepared.Environment['XDG_CACHE_HOME'] = $scratch
    Set-AntiphonBuildDriverEnvironment -StartInfo $prepared
    $argsNow = @($prepared.ArgumentList)
    Assert-Compiler ($argsNow.Count -eq $tokens.Count + 3) 'enabled compiler command gets exactly three global properties'
    Assert-Compiler ($argsNow[1] -clike '-p:RoslynTargetsPath=*') 'toolset property is present'
    Assert-Compiler ($argsNow[2] -clike '-p:RoslynTasksAssembly=*/Microsoft.Build.Tasks.CodeAnalysis.dll') 'task assembly property is present'
    Assert-Compiler ($argsNow[3] -clike '-p:SharedCompilationId=ac1885-*') 'private stable pipe property is present'
    Assert-Compiler (($argsNow[4..($argsNow.Count - 1)] -join "`n") -ceq ($tokens[1..($tokens.Count - 1)] -join "`n")) 'literal argv including application boundary is retained'
    $result = Invoke-CompilerChild $prepared
    Assert-Compiler ($result.Exit -eq 7) 'fake driver retains command exit status'
    $wrapper = New-CompilerStart (Get-Command pwsh).Source (@('-NoProfile', '-File', (Join-Path $repo 'scripts/build-slot.ps1'), '-Label', 'c1885-offline-wrapper', '--', $fake) + $tokens) $scratch
    $wrapper.Environment['XDG_CACHE_HOME'] = $scratch
    $wrapper.Environment['C589_SLOT_SHIM'] = Join-Path $repo 'scripts/fixtures/c589-slot-shim.ps1'
    $wrapper.Environment['C589_SLOT_SCRIPT'] = 'granted'
    $wrapper.Environment['C589_SLOT_LOG'] = Join-Path $scratch 'slot.log'
    [void]$wrapper.Environment.Remove('C589_COMMAND_SHIM')
    $wrapped = Invoke-CompilerChild $wrapper
    Assert-Compiler ($wrapped.Exit -eq 7) 'real wrapper preserves fake driver failure'
    Assert-Compiler ($wrapped.Out.Contains('ARG:-p:RoslynTargetsPath=') -and $wrapped.Out.Contains('ARG:-p:RoslynTasksAssembly=') -and $wrapped.Out.Contains('ARG:-p:SharedCompilationId=')) 'real wrapper prepares complete literal argv'
    Assert-Compiler ($wrapped.Out.Contains('BUILD SLOT released')) 'real wrapper releases its fixture lease after failure'
    $again = New-CompilerStart $fake $tokens $repo
    $again.Environment['XDG_CACHE_HOME'] = $scratch
    Set-AntiphonBuildDriverEnvironment -StartInfo $again
    Assert-Compiler ($again.ArgumentList[3] -ceq $argsNow[3]) 'cache and pipe are shared across working directories'
    $tools = $argsNow[1].Substring('-p:RoslynTargetsPath='.Length)
    $launcher = Join-Path $tools 'bincore/VBCSCompiler'
    try {
        & chmod 400 -- $launcher
        $unstartable = New-CompilerStart $fake $tokens $scratch
        $unstartable.Environment['XDG_CACHE_HOME'] = $scratch
        Set-AntiphonBuildDriverEnvironment -StartInfo $unstartable
        $stockWhenUnstartable = ($unstartable.ArgumentList -join "`n") -ceq ($tokens -join "`n")
    } finally { & chmod 500 -- $launcher }
    Assert-Compiler $stockWhenUnstartable 'unstartable private launcher fails open before the driver starts'
    $missingTaskset = New-CompilerStart $fake $tokens $scratch
    $missingTaskset.Environment['PATH'] = $scratch
    Set-AntiphonBuildDriverEnvironment -StartInfo $missingTaskset
    Assert-Compiler (($missingTaskset.ArgumentList -join "`n") -ceq ($tokens -join "`n")) 'missing affinity utility preserves a usable stock driver'
    foreach ($setting in @($null, '', '0')) {
        $off = New-CompilerStart $fake $tokens $scratch
        if ($null -eq $setting) { [void]$off.Environment.Remove('ANTIPHON_LINUX_COMPILER_POLICY') }
        else { $off.Environment['ANTIPHON_LINUX_COMPILER_POLICY'] = $setting }
        Assert-Compiler ($off.Environment.ContainsKey('ANTIPHON_LINUX_COMPILER_POLICY') -eq ($null -ne $setting)) 'optional policy input presence is explicit'
        if ($null -ne $setting) { Assert-Compiler ($off.Environment['ANTIPHON_LINUX_COMPILER_POLICY'] -ceq $setting) 'optional policy input value is explicit' }
        Set-AntiphonBuildDriverEnvironment -StartInfo $off
        Assert-Compiler (($off.ArgumentList -join "`n") -ceq ($tokens -join "`n")) 'off policy preserves the stock command'
    }
    foreach ($negative in @(@('run', '--no-build', '--', 'build'), @('msbuild', '-getItem:ProjectReference'), @('--info'), @('build', '-p:UseSharedCompilation=false'), @('build', '-p:RoslynTargetsPath=custom'))) {
        $skip = New-CompilerStart $fake $negative $scratch
        Set-AntiphonBuildDriverEnvironment -StartInfo $skip
        Assert-Compiler (($skip.ArgumentList -join "`n") -ceq ($negative -join "`n")) 'negative command/caller choice is preserved'
    }
    $manifest = Join-Path $tools 'manifest.sha256'
    & chmod 700 -- $manifest
    [IO.File]::WriteAllText($manifest, 'corrupted fixture cache')
    $corrupt = New-CompilerStart $fake $tokens $scratch
    $corrupt.Environment['XDG_CACHE_HOME'] = $scratch
    Set-AntiphonBuildDriverEnvironment -StartInfo $corrupt
    Assert-Compiler (($corrupt.ArgumentList -join "`n") -ceq ($tokens -join "`n")) 'corrupted cache fails open to unchanged stock argv'
    $fallback = Invoke-CompilerChild $wrapper
    Assert-Compiler ($fallback.Exit -eq 7 -and $fallback.Err.Contains('fallback=stock')) 'real wrapper reports preparation failure and preserves stock exit'
    Assert-Compiler (-not $fallback.Out.Contains('ARG:-p:RoslynTargetsPath=') -and $fallback.Out.Contains('BUILD SLOT released')) 'real wrapper runs stock argv and releases after preparation failure'
    Write-Host "C1885 cases=Adapter assertions=$script:assertions failed=0 fakeCompiler=true"
    return
}

function Test-C1885Native {
if (-not $IsLinux) { throw 'Native compiler witness requires Linux; not a passed skip' }
$grant = 0
Assert-Compiler ([int]::TryParse($env:ANTIPHON_BUILD_SLOT_MAXCPUCOUNT, [ref]$grant) -and $grant -gt 0) 'checkpoint command transports its typed grant'
. (Join-Path $PSScriptRoot 'lib/linux-compiler-evidence.ps1')
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('c1885-native-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory((Join-Path $scratch 'B'))
[IO.File]::WriteAllText((Join-Path $scratch 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>')
[IO.File]::WriteAllText((Join-Path $scratch 'A.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="A.cs"/><ProjectReference Include="B/B.csproj"/></ItemGroup></Project>')
[IO.File]::WriteAllText((Join-Path $scratch 'B/B.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
[IO.File]::WriteAllText((Join-Path $scratch 'A.cs'), 'public class A : B { }')
[IO.File]::WriteAllText((Join-Path $scratch 'B/B.cs'), 'public class B { }')
$tokens = @('build', (Join-Path $scratch 'A.csproj'), '--no-incremental', '-p:UseAppHost=false', "-maxcpucount:$grant", '-nodeReuse:false', '--nologo')
$start = New-CompilerStart (Get-Command dotnet).Source $tokens $scratch
$start.Environment['RoslynCommandLineLogFile'] = Join-Path $scratch 'roslyn.log'
$driverAffinity = [Antiphon.Build.LinuxCompilerToolset]::StatusValue('/proc/self/status', 'Cpus_allowed_list')
$policy = [Antiphon.Build.BuildDriverEnvironment]::new()
Assert-Compiler ($start.Environment['ANTIPHON_LINUX_COMPILER_POLICY'] -ceq '1') 'native policy input is explicitly enabled'
$policy.Apply($start)
Assert-Compiler ($start.ArgumentList.Count -eq $tokens.Count + 3) 'production ProcessStartInfo factory selects the Linux compiler policy'
# Same launcher generator, shorter fixture idle; no server starts for the production preparation.
$start.ArgumentList.Clear(); foreach ($token in $tokens) { $start.ArgumentList.Add($token) }
$diagnostic = [Antiphon.Build.LinuxCompilerToolset]::Prepare($start, $grant, 10, ('fixture-' + [IO.Path]::GetFileName($scratch)), $true)
Write-Host $diagnostic
$pipe = [regex]::Match($diagnostic, ' pipe=(\S+)').Groups[1].Value
$sdk = [regex]::Match($diagnostic, ' sdk=(\S+)').Groups[1].Value
$cpus = [regex]::Match($diagnostic, ' cpus=(\S+)').Groups[1].Value
$chosen = Get-C1885Set $cpus
$owned = @{}
Assert-Compiler (@(Get-C1885Compilers $pipe).Count -eq 0) 'private fixture pipe starts without a compiler'
$process = $null
$samples = [Collections.Generic.List[object]]::new()
$runRoot = Join-Path $repo ('.antiphon/c1885/native-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($runRoot)
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    while (-not $process.WaitForExit(100)) {
        if ($timer.Elapsed.TotalSeconds -gt 30) { throw 'native build exceeded its provisional 30-second bound' }
        foreach ($compiler in @(Get-C1885Compilers $pipe)) {
            $owned[('{0}:{1}' -f $compiler.Pid, $compiler.Start)] = $compiler
            $samples.Add(@{ Seconds = $timer.Elapsed.TotalSeconds; Compiler = $compiler })
            foreach ($threadSet in $compiler.Threads) {
                foreach ($cpu in (Get-C1885Set $threadSet)) { if (-not $chosen.Contains($cpu)) { throw 'compiler thread escaped selected CPU set' } }
            }
        }
    }
    Assert-Compiler ($process.ExitCode -eq 0) 'one native build succeeds'
    $buildSeconds = $timer.Elapsed.TotalSeconds
    Assert-Compiler ($stdout.Wait(1000) -and $stderr.Wait(1000)) 'native output drains within deadline'
    [IO.File]::WriteAllText((Join-Path $runRoot 'build.log'), $stdout.Result + $stderr.Result)
    Assert-Compiler ($owned.Count -eq 1) 'both dependent projects share one PID and start identity'
    $log = [IO.File]::ReadAllText((Join-Path $scratch 'roslyn.log'))
    [IO.File]::WriteAllText((Join-Path $runRoot 'roslyn.log'), $log)
    $compilerPid = @($owned.Values)[0].Pid
    Assert-Compiler (([regex]::Matches($log, "ID=VBCSCompiler $compilerPid TID=\d+: Writing Completed response")).Count -ge 2) 'two successful server responses use the observed compiler'
    Assert-Compiler (([regex]::Matches($log, 'CompilerServer: server - server processed compilation')).Count -eq 2) 'both compilations use shared compilation without fallback'
    Assert-Compiler ($log -notmatch 'CompilerServer: (?!server - server processed compilation)') 'no stand-alone compiler response is hidden'
    Assert-Compiler (@($owned.Values)[0].Argv -ccontains (Join-Path $sdk 'Roslyn/bincore/VBCSCompiler.dll')) 'launcher execs the original selected compiler DLL'
    Assert-Compiler ([Antiphon.Build.LinuxCompilerToolset]::StatusValue('/proc/self/status', 'Cpus_allowed_list') -ceq $driverAffinity) 'driver affinity is unchanged'
    $idle = [Diagnostics.Stopwatch]::StartNew()
    while (@(Get-C1885Compilers $pipe).Count -gt 0 -and $idle.Elapsed.TotalSeconds -lt 15) { Start-Sleep -Milliseconds 100 }
    Assert-Compiler (@(Get-C1885Compilers $pipe).Count -eq 0) 'compiler automatically exits within the fixture idle allowance'
    $samples | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'samples.json')
    @{ Sha = (& git -C $repo rev-parse HEAD); Grant = $grant; Scratch = $scratch; Diagnostic = $diagnostic;
        BuildSeconds = $buildSeconds; IdleSeconds = $idle.Elapsed.TotalSeconds; Assertions = $script:assertions;
        SetupSeconds = $null; Unknown = @('restore separate span', 'GC CPU', 'SDK/cache IO individual spans') } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'receipt.json')
    Write-Host "C1885 cases=Native assertions=$script:assertions failed=0 builds=1 evidence=$runRoot"
} finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(1000) }
        $process.Dispose()
    }
    if (@(Get-C1885Compilers $pipe).Count -gt 0) { Stop-C1885PrivateCompiler (Get-Command dotnet).Source (Join-Path $sdk 'Roslyn/bincore/VBCSCompiler.dll') $pipe $scratch }
    if (@(Get-C1885Compilers $pipe).Count -gt 0) { throw 'task-owned compiler residue remains on the private pipe' }
}
}
if ($Case -eq 'Adapter') { Test-C1885Adapter } else { Test-C1885Native }
