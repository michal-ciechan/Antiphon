using Antiphon.LiveLane;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport.LiveClaude;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// W2 slice S22: the Claude CLI version as its own axis next to the code digest (the owner, 2026-11-26: "a way to say digest
/// vs what versions of Claude it was tested against so we can verify multiple version of Claude"). A run asks its
/// executable for `--version` through the process seam (always a fake here); a schemaVersion 2 receipt records the token
/// and the raw line, an older receipt reads as "unknown"; live-lane-status lists, per lane and code digest, every Claude
/// version with its newest receipt's outcome, and exits 1 when a version --require-claude-version names has no passing
/// receipt. Also the command line, the table and the one-document-per-lane JSON.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneStatusTests
{
    private const string Lane = "fixture-lane";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly string[] Roster = LiveLaneLaneFixture.Roster;

    private sealed class FakeProcess(Func<string, IReadOnlyList<string>, LiveLaneProcessResult> answer) : ILiveLaneProcess
    {
        public List<string> Calls { get; } = [];

        public LiveLaneProcessResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            Calls.Add(fileName + " " + string.Join(" ", arguments));
            return answer(fileName, arguments);
        }
    }

    [Test]
    public void Version_is_a_stable_token_with_the_raw_line_beside_it()
    {
        LiveLaneClaudeVersion.Parse("2.1.3 (Claude Code)\n").Version.ShouldBe("2.1.3");
        LiveLaneClaudeVersion.Parse("2.1.3 (Claude Code)\r\n").Raw.ShouldBe("2.1.3 (Claude Code)");
        LiveLaneClaudeVersion.Parse("\n  claude 1.0.0-beta.2 build\n2.0.0\n").Version.ShouldBe("1.0.0-beta.2");
        LiveLaneClaudeVersion.Parse("v10.20.30").Version.ShouldBe("10.20.30");
        LiveLaneClaudeVersion.Parse("1.2.3.4").Version.ShouldBe(LiveLaneClaudeVersion.UnknownToken);
        LiveLaneClaudeVersion.Parse("").ShouldBeSameAs(LiveLaneClaudeVersion.Unknown);
        var odd = LiveLaneClaudeVersion.Parse("no version here\u0007");
        odd.Version.ShouldBe("unknown");
        odd.Raw.ShouldBe("no version here");
        LiveLaneClaudeVersion.Parse(new string('x', 400) + " 1.2.3").Raw!.Length.ShouldBe(LiveLaneClaudeVersion.RawLimit);
        foreach (var token in new[] { "unknown", "2.1.3", "0.0.0", "1.0.0-rc.1" }) LiveLaneClaudeVersion.IsToken(token).ShouldBeTrue(token);
        foreach (var token in new[] { "", "Unknown", "2.1", "2.1.3 ", "v2.1.3", "2.1.3-", "2.1.3\n" }) LiveLaneClaudeVersion.IsToken(token).ShouldBeFalse(token);

        var fake = new FakeProcess((_, _) => new LiveLaneProcessResult(0, "2.1.3 (Claude Code)\n"));
        var version = LiveLaneClaudeVersion.Probe(fake, "C:/tools/claude.exe");
        version.Version.ShouldBe("2.1.3");
        version.Raw.ShouldBe("2.1.3 (Claude Code)");
        fake.Calls.ShouldBe(["C:/tools/claude.exe --version"]);
        LiveLaneClaudeVersion.Probe(new FakeProcess((_, _) => new LiveLaneProcessResult(1, "2.1.3")), "claude").ShouldBeSameAs(LiveLaneClaudeVersion.Unknown);
        LiveLaneClaudeVersion.Probe(new FakeProcess((_, _) => new LiveLaneProcessResult(null, "", timedOut: true)), "claude").ShouldBeSameAs(LiveLaneClaudeVersion.Unknown);
        LiveLaneClaudeVersion.Probe(new FakeProcess((_, _) => throw new InvalidOperationException("no such file")), "claude").ShouldBeSameAs(LiveLaneClaudeVersion.Unknown);
        var never = new FakeProcess((_, _) => throw new InvalidOperationException("must not start"));
        LiveLaneClaudeVersion.Probe(never, " ").ShouldBeSameAs(LiveLaneClaudeVersion.Unknown);
        never.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Receipts_record_the_version_and_old_receipts_read_unknown()
    {
        using var scratch = new LaneScratch("c1545-claude-receipt-");
        var world = new World(scratch);
        var v2 = world.Write("v2", world.Receipt("run-v2", "2.1.3", Now.AddHours(-1)));
        var check = LiveLaneReceipt.Check(v2, roster: Roster);
        check.Valid.ShouldBeTrue(string.Join(",", check.Reasons));
        check.Claude.Version.ShouldBe("2.1.3");
        check.Claude.Raw.ShouldBe("2.1.3 (Claude Code)");
        ((LiveLaneMap)LiveLaneData.Parse(File.ReadAllText(v2))!)["schemaVersion"].ShouldBe(2L);
        var v1 = world.Write("v1", world.Receipt("run-v1", null, Now.AddHours(-1)));
        check = LiveLaneReceipt.Check(v1, roster: Roster);
        check.Valid.ShouldBeTrue(string.Join(",", check.Reasons));
        check.Claude.Version.ShouldBe("unknown");
        check.Claude.Raw.ShouldBeNull();
        // A schema 2 receipt must carry a well-formed version; a schema 1 receipt cannot carry one.
        foreach (var (name, edit, reason) in new (string, Action<LiveLaneMap>, string)[]
                 {
                     ("bad-token", d => ((LiveLaneMap)d["claude"]!)["version"] = "2.1", "type:claude.version"),
                     ("missing", d => d.Remove("claude"), "required:claude"),
                     ("extra", d => ((LiveLaneMap)d["claude"]!)["path"] = "C:/x", "field:claude.path"),
                     ("long-raw", d => ((LiveLaneMap)d["claude"]!)["raw"] = new string('r', 257), "type:claude.raw"),
                     ("v1-with-version", d => d["schemaVersion"] = 1L, "field:claude"),
                 })
        {
            var document = world.Receipt("run-" + name, "2.1.3", Now.AddHours(-1)).ToJson();
            edit(document);
            var path = world.WriteRaw(name, document);
            var refused = LiveLaneReceipt.Check(path, roster: Roster);
            refused.Valid.ShouldBeFalse(name);
            refused.Reasons.ShouldContain(reason, name + ": " + string.Join(",", refused.Reasons));
        }
        // Written once: a second write to the same path is refused and the receipt is unchanged.
        var bytes = File.ReadAllBytes(v2);
        Should.Throw<LiveLaneRefusal>(() => world.Receipt("run-v2", "2.1.4", Now).Write(v2)).Message.ShouldBe("receipt-exists");
        File.ReadAllBytes(v2).ShouldBe(bytes);
    }

    [Test]
    public void Status_lists_each_claude_version_for_the_code_digest()
    {
        using var scratch = new LaneScratch("c1545-claude-status-");
        var world = new World(scratch);
        world.Write("a-red", world.Receipt("run-a", "2.1.3", Now.AddHours(-3), outcome: "Failed"));
        world.Write("b-green", world.Receipt("run-b", "2.1.3", Now.AddHours(-2)));
        world.Write("c-red", world.Receipt("run-c", "2.1.4", Now.AddHours(-1), outcome: "Failed"));
        world.Write("d-v1", world.Receipt("run-d", null, Now.AddMinutes(-90)));
        // Another code digest and another lane never stand for this one.
        world.Write("e-other-code", world.Receipt("run-e", "9.9.9", Now.AddMinutes(-30), testSources: new string('9', 64)));
        world.Write("f-other-lane", world.Receipt("run-f", "8.8.8", Now.AddMinutes(-20), lane: "other-lane"));

        var result = LiveLaneStatus.Lane(LiveLaneTestSources.Create(), world.Options());
        result.ExitCode.ShouldBe(0, LiveLaneData.Canonical(result.Document));
        var claude = (LiveLaneMap)result.Document["claude"]!;
        claude["codeDigest"].ShouldBe(world.CodeDigest);
        ((LiveLaneMap)result.Document["fingerprint"]!)["digest"].ShouldBe(world.CodeDigest, "without an attestation the fingerprint is the code digest");
        var versions = ((List<object>)claude["versions"]!).Cast<LiveLaneMap>().ToList();
        versions.Select(v => (string)v["version"]! + "=" + v["outcome"] + "/" + v["receipts"]).ShouldBe(["2.1.3=GREEN/2", "2.1.4=RED/1", "unknown=GREEN/1"]);
        versions[0]["runId"].ShouldBe("run-b");
        versions[0]["raw"].ShouldBe("2.1.3 (Claude Code)");
        versions[0]["endedUtc"].ShouldBe("2026-11-02T10:00:00Z");
        versions[2]["raw"].ShouldBeNull();
        ((List<object>)claude["passing"]!).ShouldBe(["2.1.3", "unknown"]);
        ((List<object>)claude["failing"]!).ShouldBe(["2.1.4"]);
        claude["satisfied"].ShouldBe(true, "today's rule: one passing receipt is enough");
        ((List<object>)claude["required"]!).ShouldBeEmpty();

        // A required version must pass: met (exit 0), failing or absent (exit 1).
        LiveLaneStatus.Lane(LiveLaneTestSources.Create(), world.Options(required: ["2.1.3"])).ExitCode.ShouldBe(0);
        var failing = LiveLaneStatus.Lane(LiveLaneTestSources.Create(), world.Options(required: ["2.1.3", "2.1.4"]));
        failing.ExitCode.ShouldBe(1);
        ((List<object>)((LiveLaneMap)failing.Document["claude"]!)["missing"]!).ShouldBe(["2.1.4"]);
        ((LiveLaneMap)failing.Document["claude"]!)["satisfied"].ShouldBe(false);
        var absent = LiveLaneStatus.Lane(LiveLaneTestSources.Create(), world.Options(required: ["2.1.5"]));
        absent.ExitCode.ShouldBe(1);
        ((List<object>)((LiveLaneMap)absent.Document["claude"]!)["missing"]!).ShouldBe(["2.1.5"]);

        // Without history there are no receipts: nothing passes, the default rule still exits 0, a requirement exits 1.
        var plain = LiveLaneStatus.Lane(LiveLaneTestSources.Create(), LiveLaneStatusOptions.Of(world.Root, Lane, world.Y));
        plain.ExitCode.ShouldBe(0);
        ((LiveLaneMap)plain.Document["claude"]!)["satisfied"].ShouldBe(false);
        LiveLaneStatus.Lane(LiveLaneTestSources.Create(), LiveLaneStatusOptions.Of(world.Root, Lane, world.Y, required: ["2.1.3"])).ExitCode.ShouldBe(1);
        // A refused lane stays exit 2 whatever is required.
        LiveLaneStatus.Lane(LiveLaneTestSources.Create(), LiveLaneStatusOptions.Of(world.Root, Lane, "abc", required: ["2.1.3"])).ExitCode.ShouldBe(2);
    }

    [Test]
    public void Attested_fingerprint_keeps_the_code_digest()
    {
        using var scratch = new LaneScratch("c1545-claude-attested-");
        var world = new World(scratch);
        var documents = LiveClaudeProfileFixture.CleanTempSubdirectory("c1545-claude-docs-");
        try
        {
            LiveLaneAttestationExport.WriteAll(documents);
            var attested = LiveLaneFingerprint.Compute(LiveLaneTestSources.Create(), world.Root, world.Y, Lane, Path.Combine(documents, "base.json"));
            var newer = LiveLaneFingerprint.Compute(LiveLaneTestSources.Create(), world.Root, world.Y, Lane, Path.Combine(documents, "version-2.json"));
            attested.Complete.ShouldBeTrue(string.Join(",", attested.UnknownReasons));
            attested.Digest.ShouldNotBe(world.CodeDigest, "the attested environment is a component of the fingerprint");
            attested.CodeDigest.ShouldBe(world.CodeDigest);
            newer.Digest.ShouldNotBe(attested.Digest, "another Claude version is another attested executable");
            newer.CodeDigest.ShouldBe(world.CodeDigest, "but the same code digest");
        }
        finally { Directory.Delete(documents, true); }
    }

    [Test]
    public void Command_prints_one_document_or_block_per_lane()
    {
        using var scratch = new LaneScratch("c1545-claude-command-");
        var world = new World(scratch, secondLane: true);
        world.Write("b-green", world.Receipt("run-b", "2.1.3", Now.AddHours(-2)));
        string[] Args(params string[] extra) => ["--repository-root", world.Root, "--candidate-sha", world.Y, "--history", world.History, "--context", world.Context, .. extra];

        var json = new StringWriter();
        LiveLaneStatus.Run(LiveLaneStatusOptions.Parse(Args("--json"), null), LiveLaneTestSources.Create(), json).ShouldBe(0);
        var lines = json.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => (LiveLaneMap)LiveLaneData.Parse(line)!).ToList();
        lines.Select(line => (string)line["laneId"]!).ShouldBe([Lane, "fixture-lane-b"]);
        lines.ShouldAllBe(line => line["format"] as string == "antiphon-live-lane-status-v1" && line["claude"] != null);

        var table = new StringWriter();
        LiveLaneStatus.Run(LiveLaneStatusOptions.Parse(Args("--lane", Lane, "--require-claude-version", "2.1.3, 2.1.4"), null), LiveLaneTestSources.Create(), table).ShouldBe(1);
        var text = table.ToString();
        text.ShouldStartWith(Lane + "  DUE  fingerprint ");
        text.ShouldContain("  claude 2.1.3        GREEN        receipt ");
        text.ShouldContain("  required: 2.1.3, 2.1.4; missing 2.1.4");

        // HEAD is the default candidate; a lane that refuses prints one INVALID line and exits 2.
        var head = new StringWriter();
        LiveLaneStatus.Run(LiveLaneStatusOptions.Parse(["--repository-root", world.Root, "--lane", Lane, "--json"], null), LiveLaneTestSources.Create(), head).ShouldBe(0);
        ((LiveLaneMap)LiveLaneData.Parse(head.ToString())!)["candidateSha"].ShouldBe(world.Y);
        var refused = new StringWriter();
        LiveLaneStatus.Run(LiveLaneStatusOptions.Parse(["--repository-root", world.Root, "--lane", "no-such-lane"], null), LiveLaneTestSources.Create(), refused).ShouldBe(2);
        refused.ToString().Trim().ShouldBe("no-such-lane  INVALID  declaration-invalid");

        foreach (var bad in new string[][] { ["--require-claude-version", "unknown"], ["--require-claude-version", "2.1"], ["--require-claude-version", "2.1.3,"],
                     ["--use-record", "u.json"], ["--bogus"], ["positional"], ["--lane"] })
            Should.Throw<LiveLaneUsageException>(() => LiveLaneStatusOptions.Parse(bad, world.Root));
        var options = LiveLaneStatusOptions.Parse(["--require-claude-version", "2.1.4,2.1.3,2.1.4"], "C:/root");
        options.RequiredClaudeVersions.ShouldBe(["2.1.4", "2.1.3"]);
        options.RepositoryRoot.ShouldBe("C:/root");
        options.Lane.ShouldBeNull();
        options.CandidateSha.ShouldBeNull();
        options.ReusePolicy.ShouldBe("AlwaysRerun");
        options.Json.ShouldBeFalse();
    }

    /// <summary>A lane repository at Y and a history directory of receipts (the receipt tests' layout).</summary>
    private sealed class World
    {
        public World(LaneScratch scratch, bool secondLane = false)
        {
            var repo = new LiveLaneRepo(scratch.Path("repository"));
            repo.Put("tests/rc-project-categories.json", "{\"schemaVersion\":1,\"projects\":{\"tests/Pty.Tests/Pty.Tests.csproj\":[\"fixture\"]},\"batches\":[\"tests/Pty.Tests/Pty.Tests.csproj\"]}");
            repo.Put("tests/Pty.Tests/Pty.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"TUnit\" Version=\"1.0\" /></ItemGroup></Project>");
            repo.Put("tests/Pty.Tests/T.cs", "namespace Antiphon.Agents.Pty.Tests;\n");
            repo.Put("scripts/lib/live-lane-contract.ps1", "# contract\n");
            repo.Put("CLAUDE.md", "# fixture\n");
            repo.Put("tests/live-lanes.json", LiveLaneData.Canonical(LiveLaneLaneFixture.Manifest(Roster)));
            const string entry = "{\"manifest\":\"tests/live-lanes.json\",\"contractFiles\":[\"tests/live-lanes.json\",\"tests/live-lane-inputs.json\"],\"minimumRoots\":[\"tests/Pty.Tests/\"],"
                + "\"runtimeRoots\":[\"scripts/\"],\"namespaceRoots\":[\"Antiphon.Agents.Pty\"],\"instructionPatterns\":[\"CLAUDE.md\"]}";
            repo.Put("tests/live-lane-inputs.json", "{\"format\":\"antiphon-live-lane-inputs-v1\",\"lanes\":{\"fixture-lane\":" + entry
                + (secondLane ? ",\"fixture-lane-b\":" + entry : "") + "}}");
            Root = repo.Root;
            X = repo.Commit("X");
            repo.Put("docs/notes.md", "candidate\n");
            Y = repo.Commit("Y");
            var fingerprint = LiveLaneFingerprint.Compute(LiveLaneTestSources.Create(), Root, Y, Lane);
            fingerprint.Complete.ShouldBeTrue(string.Join(",", fingerprint.UnknownReasons));
            CodeComponents = fingerprint.Components!;
            CodeDigest = fingerprint.Digest!;
            History = scratch.Path("history");
            Directory.CreateDirectory(History);
            File.WriteAllText(Path.Combine(History, "console.log"), "console bytes\n");
            Context = scratch.Path("context.json");
            File.WriteAllText(Context, LiveLaneData.Canonical(new LiveLaneMap(("repository", "fixture/antiphon"), ("ref", "rc/fixture-1"), ("host", "host-A"),
                ("account", "account-A"), ("nonce", "nonce-A"), ("nowUtc", LiveLaneReceipt.Iso(Now)), ("probes", new List<object>()), ("revocations", new List<object>()))));
        }

        public string Root { get; }
        public string X { get; }
        public string Y { get; }
        public LiveLaneMap CodeComponents { get; }
        public string CodeDigest { get; }
        public string History { get; }
        public string Context { get; }

        public LiveLaneStatusOptions Options(IReadOnlyList<string>? required = null) =>
            LiveLaneStatusOptions.Of(Root, Lane, Y, "", History, Context, required: required);

        /// <summary>A receipt of the run: the code components plus an attested environment; version null writes a schema 1 receipt.</summary>
        public LiveLaneExecutionReceipt Receipt(string runId, string? version, DateTimeOffset ended, string outcome = "Passed", string? testSources = null, string lane = Lane)
        {
            var components = (LiveLaneMap)LiveLaneData.Parse(LiveLaneData.Canonical(CodeComponents))!;
            components["environment"] = new string('e', 64);
            if (testSources != null) components["testSources"] = testSources;
            var rows = Roster.Select((uid, i) => new KeyValuePair<string, string>(uid, i == 1 ? outcome : "Passed")).ToList();
            var failed = outcome == "Failed";
            var receipt = new LiveLaneExecutionReceipt
            {
                LaneId = lane, RunId = runId, Repository = "fixture/antiphon", Ref = "rc/fixture-1", SourceSha = X, SourceClean = true, HostEnrollmentId = "host-A",
                AccountEnrollmentId = "account-A", StartedUtc = ended.AddMinutes(-5), EndedUtc = ended, DurationMs = 300000,
                FingerprintDigest = LiveLaneFingerprint.Digest(lane, components), FingerprintComponents = components, RosterDigest = LiveLaneReceipt.RosterDigest(Roster),
                InputBefore = new string('a', 64), InputAfter = new string('a', 64), ExternalEpoch = "epoch-A", Rows = rows,
                ClassifierVerdict = failed ? 1 : 0, FinalExit = failed ? 1 : 0,
                Artifacts = [new("console.log", LiveLaneData.Sha256Text("console bytes\n"))],
                Claude = version == null ? LiveLaneClaudeVersion.Unknown : new LiveLaneClaudeVersion(version, version + " (Claude Code)")
            };
            return receipt;
        }

        public string Write(string name, LiveLaneExecutionReceipt receipt)
        {
            if (receipt.Claude.Known) receipt.Write(Path.Combine(History, name + ".json"));
            else
            {
                // A schema 1 receipt, as the CARD-1545 S2b receipts were written: no claude field.
                var document = receipt.ToJson();
                document["schemaVersion"] = 1L;
                document.Remove("claude");
                return WriteRaw(name, document);
            }
            return Path.Combine(History, name + ".json");
        }

        public string WriteRaw(string name, LiveLaneMap document)
        {
            var path = Path.Combine(History, name + ".json");
            File.WriteAllText(path, LiveLaneData.Canonical(document));
            return path;
        }
    }
}
