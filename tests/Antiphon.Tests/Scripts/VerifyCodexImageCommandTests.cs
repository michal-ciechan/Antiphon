using System.IO.Abstractions.TestingHelpers;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0660 V-9: the image qualification (the verify-card0660-codex-image command, Antiphon.Nightly.CodexImageQualification,
/// which replaced scripts/verify-card0660-codex-image.ps1 in unit 48 of the 2026-11-22 plan) in process. docker and git are a
/// recording launcher that answers as a qualifying image and a clean checkout would (or as a test's fault says; CodexImageWorld),
/// the clock is fake and the files a mock file system. Nothing starts; the real qualification is CP-10/CP-11 on a Docker host.
/// The wrapper boundaries of the retired fixture scripts/fixtures/c913-image-wrapper.ps1 are
/// CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts.
/// </summary>
[Category("Unit")]
public sealed class VerifyCodexImageCommandTests
{
    private static readonly string[] TestingRows =
        ["version", "layout", "install-readonly", "no-baked-auth", "fresh-home", "trust", "preserve", "config-accepted", "grok-version", "jq-version", "net9-offline"];

    [Test]
    public void Usage_requires_every_parameter_and_a_known_target()
    {
        string[] full = ["--target", "runtime", "--image", "t:1", "--source-revision", CodexImageWorld.Sha, "--results-root", "r"];
        var options = CodexImageOptions.Parse(full, "checkout");
        options.Target.ShouldBe("runtime");
        options.Image.ShouldBe("t:1");
        options.SourceRevision.ShouldBe(CodexImageWorld.Sha);
        options.ResultsRoot.ShouldBe("r");
        options.SkipBuild.ShouldBeFalse();
        options.Checkout.ShouldBe("checkout");
        CodexImageOptions.Parse([.. full, "--skip-build"], "").SkipBuild.ShouldBeTrue();
        // ValidateSet matched ignoring case; the canonical target is what the run uses.
        CodexImageOptions.Parse(["--target", "Session-Testing", .. full[2..]], "").Target.ShouldBe("session-testing");

        for (var i = 0; i < full.Length; i += 2)
        {
            var without = full.Where((_, at) => at != i && at != i + 1).ToArray();
            Should.Throw<NightlyUsageException>(() => CodexImageOptions.Parse(without, "")).Message.ShouldBe("missing " + full[i]);
        }
        Should.Throw<NightlyUsageException>(() => CodexImageOptions.Parse(["--target", "prod", .. full[2..]], "")).Message
            .ShouldBe("--target must be runtime or session-testing, not 'prod'");
        Should.Throw<NightlyUsageException>(() => CodexImageOptions.Parse([.. full, "-SkipBuild"], "")).Message.ShouldBe("unexpected argument '-SkipBuild'");
        CodexImageOptions.Usage.ShouldBe(
            "verify-card0660-codex-image --target runtime|session-testing --image <tag> --source-revision <full sha> --results-root <fresh dir> [--skip-build]");
    }

    [Test]
    public void A_qualifying_session_testing_image_grades_eleven_rows()
    {
        var world = new CodexImageWorld();
        var run = world.Run();
        run.Exit.ShouldBe(0, run.Output);
        run.Lines.ShouldBe([
            "C660 IMAGE: target=session-testing image=c913-test:none imageId=" + CodexImageWorld.ImageId + " source=" + CodexImageWorld.Sha
                + " rows=11/11 " + string.Join(" ", TestingRows.Select(row => row + "=ok")),
            "C660 IMAGE EXIT CODE: 0"]);
        CodexImageQualification.Rows("session-testing").ShouldBe(TestingRows);
        CodexImageQualification.Dockerfile.ShouldBe("docker/session-runner-grok/Dockerfile");
        world.Evidence("result.txt").ShouldBe(string.Join("\n", run.Lines) + "\n");
        world.Evidence("docker-version.txt").ShouldBe("docker version --format {{.Server.Version}}\n--- exit 0 ---\n27.5.1\n\n");
        world.Evidence("row-net9-offline.txt").ShouldStartWith("docker run --rm --network none ");
        world.Evidence("row-net9-offline.txt").ShouldEndWith("\n--- exit 0 ---\nC660_ROW net9-offline ok native\n\n");

        var probes = TestingRows.Where(row => row != "preserve").Concat(["preserve-arm", "preserve-check"]).Select(row => "row-" + row + ".txt");
        world.EvidenceNames().ShouldBe(
            new[] { "docker-version.txt", "build.log", "image-id.txt", "image-revision.txt", "cache-init.txt", "init-1.txt", "init-2.txt", "result.txt" }
                .Concat(probes).Order(StringComparer.Ordinal).ToArray());
    }

    [Test]
    public void A_runtime_image_grades_ten_rows_without_the_cache_volumes()
    {
        var world = new CodexImageWorld();
        var run = world.Run("runtime");
        run.Exit.ShouldBe(0, run.Output);
        run.Lines[0].ShouldBe("C660 IMAGE: target=runtime image=c913-test:none imageId=" + CodexImageWorld.ImageId + " source=" + CodexImageWorld.Sha
            + " rows=10/10 " + string.Join(" ", TestingRows.Where(row => row != "net9-offline").Select(row => row + "=ok")));
        world.Created.ShouldBe([CodexImageWorld.Owner + "-state", CodexImageWorld.Owner + "-work", CodexImageWorld.Owner + "-codex"]);
        world.DockerCalls.ShouldNotContain(call => string.Join(" ", call).Contains("chown", StringComparison.Ordinal), "no cache initialization for runtime");
        world.DockerCalls.ShouldNotContain(call => call.Contains("net9-offline"));
        world.DockerCalls.Single(call => call[0] == "build").ShouldBe([
            "build", "--file", Path.Combine([CodexImageWorld.Checkout, .. CodexImageQualification.Dockerfile.Split('/')]), "--target", "runtime",
            "--build-arg", "SOURCE_REVISION=" + CodexImageWorld.Sha, "--tag", "c913-test:none", CodexImageWorld.Checkout]);
    }

    // The container recipe: the order of the docker calls, the state-init run as state-init mounts it, and each probe in its own
    // throwaway container with no network, no published port, no Docker socket and only this run's volumes.
    [Test]
    public void Containers_follow_the_isolation_recipe()
    {
        var world = new CodexImageWorld();
        world.Run().Exit.ShouldBe(0);
        var owner = CodexImageWorld.Owner;
        var tag = "c913-test:none";
        var image = CodexImageWorld.ImageId;
        var calls = world.DockerCalls;
        calls.Select(Verb).ShouldBe([
            "version", "image inspect {{.Id}} " + tag, "build", "image inspect {{.Id}} " + tag,
            "image inspect " + CodexImageQualification.RevisionFormat + " " + image,
            "volume create " + owner + "-state", "volume create " + owner + "-work", "volume create " + owner + "-codex",
            "volume create " + owner + "-packages", "volume create " + owner + "-scratch",
            "run cache-init", "run init-state",
            "run version", "run grok-version", "run jq-version", "run layout", "run install-readonly", "run no-baked-auth", "run fresh-home", "run trust",
            "run config-accepted", "run net9-offline", "run preserve-arm", "run init-state", "run preserve-check",
            "volume rm -f " + owner + "-state", "volume rm -f " + owner + "-work", "volume rm -f " + owner + "-codex",
            "volume rm -f " + owner + "-packages", "volume rm -f " + owner + "-scratch"]);
        foreach (var create in calls.Where(call => call[0] == "volume" && call[1] == "create"))
            create[..4].ShouldBe(["volume", "create", "--label", "antiphon.c660.owner=" + owner]);

        var init = calls.First(call => call[^1] == "/stack/init-state.sh");
        init.ShouldBe([
            "run", "--rm", "--network", "none", "--user", "0:0", "--entrypoint", "/bin/sh", "--env", "CODEX_HOME_REQUIRED=1",
            "--mount", "type=volume,source=" + owner + "-work,target=/work",
            "--mount", "type=volume,source=" + owner + "-state,target=/runner-state",
            "--mount", "type=volume,source=" + owner + "-codex,target=/codex-home",
            "--mount", "type=bind,source=" + Path.Combine(CodexImageWorld.Checkout, "docker", "stack", "init-state.sh") + ",target=/stack/init-state.sh,readonly",
            image, "/stack/init-state.sh"]);

        var probeBind = "type=bind,source=" + Path.Combine(CodexImageWorld.Checkout, "docker", "session-runner-grok", "verify-codex-image.sh")
            + ",target=/c660/verify-codex-image.sh,readonly";
        string[] isolation = ["run", "--rm", "--network", "none", "--env", "PhoneHome__Enabled=false", "--entrypoint", "/bin/bash"];
        world.ProbeCall("version").ShouldBe([
            .. isolation, "--user", "1654:1654", "--tmpfs", "/c660-home:exec,uid=1654,gid=1654,mode=0700",
            "--mount", "type=volume,source=" + owner + "-work,target=/work",
            "--mount", "type=volume,source=" + owner + "-state,target=/state",
            "--mount", "type=volume,source=" + owner + "-codex,target=/state/codex",
            "--mount", probeBind, image, "/c660/verify-codex-image.sh", "version"]);
        world.ProbeCall("net9-offline").ShouldBe([
            .. isolation, "--user", "1654:1654", "--tmpfs", "/c660-home:exec,uid=1654,gid=1654,mode=0700",
            "--mount", "type=volume,source=" + owner + "-packages,target=/home/app/.nuget/packages,volume-nocopy",
            "--mount", "type=volume,source=" + owner + "-scratch,target=/var/cache/antiphon/nuget-scratch,volume-nocopy",
            "--mount", probeBind, image, "/c660/verify-codex-image.sh", "net9-offline"]);
        world.ProbeCall("no-baked-auth")[Array.IndexOf(world.ProbeCall("no-baked-auth"), "--user") + 1].ShouldBe("0:0");
        world.ProbeCall("preserve-arm")[^2..].ShouldBe(["preserve-arm", CodexImageWorld.Token]);
        foreach (var probe in CodexImageQualification.Probes("session-testing"))
            world.ProbeCall(probe.Row)[Array.IndexOf(world.ProbeCall(probe.Row), "--user") + 1].ShouldBe(probe.User, probe.Row);

        world.DockerCalls.Single(call => string.Join(" ", call).Contains("chown 1654:1654", StringComparison.Ordinal)).ShouldBe([
            "run", "--rm", "--network", "none", "--user", "0:0", "--entrypoint", "/bin/sh",
            "--mount", "type=volume,source=" + owner + "-packages,target=/home/app/.nuget/packages,volume-nocopy",
            "--mount", "type=volume,source=" + owner + "-scratch,target=/var/cache/antiphon/nuget-scratch,volume-nocopy",
            image, "-c", CodexImageQualification.CacheInitScript]);

        foreach (var run in calls.Where(call => call[0] == "run"))
        {
            run[..4].ShouldBe(["run", "--rm", "--network", "none"]);
            var text = string.Join(" ", run);
            foreach (var forbidden in new[] { "-p", "--publish", "-P", "--privileged", "-v", "--volume", "--network=host" })
                run.ShouldNotContain(forbidden);
            text.ShouldNotContain("docker.sock");
            text.ShouldNotContain("/home/mc");
        }
        calls.ShouldNotContain(call => call.Contains("prune") || call.Contains("push") || call.Contains("pull"));
    }

    // Row 7: the sentinel is armed with the nonce, init-state runs again, then the check; a failed arm or second init grades fail.
    [Test]
    public void Preserve_needs_the_arm_the_second_init_and_the_check()
    {
        var unarmed = new CodexImageWorld();
        unarmed.RowAnswers["preserve-arm"] = (1, "C660_ROW preserve-arm fail sentinel\n");
        var run = unarmed.Run();
        run.Exit.ShouldBe(1);
        run.Lines[0].ShouldBe("C660 IMAGE NOTE: 1 row(s) not ok; see row-*.txt.");
        run.Lines[1].ShouldContain(" rows=10/11 ");
        run.Lines[1].ShouldContain(" preserve=fail ");
        unarmed.DockerCalls.Count(call => call[^1] == "/stack/init-state.sh").ShouldBe(1, "no second init without an armed sentinel");
        unarmed.DockerCalls.ShouldNotContain(call => call.Contains("preserve-check"));

        var reinit = new CodexImageWorld { Init2Exit = 1 };
        reinit.Run().Lines[1].ShouldContain(" preserve=fail ");
        reinit.DockerCalls.ShouldNotContain(call => call.Contains("preserve-check"));

        var check = new CodexImageWorld();
        check.RowAnswers["preserve-check"] = (1, "C660_ROW preserve-check fail config changed\n");
        check.Run().Lines[1].ShouldContain(" preserve=fail ");
    }

    [Test]
    [Arguments(0, "C660_ROW version ok codex-cli 0.170.0\n", "ok")]
    [Arguments(0, "noise\nC660_ROW version ok detail\nmore\n", "ok")]
    [Arguments(0, "C660_ROW version ok detail\r\n", "ok")]
    [Arguments(1, "C660_ROW version ok detail\n", "fail")]
    [Arguments(0, "C660_ROW version fail detail\n", "fail")]
    [Arguments(0, "C660_ROW version ok\n", "fail")]
    [Arguments(0, "C660_ROW version ok \n", "fail")]
    [Arguments(0, "C660_ROW Version ok detail\n", "fail")]
    [Arguments(0, "c660_row version ok detail\n", "fail")]
    [Arguments(0, " C660_ROW version ok detail\n", "fail")]
    [Arguments(0, "C660_ROW versions ok detail\n", "fail")]
    [Arguments(0, "C660_ROW version ok a\nC660_ROW version ok b\n", "fail")]
    [Arguments(0, "C660_ROW version ok a\nC660_ROW layout ok b\n", "fail")]
    [Arguments(0, "", "fail")]
    public void Row_grade_needs_exit_zero_and_exactly_one_matching_ok_line(int exit, string output, string grade)
    {
        var world = new CodexImageWorld();
        world.RowAnswers["version"] = (exit, output);
        var run = world.Run("runtime");
        run.Exit.ShouldBe(grade == "ok" ? 0 : 1, run.Output);
        run.Lines[^2].ShouldContain(" version=" + grade + " ");
        world.Evidence("row-version.txt").ShouldEndWith("\n--- exit " + exit + " ---\n" + output + "\n");
    }

    [Test]
    public void Results_root_must_be_fresh_and_a_used_one_is_never_written()
    {
        var used = new CodexImageWorld();
        used.Files.AddFile(Path.Combine(used.ResultsPath, "old.txt"), new MockFileData("earlier run"));
        var run = used.Run();
        run.Exit.ShouldBe(2);
        run.Lines.ShouldBe([
            "C660 IMAGE NOTE: ResultsRoot must be a fresh directory.",
            "C660 IMAGE: target=session-testing image=c913-test:none imageId=none source=" + CodexImageWorld.Sha + " rows=0/11 "
                + string.Join(" ", TestingRows.Select(row => row + "=unknown")),
            "C660 IMAGE EXIT CODE: 2"]);
        used.Launches.ShouldBeEmpty("neither git nor docker runs");
        used.EvidenceNames().ShouldBe(["old.txt"]);
        used.Files.File.ReadAllText(Path.Combine(used.ResultsPath, "old.txt")).ShouldBe("earlier run");

        var file = new CodexImageWorld();
        file.Files.AddFile(file.ResultsPath, new MockFileData("not a directory"));
        file.Run().Lines[0].ShouldBe("C660 IMAGE NOTE: ResultsRoot must be a fresh directory.");
        file.Launches.ShouldBeEmpty();

        var empty = new CodexImageWorld();
        empty.Files.AddDirectory(empty.ResultsPath);
        empty.Run().Exit.ShouldBe(0);

        var relative = new CodexImageWorld();
        relative.Run(resultsRoot: ".antiphon/c660-image/testing").Exit.ShouldBe(0);
        relative.Files.FileExists(Path.Combine(CodexImageWorld.Checkout, ".antiphon/c660-image/testing", "result.txt")).ShouldBeTrue("relative to the checkout");
    }

    [Test]
    public void Source_gates_refuse_before_docker()
    {
        var shortSha = new CodexImageWorld();
        var run = shortSha.Run(sourceRevision: "abc123");
        run.Exit.ShouldBe(2);
        run.Lines[0].ShouldBe("C660 IMAGE NOTE: SourceRevision must be a full 40-hex commit SHA.");
        shortSha.Launches.ShouldBeEmpty();
        shortSha.Evidence("result.txt").ShouldStartWith("C660 IMAGE NOTE: SourceRevision must be a full 40-hex commit SHA.\n");

        var other = new string('9', 40);
        var moved = new CodexImageWorld { GitHead = other };
        moved.Run().Lines[0].ShouldBe("C660 IMAGE NOTE: SourceRevision " + CodexImageWorld.Sha + " is not checkout HEAD " + other + ".");
        moved.DockerCalls.ShouldBeEmpty();

        var dirty = new CodexImageWorld { GitStatus = " M docker/stack/init-state.sh\n" };
        dirty.Run().Lines[0].ShouldBe("C660 IMAGE NOTE: Tracked changes are uncommitted; the build context would not be SourceRevision.");
        dirty.DockerCalls.ShouldBeEmpty();

        // git's stderr is not its answer (the script's 2>$null): a CRLF warning on a clean checkout is not a change.
        var warned = new CodexImageWorld { GitStderr = "warning: in the working copy of 'a.txt', LF will be replaced by CRLF the next time Git touches it\n" };
        warned.Run().Exit.ShouldBe(0);
        warned.GitCalls.Select(call => string.Join(" ", call.Arguments)).ShouldBe(["rev-parse HEAD", "status --porcelain --untracked-files=no"]);
        warned.GitCalls.ShouldAllBe(call => call.WorkingDirectory == CodexImageWorld.Checkout && call.CaptureOutput);
    }

    [Test]
    public void Docker_and_image_gates_are_setup_failures_with_no_volume()
    {
        (CodexImageWorld World, bool SkipBuild, string Note)[] cases =
        [
            (new CodexImageWorld { DockerVersionExit = 1 }, false, "Docker daemon is not reachable."),
            (new CodexImageWorld { DockerMissing = true }, false, "Docker daemon is not reachable."),
            (new CodexImageWorld { TagExists = true }, false, "Image tag c913-test:none already exists; qualification needs a unique tag."),
            (new CodexImageWorld { BuildExit = 1 }, false, "docker build failed; see build.log."),
            (new CodexImageWorld(), true, "Image tag c913-test:none does not exist for --skip-build."),
            (new CodexImageWorld { Fault = "revision" }, true, "Image revision " + new string('c', 40) + " does not match SourceRevision " + CodexImageWorld.Sha + "."),
            (new CodexImageWorld { Fault = "image-id" }, false, "Image identity is invalid."),
            (new CodexImageWorld { ImageLabel = new string('d', 40) }, false, "Image revision does not match SourceRevision."),
        ];
        var lines = new List<string[]>();
        foreach (var (world, skipBuild, note) in cases)
        {
            var run = world.Run(skipBuild: skipBuild);
            run.Exit.ShouldBe(2, note);
            run.Lines[0].ShouldBe("C660 IMAGE NOTE: " + note);
            run.Lines[^1].ShouldBe("C660 IMAGE EXIT CODE: 2");
            world.DockerCalls.ShouldNotContain(call => call[0] == "volume" || call[0] == "run", note);
            lines.Add(run.Lines);
        }
        cases[0].World.DockerCalls.Count.ShouldBe(1, "nothing after the version probe");
        cases[1].World.Evidence("docker-version.txt").ShouldContain("--- exit -1 ---\ndocker could not start: ");
        cases[2].World.DockerCalls.ShouldNotContain(call => call[0] == "build", "a reused tag is never rebuilt");
        cases[3].World.Evidence("build.log").ShouldContain("\n--- exit 1 ---\n");
        lines[6][1].ShouldContain(" imageId=not-an-image ", customMessage: "the summary names the identity it refused");
    }

    [Test]
    public void Skip_build_checks_the_existing_image_without_building()
    {
        var world = new CodexImageWorld { TagExists = true };
        var run = world.Run(skipBuild: true);
        run.Exit.ShouldBe(0, run.Output);
        world.DockerCalls.ShouldNotContain(call => call[0] == "build");
        world.DockerCalls.Take(5).Select(Verb).ShouldBe([
            "version", "image inspect {{.Id}} c913-test:none", "image inspect " + CodexImageQualification.RevisionFormat + " c913-test:none",
            "image inspect {{.Id}} c913-test:none", "image inspect " + CodexImageQualification.RevisionFormat + " " + CodexImageWorld.ImageId]);
        world.Evidence("image-revision.txt").ShouldStartWith("docker image inspect --format " + CodexImageQualification.RevisionFormat + " " + CodexImageWorld.ImageId + "\n");
    }

    [Test]
    public void Every_exit_removes_only_the_volumes_this_run_created()
    {
        var owner = CodexImageWorld.Owner;
        string[] all = [owner + "-state", owner + "-work", owner + "-codex", owner + "-packages", owner + "-scratch"];

        var partial = new CodexImageWorld { Fault = "partial-create" };
        var run = partial.Run();
        run.Exit.ShouldBe(2);
        run.Lines[0].ShouldBe("C660 IMAGE NOTE: cannot create volume " + owner + "-work");
        partial.Removed.ShouldBe([owner + "-state"]);

        var caches = new CodexImageWorld { CacheInitExit = 1 };
        caches.Run().Lines[0].ShouldBe("C660 IMAGE NOTE: Owned cache initialization failed.");
        caches.Removed.ShouldBe(all);

        var init = new CodexImageWorld { Init1Exit = 1 };
        init.Run().Lines[0].ShouldBe("C660 IMAGE NOTE: first init-state run failed; see init-1.txt.");
        init.Removed.ShouldBe(all);
        init.DockerCalls.ShouldNotContain(call => call.Contains("/c660/verify-codex-image.sh"));

        var failed = new CodexImageWorld { Fault = "child-exit" };
        failed.Run().Exit.ShouldBe(1);
        failed.Removed.ShouldBe(all);
        failed.DockerCalls.Where(call => call[0] == "volume" && call[1] == "rm").ShouldAllBe(call => call.Length == 4 && call[2] == "-f");
        failed.DockerCalls.TakeLast(5).ShouldAllBe(call => call[0] == "volume" && call[1] == "rm", "cleanup is the last thing the run does");
    }

    private static string Verb(string[] call)
    {
        if (call[0] == "volume") return string.Join(" ", call[..2]) + (call[1] == "rm" ? " -f " : " ") + call[^1];
        if (call[0] == "image") return "image inspect " + call[3] + " " + call[^1];
        if (call[0] != "run") return call[0];
        var probe = Array.IndexOf(call, "/c660/verify-codex-image.sh");
        if (probe >= 0) return "run " + call[probe + 1];
        if (call[^1] == "/stack/init-state.sh") return "run init-state";
        return call[^1] == CodexImageQualification.CacheInitScript ? "run cache-init" : "run ?";
    }
}

/// <summary>
/// A Docker host and checkout for the verify-card0660-codex-image command, in process. docker answers as the fake of the retired
/// scripts/fixtures/c913-image-wrapper.ps1 did: version 27.5.1; the tag is absent until a build (unless the revision fault, with
/// --skip-build, or TagExists); the label is the checkout's SHA (forty c's on the tag under the revision fault); the identity is
/// sha256:b{64} ("not-an-image" under the image-id fault); the second volume create fails under partial-create; and every probe
/// prints "C660_ROW &lt;row&gt; ok native", except net9-offline under child-exit (exit 17), wrong-row and duplicate-row. git
/// answers rev-parse HEAD and status (" M owned-fixture" under the dirty fault) and nothing else. The volume owner token and the
/// nonce are Token; the clock stands at 2026-11-24T09:00:00Z.
/// </summary>
internal sealed class CodexImageWorld
{
    public const string Sha = "0123456789abcdef0123456789abcdef01234567";
    public const string Token = "abcdef0123456789abcdef0123456789";
    public const string Owner = "c660q-20261124T090000Z-abcdef";
    public static readonly string ImageId = "sha256:" + new string('b', 64);
    public static readonly string Checkout = OperatingSystem.IsWindows() ? @"C:\c660" : "/c660";
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\c660-results" : "/c660-results";

    private readonly List<NightlyLaunch> _launches = [];
    private int _creates;
    private int _inits;

    /// <summary>The fixture's fault: none, child-exit, wrong-row, duplicate-row, image-id, partial-create, revision or dirty.</summary>
    public string Fault { get; init; } = "none";
    public int DockerVersionExit { get; init; }
    public bool DockerMissing { get; init; }
    public bool TagExists { get; init; }
    public int BuildExit { get; init; }
    public string ImageLabel { get; init; } = Sha;
    public int CacheInitExit { get; init; }
    public int Init1Exit { get; init; }
    public int Init2Exit { get; init; }
    public string GitHead { get; init; } = Sha;
    public string GitStatus { get; init; } = "";
    public string GitStderr { get; init; } = "";

    /// <summary>A probe row's exit and output instead of the fixture's answer.</summary>
    public Dictionary<string, (int Exit, string Output)> RowAnswers { get; } = [];

    public MockFileSystem Files { get; } = new();
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-24T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    public string ResultsPath => Path.Combine(Root, Fault);
    private string _resultsRoot = "";

    public IReadOnlyList<NightlyLaunch> Launches => _launches;
    public IReadOnlyList<string[]> DockerCalls => _launches.Where(launch => launch.FileName == "docker").Select(launch => launch.Arguments.ToArray()).ToArray();
    public IReadOnlyList<NightlyLaunch> GitCalls => _launches.Where(launch => launch.FileName == "git").ToArray();
    public IReadOnlyList<string> Created => DockerCalls.Where(call => call[0] == "volume" && call[1] == "create").Select(call => call[^1]).ToArray();
    public IReadOnlyList<string> Removed => DockerCalls.Where(call => call[0] == "volume" && call[1] == "rm").Select(call => call[^1]).ToArray();

    /// <summary>The probe container of a row (the argument after /c660/verify-codex-image.sh).</summary>
    public string[] ProbeCall(string row) => DockerCalls.Single(call =>
    {
        var at = Array.IndexOf(call, "/c660/verify-codex-image.sh");
        return at >= 0 && at + 1 < call.Length && call[at + 1] == row;
    });

    /// <summary>The values after each --mount, in order.</summary>
    public static string[] Mounts(string[] call) =>
        call.Select((argument, at) => (argument, at)).Where(pair => pair.argument == "--mount" && pair.at + 1 < call.Length).Select(pair => call[pair.at + 1]).ToArray();

    public string Evidence(string name) => Files.File.ReadAllText(Path.Combine(_resultsRoot, name));

    public IReadOnlyList<string> EvidenceNames() =>
        Files.Directory.Exists(_resultsRoot)
            ? Files.Directory.GetFiles(_resultsRoot).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray()
            : [];

    public (int Exit, string Output, string[] Lines) Run(string target = "session-testing", bool skipBuild = false, string? resultsRoot = null,
        string? sourceRevision = null)
    {
        _launches.Clear();
        _creates = 0;
        _inits = 0;
        _resultsRoot = resultsRoot == null ? ResultsPath
            : Path.IsPathRooted(resultsRoot) ? resultsRoot : Path.Combine(Checkout, resultsRoot);
        using var output = new StringWriter();
        var host = new NightlyHost
        {
            Clock = Clock,
            Git = (_, arguments) => throw new NotSupportedException("the qualification runs git on the launcher: " + string.Join(" ", arguments)),
            Launcher = new RecordingLauncher(Answer),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = output,
            Trace = TextWriter.Null,
            Variables = _ => null,
            Sleep = Clock.Advance,
        };
        List<string> arguments = ["--target", target, "--image", "c913-test:" + Fault, "--source-revision", sourceRevision ?? Sha,
            "--results-root", resultsRoot ?? ResultsPath];
        if (skipBuild) arguments.Add("--skip-build");
        var exit = CodexImageQualification.Run(CodexImageOptions.Parse(arguments, Checkout), host, Files, new CodexImageSeams { Token = () => Token });
        var text = output.ToString().ReplaceLineEndings("\n");
        return (exit, text, text.TrimEnd('\n').Split('\n'));
    }

    private static NightlyLaunchResult Result(int exit, string text) => new() { ExitCode = exit, LogText = text, StandardOutputText = text };

    private NightlyLaunchResult Answer(NightlyLaunch launch)
    {
        _launches.Add(launch);
        var a = launch.Arguments.ToArray();
        if (launch.FileName == "git")
        {
            var command = string.Join(" ", a);
            var stdout = command switch
            {
                "rev-parse HEAD" => GitHead + "\n",
                "status --porcelain --untracked-files=no" => Fault == "dirty" ? " M owned-fixture\n" : GitStatus,
                _ => throw new NotSupportedException("Unexpected Git observation: " + command),
            };
            return new NightlyLaunchResult { ExitCode = 0, StandardOutputText = stdout, StandardErrorText = GitStderr, LogText = GitStderr + stdout };
        }
        if (launch.FileName != "docker") throw new NotSupportedException("Unexpected tool: " + launch.CommandText);
        if (DockerMissing) throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified.");
        launch.CaptureOutput.ShouldBeTrue("docker's output is read, not shared with the console");
        if (a[0] == "version") return Result(DockerVersionExit, "27.5.1\n");
        if (a[0] == "image")
        {
            var tag = a[^1].StartsWith("c913-test:", StringComparison.Ordinal);
            if (tag && !TagExists && Fault != "revision" && !DockerCalls.Any(call => call[0] == "build")) return Result(1, "Error: No such image: " + a[^1] + "\n");
            if (string.Join(" ", a).Contains("org.opencontainers.image.revision", StringComparison.Ordinal))
                return Result(0, (Fault == "revision" && tag ? new string('c', 40) : ImageLabel) + "\n");
            return Result(0, (Fault == "image-id" ? "not-an-image" : ImageId) + "\n");
        }
        if (a[0] == "volume" && a[1] == "create")
        {
            _creates++;
            return Fault == "partial-create" && _creates == 2 ? Result(1, "Error response from daemon: create failed\n") : Result(0, a[^1] + "\n");
        }
        if (a[0] == "build") return Result(BuildExit, "#1 building\n");
        if (a[0] != "run") return Result(0, "");
        var at = Array.IndexOf(a, "/c660/verify-codex-image.sh");
        if (at >= 0)
        {
            var row = a[at + 1];
            if (RowAnswers.TryGetValue(row, out var answer)) return Result(answer.Exit, answer.Output);
            if (row == "net9-offline")
            {
                switch (Fault)
                {
                    case "child-exit": return Result(17, "C660_ROW " + row + " ok native\n");
                    case "wrong-row": return Result(0, "C660_ROW wrong ok native\n");
                    case "duplicate-row": return Result(0, "C660_ROW " + row + " ok native\nC660_ROW " + row + " ok native\n");
                }
            }
            return Result(0, "C660_ROW " + row + " ok native\n");
        }
        if (a[^1] == "/stack/init-state.sh") return Result(++_inits == 1 ? Init1Exit : Init2Exit, "state-init owned uid=1654\n");
        if (a[^1] == CodexImageQualification.CacheInitScript) return Result(CacheInitExit, "");
        return Result(0, "");
    }
}
