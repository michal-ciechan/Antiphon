# Set up projects and inspect their readiness from a shell, without composing HTTP by hand.
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# Verbs:
#   project.ps1 new       -Dir <path> [-CreateDirectory] [-Name n] [-GitUrl u] [-BaseBranch b] [-BoardName n]
#                        [-Orchestrator | -Worker | -NoAgent] [-AgentName n] [-Profile <displayName|guid>]
#                        [-Level Frontier|High|Medium|Low] [-ReplyStyle Normal|Terse|Caveman|Brief|Explanatory]
#                        [-Bundles a,b] [-PromptFile p] [-RemoteControl] [-Start] [-Json]
#   project.ps1 readiness <project name|guid> [-Json]
#   project.ps1 catalog   [-Json]
#
# -PromptFile is read with Get-Content -Raw and sent as-is, so newlines, quotes and shell
# metacharacters survive untouched. Use a file for prompt text rather than trying to quote it.
#
# The server can return HTTP 200 with notes when -Start was refused after setup committed. That is
# not an HTTP failure: this script prints those notes loudly so the project and agent are not lost
# behind an apparently-successful launch.
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('new', 'readiness', 'catalog')]
    [string]$Verb,

    [Parameter(Position = 1)]
    [string]$Project,

    [string]$Dir,
    [switch]$CreateDirectory,
    [string]$Name,
    [string]$GitUrl,
    [string]$BaseBranch,
    [string]$BoardName,
    [switch]$Orchestrator,
    [switch]$Worker,
    [switch]$NoAgent,
    [string]$AgentName,
    [string]$Profile,
    [ValidateSet('Frontier', 'High', 'Medium', 'Low')]
    [string]$Level,
    [ValidateSet('Normal', 'Terse', 'Caveman', 'Brief', 'Explanatory')]
    [string]$ReplyStyle,
    [string[]]$Bundles,
    [string]$PromptFile,
    [switch]$RemoteControl,
    [switch]$Start,
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
            $json = $Body | ConvertTo-Json -Depth 10 -Compress
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
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

function Read-PromptFile {
    if ([string]::IsNullOrWhiteSpace($PromptFile)) { return $null }
    if (-not (Test-Path -LiteralPath $PromptFile)) { Fail "-PromptFile '$PromptFile' does not exist." }
    $text = Get-Content -LiteralPath $PromptFile -Raw -Encoding UTF8
    if ($null -eq $text) { return '' }
    return $text
}

function Resolve-ProfileId($Catalog) {
    if ([string]::IsNullOrWhiteSpace($Profile)) { return $null }
    $parsed = [guid]::Empty
    if ([guid]::TryParse($Profile, [ref]$parsed)) {
        $hits = @($Catalog.profiles | Where-Object { $_.id -eq $parsed.ToString() })
    }
    else {
        $needle = $Profile.Trim()
        $hits = @($Catalog.profiles | Where-Object { $_.displayName -and $_.displayName.Equals($needle, [System.StringComparison]::OrdinalIgnoreCase) })
    }
    if ($hits.Count -eq 0) { Fail "No enabled profile matches '$Profile'. Run: scripts/project.ps1 catalog" }
    if ($hits.Count -gt 1) { Fail "'$Profile' matches $($hits.Count) enabled profiles. Pass the profile guid instead." }
    return $hits[0].id
}

