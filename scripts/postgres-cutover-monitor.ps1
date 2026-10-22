#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1408 (CARD-1401 stage C3): read-only post-cutover Postgres monitor and D-11 rollback trigger.
.DESCRIPTION
    Measures database latency and failure rate from the desktop server's point of view, over HTTP GETs,
    and decides whether the documented rollback (plan phase 6) is due. It prints the decision and exits
    with a distinct code. The operator acts on it; this script never acts.

    Modes (default Plan):
      Plan       Print what the other modes would do. No HTTP, no TCP, no file.
      Baseline   -SampleCount GETs of the pipeline URL, the window's failed tasks and lands, the pool flags,
                 /health and /api/version. Writes -BaselinePath. Run immediately before phase 1, from the
                 desktop, against the current localhost database.
      PreWindow  -ConnectSampleCount TCP connects to -Server2Host:-Server2Port (the Tailscale address).
                 A median above -ConnectAbortMs prints ABORT (exit 3). -LanHost is measured as evidence only.
      Watch      Fresh pipeline samples and failure count compared with -BaselinePath. Prints ROLLBACK (exit 2)
                 when the fresh median is above both -MedianFloorMs and -MedianRatio times the baseline median,
                 or when the window's failures exceed the baseline count by more than -FailureSlack.
    Every mode is a dry plan until -Execute is given.

    Exit codes: 0 OK, 1 WARN, 2 ROLLBACK, 3 ABORT, 4 INVALID (bad input, missing or old baseline, nothing
    measured, a failure count that could not be read, or an unexpected error).
    WARN means: sample errors in the window, failures above baseline within the slack, health not 200, or a
    version SHA that differs from the baseline. None of those is a rollback trigger.

    Failure count: GET /api/agent-tasks?projectId=<id>&since=<window start> (the list refuses any other key
    with 400). Failed tasks are items with status Failed. List items (AgentTaskSummaryDto) carry no land
    outcome, so every item with a land attempt (landAttempt > 0, landRequestedAt or landStartedAt set) and
    every Succeeded worktree task (a refusal before the attempt counter moved leaves no other hint) is read
    from GET /api/agent-tasks/{id}, at most -MaxLandDetailRequests of them. A failed land is a detail whose
    landing.phase or landing.publication is Refused, whose landRequest.state is NeedsResolution, whose
    landRequest.terminalFailureCode is set without a publication (a land that threw), or that has a
    LandRefused event at or after the window start (refusals that never move the landing op to Refused).
    Known gaps, equal in Baseline and Watch: a land that fails in the window on a task completed before it
    is not listed, an unresolved NeedsResolution counts in every window whatever its age, and a
    Blocked task whose land threw before the window (terminalFailureCode without a publication) counts in
    every window the list returns it in (CARD-1423 A4). Each cancels in the Baseline-to-Watch delta.
    A list or detail that cannot be read, or more candidates than the cap, is INVALID unless a rollback
    trigger already fired: never a silent zero.

    Detail cap: -MaxLandDetailRequests (50) bounds the detail reads per measurement. It is sized for the default
    30-minute window, where the board lists a handful of land candidates; at the observed dispatch rate it only
    bites with -WindowMinutes around 8 hours or more (CARD-1423 A3). Then raise the cap or shorten the window.
    Over the cap is INVALID with that remedy in the UNREADABLE line, never a partial count.

    Read-only: GET requests and TCP handshakes only. It sends no delegation token: the task detail GET writes
    poll bookkeeping only for a token-bearing caller, so never add auth headers. It never writes to the
    database, restarts anything or changes a setting. The only files it writes are its JSON baseline and result (default
    .antiphon/postgres-cutover/, which is gitignored). It takes no credentials. Diagnostics properties whose
    name or value looks like a credential (password, token, dsn, connection string) are dropped, and URL
    userinfo (from :// to the last @ on the same line: a newline is the only terminator, so a password may hold
    quotes, spaces, angle brackets, '/' or '@', and a literal @ later on the line is over-masked; CARD-1423 DEF-1)
    and secret-looking query, fragment and connection-string values (quoted values included; key names compared
    after percent-decoding; a plain value that percent-decodes, with '+' read as a space, to a secret pair or a
    userinfo URL is masked whole, as is one still encoded after four rounds) are masked in every printed line,
    error and recorded JSON. The raw and escaped userinfo of the given URLs is also replaced wherever it appears
    there, so an error that quotes the credential without its URL is covered.

    Seams for tests, so no server or database is needed:
      -HttpGet      { param([string]$Url) } returns @{ StatusCode = 200; Body = '<text>'; ElapsedMs = <optional> }
      -TcpConnectMs { param([string]$HostName, [int]$Port) } returns milliseconds, or -1 on failure
      -Now          { } returns a UTC [datetime]
    Dot-source the script (". scripts/postgres-cutover-monitor.ps1") to load the functions without running.

    Operator-only: scheduling this script, the project id and endpoints, the server2 addresses, tightening the
    thresholds in the window log before phase 1, and running phase 6 after ROLLBACK.
.EXAMPLE
    pwsh -NoProfile -File scripts/postgres-cutover-monitor.ps1 -Mode Baseline -ProjectId <guid> -Execute
.EXAMPLE
    pwsh -NoProfile -File scripts/postgres-cutover-monitor.ps1 -Mode PreWindow -Server2Host 100.93.77.126 -LanHost 192.168.4.34 -Execute
.EXAMPLE
    pwsh -NoProfile -File scripts/postgres-cutover-monitor.ps1 -Mode Watch -ProjectId <guid> -Execute
#>
[CmdletBinding()]
param(
    [ValidateSet('Plan', 'Baseline', 'PreWindow', 'Watch')]
    [string]$Mode = 'Plan',
    [switch]$Execute,
    [string]$ServerBaseUrl = 'http://localhost:17202',
    [string]$ProjectId = '',
    [string]$PipelineUrl = '',
    [string]$TaskListUrl = '',
    [string]$Server2Host = '',
    [int]$Server2Port = 5432,
    [string]$LanHost = '',
    [int]$SampleCount = 20,
    [int]$ConnectSampleCount = 20,
    [int]$WindowMinutes = 30,
    [string]$WindowStartUtc = '',
    [string]$BaselinePath = '',
    [string]$ResultPath = '',
    [double]$MedianFloorMs = 1000,
    [double]$MedianRatio = 3,
    [int]$FailureSlack = 2,
    [double]$ConnectAbortMs = 20,
    [int]$HttpTimeoutSec = 60,
    [int]$MaxLandDetailRequests = 50,
    [scriptblock]$HttpGet,
    [scriptblock]$TcpConnectMs,
    [scriptblock]$Now
)

Set-StrictMode -Version 2

# ---- pure helpers -------------------------------------------------------------------------------

function Get-CutoverMedian {
    param([double[]]$Values)
    $v = @()
    foreach ($x in @($Values)) { if ($null -ne $x) { $v += [double]$x } }
    $v = @($v | Sort-Object)
    if ($v.Count -eq 0) { return $null }
    $mid = [int][math]::Floor($v.Count / 2)
    if (($v.Count % 2) -eq 1) { return [double]$v[$mid] }
    return ([double]$v[$mid - 1] + [double]$v[$mid]) / 2
}

function Get-CutoverProperty {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($k in $Object.Keys) { if ([string]$k -ieq $Name) { return $Object[$k] } }
        return $null
    }
    $p = $Object.PSObject.Properties[$Name]
    if ($p) { return $p.Value }
    return $null
}

function Test-CutoverSecretName {
    param([string]$Name)
    return ($Name -match '(?i)password|passwd|pwd|secret|token|connectionstring|connstr|dsn|authorization|apikey|api_key|credential')
}

