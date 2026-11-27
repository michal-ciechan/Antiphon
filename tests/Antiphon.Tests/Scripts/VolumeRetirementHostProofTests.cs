using System.Diagnostics;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// Unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the desktop origin proof is C1609HostProof and the
// retirement coordinator C1609Coordinator (scripts/c1609-host-proof.ps1, c1008-host-proof.ps1 and c590-real.ps1 before), so these
// rows run in process. Only the transport is mapped (C1609MappedGit: the fixture origin to its local bare repository); the real
// bounded Git runner, writer and live check stay active, and a shim sees each Git call as the PowerShell shims did. The v1 writer
// of c1008-host-proof.ps1 had no caller and was not ported: its rows run the v2 writer, which every retirement uses.
[Timeout(600_000)]
[Category("Unit")]
public sealed class VolumeRetirementHostProofTests
{
    private const string Operation = "c100800000000000000000000000000000001";
    private const string Repo = "repos/school-revision";
    private const long Created = 1700000000;

    // The writer's wall clock: the proof's created and completed seconds (a fake one; no shell reads it here).
    private static DateTimeOffset Now() => DateTimeOffset.FromUnixTimeSeconds(Created);

    private static C1609HostProof Writer(IReadOnlyDictionary<string, string>? origins = null, C1609MappedGit.Shim? shim = null) =>
        new(new C1609MappedGit(origins ?? new Dictionary<string, string>()) { Around = shim }, new FileSystem(), Now);

    private static (int Exit, string Output) Refusal(Action write)
    {
        try { write(); return (0, ""); }
        catch (C1609Refusal e) { return (2, e.Message + "\n"); }
    }

    private static (int Exit, string Output) Versioned(C1609HostProof proof, string origin, string directory, string log, string operation = Operation,
        IReadOnlyList<C1609RequiredObject>? required = null, string previous = "") =>
        Refusal(() => proof.WriteOriginProof(new C1609ProofRequest
        {
            OriginUrl = origin, RepoRelative = Repo, OperationId = operation, PhaseNonce = new string('b', 32), InventoryDigest = new string('c', 64),
            ApprovedOrigins = [origin], ProofDirectory = directory, CreatedUnix = Created, LogPath = log, RequiredObjects = required ?? [], PreviousProofFile = previous,
        }));


