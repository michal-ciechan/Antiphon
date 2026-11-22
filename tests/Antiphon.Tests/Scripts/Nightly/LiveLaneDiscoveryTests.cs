using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S24. The live-lane discovery, roster, export, receipt and scratch of live-lane-manifest.ps1, compiled
/// (scripts/lib/Antiphon.Nightly/LiveLane*.cs): the old ExtraArgumentDecoyFirst, ExportPending, ProducerReceipt,
/// ScratchResidue, BindManifest and WriteFixture cases of scripts/test-live-lane-manifest.ps1, in-process. The
/// roster rows themselves are LiveLaneInventoryTests.ExactCoreRoster and LiveClaudeInventoryTests.ExactPtyCoreRoster.
/// Scratch lives under .antiphon/live-lane-capture and is removed.
/// </summary>
[Category("Unit")]
public sealed class LiveLaneDiscoveryTests
{
    private static string Repo => LiveLaneInventoryTests.LiveLaneProcess.FindRepo();

    [Test]
    public void ExtraArgumentDecoyFirst()
    {
        var failures = LiveLaneRoster.ExtraArgumentDecoyFirstWitness(Repo);
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    [Test]
    public void ExportPending()
    {
        var problems = LiveLaneExports.ExportPendingWitness(Repo);
        problems.ShouldBeEmpty(string.Join('\n', problems));
    }

    /// <summary>
    /// The receipt shapes. The case's census check (census-skipped-receipt) needs the census, which is still
    /// PowerShell: FullCensusUnchanged runs it until S25.
    /// </summary>
    [Test]
    public void ProducerReceipt()
    {
        var problems = LiveLaneReceipt.ProducerReceiptWitness(Repo);
        problems.ShouldBeEmpty(string.Join('\n', problems));
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void ScratchResidue()
    {
        var repo = Repo;
        var problems = LiveLaneScratch.ScratchResidueWitness(repo, LiveLaneManifestFiles.FixtureRel, ProtectedPaths(repo));
        problems.ShouldBeEmpty(string.Join('\n', problems));
    }

    /// <summary>
    /// The captures bound to the committed core identities, each with the uid and categories the committed manifest
    /// has, give back tests/live-lanes.json byte for byte; a missing or doubled node and a missing capture refuse.
    /// </summary>
    [Test]
    public void BindManifest()
    {
        var repo = Repo;
        var scratch = new LiveLaneScratch();
        try
        {
            var capture = scratch.NewOwned(repo, "bindmanifest" + Guid.NewGuid().ToString("N"));
            var committedPath = Path.Combine(repo, "tests", "live-lanes.json");
            using var committed = JsonDocument.Parse(File.ReadAllText(committedPath));
            var core = committed.RootElement.GetProperty("core").EnumerateArray().ToList();
            core.Count.ShouldBe(LiveLaneContract.Core.Count);
            List<LiveLaneNode> Nodes(string project) => core.Where(row => row.GetProperty("project").GetString() == project).Select(row => new LiveLaneNode
            {
                Project = project,
                Uid = row.GetProperty("uid").GetString()!,
                Namespace = row.GetProperty("namespace").GetString()!,
                Type = row.GetProperty("class").GetString()!,
                Method = row.GetProperty("method").GetString()!,
                Arguments = row.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()!).ToArray(),
                Categories = row.GetProperty("categories").EnumerateArray().Select(a => a.GetString()!).ToArray(),
            }).ToList();
            var tests = Nodes(LiveLaneRoster.TestsProject);
            var pty = Nodes(LiveLaneRoster.PtyProject);
            // A decoy of the same method under another argument is not a second hit.
            var submit = pty.Single(n => n.Method == "Submitting_via_two_writes_completes_a_turn");
            pty.Insert(0, new LiveLaneNode { Project = submit.Project, Uid = "decoy", Namespace = submit.Namespace, Type = submit.Type, Method = submit.Method, Arguments = ["fakeclaude"] });
            WriteCapture(capture, LiveLaneRoster.TestsProject, tests);
            WriteCapture(capture, LiveLaneRoster.PtyProject, pty);

            var output = Path.Combine(capture, "live-lanes.json");
            LiveLaneManifestFiles.BindManifest(repo, capture, output).ShouldBe(output);
            File.ReadAllText(output).ReplaceLineEndings("\n").ShouldBe(File.ReadAllText(committedPath).ReplaceLineEndings("\n"));

            WriteCapture(capture, LiveLaneRoster.PtyProject, pty.Where(n => n.Uid != submit.Uid).ToList());
            Refusal(() => LiveLaneManifestFiles.BindManifest(repo, capture, output)).ShouldBe("bind-miss Submitting_via_two_writes_completes_a_turn count=0");
            WriteCapture(capture, LiveLaneRoster.PtyProject, pty.Concat([submit]).ToList());
            Refusal(() => LiveLaneManifestFiles.BindManifest(repo, capture, output)).ShouldBe("bind-miss Submitting_via_two_writes_completes_a_turn count=2");
            File.Delete(Path.Combine(capture, LiveLaneRoster.PtyProject + ".discovery.json"));
            Refusal(() => LiveLaneManifestFiles.BindManifest(repo, capture, output)).ShouldBe("capture-missing Antiphon.Agents.Pty.Tests");
        }
        finally
        {
            scratch.Clear();
        }
    }

    /// <summary>
    /// The committed census fixture's nodes, split back into the two roster captures, give back the fixture byte for
    /// byte (its producing source and per-project hashes from the captures); a missing capture refuses.
    /// </summary>
    [Test]
    public void WriteFixture()
    {
        var repo = Repo;
        var scratch = new LiveLaneScratch();
        try
        {
            var capture = scratch.NewOwned(repo, "writefixture" + Guid.NewGuid().ToString("N"));
            var committedPath = Path.Combine(repo, LiveLaneManifestFiles.FixtureRel);
            var committedText = File.ReadAllText(committedPath);
            using (var committed = JsonDocument.Parse(committedText))
            {
                var producing = committed.RootElement.GetProperty("producing");
                var nodes = committed.RootElement.GetProperty("nodes").EnumerateArray().ToList();
                foreach (var project in producing.GetProperty("projects").EnumerateArray())
                {
                    var name = project.GetProperty("project").GetString()!;
                    WriteCaptureElements(capture, name, project.GetProperty("assemblyHash").GetString()!,
                        nodes.Where(n => n.TryGetProperty("project", out var p) && p.GetString() == name));
                }
                var output = Path.Combine(capture, "fixture", "full-discovery-before.json");
                LiveLaneManifestFiles.WriteFixture(repo, capture, producing.GetProperty("sourceSha").GetString(), output).ShouldBe(output);
                var written = File.ReadAllText(output).ReplaceLineEndings("\n");
                var expected = committedText.ReplaceLineEndings("\n");
                var at = 0;
                while (at < written.Length && at < expected.Length && written[at] == expected[at]) at++;
                string Near(string text) => text.Substring(Math.Max(0, at - 80), Math.Min(text.Length, at + 80) - Math.Max(0, at - 80));
                (at == written.Length && at == expected.Length).ShouldBeTrue("the rewritten fixture differs at " + at + ": written=" + Near(written) + " committed=" + Near(expected));
            }
            File.Delete(Path.Combine(capture, LiveLaneRoster.PtyProject + ".discovery.json"));
            Should.Throw<FileNotFoundException>(() => LiveLaneManifestFiles.WriteFixture(repo, capture, "sha", Path.Combine(capture, "other.json")));
        }
        finally
        {
            scratch.Clear();
        }
    }

    /// <summary>The census's protected inputs: $script:LiveLaneProtectedPaths of scripts/lib/live-lane-manifest.ps1 (S25 moves the census).</summary>
    private static string[] ProtectedPaths(string repo)
    {
        var text = File.ReadAllText(Path.Combine(repo, "scripts", "lib", "live-lane-manifest.ps1"));
        var block = Regex.Match(text, @"\$script:LiveLaneProtectedPaths = @\((?<body>[^)]*)\)");
        block.Success.ShouldBeTrue("LiveLaneProtectedPaths");
        var paths = Regex.Matches(block.Groups["body"].Value, "'([^']+)'").Select(m => m.Groups[1].Value).ToArray();
        paths.ShouldNotBeEmpty();
        return paths;
    }

    private static string Refusal(Action action) => Should.Throw<InvalidOperationException>(action).Message;

    // A roster capture as LiveLaneDiscovery.Run writes it (only the fields the writers read).
    private static void WriteCapture(string capture, string project, IReadOnlyList<LiveLaneNode> nodes)
    {
        using var stream = File.Create(Path.Combine(capture, project + ".discovery.json"));
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("format", "antiphon-live-lane-discovery-capture-v1");
        writer.WriteString("project", project);
        writer.WriteString("assemblyHash", new string('a', 64));
        writer.WriteStartArray("nodes");
        foreach (var node in nodes)
        {
            writer.WriteStartObject();
            writer.WriteString("project", node.Project);
            writer.WriteString("uid", node.Uid);
            writer.WriteString("namespace", node.Namespace);
            writer.WriteString("type", node.Type);
            writer.WriteString("method", node.Method);
            writer.WriteStartArray("arguments");
            foreach (var a in node.Arguments) writer.WriteStringValue(a);
            writer.WriteEndArray();
            writer.WriteStartArray("categories");
            foreach (var c in node.Categories) writer.WriteStringValue(c);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCaptureElements(string capture, string project, string assemblyHash, IEnumerable<JsonElement> nodes)
    {
        using var stream = File.Create(Path.Combine(capture, project + ".discovery.json"));
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        writer.WriteStartObject();
        writer.WriteString("project", project);
        writer.WriteString("assemblyHash", assemblyHash);
        writer.WritePropertyName("nodes");
        writer.WriteStartArray();
        foreach (var node in nodes) node.WriteTo(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
