# CARD-1609. Versioned origin-only proof writer. Dot-source only.
function Invoke-C1609BoundedProcess {
    param([string]$FileName, [string[]]$Arguments, [int]$TimeoutMs=30000,
        [string]$WorkingDirectory='', [string]$InputText='', [string]$OutputFile='',
        [switch]$CaptureOutput, [switch]$GitEnvironment)
    if($TimeoutMs -le 0 -or $InputText.Length -gt 16777216){throw 'deadline'}
    $psi=[Diagnostics.ProcessStartInfo]::new()
    $psi.FileName=$FileName; $psi.UseShellExecute=$false; $psi.CreateNoWindow=$true
    $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true; $psi.RedirectStandardInput=$true
    if ($WorkingDirectory) { $psi.WorkingDirectory=$WorkingDirectory }
    if($GitEnvironment){
        foreach($key in @('GIT_DIR','GIT_WORK_TREE','GIT_INDEX_FILE','GIT_OBJECT_DIRECTORY','GIT_ALTERNATE_OBJECT_DIRECTORIES','GIT_CONFIG_PARAMETERS','GIT_CONFIG_COUNT')) {
            [void]$psi.Environment.Remove($key)
        }
        $psi.Environment['GIT_TERMINAL_PROMPT']='0'; $psi.Environment['GIT_NO_LAZY_FETCH']='1'; $psi.Environment['GIT_NO_REPLACE_OBJECTS']='1'
    }
    foreach($arg in $Arguments){[void]$psi.ArgumentList.Add($arg)}
    $proc=[Diagnostics.Process]::new(); $proc.StartInfo=$psi; $started=$false
    $watch=[Diagnostics.Stopwatch]::StartNew(); $output=[Text.StringBuilder]::new(); $writer=$null; $exitedAt=$null
    try {
        if($OutputFile){$writer=[IO.StreamWriter]::new([IO.File]::Open($OutputFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None),[Text.UTF8Encoding]::new($false))}
        [void]$proc.Start(); $started=$true
        $stdinTask=$proc.StandardInput.WriteAsync($InputText); $inputClosed=$false
        $streams=@(
            @{reader=$proc.StandardOutput;buffer=[char[]]::new(4096);done=$false;task=$null;count=0;keep=$true},
            @{reader=$proc.StandardError;buffer=[char[]]::new(4096);done=$false;task=$null;count=0;keep=$false})
        foreach($s in $streams){$s.task=$s.reader.ReadAsync($s.buffer,0,$s.buffer.Length)}
        while(-not $inputClosed -or @($streams|Where-Object{-not $_.done}).Count){
            if($watch.ElapsedMilliseconds -ge $TimeoutMs){throw 'deadline'}
            if(-not $inputClosed -and $stdinTask.IsCompleted){[void]$stdinTask.GetAwaiter().GetResult();$proc.StandardInput.Close();$inputClosed=$true}
            if($proc.HasExited){
                if($null -eq $exitedAt){$exitedAt=$watch.ElapsedMilliseconds}
                if($watch.ElapsedMilliseconds-$exitedAt -gt 2000){throw 'drain'}
            }
            foreach($s in $streams){
                if($s.done -or -not $s.task.IsCompleted){continue}
                $n=$s.task.GetAwaiter().GetResult()
                if($n -eq 0){$s.done=$true;continue}
                $s.count+=$n
                $cap=if($s.keep -and $writer){1073741824}else{8388608}
                if($s.count -gt $cap){throw 'output-bound'}
                if($s.keep -and $writer){$writer.Write($s.buffer,0,$n)}
                elseif($s.keep -and $CaptureOutput){[void]$output.Append($s.buffer,0,$n)}
                $s.task=$s.reader.ReadAsync($s.buffer,0,$s.buffer.Length)
            }
            [Threading.Thread]::Sleep(5)
        }
        $remaining=[Math]::Max(1,$TimeoutMs-[int]$watch.ElapsedMilliseconds)
        if(-not $proc.WaitForExit($remaining)){throw 'deadline'}
        return [pscustomobject]@{ExitCode=$proc.ExitCode;Stdout=$output.ToString()}
    } finally {
        if($started -and -not $proc.HasExited){$proc.Kill($true);[void]$proc.WaitForExit(2000)}
        if($writer){$writer.Dispose()}; $proc.Dispose()
    }
}

