# CARD-0251: inspect, preview, and create a dedicated sibling orchestrator workspace.
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1.
#
# Sibling layout: C:\src\<checkout>-orchestrator beside C:\src\<checkout>. Nested is unsafe
# for Claude Code (parent CLAUDE.md leaks into every Shared delegate).
#
# Verbs:
#   orchestrator-workspace.ps1 inspect <dir|project> [-Json]
#   orchestrator-workspace.ps1 plan    <project> [-Cli claude|codex|grok] [-Path <sibling>] [-Json]
#   orchestrator-workspace.ps1 setup   <project> [-Cli claude|codex|grok] [-Path <sibling>] [-Verify]
#   orchestrator-workspace.ps1 acknowledge <project>
#
# plan never writes. setup writes files and the Claude external-import flag; it does not
# PATCH the agent's WorkingDirectory (that is the operator's S5 step). Codex/Grok arms write
# files and print "hooks unverified".
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('inspect', 'plan', 'setup', 'acknowledge')]
    [string]$Verb,

    [Parameter(Position = 1)]
    [string]$Target,

    [ValidateSet('claude', 'codex', 'grok')]
    [string]$Cli = 'claude',

    [string]$Path,
    [switch]$Verify,
    [switch]$Json
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
    try {
        if ($null -ne $Body) {
            $jsonBody = $Body | ConvertTo-Json -Depth 10 -Compress
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($jsonBody)
            return Invoke-RestMethod -Method $Method -Uri "$api$Path" -Headers $headers -Body $bytes `
                -ContentType 'application/json; charset=utf-8'
        }
        return Invoke-RestMethod -Method $Method -Uri "$api$Path" -Headers $headers
    }
    catch {
        $detail = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $_.Exception.Message }
        Write-Error "Antiphon $Method $Path failed: $detail"
        exit 1
    }
}

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Resolve-Project($Needle) {
    if ([string]::IsNullOrWhiteSpace($Needle)) { Fail 'Which project? Pass its name or guid.' }
    $all = @(Invoke-Antiphon -Method GET -Path '/api/projects')
    if ($all.Count -eq 1 -and $all[0] -is [System.Array]) { $all = @($all[0]) }
    $exact = @($all | Where-Object { $_.id -eq $Needle -or $_.name -ceq $Needle })
    if ($exact.Count -eq 1) { return $exact[0] }
    if ($exact.Count -gt 1) { Fail "'$Needle' has multiple exact project matches. Pass a project guid instead." }
    $hits = @($all | Where-Object { $_.name -and $_.name.Equals($Needle, [System.StringComparison]::OrdinalIgnoreCase) })
    if ($hits.Count -eq 1) { return $hits[0] }
    if ($hits.Count -gt 1) { Fail "'$Needle' matches $($hits.Count) projects. Pass a project guid instead." }
    Fail "No project matches '$Needle'."
}

function Get-WorkspaceCheck($ProjectId) {
    $readiness = Invoke-Antiphon -Method GET -Path ("/api/projects/{0}/readiness" -f $ProjectId)
    $check = @($readiness.checks | Where-Object { $_.key -eq 'orchestrator-workspace' }) | Select-Object -First 1
    return @{ Readiness = $readiness; Check = $check }
}

function Get-SiblingPath([string]$Checkout) {
    $full = [System.IO.Path]::GetFullPath($Checkout).TrimEnd('\', '/')
    $parent = [System.IO.Path]::GetDirectoryName($full)
    $name = [System.IO.Path]::GetFileName($full)
    if ([string]::IsNullOrWhiteSpace($parent) -or [string]::IsNullOrWhiteSpace($name)) {
        return (Join-Path $full 'orchestrator')
    }
    return (Join-Path $parent ($name + '-orchestrator'))
}

function Get-ForwardSlashKey([string]$Directory) {
    return ([System.IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')).Replace('\', '/')
}

function Write-Marker([string]$Orch, [string]$Checkout, [string]$ProjectId, [string]$CliName) {
    $rel = [System.IO.Path]::GetRelativePath($Orch, $Checkout).Replace('\', '/')
    $obj = @{ version = 1; checkout = $rel; project = $ProjectId; cli = $CliName }
    $jsonText = $obj | ConvertTo-Json -Compress
    Set-Content -LiteralPath (Join-Path $Orch 'antiphon.workspace.json') -Value $jsonText -Encoding UTF8
}

function Write-ClaudeContext([string]$Orch, [string]$Checkout) {
    $rel = [System.IO.Path]::GetRelativePath($Orch, (Join-Path $Checkout 'AGENTS.md')).Replace('\', '/')
    $text = @(
        'You are an orchestrator. You do not do the work - you decompose it, delegate every piece,',
        'and integrate what comes back. Read server/Bundles/orchestrator.md in the checkout for the',
        'standing rules.',
        '',
        ('@' + $rel)
    ) -join "`n"
    Set-Content -LiteralPath (Join-Path $Orch 'CLAUDE.md') -Value $text -Encoding UTF8
}

function Write-AgentsContext([string]$Orch, [string]$Checkout) {
    $rel = [System.IO.Path]::GetRelativePath($Orch, (Join-Path $Checkout 'AGENTS.md')).Replace('\', '/')
    $text = @(
        'You are an orchestrator. You do not do the work - you decompose it, delegate every piece,',
        'and integrate what comes back.',
        '',
        ('At session start, read ' + $rel + ' in full before acting.')
    ) -join "`n"
    Set-Content -LiteralPath (Join-Path $Orch 'AGENTS.md') -Value $text -Encoding UTF8
}

function Write-ClaudeSettings([string]$Orch, [string]$Checkout) {
    $hookDir = Join-Path $Orch '.claude'
    New-Item -ItemType Directory -Force -Path $hookDir | Out-Null
    $hook = (Join-Path $Checkout 'scripts\hooks\orchestrator-investigation-hook.mjs')
    $escaped = $hook.Replace('\', '/')
    $settings = @{
        hooks = @{
            PreToolUse = @(
                @{
                    matcher = 'Read|Grep|Glob|Bash|PowerShell'
                    hooks = @(@{ type = 'command'; command = ('node "' + $escaped + '"'); timeout = 5 })
                }
            )
            SessionStart = @(
                @{
                    matcher = 'compact'
                    hooks = @(@{ type = 'command'; command = ('node "' + $escaped + '"'); timeout = 5 })
                }
            )
        }
    }
    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $hookDir 'settings.json') -Encoding UTF8

    $skillsSrc = Join-Path $Checkout '.claude\skills'
    $skillsDst = Join-Path $hookDir 'skills'
    if ((Test-Path -LiteralPath $skillsSrc) -and -not (Test-Path -LiteralPath $skillsDst)) {
        cmd /c mklink /J "$skillsDst" "$skillsSrc" | Out-Null
    }
}

function Set-ClaudeExternalIncludesApproved([string]$Orch) {
    $claudeJson = Join-Path $env:USERPROFILE '.claude.json'
    $key = Get-ForwardSlashKey $Orch
    $root = @{ projects = @{} }
    if (Test-Path -LiteralPath $claudeJson) {
        try {
            $root = Get-Content -LiteralPath $claudeJson -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
        }
        catch {
            Fail ("Could not parse {0}: {1}" -f $claudeJson, $_.Exception.Message)
        }
    }
    if (-not $root.ContainsKey('projects') -or $null -eq $root['projects']) {
        $root['projects'] = @{}
    }
    $projects = $root['projects']
    if (-not $projects.ContainsKey($key) -or $null -eq $projects[$key]) {
        $projects[$key] = @{}
    }
    $projects[$key]['hasClaudeMdExternalIncludesApproved'] = $true
    $projects[$key]['hasClaudeMdExternalIncludesWarningShown'] = $true
    ($root | ConvertTo-Json -Depth 20) | Set-Content -LiteralPath $claudeJson -Encoding UTF8
}

function Show-Plan([string]$Checkout, [string]$Orch, [string]$CliName, [string]$ProjectId) {
    Write-Output ('checkout: {0}' -f $Checkout)
    Write-Output ('sibling:  {0}' -f $Orch)
    Write-Output ('cli:      {0}' -f $CliName)
    Write-Output 'would write:'
    Write-Output ('  {0}' -f (Join-Path $Orch 'antiphon.workspace.json'))
    if ($CliName -eq 'claude') {
        Write-Output ('  {0}' -f (Join-Path $Orch 'CLAUDE.md'))
        Write-Output ('  {0}' -f (Join-Path $Orch '.claude\settings.json'))
        Write-Output ('  junction {0} -> {1}' -f (Join-Path $Orch '.claude\skills'), (Join-Path $Checkout '.claude\skills'))
        Write-Output ('  ~/.claude.json projects["{0}"].hasClaudeMdExternalIncludesApproved = true' -f (Get-ForwardSlashKey $Orch))
    }
    else {
        Write-Output ('  {0}' -f (Join-Path $Orch 'AGENTS.md'))
        Write-Output '  hooks unverified - see CARD-0251 plan 1.2/1.3'
    }
    Write-Output 'would NOT write: the agent WorkingDirectory PATCH (operator / S5).'
    Write-Output ('project: {0}' -f $ProjectId)
}

if ([string]::IsNullOrWhiteSpace($Target)) { Fail 'Pass a project name/guid, or a directory for inspect.' }

switch ($Verb) {
    'inspect' {
        if (Test-Path -LiteralPath $Target) {
            $dir = [System.IO.Path]::GetFullPath($Target)
            $marker = Join-Path $dir 'antiphon.workspace.json'
            $facts = @{
                directory = $dir
                markerExists = [bool](Test-Path -LiteralPath $marker)
                claudeMd = [bool](Test-Path -LiteralPath (Join-Path $dir 'CLAUDE.md'))
                agentsMd = [bool](Test-Path -LiteralPath (Join-Path $dir 'AGENTS.md'))
            }
            if ($facts.markerExists) {
                $facts.marker = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
            }
            if ($Json) { $facts | ConvertTo-Json -Depth 6; return }
            Write-Output ('directory: {0}' -f $dir)
            Write-Output ('marker:    {0}' -f $facts.markerExists)
            if ($facts.marker) { Write-Output ('checkout:  {0}' -f $facts.marker.checkout) }
            Write-Output ('CLAUDE.md: {0}' -f $facts.claudeMd)
            Write-Output ('AGENTS.md: {0}' -f $facts.agentsMd)
            Write-Output 'For the server classification, pass a project name: inspect <project>.'
            return
        }
        $project = Resolve-Project $Target
        $pack = Get-WorkspaceCheck $project.id
        if ($Json) { $pack.Check | ConvertTo-Json -Depth 6; return }
        Write-Output ('Project: {0} ({1})' -f $project.name, $project.id)
        if (-not $pack.Check) { Fail 'readiness did not return an orchestrator-workspace row.' }
        Write-Output ('{0,-12} {1}' -f $pack.Check.status, $pack.Check.summary)
        if ($pack.Check.detail) { Write-Output $pack.Check.detail }
        return
    }
    'plan' {
        $project = Resolve-Project $Target
        if ([string]::IsNullOrWhiteSpace($project.localRepositoryPath)) {
            Fail 'Project has no localRepositoryPath.'
        }
        $checkout = [System.IO.Path]::GetFullPath($project.localRepositoryPath)
        $orch = if ($Path) { [System.IO.Path]::GetFullPath($Path) } else { Get-SiblingPath $checkout }
        if ($Json) {
            @{ checkout = $checkout; sibling = $orch; cli = $Cli; projectId = $project.id } | ConvertTo-Json
            return
        }
        $pack = Get-WorkspaceCheck $project.id
        if ($pack.Check) {
            Write-Output ('current: {0} - {1}' -f $pack.Check.status, $pack.Check.summary)
        }
        Show-Plan $checkout $orch $Cli $project.id
        return
    }
    'setup' {
        $project = Resolve-Project $Target
        if ([string]::IsNullOrWhiteSpace($project.localRepositoryPath)) {
            Fail 'Project has no localRepositoryPath.'
        }
        $checkout = [System.IO.Path]::GetFullPath($project.localRepositoryPath)
        if (-not (Test-Path -LiteralPath $checkout)) { Fail "Checkout '$checkout' does not exist." }
        $orch = if ($Path) { [System.IO.Path]::GetFullPath($Path) } else { Get-SiblingPath $checkout }
        New-Item -ItemType Directory -Force -Path $orch | Out-Null
        Write-Marker $orch $checkout $project.id $Cli
        if ($Cli -eq 'claude') {
            Write-ClaudeContext $orch $checkout
            Write-ClaudeSettings $orch $checkout
            Set-ClaudeExternalIncludesApproved $orch
        }
        else {
            Write-AgentsContext $orch $checkout
            Write-Output 'hooks unverified - see CARD-0251 plan 1.2/1.3'
        }
        Write-Output ('wrote sibling workspace: {0}' -f $orch)
        Write-Output 'Next: PATCH the standing orchestrator WorkingDirectory to that path, then restart it.'
        Write-Output ('  scripts/orchestrator-workspace.ps1 plan {0}' -f $project.name)
        if ($Verify -and $Cli -eq 'claude') {
            Write-Output 'Claude has no offline inspect; run the S0 codeword canary for proof.'
        }
        elseif ($Verify -and $Cli -eq 'codex') {
            Write-Output 'Verify with: codex debug prompt-input  (hooks unverified)'
        }
        elseif ($Verify -and $Cli -eq 'grok') {
            Write-Output 'Verify with: grok inspect --json  (hooks unverified)'
        }
        return
    }
    'acknowledge' {
        $project = Resolve-Project $Target
        $result = Invoke-Antiphon -Method POST -Path ("/api/projects/{0}/acknowledge-orchestrator-workspace" -f $project.id) -Body @{}
        if ($Json) { $result | ConvertTo-Json -Depth 8; return }
        $check = @($result.checks | Where-Object { $_.key -eq 'orchestrator-workspace' }) | Select-Object -First 1
        if ($check) { Write-Output ('{0}: {1}' -f $check.status, $check.summary) }
        else { Write-Output 'acknowledged.' }
        return
    }
}
