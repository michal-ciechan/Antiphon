# CARD-1526 portable checker tests. No native execution is claimed.
param([switch]$PolicyOnly, [string]$Case = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$script:checks = 0
function Assert-That([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "ASSERT $Name" }
    $script:checks++
}
function Policy-Surface {
    $testing = Get-Content (Join-Path $repo 'docs/testing-and-build.md') -Raw
    Assert-That ($testing.Contains('## OS branch witnesses')) 'policy: missing OS-witness requirement'
    foreach ($file in @('server/Bundles/stage-code.md','server/Bundles/stage-review.md','docs/orchestration-loop.md')) {
        $text = Get-Content (Join-Path $repo $file) -Raw
        Assert-That ($text.Contains('docs/testing-and-build.md#os-branch-witnesses') -or $text.Contains('testing-and-build.md#os-branch-witnesses')) "policy: witness pointer $file"
    }
    foreach ($phrase in @('production factory/property','absent','empty','setupAssertion','syntax/inventory','CARD-1525','caller')) {
        Assert-That ($testing.Contains($phrase)) "policy: $phrase"
    }
    $release = Get-Content (Join-Path $repo 'docs/release-gates.md') -Raw
    foreach ($phrase in @('## Startup-path rc picks','rc-native-probe','rc-restart-rehearsal','[NullString]::Value',".Contains('ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT')",'C589_SLOT_SHIM','C589_COMMAND_SHIM','ANTIPHON_APPHOST_TEST_SEAMS','restore Antiphon.AppHost','exact integration candidate','/api/version')) {
        Assert-That ($release.Contains($phrase)) "policy: release $phrase"
    }
    $recovery = Get-Content (Join-Path $repo 'docs/apphost-runbook.md') -Raw
    foreach ($phrase in @('git revert --no-edit <exact-bad-pick-sha>','newest first','unactivated','infrastructure-only')) {
        Assert-That ($recovery.Contains($phrase)) "policy: recovery $phrase"
    }
    Write-Host 'PASS policy-surface'
}
if ($PolicyOnly) { Policy-Surface; Write-Host "ASSERTIONS $script:checks"; exit 0 }
$root = Join-Path ([IO.Path]::GetTempPath()) ('c1526-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
function Fixture-Git([string[]]$Arguments) {
    $out = & git -C $root @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "fixture git failed: $out" }
    return ($out -join "`n")
}
function Put([string]$Path, [string]$Text) {
    $full = Join-Path $root $Path
    [IO.Directory]::CreateDirectory((Split-Path $full -Parent)) | Out-Null
    [IO.File]::WriteAllText($full, $Text)
}
function Copy-Record($Value) { return ($Value | ConvertTo-Json -Depth 30 | ConvertFrom-Json -AsHashtable) }
function Run-Case([string]$Name, $Evidence, [int]$Exit, [string]$Diagnostic, [string]$Head = $script:head, [switch]$Strict) {
    if ($Case -and $Case -ne $Name) { return }
    $path = Join-Path $root 'evidence.json'
    $Evidence | ConvertTo-Json -Depth 30 | Set-Content $path
    $psi = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'pwsh'))
    $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $checker = Join-Path $repo 'scripts/check-os-witnesses.ps1'
    if ($Strict) {
        # Invoke the same checker in a child scope inheriting StrictMode, with literal argv.
        Put 'strict-checker.ps1' 'param($Checker, $Repository, $BaseRef, $HeadRef, $EvidencePath, $Plan); Set-StrictMode -Version Latest; & $Checker -Repository $Repository -BaseRef $BaseRef -HeadRef $HeadRef -EvidencePath $EvidencePath -Plan $Plan; exit $LASTEXITCODE'
        foreach ($arg in @('-NoProfile','-File',(Join-Path $root 'strict-checker.ps1'),$checker)) { $psi.ArgumentList.Add($arg) }
    } else {
        foreach ($arg in @('-NoProfile','-File',$checker)) { $psi.ArgumentList.Add($arg) }
    }
    foreach ($arg in @('-Repository',$root,'-BaseRef',$script:base,'-HeadRef',$Head,'-EvidencePath',$path,'-Plan','plan.md')) { $psi.ArgumentList.Add($arg) }
    $p = [Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEndAsync(); $stderr = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit(15000)) { $p.Kill($true); $p.WaitForExit(); throw "timeout: $Name" }
    $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    Assert-That ($p.ExitCode -eq $Exit) "$Name exit expected=$Exit actual=$($p.ExitCode): $output"
    Assert-That ($output.Contains($Diagnostic)) "$Name diagnostic=$Diagnostic actual=$output"
    $p.Dispose()
    Write-Host "PASS $Name"
}
try {
    Fixture-Git @('init','-q') | Out-Null
    Fixture-Git @('config','user.email','fixture@example.invalid') | Out-Null
    Fixture-Git @('config','user.name','Fixture') | Out-Null
    Put '.gitignore' "evidence.json`nstrict-checker.ps1"
    Put 'plan.md' "### Checkpoints`n`n| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |`n|---|---|---|---|---|---|---|---|---|`n| CP-1 | all | n/a | fixture | pwsh test.ps1 | V-1 | pass | n/a | 1 |`n"
    Put 'source.cs' "// base`n"
    # These files are lint inputs, not execution receipts. Pin real code shapes in Git.
    $realTest = @'
using System.Diagnostics;
class WitnessTests {
    [Test] public void AbsentOptOut() {
        var environment = new ProcessStartInfo().EnvironmentVariables;
        environment.Remove("OPT_OUT");
        Assert.False(environment.ContainsKey("OPT_OUT"));
        ReadOptional(environment, windows: true);
    }
    [Test] public void EmptyOptOut() {
        var environment = new ProcessStartInfo().EnvironmentVariables;
        environment["OPT_OUT"] = "";
        Assert.Equal("", environment["OPT_OUT"]);
        ReadOptional(environment, windows: true);
    }
    [Test] public void ValueOptOut() {
        var environment = new ProcessStartInfo().EnvironmentVariables;
        environment["OPT_OUT"] = "1";
        Assert.Equal("1", environment["OPT_OUT"]);
        ReadOptional(environment, windows: true);
    }
}
'@
    Put 'test.cs' $realTest
    Put 'seam.cs' ($realTest.Replace('new ProcessStartInfo().EnvironmentVariables','new StringDictionary()'))
    Put 'assigned.cs' ($realTest.Replace('environment.Remove("OPT_OUT");','environment["OPT_OUT"] = "";').Replace('Assert.False(environment.ContainsKey("OPT_OUT"));','Assert.Equal("", environment["OPT_OUT"]);'))
    Put 'comment-only.cs' '// fixture with no test or runtime object'
    Put 'comment-strings.cs' ("/*`n$realTest`n*/")
    Put 'line-comments.cs' (($realTest -split "`n" | ForEach-Object { '// ' + $_ }) -join "`n")
    Put 'hash-comments.ps1' (($realTest -split "`n" | ForEach-Object { '# ' + $_ }) -join "`n")
    Put 'unterminated-csharp.cs' ("/*`n$realTest")
    Put 'unterminated-powershell.ps1' ("<#`n$realTest")
    Put 'nested-powershell.ps1' ("<# outer`n<# inner #>`n$realTest`n#>")
    Put 'nested-unclosed-powershell.ps1' ("<# outer`n<# inner #>`n$realTest")
    Put 'closed-powershell.ps1' ("<#`n$realTest`n#>")
    Put 'csharp-nonnesting.cs' ("/* nested opener /* is only text */`n$realTest")
    Put 'string-delimiters.cs' ('class Strings { string slash = "/*"; string hash = "<#"; string verbatim = @"/* <#"; }' + "`n$realTest")
    Put 'string-delimiters.ps1' ('$slash = ''/*''; $hash = "<#"' + "`nfunction AbsentOptOut {`n" + $realTest + "`n}")
    Put 'escaped-strings.cs' ('class Strings { string text = "escaped \" /* <#"; string verbatim = @"doubled "" /* <#"; }' + "`n$realTest")
    Put 'escaped-strings.ps1' ('$single = ''doubled '''' /* <#''; $double = "escaped `" /* <#"' + "`nfunction AbsentOptOut {`n" + $realTest + "`n}")
    Put 'unterminated-string.cs' ('var text = "/* <#' + "`n$realTest")
    Put 'unterminated-comment-after-code.cs' ($realTest + "`n/*")
    Put 'factory-missing.cs' ($realTest.Replace('new ProcessStartInfo().EnvironmentVariables','new ProcessStartInfo().Environment'))
    Put 'method-call.cs' ($realTest.Replace('[Test] public void AbsentOptOut() {','[Test] public void AnotherCase() { AbsentOptOut();'))
    Put 'method-prefix.cs' ($realTest.Replace('AbsentOptOut()', 'AbsentOptOutExtra()'))
    Put 'helper.cs' 'if (OperatingSystem.IsWindows()) ReadOptional();'
    Put 'scripts/lib/start.cs' 'Start();'
    Fixture-Git @('add','.') | Out-Null; Fixture-Git @('commit','-qm','base') | Out-Null
    $script:base = Fixture-Git @('rev-parse','HEAD')
    Put 'source.cs' 'if (OperatingSystem.IsWindows()) ReadOptional();'
    Fixture-Git @('add','.') | Out-Null; Fixture-Git @('commit','-qm','head') | Out-Null
    $script:head = Fixture-Git @('rev-parse','HEAD')
    $witness = @{
        id='W-1'; sourcePath='source.cs'; branchAnchor='OperatingSystem.IsWindows()'; targetOS='Windows'
        witnessKind='real-type'; productionType='ProcessStartInfo'; factory='new ProcessStartInfo().EnvironmentVariables'
        testPath='test.cs'; testCase='AbsentOptOut'; setup='Remove the key from the real property'; outcome='policy applies without exception'
        executionMode='portable-forced'; checkpointId='CP-1'; nativeObligation=@{status='not-required';reason='existing OS parameter is forceable'}
        input=@{kind='optional-config';key='OPT_OUT';cases=@(
            @{state='absent';keyPresent=$false;setupAssertion='Assert.False(environment.ContainsKey("OPT_OUT"))'},
            @{state='empty';keyPresent=$true;value='';setupAssertion='Assert.Equal("", environment["OPT_OUT"])'},
            @{state='value';keyPresent=$true;value='1';setupAssertion='Assert.Equal("1", environment["OPT_OUT"])'})}
    }
    $valid = @{version=1;baseSha=$script:base;headSha=$script:head;inspectedPaths=@(@{
        path='source.cs';disposition='os-gated';reason='changed optional read';helpers=@();candidates=@(
            @{revision='head';line=1;disposition='witness';witnessId='W-1'})
    });witnesses=@($witness);restartImpact=@{required=$false;paths=@();additionalPaths=@();reason='no startup path'}}
    Run-Case 'forced-real-type' $valid 0 'complete syntax/inventory'
    Put 'source.cs' '// unstaged source must not hide the pinned candidate'
    Run-Case 'pinned-source' $valid 0 'CANDIDATE head source.cs:1'
    Put 'source.cs' 'if (OperatingSystem.IsWindows()) ReadOptional();'
    Run-Case 'real-absent-test' $valid 0 'complete syntax/inventory'
    Run-Case 'strict-valid-absent' $valid 0 'complete syntax/inventory' -Strict
    foreach ($row in @(
        @('test-unterminated-csharp-block','unterminated-csharp.cs',2,'unterminated /* block comment'),
        @('test-unterminated-powershell-block','unterminated-powershell.ps1',2,'unterminated <# block comment'),
        @('test-nested-powershell-block','nested-powershell.ps1',1,'testCase declaration missing'),
        @('test-nested-powershell-inner-close-only','nested-unclosed-powershell.ps1',2,'unterminated <# block comment'),
        @('test-closed-powershell-block','closed-powershell.ps1',1,'testCase declaration missing'),
        @('test-csharp-block-does-not-nest','csharp-nonnesting.cs',0,'complete syntax/inventory'),
        @('test-csharp-string-comment-delimiters','string-delimiters.cs',0,'complete syntax/inventory'),
        @('test-powershell-string-comment-delimiters','string-delimiters.ps1',0,'complete syntax/inventory'),
        @('test-csharp-escaped-string-delimiters','escaped-strings.cs',0,'complete syntax/inventory'),
        @('test-powershell-escaped-string-delimiters','escaped-strings.ps1',0,'complete syntax/inventory'),
        @('test-unterminated-string','unterminated-string.cs',2,'unterminated string literal'),
        @('test-unterminated-block-after-code','unterminated-comment-after-code.cs',2,'unterminated /* block comment')
    )) {
        $e=Copy-Record $valid; $e.witnesses[0].testPath=$row[1]
        Run-Case $row[0] $e $row[2] $row[3]
    }
    foreach ($row in @(
        @('test-seam-only','seam.cs','productionType missing from test code'),
        @('test-always-assigned','assigned.cs','absent setupAssertion missing from test code'),
        @('test-comment-only','comment-only.cs','testCase declaration missing'),
        @('test-comment-strings','comment-strings.cs','testCase declaration missing'),
        @('test-line-comment-strings','line-comments.cs','testCase declaration missing'),
        @('test-hash-comment-strings','hash-comments.ps1','testCase declaration missing'),
        @('test-factory-missing','factory-missing.cs','factory missing from test code'),
        @('test-name-call-only','method-call.cs','testCase declaration missing'),
        @('test-name-prefix','method-prefix.cs','testCase declaration missing')
    )) {
        $e=Copy-Record $valid; $e.witnesses[0].testPath=$row[1]
        Run-Case $row[0] $e 1 $row[2]
    }
    $e=Copy-Record $valid; $e.witnesses[0].testCase='MissingOptOut'
    Run-Case 'test-case-missing' $e 1 'testCase declaration missing'
    Put 'test.cs' '// uncommitted deletion of witness must not replace the pinned test'
    Run-Case 'pinned-test-ignores-worktree' $valid 0 'complete syntax/inventory'
    Put 'test.cs' $realTest
    Put 'seam.cs' $realTest
    $e=Copy-Record $valid; $e.witnesses[0].testPath='seam.cs'
    Run-Case 'pinned-test-rejects-worktree-repair' $e 1 'productionType missing from test code'
    Put 'seam.cs' ($realTest.Replace('new ProcessStartInfo().EnvironmentVariables','new StringDictionary()'))
    $e=Copy-Record $valid; $e.inspectedPaths='not-an-array'
    Run-Case 'malformed-record' $e 2 'invalid array'
    $e=Copy-Record $valid; $e.witnesses[0].testPath='../outside.ps1'
    Run-Case 'unsafe-path' $e 1 'invalid path'
    $e=Copy-Record $valid; $e.witnesses[0].witnessKind='seam'
    Run-Case 'seam-only' $e 1 'real-type required'
    $e=Copy-Record $valid; $e.witnesses[0].executionMode='native'; $e.witnesses[0].nativeObligation=@{status='pending';targetOS='Windows';sourceSha=$script:head;command='native test';reason='native API'}
    Run-Case 'native-pending' $e 0 'NATIVE pending'
    $e=Copy-Record $valid; $e.witnesses=@()
    Run-Case 'missing-witness' $e 1 'missing witness'
    $e=Copy-Record $valid; $e.witnesses[0].input.cases[0].keyPresent=$true; $e.witnesses[0].input.cases[0].value=''
    Run-Case 'empty-is-not-absent' $e 1 'absent requires removal'
    $e=Copy-Record $valid; $e.witnesses[0].input.cases=@($e.witnesses[0].input.cases[1],$e.witnesses[0].input.cases[2])
    Run-Case 'absent-case-required' $e 1 'absent, empty and value'
    $e=Copy-Record $valid; $e.inspectedPaths[0].candidates=@()
    Run-Case 'candidate-mismatch' $e 1 'candidate unclassified'
    $e=Copy-Record $valid; $e.inspectedPaths[0].disposition='portable'
    Run-Case 'candidate-contradiction' $e 1 'witness requires os-gated'
    $e=Copy-Record $valid; $e.inspectedPaths[0].disposition='portable'; $e.inspectedPaths[0].candidates[0]=@{revision='head';line=1;disposition='non-behavior';reason='unchanged guard, changed comment only'}; $e.witnesses=@()
    Run-Case 'candidate-reason' $e 0 'CANDIDATE head source.cs:1'
    $e=Copy-Record $valid; $e.inspectedPaths[0].helpers=@('helper.cs')
    Run-Case 'helper-missing' $e 1 'helper not inspected'
    $e.inspectedPaths+=@{path='helper.cs';disposition='os-gated';reason='called by changed code';helpers=@();candidates=@(@{revision='base';line=1;disposition='witness';witnessId='W-2'},@{revision='head';line=1;disposition='witness';witnessId='W-2'})}
    $w=Copy-Record $witness; $w.id='W-2'; $w.sourcePath='helper.cs'; $e.witnesses+= $w
    Run-Case 'helper-complete' $e 0 'CANDIDATE base helper.cs:1'
    $e=Copy-Record $valid; $e.headSha=('f'*40)
    Run-Case 'sha-mismatch' $e 2 'SHA mismatch'
    Run-Case 'unresolved-ref' $valid 2 'unresolved commit' ('f'*40)
    $e=Copy-Record $valid; $e.witnesses[0].testPath='missing.ps1'
    Run-Case 'missing-path' $e 1 'path missing'
    $e=Copy-Record $valid; $e.witnesses[0].checkpointId='CP-999'
    Run-Case 'missing-checkpoint' $e 1 'checkpoint missing'
    $e=Copy-Record $valid; $e.inspectedPaths+=Copy-Record $e.inspectedPaths[0]
    Run-Case 'duplicate-inventory' $e 1 'duplicate inventory'
    $e=Copy-Record $valid; $e.witnesses[0].nativeObligation=@{status='recorded';targetOS='Windows';sourceSha=$script:head;command='native test';receiptReference='fixture.json';executionMode='portable-forced'}
    Run-Case 'fixture-native' $e 1 'native receipt requires native execution'
    # Rename and delete are separate committed deltas; no worktree source is used by the checker.
    Fixture-Git @('mv','scripts/lib/start.cs','moved.cs') | Out-Null
    Fixture-Git @('commit','-qam','rename') | Out-Null
    $rename=Fixture-Git @('rev-parse','HEAD')
    $e=Copy-Record $valid; $e.headSha=$rename; $e.inspectedPaths+=@{path='scripts/lib/start.cs';disposition='removed';reason='renamed';helpers=@();candidates=@()},@{path='moved.cs';disposition='portable';reason='renamed startup';helpers=@();candidates=@()}
    Run-Case 'restart-rename-missing' $e 1 'restart path census' $rename
    $e.restartImpact=@{required=$true;paths=@('scripts/lib/start.cs');additionalPaths=@();reason='renamed startup';nativeObligation=@{status='pending';targetOS='Windows';sourceSha=$rename;command='rc-native-probe';reason='future native integration'}}
    Run-Case 'restart-rename-pending' $e 0 'NATIVE pending' $rename
    Fixture-Git @('rm','moved.cs') | Out-Null; Fixture-Git @('commit','-qm','delete') | Out-Null
    $deleted=Fixture-Git @('rev-parse','HEAD'); $e.headSha=$deleted; $e.restartImpact.nativeObligation.sourceSha=$deleted
    $e.inspectedPaths=@($e.inspectedPaths | Where-Object path -ne 'moved.cs')
    Run-Case 'restart-delete' $e 0 'complete syntax/inventory' $deleted
    Put 'unknown.widget' 'unrecognized executable/configuration candidate'
    Fixture-Git @('add','.') | Out-Null; Fixture-Git @('commit','-qm','unknown') | Out-Null
    $unknown=Fixture-Git @('rev-parse','HEAD'); $e.headSha=$unknown; $e.restartImpact.nativeObligation.sourceSha=$unknown
    Run-Case 'unknown-classification' $e 1 'inventory missing: unknown.widget' $unknown
    # Documentation-only uses a second base at the current commit.
    $script:base=$unknown; Put 'readme.md' '# documentation'
    Fixture-Git @('add','readme.md') | Out-Null; Fixture-Git @('commit','-qm','docs') | Out-Null
    $docs=Fixture-Git @('rev-parse','HEAD')
    $e=@{version=1;baseSha=$script:base;headSha=$docs;inspectedPaths=@();witnesses=@();restartImpact=@{required=$false;paths=@();additionalPaths=@();reason='documentation-only readme.md'}}
    Run-Case 'documentation-only' $e 0 'complete syntax/inventory' $docs
    if (-not $Case) { Policy-Surface }
    if ($script:checks -eq 0) { throw "unknown case: $Case" }
    Write-Host "ASSERTIONS $script:checks"
} finally {
    # Only the private, freshly created direct child of the system temp root is removed.
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ($root -and (Split-Path $root -Parent) -eq $parent -and (Split-Path $root -Leaf) -match '^c1526-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
