# Test-owned console peer. Unicode provider captures are loaded, never retyped here.
$DefinitionsOnly = $args -contains '-DefinitionsOnly'
$capture = Get-Content -LiteralPath $env:C449_CAPTURE -Raw | ConvertFrom-Json
$script:template = $capture.screen
$script:composerTemplate = $capture.composerTemplate
$script:highlight = if ($env:C449_REQUESTED -eq 'high') { 1 } else { 2 }
$script:dialog = $true
$script:composer = ''
$script:applied = $null
$script:keys = [System.Collections.Generic.List[string]]::new()
$script:sequence = 0
$script:clearReceived = $null
$script:clearPainted = $null
function Paint {
    if ($script:dialog) {
        $screen = $script:template.Replace('> Keep', '  Keep').Replace('     Switch', '   > Switch')
        if ($script:highlight -eq 1) { $screen = $script:template }
    } else {
        $screen = $script:composerTemplate.Replace('{effort}', [string]$script:applied).Replace('{composer}', $script:composer)
    }
    [Console]::Write(([char]27 + '[2J' + [char]27 + '[H') + $screen)
    if ($script:PaintSink) { & $script:PaintSink $screen }
}
function Publish-Trace([string]$phase) {
    $script:sequence++
    $json = @{ applied = $script:applied; highlight = $script:highlight; keys = $script:keys.ToArray(); composer = $script:composer; pid = $PID;
       sequence = $script:sequence; phase = $phase; clearReceived = $script:clearReceived; clearPainted = $script:clearPainted } |
        ConvertTo-Json -Compress
    $temporary = $env:C449_TRACE + '.tmp'
    [IO.File]::WriteAllText($temporary, $json)
    [IO.File]::Move($temporary, $env:C449_TRACE, $true)
    if ($script:PublishSink) { & $script:PublishSink $phase }
}
function Invoke-FixtureKey([ConsoleKeyInfo]$key) {
    $name = [string]$key.KeyChar
    if ($key.Key -eq 'Enter') { $name = 'Enter' }
    if ($key.KeyChar -eq [char]21) { $name = 'Ctrl+U' }
    $script:keys.Add($name)
    if ($script:dialog) {
        if ($key.KeyChar -eq 'j' -or $key.Key -eq 'DownArrow' -or $key.KeyChar -eq [char]14) { $script:highlight = 3 - $script:highlight }
        if ($key.Key -eq 'Enter' -and $env:C449_STUCK -ne '1') {
            $script:applied = if ($script:highlight -eq 1) { 'xhigh' } else { 'high' }
            $script:dialog = $false
        }
    } elseif ($key.KeyChar -eq [char]21) {
        $script:clearReceived = [DateTime]::UtcNow.ToString('O')
        if ($env:C1516_CLEAR_DELAY_MS) { Start-Sleep -Milliseconds ([int]$env:C1516_CLEAR_DELAY_MS) }
        if ($env:C1516_NEVER_CLEAR -ne '1') { $script:composer = '' }
    }
    elseif ($key.KeyChar -ne [char]0) { $script:composer += $key.KeyChar }
    $phase = if ($name -eq 'Ctrl+U') { 'clear' } elseif ($name -eq 'Enter') { 'enter' } else { 'key' }
    Paint
    if ($name -eq 'Ctrl+U') { $script:clearPainted = [DateTime]::UtcNow.ToString('O') }
    if ($phase -ne 'key') { Publish-Trace $phase }
}
if (!$DefinitionsOnly) {
    Paint
    while ($true) { Invoke-FixtureKey ([Console]::ReadKey($true)) }
}
