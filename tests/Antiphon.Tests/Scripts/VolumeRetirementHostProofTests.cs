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
        }
        finally { Directory.Delete(root, recursive: true); }
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
            finally { Directory.Delete(root, recursive: true); }
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
        finally { Directory.Delete(refuseRoot, recursive: true); }
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
        finally { Directory.Delete(root, recursive: true); }
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
        finally { Directory.Delete(proof, recursive: true); }

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
        finally { Directory.Delete(bound, recursive: true); }
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
        finally { Directory.Delete(denied, recursive: true); }
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
        finally { Directory.Delete(root, recursive: true); }
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
            "function Invoke-C1609HostGit { param($Repo,[string[]]$GitArgs,[int]$TimeoutMs=30000,[string]$WorkingDirectory='') " +
            "$mapped=@('-c','protocol.file.allow=always')+@($GitArgs|ForEach-Object {if($_ -ceq 'https://fixture.invalid/proof.git'){$env:C1609_BARE}else{$_}}); " +
            "& $original -Repo $Repo -GitArgs $mapped -TimeoutMs $TimeoutMs -WorkingDirectory $WorkingDirectory } }; " +
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
}
