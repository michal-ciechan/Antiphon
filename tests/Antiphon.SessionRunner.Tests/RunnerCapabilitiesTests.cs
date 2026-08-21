using System.Text.Json;
using System.Xml.Linq;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public class RunnerCapabilitiesTests
{
    [Test]
    public void Declared_transcript_formats_match_the_formats_implemented_by_the_runner()
    {
        // The runtime's list is the gate used before its Grok/Codex/Claude dispatch branches and
        // is what /capabilities reports. The shared constants enumerate the contract's formats, so
        // this is the mechanical drift alarm: a newly named format needs a runner branch too.
        var dispatchFormats = Enum.GetValues<SessionRunnerRuntime.TranscriptTailerKind>()
            .Select(tailer => SessionRunnerRuntime.SupportedTranscriptFormats[(int)tailer])
            .ToArray();
        dispatchFormats.ShouldBe(SessionRunnerRuntime.SupportedTranscriptFormats);
        dispatchFormats.ShouldBe([TranscriptFormats.Claude, TranscriptFormats.Grok, TranscriptFormats.Codex]);
    }

    [Test]
    public void Capability_dto_round_trips_with_old_and_new_wire_shapes()
    {
        var oldShape = new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false);
        var oldRoundTrip = JsonSerializer.Deserialize<RunnerCapabilitiesDto>(JsonSerializer.Serialize(oldShape));
        oldRoundTrip!.TranscriptFormats.ShouldBeNull();
        oldRoundTrip.Build.ShouldBeNull();

        var build = new RunnerBuildDto("1.0.0+0123456789012345678901234567890123456789",
            "0123456789012345678901234567890123456789", DateTime.UnixEpoch, DateTime.UnixEpoch.AddMinutes(1));
        var current = oldShape with { TranscriptFormats = SessionRunnerRuntime.SupportedTranscriptFormats, Build = build };
        var currentRoundTrip = JsonSerializer.Deserialize<RunnerCapabilitiesDto>(JsonSerializer.Serialize(current));
        currentRoundTrip!.TranscriptFormats.ShouldBe(SessionRunnerRuntime.SupportedTranscriptFormats);
        currentRoundTrip.Build.ShouldBe(build);
    }

    [Test]
    public void Build_identity_parses_the_sdk_sourcelink_shape_only()
    {
        RunnerBuildIdentity.TryParseCommitSha("1.0.0+0123456789012345678901234567890123456789")
            .ShouldBe("0123456789012345678901234567890123456789");
        RunnerBuildIdentity.TryParseCommitSha("1.0.0").ShouldBeNull();
        RunnerBuildIdentity.TryParseCommitSha("1.0.0+not-a-sha").ShouldBeNull();
    }

    [Test]
    public async Task Daemon_build_script_covers_the_runner_project_reference_closure()
    {
        var root = FindRepoRoot();
        var script = await File.ReadAllTextAsync(Path.Combine(root, "scripts", "check-daemon-build.ps1"));
        var closure = ProjectClosure(root, "src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj");

        foreach (var project in closure)
        {
            var directory = Path.GetDirectoryName(project)!;
            var relative = Path.GetRelativePath(root, directory).Replace('\\', '/');
            script.ShouldContain(relative, customMessage: $"daemon check omits runner dependency {relative}");
        }
    }

    private static IReadOnlyCollection<string> ProjectClosure(string root, string entryProject)
    {
        var pending = new Queue<string>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(Path.GetFullPath(Path.Combine(root, entryProject)));
        while (pending.TryDequeue(out var project))
        {
            if (!found.Add(project))
                continue;

            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (!string.IsNullOrWhiteSpace(include))
                    pending.Enqueue(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include)));
            }
        }

        return found;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
