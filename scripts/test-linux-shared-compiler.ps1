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

if ($Case -eq 'Adapter') {
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
    $again = New-CompilerStart $fake $tokens $repo
    $again.Environment['XDG_CACHE_HOME'] = $scratch
    Set-AntiphonBuildDriverEnvironment -StartInfo $again
    Assert-Compiler ($again.ArgumentList[3] -ceq $argsNow[3]) 'cache and pipe are shared across working directories'
    foreach ($setting in @($null, '', '0')) {
        $off = New-CompilerStart $fake $tokens $scratch
        if ($null -eq $setting) { [void]$off.Environment.Remove('ANTIPHON_LINUX_COMPILER_POLICY') }
        else { $off.Environment['ANTIPHON_LINUX_COMPILER_POLICY'] = $setting }
        Assert-Compiler ($off.Environment.ContainsKey('ANTIPHON_LINUX_COMPILER_POLICY') -eq ($null -ne $setting)) 'optional policy input presence is explicit'
        Set-AntiphonBuildDriverEnvironment -StartInfo $off
        Assert-Compiler (($off.ArgumentList -join "`n") -ceq ($tokens -join "`n")) 'off policy preserves the stock command'
    }
    foreach ($negative in @(@('run', '--no-build', '--', 'build'), @('msbuild', '-getItem:ProjectReference'), @('--info'), @('build', '-p:UseSharedCompilation=false'), @('build', '-p:RoslynTargetsPath=custom'))) {
        $skip = New-CompilerStart $fake $negative $scratch
        Set-AntiphonBuildDriverEnvironment -StartInfo $skip
        Assert-Compiler (($skip.ArgumentList -join "`n") -ceq ($negative -join "`n")) 'negative command/caller choice is preserved'
    }
    $tools = $argsNow[1].Substring('-p:RoslynTargetsPath='.Length)
    $manifest = Join-Path $tools 'manifest.sha256'
    & chmod 700 -- $manifest
    [IO.File]::WriteAllText($manifest, 'corrupted fixture cache')
    $corrupt = New-CompilerStart $fake $tokens $scratch
    $corrupt.Environment['XDG_CACHE_HOME'] = $scratch
    Set-AntiphonBuildDriverEnvironment -StartInfo $corrupt
    Assert-Compiler (($corrupt.ArgumentList -join "`n") -ceq ($tokens -join "`n")) 'corrupted cache fails open to unchanged stock argv'
    Write-Host "C1885 cases=Adapter assertions=$script:assertions failed=0 fakeCompiler=true"
    return
}

throw 'Native fixture will be added with integration; no native proof claimed'
