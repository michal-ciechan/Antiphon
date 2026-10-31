#requires -Version 5.1
<#
.SYNOPSIS
Print bounded evidence for failed tests in one or more explicitly selected TRX files.
.DESCRIPTION
Diagnostic helper, always exits 0, including malformed/missing inputs (reported as
TRX-READ-ERROR). It grants no test or release credit. ASCII-only, PowerShell 5.1 safe.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string[]]$Path = @())
$ErrorActionPreference = 'Stop'
$count = 0
function Get-Prefix([string]$Text, [int]$Limit) {
    return $Text.Substring(0, [Math]::Min($Limit, $Text.Length))
}
foreach ($file in $Path) {
    try {
        [xml]$xml = Get-Content -LiteralPath $file -Raw
        $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
        $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        if ($xml.DocumentElement.LocalName -ne 'TestRun' -or $xml.DocumentElement.NamespaceURI -ne $ns.LookupNamespace('t')) { throw 'Not a supported TRX TestRun.' }
        # Same failure set as TrxReport and run-checkpoint.ps1.
        foreach ($result in $xml.SelectNodes('//t:UnitTestResult[@outcome="Failed" or @outcome="Error" or @outcome="Timeout" or @outcome="Aborted"]', $ns)) {
            $count++
            $message = $result.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
            $stack = $result.SelectSingleNode('t:Output/t:ErrorInfo/t:StackTrace', $ns)
            $stdout = $result.SelectSingleNode('t:Output/t:StdOut', $ns)
            "FILE $file"
            "TEST $($result.testName)"
            "DURATION $($result.duration)"
            $messageText = ''; if ($message) { $messageText = $message.InnerText }
            "MESSAGE $(Get-Prefix $messageText 1500)"
            'STACK (first 3 frames)'
            if ($stack) {
                $frames = @($stack.InnerText -split '\r?\n' | Where-Object { $_.Trim() -match '^at\s+' })
                # Preserve useful non-.NET stack formats rather than discarding all evidence.
                if ($frames.Count -eq 0) { $frames = @($stack.InnerText -split '\r?\n' | Where-Object { $_.Trim() }) }
                $frames | Select-Object -First 3
            }
            $stdoutText = ''; if ($stdout) { $stdoutText = $stdout.InnerText }
            "STDOUT $(Get-Prefix $stdoutText 2500)"
        }
    } catch { "TRX-READ-ERROR ${file}: $($_.Exception.Message)" }
}
if ($count -eq 0) { 'no failures' }
exit 0
