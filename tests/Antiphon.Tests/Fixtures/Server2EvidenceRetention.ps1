param([string] $Fixture, [string] $Repository, [string] $Scenario)
$ErrorActionPreference = 'Stop'
$owner = '570cc8cb-d999-45c4-871c-941be15d571b'
$other = 'e8f4a030-81d9-44e4-abbf-b6ac84593d29'
$active = 'cb3f1c4a-7d5a-45e1-a157-27497598070b'
$age = [DateTime]::UtcNow.AddHours(-25)
$mount = Join-Path $Fixture 'work'
$root = if ($Scenario -eq 'root') { 'retained' } else { 'evidence' }
$path = Join-Path $mount "$root/task-570cc8cb"
$unlisted = Join-Path $mount "$root/task-e8f4a030"
foreach ($folder in @($path, $unlisted)) {
    [void][IO.Directory]::CreateDirectory($folder)
    [IO.File]::WriteAllText((Join-Path $folder 'receipt'), 'original bytes')
    [IO.File]::SetLastWriteTimeUtc((Join-Path $folder 'receipt'), $age)
    [IO.Directory]::SetLastWriteTimeUtc($folder, $age)
}
$identity = & stat --printf '%d:%i' -- $path
if ($LASTEXITCODE -ne 0) { throw 'stat fixture setup failed' }
if ($Scenario -eq 'identity') {
    [IO.Directory]::Move($path, $path + '-first')
    [void][IO.Directory]::CreateDirectory($path)
    [IO.Directory]::SetLastWriteTimeUtc($path, $age)
}

$scripts = Join-Path $Fixture 'scripts'
[void][IO.Directory]::CreateDirectory((Join-Path $scripts 'lib'))
$source = Get-Content -LiteralPath (Join-Path $Repository 'scripts/sweep-server2-evidence.ps1') -Raw
$source.Replace('/work/', $mount + '/') | Set-Content -LiteralPath (Join-Path $scripts 'sweep.ps1')
$library = Get-Content -LiteralPath (Join-Path $Repository 'scripts/lib/server2-retention-files.ps1') -Raw
# This test owns both fixture trees and has no holder. The cwd witness uses the real procfs scanner.
($library + [Environment]::NewLine + 'function Get-AntiphonRetentionHolder { param($Path) }') |
    Set-Content -LiteralPath (Join-Path $scripts 'lib/server2-retention-files.ps1')
$releaseList = Join-Path $Fixture 'release.json'
ConvertTo-Json -InputObject @(@{ path = $path; taskId = $owner; identity = $identity }) |
    Set-Content -LiteralPath $releaseList

$env:ANTIPHON_API = 'http://127.0.0.1:1'
$env:ANTIPHON_TASK_TOKEN = ''
function git { $global:LASTEXITCODE = 1 }
function Invoke-RestMethod {
    param($Uri, $Headers, $TimeoutSec, $MaximumRedirection)
    switch -Regex ($Uri) {
        '/api/agent-tasks\?.*status=' {
            return @{ items = @(@{ id = $active }); excluded = @{ byProject = @(); total = 0 } }
        }
        '/api/agent-tasks\?.*landPending=' {
            return @{ items = @(); excluded = @{ byProject = @(); total = 0 } }
        }
        "/api/agent-tasks/$active$" {
            $goal = if ($Scenario -eq 'race' -and -not (Test-Path -LiteralPath $path)) { $path } else { 'unrelated' }
            return @{ summary = @{ id = $active; status = 'Queued' }; goal = $goal }
        }
        "/api/agent-tasks/($owner|$other)$" {
            return @{ summary = @{ id = $Matches[1]; status = 'Succeeded'; completedAt = $age }; session = $null }
        }
        '/api/cards/search\?' { return @{ cards = @(); truncated = $false; nextPageToken = $null } }
        default { throw "Unscripted evidence request: $Uri" }
    }
}

$parameters = @{
    ReleaseList = $releaseList
    Repository = $Fixture
    ProjectId = '8721a217-109d-462d-9447-950956b3addb'
    Apply = $true
}
try {
    & (Join-Path $scripts 'sweep.ps1') @parameters
} catch {
    if ($Scenario -ne 'root') { throw }
    $_.Exception.Message | Write-Output
}
"listed=$([IO.Directory]::Exists($path));unlisted=$([IO.Directory]::Exists($unlisted))"