# A value shaped like a credential whatever its property is called: a URL or DSN with userinfo (to the last '@'
# on the same line, as Hide-CutoverText masks it; CARD-1423 DEF-1), or a connection-string pair such as Password= / Pwd=.
function Test-CutoverSecretValue {
    param([string]$Value)
    if ($Value -match '(?i)[a-z][a-z0-9+.\-]*://[^\r\n]*@') { return $true }
    return ($Value -match '(?i)(^|[;\s])(password|passwd|pwd|user id|uid|secret|token)\s*=')
}

# The raw and escaped userinfo of each URL this run was given (CARD-1423 DEF-1): the text from '://' to the last '@' on
# its first line, its percent-escaped and percent-decoded spellings, and the [uri] parse's UserInfo (which escapes a
# double quote, a space and angle brackets but keeps an apostrophe). Hide-CutoverText replaces them wherever they
# appear, so an error that quotes the credential without its URL is covered too. Empty forms are dropped.
function Get-CutoverUserinfoForms {
    param([string[]]$Urls)
    $forms = New-Object System.Collections.Generic.List[string]
    foreach ($u in @($Urls)) {
        if (-not $u -or $u -notmatch '(?i)://(?<ui>[^\r\n]*)@') { continue }
        $raw = [string]$Matches['ui']
        if (-not $raw) { continue }
        [void]$forms.Add($raw)
        try { [void]$forms.Add([uri]::EscapeDataString($raw)) } catch { }
        try { [void]$forms.Add([uri]::UnescapeDataString($raw)) } catch { }
        try { $parsed = [uri]$u; if ($parsed.UserInfo) { [void]$forms.Add([string]$parsed.UserInfo) } } catch { }
    }
    return @($forms | Where-Object { $_ } | Select-Object -Unique)
}

# The userinfo forms of the URLs the current invocation was given; set by Invoke-PostgresCutoverMonitor for its
# duration and empty otherwise, so a direct Hide-CutoverText call masks by the rules alone.
$script:CutoverKnownUserinfo = @()

# A key=value name that carries a secret, compared after percent-decoding: .NET sends access%5Ftoken as access_token.
function Test-CutoverSecretKey {
    param([string]$Key)
    $decoded = $Key
    try { $decoded = [uri]::UnescapeDataString($Key) } catch { $decoded = $Key }
    return ($decoded -match '(?i)token|key|password|passwd|pwd|secret|sig|signature|auth|credential')
}

# A plain (non-secret-keyed) value whose percent-decoding reveals a secret (CARD-1423 A2): x=y%26token%3DS reaches
# the server as x=y&token=S, and next=http%3A%2F%2Fu%3Ap%40h as a URL with userinfo. The server reads '+' in a
# value as a space, exactly as it reads %20, so every round is also checked with '+' as a space (DEF-1). Decoding
# repeats while the text changes, up to four rounds, so a double-encoded pair is found; a value still encoded
# after the bound is a secret whatever it hides (DEF-1: a pair encoded five times passed). A value that decodes
# to nothing secret is kept.
function Test-CutoverEncodedSecret {
    param([string]$Value)
    $current = $Value
    for ($i = 0; $i -lt 4; $i++) {
        if ($current -notmatch '%[0-9a-fA-F]{2}|\+') { return $false }
        $decoded = $current
        try { $decoded = [uri]::UnescapeDataString($current) } catch { return $true }
        $spaced = $decoded.Replace('+', ' ')
        if ((Hide-CutoverText -Text $decoded) -ne $decoded -or (Hide-CutoverText -Text $spaced) -ne $spaced) { return $true }
        if ($decoded -eq $current) { return $false }
        $current = $decoded
    }
    return ($current -match '%[0-9a-fA-F]{2}')
}

# Masks, in any text before it is printed, recorded or returned (exception messages that embed a URL included):
#  1. URL userinfo, from the first scheme '://' to the LAST '@' on the same line. A newline is the only terminator,
#     so a password may hold '/', '@', quotes, spaces or angle brackets (CARD-1423 A1 and DEF-1: .NET keeps ' " space
#     < > as part of the password, and the old class stopped at them); a literal '@' later on the line is over-masked.
#  2. A quoted value (Password="..." or Password='...', DEF-1) of a secret-looking key, masked whole with its quotes;
#     a plain key's quoted value is masked whole when it percent-decodes to a secret. Skipped for JSON text, where a
#     raw quote is structure and a quote inside a value is escaped.
#  3. A secret-looking key=value pair (query strings, fragments, connection strings). A plain value is searched for
#     pairs of its own (a URL inside a value, a '#' fragment) and, when it percent-decodes to a secret, masked whole.
#  4. The raw and escaped userinfo forms of the URLs this run was given ($script:CutoverKnownUserinfo, plus
#     -KnownUserinfo), replaced wherever they appear (DEF-1), longest first.
# -JsonText is for serialized JSON: a pair value stops at a structural '"' (an escaped quote is part of the value) and
# the known forms are matched in their JSON-escaped spelling too. The request itself always uses the unmasked URL.
function Hide-CutoverText {
    param([string]$Text, [switch]$JsonText, [string[]]$KnownUserinfo = @())
    if (-not $Text) { return $Text }
    $t = [regex]::Replace($Text, '(?i)\b([a-z][a-z0-9+.\-]*://)[^\r\n]*@', '$1<redacted>@')
    $lead = '(?i)(?<lead>(?:^|[?&;#"''\s]|\\u0026)(?<key>(?:[a-z0-9_.\-]|%[0-9a-f]{2})+)\s*=\s*)'
    if (-not $JsonText) {
        $quoted = [System.Text.RegularExpressions.MatchEvaluator]{
            param($m)
            $val = $m.Groups['val'].Value
            if ((Test-CutoverSecretKey -Key $m.Groups['key'].Value) -or (Test-CutoverEncodedSecret -Value $val.Substring(1, $val.Length - 2))) { return $m.Groups['lead'].Value + '<redacted>' }
            return $m.Value
        }
        $t = [regex]::Replace($t, $lead + '(?<val>"[^"\r\n]*"|''[^''\r\n]*'')', $quoted)
    }
    $valClass = '(?:[^&;\s\\]|\\.)+'
    if ($JsonText) { $valClass = '(?:[^&;\s"\\]|\\.)+' }
    $pair = [System.Text.RegularExpressions.MatchEvaluator]{
        param($m)
        if (Test-CutoverSecretKey -Key $m.Groups['key'].Value) { return $m.Groups['lead'].Value + '<redacted>' }
        $val = $m.Groups['val'].Value
        # A plain value can hold pairs of its own (a nested URL's query, a '#' fragment): they are masked in place.
        $inner = Hide-CutoverText -Text $val -JsonText:$JsonText
        if ($inner -ne $val) { return $m.Groups['lead'].Value + $inner }
        # A plain value that percent-decodes to a secret (an embedded pair, a URL with userinfo) is one whole.
        if (Test-CutoverEncodedSecret -Value $val) { return $m.Groups['lead'].Value + '<redacted>' }
        return $m.Value
    }
    $t = [regex]::Replace($t, $lead + '(?<val>' + $valClass + ')', $pair)
    $forms = @(@($script:CutoverKnownUserinfo) + @($KnownUserinfo) | Where-Object { $_ })
    if ($JsonText) {
        $forms = @($forms | ForEach-Object { $_; $j = ConvertTo-Json -InputObject ([string]$_) -Compress; $j.Substring(1, $j.Length - 2) })
    }
    foreach ($f in @($forms | Where-Object { $_ } | Select-Object -Unique | Sort-Object -Property Length -Descending)) { $t = $t.Replace($f, '<redacted>') }
    return $t
}

