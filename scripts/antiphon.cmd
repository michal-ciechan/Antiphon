@echo off
rem antiphon <command> [arguments]: runs antiphon-scripts (scripts/lib/Antiphon.Scripts) from this checkout and passes the
rem arguments, stdin, stdout, stderr and exit code through untouched; exit 9 (one stderr line) when the CLI could not be
rem built or started. The Windows twin of scripts/antiphon, with the same cache and protocol (docs/testing-and-build.md,
rem "The CLI entry (cached launcher)").
rem Warm: bin-launcher\antiphon-scripts\current names the published build of the CLI's inputs, and this runs it with
rem ANTIPHON_CLI_CHECK set: one dotnet process, no msbuild. Before any command the CLI checks its inputs' identity and, when
rem they changed, builds and publishes the current one under the build lock and runs that instead (scripts/lib/Antiphon.CliLauncher).
rem First run (nothing published): the launcher project is built into a fresh boot-<n> folder and publishes the CLI.
rem cmd.exe parses the arguments before this runs: ^ % & | and inner quotes do not survive, and pwsh drops empty ones.
rem Pass SHAs or HEAD~n (never HEAD^), or run scripts/antiphon from bash. From PowerShell scripts\antiphon.exe runs instead
rem once the CLI has made it (generated, untracked; scripts/lib/Antiphon.CliLauncher/CliEntry.cs): no cmd.exe in between.
setlocal
for %%I in ("%~dp0..") do set "root=%%~fI"
set "cache=%root%\bin-launcher\antiphon-scripts"
set "ANTIPHON_CLI_CHECK="
if /i "%~1:%~2"=="checkpoints:row" goto :checkpoint_disk
if /i "%~1:%~2"=="checkpoints:run" goto :checkpoint_disk
if /i "%~1:%~2"=="checkpoints:start" goto :checkpoint_disk
goto :launch

:checkpoint_disk
rem Measure before resolving or creating either bootstrap cache.
set "ANTIPHON_CHECKPOINT_ENTRY_ROOT=%root%"
powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$paths=@($env:ANTIPHON_CHECKPOINT_ENTRY_ROOT,(Get-Location -PSProvider FileSystem).ProviderPath,[IO.Path]::GetTempPath()); foreach($path in $paths) { try { $free=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($path)).AvailableFreeSpace } catch { continue }; if($free -lt 20GB) { [Console]::Error.WriteLine(('checkpoints: workspace_disk_low: {0} has {1} bytes available. Free at least 20 GiB before retrying.' -f $path,$free)); exit 9 } }; exit 0"
if errorlevel 1 exit /b 9

:launch
call :resolve
if defined dll goto :run
call :bootstrap || exit /b 9
if not defined dll goto :unpublished
:run
set "ANTIPHON_CLI_CHECK=%root%"
dotnet exec "%dll%" %*
exit /b %errorlevel%

:resolve
set "dll="
set "id="
if not exist "%cache%\current" exit /b 0
set /p id=<"%cache%\current"
if not defined id exit /b 0
if exist "%cache%\%id%\Antiphon.Scripts.dll" set "dll=%cache%\%id%\Antiphon.Scripts.dll"
exit /b 0

:bootstrap
if not exist "%cache%" mkdir "%cache%" 2>nul
set "boot="
for /l %%N in (1,1,20) do if not defined boot call :claim
if not defined boot goto :noboot
pushd "%root%"
dotnet build "%root%\scripts\lib\Antiphon.CliLauncher\Antiphon.CliLauncher.csproj" -c Release -o "%boot%\out" --artifacts-path "%boot%\artifacts" -nologo -v q -nodeReuse:false -p:UseSharedCompilation=false >"%boot%\build.log" 2>&1
set "built=%errorlevel%"
popd
if not "%built%"=="0" goto :bootfail
if not exist "%boot%\out\Antiphon.CliLauncher.dll" goto :bootfail
dotnet exec "%boot%\out\Antiphon.CliLauncher.dll" publish "%root%"
set "published=%errorlevel%"
rd /s /q "%boot%" 2>nul
if not "%published%"=="0" exit /b 9
call :resolve
exit /b 0

:claim
set "try=%cache%\boot-%RANDOM%%RANDOM%"
mkdir "%try%" 2>nul && set "boot=%try%"
exit /b 0

:unpublished
echo antiphon-cli: no published CLI under %cache% after the build>&2
exit /b 9

:noboot
echo antiphon-cli: cannot create a bootstrap folder under %cache%>&2
exit /b 9

:bootfail
echo antiphon-cli: the launcher did not build ^(exit %built%, log %boot%\build.log^)>&2
exit /b 9
