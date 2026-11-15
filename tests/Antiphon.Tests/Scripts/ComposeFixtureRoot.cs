using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;

namespace Antiphon.Tests.Scripts;

// Parent-owned even when a timed-out node/PTY child is forcibly terminated.
internal sealed class ComposeFixtureRoot : IDisposable
{
    private readonly string token = Guid.NewGuid().ToString("N");
    private readonly string prefix;
    private readonly string basePath = Path.GetFullPath(OperatingSystem.IsLinux() ? "/var/tmp" : Path.GetTempPath())
        .TrimEnd(Path.DirectorySeparatorChar);
    private bool markerWritten;
    internal string Root { get; }

    internal ComposeFixtureRoot(string prefix)
    {
        this.prefix = prefix;
        Root = Path.Combine(basePath, prefix + token);
        Directory.Exists(Root).ShouldBeFalse("fixture only owns a unique new directory");
        Directory.CreateDirectory(Root);
        try
        {
            File.WriteAllText(Path.Combine(Root, ".compose-fixture-owner"), token);
            markerWritten = true;
        }
        catch { Dispose(); throw; }
    }

    internal void Bind(ProcessStartInfo start)
    {
        start.Environment["ANTIPHON_COMPOSE_FIXTURE_ROOT"] = Root;
        start.Environment["ANTIPHON_COMPOSE_FIXTURE_TOKEN"] = token;
        start.Environment.Remove("ANTIPHON_COMPOSE_FIXTURE_WORKER");
    }

    public void Dispose()
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "compose-fixture-delete");
        Path.GetDirectoryName(Root).ShouldBe(basePath);
        Path.GetFileName(Root).ShouldBe(prefix + token);
        if (!Directory.Exists(Root)) return;
        (File.GetAttributes(Root) & FileAttributes.ReparsePoint).ShouldBe((FileAttributes)0);
        if (markerWritten)
        {
            var marker = Path.Combine(Root, ".compose-fixture-owner");
            (File.GetAttributes(marker) & FileAttributes.ReparsePoint).ShouldBe((FileAttributes)0);
            File.ReadAllText(marker).ShouldBe(token);
        }
        Directory.Delete(Root, recursive: markerWritten);
    }
}
