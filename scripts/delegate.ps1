# Hand a piece of work to another agent. Invoked by the antiphon-delegate skill from inside a
# running agent session; identity comes from the ANTIPHON_* environment, never from arguments.
#
# BIND THE CARD (CARD-0040). Pass -Card CARD-nnnn, or lead -Title with the identifier, and the card
# moves itself: In Progress when this task dispatches, Review when it settles Succeeded with nothing
# else open, within 60s, recorded on the card's history as `card-transitions`. Creation echoes
# "- bound to CARD-nnnn"; an explicit -Card that names no card is refused 422 rather than ignored.
# A move a human makes afterwards is never overridden, and Review -> Done is never automated.
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

    # 2-5 word label (max 80) for check headers and the board chip; not a second Goal.
    [Parameter(ParameterSetName = 'Create')]
    [string]$Title,

    # Make it a sub-orchestrator: it decomposes its chunk and runs its own agents.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$Orchestrator,

    # Override the role's tier. Say why in -Goal.
    [Parameter(ParameterSetName = 'Create')]
    [Parameter(ParameterSetName = 'Reroute', Mandatory = $true)]
    [ValidateSet('Frontier', 'High', 'Medium', 'Low')]
    [string]$Level,

    # Which agent program runs it. Omitted means ClaudeCode, exactly as before. Grok is opt-in and
    # WORKERS ONLY (CARD-0084): its delegate mileage is zero, and live-typed follow-up messages to
    # it arrive with their line breaks joined - briefs and refinements travel by file and are safe.
    # Codex is opt-in and WORKERS ONLY too (CARD-0099): same zero mileage, and while its composer
    # keeps typed line breaks intact, a body over ~one write costs an extra Enter - so its briefs and
    # refinements also travel by file. An orchestrator stays ClaudeCode for both.
    [Parameter(ParameterSetName = 'Create')]
    [Parameter(ParameterSetName = 'Reroute', Mandatory = $true)]
    [ValidateSet('ClaudeCode', 'Grok', 'Codex')]
    [string]$Kind,

    # Which CARD this work is against (CARD-0040). Accepts CARD-0040, card-40, #40, 40, or the
    # card's guid. Omitted, the server derives it: your own task's card, else the first CARD-nnnn
    # in -Title. Binding it is what makes the card move itself - Backlog -> In Progress when this
    # task dispatches, -> Review when it settles - so a card you never touch stays honest. An
    # explicit value that names no card is refused 422 rather than silently ignored.
    [Parameter(ParameterSetName = 'Create')]
    [string]$Card,

    # Run somewhere else - another repo, another checkout. Defaults to the caller's directory.
    [Parameter(ParameterSetName = 'Create')]
    [string]$Dir,

    # Follow-up: run this on the SAME agent that ran the given task (short id from its report),
    # keeping that agent's context. Waits if the agent is still busy; inherits its directory+tier.
    [Parameter(ParameterSetName = 'Create')]
    [string]$OnAgent,

    # Run this task on an existing STANDING agent by name, slug, or guid (CARD-0291). The task
    # queues while that agent is busy; you get the normal [task ... done] note when it settles.
    # Ambiguous or unknown references are refused, as are pool delegates (that is -OnAgent's job).
    [Parameter(ParameterSetName = 'Create')]
    [string]$Agent,

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

    # Bypass the CARD-0324 create-time 409 provider_sign_in_required. Queues a Grok
    # task even when GROK_HOME has no usable session. The dispatcher still fails it
    # if the store is empty at launch. For the operator about to run `grok login`.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$AllowUnauthenticatedProvider,

    # Bypass the CARD-0309 create-time 409 model_disabled. Queues the task; the dispatcher
    # still skips it until the hold clears. This is NOT a launch-anyway switch - Start
    # never honours it.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$IgnoreModelDisabled,

    # Record this dispatch's Role/Card/Kind/Level as a HUMAN, REQUIRED routing pin (CARD-0305), so
    # the next create against this card+stage runs the same way without being told again. Refused
    # without a card: a stage-wide pin changes routing for EVERY card and must be written
    # deliberately with routing-pin.ps1, never as a side effect of one dispatch.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$Pin,

    # Ignore the routing pin for THIS create (CARD-0305). Without it, a request that disagrees with
    # a Required pin is refused 409 routing_pin_conflict. The pin is left standing - this is
    # one-shot, not a clear. Distinct from -IgnoreModelDisabled: this one is about which model
    # SHOULD run the work, that one about whether the model MAY run at all.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$IgnoreRoutingPin,

    # Caller-declared hardness (CARD-0090). Walks the Hard/Medium/Easy chain instead of resolving
    # kind/level from the role policy. Combined with -Kind or -Level is refused: an explicit pair
    # is a single candidate the caller chose and is never silently rerouted.
    [Parameter(ParameterSetName = 'Create')]
    [ValidateSet('Hard', 'Medium', 'Easy')]
    [string]$Complexity,

    # When the chain is exhausted, 409 routing_exhausted instead of a Blocked task (CARD-0090).
    [Parameter(ParameterSetName = 'Create')]
    [switch]$RefuseIfExhausted,

    # Overlay env vars on this task's process launch (CARD-0106). ANTIPHON_* names are refused
    # 422. A non-empty overlay excludes the task from warm-pool reuse (reuse launches no
    # process, so the overlay could never apply). Combined with -OnAgent is refused 422.
    # Usage: -EnvOverride @{ ANTHROPIC_BASE_URL='http://proxy:8080'; ANTHROPIC_API_KEY='{{key:proxy-key}}' }
    [Parameter(ParameterSetName = 'Create')]
    [hashtable]$EnvOverride,

    # Forward the caller's live X_LLM_PROJECT/X_LLM_KEY routing facts unless an explicit
    # -EnvOverride already names them. Use this only when the child must not inherit the shell's
    # current key-proxy project; server-side inheritance from stored agent layers still applies.
    [Parameter(ParameterSetName = 'Create')]
    [switch]$NoInheritEnv,

    # CARD-0294 S1: the caller's own words for what this task is already authorised to do.
    # Injected into the child's brief so it does not stop to ask for a go-ahead this already
    # grants, and replayed by -Continue. Long text goes through -AuthorityFile.
    [Parameter(ParameterSetName = 'Create')]
    [string]$Authority,

    [Parameter(ParameterSetName = 'Create')]
    [string]$AuthorityFile,

    # Answer a blocked delegate's question: -Reply <taskId> "your answer"
    [Parameter(ParameterSetName = 'Reply', Mandatory = $true)]
    [string]$Reply,

    # Replay the standing authority given at dispatch as the answer: -Continue <taskId>
    [Parameter(ParameterSetName = 'Continue', Mandatory = $true)]
    [string]$Continue,

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

    # Explicitly land a succeeded Worktree task through the server's fetch/rebase/verify/push
    # operation. -Verify is an optional treenode filter; the full suite is deliberately not a gate.
    [Parameter(ParameterSetName = 'Land', Mandatory = $true)]
    [string]$Land,

    [Parameter(ParameterSetName = 'Land')]
    [string]$Verify,

    # Explicit pick of kind/level for a Blocked-for-routing or Queued chain task (CARD-0090).
    # Ends chain governance for that task. Use with -Kind and -Level.
    [Parameter(ParameterSetName = 'Reroute', Mandatory = $true)]
    [string]$Reroute,

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

    'Land' {
        $body = @{}
        if ($Verify) { $body['verify'] = $Verify }
        $result = Invoke-Antiphon -Method POST -Path "/api/agent-tasks/$Land/land" -Body $body
        $suffix = if ($Verify) { " with test filter '$Verify'" } else { '' }
        $word = if ($result.status -eq 'requeued') { 'Requeued land' } else { 'Queued land' }
        Write-Output "$word for task $Land$suffix. The outcome will be delivered to the caller session."
        return
    }

    'Reroute' {
        if (-not $Kind -or -not $Level) {
            Write-Error 'reroute requires -Kind and -Level (the explicit pair a human chose).'
            exit 1
        }
        Invoke-Antiphon -Method POST -Path "/api/agent-tasks/$Reroute/reroute" -Body @{
            agentKind  = $Kind
            modelLevel = $Level
        } | Out-Null
        Write-Output ("rerouted task {0} to {1}/{2} (chain governance ended)" -f $Reroute, $Kind, $Level)
        return
    }

    'Reply' {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            Write-Error 'Pass the answer as the first argument: delegate.ps1 -Reply <taskId> "your answer"'
            exit 1
        }
        Invoke-Antiphon -Method POST -Path "/api/agent-tasks/$Reply/reply" -Body @{
            message = $Message
            origin  = 'Cli'
        } | Out-Null
        Write-Output "Answered task $Reply. It will resume and report back."
        return
    }

    'Continue' {
        Invoke-Antiphon -Method POST -Path "/api/agent-tasks/$Continue/continue" -Body @{
            origin = 'Cli'
        } | Out-Null
        Write-Output "Continued task $Continue with its standing authority. It will resume and report back."
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

        # Whitespace-only is omitted (do not send title). Measure length after Trim; refuse CR/LF
        # and anything over 80 rather than clamping. The server's BuildTitle clamp stays 300.
        $titleText = $null
        if (-not [string]::IsNullOrWhiteSpace($Title)) {
            $titleText = $Title.Trim()
            if ($titleText -match '[\r\n]') {
                Write-Error '-Title is a single line'
                exit 1
            }
            if ($titleText.Length -gt 80) {
                Write-Error (('Title is {0} characters; the limit is 80. -Title is a 2-5 word label for check ' `
                    + 'headers and the board, not a second Goal. Trim it, or put the rest in -Goal.') -f $titleText.Length)
                exit 1
            }
        }
        else {
            # Same first-line extraction as AgentTaskService.BuildTitle: CR -> LF, skip empty, trim.
            $goalNormalized = $Goal.Replace("`r`n", "`n").Replace("`r", "`n")
            $firstLine = $goalNormalized -split "`n" | Where-Object { $_ -ne '' } | Select-Object -First 1
            if ($null -eq $firstLine) { $firstLine = '' } else { $firstLine = $firstLine.Trim() }
            if ($firstLine.Length -gt 80) {
                Write-Output (('WARNING: no -Title; the goal''s first line is {0} characters and will become the ' `
                    + 'check-header/board title (server clamp 300). Pass -Title with 2-5 words (max 80).') -f $firstLine.Length)
            }
        }

        # -Pin writes a pin for THIS card+stage. With nothing that could bind a card - no -Card, no
        # CARD-nnnn in the title, and no task token whose own card could be inherited - it would
        # write a STAGE-WIDE pin that changes routing for every card. That is a deliberate act, so
        # it goes through routing-pin.ps1 rather than falling out of one dispatch.
        if ($Pin -and -not $Card -and $titleText -notmatch '(?i)\bCARD-[0-9]+\b' `
                -and [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
            Write-Error ('-Pin without a card would write a stage-wide pin. Pass -Card CARD-nnnn ' `
                + '(or lead -Title with it), or write the stage pin explicitly with routing-pin.ps1.')
            exit 1
        }
        # Same wording as the server's 422 - refused locally so the mistake costs no round trip.
        if ($Agent -and $OnAgent) {
            Write-Error ('Agent and FollowUpOnTask are two different "run it on that agent" idioms - ' `
                + 'a follow-up already pins to the agent that ran the prior task. Use -Agent or -OnAgent, not both.')
            exit 1
        }
        if ($Complexity -and ($Kind -or $Level)) {
            Write-Error ('complexity cannot be combined with agentKind or modelLevel. An explicit pair ' `
                + 'is a single candidate the caller chose and is never silently rerouted. Pass ' `
                + '-Complexity without -Kind/-Level, or pass the pair without -Complexity.')
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
        if ($Agent) { $body['agent'] = $Agent }
        if ($titleText) { $body['title'] = $titleText }
        if ($Level) { $body['modelLevel'] = $Level }
        # Sent only when chosen - an omitted -Kind leaves the decision to the role policy, which
        # ships unset and therefore resolves to ClaudeCode.
        if ($Kind) { $body['agentKind'] = $Kind }
        if ($Card) { $body['card'] = $Card }
        if ($Dir) { $body['workingDirectory'] = $Dir }
        if ($Scope) { $body['scope'] = $Scope }
        # Omitted (0 - an unbound [int] is 0, not $null) leaves the server's default expectation.
        if ($ExpectAbout -gt 0) { $body['expectedMinutes'] = $ExpectAbout }
        if ($IgnoreSubscriptionQuota) { $body['ignoreSubscriptionQuota'] = $true }
        if ($AllowUnauthenticatedProvider) { $body['allowUnauthenticatedProvider'] = $true }
        if ($IgnoreModelDisabled) { $body['ignoreModelDisabled'] = $true }
        if ($IgnoreRoutingPin) { $body['ignoreRoutingPin'] = $true }
        if ($Complexity) { $body['complexity'] = $Complexity }
        if ($RefuseIfExhausted) { $body['refuseIfExhausted'] = $true }
        if ($EnvOverride -and $EnvOverride.Count -gt 0) { $body['launchEnvOverride'] = $EnvOverride }
        $authorityText = $null
        if (-not [string]::IsNullOrWhiteSpace($AuthorityFile)) {
            if (-not [string]::IsNullOrWhiteSpace($Authority)) {
                Write-Error 'Pass -Authority or -AuthorityFile, not both.'
                exit 1
            }
            if (-not (Test-Path -LiteralPath $AuthorityFile)) {
                Write-Error "-AuthorityFile '$AuthorityFile' does not exist."
                exit 1
            }
            $authorityText = Get-Content -LiteralPath $AuthorityFile -Raw -Encoding UTF8
            if ($null -eq $authorityText) { $authorityText = '' }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($Authority)) {
            $authorityText = $Authority
        }
        if (-not [string]::IsNullOrWhiteSpace($authorityText)) { $body['authority'] = $authorityText }
        if (-not $NoInheritEnv) {
            $inheritedLlmEnv = @{}
            foreach ($name in @('X_LLM_PROJECT', 'X_LLM_KEY')) {
                if ($EnvOverride -and $EnvOverride.ContainsKey($name)) { continue }
                $value = [Environment]::GetEnvironmentVariable($name)
                if ($null -ne $value) { $inheritedLlmEnv[$name] = $value }
            }
            if ($inheritedLlmEnv.Count -gt 0) { $body['inheritedLlmEnv'] = $inheritedLlmEnv }
        }

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
        # Which card this task will move (CARD-0040), said HERE because this is the last moment the
        # caller can still correct a mis-binding - the alternative is discovering it on the board.
        $cardNote = ''
        if ($created.cardIdentifier) { $cardNote = " - bound to $($created.cardIdentifier)" }
        if ($created.status -eq 'Blocked' -and $created.complexity) {
            Write-Output ("BLOCKED - {0}" -f $created.warning)
            Write-Output ('A human decides: clear a hold (model-availability.ps1 clear), wait for a reset, or delegate.ps1 -Reroute <id> -Kind .. -Level ..  Do NOT pick a kind yourself.')
        }
        else {
            Write-Output ("queued task {0} ({1} {2} on {3}{4}){5}{6}" -f `
                    $created.shortId, $body.kind.ToLower(), $body.role.ToLower(), $created.modelLevel, $kindNote, $routing, $cardNote)
            if ($created.routing -and $created.routing.candidates) {
                $all = @($created.routing.candidates)
                $chosen = $all | Where-Object { $_.outcome -eq 'chosen' } | Select-Object -First 1
                $skipped = @($all | Where-Object { $_.outcome -eq 'skipped' })
                if ($null -ne $chosen) {
                    $idx = 1
                    foreach ($c in $all) {
                        if ($c.outcome -eq 'chosen') { break }
                        $idx++
                    }
                    $skipBits = @()
                    foreach ($s in $skipped) {
                        $why = if ($s.reason) { $s.reason } else { 'skipped' }
                        $skipBits += ("{0} ({1})" -f $s.alias, $why)
                    }
                    $skipText = if ($skipBits.Count -gt 0) { '; skipped ' + ($skipBits -join ', ') } else { '' }
                    Write-Output ("routed {0} -> {1} (candidate {2}/{3}){4}" -f `
                            $created.complexity, $chosen.alias, $idx, $all.Count, $skipText)
                }
            }
            # A warning at creation is the caller's one chance to reconsider before the collision.
            if ($created.warning) { Write-Output ("WARNING: {0}" -f $created.warning) }
        }
        # -Pin records what this dispatch RESOLVED to, not what was typed: a caller who passed no
        # -Kind and got Grok from the role policy still means "next time, the same" - and the pin is
        # what makes that survive the next policy change. Human + Required, because a pin written by
        # hand is exactly the decision an Auto write must not overwrite.
        if ($Pin) {
            if (-not $created.cardId) {
                Write-Output ('WARNING: -Pin wrote nothing - this task bound no card, and a pin ' `
                        + 'with no card is a stage-wide rule. Use routing-pin.ps1 if that is what you meant.')
            }
            else {
                $pinBody = @{
                    card       = $created.cardId
                    role       = $body.role
                    provenance = 'Human'
                    strength   = 'Required'
                    agentKind  = $created.agentKind
                    modelLevel = $created.modelLevel
                    reason     = ("pinned by delegate.ps1 -Pin from task {0}" -f $created.shortId)
                }
                $pinned = Invoke-Antiphon -Method PUT -Path '/api/routing-pins' -Body $pinBody
                $pinLine = "pinned {0} {1} to {2}/{3} (human, required)" -f `
                    $created.cardIdentifier, $pinned.role, $pinned.agentKind, $pinned.modelLevel
                if ($Complexity) {
                    $pinLine = $pinLine + (" (this removes {0} {1} from {2}-chain fallback; clear the pin to restore it)" -f `
                            $created.cardIdentifier, $body.role, $Complexity)
                }
                Write-Output $pinLine
            }
        }
        if ($created.followUpMessage) { Write-Output ("FOLLOW-UP: {0}" -f $created.followUpMessage) }
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
