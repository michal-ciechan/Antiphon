#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts test-client` (scripts/lib/Antiphon.Nightly/ClientChecks.cs): the client Vitest
    suite behind its typecheck and lint gates, ending with the unlosable "CLIENT TESTS EXIT CODE: n" line (CARD-0069).

    Unit 32 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because scripts/c590-remote.sh (the
    CARD-0590 nested-lane client case) runs this file by path. Delete it when that harness is ported. Agents run
    `scripts/antiphon test-client` (Windows: scripts\antiphon.cmd test-client) directly, and the nightly's client chunk
    runs the command.

    -JsonResultPath, -TypecheckProject and -LintPath are the command's --json-result-path, --typecheck-project and
    --lint-path; every other argument is a Vitest filter or flag and passes through unchanged. The command's exit code is
    returned; 9 when the CLI cannot be built or started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$flags = @{ '-JsonResultPath' = '--json-result-path'; '-TypecheckProject' = '--typecheck-project'; '-LintPath' = '--lint-path' }
$arguments = @()
for ($i = 0; $i -lt $args.Count; $i++) {
    $item = [string]$args[$i]
    if ($flags.ContainsKey($item) -and ($i + 1) -lt $args.Count) {
        $arguments += @($flags[$item], [string]$args[$i + 1])
        $i++
        continue
    }
    $arguments += $item
}
exit (Invoke-AntiphonCli -Command 'test-client' -Arguments $arguments)
