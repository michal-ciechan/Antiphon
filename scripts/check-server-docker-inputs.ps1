#requires -Version 7.0
<#
.SYNOPSIS
    Check explicit linked Compile inputs in server/ and src/ against server-build COPYs.
.NOTES
    Static guard, not MSBuild evaluation. Conditional items are checked conservatively.
    Unsupported dynamic paths fail closed. Does not build, deploy or contact a runner.
#>
param([string]$Repository = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'

function Normalize([string]$Path) {
    $parts = [Collections.Generic.List[string]]::new()
    foreach ($part in $Path.Replace('\', '/').Split('/')) {
        if ($part -eq '' -or $part -eq '.') { continue }
        if ($part -eq '..') {
            if ($parts.Count -eq 0) { throw "Path escapes root: $Path" }
            $parts.RemoveAt($parts.Count - 1)
        } else { $parts.Add($part) }
    }
    return ($parts -join '/')
}
function Glob([string]$Pattern, [string]$Path) {
    $regex = [regex]::Escape($Pattern).Replace('\*\*/', '(?:.*/)?').Replace('\*\*', '.*').Replace('\*', '[^/]*').Replace('\?', '[^/]')
    return [regex]::IsMatch($Path, '^' + $regex + '$')
}
function Is-Ignored([string]$Path, [string[]]$Rules) {
    # Check parents too: Docker cannot re-include a child of an excluded directory.
    $parts = $Path.Split('/')
    for ($i = 1; $i -le $parts.Count; $i++) {
        $prefix = $parts[0..($i - 1)] -join '/'
        $ignored = $false
        foreach ($rule in $Rules) {
            $negated = $rule.StartsWith('!')
            $pattern = $rule.TrimStart('!').Trim('/')
            if ($pattern -and (Glob $pattern $prefix)) { $ignored = -not $negated }
        }
        if ($ignored) { return $true }
    }
    return $false
}
try {
    $Repository = [IO.Path]::GetFullPath($Repository)
    $required = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $projects = @(Get-ChildItem (Join-Path $Repository 'server'), (Join-Path $Repository 'src') -Filter '*.csproj' -Recurse -File)
    foreach ($project in $projects) {
        [xml]$xml = Get-Content -LiteralPath $project.FullName -Raw
        foreach ($item in $xml.SelectNodes('//Compile[@Include]')) {
            foreach ($include in $item.GetAttribute('Include').Split(';')) {
                if ($include -match '[$@%*?]') { throw "Dynamic Compile Include needs guard support: $include" }
                $path = [IO.Path]::GetFullPath((Join-Path $project.DirectoryName $include.Replace('\', '/')))
                $relative = [IO.Path]::GetRelativePath($Repository, $path).Replace('\', '/')
                # Local Compile declarations are already covered by the project directory COPY.
                if (-not $path.StartsWith($project.DirectoryName + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
                    [void]$required.Add((Normalize $relative))
                }
            }
        }
    }
    if ($projects.Count -eq 0 -or $required.Count -eq 0) { throw 'No projects or linked Compile inputs found' }

    $ignorePath = Join-Path $Repository 'Dockerfile.dockerignore'
    if (-not (Test-Path -LiteralPath $ignorePath)) { $ignorePath = Join-Path $Repository '.dockerignore' }
    $rules = @(Get-Content -LiteralPath $ignorePath | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
    $copies = [Collections.Generic.List[object]]::new()
    $stage = ''; $workdir = ''; $foundStage = $false; $foundPublish = $false
    $dockerfile = (Get-Content -LiteralPath (Join-Path $Repository 'Dockerfile') -Raw) -replace '\\\r?\n', ' '
    foreach ($line in ($dockerfile -split '\r?\n')) {
        if ($line -match '^\s*FROM\s+\S+\s+AS\s+(\S+)') { $stage = $Matches[1]; $workdir = ''; continue }
        if ($stage -ne 'server-build') { continue }
        $foundStage = $true
        if ($line -match '^\s*RUN\s+.*\bdotnet\s+publish\b') { $foundPublish = $true; break }
        if ($line -match '^\s*WORKDIR\s+(\S+)\s*$') {
            $dir = $Matches[1]
            $workdir = Normalize $(if ($dir.StartsWith('/')) { $dir } else { "$workdir/$dir" })
        }
        if ($line -notmatch '^\s*COPY\s+(.+)$') { continue }
        $body = $Matches[1]
        if ($body -match '^--from=') { continue }
        $body = $body -replace '^(--(?:chown|chmod)=\S+\s+)+', ''
        if ($body.StartsWith('--') -or $body.Contains('$')) { throw "Unsupported COPY: $line" }
        $tokens = if ($body.StartsWith('[')) { @($body | ConvertFrom-Json) } else { @([regex]::Matches($body, '"[^"]*"|\S+') | ForEach-Object { $_.Value.Trim('"') }) }
        if ($tokens.Count -lt 2) { throw "Invalid COPY: $line" }
        $destination = $tokens[-1]
        $target = Normalize $(if ($destination.StartsWith('/')) { $destination } else { "$workdir/$destination" })
        foreach ($source in $tokens[0..($tokens.Count - 2)]) {
            $copies.Add(@{ Source = $source.TrimStart('/').TrimEnd('/'); Target = $target; DirectoryTarget = $destination.EndsWith('/') -or $destination -eq '.' -or $tokens.Count -gt 2 })
        }
    }
    if (-not $foundStage -or -not $foundPublish) { throw 'Missing server-build stage or dotnet publish' }
    $missing = [Collections.Generic.List[string]]::new()
    foreach ($path in ($required | Sort-Object)) {
        $expected = "src/$path"; $copied = $false
        foreach ($copy in $copies) {
            $source = Normalize $copy.Source
            $sourceDirectory = Test-Path -LiteralPath (Join-Path $Repository $source) -PathType Container
            $actual = $null
            if ($sourceDirectory -and ($source -eq '' -or $path.StartsWith("$source/", [StringComparison]::Ordinal))) {
                $suffix = if ($source -eq '') { $path } else { $path.Substring($source.Length + 1) }
                $actual = Normalize ($copy.Target + '/' + $suffix)
            } elseif (Glob $source $path) {
                $actual = if ($copy.DirectoryTarget) { Normalize ($copy.Target + '/' + ($path.Split('/')[-1])) } else { $copy.Target }
            }
            if ($actual -ceq $expected) { $copied = $true }
        }
        if (-not (Test-Path -LiteralPath (Join-Path $Repository $path) -PathType Leaf)) { $missing.Add("$path (source absent)") }
        elseif (Is-Ignored $path $rules) { $missing.Add("$path (dockerignore excludes input)") }
        elseif (-not $copied) { $missing.Add("$path (no COPY to /$expected before publish)") }
    }
    foreach ($failure in $missing) { Write-Host "DOCKER INPUT missing $failure" }
    Write-Host "DOCKER INPUT projects=$($projects.Count) linked=$($required.Count) missing=$($missing.Count)"
    if ($missing.Count) { exit 1 }
    exit 0
} catch { Write-Host "DOCKER INPUT error: $($_.Exception.Message)"; exit 2 }
