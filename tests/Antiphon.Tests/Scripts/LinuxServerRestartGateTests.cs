using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.LinuxServerRestartWorld;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1745. The Linux server SHA gate is off until a config names the server. --execute probes first and does not stop a
/// serving container. The desktop AppHost scripts stay uncoupled. Unit 64 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md
/// made the gate the restart-linux-server command (Antiphon.Nightly.LinuxServerRestart) and deleted its driver
/// scripts/test-restart-linux-server.ps1, whose cases W1-W17 are these tests, in process on every platform (LinuxServerRestartWorld:
/// a mock disk, a recording HTTP seam and a recording docker launcher). Each pins the driver's rows and the lines captured from
/// the script before its deletion. The command's own tables are LinuxServerRestartTests.
/// </summary>
[Category("Unit")]
public sealed class LinuxServerRestartGateTests
{
    private static readonly string[] DesktopPaths =
    [
        "scripts/restart-apphost.ps1",
        "scripts/autostart-apphost.ps1",
        "Antiphon.AppHost/Program.cs",
        // Unit 31 of 2026-11-22-remaining-ps1-to-csharp.md: the desktop restart, its library and the logon start are C#.
        "scripts/lib/Antiphon.Nightly/AppHostRestart.cs",
        "scripts/lib/Antiphon.Nightly/AppHostCommon.cs",
        "scripts/lib/Antiphon.Nightly/AppHostAutostart.cs",
        // Unit 37: dev-aspire is C# (unit 44 deleted its dev-aspire.ps1 launcher).
        "scripts/lib/Antiphon.Nightly/DevAspire.cs",
        // Unit 49: the desktop deploy is C# (it deleted deploy-local.ps1 and the worktree guard's scripts/apphost-common.ps1).
        "scripts/lib/Antiphon.Nightly/DeployLocal.cs",
    ];

    private const string Gate = "scripts/lib/Antiphon.Nightly/LinuxServerRestart.cs";

    private static string[] Unverified(string expected, string observed, string reason, string lockPath, bool execute, string container = "antiphon-staging", string? tag = null)
    {
        var lines = new List<string>
        {
            "REFUSED: server build unverified (exit 5).",
            "  expected SHA: " + expected,
            "  observed SHA: " + observed,
            "  reason: " + reason,
            "  lock: " + lockPath,
        };
        if (tag != null)
        {
            lines.Add("  tag image: " + tag);
            lines.Add("  The image tag moved. This gate does not recreate the container.");
        }
        lines.Add("Nothing was restarted.");
        if (execute)
        {
            lines.Add("The serving container was left running.");
            lines.Add("recovery: docker start " + container);
        }
        return lines.ToArray();
    }

    private static string[] Probed => [BaseUrl.TrimEnd('/') + "/health", BaseUrl.TrimEnd('/') + "/api/version"];

    private static readonly string[] SameImageInspects =
    [
        "inspect --format {{.Image}} antiphon-staging",
        "inspect --format {{.Config.Image}} antiphon-staging",
        "image inspect --format {{.Id}} antiphon-server:current",
    ];

    [Test]
    public void Unconfigured_refuses_and_restarts_nothing()
    {
        // W1-exit-3, W1-says-not-configured, W1-says-nothing-restarted, W1-no-docker, W1-no-lock.
        var world = new LinuxServerRestartWorld();
        var run = world.Run();
        run.Exit.ShouldBe(3, run.ToString());
        run.Lines.ShouldBe(["REFUSED: Linux server is not configured.", "Nothing was restarted."]);
        run.Docker.ShouldBeEmpty();
        run.Http.ShouldBeEmpty();
        world.LockText.ShouldBeNull();
    }