function Invoke-C1609HostGit {
    param([string]$Repo, [string[]]$GitArgs, [int]$TimeoutMs=30000, [string]$WorkingDirectory='',
        [string]$InputText='', [string]$OutputFile='')
    try {
        $arguments=@('-c','core.hooksPath=/dev/null','-c','protocol.allow=never','-c','protocol.https.allow=always',
            '-c','protocol.ssh.allow=always','-c','http.followRedirects=false','-c','credential.interactive=false')+$GitArgs
        $result=Invoke-C1609BoundedProcess -FileName git -Arguments $arguments -TimeoutMs $TimeoutMs -WorkingDirectory $WorkingDirectory `
            -InputText $InputText -OutputFile $OutputFile -CaptureOutput -GitEnvironment
        if($result.ExitCode -ne 0){throw 'git'}
        return $result.Stdout
    } catch {throw (Get-C1008HostLsRemoteDiagnosis -Repo $Repo -Status 2)}
}

function Assert-C1609RequiredObjects {
    param([object[]]$RequiredObjects,[string[]]$Advertisement,[string]$Scratch,[string]$Repo,[int]$TimeoutMs)
    if(-not $RequiredObjects.Count){return}
    $expected=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach($row in $RequiredObjects){
        if((($row.PSObject.Properties.Name|Sort-Object)-join ',') -cne 'objectId,type' -or
            $row.objectId -isnot [string] -or $row.objectId -cnotmatch '^[0-9a-f]{40}$' -or
            $row.type -isnot [string] -or $row.type -cnotin @('commit','tag','tree','blob') -or -not $expected.TryAdd($row.objectId,$row.type)){throw 'required-object'}
    }
    $watch=[Diagnostics.Stopwatch]::StartNew();$nl=[string][char]10
    $types=Invoke-C1609HostGit -Repo $Repo -WorkingDirectory $Scratch -TimeoutMs $TimeoutMs `
        -GitArgs @('--git-dir',$Scratch,'cat-file','--batch-check=%(objectname) %(objecttype)') -InputText (($expected.Keys -join $nl)+$nl)
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($line in $types.TrimEnd([char]10).Split([char]10)){
        if($line -cnotmatch '^([0-9a-f]{40}) (commit|tag|tree|blob)$' -or
            -not $expected.ContainsKey($Matches[1]) -or $expected[$Matches[1]] -cne $Matches[2] -or -not $seen.Add($Matches[1])){throw 'object-type'}
    }
    if($seen.Count -ne $expected.Count){throw 'object-coverage'}
    $advertised=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $roots=[Collections.Generic.List[string]]::new()
    foreach($line in $Advertisement){
        [void]$advertised.Add($line.Substring(0,40))
        if($line -cmatch '\trefs/(heads/|antiphon/salvage/)'){$roots.Add($line.Substring(0,40))}
    }
    $commits=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $trees=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($pair in $expected.GetEnumerator()){
        switch($pair.Value){
            commit {[void]$commits.Add($pair.Key)}
            tree {if(-not $advertised.Contains($pair.Key)){[void]$trees.Add($pair.Key)}}
            blob {if(-not $advertised.Contains($pair.Key)){throw 'unpublished-blob'}}
            tag {
                if(-not $advertised.Contains($pair.Key)){throw 'unpublished-tag'}
                $remaining=$TimeoutMs-[int]$watch.ElapsedMilliseconds;if($remaining -le 0){throw 'deadline'}
                $peeled=Invoke-C1609HostGit -Repo $Repo -WorkingDirectory $Scratch -TimeoutMs $remaining `
                    -GitArgs @('--git-dir',$Scratch,'cat-file','--batch-check=%(objectname) %(objecttype)') -InputText ($pair.Key+'^{commit}'+$nl)
                if($peeled.Trim() -cmatch '^([0-9a-f]{40}) commit$'){[void]$commits.Add($Matches[1])}
                elseif($peeled.Trim() -cne ($pair.Key+'^{commit} missing')){throw 'tag-peel'}
            }
        }
    }
    if($commits.Count){
        $remaining=$TimeoutMs-[int]$watch.ElapsedMilliseconds;if($remaining -le 0){throw 'deadline'}
        $revisions=[Collections.Generic.List[string]]::new();foreach($oid in $commits){$revisions.Add($oid)};foreach($root in $roots){$revisions.Add('^'+$root)}
        $count=Invoke-C1609HostGit -Repo $Repo -WorkingDirectory $Scratch -TimeoutMs $remaining `
            -GitArgs @('--git-dir',$Scratch,'rev-list','--count','--stdin') -InputText (($revisions.ToArray() -join $nl)+$nl)
        if($count.Trim() -cne '0'){throw 'unpublished-commit'}
    }
    if($trees.Count){
        $remaining=$TimeoutMs-[int]$watch.ElapsedMilliseconds;if($remaining -le 0){throw 'deadline'}
        $file=Join-Path $Scratch 'required-trees'
        [void](Invoke-C1609HostGit -Repo $Repo -WorkingDirectory $Scratch -TimeoutMs $remaining -OutputFile $file `
            -GitArgs (@('--git-dir',$Scratch,'rev-list','--objects','--no-object-names','--filter=blob:none','--missing=error','--stdin')) `
            -InputText (($roots.ToArray() -join $nl)+$nl))
        foreach($line in [IO.File]::ReadLines($file)){if($line -cnotmatch '^[0-9a-f]{40}$'){throw 'tree-graph'};[void]$trees.Remove($line)}
        if($trees.Count){throw 'unpublished-tree'}
    }
}

function Read-C1609Advertisement([string]$Text) {
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $rows=[Collections.Generic.List[string]]::new()
    $lines=$Text.Split([char]10)
    for($i=0;$i -lt $lines.Length;$i++){
        $line=$lines[$i].TrimEnd([char]13)
        if($line -eq '' -and $i -eq $lines.Length-1){continue}
        if($line -cnotmatch '^[0-9a-f]{40}\trefs/[^\s]+$'){throw 'advertisement'}
        $ref=$line.Substring(41)
        if($ref -cnotmatch '^refs/(heads|tags)/[^\s]+$'){
            if($ref -cnotmatch '^refs/antiphon/salvage/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/([0-9a-f]{40})$' -or
                $Matches[1] -cne $line.Substring(0,40)){throw 'advertisement'}
        }
        if(-not $seen.Add($ref)){throw 'advertisement'}
        if($line.ToLowerInvariant() -match 'gh[pousr]_|github_pat_|token|password|secret'){throw 'advertisement'}
        $rows.Add($line)
    }
    if($rows.Count -eq 0 -or @($rows|Where-Object{$_ -cmatch '\trefs/heads/'}).Count -eq 0){throw 'advertisement'}
    $array=$rows.ToArray(); [Array]::Sort($array,[StringComparer]::Ordinal)
    return $array
}

# The advertisement has already passed the closed name/OID check. Validate all
# commit comparison roots even when no renewal objects were requested, including
# graphs recovered from a previous bundle. Tags retain exact-object semantics.
function Assert-C1609CommitRoots {
    param([string[]]$Advertisement,[string]$Scratch,[string]$Repo,[int]$TimeoutMs)
    $roots=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($line in $Advertisement){
        if($line -cmatch '\trefs/(heads/|antiphon/salvage/)'){[void]$roots.Add($line.Substring(0,40))}
    }
    $nl=[string][char]10
    $types=Invoke-C1609HostGit -Repo $Repo -WorkingDirectory $Scratch -TimeoutMs $TimeoutMs `
        -GitArgs @('--git-dir',$Scratch,'cat-file','--batch-check=%(objectname) %(objecttype)') -InputText (($roots -join $nl)+$nl)
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($line in $types.TrimEnd([char]10).Split([char]10)){
        if($line -cnotmatch '^([0-9a-f]{40}) commit$' -or -not $roots.Contains($Matches[1]) -or -not $seen.Add($Matches[1])){throw 'origin-type'}
    }
    if($seen.Count -ne $roots.Count){throw 'origin-type'}
}

function Assert-C1609BundleAdvertisement {
    param([string]$Text,[string[]]$Advertisement)
    $rows=[Collections.Generic.List[string]]::new()
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($line in $Text.TrimEnd([char]10).Split([char]10)){
        if($line -cnotmatch '^([0-9a-f]{40}) ([^\s]+)$' -or -not $seen.Add($Matches[2])){throw 'bundle-refs'}
        if($Matches[2] -ceq 'HEAD'){continue}
        $rows.Add($Matches[1]+[char]9+$Matches[2])
    }
    $actual=@(Read-C1609Advertisement (($rows.ToArray() -join [char]10)+[char]10))
    $expected=@($Advertisement|Where-Object{-not $_.EndsWith('^{}')})
    if(($actual -join [char]10) -cne ($expected -join [char]10)){throw 'bundle-refs'}
}

# CARD-1693: the standing fleet pushes to the shared origin all the time, so the before/after
# advertisement compare can move during a full fetch (52 s on 2026-11-03 run c7275700). Only that
# 'snapshot' move is retried, at most five attempts inside the one network budget; every attempt
# repeats every check. Each failed attempt logs its fixed reason token (never a URL or Git stderr)
# to the local host-proof.log, which is not uploaded. The refusal text stays unchanged.
# The attempts share one scratch repository: a retry fetches incrementally with --prune instead of
# re-cloning the whole origin, so its ls-remote..ls-remote window is seconds, not a full clone (the
# 2026-11-04 preview c72743c9 re-cloned 5081 refs per attempt and logged reason=snapshot three times).
# Local refs still equal the fetched advertisement exactly; leftover objects reach no proof because
# every reachability check and the bundle start from those refs.
function Write-C1609HostOriginProof {
    param([string]$OriginUrl,[string]$RepoRelative,[string]$OperationId,[string]$ProofDirectory,
        [long]$CreatedUnix,[string]$LogPath,[string]$PhaseNonce,[string]$InventoryDigest,[string[]]$ApprovedOrigins,
        [int]$NetworkBudgetMs=600000,[object[]]$RequiredObjects=@(),[string]$PreviousProofFile='')
    $budget=[Diagnostics.Stopwatch]::StartNew()
    $scratch=Join-Path ([IO.Path]::GetTempPath()) ('c1609-host-'+[guid]::NewGuid().ToString('N'))
    try {
        for($attempt=1;;$attempt++){
            $script:C1609HostProofReason=''
            $remaining=$NetworkBudgetMs-[int]$budget.ElapsedMilliseconds
            try {
                Invoke-C1609HostOriginProofAttempt -OriginUrl $OriginUrl -RepoRelative $RepoRelative -OperationId $OperationId `
                    -ProofDirectory $ProofDirectory -CreatedUnix $CreatedUnix -LogPath $LogPath -PhaseNonce $PhaseNonce `
                    -InventoryDigest $InventoryDigest -ApprovedOrigins $ApprovedOrigins -NetworkBudgetMs $remaining `
                    -RequiredObjects $RequiredObjects -PreviousProofFile $PreviousProofFile -Scratch $scratch
                return
            } catch {
                $reason=[string]$script:C1609HostProofReason
                if($reason -cnotmatch '^[a-z][a-z-]{0,31}$'){$reason='other'}
                if($LogPath -and $RepoRelative -cmatch '^[A-Za-z0-9._/-]+$'){
                    try{[IO.File]::AppendAllText($LogPath,('repo='+$RepoRelative+' attempt='+$attempt+' reason='+$reason+[char]10),[Text.UTF8Encoding]::new($false))}catch{}
                }
                if($reason -cne 'snapshot' -or $attempt -ge 5 -or ($NetworkBudgetMs-[int]$budget.ElapsedMilliseconds) -le 0){throw}
            }
        }
    } finally {
        if([IO.Directory]::Exists($scratch)){Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue}
    }
}

