# Hand a piece of work to another agent. Invoked by the antiphon-delegate skill from inside a
# running agent session; identity comes from the ANTIPHON_* environment, never from arguments.
#
# ASCII-only on purpose: daemon/agent scripts must parse under Windows PowerShell 5.1, which reads
# a no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
[CmdletBinding(DefaultParameterSetName = 'Create')]
param(
    [Parameter(ParameterSetName = 'Create', Position = 0)]
    [ValidateSet('Plan', 'Code', 'Review', 'Debug', 'Coverage', 'Docs', 'Commit', 'Test', 'Deploy', 'Merge', 'Custom')]
    [string]$Role = 'Custom',

    [Parameter(ParameterSetName = 'Create')]
    [string]$Goal,

    [Parameter(ParameterSetName = 'Create')]
    [string]$Title,

    # Make it a sub-orchestrator: it decomposes its chunk and runs its own agents.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$Orchestrator,

    # Override the role's tier. Say why in -Goal.
    [Parameter(ParameterSetName = 'Create')]
    [ValidateSet('Frontier', 'High', 'Medium', 'Low')]
    [string]$Level,

    # Which agent program runs it. Omitted means ClaudeCode, exactly as before. Grok is opt-in and
    # WORKERS ONLY (CARD-0084): its delegate mileage is zero, and live-typed follow-up messages to
    # it arrive with their line breaks joined - briefs and refinements travel by file and are safe.
    # Codex is opt-in and WORKERS ONLY too (CARD-0099): same zero mileage, and while its composer
    # keeps typed line breaks intact, a body over ~one write costs an extra Enter - so its briefs and
    # refinements also travel by file. An orchestrator stays ClaudeCode for both.
    [Parameter(ParameterSetName = 'Create')]
    [ValidateSet('ClaudeCode', 'Grok', 'Codex')]
    [string]$Kind,

    # Run somewhere else - another repo, another checkout. Defaults to the caller's directory.
    [Parameter(ParameterSetName = 'Create')]
    [string]$Dir,

    # Follow-up: run this on the SAME agent that ran the given task (short id from its report),
    # keeping that agent's context. Waits if the agent is still busy; inherits its directory+tier.
    [Parameter(ParameterSetName = 'Create')]
    [string]$OnAgent,

    # Isolate in a fresh git worktree, merged back when it finishes. Workers default to running
    # right in the directory; a sub-orchestrator gets a worktree by default already.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$Worktree,

    # Force the shared directory - opts a sub-orchestrator OUT of its default worktree. The server
    # will warn: its delegates and its caller can overwrite each other.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$Shared,

    [Parameter(ParameterSetName = 'Create')]
    [switch]$ReadOnly,

    # Do not arm the PreToolUse deny hook in a sub-orchestrator's worktree (it blocks direct
    # Edit/Write with "delegate this instead"). Use when the orchestrator must write a plan file.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$AllowDirectEdits,

    # What this task owns: a comma-separated list of AREA NAMES from the repo's antiphon.areas.json
    # and/or path globs, each element compared independently. Two Shared tasks whose scopes
    # intersect are serialised - the waiting one gets a visible 'Held' event naming what it waits
    # for. A ReadOnly task never waits and never makes anyone wait. Run -ListAreas to see the names.
    [Parameter(ParameterSetName = 'Create')]
    [string]$Scope,

    # Roughly how long you expect this to take, in minutes. It schedules the first check-in on the
    # delegate - a short note back saying whether it is working, has produced something, looks
    # stuck, or has already settled - and nothing else. It is a HINT, NEVER A DEADLINE: nothing
    # fails, escalates or cancels a task for running past it, so declare an honest number rather
    # than padding it.
    [Parameter(ParameterSetName = 'Create')]
    [ValidateRange(1, 1440)]
    [int]$ExpectAbout,

    # Bypass the subscription-quota launch gate (CARD-0136). Without this, a fresh
    # reading past the remaining-%-vs-time-to-reset threshold is refused with 409
    # subscription_quota_low. Two ways out: pick another -Kind / agent, or re-run
    # the same command with this switch to launch anyway.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$IgnoreSubscriptionQuota,

    # Overlay env vars on this task's process launch (CARD-0106). ANTIPHON_* names are refused
    # 422. A non-empty overlay excludes the task from warm-pool reuse (reuse launches no
    # process, so the overlay could never apply). Combined with -OnAgent is refused 422.
    # Usage: -EnvOverride @{ ANTHROPIC_BASE_URL='http://proxy:8080'; ANTHROPIC_API_KEY='{{key:proxy-key}}' }
    [Parameter(ParameterSetName = 'Create')]
    [hashtable]$EnvOverride,

    # Answer a blocked delegate's question: -Reply <taskId> "your answer"
    [Parameter(ParameterSetName = 'Reply', Mandatory = $true)]
    [string]$Reply,

    # Steer a RUNNING delegate without cancelling it: -Refine <taskId> "your message". Delivered
    # between its turns; a still-queued task gets the message folded into its brief instead. Use
    # -Reply, not this, for a task that is Blocked on a question.
    [Parameter(ParameterSetName = 'Refine', Mandatory = $true)]
    [string]$Refine,

    [Parameter(ParameterSetName = 'Reply', Position = 0)]
    [Parameter(ParameterSetName = 'Refine', Position = 0)]
    [string]$Message,

    # Look up a task you already created.
    [Parameter(ParameterSetName = 'Status', Mandatory = $true)]
    [string]$Status,

    # Print the repo's named areas - what -Scope may name, and the paths each one owns. Reads
    # antiphon.areas.json at the repo root of -Dir (or the current directory).
    [Parameter(ParameterSetName = 'ListAreas', Mandatory = $true)]
    [switch]$ListAreas,

    [Parameter(ParameterSetName = 'ListAreas')]
    [string]$AreasDir
)

