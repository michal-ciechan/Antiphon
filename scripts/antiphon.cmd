@echo off
rem antiphon <command> [arguments]: the Windows twin of scripts/antiphon (same build, same exit codes; 9 = build failed).
rem Minimal form; the cached launcher in section 2.2 of docs/superpowers/plans/2026-11-19-ps1-to-csharp-migration-plan.md replaces it.
rem cmd.exe parses the arguments before this runs: ^ % & | and inner quotes do not survive, and pwsh drops empty ones.
rem Pass SHAs or HEAD~n (never HEAD^), or run scripts/antiphon from bash.
setlocal
rem The build goes to <checkout>/bin-launcher (ignored, and outside scripts/ so fixtures that copy scripts/lib never carry it).
rem Forward slashes: Git Bash hands scripts/antiphon's path over as C:/..., and one ArtifactsPath string keeps one restore.
for %%I in ("%~dp0..") do set "root=%%~fI"
set "root=%root:\=/%"
set "project=%root%/scripts/lib/Antiphon.Scripts"
set "artifacts=%root%/bin-launcher/antiphon-scripts"
dotnet msbuild "%project%" -restore -p:Configuration=Release "-p:ArtifactsPath=%artifacts%" -nologo -v:q -tl:off -clp:NoSummary -nodeReuse:false -p:UseSharedCompilation=false 1>&2 || exit /b 9
dotnet exec "%artifacts%/bin/Antiphon.Scripts/release/Antiphon.Scripts.dll" %*
exit /b %errorlevel%