function Hide-CutoverSecrets {
    param($Object, [int]$Depth = 0)
    if ($null -eq $Object -or $Depth -gt 12) { return $Object }
    if ($Object -is [string]) { if (Test-CutoverSecretValue -Value $Object) { return '<redacted>' } else { return $Object } }
    if ($Object -is [bool] -or $Object -is [int] -or $Object -is [long] -or $Object -is [double] -or $Object -is [decimal] -or $Object -is [datetime] -or $Object -is [guid]) { return $Object }
    if ($Object -is [System.Collections.IDictionary]) {
        $out = [ordered]@{}
        foreach ($k in $Object.Keys) {
            if (-not (Test-CutoverSecretName -Name ([string]$k))) { $out[[string]$k] = Hide-CutoverSecrets -Object $Object[$k] -Depth ($Depth + 1) }
        }
        return $out
    }
    if ($Object -is [System.Collections.IEnumerable]) {
        $list = New-Object System.Collections.ArrayList
        foreach ($i in $Object) { [void]$list.Add((Hide-CutoverSecrets -Object $i -Depth ($Depth + 1))) }
        return $list.ToArray()
    }
    if ($Object.PSObject.BaseObject -is [System.Management.Automation.PSCustomObject]) {
        $out = [ordered]@{}
        foreach ($p in $Object.PSObject.Properties) {
            if (-not (Test-CutoverSecretName -Name $p.Name)) { $out[$p.Name] = Hide-CutoverSecrets -Object $p.Value -Depth ($Depth + 1) }
        }
        return $out
    }
    return $Object
}

# A list item (AgentTaskSummaryDto) that may carry a land outcome. Only these need the detail read: a land
# attempt or pending land, or a Succeeded worktree task. The last is the only land-eligible shape a refusal
# made before the attempt counter moved (landing_protocol_unavailable) leaves behind, because ClearPending
# clears landRequestedAt and landStartedAt and landAttempt stays 0 (LandApproval.RequestStatusEligible).
function Test-CutoverLandCandidate {
    param($Item)
    if ($null -eq $Item) { return $false }
    $attempt = Get-CutoverProperty -Object $Item -Name 'landAttempt'
    if ($null -ne $attempt -and [int]$attempt -gt 0) { return $true }
    if ($null -ne (Get-CutoverProperty -Object $Item -Name 'landRequestedAt')) { return $true }
    if ($null -ne (Get-CutoverProperty -Object $Item -Name 'landStartedAt')) { return $true }
    return ([string](Get-CutoverProperty -Object $Item -Name 'workspace') -eq 'Worktree' -and [string](Get-CutoverProperty -Object $Item -Name 'status') -eq 'Succeeded')
}

# A JSON date as UTC: ConvertFrom-Json gives a [datetime] in PowerShell 7 and a string in 5.1. $null if unreadable.
function ConvertTo-CutoverUtc {
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) {
        if ($Value.Kind -eq [System.DateTimeKind]::Unspecified) { return [datetime]::SpecifyKind($Value, [System.DateTimeKind]::Utc) }
        return $Value.ToUniversalTime()
    }
    $parsed = [datetime]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal
    if ([datetime]::TryParse([string]$Value, [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) { return $parsed }
    return $null
}

# A task detail (AgentTaskDetailDto) whose land failed: landing.phase or landing.publication Refused,
# landRequest.state NeedsResolution, a landRequest.terminalFailureCode (a land that threw; PersistFailureAsync)
# unless the landing published, or a LandRefused event at or after the window start. The last two cover
# refusals that never move the landing op to Refused. An event time that cannot be read counts.
function Test-CutoverLandFailed {
    param($Detail, [datetime]$WindowStartUtc = [datetime]::MinValue)
    $landing = Get-CutoverProperty -Object $Detail -Name 'landing'
    $request = Get-CutoverProperty -Object $Detail -Name 'landRequest'
    $publication = [string](Get-CutoverProperty -Object $landing -Name 'publication')
    if ([string](Get-CutoverProperty -Object $landing -Name 'phase') -eq 'Refused') { return $true }
    if ($publication -eq 'Refused') { return $true }
    if ([string](Get-CutoverProperty -Object $request -Name 'state') -eq 'NeedsResolution') { return $true }
    # A failure code after publication (landing_interrupted_after_publication) ends in a Landed event: it landed.
    if ([string](Get-CutoverProperty -Object $request -Name 'terminalFailureCode') -and $publication -ne 'Landed' -and $publication -ne 'AlreadyPresent') { return $true }
    foreach ($e in @(Get-CutoverProperty -Object $Detail -Name 'events')) {
        if ($null -eq $e -or [string](Get-CutoverProperty -Object $e -Name 'type') -ne 'LandRefused') { continue }
        $at = ConvertTo-CutoverUtc -Value (Get-CutoverProperty -Object $e -Name 'at')
        if ($null -eq $at -or $at -ge $WindowStartUtc) { return $true }
    }
    return $false
}

function Get-CutoverFailureCount {
    param([object[]]$Items, [object[]]$Details = @(), [datetime]$WindowStartUtc = [datetime]::MinValue)
    $tasks = 0
    $lands = 0
    foreach ($it in @($Items)) {
        if ($null -eq $it) { continue }
        if ([string](Get-CutoverProperty -Object $it -Name 'status') -eq 'Failed') { $tasks++ }
    }
    foreach ($d in @($Details)) {
        if ($null -ne $d -and (Test-CutoverLandFailed -Detail $d -WindowStartUtc $WindowStartUtc)) { $lands++ }
    }
    return [pscustomobject]@{ FailedTasks = $tasks; FailedLands = $lands; Total = ($tasks + $lands) }
}

function Get-CutoverExitCode {
    param([string]$Verdict)
    switch ($Verdict) {
        'OK' { return 0 }
        'WARN' { return 1 }
        'ROLLBACK' { return 2 }
        'ABORT' { return 3 }
        default { return 4 }
    }
}

# D-11: rollback when the fresh median clears both the floor and the ratio, or failures exceed baseline + slack.
function Get-CutoverVerdict {
    param(
        [double]$FreshMedianMs,
        [double]$BaselineMedianMs,
        [int]$FreshFailures,
        [int]$BaselineFailures,
        [int]$SampleErrors = 0,
        [int]$HealthCode = 200,
        [string]$FreshSha = '',
        [string]$BaselineSha = '',
        [double]$MedianFloorMs = 1000,
        [double]$MedianRatio = 3,
        [int]$FailureSlack = 2
    )
    $reasons = New-Object System.Collections.Generic.List[string]
    $notes = New-Object System.Collections.Generic.List[string]
    $ratioLine = $MedianRatio * $BaselineMedianMs
    $aboveFloor = ($FreshMedianMs -gt $MedianFloorMs)
    $aboveRatio = ($FreshMedianMs -gt $ratioLine)
    $delta = $FreshFailures - $BaselineFailures
    $rollback = $false
    if ($aboveFloor -and $aboveRatio) {
        $rollback = $true
        [void]$reasons.Add(('median {0} ms is above the {1} ms floor and above {2}x the baseline median {3} ms ({4} ms)' -f $FreshMedianMs, $MedianFloorMs, $MedianRatio, $BaselineMedianMs, $ratioLine))
    }
    if ($delta -gt $FailureSlack) {
        $rollback = $true
        [void]$reasons.Add(('failures {0} exceed the baseline {1} by {2}, more than the slack of {3}' -f $FreshFailures, $BaselineFailures, $delta, $FailureSlack))
    }
    $warn = $false
    if (-not $rollback) {
        if ($SampleErrors -gt 0) { $warn = $true; [void]$reasons.Add(('{0} pipeline sample(s) failed in the window' -f $SampleErrors)) }
        if ($delta -gt 0) { $warn = $true; [void]$reasons.Add(('failures {0} are above the baseline {1} by {2}, within the slack of {3}' -f $FreshFailures, $BaselineFailures, $delta, $FailureSlack)) }
        if ($HealthCode -ne 200) { $warn = $true; [void]$reasons.Add(('health returned {0}' -f $HealthCode)) }
        if ($FreshSha -and $BaselineSha -and ($FreshSha -ne $BaselineSha)) { $warn = $true; [void]$reasons.Add('version SHA differs from the baseline') }
    }
    if ($aboveRatio -and -not $aboveFloor) { [void]$notes.Add(('median {0} ms is above {1}x the baseline but under the {2} ms floor; not a rollback (D-11)' -f $FreshMedianMs, $MedianRatio, $MedianFloorMs)) }
    if ($aboveFloor -and -not $aboveRatio) { [void]$notes.Add(('median {0} ms is above the {1} ms floor but under {2}x the baseline ({3} ms); not a rollback (D-11)' -f $FreshMedianMs, $MedianFloorMs, $MedianRatio, $ratioLine)) }
    $verdict = 'OK'
    if ($rollback) { $verdict = 'ROLLBACK' } elseif ($warn) { $verdict = 'WARN' }
    return [pscustomobject]@{
        Verdict = $verdict
        ExitCode = (Get-CutoverExitCode -Verdict $verdict)
        Reasons = @($reasons.ToArray())
        Notes = @($notes.ToArray())
        AboveFloor = $aboveFloor
        AboveRatio = $aboveRatio
        RatioLineMs = $ratioLine
        FailureDelta = $delta
    }
}

function Get-CutoverConnectVerdict {
    param($MedianMs, [int]$Failures, [int]$Succeeded, [double]$ConnectAbortMs)
    $reasons = New-Object System.Collections.Generic.List[string]
    if ($Succeeded -le 0 -or $null -eq $MedianMs) {
        [void]$reasons.Add('no TCP connect succeeded; the Tailscale address cannot be measured')
        return [pscustomobject]@{ Verdict = 'INVALID'; ExitCode = 4; Reasons = @($reasons.ToArray()) }
    }
    if ([double]$MedianMs -gt $ConnectAbortMs) {
        [void]$reasons.Add(('Tailscale TCP connect median {0} ms is above the {1} ms abort line' -f $MedianMs, $ConnectAbortMs))
        return [pscustomobject]@{ Verdict = 'ABORT'; ExitCode = 3; Reasons = @($reasons.ToArray()) }
    }
    if ($Failures -gt 0) {
        [void]$reasons.Add(('{0} TCP connect(s) failed' -f $Failures))
        return [pscustomobject]@{ Verdict = 'WARN'; ExitCode = 1; Reasons = @($reasons.ToArray()) }
    }
    return [pscustomobject]@{ Verdict = 'OK'; ExitCode = 0; Reasons = @() }
}

# ---- measurement (seam-aware) -------------------------------------------------------------------

function Invoke-CutoverHttpGet {
    param([string]$Url, [scriptblock]$HttpGet, [int]$TimeoutSec = 60)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if ($HttpGet) {
            $r = & $HttpGet $Url
        } else {
            $resp = Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec $TimeoutSec
            $r = @{ StatusCode = [int]$resp.StatusCode; Body = [string]$resp.Content }
        }
        $sw.Stop()
        $ms = $sw.Elapsed.TotalMilliseconds
        $explicit = Get-CutoverProperty -Object $r -Name 'ElapsedMs'
        if ($null -ne $explicit) { $ms = [double]$explicit }
        return [pscustomobject]@{
            Ok = $true
            StatusCode = [int](Get-CutoverProperty -Object $r -Name 'StatusCode')
            Body = [string](Get-CutoverProperty -Object $r -Name 'Body')
            ElapsedMs = $ms
            Error = ''
        }
    } catch {
        $sw.Stop()
        $code = 0
        try { if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode } } catch { $code = 0 }
        # The message can embed the URL (a [uri] conversion error quotes it whole, a request error quotes the escaped
        # AbsoluteUri), so it is masked here, this URL's raw and escaped userinfo included (CARD-1423 DEF-1).
        return [pscustomobject]@{ Ok = $false; StatusCode = $code; Body = ''; ElapsedMs = $sw.Elapsed.TotalMilliseconds; Error = (Hide-CutoverText -Text ([string]$_.Exception.Message) -KnownUserinfo (Get-CutoverUserinfoForms -Urls @($Url))) }
    }
}

