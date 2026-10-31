using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>C680 and completed-artifact sanitizer witnesses. No native process or provider call.</summary>
[Category("Unit"), Timeout(30_000)]
public class LiveLaneEvidenceTests
{
    internal const string Link = "https://claude.ai/code/session/SENTINEL-LINK-3a7c132f";
    internal const string Screen = "SENTINEL-SCREEN-3a7c132f";
    internal const string Secret = "SENTINEL-TOKEN-3a7c132f";
    internal const string Device = "SENTINEL-DEVICE-3a7c132f";

    [Test]
    public void C680EvidenceIsSanitized()
    {
        var root = Path.Combine("/tmp", "c1545-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = Guid.NewGuid();
            var diag = new ClaudeDialogCanaryDiagnostics(root, Path.Combine(root, "sessions"),
                Path.Combine(root, "cwd"), session, "v", "m", "Bash");
            var raw = Link + "\n" + Screen + "\n" + Secret + "\n" + Device;
            diag.Composer(raw, empty: true, settled: true);
            diag.Draft(raw, complete: true, settled: true);
            diag.Observe(new SessionRunnerSnapshotDto(session, raw, raw, 1, DateTime.UtcNow),
                new SessionRunnerTranscriptDto(session, [
                    new SessionRunnerTranscriptEvent(session, 1, TranscriptKinds.UserPrompt, null, null, null, null, raw, null, null, null, null, null)
                ], 1), blocked: false, submitted: true, called: false);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (text.Contains(Link, StringComparison.Ordinal) || text.Contains(Screen, StringComparison.Ordinal)
                    || text.Contains(Secret, StringComparison.Ordinal) || text.Contains(Device, StringComparison.Ordinal))
                    throw new InvalidOperationException("C1545 V-7/diagnostics payloadHasSentinel");
            }
            var composer = Directory.EnumerateFiles(root, "composer-screen.txt", SearchOption.AllDirectories).Single();
            if (!File.ReadAllText(composer).Contains("C1545-EVIDENCE case=c680-composer", StringComparison.Ordinal))
                throw new InvalidOperationException("C1545 V-7/suppress-marker actualSinkHasCaseMarker");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void ArtifactGateRequiresPresence()
    {
        var script = Path.Combine(FindRepo(), "scripts", "lib", "live-lane-evidence.ps1");
        if (!File.Exists(script))
            throw new InvalidOperationException("C1545 V-8/entry required evidence scanner is absent");
        throw new InvalidOperationException("C1545 V-8/marker-missing evidence-sanitization");
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("C1545 V-8/repo");
    }
}
