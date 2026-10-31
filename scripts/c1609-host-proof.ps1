# CARD-1609. Versioned origin-only proof writer. Dot-source only.
function Invoke-C1609HostGit {
    param([string]$Repo, [string[]]$GitArgs, [int]$TimeoutMs=30000, [string]$WorkingDirectory='')
    $psi=[Diagnostics.ProcessStartInfo]::new()
    $psi.FileName='git'; $psi.UseShellExecute=$false
    $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    if ($WorkingDirectory) { $psi.WorkingDirectory=$WorkingDirectory }
    foreach($key in @('GIT_DIR','GIT_WORK_TREE','GIT_INDEX_FILE','GIT_OBJECT_DIRECTORY','GIT_ALTERNATE_OBJECT_DIRECTORIES','GIT_CONFIG_PARAMETERS','GIT_CONFIG_COUNT')) {
        [void]$psi.Environment.Remove($key)
    }
    $psi.Environment['GIT_TERMINAL_PROMPT']='0'
    foreach($arg in @('-c','core.hooksPath=/dev/null','-c','protocol.allow=never','-c','protocol.https.allow=always',
        '-c','protocol.ssh.allow=always','-c','http.followRedirects=false','-c','credential.interactive=false')+$GitArgs) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $proc=[Diagnostics.Process]::new(); $proc.StartInfo=$psi; $started=$false
    $watch=[Diagnostics.Stopwatch]::StartNew(); $output=[Text.StringBuilder]::new()
    try {
        [void]$proc.Start(); $started=$true
        $streams=@(
            @{reader=$proc.StandardOutput;buffer=[char[]]::new(4096);done=$false;task=$null;count=0;keep=$true},
            @{reader=$proc.StandardError;buffer=[char[]]::new(4096);done=$false;task=$null;count=0;keep=$false})
        foreach($s in $streams){$s.task=$s.reader.ReadAsync($s.buffer,0,$s.buffer.Length)}
        while(@($streams|Where-Object{-not $_.done}).Count){
            if($watch.ElapsedMilliseconds -ge $TimeoutMs){throw 'deadline'}
            foreach($s in $streams){
                if($s.done -or -not $s.task.IsCompleted){continue}
                $n=$s.task.GetAwaiter().GetResult()
                if($n -eq 0){$s.done=$true;continue}
                $s.count+=$n
                if($s.count -gt 8388608){throw 'output-bound'}
                if($s.keep){[void]$output.Append($s.buffer,0,$n)}
                $s.task=$s.reader.ReadAsync($s.buffer,0,$s.buffer.Length)
            }
            [Threading.Thread]::Sleep(5)
        }
        $remaining=[Math]::Max(1,$TimeoutMs-[int]$watch.ElapsedMilliseconds)
        if(-not $proc.WaitForExit($remaining) -or $proc.ExitCode -ne 0){throw 'git'}
        return $output.ToString()
    } catch {
        if($started -and -not $proc.HasExited){$proc.Kill($true);[void]$proc.WaitForExit(2000)}
        throw (Get-C1008HostLsRemoteDiagnosis -Repo $Repo -Status 2)
    } finally {$proc.Dispose()}
}

function Read-C1609Advertisement([string]$Text) {
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $rows=[Collections.Generic.List[string]]::new()
    $lines=$Text.Split([char]10)
    for($i=0;$i -lt $lines.Length;$i++){
        $line=$lines[$i].TrimEnd([char]13)
        if($line -eq '' -and $i -eq $lines.Length-1){continue}
        if($line -cnotmatch '^[0-9a-f]{40}\trefs/(heads|tags)/[^\s]+$'){throw 'advertisement'}
        $ref=$line.Substring(41)
        if(-not $seen.Add($ref)){throw 'advertisement'}
        if($line.ToLowerInvariant() -match 'gh[pousr]_|github_pat_|token|password|secret'){throw 'advertisement'}
        $rows.Add($line)
    }
    if($rows.Count -eq 0 -or @($rows|Where-Object{$_ -cmatch '\trefs/heads/'}).Count -eq 0){throw 'advertisement'}
    $array=$rows.ToArray(); [Array]::Sort($array,[StringComparer]::Ordinal)
    return $array
}

function Write-C1609HostOriginProof {
    param([string]$OriginUrl,[string]$RepoRelative,[string]$OperationId,[string]$ProofDirectory,
        [long]$CreatedUnix,[string]$LogPath,[string]$PhaseNonce,[string]$InventoryDigest,[string[]]$ApprovedOrigins,
        [int]$NetworkBudgetMs=600000)
    $scratch=''; $bundle=''
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
        $scratch=Join-Path ([IO.Path]::GetTempPath()) ('c1609-host-'+[guid]::NewGuid().ToString('N'))
        [void][IO.Directory]::CreateDirectory($scratch)
        $originHash=Get-C1008Sha256 $OriginUrl
        $name=Get-C1008Sha256 ($RepoRelative+[char]10+$OriginUrl)
        $bundle=Join-Path $ProofDirectory ($name+'.bundle')
        $proofFile=Join-Path $ProofDirectory $name
        if([IO.File]::Exists($bundle) -or [IO.File]::Exists($proofFile)){throw 'existing'}
        [void](Invoke-C1609HostGit -Repo $RepoRelative -GitArgs @('init','-q','--bare','--template=',$scratch) -WorkingDirectory $scratch)
        $common=@('--git-dir',$scratch)
        $cap=[Math]::Min(30000,$NetworkBudgetMs-[int]$watch.ElapsedMilliseconds);if($cap -le 0){throw 'deadline'}
        $before=@(Read-C1609Advertisement (Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('ls-remote','--heads','--tags',$OriginUrl)) -TimeoutMs $cap -WorkingDirectory $scratch))
        $cap=[Math]::Min(300000,$NetworkBudgetMs-[int]$watch.ElapsedMilliseconds);if($cap -le 0){throw 'deadline'}
        [void](Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('fetch','-q','--no-tags','--no-write-fetch-head',
            '--no-recurse-submodules',$OriginUrl,'+refs/heads/*:refs/heads/*','+refs/tags/*:refs/tags/*')) -TimeoutMs $cap -WorkingDirectory $scratch)
        $actual=Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('show-ref','--dereference')) -WorkingDirectory $scratch
        $actual=@(Read-C1609Advertisement ($actual.Replace(' ',[string][char]9)))
        if(($before -join [char]10) -cne ($actual -join [char]10)){throw 'snapshot'}
        $cap=[Math]::Min(300000,$NetworkBudgetMs-[int]$watch.ElapsedMilliseconds);if($cap -le 0){throw 'deadline'}
        [void](Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('bundle','create',$bundle,'--all')) -TimeoutMs $cap -WorkingDirectory $scratch)
        $cap=[Math]::Min(30000,$NetworkBudgetMs-[int]$watch.ElapsedMilliseconds);if($cap -le 0){throw 'deadline'}
        $after=@(Read-C1609Advertisement (Invoke-C1609HostGit -Repo $RepoRelative -GitArgs ($common+@('ls-remote','--heads','--tags',$OriginUrl)) -TimeoutMs $cap -WorkingDirectory $scratch))
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
        # No URL, credential or raw Git stderr crosses this boundary.
        throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2)
    } finally {
        if($scratch -and [IO.Directory]::Exists($scratch)){Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue}
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