function ConvertFrom-CutoverJsonBody {
    param([string]$Body)
    if ([string]::IsNullOrWhiteSpace($Body)) { return $null }
    try { return ($Body | ConvertFrom-Json) } catch { return $null }
}

function Measure-CutoverPipeline {
    param([string]$Url, [int]$Count, [scriptblock]$HttpGet, [int]$TimeoutSec = 60)
    $samples = @()
    $errors = 0
    $errorText = @()
    for ($i = 0; $i -lt $Count; $i++) {
        $r = Invoke-CutoverHttpGet -Url $Url -HttpGet $HttpGet -TimeoutSec $TimeoutSec
        if ($r.Ok -and $r.StatusCode -eq 200) {
            $samples += [math]::Round([double]$r.ElapsedMs, 2)
        } else {
            $errors++
            if ($errorText.Count -lt 3) { $errorText += (Hide-CutoverText -Text ('status=' + $r.StatusCode + ' ' + $r.Error)) }
        }
    }
    $min = $null
    $max = $null
    if ($samples.Count -gt 0) {
        $m = $samples | Measure-Object -Minimum -Maximum
        $min = [double]$m.Minimum
        $max = [double]$m.Maximum
    }
    return [pscustomobject]@{
        url = (Hide-CutoverText -Text $Url)
        requested = $Count
        succeeded = $samples.Count
        errors = $errors
        minMs = $min
        medianMs = (Get-CutoverMedian -Values $samples)
        maxMs = $max
        samplesMs = @($samples)
        errorText = @($errorText)
    }
}

function Get-CutoverTcpConnectMs {
    param([string]$HostName, [int]$Port, [int]$TimeoutMs = 2000)
    $client = $null
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $iar = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $iar.AsyncWaitHandle.WaitOne($TimeoutMs, $false)) { return -1 }
        $client.EndConnect($iar)
        $sw.Stop()
        if ($client.Connected) { return $sw.Elapsed.TotalMilliseconds }
        return -1
    } catch {
        return -1
    } finally {
        if ($client) { $client.Close() }
    }
}

function Measure-CutoverTcpConnect {
    param([string]$HostName, [int]$Port, [int]$Count, [scriptblock]$TcpConnectMs)
    $samples = @()
    $failures = 0
    for ($i = 0; $i -lt $Count; $i++) {
        $ms = -1
        try {
            if ($TcpConnectMs) { $ms = [double](& $TcpConnectMs $HostName $Port) } else { $ms = [double](Get-CutoverTcpConnectMs -HostName $HostName -Port $Port) }
        } catch { $ms = -1 }
        if ($ms -ge 0) { $samples += [math]::Round($ms, 2) } else { $failures++ }
    }
    $min = $null
    $max = $null
    if ($samples.Count -gt 0) {
        $m = $samples | Measure-Object -Minimum -Maximum
        $min = [double]$m.Minimum
        $max = [double]$m.Maximum
    }
    return [pscustomobject]@{
        host = $HostName
        port = $Port
        requested = $Count
        succeeded = $samples.Count
        failures = $failures
        minMs = $min
        medianMs = (Get-CutoverMedian -Values $samples)
        maxMs = $max
        samplesMs = @($samples)
    }
}

