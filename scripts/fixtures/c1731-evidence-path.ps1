# CARD-1731: G-287 evidence containment, called on the production resolver.
# ASCII-only. No acceptance cache: the second call must see a component that became a link.
function New-C1731DirectoryLink {
    param([string]$Link, [string]$Target)
    try {
        New-Item -ItemType SymbolicLink -Path $Link -Target $Target -ErrorAction Stop | Out-Null
    } catch {
        if ($IsWindows -or $env:OS -eq 'Windows_NT') {
            New-Item -ItemType Junction -Path $Link -Target $Target -ErrorAction Stop | Out-Null
            return
        }
        throw
    }
}

function New-C1731FileLink {
    param([string]$Link, [string]$Target)
    try {
        New-Item -ItemType SymbolicLink -Path $Link -Target $Target -ErrorAction Stop | Out-Null
    } catch {
        if ($IsWindows -or $env:OS -eq 'Windows_NT') {
            $dir = Join-Path (Split-Path -Parent $Link) 'filelink-target'
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            New-Item -ItemType Junction -Path $Link -Target $dir -ErrorAction Stop | Out-Null
            return
        }
        throw
    }
}

function New-C1731EvidenceRoot {
    $root = Join-Path $ResultsDirectory ('c1731-evidence-' + [guid]::NewGuid().ToString('N'))
    $cand = Join-Path $root 'candidate'
    New-Item -ItemType Directory -Path (Join-Path $cand 'evidence') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'outside') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $cand 'real-dir') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'break-target') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $cand 'evidence/suite.trx'), 'trx')
    [IO.File]::WriteAllText((Join-Path $root 'outside/escape.trx'), 'out')
    [IO.File]::WriteAllText((Join-Path $cand 'real-dir/kept.trx'), 'in')
    [IO.File]::WriteAllText((Join-Path $cand 'evidence/hard-source.trx'), 'hard')
    1..13 | ForEach-Object {
        [IO.File]::WriteAllText((Join-Path $cand ('evidence/suite-{0:d3}.trx' -f $_)), 'trx')
    }
    New-C1731DirectoryLink -Link (Join-Path $cand 'escape-link') -Target (Join-Path $root 'outside')
    New-C1731DirectoryLink -Link (Join-Path $cand 'inside-link') -Target (Join-Path $cand 'real-dir')
    New-C1731DirectoryLink -Link (Join-Path $cand 'broken-link') -Target (Join-Path $root 'break-target')
    Remove-Item -LiteralPath (Join-Path $root 'break-target') -Force
    New-Item -ItemType HardLink -Path (Join-Path $cand 'evidence/hard.trx') -Target (Join-Path $cand 'evidence/hard-source.trx') | Out-Null
    New-C1731FileLink -Link (Join-Path $cand 'evidence/filelink.trx') -Target (Join-Path $cand 'evidence/suite.trx')
    # Middle component: a directory link between evidence/ and the file. Symlink on Linux; junction if symlink creation is refused.
    New-Item -ItemType Directory -Path (Join-Path $cand 'evidence/mid-real') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $cand 'evidence/mid-real/x.trx'), 'mid')
    New-C1731DirectoryLink -Link (Join-Path $cand 'evidence/mid-link') -Target (Join-Path $cand 'evidence/mid-real')
    return [pscustomobject]@{ Root = $root; Candidate = $cand }
}