    [Test]
    [Arguments("forward")]
    [Arguments("rewrite")]
    [Arguments("delete")]
    [Arguments("new")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1986_Unrelated_origin_changes_preserve_renewal_and_live_proof(string change)
    {
        using var f = await VolumeRetirementPublicationFixture.Create();
        await f.Publish(f.U, "refs/heads/required");
        await f.Publish(f.X, "refs/heads/unrelated");
        var initial = await f.WriteProof([(f.U, "commit")]);
        initial.Exit.ShouldBe(0, initial.Output);
        if (change == "delete") await f.Git(f.Bare, "update-ref", "-d", "refs/heads/unrelated");
        else if (change == "rewrite") await f.Git(f.Bare, "update-ref", "refs/heads/unrelated", f.A);
        else
        {
            var next = await f.Commit("other task push", f.X, "other task bytes");
            await f.Publish(next, change == "new" ? "refs/heads/new-task" : "refs/heads/unrelated");
        }
        var live = await f.WriteProof(previous: initial.File, finalCheck: true);
        var renewed = await f.WriteProof([(f.U, "commit")], previous: initial.File, freshGraph: true);
        (live.Exit, renewed.Exit).ShouldBe((0, 0), change + ": live=" + live.Output + "; renew=" + renewed.Output);
        (await f.WriteProof(previous: renewed.File, finalCheck: true)).Exit.ShouldBe(0);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1986_Depended_branch_fast_forward_preserves_proof()
    {
        using var f = await VolumeRetirementPublicationFixture.Create();
        await f.Publish(f.U, "refs/heads/required");
        var initial = await f.WriteProof([(f.U, "commit")]);
        initial.Exit.ShouldBe(0, initial.Output);
        var next = await f.Commit("published descendant", f.U, "descendant bytes");
        await f.Publish(next, "refs/heads/required");
        var live = await f.WriteProof(previous: initial.File, finalCheck: true);
        var renewed = await f.WriteProof([(f.U, "commit")], previous: initial.File, freshGraph: true);
        (live.Exit, renewed.Exit).ShouldBe((0, 0), "ancestry preserves publication: " + live.Output + renewed.Output);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1986_Renewal_narrows_the_initial_full_origin_graph()
    {
        using var f = await VolumeRetirementPublicationFixture.Create();
        await f.Publish(f.U, "refs/heads/required");
        await f.Publish(f.X, "refs/heads/unrelated");
        var initial = await f.WriteProof();
        initial.Exit.ShouldBe(0, initial.Output);
        await f.Git(f.Bare, "update-ref", "refs/heads/unrelated", f.A);
        var renewed = await f.WriteProof([(f.U, "commit")], previous: initial.File, freshGraph: true);
        renewed.Exit.ShouldBe(0, renewed.Output);
        await f.Git(f.Bare, "update-ref", "-d", "refs/heads/unrelated");
        var live = await f.WriteProof(previous: renewed.File, finalCheck: true);
        live.Exit.ShouldBe(0, live.Output);
    }

    [Test]
    [Arguments("delete")]
    [Arguments("rewrite")]
    [Arguments("tag")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1986_Withdrawn_dependency_reports_ref_and_tips(string change)
    {
        using var f = await VolumeRetirementPublicationFixture.Create();
        var reference = change == "tag" ? "refs/tags/release" : "refs/heads/required";
        await f.Publish(f.U, "refs/heads/required");
        if (change == "tag") await f.Publish(f.U, reference);
        var initial = await f.WriteProof([(f.U, "commit")]);
        initial.Exit.ShouldBe(0, initial.Output);
        if (change == "delete") await f.Git(f.Bare, "update-ref", "-d", reference);
        else await f.Git(f.Bare, "update-ref", reference, f.A);
        var live = await f.WriteProof(previous: initial.File, finalCheck: true);
        var renewed = await f.WriteProof([(f.U, "commit")], previous: initial.File, freshGraph: true);
        foreach (var refused in new[] { live, renewed })
        {
            refused.Exit.ShouldBe(2, refused.Output);
            refused.Output.ShouldContain("RecycleOriginMoved reason=withdrawn-ref");
            refused.Output.ShouldContain("ref=" + reference);
            refused.Output.ShouldContain("old=" + f.U);
            refused.Output.ShouldContain("new=" + (change == "delete" ? "deleted" : f.A));
        }
    }

    [Test]
    public void C1986_Result_boundary_preserves_withdrawal_diagnosis()
    {
        // Write-C590Result's replacement of a generic refusal by this invocation's withdrawn-ref record (C590Bridge's Result).
        using var world = new C590BridgeWorld();
        var root = Path.Combine(world.Root, "evidence");
        var diagnosis = "RecycleOriginMoved reason=withdrawn-ref repo=repos/outer ref=refs/heads/required old=" + new string('a', 40) + " new=deleted";
        using var output = new StringWriter();
        using var error = new StringWriter();
        var session = new C590Bridge.Session(world.Checkout, world.Host(output, error), new FileSystem(), world.Seams(), false);
        session.Proof.OriginMovedDiagnosis = diagnosis;
        foreach (var generic in new[] { "RemoteResultMissing exit=2", "RecycleGitAuditUnknown", "RecycleGitAuditUnknown audit check=host-ls-remote status=2 repo=repos/outer" })
        {
            Should.Throw<C590Exit>(() => session.Result(root, false, generic, 2)).Code.ShouldBe(2);
            File.ReadAllText(Path.Combine(root, "c590-result.json")).ShouldBe("{\"accepted\":false,\"diagnosis\":\"" + diagnosis + "\",\"exit\":2}" + Environment.NewLine);
        }
        // Never an accepted result, another exit or another specific refusal.
        Should.Throw<C590Exit>(() => session.Result(root, false, "RecycleUnpublishedWork", 2));
        File.ReadAllText(Path.Combine(root, "c590-result.json")).ShouldContain("\"diagnosis\":\"RecycleUnpublishedWork\"");
        Should.Throw<C590Exit>(() => session.Result(root, false, "RemoteResultMissing exit=2", 1)).Code.ShouldBe(1);
        File.ReadAllText(Path.Combine(root, "c590-result.json")).ShouldContain("\"diagnosis\":\"RemoteResultMissing exit=2\"");
        Should.Throw<C590Exit>(() => session.Result(root, true, "RecycleGitAuditUnknown", 2));
        File.ReadAllText(Path.Combine(root, "c590-result.json")).ShouldContain("\"diagnosis\":\"RecycleGitAuditUnknown\"");
        output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0].ShouldBe("DIAGNOSIS=" + diagnosis);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Publication_fixture_disposal_leaves_no_checkout_or_temp_residue()
    {
        var evidence = Path.Combine(DelegateScriptRunner.RepoRoot, ".antiphon/evidence/card-1901");
        foreach (var outcome in new[] { "success", "assertion", "cancellation" })
        {
            var fixture = await VolumeRetirementPublicationFixture.Create();
            var root = fixture.Root;
            try
            {
                using (fixture)
                {
                    File.Exists(Path.Combine(root, "origin.git", "HEAD")).ShouldBeTrue();
                    if (outcome == "assertion") false.ShouldBeTrue("fixture body failure");
                    if (outcome == "cancellation") throw new OperationCanceledException();
                }
            }
            catch (ShouldAssertException exception) when (outcome == "assertion" && exception.Message.Contains("fixture body failure", StringComparison.Ordinal)) { }
            catch (OperationCanceledException) when (outcome == "cancellation") { }

            Directory.Exists(root).ShouldBeFalse("fixture disposal must remove its exact root after " + outcome);
            Path.GetDirectoryName(root).ShouldBe(Path.GetFullPath(OperatingSystem.IsWindows() ? Path.GetTempPath() : "/var/tmp").TrimEnd(Path.DirectorySeparatorChar));
            if (Directory.Exists(evidence))
                Directory.EnumerateFileSystemEntries(evidence, "*", SearchOption.AllDirectories).ShouldBeEmpty("publication fixtures must leave nothing under checkout evidence");
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Fresh_graph_renewal_fetches_again_for_unchanged_advertisement()
    {
        using var f = await VolumeRetirementPublicationFixture.Create();
        var fetches = 0;
        C1609MappedGit.Shim observeFetch = (gitArgs, _, run) => { if (gitArgs.Contains("fetch")) fetches++; return run(); };
        var initial = await f.WriteProof([(f.A, "commit")], shim: observeFetch);
        initial.Exit.ShouldBe(0, initial.Output);
        fetches.ShouldBeGreaterThan(0, "the first real writer fetched the origin");
        var initialFetches = fetches;
        var renewed = await f.WriteProof([(f.A, "commit")], previous: initial.File, freshGraph: true, shim: observeFetch);
        renewed.Exit.ShouldBe(0, renewed.Output);
        fetches.ShouldBeGreaterThan(initialFetches, "FreshGraph must fetch again even while the prior advertisement and required objects are unchanged");
    }

    // 2026-11-16 live preview c1008d39: the CARD-1955 script-only export nests the desktop evidence root 70 characters deeper, so
    // the renewed proof directory became 264 characters. Windows cannot start a process whose current directory is MAX_PATH (260)
    // or longer, and the final live control refused before any network read. The shim emulates that limit here.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Final_live_check_runs_git_outside_a_long_desktop_proof_directory()
    {
        using var f = await VolumeRetirementPublicationFixture.Create();
        var initial = await f.WriteProof([(f.A, "commit")]);
        initial.Exit.ShouldBe(0, initial.Output);
        var deep = Path.Combine(f.Root, "retirement-tooling", new string('d', 40), ".antiphon", "rolling-server2",
            "exchange-" + new string('e', 32), "c1008-proof-" + Operation + "-1794832100-" + new string('f', 32));
        deep.Length.ShouldBeGreaterThanOrEqualTo(264, "the witness must exceed the observed desktop proof directory");
        Directory.CreateDirectory(deep);
        var copied = Path.Combine(deep, Path.GetFileName(initial.File));
        File.Copy(initial.File, copied);
        File.Copy(initial.File + ".bundle", copied + ".bundle");
        var used = new List<string>();
        var final = await f.WriteProof(previous: copied, finalCheck: true, shim: (_, directory, run) =>
        {
            used.Add(directory);
            if (directory.Length >= 260) throw new IOException("cwd-max-path");
            return run();
        });

        final.Exit.ShouldBe(0, "the final live control must not start Git inside the long proof directory: " + final.Output);
        var one = used.ShouldHaveSingleItem();
        one.ShouldNotBe(deep);
        Directory.Exists(one).ShouldBeFalse("the live check removes its own scratch working directory");
        var recorded = Path.GetFullPath(one);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        recorded.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            .ShouldBeTrue("the live check cwd must be a temp scratch, not an arbitrary short path: " + recorded);
        Path.GetFileName(recorded).ShouldMatch(@"^c1609-live-[0-9a-f]{32}$", "the live check cwd must be the c1609-live- temp directory");
    }

    // The writer scratch refuses a name outside c1609-host-<32 hex> and a reparse-point proof directory. The live scratch must apply
    // the same two checks to its own directory.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Live_scratch_refuses_a_bad_name_or_reparse_point()
    {
        var root = Directory.CreateTempSubdirectory("c1609-live-custody-").FullName;
        try
        {
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(target);
            var link = Path.Combine(root, "c1609-live-" + new string('a', 32));
            Directory.CreateSymbolicLink(link, target);
            var real = Path.Combine(root, "c1609-live-" + new string('b', 32));
            Directory.CreateDirectory(real);
            var proof = Writer();
            Should.Throw<C1609Refusal>(() => proof.AssertLiveScratchCustody("not-a-live-scratch")).Message.ShouldBe("custody", "bad name admitted");
            Should.Throw<C1609Refusal>(() => proof.AssertLiveScratchCustody(link)).Message.ShouldBe("custody", "reparse point admitted");
            proof.AssertLiveScratchCustody(real);
            var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "C1609HostProof.cs"));
            var live = text.IndexOf("public void AssertLiveOriginProof(", StringComparison.Ordinal);
            var git = text.IndexOf("HostGit(", live, StringComparison.Ordinal);
            var check = text.IndexOf("AssertLiveScratchCustody(scratch)", live, StringComparison.Ordinal);
            live.ShouldBeGreaterThanOrEqualTo(0);
            check.ShouldBeGreaterThan(live);
            check.ShouldBeLessThan(git, "the live check must custody-check its scratch before Git starts");
        }
        finally { DeleteOwned(root, "c1609-live-custody-"); }
    }

    // remote-output.txt used to record only failure-line, and c1609-host-proof.ps1 and c590-real.ps1 both had a line 389, so the
    // first live receipt could not name the failing source. The port records the C# source that refused (and no line).
    [Test]
    public void Coordinator_receipt_names_the_failing_source_and_reason()
    {
        var phase = new string('b', 32);
        var exchange = new string('a', 32);
        var controlNonce = new string('f', 32);
        var required = new string('e', 64);
        const string project = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var proofLeaf = "c1008-proof-c1008" + new string('a', 32) + "-1700000000-" + new string('c', 32);
        const string origin = "https://fixture.invalid/retired.git";
        const string repo = "repos/outer";
        using (var world = new C590BridgeWorld())
        {
            world.Rules([new JsonObject
            {
                ["name"] = "ssh",
                ["protocol"] = new JsonArray(
                    new JsonObject { ["out"] = "C1609_PROOF_REQUEST " + exchange + " " + phase + " initial none " + required },
                    new JsonObject { ["read"] = true },
                    new JsonObject { ["outFields"] = "C1609_CONTROL_REQUEST " + controlNonce + " ready {7} {9}" }),
            }]);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var evidence = Path.Combine(world.Root, "evidence");
            Directory.CreateDirectory(evidence);
            var receipt = Path.Combine(evidence, "remote-output.txt");
            var session = new C590Bridge.Session(world.Checkout, world.Host(output, error), new FileSystem(), world.Seams(), false)
            {
                // The PowerShell test replaced Get-C1008RetireProofExports: a proof set whose one proof is not a v2 proof.
                ProofExports = (root, _, _, _, projectId, _, _, _, _, _, _) =>
                {
                    var proofName = C1609HostProof.Sha256(repo + "\n" + origin);
                    var dir = Path.Combine(root, proofLeaf);
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, proofName), "not-a-proof\n");
                    var inventory = Path.Combine(root, "inventory-" + phase);
                    Directory.CreateDirectory(inventory);
                    File.WriteAllText(Path.Combine(inventory, "private-origins.txt"),
                        "C1008_PRIVATE_ORIGIN repo=" + repo + " origin=" + origin + " originHash=" + C1609HostProof.Sha256(origin) + " proofName=" + proofName + "\n");
                    File.WriteAllText(Path.Combine(inventory, "trusted-origins.json"), "{\"schema\":1,\"projectId\":\"" + projectId + "\",\"origins\":[\"" + origin + "\"]}");
                    return ["export C1008_HOST_PROOF='/home/mc/antiphon-c590/" + proofLeaf + "'", "export C1008_OPERATION_STARTED='1700000000'",
                        "export C1609_PROOF_NONCE='" + new string('d', 32) + "'", "export C1609_PROOF_INVENTORY='" + required + "'"];
                },
            };
            session.Coordinate("true", evidence, "c727aabbccddee00", Operation, project, receipt).ShouldBe(2);
            var live = File.ReadAllText(receipt);
            live.ShouldContain("transport-exit=2\n", Case.Sensitive);
            live.ShouldContain("failure-script=" + C1609HostProof.Site + "\n", Case.Sensitive);
            live.ShouldContain("failure-reason=proof\n", Case.Sensitive);
            live.ShouldContain("failure-line=0\n", Case.Sensitive);
            live.ShouldContain("control-step=proof-read\n", Case.Sensitive);
        }
        using (var world = new C590BridgeWorld())
        {
            world.Rules([new JsonObject { ["name"] = "ssh", ["stdout"] = new JsonArray("C1609_PROOF_REQUEST bad") }]);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var evidence = Path.Combine(world.Root, "evidence");
            Directory.CreateDirectory(evidence);
            var receipt = Path.Combine(evidence, "remote-output.txt");
            new C590Bridge.Session(world.Checkout, world.Host(output, error), new FileSystem(), world.Seams(), false)
                .Coordinate("true", evidence, "c727aabbccddee00", Operation, project, receipt).ShouldBe(2);
            File.ReadAllText(receipt).ShouldBe("transport-exit=2\nfailure-line=0\nfailure-script=" + C1609Coordinator.Site + "\nfailure-reason=protocol\n");
        }
    }

    // Every desktop child the retirement coordinator starts must still start when the directory it would inherit is 264 characters:
    // the coordinator's ssh, the proof's ssh and scp, and Git without an explicit directory start in the temp path; an explicit short
    // scratch stays Git's directory.
    [Test]
    public void Desktop_children_start_when_the_working_directory_is_264_characters()
    {
        using var world = new C590BridgeWorld();
        var deep = Path.Combine(world.Root, "retirement-tooling", new string('d', 40), ".antiphon", "rolling-server2", "exchange-" + new string('e', 32));
        if (deep.Length < 264) deep = Path.Combine(deep, new string('x', Math.Max(1, 264 - deep.Length - 1)));
        deep.Length.ShouldBeGreaterThanOrEqualTo(264, "the witness must use a 264-character working directory");
        foreach (var segment in deep.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) segment.Length.ShouldBeLessThanOrEqualTo(255);
        var temp = Path.GetTempPath().TrimEnd('\\', '/');
        world.CurrentDirectory = deep;
        world.Rules([]);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var evidence = Path.Combine(world.Root, "evidence");
        Directory.CreateDirectory(evidence);
        new C590Bridge.Session(world.Checkout, world.Host(output, error), new FileSystem(), world.Seams(), false)
            .Coordinate("true", evidence, "c727aabbccddee00", Operation, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", Path.Combine(evidence, "remote-output.txt")).ShouldBe(0);
        world.Calls.Single(c => c["end"] == null)["cwd"]!.GetValue<string>().ShouldBe(temp, "the coordinator's ssh starts in the temp path");

        var scratch = Path.Combine(Path.GetTempPath(), "c1609-host-" + Guid.NewGuid().ToString("N"));
        var children = new C1609BoundedProcesses(new FileSystem(), () => deep);
        foreach (var requested in new[] { "", deep })
            children.StartInfo("ssh", ["-o", "BatchMode=yes"], requested, input: true).WorkingDirectory.ShouldBe(temp, "a 264-character directory is never a child's: " + requested);
        children.StartInfo("git", ["status"], scratch, input: true).WorkingDirectory.ShouldBe(scratch, "an explicit short scratch stays the git cwd");
        C1609BoundedProcesses.StartDirectory("", "C:\\short", temp).ShouldBe("", "a short current directory is inherited");
        C1609BoundedProcesses.StartDirectory(scratch, deep, temp).ShouldBe(scratch);
    }

    // Operator decision 2026-11-16 widened only the host freshness fence to a provisional 11,000 ms. CARD-1989 made the desktop live
    // check's control and Git budget configurable (ANTIPHON_RETIRE_CONTROL_WINDOW_MS, default and cap 8,000 ms) below those unchanged
    // host fences.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Final_live_check_bounds_the_configurable_desktop_budget()
    {
        using var f = await VolumeRetirementPublicationFixture.Create();
        var initial = await f.WriteProof([(f.A, "commit")]);
        initial.Exit.ShouldBe(0, initial.Output);
        var gitCalls = 0;
        C1609MappedGit.Shim observeGit = (_, _, run) => { gitCalls++; return run(); };

        var admitted = await f.WriteProof(previous: initial.File, finalCheck: true, networkBudgetMs: 8000, shim: observeGit);
        admitted.Exit.ShouldBe(0, "8,000 ms fits below the unchanged host read and effect fences: " + admitted.Output);
        gitCalls.ShouldBeGreaterThan(0, "the admitted live check reads the origin through the observed Git runner");
        foreach (var budget in new[] { 0, 8001, 11000 })
        {
            gitCalls = 0;
            var refused = await f.WriteProof(previous: initial.File, finalCheck: true, networkBudgetMs: budget, shim: observeGit);
            refused.Exit.ShouldBe(2, budget + ": " + refused.Output);
            gitCalls.ShouldBe(0, budget + " ms must be refused before Git starts");
        }
        var coordinator = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "C1609Coordinator.cs"));
        coordinator.ShouldContain("var remaining = Math.Min(controlWindowMs - (int)(controlWatch.ElapsedMilliseconds - cleanupMs),", Case.Sensitive);
        coordinator.ShouldContain("if (controlWatch.ElapsedMilliseconds - cleanupMs > controlWindowMs) throw Refuse(\"control-deadline\");", Case.Sensitive);
    }

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
            const string url = "https://fixture.invalid/renewal.git";
            var map = new Dictionary<string, string> { [url] = origin };
            var proof = Path.Combine(root, "proof");
            Directory.CreateDirectory(proof);
            var wrote = Versioned(Writer(map), url, proof, Path.Combine(root, "proof.log"));
            wrote.Exit.ShouldBe(0, "c1609-h1: " + wrote.Output);
            var proofFile = Directory.GetFiles(proof).Single(path => !path.EndsWith(".bundle", StringComparison.Ordinal));
            var body = await File.ReadAllTextAsync(proofFile);
            body.ShouldStartWith("c1008-host-proof 2\n");
            body.ShouldContain("operation " + Operation);
            body.ShouldContain(tip + "\trefs/heads/main");
            body.ShouldNotContain(origin);
            var bundle = Directory.GetFiles(proof).Single(path => path.EndsWith(".bundle", StringComparison.Ordinal));
            var heads = await Git(root, "bundle", "list-heads", bundle);
            heads.Exit.ShouldBe(0, heads.Output);
            heads.Output.ShouldContain(tip);
            heads.Output.ShouldContain("refs/heads/main");

            // One seed supplies all renewal controls; every producer invocation still owns a fresh origin-only proof repository and output.
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
                var renewal = Versioned(Writer(map), url, destination, Path.Combine(root, "renewal.log"), required: [new C1609RequiredObject(oid, type)]);
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
                var url = "https://fixture.invalid/" + name;
                var wrote = Versioned(Writer(new Dictionary<string, string> { [url] = origin }), url, proof, Path.Combine(root, "proof.log"));
                wrote.Exit.ShouldBe(0, name + ": " + wrote.Output);
                var text = string.Join('\n', Directory.GetFiles(proof).Where(p => !p.EndsWith(".bundle", StringComparison.Ordinal)).Select(File.ReadAllText));
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
            // A credential-bearing origin is never an approved scrubbed origin: refused before Git runs, and the token never crosses.
            var gitCalls = 0;
            var refused = Versioned(Writer(shim: (_, _, run) => { gitCalls++; return run(); }), "https://user:ghp_PRIVATE_SENTINEL@example.com/token.git",
                refuseRoot, Path.Combine(refuseRoot, "proof.log"));
            refused.Exit.ShouldNotBe(0, refused.Output);
            refused.Output.ShouldContain("RecycleGitAuditUnknown");
            refused.Output.ShouldNotContain("ghp_PRIVATE_SENTINEL");
            gitCalls.ShouldBe(0);
            Directory.GetFiles(refuseRoot).Where(p => !p.EndsWith("proof.log", StringComparison.Ordinal)).ShouldBeEmpty();
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
            const string url = "https://fixture.invalid/missing.git";
            var run = Versioned(Writer(new Dictionary<string, string> { [url] = Path.Combine(root, "missing.git") }), url, proof, Path.Combine(root, "proof.log"));
            run.Exit.ShouldNotBe(0, run.Output);
            run.Output.ShouldContain("RecycleGitAuditUnknown");
            run.Output.ShouldNotContain("fatal:");
            Directory.GetFiles(proof).ShouldBeEmpty();
            await Task.CompletedTask;
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
        C1609HostProof.ReadPrivateOriginLines(line + "\n").Count.ShouldBe(1);
        Should.Throw<C1609Refusal>(() => C1609HostProof.ReadPrivateOriginLines(line + "\n" + line + "\n")).Message.ShouldBe("RecycleGitAuditUnknown");
        Should.Throw<C1609Refusal>(() => C1609HostProof.ReadPrivateOriginLines(line.Replace(hash, new string('0', 64), StringComparison.Ordinal) + "\n"))
            .Message.ShouldBe("RecycleGitAuditUnknown");

        var proof = Directory.CreateTempSubdirectory("c1609-h4-").FullName;
        try
        {
            var gitCalls = 0;
            var bad = Versioned(Writer(shim: (_, _, run) => { gitCalls++; return run(); }), origin, proof, Path.Combine(proof, "proof.log"), "not-an-operation");
            bad.Exit.ShouldNotBe(0, bad.Output);
            bad.Output.ShouldContain("RecycleGitAuditUnknown");
            gitCalls.ShouldBe(0);
            Directory.GetFiles(proof).Where(p => !p.EndsWith("proof.log", StringComparison.Ordinal)).ShouldBeEmpty();
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
            body.ShouldContain("nonce " + new string('b', 32));
            body.ShouldContain("inventory " + new string('c', 64));
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
        C1609HostProof.ScrubOrigin("https://user:pass@example.com/repo.git").ShouldBe("https://example.com/repo.git");
        C1609HostProof.ScrubOrigin("https://example.com/token.git").ShouldBeNull();

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
            var proof = Path.Combine(root, "proof");
            Directory.CreateDirectory(proof);
            // Git's own error names the mapped path, which carries the sentinel: it is on Git's stderr only and must not cross.
            const string url = "https://example.com/repo.git";
            var run = Versioned(Writer(new Dictionary<string, string> { [url] = Path.Combine(root, "SENTINEL_STDERR_C1609") }), url, proof, Path.Combine(root, "proof.log"));
            run.Exit.ShouldNotBe(0, run.Output);
            run.Output.ShouldContain("RecycleGitAuditUnknown");
            run.Output.ShouldNotContain("SENTINEL_STDERR_C1609");
            var log = Path.Combine(root, "proof.log");
            if (File.Exists(log)) (await File.ReadAllTextAsync(log)).ShouldNotContain("SENTINEL_STDERR_C1609");
            Directory.GetFiles(proof).ShouldBeEmpty();
            // The bounded runner keeps standard output only, and stops a child at its deadline.
            var children = new C1609BoundedProcesses(new FileSystem());
            var kept = children.Run(new C1609ProcessCall { FileName = "git", Arguments = ["no-such-command-c1609"], CaptureOutput = true, TimeoutMs = 30000 });
            kept.ExitCode.ShouldNotBe(0);
            kept.Stdout.ShouldNotContain("no-such-command-c1609");
            Should.Throw<C1609Refusal>(() => children.Run(new C1609ProcessCall { FileName = "git", Arguments = ["--version"], TimeoutMs = 0 })).Message.ShouldBe("deadline");
        }
        finally { DeleteOwned(root, "c1609-h6-"); }
    }