function Resolve-CutoverUrls {
    param([string]$ServerBaseUrl, [string]$ProjectId, [string]$PipelineUrl, [string]$TaskListUrl, [datetime]$WindowStartUtc)
    $base = $ServerBaseUrl.TrimEnd('/')
    $projQuery = ''
    if ($ProjectId) { $projQuery = 'projectId=' + [uri]::EscapeDataString($ProjectId) }
    $pipeline = $PipelineUrl
    if (-not $pipeline) {
        $pipeline = $base + '/api/agent-tasks/pipeline'
        if ($projQuery) { $pipeline += '?' + $projQuery }
    }
    $tasks = $TaskListUrl
    if (-not $tasks) {
        $tasks = $base + '/api/agent-tasks?'
        if ($projQuery) { $tasks += $projQuery + '&' }
        # 'since' is the list's window key (AgentTaskScope.SupportedListQueryKeys); any other key is a 400.
        $tasks += 'since=' + [uri]::EscapeDataString($WindowStartUtc.ToUniversalTime().ToString('o'))
    }
    return [pscustomobject]@{
        TaskDetailPrefix = $base + '/api/agent-tasks/'
        Pipeline = $pipeline
        Tasks = $tasks
        Health = $base + '/health'
        Version = $base + '/api/version'
        Diagnostics = $base + '/api/diagnostics/session-state'
    }
}

# The list envelope { scope, items, excluded }. Anything else is unreadable, never an empty list.
function Read-CutoverTaskList {
    param($Response)
    if (-not $Response.Ok -or $Response.StatusCode -ne 200) {
        return [pscustomobject]@{ Ok = $false; Items = @(); Reason = ('task list returned status ' + $Response.StatusCode + ' ' + $Response.Error).Trim() }
    }
    $parsed = ConvertFrom-CutoverJsonBody -Body $Response.Body
    if ($null -eq $parsed -or -not ($parsed.PSObject.BaseObject -is [System.Management.Automation.PSCustomObject])) {
        return [pscustomobject]@{ Ok = $false; Items = @(); Reason = 'task list body is not a JSON object' }
    }
    $prop = $parsed.PSObject.Properties['items']
    if (-not $prop -or $null -eq $prop.Value) {
        return [pscustomobject]@{ Ok = $false; Items = @(); Reason = 'task list body has no items array' }
    }
    return [pscustomobject]@{ Ok = $true; Items = @($prop.Value); Reason = '' }
}

# Reads AgentTaskDetailDto for every land candidate, bounded by $MaxRequests. Incomplete is never zero.
function Read-CutoverLandDetails {
    param([object[]]$Items, [string]$DetailPrefix, [int]$MaxRequests, [scriptblock]$HttpGet, [int]$TimeoutSec)
    $candidates = @(@($Items) | Where-Object { Test-CutoverLandCandidate -Item $_ })
    $details = New-Object System.Collections.ArrayList
    if ($candidates.Count -gt $MaxRequests) {
        return [pscustomobject]@{ Ok = $false; Candidates = $candidates.Count; Details = @(); Reason = ('{0} tasks with a land attempt exceed -MaxLandDetailRequests {1} (sized for the default 30-minute window; it bites with -WindowMinutes around 8 hours or more: raise the cap or shorten the window); failed lands not counted' -f $candidates.Count, $MaxRequests) }
    }
    foreach ($c in $candidates) {
        $id = [string](Get-CutoverProperty -Object $c -Name 'id')
        $r = Invoke-CutoverHttpGet -Url ($DetailPrefix + [uri]::EscapeDataString($id)) -HttpGet $HttpGet -TimeoutSec $TimeoutSec
        $parsed = $null
        if ($r.Ok -and $r.StatusCode -eq 200) { $parsed = ConvertFrom-CutoverJsonBody -Body $r.Body }
        if ($null -eq $parsed -or $null -eq (Get-CutoverProperty -Object $parsed -Name 'summary')) {
            return [pscustomobject]@{ Ok = $false; Candidates = $candidates.Count; Details = @($details.ToArray()); Reason = ('task detail ' + $id + ' could not be read (status ' + $r.StatusCode + ' ' + $r.Error + ')').Trim() }
        }
        [void]$details.Add($parsed)
    }
    return [pscustomobject]@{ Ok = $true; Candidates = $candidates.Count; Details = @($details.ToArray()); Reason = '' }
}

function Get-CutoverVersionSha {
    param($Parsed)
    foreach ($name in @('sha', 'commit', 'sourceSha', 'version')) {
        $v = Get-CutoverProperty -Object $Parsed -Name $name
        if ($v) { return [string]$v }
    }
    return ''
}

# Collects the shared Baseline/Watch observations: pipeline samples, window failures, pool flags, health, version.
function Get-CutoverObservation {
    param($Urls, [int]$SampleCount, [scriptblock]$HttpGet, [int]$TimeoutSec, [int]$MaxLandDetailRequests = 50, [datetime]$WindowStartUtc = [datetime]::MinValue)
    $pipeline = Measure-CutoverPipeline -Url $Urls.Pipeline -Count $SampleCount -HttpGet $HttpGet -TimeoutSec $TimeoutSec
    $list = Read-CutoverTaskList -Response (Invoke-CutoverHttpGet -Url $Urls.Tasks -HttpGet $HttpGet -TimeoutSec $TimeoutSec)
    $items = @($list.Items)
    $lands = [pscustomobject]@{ Ok = $false; Candidates = 0; Details = @(); Reason = 'task list not read' }
    if ($list.Ok) { $lands = Read-CutoverLandDetails -Items $items -DetailPrefix $Urls.TaskDetailPrefix -MaxRequests $MaxLandDetailRequests -HttpGet $HttpGet -TimeoutSec $TimeoutSec }
    $failures = Get-CutoverFailureCount -Items $items -Details @($lands.Details) -WindowStartUtc $WindowStartUtc
    $unread = @()
    if (-not $list.Ok) { $unread += $list.Reason } elseif (-not $lands.Ok) { $unread += $lands.Reason }
    $health = Invoke-CutoverHttpGet -Url $Urls.Health -HttpGet $HttpGet -TimeoutSec $TimeoutSec
    $version = Invoke-CutoverHttpGet -Url $Urls.Version -HttpGet $HttpGet -TimeoutSec $TimeoutSec
    $versionParsed = ConvertFrom-CutoverJsonBody -Body $version.Body
    $caps = Get-CutoverProperty -Object $versionParsed -Name 'capabilities'
    if ($null -eq $caps) { $caps = @() }
    $diag = Invoke-CutoverHttpGet -Url $Urls.Diagnostics -HttpGet $HttpGet -TimeoutSec $TimeoutSec
    $pool = $null
    if ($diag.Ok -and $diag.StatusCode -eq 200) {
        $parsed = ConvertFrom-CutoverJsonBody -Body $diag.Body
        $poolProp = Get-CutoverProperty -Object $parsed -Name 'pool'
        if ($null -eq $poolProp) { $poolProp = Get-CutoverProperty -Object $parsed -Name 'connectionPool' }
        if ($null -eq $poolProp) { $poolProp = $parsed }
        $pool = Hide-CutoverSecrets -Object $poolProp
    }
    return [pscustomobject]@{
        pipeline = $pipeline
        failures = [ordered]@{
            url = (Hide-CutoverText -Text $Urls.Tasks)
            listOk = $list.Ok
            listed = @($items).Count
            landCandidates = $lands.Candidates
            landDetailsRead = @($lands.Details).Count
            landsOk = $lands.Ok
            complete = ($list.Ok -and $lands.Ok)
            unreadable = @($unread)
            failedTasks = $failures.FailedTasks
            failedLands = $failures.FailedLands
            total = $failures.Total
        }
        health = [ordered]@{ url = (Hide-CutoverText -Text $Urls.Health); statusCode = $health.StatusCode; ok = ($health.Ok -and $health.StatusCode -eq 200) }
        version = [ordered]@{
            url = (Hide-CutoverText -Text $Urls.Version)
            statusCode = $version.StatusCode
            sha = (Get-CutoverVersionSha -Parsed $versionParsed)
            capabilities = $caps
        }
        poolFlags = $pool
        diagnosticsStatusCode = $diag.StatusCode
    }
}