    [Test]
    public void WhatIf_match_exits_0_without_docker()
    {
        // W2: an upper-case version matches the lower-cased expected SHA.
        var world = new LinuxServerRestartWorld();
        world.Serve(Version(Sha64.ToUpperInvariant()));
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha64, "--what-if");
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe(["VERIFIED sha=" + Sha64 + " mode=whatif"]);
        run.Http.ShouldBe(Probed);
        run.Docker.ShouldBeEmpty();
        world.LockText.ShouldBeNull();
    }

    [Test]
    public void WhatIf_mismatch_exits_5_and_leaves_lock()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve(Version(Other));
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha64, "--what-if");
        run.Exit.ShouldBe(5, run.ToString());
        run.Lines.ShouldBe(Unverified(Sha64, Other, "mismatch", world.Lock, execute: false));
        world.LockText.ShouldBe(world.ExpectedLock("mismatch", Sha64, Other, "whatif"));
        run.Docker.ShouldBeEmpty();
        run.Out.ShouldNotContain("VERIFIED", Case.Sensitive);
    }

    [Test]
    public void WhatIf_health_failure_is_not_verified()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("{\"version\":\"" + Sha40 + "\"}", healthCode: 503);
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40, "--what-if");
        run.Exit.ShouldBe(1, run.ToString());
        run.Lines.ShouldBe(["REFUSED: /health was not 200.", "Nothing was restarted."]);
        run.Http.ShouldBe([Probed[0]]);
        world.LockText.ShouldBeNull();
        run.Docker.ShouldBeEmpty();
    }

    [Test]
    public void Execute_match_reports_already_at_expected_sha_without_stopping()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve(Version(Sha40));
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40, "--execute");
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe(["already at expected SHA", "VERIFIED sha=" + Sha40 + " mode=execute"]);
        run.Docker.ShouldBeEmpty();
        world.LockText.ShouldBeNull();
    }

    [Test]
    public void Execute_mismatch_refuses_before_stopping()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("{\"version\":\"" + Other + "\"}");
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40, "--execute");
        run.Exit.ShouldBe(5, run.ToString());
        run.Lines.ShouldBe(Unverified(Sha40, Other, "mismatch", world.Lock, execute: true));
        world.LockText.ShouldBe(world.ExpectedLock("mismatch", Sha40, Other, "execute", "sha256:c1745previous"));
        // W6-inspected-same-image and W6-no-stop: three inspects, no stop or start.
        run.Docker.ShouldBe(SameImageInspects);
        run.DockerFiles.ShouldBe([world.DockerPath]);
        run.Out.ShouldNotContain("VERIFIED", Case.Sensitive);
        run.Out.ShouldNotContain("left stopped", Case.Sensitive);
    }

    [Test]
    public void Malformed_sha_refuses_before_docker()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("{\"version\":\"" + Sha40 + "\"}");
        var run = world.Run("--config", world.Config(), "--expected-server-sha", "abc", "--execute");
        run.Exit.ShouldBe(3, run.ToString());
        run.Lines.ShouldBe(["REFUSED: -ExpectedServerSha is not a full 40- or 64-character SHA.", "Nothing was restarted."]);
        run.Docker.ShouldBeEmpty();
        run.Http.ShouldBeEmpty();
    }

    [Test]
    public void Desktop_apphost_port_refuses()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("{\"version\":\"" + Sha40 + "\"}");
        var run = world.Run("--config", world.Config("http://127.0.0.1:17202/"), "--expected-server-sha", Sha40, "--execute");
        run.Exit.ShouldBe(3, run.ToString());
        run.Lines.ShouldBe(["REFUSED: baseUrl port 17202 is a desktop AppHost port.", "Nothing was restarted."]);
        run.Docker.ShouldBeEmpty();
        run.Http.ShouldBeEmpty();
    }

    [Test]
    public void Runner_container_refuses()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("{\"version\":\"" + Sha40 + "\"}");
        var run = world.Run("--config", world.Config(container: "session-runner"), "--expected-server-sha", Sha40, "--execute");
        run.Exit.ShouldBe(3, run.ToString());
        run.Lines.ShouldBe(["REFUSED: container session-runner is outside this gate.", "Nothing was restarted."]);
        run.Docker.ShouldBeEmpty();
    }

    [Test]
    public void Omitted_switches_are_whatif()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("{\"version\":\"" + Sha40 + "\"}");
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40);
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe(["VERIFIED sha=" + Sha40 + " mode=whatif"]);
        run.Docker.ShouldBeEmpty();
    }

    [Test]
    public void Malformed_version_exits_5_and_leaves_lock()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("not-json");
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40, "--what-if");
        run.Exit.ShouldBe(5, run.ToString());
        run.Lines.ShouldBe(Unverified(Sha40, "", "malformed", world.Lock, execute: false));
        world.LockText.ShouldBe(world.ExpectedLock("malformed", Sha40, "", "whatif"));
        run.Docker.ShouldBeEmpty();
    }

    [Test]
    public void Execute_health_failure_exits_5_without_docker()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("{\"version\":\"" + Sha40 + "\"}", healthCode: 503);
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40, "--execute");
        run.Exit.ShouldBe(5, run.ToString());
        run.Lines.ShouldBe(Unverified(Sha40, "", "health", world.Lock, execute: true));
        world.LockText.ShouldBe(world.ExpectedLock("health", Sha40, "", "execute"));
        run.Http.ShouldBe([Probed[0]]);
        run.Docker.ShouldBeEmpty();
    }

    [Test]
    public void Execute_unknown_version_exits_5_without_docker()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("{\"version\":\"" + Sha40 + "\"}", versionCode: 500);
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40, "--execute");
        run.Exit.ShouldBe(5, run.ToString());
        run.Lines.ShouldBe(Unverified(Sha40, "", "unavailable", world.Lock, execute: true));
        world.LockText.ShouldBe(world.ExpectedLock("unavailable", Sha40, "", "execute"));
        run.Docker.ShouldBeEmpty();
    }

    [Test]
    public void Execute_malformed_version_exits_5_without_docker()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("not-json");
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40, "--execute");
        run.Exit.ShouldBe(5, run.ToString());
        run.Lines.ShouldBe(Unverified(Sha40, "", "malformed", world.Lock, execute: true));
        world.LockText.ShouldBe(world.ExpectedLock("malformed", Sha40, "", "execute"));
        run.Docker.ShouldBeEmpty();
    }

    [Test]
    public void Execute_moved_image_refuses_before_stopping()
    {
        var world = new LinuxServerRestartWorld { TagId = (0, ["sha256:c1745moved"]) };
        world.Serve("{\"version\":\"" + Other + "\"}");
        var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40, "--execute");
        run.Exit.ShouldBe(5, run.ToString());
        run.Lines.ShouldBe(Unverified(Sha40, Other, "image-moved", world.Lock, execute: true, tag: "sha256:c1745moved"));
        world.LockText.ShouldBe(world.ExpectedLock("image-moved", Sha40, Other, "execute", "sha256:c1745previous"));
        run.Docker.ShouldBe(SameImageInspects);
    }

    [Test]
    public void AppHost_files_do_not_reference_the_linux_gate()
    {
        foreach (var relative in DesktopPaths)
        {
            var text = File.ReadAllText(Path.Combine(Antiphon.Tests.Infrastructure.DockerStackDocuments.RepoRoot, relative));
            text.Contains("restart-linux-server", StringComparison.Ordinal).ShouldBeFalse(relative);
        }

        // Unit 64: the gate is the command's source; the script and its driver are gone.
        File.Exists(Path.Combine(Antiphon.Tests.Infrastructure.DockerStackDocuments.RepoRoot, "scripts", "restart-linux-server.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(Antiphon.Tests.Infrastructure.DockerStackDocuments.RepoRoot, "scripts", "test-restart-linux-server.ps1")).ShouldBeFalse();
        var gate = File.ReadAllText(Path.Combine(Antiphon.Tests.Infrastructure.DockerStackDocuments.RepoRoot, Gate));
        foreach (var banned in new[] { "restart-apphost", "deploy-server2", "schtasks", "phone-home", "PhoneHome" })
            gate.Contains(banned, StringComparison.Ordinal).ShouldBeFalse(banned);
    }

    [Test]
    public void Docker_stack_doc_names_the_inert_gate()
    {
        var text = File.ReadAllText(Path.Combine(Antiphon.Tests.Infrastructure.DockerStackDocuments.RepoRoot, "docs", "docker-stack.md"));
        text.ShouldContain("scripts/antiphon restart-linux-server");
        text.ShouldContain("--execute");
        text.ShouldContain("what-if");
        text.ShouldContain("exit 5");
        text.ShouldContain("not configured");
        text.ShouldContain("already at expected SHA");
        text.ShouldContain("left running");
        text.ShouldContain("docker start");
        text.ShouldNotContain("scripts/restart-linux-server.ps1");
    }
}
