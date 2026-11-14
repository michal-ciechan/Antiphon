using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.TestSupport;
using Shouldly;

namespace Antiphon.Tests.Scripts;

// Each method owns mutable origins. Keep its real Git objects, manifests, command
// observations and audit output in ignored evidence, including on assertion failure.
internal sealed class VolumeRetirementPublicationFixture
{
    internal const string Origin = VolumeRetirementFixture.OriginUrl;
    internal const string Scope = "19010000-0000-0000-0000-000000000001";
    internal const string RepoRelative = "repos/outer";
    internal const string Nonce = "19010000000000000000000000000001";
    internal const string TaskBase = "ca8737e78170c9a441e216408da5fab636c34ca0";
    internal string Root { get; }
    internal string Work { get; }
    internal string Repo { get; }
    internal string Bare { get; }
    internal string Backup { get; }
    internal string A { get; private set; } = "";
    internal string U { get; private set; } = "";
    internal string X { get; private set; } = "";
    internal string Tree { get; private set; } = "";
    internal C1008HostFixture? Host { get; private set; }
    private int sequence;

    private VolumeRetirementPublicationFixture(string root, string work)
    {
        Root = root; Work = work; Repo = Path.Combine(work, RepoRelative);
        Bare = Path.Combine(root, "origin.git"); Backup = Path.Combine(root, "backup.git");
    }

