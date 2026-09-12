using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
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
                AcceptedStartedAt = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-1)),
            };
            var path = HerdrPaneSidecar.PathFor(root, sessionId);
            sidecar.SaveAtomic(path);

            var loaded = HerdrPaneSidecar.TryLoad(path);
            loaded.ShouldNotBeNull();
            loaded!.SessionId.ShouldBe(sessionId);
            loaded.WorkspaceKey.ShouldBe("project:abc");
            loaded.PaneId.ShouldBe("w2:p1");
            loaded.ChildPid.ShouldBe(4242);
            loaded.AcceptedStartedAt.ShouldBe(sidecar.AcceptedStartedAt);

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
    public void A_sidecar_without_the_field_loads_a_null_generation()
    {
        var root = Path.Combine(Path.GetTempPath(), "herdr-sidecar-old-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sessionId = Guid.NewGuid();
            var path = HerdrPaneSidecar.PathFor(root, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                $$"""{"schemaVersion":1,"sessionId":"{{sessionId:D}}","workspaceKey":"none","workspaceId":"w","tabId":"t","paneId":"p","launchedAtUtc":"2026-01-01T00:00:00Z","updatedAtUtc":"2026-01-01T00:00:00Z"}""");
            HerdrPaneSidecar.TryLoad(path)!.AcceptedStartedAt.ShouldBeNull();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void last_pane_round_trips_and_retire_moves_atomically()
    {
        var root = Path.Combine(Path.GetTempPath(), "herdr-last-pane-" + Guid.NewGuid().ToString("N"));
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
                AgentKind = "grok",
                Origin = HerdrPaneOrigins.Launched,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(root, sessionId));

            HerdrPaneSidecar.Retire(root, sessionId, HerdrExitReasons.RestartPresumedDead);

            File.Exists(HerdrPaneSidecar.PathFor(root, sessionId)).ShouldBeFalse();
            var last = HerdrLastPane.TryLoad(root, sessionId);
            last.ShouldNotBeNull();
            last!.SessionId.ShouldBe(sessionId);
            last.PaneId.ShouldBe("w2:p1");
            last.WorkspaceKey.ShouldBe("project:abc");
            last.LastChildPid.ShouldBe(4242);
            last.Origin.ShouldBe(HerdrPaneOrigins.Launched);
            last.ExitReason.ShouldBe(HerdrExitReasons.RestartPresumedDead);

            last.SaveAtomic(HerdrLastPane.PathFor(root, sessionId));
            var reloaded = HerdrLastPane.TryLoad(root, sessionId);
            reloaded.ShouldNotBeNull();
            reloaded!.PaneId.ShouldBe(last.PaneId);

            HerdrLastPane.TryDelete(root, sessionId);
            HerdrLastPane.TryLoad(root, sessionId).ShouldBeNull();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void retire_of_an_attached_sidecar_writes_no_last_pane_record()
    {
        var root = Path.Combine(Path.GetTempPath(), "herdr-last-pane-att-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sessionId = Guid.NewGuid();
            var sidecar = new HerdrPaneSidecar
            {
                SessionId = sessionId,
                WorkspaceKey = "none",
                WorkspaceId = "w2",
                TabId = "w2:t1",
                PaneId = "w2:p1",
                ChildPid = 1,
                ShellPid = 1,
                LaunchedAtUtc = DateTime.UtcNow,
                Origin = HerdrPaneOrigins.Attached,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(root, sessionId));
            HerdrPaneSidecar.Retire(root, sessionId, HerdrExitReasons.PaneClosed);
            File.Exists(HerdrPaneSidecar.PathFor(root, sessionId)).ShouldBeFalse();
            File.Exists(HerdrLastPane.PathFor(root, sessionId)).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Placement_labels_round_trip_and_old_files_load_with_null_labels()
    {
        var root = Path.Combine(Path.GetTempPath(), "herdr-sidecar-labels-" + Guid.NewGuid().ToString("N"));
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
                ChildPid = 1,
                ShellPid = 1,
                LaunchedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                WorkspaceLabel = "PredictionMarkets",
                TabLabel = "Orch",
            };
            sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(root, sessionId));
            var json = File.ReadAllText(HerdrPaneSidecar.PathFor(root, sessionId));
            json.ShouldContain("\"workspaceLabel\"");
            json.ShouldContain("\"tabLabel\"");
            var loaded = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(root, sessionId))!;
            loaded.WorkspaceLabel.ShouldBe("PredictionMarkets");
            loaded.TabLabel.ShouldBe("Orch");

            var oldPath = HerdrPaneSidecar.PathFor(root, Guid.NewGuid());
            Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
            File.WriteAllText(oldPath, """
                {"schemaVersion":1,"sessionId":"00000000-0000-0000-0000-000000000001","workspaceKey":"k","workspaceId":"w","tabId":"t","paneId":"p","launchedAtUtc":"2026-01-01T00:00:00Z","updatedAtUtc":"2026-01-01T00:00:00Z"}
                """);
            var old = HerdrPaneSidecar.TryLoad(oldPath)!;
            old.WorkspaceLabel.ShouldBeNull();
            old.TabLabel.ShouldBeNull();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void FromSidecar_and_FromLaunchRequest_copy_labels()
    {
        var sidecar = new HerdrPaneSidecar
        {
            SessionId = Guid.NewGuid(),
            WorkspaceKey = "k",
            WorkspaceId = "w",
            TabId = "t",
            PaneId = "p",
            LaunchedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            WorkspaceLabel = "PredictionMarkets",
            TabLabel = "Orch",
        };
        var last = HerdrLastPane.FromSidecar(sidecar, "x");
        last.WorkspaceLabel.ShouldBe("PredictionMarkets");
        last.TabLabel.ShouldBe("Orch");

        var fromReq = HerdrLastPane.FromLaunchRequest(
            new RunnerLaunchRequest(
                sidecar.SessionId, "e", [], new Dictionary<string, string>(), "c", 120, 30),
            new HerdrLaunchOptions("k", "PredictionMarkets", "c", "title", TabLabel: "Orch"),
            "w", "t", "p", 1, "timeout");
        fromReq.WorkspaceLabel.ShouldBe("PredictionMarkets");
        fromReq.TabLabel.ShouldBe("Orch");
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
