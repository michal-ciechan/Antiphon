param([string]$Case, [string]$ResultsDirectory, [string]$ControlDirectory)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('c1516-trace-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
try {
    $env:C449_CAPTURE = Join-Path $root 'capture.json'
    $env:C449_TRACE = Join-Path $root 'trace.json'
    $env:C449_REQUESTED = 'xhigh'
    $env:C449_STUCK = '0'
    $env:C1516_NEVER_CLEAR = '0'
    $env:C1516_CLEAR_DELAY_MS = '0'
    $ruler = ([string][char]0x2500) * 40
    @{ screen = "> Keep xhigh`n     Switch high"; composer = "$ruler`n> {{composer}}`n$ruler`n? for shortcuts" } |
        ConvertTo-Json | Set-Content -LiteralPath $env:C449_CAPTURE
    . (Join-Path $PSScriptRoot 'claude-effort-dialog.ps1') -DefinitionsOnly
    $script:events = [Collections.Generic.List[string]]::new()
    $script:PaintSink = { param($screen) $script:events.Add('paint:' + $screen) }
    $script:PublishSink = { param($phase) $script:events.Add('publish:' + $phase) }
    function Assert([bool]$value, [string]$message) { if (!$value) { throw $message } }
    function Key([char]$char, [ConsoleKey]$key) { Invoke-FixtureKey ([ConsoleKeyInfo]::new($char, $key, $false, $false, $false)) }
    Key 'j' J
    Assert (!(Test-Path -LiteralPath $env:C449_TRACE)) 'ordinary j must not publish trace'
    Write-Output "`nPASS C1516 ordinary"
    $script:events.Clear()
    Key ([char]13) Enter
    $text = [IO.File]::ReadAllText($env:C449_TRACE)
    $json = $text | ConvertFrom-Json
    Assert ($json.applied -eq 'xhigh' -and $json.composer -eq '' -and $json.sequence -eq 1 -and $json.phase -eq 'enter') 'coherent Enter snapshot'
    Assert (($json.keys -join ',') -eq 'j,Enter') 'Enter exact key prefix'
    Assert ($script:events.Count -eq 2 -and $script:events[0].StartsWith('paint:') -and $script:events[1] -eq 'publish:enter') 'Enter paints before publication'
    Write-Output "`nPASS C1516 enter"
    foreach ($c in 'zz12345678'.ToCharArray()) {
        Key $c A
        Assert ([IO.File]::ReadAllText($env:C449_TRACE) -ceq $text) 'ordinary token characters must not publish trace'
    }
    Write-Output "`nPASS C1516 token"
    $script:events.Clear()
    Key ([char]21) U
    $json = [IO.File]::ReadAllText($env:C449_TRACE) | ConvertFrom-Json
    Assert ($json.composer -eq '' -and $json.sequence -eq 2 -and $json.phase -eq 'clear' -and $json.applied -eq 'xhigh') 'coherent clear snapshot'
    Assert (($json.keys -join ',') -eq 'j,Enter,z,z,1,2,3,4,5,6,7,8,Ctrl+U') 'every key retained'
    Assert ($script:events.Count -eq 2 -and $script:events[0].StartsWith('paint:') -and $script:events[1] -eq 'publish:clear') 'clear paints before publication'
    Assert ($null -ne $json.clearPainted) 'clear publication includes paint observation'
    Write-Output "`nPASS C1516 clear"
    Write-Output 'C487: 4 passed, 0 failed, 4 rows'
    Write-Output 'C487 HARNESS EXIT CODE: 0'
} catch { Write-Output ('FAIL C1516 ' + $_); exit 1 }
finally { [IO.Directory]::Delete($root, $true) }