    internal static async Task<VolumeRetirementPublicationFixture> Create(bool preview = false)
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "shape-git");
        // Native preview needs owned mount paths outside /tmp (the runner-tmp role).
        var root = preview ? Path.Combine("/var/tmp", "c1901-" + Guid.NewGuid().ToString("N")) :
            Path.Combine(DelegateScriptRunner.RepoRoot, ".antiphon/evidence/card-1901/fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        C1008HostFixture? host = preview ? new C1008HostFixture(main: false, root) : null;
        var f = new VolumeRetirementPublicationFixture(root, host is null ? Path.Combine(root, "work") : VolumeRetirementFixture.WorkMount(host));
        f.Host = host;
        Directory.CreateDirectory(f.Work);
        if (host is not null)
        {
            var seed = await VolumeRetirementFixture.SeedPublishedOuter(host);
            seed.Bare.ShouldBe(f.Bare); seed.Outer.ShouldBe(f.Repo);
        }
        else
        {
            await f.Git(root, "init", "-q", "--bare", f.Bare);
            await f.Git(root, "init", "-q", "-b", "main", f.Repo);
            await File.WriteAllTextAsync(Path.Combine(f.Repo, ".gitignore"), ".antiphon/\n");
            await File.WriteAllTextAsync(Path.Combine(f.Repo, "readme.txt"), "published\n");
            await f.Git(f.Repo, "add", ".gitignore", "readme.txt");
            await f.Git(f.Repo, "commit", "-qm", "A");
            await f.Git(f.Repo, "remote", "add", "origin", Origin);
            await f.Git(f.Repo, "push", "-q", f.Bare, "HEAD:refs/heads/main");
        }
        await f.Git(f.Repo, "remote", "set-url", "origin", Origin);
        await f.Git(root, "init", "-q", "--bare", f.Backup);
        f.A = await f.Git(f.Repo, "rev-parse", "HEAD");
        f.Tree = await f.Git(f.Repo, "rev-parse", "HEAD^{tree}");
        f.U = await f.Commit("original", f.A, "unique original bytes");
        f.X = await f.Commit("sibling", f.A, "unique sibling bytes");
        await File.WriteAllTextAsync(Path.Combine(root, "source-sha"), await f.Git(DelegateScriptRunner.RepoRoot, "rev-parse", "HEAD"));
        return f;
    }

    internal static string Canonical(string oid) => "refs/antiphon/salvage/" + Scope + "/" + oid;
    internal static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    internal static string Ps(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    internal static string Q(string value) => VolumeRetirementFixture.Quote(value);
    internal string Inventory => Sha("C1008_PRIVATE_ORIGIN repo=" + RepoRelative + " origin=" + Origin +
        " originHash=" + Sha(Origin) + " proofName=" + Sha(RepoRelative + "\n" + Origin) + "\n");

    internal async Task<string> Git(string directory, params string[] args)
    {
        var result = await Run("git", new[] { "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
            "-c", "commit.gpgsign=false", "-c", "core.hooksPath=/dev/null", "-C", directory }.Concat(args));
        result.Exit.ShouldBe(0, string.Join(' ', args) + "; " + result.Output);
        return result.Output.TrimEnd('\r', '\n');
    }

    internal async Task<string> Commit(string label, string parent, string contents, string? secondParent = null)
    {
        // commit-tree gives explicit parents without altering HEAD, index, refs or logs.
        var blob = await Run("git", ["-C", Repo, "hash-object", "-w", "--stdin"], input: contents + "\n");
        blob.Exit.ShouldBe(0, blob.Output);
        var tree = await Run("git", ["-C", Repo, "mktree"], input: "100644 blob " + blob.Output.Trim() + "\tvector.txt\n");
        tree.Exit.ShouldBe(0, tree.Output);
        var args = new List<string> { "-C", Repo, "commit-tree", tree.Output.Trim(), "-p", parent };
        if (secondParent is not null) args.AddRange(["-p", secondParent]);
        var commit = await Run("git", args, input: label + "\n");
        commit.Exit.ShouldBe(0, commit.Output);
        return commit.Output.Trim();
    }

    internal async Task Publish(string oid, string? reference = null) =>
        await Git(Repo, "push", "-q", Bare, oid + ":" + (reference ?? Canonical(oid)));

    internal async Task Protect(string oid, string reference, bool packed = false)
    {
        await Git(Repo, "update-ref", reference, oid);
        if (packed) await Git(Repo, "pack-refs", "--all", "--prune");
    }

    internal sealed record Proof(int Exit, string Output, string Directory, string File);

    internal async Task<Proof> WriteProof((string Oid, string Type)[]? required = null, int version = 2,
        string previous = "", string extraShim = "")
    {
        var directory = Path.Combine(Root, "proof-" + ++sequence);
        Directory.CreateDirectory(directory);
        var script = "$ErrorActionPreference='Stop'; . " + Ps(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-host-proof.ps1")) + "; " +
            // Only transport is mapped; the real bounded Git runner and writer stay active.
            "$original=${function:Invoke-C1609HostGit}; function Invoke-C1609HostGit { " +
            "param($Repo,[string[]]$GitArgs,[int]$TimeoutMs=30000,[string]$WorkingDirectory=''," +
            "[string]$InputText='', [string]$OutputFile=''); " + extraShim +
            "$mapped=@('-c','protocol.file.allow=always')+@($GitArgs|ForEach-Object {if($_ -ceq " + Ps(Origin) + "){" + Ps(Bare) + "}else{$_}}); " +
            "& $original -Repo $Repo -GitArgs $mapped -TimeoutMs $TimeoutMs -WorkingDirectory $WorkingDirectory -InputText $InputText -OutputFile $OutputFile }; " +
            "try { Write-C1008HostOriginProof -OriginUrl " + Ps(version == 1 ? Bare : Origin) + " -RepoRelative " + Ps(RepoRelative) +
            " -OperationId " + Ps(VolumeRetirementFixture.Operation) + " -ProofVersion " + version +
            " -PhaseNonce " + Ps(Nonce) + " -InventoryDigest " + Ps(Inventory) + " -ApprovedOrigins @(" + Ps(Origin) + ") " +
            "-ProofDirectory " + Ps(directory) + " -CreatedUnix ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -LogPath " + Ps(Path.Combine(Root, "host-proof.log")) +
            " -RequiredObjects @(" + string.Join(',', (required ?? []).Select(r => "[pscustomobject]@{objectId=" + Ps(r.Oid) + ";type=" + Ps(r.Type) + "}")) + ") " +
            "-PreviousProofFile " + Ps(previous) + "; exit 0 } catch { Write-Output $_.Exception.Message; exit 2 }";
        var result = await Run("pwsh", ["-NoProfile", "-NonInteractive", "-Command", script], seconds: 60);
        var file = Path.Combine(directory, Sha(RepoRelative + "\n" + (version == 1 ? Bare : Origin)));
        if (result.Exit != 0) File.Exists(file).ShouldBeFalse("a refused writer cannot emit an authorizing header");
        return new Proof(result.Exit, result.Output, directory, file);
    }

    internal string Bindings(Proof proof) => "\nexport C1008_HOST_PROOF=" + Q(proof.Directory) +
        "\nexport C1008_OPERATION_STARTED=1700000000\nexport C1609_PROOF_NONCE=" + Nonce +
        "\nexport C1609_PROOF_INVENTORY=" + Inventory + "\n";

    internal async Task<(int Exit, string Output)> Preview(Proof proof)
    {
        Host.ShouldNotBeNull();
        var result = await Host.Run("retire-temp-runner", Bindings(proof), dryRun: true);
        Host.Removed.ShouldBeEmpty("preview never removes volumes");
        var text = result.Output + await VolumeRetirementFixture.AuditText(Host);
        await File.WriteAllTextAsync(Path.Combine(Root, "preview-" + ++sequence + ".out"), text);
        return (result.Exit, text);
    }

    internal async Task<(int Exit, string Output)> Audit(Proof proof, bool baseline = false)
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.OurCode, "run");
        var source = baseline ? await Git(DelegateScriptRunner.RepoRoot, "show", TaskBase + ":scripts/c590-remote.sh") :
            await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        var content = baseline ? await Git(DelegateScriptRunner.RepoRoot, "show", TaskBase + ":scripts/c1008-content-audit.sh") :
            await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-content-audit.sh"));
        const string start = "<<'C1008_AUDIT_BODY' || :\n";
        var offset = source.IndexOf(start, StringComparison.Ordinal) + start.Length;
        offset.ShouldBeGreaterThan(start.Length);
        var body = source[offset..source.IndexOf("\nC1008_AUDIT_BODY\n", offset, StringComparison.Ordinal)];
        const string stub = "# C1609_CONTENT_AUDIT_INSERT\nc1609_prepare_common() { c1609_skip_publication=0; }\nc1609_consider_fetch_head() { consider_pseudoref \"$1\" FETCH_HEAD; }\nc1609_list_offenders() { :; }\nc1609_finish_blocks() { :; }";
        body.ShouldContain(stub);
        body = body.Replace(stub, content, StringComparison.Ordinal);
        // Map only mount constants; retain every production verdict and cleanup rule.
        // /tmp is an independent runner role, not the host's temporary parent.
        var scratch = Path.Combine(Root, "audit-scratch"); Directory.CreateDirectory(scratch);
        body = body.Replace("readlink -e /work", "readlink -e " + Q(Work), StringComparison.Ordinal)
            .Replace("/audit-scratch/c1008-audit-XXXXXXXX", scratch + "/c1008-audit-XXXXXXXX", StringComparison.Ordinal)
            .Replace("runner_tmp=/tmp", "runner_tmp=" + Q(Path.Combine(Root, "absent-runner-tmp")), StringComparison.Ordinal);
        var file = Path.Combine(Root, "audit-" + ++sequence + (baseline ? "-base" : "-candidate") + ".sh");
        await File.WriteAllTextAsync(file, "export C1609_CONTENT_V2=1 C1008_OPERATION=" + VolumeRetirementFixture.Operation + "\n" + Bindings(proof) + body);
        var run = await Run("bash", [file], seconds: 60);
        await File.WriteAllTextAsync(file + ".out", run.Output);
        return run;
    }

    internal async Task RehashProof(Proof proof, string[] lines)
    {
        lines[7] = "bundle " + Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(proof.File + ".bundle"))).ToLowerInvariant();
        lines[8] = "advertisement " + Sha(string.Join('\n', lines.Skip(11)) + "\n");
        lines[9] = "count " + (lines.Length - 11);
        await File.WriteAllTextAsync(proof.File, string.Join('\n', lines) + "\n");
    }

    // Independent oracle: explicit expected accepted OIDs, checked by Git's
    // pairwise ancestry predicate. It does not parse a namespace or mirror the
    // audit's exclusion/rev-list construction.
    internal async Task Oracle(string label, string[] protectedCommits, string[] acceptedRoots,
        bool expectedPublished, Proof proof, bool compareBase = true)
    {
        var observations = new List<object>();
        var published = true;
        foreach (var tip in protectedCommits)
        {
            var contained = false;
            foreach (var root in acceptedRoots)
            {
                var r = await Run("git", ["-C", Repo, "merge-base", "--is-ancestor", tip, root]);
                r.Exit.ShouldBeOneOf(0, 1);
                observations.Add(new { tip, root, exit = r.Exit });
                contained |= r.Exit == 0;
            }
            published &= contained;
        }
        published.ShouldBe(expectedPublished, label + " independent DAG expectations");
        var audit = await Audit(proof);
        if (expectedPublished) audit.Exit.ShouldBe(0, label + "; " + audit.Output);
        else
        {
            audit.Exit.ShouldBe(3, label + ": locally unique history must never pass; " + audit.Output);
            audit.Output.ShouldContain("RecycleUnpublishedWork");
        }
        if (compareBase)
        {
            var old = await Audit(proof, baseline: true);
            old.Exit.ShouldBe(audit.Exit, label + " unaffected base/candidate verdict");
        }
        await File.WriteAllTextAsync(Path.Combine(Root, "oracle-" + ++sequence + ".json"), JsonSerializer.Serialize(new
        { label, protectedCommits, acceptedRoots, expectedPublished, published, observations, exit = audit.Exit, baseSha = TaskBase }));
    }

    internal async Task<(int Exit, string Output)> Run(string program, IEnumerable<string> args, string? input = null, int seconds = 15)
    {
        var psi = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = input is not null, WorkingDirectory = Root };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        foreach (var key in new[] { "GIT_AUTHOR_NAME", "GIT_COMMITTER_NAME" }) psi.Environment[key] = "Fixture";
        foreach (var key in new[] { "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_EMAIL" }) psi.Environment[key] = "fixture@example.invalid";
        foreach (var key in new[] { "GIT_AUTHOR_DATE", "GIT_COMMITTER_DATE" }) psi.Environment[key] = "2026-01-01T00:00:00Z";
        psi.Environment["GIT_CONFIG_SYSTEM"] = "/dev/null";
        psi.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var phase = TestTiming.BeginProcess(program, psi.ArgumentList.Count, "cli");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            if (input is not null) { await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); }
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout + await stderr;
            await File.AppendAllTextAsync(Path.Combine(Root, "processes.jsonl"), JsonSerializer.Serialize(new
                { program, args = psi.ArgumentList.ToArray(), exit = process.ExitCode, output }) + "\n");
            return (process.ExitCode, output);
        }
        catch
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            throw;
        }
    }
}
