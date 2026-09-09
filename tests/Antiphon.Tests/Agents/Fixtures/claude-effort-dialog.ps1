# Test-owned console peer. Unicode provider captures are loaded, never retyped here.
$capture = Get-Content -LiteralPath $env:C449_CAPTURE -Raw | ConvertFrom-Json
$template = $capture.screen
$highlight = 2
$dialog = $true
$composer = ''
$applied = $null
$keys = [System.Collections.Generic.List[string]]::new()
function Paint {
    if ($dialog) {
        $screen = $template.Replace('> Keep', '  Keep').Replace('     Switch', '   > Switch')
        if ($highlight -eq 1) { $screen = $template }
    } else {
        $screen = "Fable 5.1 with $applied effort" + [char]10 + '> ' + $composer + [char]10 + '? for shortcuts'
    }
    [Console]::Write(([char]27 + '[2J' + [char]27 + '[H') + $screen)
}
Paint
while ($true) {
    $key = [Console]::ReadKey($true)
    $name = [string]$key.KeyChar
    if ($key.Key -eq 'Enter') { $name = 'Enter' }
    if ($key.KeyChar -eq [char]21) { $name = 'Ctrl+U' }
    $keys.Add($name)
    if ($dialog) {
        if ($key.KeyChar -eq 'j' -or $key.Key -eq 'DownArrow' -or $key.KeyChar -eq [char]14) { $highlight = 3 - $highlight }
        if ($key.Key -eq 'Enter' -and $env:C449_STUCK -ne '1') {
            $applied = if ($highlight -eq 1) { 'xhigh' } else { 'high' }
            $dialog = $false
        }
    } elseif ($key.KeyChar -eq [char]21) { $composer = '' }
    elseif ($key.KeyChar -ne [char]0) { $composer += $key.KeyChar }
    @{ applied = $applied; highlight = $highlight; keys = $keys.ToArray(); composer = $composer; pid = $PID } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath $env:C449_TRACE
    Paint
}