function Invoke-C1609HostOriginProofAttempt {
    param([string]$OriginUrl,[string]$RepoRelative,[string]$OperationId,[string]$ProofDirectory,
        [long]$CreatedUnix,[string]$LogPath,[string]$PhaseNonce,[string]$InventoryDigest,[string[]]$ApprovedOrigins,
        [int]$NetworkBudgetMs=600000,[object[]]$RequiredObjects=@(),[string]$PreviousProofFile='',[string]$Scratch)
    $bundle=''; $bundleCreated=$false
    try {
        if($OperationId -cnotmatch '^c1008[0-9a-f]{32}$' -or $PhaseNonce -cnotmatch '^[0-9a-f]{32}$' -or
            $InventoryDigest -cnotmatch '^[0-9a-f]{64}$' -or $RepoRelative -cnotmatch '^[A-Za-z0-9._/-]+$' -or
            $RepoRelative.Contains('..') -or $RepoRelative.Contains('//') -or $RepoRelative.StartsWith('/')){throw 'context'}
        $allowed=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($approved in $ApprovedOrigins){[void]$allowed.Add($approved)}
        if(-not $allowed.Contains($OriginUrl)){throw 'origin'}
        $scrubbed=ConvertTo-C1008ScrubbedOrigin -OriginUrl $OriginUrl
        if(-not $scrubbed -or $scrubbed -cne $OriginUrl){throw 'origin'}
        $valid=$OriginUrl -cmatch '^git@[A-Za-z0-9.-]+:[A-Za-z0-9._/-]+$'
        if(-not $valid){
            $uri=$null
            if(-not [Uri]::TryCreate($OriginUrl,[UriKind]::Absolute,[ref]$uri) -or
                $uri.Scheme -cnotin @('https','ssh') -or $uri.Query -or $uri.Fragment -or
                ($uri.Scheme -ceq 'https' -and $uri.UserInfo) -or
                ($uri.Scheme -ceq 'ssh' -and $uri.UserInfo -cne 'git') -or
                $OriginUrl -cmatch '[\x00-\x20\x7f]'){throw 'origin'}
        }
        if(-not [IO.Directory]::Exists($ProofDirectory) -or
            ([IO.DirectoryInfo]::new($ProofDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'custody'}
        $now=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        if($CreatedUnix -lt 0 -or $CreatedUnix -gt $now+60 -or $now-$CreatedUnix -gt 600){throw 'clock'}
        if($NetworkBudgetMs -le 0 -or $NetworkBudgetMs -gt 600000){throw 'deadline'}
        $watch=[Diagnostics.Stopwatch]::StartNew()
        # The caller owns the scratch path and removes it. Only the first attempt creates it, and only a
        # fresh repository may take a previous bundle: a retry fetches into the refs it already holds.
        if(-not $Scratch -or [IO.Path]::GetFileName($Scratch) -cnotmatch '^c1609-host-[0-9a-f]{32}$'){throw 'custody'}
        $fresh=-not [IO.Directory]::Exists($scratch)
        if($fresh){[void][IO.Directory]::CreateDirectory($scratch)}
        foreach($leftover in @('c1609-proof.bundle','required-trees')){
            $path=Join-Path $scratch $leftover
            if([IO.File]::Exists($path)){[IO.File]::Delete($path)}
        }
        $originHash=Get-C1008Sha256 $OriginUrl
        $name=Get-C1008Sha256 ($RepoRelative+[char]10+$OriginUrl)
        $bundle=Join-Path $ProofDirectory ($name+'.bundle')
        $proofFile=Join-Path $ProofDirectory $name
        if([IO.File]::Exists($bundle) -or [IO.File]::Exists($proofFile)){throw 'existing'}
        if($fresh){[void](Invoke-C1609HostGit -Repo $RepoRelative -GitArgs @('init','-q','--bare','--template=',$scratch) -WorkingDirectory $scratch)}
        $common=@('--git-dir',$scratch)
        $cap=[Math]::Min(30000,$NetworkBudgetMs-[int]$watch.ElapsedMilliseconds);if($cap -le 0){throw 'deadline'}
        $before=@(Read-C1609Advertisement (Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('ls-remote',$OriginUrl,'refs/heads/*','refs/tags/*','refs/antiphon/salvage/*')) -TimeoutMs $cap -WorkingDirectory $scratch))
        $reuse=$false
        if($PreviousProofFile){
            $old=[IO.File]::ReadAllLines($PreviousProofFile)
            if($old.Length -lt 12 -or $old[0] -cne 'c1008-host-proof 2' -or $old[1] -cne ('operation '+$OperationId) -or
                $old[2] -cne ('origin '+$originHash) -or $old[7] -cnotmatch '^bundle [0-9a-f]{64}$' -or
                ([IO.FileInfo]::new($PreviousProofFile).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                (Get-FileHash -LiteralPath ($PreviousProofFile+'.bundle') -Algorithm SHA256).Hash.ToLowerInvariant() -cne $old[7].Substring(7)){throw 'previous-proof'}
            $oldAdvertisement=@(Read-C1609Advertisement (($old[11..($old.Length-1)] -join [char]10)+[char]10))
            if($old[3] -cne ('repo '+$RepoRelative) -or $old[5] -cnotmatch '^nonce [0-9a-f]{32}$' -or
                $old[6] -cnotmatch '^inventory [0-9a-f]{64}$' -or $old[8] -cne ('advertisement '+(Get-C1008Sha256 (($oldAdvertisement -join [char]10)+[char]10))) -or
                $old[9] -cne ('count '+$oldAdvertisement.Count)){throw 'previous-proof'}
            $reuse=$fresh -and ($oldAdvertisement -join [char]10) -ceq ($before -join [char]10)
        }
        $cap=[Math]::Min(300000,$NetworkBudgetMs-[int]$watch.ElapsedMilliseconds);if($cap -le 0){throw 'deadline'}
        # CARD-1693: Git reads and writes a bundle only inside the short scratch directory. The desktop
        # proof directory is 194 characters, so <name>.bundle is 266 and its .lock 271: past MAX_PATH for
        # Git for Windows without core.longpaths. .NET moves the bytes and is long-path safe.
        $staged=Join-Path $scratch 'c1609-proof.bundle'
        if($reuse){
            [IO.File]::Copy($PreviousProofFile+'.bundle',$staged,$false)
            if((Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash.ToLowerInvariant() -cne $old[7].Substring(7)){throw 'previous-proof'}
            $bundleRefs=Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('bundle','unbundle',$staged)) -TimeoutMs $cap -WorkingDirectory $scratch
            Assert-C1609BundleAdvertisement -Text $bundleRefs -Advertisement $before
            $updates=@($before|Where-Object{-not $_.EndsWith('^{}')}|ForEach-Object{'create '+$_.Substring(41)+' '+$_.Substring(0,40)})
            [void](Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('update-ref','--stdin')) -TimeoutMs $cap -WorkingDirectory $scratch -InputText (($updates -join [char]10)+[char]10))
        } else {
            [void](Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('fetch','-q','--prune','--no-tags','--no-write-fetch-head',
                '--no-recurse-submodules',$OriginUrl,'+refs/heads/*:refs/heads/*','+refs/tags/*:refs/tags/*',
                '+refs/antiphon/salvage/*:refs/antiphon/salvage/*')) -TimeoutMs $cap -WorkingDirectory $scratch)
        }
        $actual=Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('show-ref','--dereference')) -WorkingDirectory $scratch
        $actual=@(Read-C1609Advertisement ($actual.Replace(' ',[string][char]9)))
        if(($before -join [char]10) -cne ($actual -join [char]10)){throw 'snapshot'}
        $cap=[Math]::Min(300000,$NetworkBudgetMs-[int]$watch.ElapsedMilliseconds);if($cap -le 0){throw 'deadline'}
        Assert-C1609CommitRoots -Advertisement $before -Scratch $scratch -Repo $RepoRelative -TimeoutMs $cap
        $cap=[Math]::Min(300000,$NetworkBudgetMs-[int]$watch.ElapsedMilliseconds);if($cap -le 0){throw 'deadline'}
        Assert-C1609RequiredObjects -RequiredObjects $RequiredObjects -Advertisement $before -Scratch $scratch -Repo $RepoRelative -TimeoutMs $cap
        $cap=[Math]::Min(300000,$NetworkBudgetMs-[int]$watch.ElapsedMilliseconds);if($cap -le 0){throw 'deadline'}
        $bundleCreated=$true
        if(-not $reuse){[void](Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('bundle','create',$staged,'--all')) -TimeoutMs $cap -WorkingDirectory $scratch)}
        [IO.File]::Move($staged,$bundle)
        $cap=[Math]::Min(30000,$NetworkBudgetMs-[int]$watch.ElapsedMilliseconds);if($cap -le 0){throw 'deadline'}
        $after=@(Read-C1609Advertisement (Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('ls-remote',$OriginUrl,'refs/heads/*','refs/tags/*','refs/antiphon/salvage/*')) -TimeoutMs $cap -WorkingDirectory $scratch))
        if(($before -join [char]10) -cne ($after -join [char]10)){throw 'snapshot'}
        $completed=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        if([Math]::Abs(($completed-$now)*1000-$watch.ElapsedMilliseconds) -gt 60000){throw 'clock'}
        $bundleHash=(Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash.ToLowerInvariant()
        $nl=[string][char]10
        $advertisement=($after -join $nl)+$nl
        $advertisementHash=Get-C1008Sha256 $advertisement
        $header=@('c1008-host-proof 2',"operation $OperationId","origin $originHash","repo $RepoRelative",
            "created $now","nonce $PhaseNonce","inventory $InventoryDigest","bundle $bundleHash",
            "advertisement $advertisementHash",('count '+$after.Count),"completed $completed")
        $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($header -join $nl)+$nl+$advertisement)
        $file=[IO.File]::Open($proofFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try{$file.Write($bytes);$file.Flush($true)}finally{$file.Dispose()}
        [IO.File]::AppendAllText($LogPath,('repo='+$RepoRelative+' refs='+$after.Count+$nl),[Text.UTF8Encoding]::new($false))
    } catch {
        # No URL, credential or raw Git stderr crosses this boundary. The reason is a fixed token.
        $reason=[string]$_.Exception.Message
        $script:C1609HostProofReason=if($reason -cmatch '^RecycleGitAuditUnknown'){'git'}elseif($reason -cmatch '^[a-z][a-z-]{0,31}$'){$reason}else{'other'}
        # A failed attempt's bundle is never a proof; remove only the file this attempt wrote.
        if($bundleCreated -and $bundle -and [IO.File]::Exists($bundle) -and -not [IO.File]::Exists($proofFile)){
            try{[IO.File]::Delete($bundle)}catch{}
        }
        throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2)
    }
}

function Copy-C1609HostOriginProof {
    param([string]$Directory,[string]$SourceName,[string]$Repo,[string]$Origin)
    if($SourceName -cnotmatch '^[0-9a-f]{64}$' -or $Repo -cnotmatch '^[A-Za-z0-9._/-]+$'){throw 'RecycleGitAuditUnknown'}
    $source=Join-Path $Directory $SourceName
    $lines=[IO.File]::ReadAllLines($source)
    if($lines.Length -lt 12 -or $lines[0] -cne 'c1008-host-proof 2' -or
        $lines[2] -cne ('origin '+(Get-C1008Sha256 $Origin))){throw 'RecycleGitAuditUnknown'}
    $name=Get-C1008Sha256 ($Repo+[char]10+$Origin)
    $lines[3]='repo '+$Repo
    [IO.File]::Copy($source+'.bundle',(Join-Path $Directory ($name+'.bundle')),$false)
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($lines -join [char]10)+[char]10)
    $file=[IO.File]::Open((Join-Path $Directory $name),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$file.Write($bytes);$file.Flush($true)}finally{$file.Dispose()}
}