function Write-CutoverJson {
    param([string]$Path, $Object)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $json = Hide-CutoverText -Text ($Object | ConvertTo-Json -Depth 12) -JsonText
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine)
}

# ---- entry point --------------------------------------------------------------------------------

# Same parameters as Invoke-CutoverMonitorCore. Every returned line goes through Hide-CutoverText, and an
# unexpected error is INVALID (exit 4), never the WARN code 1 that an uncaught throw would give.
function Invoke-PostgresCutoverMonitor {
    [CmdletBinding()]
    param(
        [ValidateSet('Plan', 'Baseline', 'PreWindow', 'Watch')]
        [string]$Mode = 'Plan',
        [switch]$Execute,
        [string]$ServerBaseUrl = 'http://localhost:17202',
        [string]$ProjectId = '',
        [string]$PipelineUrl = '',
        [string]$TaskListUrl = '',
        [string]$Server2Host = '',
        [int]$Server2Port = 5432,
        [string]$LanHost = '',
        [int]$SampleCount = 20,
        [int]$ConnectSampleCount = 20,
        [int]$WindowMinutes = 30,
        [string]$WindowStartUtc = '',
        [string]$BaselinePath = '',
        [string]$ResultPath = '',
        [double]$MedianFloorMs = 1000,
        [double]$MedianRatio = 3,
        [int]$FailureSlack = 2,
        [double]$ConnectAbortMs = 20,
        [int]$HttpTimeoutSec = 60,
        [int]$MaxLandDetailRequests = 50,
        [scriptblock]$HttpGet,
        [scriptblock]$TcpConnectMs,
        [scriptblock]$Now
    )
    $ErrorActionPreference = 'Stop'
    # The raw and escaped userinfo of the given URLs is replaced on every surface for this invocation (DEF-1).
    $script:CutoverKnownUserinfo = @(Get-CutoverUserinfoForms -Urls @($ServerBaseUrl, $PipelineUrl, $TaskListUrl))
    try {
        try {
            $o = Invoke-CutoverMonitorCore @PSBoundParameters
        } catch {
            $msg = Hide-CutoverText -Text ([string]$_.Exception.Message)
            $lines = @(('INVALID unexpected error: ' + $msg), ('CUTOVER-MONITOR mode=' + $Mode + ' verdict=INVALID exit=4'))
            return [pscustomobject]@{ ExitCode = 4; Verdict = 'INVALID'; Lines = $lines; Result = $null; ResultPath = '' }
        }
        $masked = @(@($o.Lines) | ForEach-Object { Hide-CutoverText -Text ([string]$_) })
        return [pscustomobject]@{ ExitCode = $o.ExitCode; Verdict = $o.Verdict; Lines = $masked; Result = $o.Result; ResultPath = $o.ResultPath }
    } finally {
        $script:CutoverKnownUserinfo = @()
    }
}

