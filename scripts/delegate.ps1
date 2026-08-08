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

    # Declare the files this task owns; intersecting scopes are serialised.
    [Parameter(ParameterSetName = 'Create')]
    [string]$Scope,

    # Answer a blocked delegate's question: -Reply <taskId> "your answer"
    [Parameter(ParameterSetName = 'Reply', Mandatory = $true)]
    [string]$Reply,

    [Parameter(ParameterSetName = 'Reply', Position = 0)]
    [string]$Message,

    # Look up a task you already created.
    [Parameter(ParameterSetName = 'Status', Mandatory = $true)]
    [string]$Status
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
        if ($Dir) { $body['workingDirectory'] = $Dir }
        if ($Scope) { $body['scopeGlob'] = $Scope }

        $created = Invoke-Antiphon -Method POST -Path '/api/agent-tasks' -Body $body
        Write-Output ("queued task {0} ({1} {2} on {3}) - its report will arrive in your session" -f `
                $created.shortId, $body.kind.ToLower(), $body.role.ToLower(), $created.modelLevel)
        # A warning at creation is the caller's one chance to reconsider before the collision.
        if ($created.warning) { Write-Output ("WARNING: {0}" -f $created.warning) }
        return
    }
}
