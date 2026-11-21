@echo off
rem antiphon <command> [arguments]: the Windows twin of scripts/antiphon (same build, same exit codes; 9 = build failed).
rem Minimal form; the cached launcher in section 2.2 of docs/superpowers/plans/2026-11-19-ps1-to-csharp-migration-plan.md replaces it.
rem cmd.exe parses the arguments before this runs: ^ % & | and inner quotes do not survive, and pwsh drops empty ones.
rem Pass SHAs or HEAD~n (never HEAD^), or run scripts/antiphon from bash.
setlocal
rem Forward slashes: Git Bash hands scripts/antiphon's path over as C:/..., and one ArtifactsPath string keeps one restore.
set "project=%~dp0lib\Antiphon.Scripts"
set "project=%project:\=/%"
dotnet msbuild "%project%" -restore -p:Configuration=Release "-p:ArtifactsPath=%project%/bin/launcher" -nologo -v:q -tl:off -clp:NoSummary -nodeReuse:false -p:UseSharedCompilation=false 1>&2 || exit /b 9
dotnet exec "%project%/bin/launcher/bin/Antiphon.Scripts/release/Antiphon.Scripts.dll" %*
exit /b %errorlevel%