$ErrorActionPreference = 'Stop'

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}

function Invoke-Antiphon {
    param([string]$Method, [string]$Path, $Body)
    $uri = "$api$Path"
    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 6 -Compress
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -Body $json -ContentType 'application/json'
        }
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }
    catch {
        # Surface the server's own message - it explains WHY (worker cannot delegate, directory
        # outside the allowed roots, cost ceiling reached) and that is the actionable part.
        $detail = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $_.Exception.Message }
        Write-Error "Antiphon $Method $Path failed: $detail"
        exit 1
    }
}

switch ($PSCmdlet.ParameterSetName) {
    'ListAreas' {
        $dir = if ($AreasDir) { $AreasDir } else { (Get-Location).Path }
        $map = Invoke-Antiphon -Method GET -Path "/api/agent-tasks/areas?directory=$([uri]::EscapeDataString($dir))"
        if (-not $map.areas -or $map.areas.Count -eq 0) {
            Write-Output "No areas declared for $($map.repoPath) - every -Scope token is read as a path or a label."
            return
        }
        Write-Output "Areas in $($map.sourcePath):"
        foreach ($area in $map.areas) {
            $weight = if ($area.weight -eq 'allow') { '  [allow]' } else { '' }
            Write-Output ("  {0}{1}" -f $area.name, $weight)
            foreach ($path in $area.paths) { Write-Output "      $path" }
        }
        Write-Output ''
        Write-Output 'An area is added when two tasks collide in it, named for the work, not the folder.'
        return
    }

    'Status' {
        $task = Invoke-Antiphon -Method GET -Path "/api/agent-tasks/$Status"
        $s = $task.summary
        Write-Output ("{0}  {1}  {2}/{3}  {4}" -f $s.status, $s.title, $s.kind, $s.role, $s.modelLevel)
        if ($task.result) { Write-Output ''; Write-Output $task.result }
        elseif ($task.failureReason) { Write-Output ''; Write-Output "failed: $($task.failureReason)" }
        return
    }

    'Reply' {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            Write-Error 'Pass the answer as the first argument: delegate.ps1 -Reply <taskId> "your answer"'
            exit 1
        }
        Invoke-Antiphon -Method POST -Path "/api/agent-tasks/$Reply/reply" -Body @{ message = $Message } | Out-Null
        Write-Output "Answered task $Reply. It will resume and report back."
        return
    }

    'Refine' {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            Write-Error 'Pass the message as the first argument: delegate.ps1 -Refine <taskId> "your message"'
            exit 1
        }
        $summary = Invoke-Antiphon -Method POST -Path "/api/agent-tasks/$Refine/refine" -Body @{ message = $Message }
        if ($summary.status -eq 'Queued') {
            Write-Output "Refined task $Refine before dispatch - folded into its brief."
        }
        else {
            Write-Output "Refined task $Refine. The message will land between its turns; its report will note it."
        }
        return
    }

    'Create' {
        if ([string]::IsNullOrWhiteSpace($Goal)) {
            Write-Error 'A -Goal is required. Write it as an outcome, not a procedure.'
            exit 1
        }

        $body = @{
            goal = $Goal
            kind = if ($Orchestrator) { 'Orchestrator' } else { 'Worker' }
            role = if ($Orchestrator -and $Role -eq 'Custom') { 'Plan' } else { $Role }
        }
        # Workspace is sent only when chosen - omitted, the server decides: workers run shared,
        # a sub-orchestrator gets its own worktree unless it already has its own -Dir.
        if ($Worktree) { $body['workspace'] = 'Worktree' }
        elseif ($ReadOnly) { $body['workspace'] = 'ReadOnly' }
        elseif ($Shared) { $body['workspace'] = 'Shared' }
        if ($AllowDirectEdits) { $body['denyDirectEdits'] = $false }
        if ($OnAgent) { $body['followUpOnTask'] = $OnAgent }
        if ($Title) { $body['title'] = $Title }
        if ($Level) { $body['modelLevel'] = $Level }
        # Sent only when chosen - an omitted -Kind leaves the decision to the role policy, which
        # ships unset and therefore resolves to ClaudeCode.
        if ($Kind) { $body['agentKind'] = $Kind }
        if ($Dir) { $body['workingDirectory'] = $Dir }
        if ($Scope) { $body['scope'] = $Scope }
        # Omitted (0 - an unbound [int] is 0, not $null) leaves the server's default expectation.
        if ($ExpectAbout -gt 0) { $body['expectedMinutes'] = $ExpectAbout }
        if ($IgnoreSubscriptionQuota) { $body['ignoreSubscriptionQuota'] = $true }
        if ($EnvOverride -and $EnvOverride.Count -gt 0) { $body['launchEnvOverride'] = $EnvOverride }

        $created = Invoke-Antiphon -Method POST -Path '/api/agent-tasks' -Body $body
        # The RESOLVED kind is echoed, not the requested one - a role policy promoted to Grok in
        # config is exactly the case where the caller asked for nothing and should still see it.
        $kindNote = ''
        if ($created.agentKind -and $created.agentKind -ne 'ClaudeCode') { $kindNote = " [$($created.agentKind)]" }
        # Where the report goes is a fact about THIS task, not a constant. A token-less caller has
        # no parent task and no parent session, so nothing is routed anywhere and the result only
        # lands on the board - saying "its report will arrive in your session" there is a lie the
        # caller can only discover by waiting forever (CARD-0020 S1).
        $routing = if ($created.noReplyRouting) {
            " - NO REPLY WILL BE ROUTED: read the result on the board (antiphon task get {0})" -f $created.shortId
        } else {
            " - its report will arrive in your session"
        }
        Write-Output ("queued task {0} ({1} {2} on {3}{4}){5}" -f `
                $created.shortId, $body.kind.ToLower(), $body.role.ToLower(), $created.modelLevel, $kindNote, $routing)
        # A warning at creation is the caller's one chance to reconsider before the collision.
        if ($created.warning) { Write-Output ("WARNING: {0}" -f $created.warning) }
        # And so is this: what the declared -Scope costs, right now, against what is already
        # running. 'serialise' means this task waits; 'warn' means it starts and somebody owes a
        # rebase. Said here because the dispatcher's verdict is 5 seconds and one queue away, and
        # by then nobody is reading.
        foreach ($overlap in @($created.scopeOverlaps)) {
            if (-not $overlap) { continue }
            $what = if ($overlap.areas) { $overlap.areas } else { 'shared checkout' }
            if ($overlap.policy -eq 'serialise') {
                Write-Output ("  will wait behind {0} ({1}, {2})" -f $overlap.shortId, $what, $overlap.workspace)
            }
            else {
                $branch = if ($overlap.branch) { $overlap.branch } else { 'the shared checkout' }
                Write-Output ("  overlaps {0} ({1}, {2}) - merge order matters; expect a rebase against {3}" -f `
                    $overlap.shortId, $what, $overlap.workspace, $branch)
            }
        }
        return
    }
}