function Resolve-Project($Needle) {
    if ([string]::IsNullOrWhiteSpace($Needle)) { Fail 'Which project? Pass its name or guid.' }
    $all = @(Invoke-Antiphon -Method GET -Path '/api/projects')
    # Invoke-RestMethod returns JSON arrays as one object when they cross a function boundary.
    # Unwrap that one object so property comparisons below are against projects, not an Object[].
    if ($all.Count -eq 1 -and $all[0] -is [System.Array]) { $all = @($all[0]) }
    $exact = @($all | Where-Object { $_.id -eq $Needle -or $_.name -ceq $Needle })
    if ($exact.Count -eq 1) { return $exact[0] }
    if ($exact.Count -gt 1) { Fail "'$Needle' has multiple exact project matches. Pass a project guid instead." }
    $hits = @($all | Where-Object { $_.name -and $_.name.Equals($Needle, [System.StringComparison]::OrdinalIgnoreCase) })
    if ($hits.Count -eq 1) { return $hits[0] }
    $candidates = if ($hits.Count -gt 0) { $hits } else { $all }
    $names = ($candidates | ForEach-Object { "{0} ({1})" -f $_.name, $_.id }) -join ', '
    if ([string]::IsNullOrWhiteSpace($names)) { $names = ($all | ForEach-Object { "{0} ({1})" -f $_.name, $_.id }) -join ', ' }
    Fail "Project '$Needle' is not uniquely resolvable. Candidates: $names"
}

function Write-Readiness($Readiness) {
    if ($Readiness.canDispatch) { Write-Output 'Ready to dispatch' } else { Write-Output 'Cannot dispatch yet' }
    $rows = @($Readiness.checks | Where-Object { $_.level -eq 'Required' -and $_.status -eq 'Missing' }) + `
        @($Readiness.checks | Where-Object { -not ($_.level -eq 'Required' -and $_.status -eq 'Missing') })
    Write-Output ('{0,-22} {1,-12} {2,-15} {3,-48} {4}' -f 'key', 'level', 'status', 'summary', 'fix')
    foreach ($check in $rows) {
        $fix = ''
        if ($check.fix) {
            $fix = $check.fix.label
            if ($check.fix.route) { $fix += " ($($check.fix.route))" }
        }
        Write-Output ('{0,-22} {1,-12} {2,-15} {3,-48} {4}' -f $check.key, $check.level, $check.status, $check.summary, $fix)
    }
}

function Write-Catalog($Catalog) {
    Write-Output 'Model levels:'
    foreach ($tier in $Catalog.modelLevels) {
        $aliases = ($tier.aliasesByKind.psobject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ', '
        Write-Output ("  {0} ({1}): {2}" -f $tier.key, $aliases, $tier.blurb)
    }
    Write-Output 'Reply styles:'
    foreach ($style in $Catalog.replyStyles) { Write-Output ("  {0}: {1}" -f $style.key, $style.description) }
    Write-Output 'Bundles:'
    foreach ($bundle in $Catalog.bundles) { Write-Output ("  {0}: {1}" -f $bundle.key, $bundle.summary) }
    Write-Output 'Presets:'
    foreach ($preset in $Catalog.presets) {
        $bundles = $preset.bundleKeys -join ', '
        Write-Output ("  {0}: {1}; level={2}; style={3}; alwaysOn={4}; bundles={5}; name={6}; prompt={7}" -f `
                $preset.key, $preset.description, $preset.modelLevel, $preset.replyStyle, $preset.alwaysOn, $bundles, `
                $preset.namePattern, $preset.systemPromptTemplate)
    }
    Write-Output 'Profiles:'
    foreach ($profile in $Catalog.profiles) { Write-Output ("  {0} ({1}) {2}" -f $profile.displayName, $profile.kind, $profile.id) }
    $d = $Catalog.delegation
    Write-Output 'Delegation:'
    Write-Output ("  allowedRoots={0}; allowedRootsIsEmpty={1}; maxConcurrentTasks={2}; maxCostUsdPerRoot={3}; maxDepth={4}; defaultLevel={5}" -f `
            ($d.allowedRoots -join ', '), $d.allowedRootsIsEmpty, $d.maxConcurrentTasks, $d.maxCostUsdPerRoot, $d.maxDepth, $d.defaultLevel)
}