    // The desktop writes the proof under C:\src\Antiphon\.antiphon/rolling-server2/<run>/exchange-<n>/c1008-proof-<op>-<started>-<nonce>:
    // 194 characters, so <name>.bundle is 266 and its .lock 271. Git for Windows without core.longpaths cannot create a file past
    // MAX_PATH (259), so every retire-temp preview refused as host-ls-remote status=2 reason=git (2026-11-03 runs c7273562, c727c831,
    // c7272de4). The shim fails any Git path that could not take a .lock under 259 characters, the way that Git does; the .NET writes
    // are long-path safe.
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
            var violations = new List<string>();
            const string url = "https://fixture.invalid/proof.git";
            var writer = Writer(new Dictionary<string, string> { [url] = origin }, (gitArgs, directory, run) =>
            {
                foreach (var path in gitArgs.Append(directory))
                    if (path.Length + ".lock".Length > 259)
                    {
                        violations.Add((gitArgs[0] == "--git-dir" ? gitArgs[2] : gitArgs[0]) + " length=" + path.Length);
                        throw new IOException("max-path");
                    }
                return run();
            });
            var name = Sha(Repo + "\n" + url);

            var wrote = Versioned(writer, url, first, Path.Combine(first, "host-proof.log"));
            violations.ShouldBeEmpty("C1693 a fresh proof hands Git no path past MAX_PATH; " + wrote.Output);
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
            var reused = Versioned(writer, url, renewed, Path.Combine(renewed, "host-proof.log"), previous: Path.Combine(first, name));
            violations.ShouldBeEmpty("C1693 a reused renewal hands Git no path past MAX_PATH; " + reused.Output);
            reused.Exit.ShouldBe(0, "C1693 reused renewal at desktop length; " + reused.Output);
            File.ReadAllBytes(Path.Combine(renewed, name + ".bundle")).ShouldBe(File.ReadAllBytes(bundle), "C1693 the reused renewal keeps the proven graph bytes");
            File.Exists(Path.Combine(renewed, name)).ShouldBeTrue();
        }
        finally { DeleteOwned(root, prefix); }
    }

    // After the MAX_PATH repair the 2026-11-04 01:19Z preview (run c72743c9) still refused: host-proof.log read attempt=1..3
    // reason=snapshot. Every attempt re-cloned the whole 5081-ref origin into a fresh scratch, so each attempt's ls-remote..ls-remote
    // window held a full clone, and the standing fleet pushed or deleted a branch inside every one. The shim lands one fleet push and
    // one land-time branch delete on origin during each full clone and none during an incremental fetch; a retry must then prove the
    // moved origin exactly.
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
        var clones = 0;
        var churn = 0;
        const string url = "https://fixture.invalid/proof.git";
        var writer = Writer(new Dictionary<string, string> { [url] = origin }, (gitArgs, _, run) =>
        {
            var fetch = gitArgs.Count > 2 && gitArgs[0] == "--git-dir" && gitArgs[2] == "fetch";
            var full = false;
            if (fetch)
            {
                full = Git(root, "--git-dir", gitArgs[1], "for-each-ref", "--count=1").GetAwaiter().GetResult().Output.Trim().Length == 0;
                if (full) clones++;
            }
            var result = run();
            if (fetch && (full || churnEveryFetch))
            {
                churn++;
                var main = Git(root, "--git-dir", origin, "rev-parse", "refs/heads/main").GetAwaiter().GetResult().Output.Trim();
                Git(root, "--git-dir", origin, "update-ref", "refs/heads/fleet-" + churn, main).GetAwaiter().GetResult();
                Git(root, "--git-dir", origin, "update-ref", "-d", "refs/heads/landed").GetAwaiter().GetResult();
            }
            return result;
        });
        var result = Versioned(writer, url, proof, log);
        var name = Sha(Repo + "\n" + url);
        await Task.CompletedTask;
        return (result, File.Exists(log) ? (await File.ReadAllTextAsync(log)).Replace("\r", "", StringComparison.Ordinal) : "", Path.Combine(proof, name), clones);
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
        var origin = Path.Combine(root, "origin.git"); var work = Path.Combine(root, "work");
        (await Git(root, "init", "-q", "--bare", origin)).Exit.ShouldBe(0);
        (await Git(root, "init", "-q", "-b", "main", work)).Exit.ShouldBe(0);
        await File.WriteAllTextAsync(Path.Combine(work, "published"), "published\n");
        (await Git(work, "add", "published")).Exit.ShouldBe(0);
        (await Git(work, "commit", "-qm", "published")).Exit.ShouldBe(0);
        (await Git(work, "tag", "-a", "v1", "-m", "published")).Exit.ShouldBe(0);
        (await Git(work, "push", "-q", origin, "main", "--tags")).Exit.ShouldBe(0);
        var proof = Path.Combine(root, "proof"); Directory.CreateDirectory(proof);
        const string url = "https://fixture.invalid/proof.git";
        // Unsafe: the allowlist names the local path itself, a file transport no proof may use.
        return Versioned(Writer(new Dictionary<string, string> { [url] = origin }), unsafeOrigin ? origin : url, proof, Path.Combine(root, "proof.log"));
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
