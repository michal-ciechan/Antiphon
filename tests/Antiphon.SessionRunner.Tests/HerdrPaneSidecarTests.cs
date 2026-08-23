using Antiphon.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public class HerdrPaneSidecarTests
{
    [Test]
    public void save_load_round_trips_atomically_and_load_all_sweeps()
    {
        var root = Path.Combine(Path.GetTempPath(), "herdr-sidecar-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sessionId = Guid.NewGuid();
            var sidecar = new HerdrPaneSidecar
            {
                SessionId = sessionId,
                WorkspaceKey = "project:abc",
                WorkspaceId = "w2",
                TabId = "w2:t1",
                PaneId = "w2:p1",
                ChildPid = 4242,
                ShellPid = 100,
                LaunchedAtUtc = DateTime.UtcNow,
                Cwd = @"C:\src\Antiphon",
                UpdatedAtUtc = DateTime.UtcNow,
            };
            var path = HerdrPaneSidecar.PathFor(root, sessionId);
            sidecar.SaveAtomic(path);

            var loaded = HerdrPaneSidecar.TryLoad(path);
            loaded.ShouldNotBeNull();
            loaded!.SessionId.ShouldBe(sessionId);
            loaded.WorkspaceKey.ShouldBe("project:abc");
            loaded.PaneId.ShouldBe("w2:p1");
            loaded.ChildPid.ShouldBe(4242);

            HerdrPaneSidecar.LoadAll(root).Select(s => s.SessionId).ShouldContain(sessionId);

            HerdrPaneSidecar.TryDelete(root, sessionId);
            HerdrPaneSidecar.TryLoad(path).ShouldBeNull();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void try_load_tolerates_corrupt_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "herdr-sidecar-bad-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = HerdrPaneSidecar.PathFor(root, Guid.NewGuid());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{not-json");
            HerdrPaneSidecar.TryLoad(path).ShouldBeNull();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