switch ($Verb) {
    'catalog' {
        if ($Project) { Fail 'catalog takes no project argument.' }
        $catalog = Invoke-Antiphon -Method GET -Path '/api/projects/setup-catalog'
        if ($Json) { $catalog | ConvertTo-Json -Depth 10; return }
        Write-Catalog $catalog
        return
    }
    'readiness' {
        $resolved = Resolve-Project $Project
        $readiness = Invoke-Antiphon -Method GET -Path ("/api/projects/{0}/readiness" -f $resolved.id)
        if ($Json) { $readiness | ConvertTo-Json -Depth 10; return }
        Write-Output ("Project: {0} ({1})" -f $resolved.name, $resolved.id)
        Write-Readiness $readiness
        return
    }
    'new' {
        if ($Project) { Fail 'new does not take a positional project argument; use -Dir.' }
        if ([string]::IsNullOrWhiteSpace($Dir)) { Fail 'new requires -Dir <path>.' }
        $presetCount = @($Orchestrator.IsPresent, $Worker.IsPresent, $NoAgent.IsPresent | Where-Object { $_ }).Count
        if ($presetCount -gt 1) { Fail 'Pass only one of -Orchestrator, -Worker, or -NoAgent.' }
        $agentOptions = -not [string]::IsNullOrWhiteSpace($AgentName) -or -not [string]::IsNullOrWhiteSpace($Profile) -or `
            -not [string]::IsNullOrWhiteSpace($Level) -or -not [string]::IsNullOrWhiteSpace($ReplyStyle) -or $Bundles -or `
            -not [string]::IsNullOrWhiteSpace($PromptFile) -or $RemoteControl
        if ($NoAgent -and $agentOptions) { Fail '-NoAgent cannot be combined with agent options.' }

        $body = @{ directory = $Dir }
        if ($CreateDirectory) { $body.createDirectory = $true }
        if ($Name) { $body.name = $Name }
        if ($GitUrl) { $body.gitRepositoryUrl = $GitUrl }
        if ($BaseBranch) { $body.baseBranch = $BaseBranch }
        if ($BoardName) { $body.boardName = $BoardName }
        if ($Start) { $body.startAgent = $true }
        if ($NoAgent) {
            $body.agent = $null
        }
        elseif ($Orchestrator -or $Worker -or $agentOptions) {
            $agent = @{}
            if ($Orchestrator) { $agent.preset = 'orchestrator' }
            elseif ($Worker) { $agent.preset = 'worker' }
            else { $agent.preset = $null }
            if ($AgentName) { $agent.name = $AgentName }
            if ($Profile) {
                $catalog = Invoke-Antiphon -Method GET -Path '/api/projects/setup-catalog'
                $agent.tuiProfileId = Resolve-ProfileId $catalog
            }
            if ($Level) { $agent.modelLevel = $Level }
            if ($ReplyStyle) { $agent.replyStyle = $ReplyStyle }
            if ($Bundles) {
                $agent.bundleKeys = @($Bundles | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            }
            $prompt = Read-PromptFile
            if ($null -ne $prompt) { $agent.systemPromptAppend = $prompt }
            if ($RemoteControl) { $agent.remoteControlEnabled = $true }
            $body.agent = $agent
        }
        $result = Invoke-Antiphon -Method POST -Path '/api/projects/setup' -Body $body
        if ($Json) { $result | ConvertTo-Json -Depth 10; return }
        Write-Output ("Project: {0} ({1})" -f $result.project.name, $result.project.id)
        Write-Output ("Board:   {0} ({1})" -f $result.board.name, $result.board.id)
        if ($result.agent) { Write-Output ("Agent:   {0} ({1})" -f $result.agent.name, $result.agent.id) }
        else { Write-Output 'Agent:   none' }
        Write-Readiness $result.readiness
        if ($result.notes -and $result.notes.Count -gt 0) {
            Write-Warning 'SETUP NOTES - AGENT START MAY HAVE BEEN REFUSED:'
            foreach ($note in $result.notes) { Write-Warning "  $note" }
        }
        return
    }
}