function Invoke-CutoverMonitorCore {
    [CmdletBinding()]
    param(
        [ValidateSet('Plan', 'Baseline', 'PreWindow', 'Watch')]
        [string]$Mode = 'Plan',
        [switch]$Execute,
        [string]$ServerBaseUrl = 'http://localhost:17202',
        [string]$ProjectId = '',
        [string]$PipelineUrl = '',
        [string]$TaskListUrl = '',
        [string]$Server2Host = '',
        [int]$Server2Port = 5432,
        [string]$LanHost = '',
        [int]$SampleCount = 20,
        [int]$ConnectSampleCount = 20,
        [int]$WindowMinutes = 30,
        [string]$WindowStartUtc = '',
        [string]$BaselinePath = '',
        [string]$ResultPath = '',
        [double]$MedianFloorMs = 1000,
        [double]$MedianRatio = 3,
        [int]$FailureSlack = 2,
        [double]$ConnectAbortMs = 20,
        [int]$HttpTimeoutSec = 60,
        [int]$MaxLandDetailRequests = 50,
        [scriptblock]$HttpGet,
        [scriptblock]$TcpConnectMs,
        [scriptblock]$Now
    )
    $lines = New-Object System.Collections.Generic.List[string]
    $nowUtc = [datetime]::UtcNow
    if ($Now) { $nowUtc = ([datetime](& $Now)).ToUniversalTime() }
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $evidenceDir = Join-Path $repoRoot (Join-Path '.antiphon' 'postgres-cutover')
    if (-not $BaselinePath) { $BaselinePath = Join-Path $evidenceDir 'baseline.json' }
    if (-not $ResultPath) { $ResultPath = Join-Path $evidenceDir ($Mode.ToLowerInvariant() + '-' + $nowUtc.ToString('yyyyMMdd-HHmmss') + '.json') }
    $windowStart = $nowUtc.AddMinutes(-1 * $WindowMinutes)
    if ($WindowStartUtc) {
        try {
            $windowStart = [datetime]::Parse($WindowStartUtc, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal)
        } catch {
            [void]$lines.Add('INVALID -WindowStartUtc is not a date: ' + $WindowStartUtc)
            [void]$lines.Add('CUTOVER-MONITOR mode=' + $Mode + ' verdict=INVALID exit=4')
            return [pscustomobject]@{ ExitCode = 4; Verdict = 'INVALID'; Lines = @($lines.ToArray()); Result = $null; ResultPath = $ResultPath }
        }
    }
    $urls = Resolve-CutoverUrls -ServerBaseUrl $ServerBaseUrl -ProjectId $ProjectId -PipelineUrl $PipelineUrl -TaskListUrl $TaskListUrl -WindowStartUtc $windowStart
    $thresholds = [ordered]@{ medianFloorMs = $MedianFloorMs; medianRatio = $MedianRatio; failureSlack = $FailureSlack; connectAbortMs = $ConnectAbortMs; sampleCount = $SampleCount; connectSampleCount = $ConnectSampleCount; windowMinutes = $WindowMinutes }

    if (-not $Execute -or $Mode -eq 'Plan') {
        $shown = $Mode
        if ($Mode -eq 'Plan') { $shown = 'Plan (describing Baseline, PreWindow and Watch)' }
        [void]$lines.Add('PLAN mode=' + $shown + ' dry run; pass -Execute with -Mode Baseline, PreWindow or Watch to measure')
        if ($Mode -eq 'Plan' -or $Mode -eq 'Baseline' -or $Mode -eq 'Watch') {
            [void]$lines.Add('PLAN would GET ' + $urls.Pipeline + ' x' + $SampleCount + ' and take the median')
            [void]$lines.Add('PLAN would GET ' + $urls.Tasks + ' and count Failed tasks; an unreadable list is INVALID')
            [void]$lines.Add('PLAN would GET ' + $urls.TaskDetailPrefix + '{id} for each listed task with a land attempt or a Succeeded worktree task (at most ' + $MaxLandDetailRequests + ') and count failed lands')
            [void]$lines.Add('PLAN would GET ' + $urls.Health + ', ' + $urls.Version + ' and ' + $urls.Diagnostics + ' (pool flags, credentials dropped)')
            if (-not $ProjectId) { [void]$lines.Add('NOTE no -ProjectId: the pipeline sample would be fleet-wide, not the Antiphon project (D-11 wants the project scope)') }
        }
        if ($Mode -eq 'Plan' -or $Mode -eq 'PreWindow') {
            $h = $Server2Host
            if (-not $h) { $h = '<-Server2Host required>' }
            [void]$lines.Add('PLAN would TCP-connect to ' + $h + ':' + $Server2Port + ' x' + $ConnectSampleCount + ' (Tailscale, decides) and abort above ' + $ConnectAbortMs + ' ms')
            if ($LanHost) { [void]$lines.Add('PLAN would TCP-connect to ' + $LanHost + ':' + $Server2Port + ' x' + $ConnectSampleCount + ' (LAN, evidence only)') }
        }
        if ($Mode -eq 'Plan' -or $Mode -eq 'Baseline') { [void]$lines.Add('PLAN would write the baseline to ' + $BaselinePath) }
        if ($Mode -eq 'Plan' -or $Mode -eq 'Watch') {
            $present = 'missing'
            if (Test-Path -LiteralPath $BaselinePath) { $present = 'present' }
            [void]$lines.Add('PLAN would compare with the baseline at ' + $BaselinePath + ' (' + $present + ')')
            [void]$lines.Add('PLAN rollback when median > ' + $MedianFloorMs + ' ms and > ' + $MedianRatio + 'x baseline median, or failures > baseline + ' + $FailureSlack)
        }
        [void]$lines.Add('PLAN would write the result to ' + $ResultPath)
        [void]$lines.Add('PLAN no request was made and no file was written')
        [void]$lines.Add('CUTOVER-MONITOR mode=' + $Mode + ' verdict=OK exit=0 dry=true')
        return [pscustomobject]@{ ExitCode = 0; Verdict = 'OK'; Lines = @($lines.ToArray()); Result = $null; ResultPath = $ResultPath }
    }

    $result = [ordered]@{
        # schema 2: failures read with since= and lands from task detail; Watch refuses an older baseline.
        schema = 2
        mode = $Mode
        recordedAtUtc = $nowUtc.ToString('o')
        server = (Hide-CutoverText -Text $ServerBaseUrl)
        projectId = $ProjectId
        window = [ordered]@{ startUtc = $windowStart.ToString('o'); endUtc = $nowUtc.ToString('o'); minutes = $WindowMinutes }
        thresholds = $thresholds
    }

    if ($Mode -eq 'PreWindow') {
        if (-not $Server2Host) {
            [void]$lines.Add('INVALID -Server2Host (the Tailscale address) is required for PreWindow -Execute')
            [void]$lines.Add('CUTOVER-MONITOR mode=PreWindow verdict=INVALID exit=4')
            return [pscustomobject]@{ ExitCode = 4; Verdict = 'INVALID'; Lines = @($lines.ToArray()); Result = $null; ResultPath = $ResultPath }
        }
        $tail = Measure-CutoverTcpConnect -HostName $Server2Host -Port $Server2Port -Count $ConnectSampleCount -TcpConnectMs $TcpConnectMs
        $lan = $null
        if ($LanHost) { $lan = Measure-CutoverTcpConnect -HostName $LanHost -Port $Server2Port -Count $ConnectSampleCount -TcpConnectMs $TcpConnectMs }
        $v = Get-CutoverConnectVerdict -MedianMs $tail.medianMs -Failures $tail.failures -Succeeded $tail.succeeded -ConnectAbortMs $ConnectAbortMs
        $result.tailscale = $tail
        $result.lan = $lan
        $result.decision = [ordered]@{ reasons = @($v.Reasons) }
        $result.verdict = $v.Verdict
        $result.exitCode = $v.ExitCode
        Write-CutoverJson -Path $ResultPath -Object $result
        [void]$lines.Add('MEASURED tailscale ' + $Server2Host + ':' + $Server2Port + ' connect medianMs=' + $tail.medianMs + ' minMs=' + $tail.minMs + ' maxMs=' + $tail.maxMs + ' succeeded=' + $tail.succeeded + ' failures=' + $tail.failures)
        if ($lan) { [void]$lines.Add('MEASURED lan ' + $LanHost + ':' + $Server2Port + ' connect medianMs=' + $lan.medianMs + ' (evidence only)') }
        foreach ($r in $v.Reasons) { [void]$lines.Add('REASON ' + $r) }
        if ($v.Verdict -eq 'ABORT') { [void]$lines.Add('ABORT') }
        [void]$lines.Add('CUTOVER-MONITOR mode=PreWindow verdict=' + $v.Verdict + ' exit=' + $v.ExitCode + ' connectMedianMs=' + $tail.medianMs + ' abortMs=' + $ConnectAbortMs + ' result=' + $ResultPath)
        return [pscustomobject]@{ ExitCode = $v.ExitCode; Verdict = $v.Verdict; Lines = @($lines.ToArray()); Result = $result; ResultPath = $ResultPath }
    }

    $baseline = $null
    if ($Mode -eq 'Watch') {
        if (-not (Test-Path -LiteralPath $BaselinePath)) {
            [void]$lines.Add('INVALID baseline not found at ' + $BaselinePath + '; run -Mode Baseline -Execute before phase 1')
            [void]$lines.Add('CUTOVER-MONITOR mode=Watch verdict=INVALID exit=4')
            return [pscustomobject]@{ ExitCode = 4; Verdict = 'INVALID'; Lines = @($lines.ToArray()); Result = $null; ResultPath = $ResultPath }
        }
        $baseline = ConvertFrom-CutoverJsonBody -Body (Get-Content -LiteralPath $BaselinePath -Raw)
        $baselineMedian = Get-CutoverProperty -Object (Get-CutoverProperty -Object $baseline -Name 'pipeline') -Name 'medianMs'
        $bad = ''
        $parsedMedian = [double]::NaN
        if ($null -ne $baselineMedian) { [void][double]::TryParse([string]$baselineMedian, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsedMedian) }
        $schema = Get-CutoverProperty -Object $baseline -Name 'schema'
        $baselineComplete = Get-CutoverProperty -Object (Get-CutoverProperty -Object $baseline -Name 'failures') -Name 'complete'
        if ($null -eq $baseline -or $null -eq $baselineMedian) { $bad = 'has no pipeline.medianMs' }
        elseif ([double]::IsNaN($parsedMedian) -or [double]::IsInfinity($parsedMedian) -or $parsedMedian -lt 0) { $bad = ('has a pipeline.medianMs that is not a finite number (' + $baselineMedian + '); the latency trigger cannot be evaluated') }
        elseif ($null -eq $schema -or [int]$schema -lt 2) { $bad = 'is an older schema whose failure count was not read from the server; re-run -Mode Baseline -Execute' }
        elseif ($baselineComplete -ne $true) { $bad = 'has an incomplete failure count; re-run -Mode Baseline -Execute' }
        if ($bad) {
            [void]$lines.Add('INVALID baseline at ' + $BaselinePath + ' ' + $bad)
            [void]$lines.Add('CUTOVER-MONITOR mode=Watch verdict=INVALID exit=4')
            return [pscustomobject]@{ ExitCode = 4; Verdict = 'INVALID'; Lines = @($lines.ToArray()); Result = $null; ResultPath = $ResultPath }
        }
        $baselineMedian = $parsedMedian
    }
    if (-not $ProjectId -and -not $PipelineUrl) { [void]$lines.Add('NOTE no -ProjectId: the pipeline sample is fleet-wide, not the Antiphon project (D-11 wants the project scope)') }

    $obs = Get-CutoverObservation -Urls $urls -SampleCount $SampleCount -HttpGet $HttpGet -TimeoutSec $HttpTimeoutSec -MaxLandDetailRequests $MaxLandDetailRequests -WindowStartUtc $windowStart
    $result.pipeline = $obs.pipeline
    $result.failures = $obs.failures
    $result.health = $obs.health
    $result.version = $obs.version
    $result.poolFlags = $obs.poolFlags
    $result.diagnosticsStatusCode = $obs.diagnosticsStatusCode
    [void]$lines.Add('MEASURED pipeline ' + $urls.Pipeline + ' medianMs=' + $obs.pipeline.medianMs + ' minMs=' + $obs.pipeline.minMs + ' maxMs=' + $obs.pipeline.maxMs + ' succeeded=' + $obs.pipeline.succeeded + ' errors=' + $obs.pipeline.errors)
    [void]$lines.Add('MEASURED failures since ' + $windowStart.ToString('o') + ' failedTasks=' + $obs.failures.failedTasks + ' failedLands=' + $obs.failures.failedLands + ' total=' + $obs.failures.total + ' listed=' + $obs.failures.listed + ' listOk=' + $obs.failures.listOk + ' landCandidates=' + $obs.failures.landCandidates + ' landsOk=' + $obs.failures.landsOk)
    foreach ($u in $obs.failures.unreadable) { [void]$lines.Add('UNREADABLE ' + $u) }
    [void]$lines.Add('MEASURED health=' + $obs.health.statusCode + ' versionSha=' + $obs.version.sha + ' diagnostics=' + $obs.diagnosticsStatusCode)
    foreach ($e in $obs.pipeline.errorText) { [void]$lines.Add('SAMPLE-ERROR ' + $e) }

    if ($Mode -eq 'Baseline') {
        $verdict = 'OK'
        $reasons = @()
        if ($obs.pipeline.succeeded -le 0) {
            $verdict = 'INVALID'
            $reasons += 'no pipeline sample succeeded; the baseline was not written'
        } else {
            if ($obs.pipeline.errors -gt 0) { $verdict = 'WARN'; $reasons += ($obs.pipeline.errors.ToString() + ' pipeline sample(s) failed') }
            if (-not $obs.health.ok) { $verdict = 'WARN'; $reasons += ('health returned ' + $obs.health.statusCode) }
            if (-not $obs.version.sha) { $verdict = 'WARN'; $reasons += 'version SHA could not be read' }
            if (-not $obs.failures.complete) {
                $verdict = 'INVALID'
                $reasons += 'the failure count could not be read (' + (@($obs.failures.unreadable) -join '; ') + '); the baseline was not written'
            }
        }
        $exit = Get-CutoverExitCode -Verdict $verdict
        $result.decision = [ordered]@{ reasons = @($reasons) }
        $result.verdict = $verdict
        $result.exitCode = $exit
        if ($verdict -ne 'INVALID') {
            Write-CutoverJson -Path $BaselinePath -Object $result
            [void]$lines.Add('BASELINE written to ' + $BaselinePath)
        }
        Write-CutoverJson -Path $ResultPath -Object $result
        foreach ($r in $reasons) { [void]$lines.Add('REASON ' + $r) }
        [void]$lines.Add('CUTOVER-MONITOR mode=Baseline verdict=' + $verdict + ' exit=' + $exit + ' medianMs=' + $obs.pipeline.medianMs + ' failures=' + $obs.failures.total + ' result=' + $ResultPath)
        return [pscustomobject]@{ ExitCode = $exit; Verdict = $verdict; Lines = @($lines.ToArray()); Result = $result; ResultPath = $ResultPath }
    }

    # Watch
    $baselineFailures = 0
    $bf = Get-CutoverProperty -Object (Get-CutoverProperty -Object $baseline -Name 'failures') -Name 'total'
    if ($null -ne $bf) { $baselineFailures = [int]$bf }
    $baselineSha = [string](Get-CutoverProperty -Object (Get-CutoverProperty -Object $baseline -Name 'version') -Name 'sha')
    $freshMedian = $obs.pipeline.medianMs
    if ($null -eq $freshMedian -or [double]::IsNaN([double]$freshMedian) -or [double]::IsInfinity([double]$freshMedian)) {
        $result.decision = [ordered]@{ reasons = @('no pipeline sample succeeded; nothing to compare') }
        $result.verdict = 'INVALID'
        $result.exitCode = 4
        Write-CutoverJson -Path $ResultPath -Object $result
        [void]$lines.Add('INVALID no pipeline sample succeeded; nothing to compare')
        [void]$lines.Add('CUTOVER-MONITOR mode=Watch verdict=INVALID exit=4 result=' + $ResultPath)
        return [pscustomobject]@{ ExitCode = 4; Verdict = 'INVALID'; Lines = @($lines.ToArray()); Result = $result; ResultPath = $ResultPath }
    }
    $v = Get-CutoverVerdict -FreshMedianMs ([double]$freshMedian) -BaselineMedianMs ([double]$baselineMedian) -FreshFailures ([int]$obs.failures.total) -BaselineFailures $baselineFailures -SampleErrors ([int]$obs.pipeline.errors) -HealthCode ([int]$obs.health.statusCode) -FreshSha ([string]$obs.version.sha) -BaselineSha $baselineSha -MedianFloorMs $MedianFloorMs -MedianRatio $MedianRatio -FailureSlack $FailureSlack
    $verdict = $v.Verdict
    $exit = $v.ExitCode
    $reasons = @($v.Reasons)
    # D1: an unread failure count is never a silent zero. A rollback that already fired stands.
    if (-not $obs.failures.complete) {
        $reasons += ('the failure count could not be read (' + (@($obs.failures.unreadable) -join '; ') + '); the failure trigger was not evaluated')
        if ($verdict -ne 'ROLLBACK') { $verdict = 'INVALID'; $exit = 4 }
    }
    $result.baseline = [ordered]@{ path = $BaselinePath; medianMs = [double]$baselineMedian; failures = $baselineFailures; sha = $baselineSha }
    $result.decision = [ordered]@{
        aboveFloor = $v.AboveFloor
        aboveRatio = $v.AboveRatio
        ratioLineMs = $v.RatioLineMs
        failureDelta = $v.FailureDelta
        reasons = @($reasons)
        notes = @($v.Notes)
    }
    $result.verdict = $verdict
    $result.exitCode = $exit
    Write-CutoverJson -Path $ResultPath -Object $result
    foreach ($n in $v.Notes) { [void]$lines.Add('NOTE ' + $n) }
    foreach ($r in $reasons) { [void]$lines.Add('REASON ' + $r) }
    if ($verdict -eq 'ROLLBACK') { [void]$lines.Add('ROLLBACK') }
    [void]$lines.Add('CUTOVER-MONITOR mode=Watch verdict=' + $verdict + ' exit=' + $exit + ' medianMs=' + $freshMedian + ' baselineMedianMs=' + $baselineMedian + ' failures=' + $obs.failures.total + ' baselineFailures=' + $baselineFailures + ' result=' + $ResultPath)
    return [pscustomobject]@{ ExitCode = $exit; Verdict = $verdict; Lines = @($lines.ToArray()); Result = $result; ResultPath = $ResultPath }
}

if ($MyInvocation.InvocationName -ne '.') {
    $outcome = Invoke-PostgresCutoverMonitor @PSBoundParameters
    foreach ($line in $outcome.Lines) { Write-Output $line }
    exit $outcome.ExitCode
}