function Get-C1731EvidenceFull {
    param([string]$Candidate, [string]$Relative)
    $root = [IO.Path]::GetFullPath($Candidate).TrimEnd([char[]]@('\', '/'))
    return [IO.Path]::GetFullPath((Join-Path $root $Relative))
}

function Test-C1731_EvidenceRefusals {
    # Each G-287 shape refuses, and every accepted path is the normalized full path.
    # Measured against the production resolver at the cmdlet walk and again after it.
    $fx = New-C1731EvidenceRoot
    $cand = $fx.Candidate
    $ordinary = Get-C1731EvidenceFull -Candidate $cand -Relative 'evidence/suite.trx'
    $missing = Get-C1731EvidenceFull -Candidate $cand -Relative 'evidence/missing.trx'
    $missingParent = Get-C1731EvidenceFull -Candidate $cand -Relative 'no/such/file.trx'
    $hard = Get-C1731EvidenceFull -Candidate $cand -Relative 'evidence/hard.trx'
    $cases = @(
        @{ n = 'ordinary'; r = 'evidence/suite.trx'; e = $ordinary },
        @{ n = 'missing-file'; r = 'evidence/missing.trx'; e = $missing },
        @{ n = 'missing-parent'; r = 'no/such/file.trx'; e = $missingParent },
        @{ n = 'blank'; r = '   '; e = '' },
        @{ n = 'empty'; r = ''; e = '' },
        @{ n = 'absolute'; r = $ordinary; e = '' },
        @{ n = 'parent-escape'; r = '../outside/escape.trx'; e = '' },
        @{ n = 'deep-escape'; r = 'evidence/../../outside/escape.trx'; e = '' },
        @{ n = 'dotdot-inside'; r = 'evidence/../evidence/suite.trx'; e = $ordinary },
        @{ n = 'dot-segment'; r = './evidence/./suite.trx'; e = $ordinary },
        @{ n = 'escape-link'; r = 'escape-link/escape.trx'; e = '' },
        @{ n = 'inside-link'; r = 'inside-link/kept.trx'; e = '' },
        @{ n = 'mid-link'; r = 'evidence/mid-link/x.trx'; e = '' },
        @{ n = 'file-link'; r = 'evidence/filelink.trx'; e = '' },
        @{ n = 'broken-link'; r = 'broken-link'; e = '' },
        @{ n = 'broken-child'; r = 'broken-link/file.trx'; e = '' },
        @{ n = 'hard-link'; r = 'evidence/hard.trx'; e = $hard },
        @{ n = 'root-dot'; r = '.'; e = '' }
    )
    $got = @{}
    foreach ($c in $cases) {
        $got[$c.n] = [string](Resolve-ReleaseAuthorityEvidencePath -CandidateRoot $cand -Relative $c.r)
    }
    Assert-C487 ($got['ordinary'] -ceq $ordinary) 'C1731 ordinary relative evidence resolves under the candidate root' $got['ordinary']
    Assert-C487 (($got['missing-file'] -ceq $missing) -and ($got['missing-parent'] -ceq $missingParent)) 'C1731 missing evidence resolves when no component is a reparse point' ($got['missing-file'] + ' ' + $got['missing-parent'])
    Assert-C487 (($got['blank'] -ceq '') -and ($got['empty'] -ceq '')) 'C1731 blank and empty evidence refuses' ($got['blank'] + '/' + $got['empty'])
    Assert-C487 ($got['absolute'] -ceq '') 'C1731 absolute evidence refuses' $got['absolute']
    Assert-C487 (($got['parent-escape'] -ceq '') -and ($got['deep-escape'] -ceq '') -and ($got['root-dot'] -ceq '')) 'C1731 parent-relative escape refuses' ($got['parent-escape'] + ' ' + $got['deep-escape'])
    Assert-C487 (($got['dotdot-inside'] -ceq $ordinary) -and ($got['dot-segment'] -ceq $ordinary)) 'C1731 a path that normalizes inside the root resolves to that file' ($got['dotdot-inside'] + ' ' + $got['dot-segment'])
    Assert-C487 ($got['escape-link'] -ceq '') 'C1731 a linked directory pointing outside refuses' $got['escape-link']
    Assert-C487 ($got['inside-link'] -ceq '') 'C1731 a linked directory whose target stays inside the root still refuses' $got['inside-link']
    Assert-C487 ($got['mid-link'] -ceq '') 'C1731 a link in a middle path component refuses' $got['mid-link']
    Assert-C487 ($got['file-link'] -ceq '') 'C1731 a linked file refuses' $got['file-link']
    Assert-C487 (($got['broken-link'] -ceq '') -and ($got['broken-child'] -ceq '')) 'C1731 a broken link refuses' ($got['broken-link'] + ' ' + $got['broken-child'])
    Assert-C487 ($got['hard-link'] -ceq $hard) 'C1731 a hard link resolves' $got['hard-link']
    $same = $true
    $diff = ''
    foreach ($c in $cases) {
        if ($got[$c.n] -cne [string]$c.e) { $same = $false; $diff = $c.n + ' got=[' + $got[$c.n] + '] expected=[' + $c.e + ']' }
    }
    Assert-C487 $same 'C1731 seeded evidence results match the recorded set' $diff

    $fresh = Join-Path $cand 'fresh'
    New-Item -ItemType Directory -Path $fresh -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $fresh 'file.trx'), 'f')
    $before = [string](Resolve-ReleaseAuthorityEvidencePath -CandidateRoot $cand -Relative 'fresh/file.trx')
    Remove-Item -LiteralPath (Join-Path $fresh 'file.trx') -Force
    Remove-Item -LiteralPath $fresh -Force
    New-C1731DirectoryLink -Link $fresh -Target (Join-Path $fx.Root 'outside')
    $after = [string](Resolve-ReleaseAuthorityEvidencePath -CandidateRoot $cand -Relative 'fresh/escape.trx')
    $expectedBefore = Get-C1731EvidenceFull -Candidate $cand -Relative 'fresh/file.trx'
    Assert-C487 (($before -ceq $expectedBefore) -and ($after -ceq '')) 'C1731 an accepted path refuses after that component becomes a link' ('before=' + $before + ' after=' + $after)
}

function Test-C1731_EvidenceWalk {
    # 611 ordinary resolutions is the 2026-11-06 ledger path-walk count.
    # The cmdlet walk measured 138.2s at 9e7ff34988; the ceiling is 15s.
    # That still fails the cmdlet walk on this host and is about 100x the
    # 0.153s attribute-walk sample (n=1, provisional).
    $fx = New-C1731EvidenceRoot
    $cand = $fx.Candidate
    $paths = 1..13 | ForEach-Object { 'evidence/suite-{0:d3}.trx' -f $_ }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt 47; $i++) {
        foreach ($p in $paths) {
            [void](Resolve-ReleaseAuthorityEvidencePath -CandidateRoot $cand -Relative $p)
        }
    }
    $ms = $sw.Elapsed.TotalMilliseconds
    Assert-C487 ($ms -lt 15000) 'C1731 six hundred eleven ordinary resolutions finish within fifteen seconds' ('elapsedMs=' + [int]$ms)
}
