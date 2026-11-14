using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Timeout(600_000)]
[Category("Unit")]
public sealed class VolumeRetirementHostProofTests
{
    private const string Operation = "c100800000000000000000000000000000001";
    private const string Repo = "repos/school-revision";

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Host_proves_complete_origin_ancestry()
    {
        var root = Directory.CreateTempSubdirectory("c1609-h1-").FullName;
        try
        {
            var origin = Path.Combine(root, "origin.git");
            var work = Path.Combine(root, "work");
            var setup = await Git(root, "init", "-q", "--bare", origin);
            setup.Exit.ShouldBe(0, setup.Output);
            (await Git(root, "init", "-q", "-b", "main", work)).Exit.ShouldBe(0);
            (await Git(work, "config", "user.name", "Fixture")).Exit.ShouldBe(0);
            (await Git(work, "config", "user.email", "fixture@example.invalid")).Exit.ShouldBe(0);
            await File.WriteAllTextAsync(Path.Combine(work, "sentinel"), "published\n");
            (await Git(work, "add", "sentinel")).Exit.ShouldBe(0);
            (await Git(work, "commit", "-qm", "published")).Exit.ShouldBe(0);
            (await Git(work, "remote", "add", "origin", origin)).Exit.ShouldBe(0);
            (await Git(work, "push", "-q", "origin", "main")).Exit.ShouldBe(0);
            var tip = (await Git(work, "rev-parse", "HEAD")).Output.Trim();
            var proof = Path.Combine(root, "proof");
            Directory.CreateDirectory(proof);
            var log = Path.Combine(root, "proof.log");
            var wrote = await Proof(origin, proof, log);
            wrote.Exit.ShouldBe(0, "c1609-h1: " + wrote.Output);
            var proofFile = Directory.GetFiles(proof).Single(path => !path.EndsWith(".bundle", StringComparison.Ordinal));
            var body = await File.ReadAllTextAsync(proofFile);
            body.ShouldContain("c1008-host-proof 1");
            body.ShouldContain("operation " + Operation);
            body.ShouldContain(tip + "\trefs/heads/main");
            body.ShouldNotContain(origin);
            var bundle = Directory.GetFiles(proof).Single(path => path.EndsWith(".bundle", StringComparison.Ordinal));
            var heads = await Git(root, "bundle", "list-heads", bundle);
            heads.Exit.ShouldBe(0, heads.Output);
            heads.Output.ShouldContain(tip);
            heads.Output.ShouldContain("refs/heads/main");

            // One seed supplies all renewal controls; every producer invocation
            // still owns a fresh origin-only proof repository and output.
            (await Git(work, "tag", "-a", "published", "-m", "published", tip)).Exit.ShouldBe(0);
            await File.WriteAllTextAsync(Path.Combine(work, "sentinel"), "advanced\n");
            (await Git(work, "add", "sentinel")).Exit.ShouldBe(0);
            (await Git(work, "commit", "-qm", "advanced")).Exit.ShouldBe(0);
            var publishedTree = (await Git(work, "rev-parse", "HEAD^{tree}")).Output.Trim();
            var publishedBlob = (await Git(work, "rev-parse", "HEAD:sentinel")).Output.Trim();
            var publishedTag = (await Git(work, "rev-parse", "refs/tags/published")).Output.Trim();
            (await Git(work, "update-ref", "refs/tags/published-blob", publishedBlob)).Exit.ShouldBe(0);
            (await Git(work, "push", "-q", "origin", "main", "--tags")).Exit.ShouldBe(0);
            await File.WriteAllTextAsync(Path.Combine(work, "sentinel"), "unique unpublished bytes\n");
            (await Git(work, "add", "sentinel")).Exit.ShouldBe(0);
            (await Git(work, "commit", "-qm", "unpublished")).Exit.ShouldBe(0);
            (await Git(work, "tag", "-a", "private", "-m", "private")).Exit.ShouldBe(0);
            var child = (await Git(work, "rev-parse", "HEAD")).Output.Trim();
            var privateTree = (await Git(work, "rev-parse", "HEAD^{tree}")).Output.Trim();
            var privateBlob = (await Git(work, "rev-parse", "HEAD:sentinel")).Output.Trim();
            var privateTag = (await Git(work, "rev-parse", "refs/tags/private")).Output.Trim();
            foreach (var (label, oid, type, admitted) in new[]
            {
                ("ancestor", tip, "commit", true), ("unpublished-commit", child, "commit", false),
                ("reachable-tree", publishedTree, "tree", true), ("unpublished-tree", privateTree, "tree", false),
                ("advertised-blob", publishedBlob, "blob", true), ("unpublished-blob", privateBlob, "blob", false),
                ("advertised-tag", publishedTag, "tag", true), ("unpublished-tag", privateTag, "tag", false),
                ("wrong-type", tip, "blob", false), ("missing", new string('f', 40), "commit", false)
            })
            {
                var destination = Path.Combine(root, label); Directory.CreateDirectory(destination);
                var request = System.Text.Json.JsonSerializer.Serialize(new[] { new { objectId = oid, type } });
                var command = "$ErrorActionPreference='Stop'; . " + Quote(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-host-proof.ps1")) + "; " +
                    "$original=${function:Invoke-C1609HostGit}; " +
                    "function Invoke-C1609HostGit { param($Repo,[string[]]$GitArgs,[int]$TimeoutMs=30000,[string]$WorkingDirectory='', $InputText=$null, [string]$OutputFile='') " +
                    "$mapped=@('-c','protocol.file.allow=always')+@($GitArgs|ForEach-Object {if($_ -ceq 'https://fixture.invalid/renewal.git'){$env:C1609_BARE}else{$_}}); " +
                    "$io=@{};if($null -ne $InputText){$io.InputText=$InputText};if($OutputFile){$io.OutputFile=$OutputFile}; " +
                    "& $original -Repo $Repo -GitArgs $mapped -TimeoutMs $TimeoutMs -WorkingDirectory $WorkingDirectory @io }; " +
                    "$required=@{};if((Get-Command Write-C1008HostOriginProof).Parameters.ContainsKey('RequiredObjects')) { " +
                    "$required.RequiredObjects=@($env:C1609_REQUIRED|ConvertFrom-Json) }; " +
                    "Write-C1008HostOriginProof -OriginUrl 'https://fixture.invalid/renewal.git' -RepoRelative '" + Repo + "' -OperationId '" + Operation +
                    "' -ProofVersion 2 -PhaseNonce ('b'*32) -InventoryDigest ('c'*64) -ApprovedOrigins @('https://fixture.invalid/renewal.git') " +
                    "-ProofDirectory $env:C1609_PROOF -CreatedUnix ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -LogPath $env:C1609_LOG @required";
                var renewal = await Pwsh(command, new[] { ("C1609_BARE", origin), ("C1609_PROOF", destination),
                    ("C1609_LOG", Path.Combine(root, "renewal.log")), ("C1609_REQUIRED", request) });
                if (admitted) renewal.Exit.ShouldBe(0, "H1 fresh origin graph proves " + label + "; " + renewal.Output);
                else
                {
                    renewal.Exit.ShouldNotBe(0, "H1 renewal must refuse " + label + " rather than merely refresh a timestamp; " + renewal.Output);
                    Directory.GetFiles(destination).Any(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), "^[0-9a-f]{64}$"))
                        .ShouldBeFalse("H1 a refused required object cannot publish an authorizing proof header");
                }
            }
        }
        finally { DeleteOwned(root, "c1609-h1-"); }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Private_and_public_origins_share_offline_gate()
    {
        foreach (var name in new[] { "private.git", "public.git" })
        {
            var root = Directory.CreateTempSubdirectory("c1609-h2-").FullName;
            try
            {
                var origin = Path.Combine(root, name);
                var work = Path.Combine(root, "work");
                (await Git(root, "init", "-q", "--bare", origin)).Exit.ShouldBe(0);
                (await Git(root, "init", "-q", "-b", "main", work)).Exit.ShouldBe(0);
                (await Git(work, "config", "user.name", "Fixture")).Exit.ShouldBe(0);
                (await Git(work, "config", "user.email", "fixture@example.invalid")).Exit.ShouldBe(0);
                await File.WriteAllTextAsync(Path.Combine(work, "sentinel"), name + "\n");
                (await Git(work, "add", "sentinel")).Exit.ShouldBe(0);
                (await Git(work, "commit", "-qm", "published")).Exit.ShouldBe(0);
                (await Git(work, "remote", "add", "origin", origin)).Exit.ShouldBe(0);
                (await Git(work, "push", "-q", "origin", "main")).Exit.ShouldBe(0);
                var proof = Path.Combine(root, "proof");
                Directory.CreateDirectory(proof);
                var wrote = await Proof(origin, proof, Path.Combine(root, "proof.log"));
                wrote.Exit.ShouldBe(0, name + ": " + wrote.Output);
                var text = string.Join('\n', Directory.GetFiles(proof).Select(File.ReadAllText));
                text.ShouldContain("refs/heads/main");
                text.ShouldNotContain("ghp_");
                text.ShouldNotContain("password");
                wrote.Output.ShouldNotContain("ghp_");
            }
            finally { DeleteOwned(root, "c1609-h2-"); }
        }

        var refuseRoot = Directory.CreateTempSubdirectory("c1609-h2-refuse-").FullName;
        try
        {
            // Userinfo is stripped first. The remaining token needle must refuse before git runs.
            var refused = await Proof("https://user:ghp_PRIVATE_SENTINEL@example.com/token.git",
                refuseRoot, Path.Combine(refuseRoot, "proof.log"));
            refused.Exit.ShouldNotBe(0, refused.Output);
            refused.Output.ShouldContain("RecycleGitAuditUnknown");
            refused.Output.ShouldNotContain("ghp_PRIVATE_SENTINEL");
            Directory.GetFiles(refuseRoot).ShouldBeEmpty();
        }
        finally { DeleteOwned(refuseRoot, "c1609-h2-refuse-"); }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Unknown_remote_and_object_failures_refuse()
    {
        var root = Directory.CreateTempSubdirectory("c1609-h3-").FullName;
        try
        {
            var proof = Path.Combine(root, "proof");
            Directory.CreateDirectory(proof);
            var missing = Path.Combine(root, "missing.git");
            var run = await Proof(missing, proof, Path.Combine(root, "proof.log"));
            run.Exit.ShouldNotBe(0, run.Output);
            run.Output.ShouldContain("RecycleGitAuditUnknown");
            run.Output.ShouldNotContain("fatal:");
            Directory.GetFiles(proof).ShouldBeEmpty();
        }
        finally { DeleteOwned(root, "c1609-h3-"); }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Proof_coverage_binding_and_freshness_are_exact()
    {
        var origin = "https://example.com/repo.git";
        var hash = Sha(origin);
        var name = Sha(Repo + "\n" + origin);
        var line = "C1008_PRIVATE_ORIGIN repo=" + Repo + " origin=" + origin + " originHash=" + hash + " proofName=" + name;
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-host-proof.ps1");
        var command = "$ErrorActionPreference='Stop'; . " + Quote(script) + "; " +
            "$text = $env:C1609_LINES; " +
            "try { $rows = @(Read-C1008PrivateOriginLines -Text $text); Write-Output ('rows=' + $rows.Count) } " +
            "catch { Write-Output ('refused=' + $_.Exception.Message); exit 2 }";
        var one = await Pwsh(command, new[] { ("C1609_LINES", line + "\n") });
        one.Exit.ShouldBe(0, one.Output);
        one.Output.ShouldContain("rows=1");
        var duplicate = await Pwsh(command, new[] { ("C1609_LINES", line + "\n" + line + "\n") });
        duplicate.Exit.ShouldBe(2, duplicate.Output);
        duplicate.Output.ShouldContain("RecycleGitAuditUnknown");
        var wrong = await Pwsh(command, new[] { ("C1609_LINES", line.Replace(hash, new string('0', 64), StringComparison.Ordinal) + "\n") });
        wrong.Exit.ShouldBe(2, wrong.Output);
        wrong.Output.ShouldContain("RecycleGitAuditUnknown");

        var proof = Directory.CreateTempSubdirectory("c1609-h4-").FullName;
        try
        {
            var bad = await Proof(origin, proof, Path.Combine(proof, "proof.log"), "not-an-operation");
            bad.Exit.ShouldNotBe(0, bad.Output);
            bad.Output.ShouldContain("RecycleGitAuditUnknown");
            Directory.GetFiles(proof).ShouldBeEmpty();
        }
        finally { DeleteOwned(proof, "c1609-h4-"); }

        var bound = Directory.CreateTempSubdirectory("c1609-h4-v2-").FullName;
        try
        {
            var written = await VersionedProof(bound);
            written.Exit.ShouldBe(0, written.Output);
            var files = Directory.GetFiles(Path.Combine(bound, "proof"));
            var body = await File.ReadAllTextAsync(files.Single(p => !p.EndsWith(".bundle", StringComparison.Ordinal)));
            body.ShouldStartWith("c1008-host-proof 2\n", customMessage: "H4 current publication transport requires a typed v2 proof");
            body.ShouldContain("nonce " + new string('b',32));
            body.ShouldContain("inventory " + new string('c',64));
            var bundle = files.Single(p => p.EndsWith(".bundle", StringComparison.Ordinal));
            body.ShouldContain("bundle " + Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(bundle))).ToLowerInvariant());
            body.ShouldContain("count 3\n", customMessage: "head plus original annotated tag and peeled object");
            body.ShouldNotContain("fixture.invalid");
            body.ShouldNotContain(bound);
        }
        finally { DeleteOwned(bound, "c1609-h4-v2-"); }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Origins_and_git_configuration_cannot_redirect_credentials()
    {
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-host-proof.ps1");
        var command = "$ErrorActionPreference='Stop'; . " + Quote(script) + "; " +
            "$user = ConvertTo-C1008ScrubbedOrigin -OriginUrl 'https://user:pass@example.com/repo.git'; " +
            "$token = ConvertTo-C1008ScrubbedOrigin -OriginUrl 'https://example.com/token.git'; " +
            "Write-Output ('user=' + $user); if ($null -eq $token) { Write-Output 'token=null' } else { Write-Output ('token=' + $token) }";
        var run = await Pwsh(command);
        run.Exit.ShouldBe(0, run.Output);
        run.Output.ShouldContain("user=https://example.com/repo.git");
        run.Output.ShouldContain("token=null");
        run.Output.ShouldNotContain("pass@");

        var denied = Directory.CreateTempSubdirectory("c1609-h5-v2-").FullName;
        try
        {
            var local = await VersionedProof(denied, unsafeOrigin: true);
            local.Exit.ShouldNotBe(0, "H5 an allowlist entry cannot authorize a local/file transport: " + local.Output);
            local.Output.ShouldContain("RecycleGitAuditUnknown");
            Directory.GetFiles(Path.Combine(denied, "proof")).ShouldBeEmpty();
        }
        finally { DeleteOwned(denied, "c1609-h5-v2-"); }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Child_deadline_and_output_redaction_are_bounded()
    {
        var root = Directory.CreateTempSubdirectory("c1609-h6-").FullName;
        try
        {
            var bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(bin);
            var proof = Path.Combine(root, "proof");
            Directory.CreateDirectory(proof);
            if (OperatingSystem.IsWindows())
            {
                await File.WriteAllTextAsync(Path.Combine(bin, "git.cmd"),
                    "@echo SENTINEL_STDERR_C1609 1>&2\r\n@exit /b 1\r\n");
            }
            else
            {
                var git = Path.Combine(bin, "git");
                await File.WriteAllTextAsync(git, "#!/bin/sh\nprintf '%s\\n' SENTINEL_STDERR_C1609 >&2\nexit 1\n");
                var mode = await Run("chmod", "0755", git);
                mode.Exit.ShouldBe(0, mode.Output);
            }
            var run = await Proof("https://example.com/repo.git", proof, Path.Combine(root, "proof.log"),
                pathPrefix: bin);
            run.Exit.ShouldNotBe(0, run.Output);
            run.Output.ShouldContain("RecycleGitAuditUnknown");
            run.Output.ShouldNotContain("SENTINEL_STDERR_C1609");
            var log = Path.Combine(root, "proof.log");
            if (File.Exists(log)) (await File.ReadAllTextAsync(log)).ShouldNotContain("SENTINEL_STDERR_C1609");
            Directory.GetFiles(proof).ShouldBeEmpty();
        }
        finally { DeleteOwned(root, "c1609-h6-"); }
    }

    // The desktop writes the proof under C:\src\Antiphon\.antiphon/rolling-server2/<run>/exchange-<n>/
    // c1008-proof-<op>-<started>-<nonce>: 194 characters, so <name>.bundle is 266 and its .lock 271.
    // Git for Windows without core.longpaths cannot create a file past MAX_PATH (259), so every
    // retire-temp preview refused as host-ls-remote status=2 reason=git (2026-11-03 runs c7273562,
    // c727c831, c7272de4). The seam fails any Git path that could not take a .lock under 259
    // characters, the way that Git does; the .NET writes are long-path safe.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1693_Desktop_length_proof_directory_keeps_every_git_path_under_max_path()
    {
        const string prefix = "c1693-mp-";
        var root = Directory.CreateTempSubdirectory(prefix).FullName;
        try
        {
            var origin = Path.Combine(root, "origin.git");
            var work = Path.Combine(root, "work");
            (await Git(root, "init", "-q", "--bare", origin)).Exit.ShouldBe(0);
            (await Git(root, "init", "-q", "-b", "main", work)).Exit.ShouldBe(0);
            await File.WriteAllTextAsync(Path.Combine(work, "published"), "published\n");
            (await Git(work, "add", "published")).Exit.ShouldBe(0);
            (await Git(work, "commit", "-qm", "published")).Exit.ShouldBe(0);
            (await Git(work, "tag", "-a", "v1", "-m", "published")).Exit.ShouldBe(0);
            (await Git(work, "push", "-q", origin, "main", "--tags")).Exit.ShouldBe(0);
            string LongDirectory(char fill)
            {
                var path = Path.Combine(root, new string(fill, 194 - root.Length - 1));
                path.Length.ShouldBe(194);
                Directory.CreateDirectory(path);
                return path;
            }
            var first = LongDirectory('p');
            var renewed = LongDirectory('r');
            var violations = Path.Combine(root, "long-git-paths.txt");
            var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-host-proof.ps1");
            var command = "$ErrorActionPreference='Stop'; . " + Quote(script) + "; " +
                "$original=${function:Invoke-C1609HostGit}; " +
                "function Invoke-C1609HostGit { param($Repo,[string[]]$GitArgs,[int]$TimeoutMs=30000,[string]$WorkingDirectory='', $InputText=$null, [string]$OutputFile='') " +
                "foreach($path in @($GitArgs)+@($WorkingDirectory)){if(([string]$path).Length+'.lock'.Length -gt 259){" +
                "$verb=if($GitArgs[0] -ceq '--git-dir'){$GitArgs[2]}else{$GitArgs[0]}; [IO.File]::AppendAllText($env:C1693_VIOLATIONS,($verb+' length='+([string]$path).Length+[char]10));" +
                "throw (Get-C1008HostLsRemoteDiagnosis -Repo $Repo -Status 2)}}; " +
                "$mapped=@('-c','protocol.file.allow=always')+@($GitArgs|ForEach-Object {if($_ -ceq 'https://fixture.invalid/proof.git'){$env:C1609_BARE}else{$_}}); " +
                "$io=@{};if($null -ne $InputText){$io.InputText=$InputText};if($OutputFile){$io.OutputFile=$OutputFile}; " +
                "& $original -Repo $Repo -GitArgs $mapped -TimeoutMs $TimeoutMs -WorkingDirectory $WorkingDirectory @io }; " +
                "$previous=@{};if($env:C1693_PREVIOUS){$previous.PreviousProofFile=$env:C1693_PREVIOUS}; " +
                "Write-C1008HostOriginProof -OriginUrl 'https://fixture.invalid/proof.git' -RepoRelative '" + Repo + "' -OperationId '" + Operation +
                "' -ProofVersion 2 -PhaseNonce ('b'*32) -InventoryDigest ('c'*64) -ApprovedOrigins @('https://fixture.invalid/proof.git') " +
                "-ProofDirectory $env:C1609_PROOF -CreatedUnix ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -LogPath $env:C1609_LOG @previous";
            var name = Sha(Repo + "\nhttps://fixture.invalid/proof.git");

            var wrote = await Pwsh(command, new[] { ("C1609_BARE", origin), ("C1609_PROOF", first),
                ("C1609_LOG", Path.Combine(first, "host-proof.log")), ("C1693_VIOLATIONS", violations), ("C1693_PREVIOUS", "") });
            var seen = File.Exists(violations) ? File.ReadAllText(violations) : "";
            seen.ShouldBeEmpty("C1693 a fresh proof hands Git no path past MAX_PATH; " + wrote.Output);
            wrote.Exit.ShouldBe(0, "C1693 fresh proof at desktop length; " + wrote.Output);
            var bundle = Path.Combine(first, name + ".bundle");
            bundle.Length.ShouldBe(266);
            File.Exists(bundle).ShouldBeTrue("C1693 the bundle lands in the long proof directory");
            File.Exists(Path.Combine(first, name)).ShouldBeTrue();
            var check = Path.Combine(root, "check.bundle");
            File.Copy(bundle, check);
            var heads = await Git(root, "bundle", "list-heads", check);
            heads.Exit.ShouldBe(0, heads.Output);
            heads.Output.ShouldContain("refs/heads/main");

            // A renewal with an unchanged advertisement reuses the previous long-path bundle.
            var reused = await Pwsh(command, new[] { ("C1609_BARE", origin), ("C1609_PROOF", renewed),
                ("C1609_LOG", Path.Combine(renewed, "host-proof.log")), ("C1693_VIOLATIONS", violations),
                ("C1693_PREVIOUS", Path.Combine(first, name)) });
            seen = File.Exists(violations) ? File.ReadAllText(violations) : "";
            seen.ShouldBeEmpty("C1693 a reused renewal hands Git no path past MAX_PATH; " + reused.Output);
            reused.Exit.ShouldBe(0, "C1693 reused renewal at desktop length; " + reused.Output);
            File.ReadAllBytes(Path.Combine(renewed, name + ".bundle"))
                .ShouldBe(File.ReadAllBytes(bundle), "C1693 the reused renewal keeps the proven graph bytes");
            File.Exists(Path.Combine(renewed, name)).ShouldBeTrue();
        }
        finally { DeleteOwned(root, prefix); }
    }

    // After the MAX_PATH repair the 2026-11-04 01:19Z preview (run c72743c9) still refused:
    // host-proof.log read attempt=1..3 reason=snapshot. Every attempt re-cloned the whole
    // 5081-ref origin into a fresh scratch, so each attempt's ls-remote..ls-remote window held
    // a full clone, and the standing fleet pushed or deleted a branch inside every one. The seam
    // lands one fleet push and one land-time branch delete on origin during each full clone and
    // none during an incremental fetch; a retry must then prove the moved origin exactly.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1693_Snapshot_retry_proves_an_origin_that_moved_during_the_full_clone()
    {
        const string prefix = "c1693-churn-";
        var root = Directory.CreateTempSubdirectory(prefix).FullName;
        try
        {
            var run = await ChurningProof(root, churnEveryFetch: false);
            run.Result.Exit.ShouldBe(0, "C1693 a retry proves the origin the fleet moved; " + run.Result.Output);
            run.Log.ShouldStartWith("repo=" + Repo + " attempt=1 reason=snapshot\n", customMessage: "C1693 the first attempt saw the move");
            run.Log.ShouldContain("repo=" + Repo + " refs=", Case.Sensitive, "C1693 a retry completed the proof");
            run.FullClones.ShouldBe(1, "C1693 a retry fetches into the attempt-1 scratch instead of re-cloning");
            var body = await File.ReadAllTextAsync(run.ProofFile);
            body.ShouldContain("\trefs/heads/fleet-1\n", Case.Sensitive, "C1693 the proof holds the pushed branch");
            body.ShouldNotContain("refs/heads/landed", Case.Sensitive, "C1693 the retry pruned the branch origin deleted");
            var origin = await Git(root, "ls-remote", "--heads", "--tags", Path.Combine(root, "origin.git"));
            origin.Exit.ShouldBe(0, origin.Output);
            var advertised = origin.Output.Replace("\r", "", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Order(StringComparer.Ordinal).ToArray();
            var lines = body.Split('\n');
            lines.Skip(11).Where(l => l.Length > 0).ShouldBe(advertised, "C1693 the proof advertisement is origin's current one");
            var check = Path.Combine(root, "check.bundle");
            File.Copy(run.ProofFile + ".bundle", check);
            var heads = await Git(root, "bundle", "list-heads", check);
            heads.Exit.ShouldBe(0, heads.Output);
            heads.Output.Replace(' ', '\t').Replace("\r", "", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Order(StringComparer.Ordinal).ShouldBe(advertised.Where(l => !l.EndsWith("^{}", StringComparison.Ordinal)),
                    "C1693 the bundle holds exactly the advertised refs, no pruned leftover");
        }
        finally { DeleteOwned(root, prefix); }
    }

    // The guard is unchanged: an origin that moves inside every attempt never proves.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1693_Origin_that_moves_in_every_attempt_still_refuses()
    {
        const string prefix = "c1693-moving-";
        var root = Directory.CreateTempSubdirectory(prefix).FullName;
        try
        {
            var run = await ChurningProof(root, churnEveryFetch: true);
            run.Result.Exit.ShouldNotBe(0, "C1693 a moving origin must refuse; " + run.Result.Output);
            run.Result.Output.ShouldContain("RecycleGitAuditUnknown");
            var attempts = run.Log.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            attempts.ShouldNotBeEmpty();
            attempts.ShouldAllBe(l => l.EndsWith(" reason=snapshot", StringComparison.Ordinal), run.Log);
            Directory.GetFiles(Path.GetDirectoryName(run.ProofFile)!).Where(p => !p.EndsWith("host-proof.log", StringComparison.Ordinal))
                .ShouldBeEmpty("C1693 a refused proof leaves no proof file and no bundle");
        }
        finally { DeleteOwned(root, prefix); }
    }

    private static async Task<((int Exit, string Output) Result, string Log, string ProofFile, int FullClones)> ChurningProof(
        string root, bool churnEveryFetch)
    {
        var origin = Path.Combine(root, "origin.git");
        var work = Path.Combine(root, "work");
        (await Git(root, "init", "-q", "--bare", origin)).Exit.ShouldBe(0);
        (await Git(root, "init", "-q", "-b", "main", work)).Exit.ShouldBe(0);
        await File.WriteAllTextAsync(Path.Combine(work, "published"), "published\n");
        (await Git(work, "add", "published")).Exit.ShouldBe(0);
        (await Git(work, "commit", "-qm", "published")).Exit.ShouldBe(0);
        (await Git(work, "tag", "-a", "v1", "-m", "published")).Exit.ShouldBe(0);
        (await Git(work, "push", "-q", origin, "main", "--tags")).Exit.ShouldBe(0);
        (await Git(work, "push", "-q", origin, "main:refs/heads/landed")).Exit.ShouldBe(0);
        var proof = Path.Combine(root, "proof");
        Directory.CreateDirectory(proof);
        var log = Path.Combine(proof, "host-proof.log");
        var clones = Path.Combine(root, "full-clones.txt");
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-host-proof.ps1");
        var command = "$ErrorActionPreference='Stop'; . " + Quote(script) + "; " +
            "$original=${function:Invoke-C1609HostGit}; " +
            "function Invoke-C1609HostGit { param($Repo,[string[]]$GitArgs,[int]$TimeoutMs=30000,[string]$WorkingDirectory='', $InputText=$null, [string]$OutputFile='') " +
            "$fetch=$GitArgs.Count -gt 2 -and $GitArgs[0] -ceq '--git-dir' -and $GitArgs[2] -ceq 'fetch'; $full=$false; " +
            "if($fetch){$full=-not (& git --git-dir $GitArgs[1] for-each-ref --count=1); if($full){[IO.File]::AppendAllText($env:C1693_CLONES,'full'+[char]10)}}; " +
            "$mapped=@('-c','protocol.file.allow=always')+@($GitArgs|ForEach-Object {if($_ -ceq 'https://fixture.invalid/proof.git'){$env:C1609_BARE}else{$_}}); " +
            "$io=@{};if($null -ne $InputText){$io.InputText=$InputText};if($OutputFile){$io.OutputFile=$OutputFile}; " +
            "$out=& $original -Repo $Repo -GitArgs $mapped -TimeoutMs $TimeoutMs -WorkingDirectory $WorkingDirectory @io; " +
            "if($fetch -and ($full -or $env:C1693_EVERY -eq '1')){$script:churn++; $main=(& git --git-dir $env:C1609_BARE rev-parse refs/heads/main); " +
            "& git --git-dir $env:C1609_BARE update-ref ('refs/heads/fleet-'+$script:churn) $main; & git --git-dir $env:C1609_BARE update-ref -d refs/heads/landed}; " +
            "return $out }; " +
            "Write-C1008HostOriginProof -OriginUrl 'https://fixture.invalid/proof.git' -RepoRelative '" + Repo + "' -OperationId '" + Operation +
            "' -ProofVersion 2 -PhaseNonce ('b'*32) -InventoryDigest ('c'*64) -ApprovedOrigins @('https://fixture.invalid/proof.git') " +
            "-ProofDirectory $env:C1609_PROOF -CreatedUnix ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -LogPath $env:C1609_LOG";
        var result = await Pwsh(command, new[] { ("C1609_BARE", origin), ("C1609_PROOF", proof), ("C1609_LOG", log),
            ("C1693_CLONES", clones), ("C1693_EVERY", churnEveryFetch ? "1" : "0") });
        var name = Sha(Repo + "\nhttps://fixture.invalid/proof.git");
        return (result, File.Exists(log) ? (await File.ReadAllTextAsync(log)).Replace("\r", "", StringComparison.Ordinal) : "",
            Path.Combine(proof, name), File.Exists(clones) ? File.ReadAllLines(clones).Length : 0);
    }

    [Test]
    public void Cleanup_clears_read_only_git_objects_before_windows_shaped_delete()
    {
        const string prefix = "c1609-ro-";
        var root = Directory.CreateTempSubdirectory(prefix).FullName;
        var fanout = Path.Combine(root, "objects", "ab");
        var loose = Path.Combine(fanout, new string('c', 38));
        try
        {
            Directory.CreateDirectory(fanout);
            File.WriteAllText(loose, "git-object");
            File.SetAttributes(loose, File.GetAttributes(loose) | FileAttributes.ReadOnly);
            File.SetAttributes(fanout, File.GetAttributes(fanout) | FileAttributes.ReadOnly);
            File.GetAttributes(loose).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();

            // Linux deletes a read-only file when its directory is writable. The seam
            // throws UnauthorizedAccessException instead, which is the Windows rule.
            DeleteOwned(root, prefix, WindowsShapedDelete);
            Directory.Exists(root).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                ReadOnlyDirectory.ClearReadOnly(root);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void Cleanup_failure_stays_secondary_to_the_assertion()
    {
        const string prefix = "c1609-mask-";
        var root = Directory.CreateTempSubdirectory(prefix).FullName;
        var report = new StringWriter();
        Exception? observed = null;
        try
        {
            try
            {
                throw new InvalidOperationException("assertion-failed");
            }
            finally
            {
                DeleteOwned(root, prefix, static (_, _) =>
                    throw new UnauthorizedAccessException("cleanup-denied"), report);
            }
        }
        catch (Exception error)
        {
            observed = error;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        observed.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("assertion-failed");
        report.ToString().ShouldContain("cleanup-denied");
    }

    [Test]
    public void Cleanup_deletes_only_its_exact_temp_directory()
    {
        const string prefix = "c1609-guard-";
        var root = Directory.CreateTempSubdirectory(prefix).FullName;
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "keep"), "keep");
        File.WriteAllText(Path.Combine(root, "keep"), "keep");
        var report = new StringWriter();
        try
        {
            DeleteOwned(nested, prefix, report: report);
            Directory.Exists(nested).ShouldBeTrue();
            report.ToString().ShouldContain("escaped its owned temp root");

            report.GetStringBuilder().Clear();
            DeleteOwned(root, "c1609-other-", report: report);
            Directory.Exists(root).ShouldBeTrue();
            File.Exists(Path.Combine(root, "keep")).ShouldBeTrue();
            report.ToString().ShouldContain("escaped its owned temp root");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                ReadOnlyDirectory.ClearReadOnly(root);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<(int Exit, string Output)> VersionedProof(string root, bool unsafeOrigin = false)
    {
        var origin = Path.Combine(root,"origin.git"); var work = Path.Combine(root,"work");
        (await Git(root,"init","-q","--bare",origin)).Exit.ShouldBe(0);
        (await Git(root,"init","-q","-b","main",work)).Exit.ShouldBe(0);
        await File.WriteAllTextAsync(Path.Combine(work,"published"),"published\n");
        (await Git(work,"add","published")).Exit.ShouldBe(0);
        (await Git(work,"commit","-qm","published")).Exit.ShouldBe(0);
        (await Git(work,"tag","-a","v1","-m","published")).Exit.ShouldBe(0);
        (await Git(work,"push","-q",origin,"main","--tags")).Exit.ShouldBe(0);
        var proof = Path.Combine(root,"proof"); Directory.CreateDirectory(proof);
        var script = Path.Combine(DelegateScriptRunner.RepoRoot,"scripts/c1008-host-proof.ps1");
        var command = "$ErrorActionPreference='Stop'; . " + Quote(script) + "; $extra=@{}; $url=$env:C1609_BARE; " +
            "if((Get-Command Write-C1008HostOriginProof).Parameters.ContainsKey('ProofVersion')) { " +
            "$url=if($env:C1609_UNSAFE -eq '1'){$env:C1609_BARE}else{'https://fixture.invalid/proof.git'}; " +
            "$extra=@{ProofVersion=2;PhaseNonce=('b'*32);InventoryDigest=('c'*64);ApprovedOrigins=@($url)}; " +
            "$original=${function:Invoke-C1609HostGit}; " +
            "function Invoke-C1609HostGit { param($Repo,[string[]]$GitArgs,[int]$TimeoutMs=30000,[string]$WorkingDirectory='',[string]$InputText='',[string]$OutputFile='') " +
            "$mapped=@('-c','protocol.file.allow=always')+@($GitArgs|ForEach-Object {if($_ -ceq 'https://fixture.invalid/proof.git'){$env:C1609_BARE}else{$_}}); " +
            "& $original -Repo $Repo -GitArgs $mapped -TimeoutMs $TimeoutMs -WorkingDirectory $WorkingDirectory -InputText $InputText -OutputFile $OutputFile } }; " +
            "Write-C1008HostOriginProof -OriginUrl $url -RepoRelative '" + Repo + "' -OperationId '" + Operation +
            "' -ProofDirectory $env:C1609_PROOF -CreatedUnix ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -LogPath $env:C1609_LOG @extra";
        return await Pwsh(command,new[]{("C1609_BARE",origin),("C1609_PROOF",proof),("C1609_LOG",Path.Combine(root,"proof.log")),("C1609_UNSAFE",unsafeOrigin?"1":"0")});
    }

    private static async Task<(int Exit, string Output)> Proof(string origin, string proof, string log,
        string? operation = null, string? pathPrefix = null)
    {
        Directory.CreateDirectory(proof);
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-host-proof.ps1");
        var command = "$ErrorActionPreference='Stop'; . " + Quote(script) + "; " +
            "Write-C1008HostOriginProof -OriginUrl $env:C1609_ORIGIN -RepoRelative $env:C1609_REPO " +
            "-OperationId $env:C1609_OPERATION -ProofDirectory $env:C1609_PROOF " +
            "-CreatedUnix 1700000000 -LogPath $env:C1609_LOG";
        return await Pwsh(command, new (string, string)[]
        {
            ("C1609_ORIGIN", origin),
            ("C1609_REPO", Repo),
            ("C1609_OPERATION", operation ?? Operation),
            ("C1609_PROOF", proof),
            ("C1609_LOG", log)
        }, pathPrefix);
    }

    private static async Task<(int Exit, string Output)> Pwsh(string command,
        IEnumerable<(string Key, string Value)>? env = null, string? pathPrefix = null)
    {
        var psi = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = DelegateScriptRunner.RepoRoot
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);
        if (pathPrefix is not null)
        {
            var current = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = pathPrefix + Path.PathSeparator + current;
        }
        if (env is not null)
        {
            foreach (var (key, value) in env) psi.Environment[key] = value;
        }
        return await Run(psi);
    }

    private static async Task<(int Exit, string Output)> Git(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = cwd
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        psi.Environment["GIT_AUTHOR_NAME"] = "Fixture";
        psi.Environment["GIT_AUTHOR_EMAIL"] = "fixture@example.invalid";
        psi.Environment["GIT_COMMITTER_NAME"] = "Fixture";
        psi.Environment["GIT_COMMITTER_EMAIL"] = "fixture@example.invalid";
        return await Run(psi);
    }

    private static Task<(int Exit, string Output)> Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return Run(psi);
    }

    private static async Task<(int Exit, string Output)> Run(ProcessStartInfo psi)
    {
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("child did not start");
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try { await proc.WaitForExitAsync(deadline.Token); }
        catch
        {
            if (!proc.HasExited) { proc.Kill(true); await proc.WaitForExitAsync(); }
            throw;
        }
        return (proc.ExitCode, await stdout + await stderr);
    }

    private static string Sha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

    private static void DeleteOwned(
        string root,
        string prefix,
        Action<string, bool>? delete = null,
        TextWriter? report = null)
    {
        report ??= Console.Error;
        try
        {
            var full = OwnedTempRoot(root, prefix);
            if (!Directory.Exists(full))
                return;
            if (delete is null)
                ReadOnlyDirectory.Delete(full);
            else
                ReadOnlyDirectory.Delete(full, delete);
        }
        catch (Exception error)
        {
            report.WriteLine($"Git fixture cleanup failed for {root}: {error}");
        }
    }

    private static string OwnedTempRoot(string root, string prefix)
    {
        if (prefix.Length == 0)
            throw new ArgumentException("Host-proof cleanup requires an owned prefix.", nameof(prefix));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var full = Path.GetFullPath(root);
        if (!string.Equals(Path.GetDirectoryName(full), temp, comparison)
            || !Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal))
            throw new IOException("Host-proof cleanup escaped its owned temp root: " + root);
        return full;
    }

    private static void WindowsShapedDelete(string path, bool recursive)
    {
        if (recursive)
            RefuseReadOnly(path);
        Directory.Delete(path, recursive);
    }

    private static void RefuseReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            RefuseReadOnly(entry);
    }
}
