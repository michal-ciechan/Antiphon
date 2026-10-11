using System.Diagnostics;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class OutputCleanupTests : CheckpointTestBase
{
    [Test]
    public void deletes_only_this_runs_bin_names()
    {
        var root = Tree();
        var created = new[] { Path.Combine(root, "bin-c723a"), Path.Combine(root, "src", "bin-c723a") };
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, false, createdPaths: created);
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
        Directory.Exists(Path.Combine(root, "src", "bin-c723a")).ShouldBeFalse();
        File.Exists(Path.Combine(root, "bin-foreign", "keep.txt")).ShouldBeTrue();
        result.Deleted.Count.ShouldBe(2);
    }

    [Test]
    public void never_deletes_bin_obj_workspace_or_foreign_bin_star()
    {
        var root = Tree();
        Directory.CreateDirectory(Path.Combine(root, "bin", "bin-c723a"));
        File.WriteAllText(Path.Combine(root, "bin", "bin-c723a", "sentinel"), "keep");
        File.WriteAllText(Path.Combine(root, "bin", "keep.dll"), "keep");
        OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, false,
            createdPaths: [Path.Combine(root, "bin-c723a"), Path.Combine(root, "src", "bin-c723a")]);
        File.Exists(Path.Combine(root, "bin", "keep.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "bin", "bin-c723a", "sentinel")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "obj", "keep.o")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "workspace", "keep.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "bin-foreign", "keep.txt")).ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
    }

    [Test]
    public void red_keeps_unless_clean_on_red()
    {
        var root = Tree();
        var created = new[] { Path.Combine(root, "bin-c723a"), Path.Combine(root, "src", "bin-c723a") };
        var kept = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 1, false, false, createdPaths: created);
        kept.KeptBecauseRed.ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeTrue();
        OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 1, true, false, createdPaths: created);
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
    }

    [Test]
    public void older_than_removes_run_folders()
    {
        var root = TempDir();
        var old = Path.Combine(root, "20200101-000000-aaaa");
        var young = Path.Combine(root, "20990101-000000-bbbb");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(young);
        CheckpointFixtures.MarkRun(old, alive: false);
        CheckpointFixtures.MarkRun(young, alive: true);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
        var dry = OutputCleanup.RemoveOlderRuns(root, TimeSpan.FromDays(7), dryRun: true);
        dry.Deleted.Count.ShouldBe(1);
        dry.Refusals.ShouldBeEmpty();
        Directory.Exists(old).ShouldBeTrue();
        var removed = OutputCleanup.RemoveOlderRuns(root, TimeSpan.FromDays(7), dryRun: false);
        removed.Deleted.Count.ShouldBe(1);
        removed.Refusals.ShouldBeEmpty();
        Directory.Exists(old).ShouldBeFalse();
        Directory.Exists(young).ShouldBeTrue();
    }

    [Test]
    public void evidence_under_antiphon_survives_and_dry_run_says_would_delete()
    {
        var root = Tree();
        var evidence = Path.Combine(root, ".antiphon", "checkpoints", "run", "builds", "bin-c723a");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "build.log"), "log");
        var created = new[] { Path.Combine(root, "bin-c723a"), Path.Combine(root, "src", "bin-c723a") };
        var dry = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, dryRun: true, createdPaths: created);
        dry.Deleted.ShouldContain(path => path.EndsWith("bin-c723a", StringComparison.Ordinal) && !path.Contains(".antiphon", StringComparison.Ordinal));
        OutputCleanup.DeletedLine(true, dry.Deleted[0]).ShouldStartWith("would delete ");
        OutputCleanup.DeletedLine(false, dry.Deleted[0]).ShouldStartWith("deleted ");
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeTrue();
        var deleted = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, dryRun: false, createdPaths: created);
        deleted.Deleted.ShouldAllBe(path => !path.Contains(".antiphon", StringComparison.Ordinal));
        File.Exists(Path.Combine(evidence, "build.log")).ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
    }

    [Test]
    public void refuses_unsafe_build_ids_and_keeps_tracked_files()
    {
        var root = InitGitRepo();
        var tests = Path.Combine(root, "tests");
        var src = Path.Combine(root, "src");
        var trackedBin = Path.Combine(root, "bin-tracked");
        var safe = Path.Combine(root, "bin-safe");
        Directory.CreateDirectory(tests);
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(trackedBin);
        Directory.CreateDirectory(safe);
        File.WriteAllText(Path.Combine(tests, "kept.cs"), "tracked");
        File.WriteAllText(Path.Combine(src, "kept.cs"), "tracked");
        File.WriteAllText(Path.Combine(trackedBin, "kept.txt"), "tracked");
        File.WriteAllText(Path.Combine(safe, "out.dll"), "untracked");
        Git(root, "add", "--", "tests/kept.cs", "src/kept.cs", "bin-tracked/kept.txt");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "card-1199", "--no-verify");

        var outside = Path.Combine(Path.GetTempPath(), "c1199-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "outside");
        var parent = Directory.GetParent(root)!.FullName;
        try
        {
            var result = OutputCleanup.CleanOwnedOutputs(root,
                ["tests", ".", "..", "src", outside, "bin-tracked", "bin-safe"],
                0, false, false,
                createdPaths: [tests, src, outside, trackedBin, safe]);
            result.Refusals.ShouldContain("tracked");

            File.Exists(Path.Combine(tests, "kept.cs")).ShouldBeTrue();
            File.Exists(Path.Combine(src, "kept.cs")).ShouldBeTrue();
            File.Exists(Path.Combine(trackedBin, "kept.txt")).ShouldBeTrue();
            Directory.Exists(root).ShouldBeTrue();
            Directory.Exists(parent).ShouldBeTrue();
            File.Exists(Path.Combine(outside, "keep.txt")).ShouldBeTrue();
            Directory.Exists(safe).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public void deletes_only_paths_recorded_for_this_run()
    {
        var root = Tree();
        var preexisting = Path.Combine(root, "bin-c723a");
        var nested = Path.Combine(root, "src", "bin-c723a");
        var before = OutputCleanup.MatchingIsolatedDirectories(root, "bin-c723a");
        var fresh = Path.Combine(root, "fresh", "bin-c723a");
        Directory.CreateDirectory(fresh);
        Directory.CreateDirectory(Path.Combine(root, "tests"));
        var state = new RunState();
        OutputCleanup.RecordCreatedOutputs(state, root, "bin-c723a", before);
        OutputCleanup.RecordCreatedOutputs(state, root, "tests", []);
        state.CreatedOutputPaths.ShouldBe([Path.GetFullPath(fresh)]);

        var deleted = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a", "tests"], 0, false, false,
            createdPaths: state.CreatedOutputPaths);
        Directory.Exists(fresh).ShouldBeFalse();
        Directory.Exists(preexisting).ShouldBeTrue();
        Directory.Exists(nested).ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "tests")).ShouldBeTrue();
        deleted.Deleted.Count.ShouldBe(1);

        OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, false, createdPaths: []);
        Directory.Exists(preexisting).ShouldBeTrue();
    }

    [Test]
    public void corrupted_git_metadata_refuses_cleanup_and_keeps_the_file()
    {
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-safe");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "out.dll");
        File.WriteAllText(sentinel, "keep");
        Directory.Delete(Path.Combine(root, ".git", "objects"), recursive: true);
        Directory.Delete(Path.Combine(root, ".git", "refs"), recursive: true);
        File.Delete(Path.Combine(root, ".git", "HEAD"));

        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-safe"], 0, false, false, createdPaths: [output]);

        File.ReadAllText(sentinel).ShouldBe("keep");
        result.Deleted.ShouldBeEmpty();
        result.Refusals.ShouldContain("not-a-repository");
    }

    [Test]
    public void git_missing_from_path_refuses_cleanup_and_keeps_the_file()
    {
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-safe");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "out.dll");
        File.WriteAllText(sentinel, "keep");
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-safe"], 0, false, false, createdPaths: [output],
            gitExecutable: Path.Combine(Path.GetTempPath(), "c1199-no-git", "git"));
        File.ReadAllText(sentinel).ShouldBe("keep");
        result.Deleted.ShouldBeEmpty();
        result.Refusals.ShouldContain("git-unavailable");
    }

    [Test]
    public void nested_repository_tracked_output_is_owner_checked_and_kept()
    {
        var outer = InitGitRepo();
        var nested = Path.Combine(outer, "nested");
        Directory.CreateDirectory(nested);
        Git(nested, "init", "-q", "-b", "inner");
        var output = Path.Combine(nested, "bin-inner");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "kept.txt");
        File.WriteAllText(sentinel, "tracked-inner");
        Git(nested, "add", "--", "bin-inner/kept.txt");
        Git(nested, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "inner", "--no-verify");

        var result = OutputCleanup.CleanOwnedOutputs(outer, ["bin-inner"], 0, false, false, createdPaths: [output]);

        File.ReadAllText(sentinel).ShouldBe("tracked-inner");
        result.Deleted.ShouldBeEmpty();
        result.Refusals.ShouldContain("tracked");
    }

    [Test]
    public void manual_clean_refuses_an_unrecorded_directory()
    {
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-manual");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "keep.txt"), "keep");
        var run = Path.Combine(root, ".antiphon", "checkpoints", "run-manual");
        Directory.CreateDirectory(run);
        var manifest = new CheckpointManifest();
        manifest.Builds.Add(new BuildSpec { Id = "bin-manual", Project = "sample", OutputPath = "bin-manual/" });
        manifest.Checkpoints.Add(new CheckpointSpec
        {
            Id = "CP-1", After = ["all"], Build = "bin-manual", Filter = "/*/*/*/*",
            EstimatedMinutes = 1, MinExecuted = 1,
        });
        File.WriteAllText(Path.Combine(run, "manifest.resolved.yaml"), ManifestLoader.ToYaml(manifest));
        new RunStateStore().Write(Path.Combine(run, "state.json"), new RunState
        {
            RunId = "run-manual",
            Phase = "done",
            CreatedOutputPaths = [],
        });
        var outputText = new StringWriter();
        var exit = Antiphon.Checkpoints.Program.RunAsync(["clean", "--repo-root", root, "--run", "run-manual"],
            new CheckpointApp.Runtime { Output = outputText }).GetAwaiter().GetResult();

        File.Exists(Path.Combine(output, "keep.txt")).ShouldBeTrue();
        exit.ShouldNotBe(0);
        var text = outputText.ToString();
        text.ShouldContain("refused: not-created-by-run");
        text.ShouldNotContain("deleted ");
    }

    [Test]
    public void junction_outside_the_isolated_root_is_refused()
    {
        var root = InitGitRepo();
        var outside = Path.Combine(Path.GetTempPath(), "c1199-junction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "outside");
        var link = Path.Combine(root, "bin-link");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-link"], 0, false, false, createdPaths: [link]);
            File.ReadAllText(sentinel).ShouldBe("outside");
            Directory.Exists(link).ShouldBeTrue();
            result.Deleted.ShouldBeEmpty();
            result.Refusals.ShouldContain("reparse-outside-root");
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public void tracked_build_stamp_is_not_deleted_or_replaced()
    {
        var root = InitGitRepo();
        var binding = new CheckpointBuildBinding
        {
            RepositoryRoot = Path.GetFullPath(root),
            Project = Path.GetFullPath(Path.Combine(root, "sample")),
            OutputPath = Path.GetFullPath(Path.Combine(root, "sample", "bin-tracked")),
        };
        Directory.CreateDirectory(binding.OutputPath);
        File.WriteAllText(binding.PathName, "original-stamp");
        Git(root, "add", "--", "sample/bin-tracked/checkpoint-build-source.json");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "stamp", "--no-verify");

        binding.Invalidate().ShouldBe("tracked");
        File.ReadAllText(binding.PathName).ShouldBe("original-stamp");
        binding.Write().ShouldBe("tracked");
        File.ReadAllText(binding.PathName).ShouldBe("original-stamp");
    }

    [Test]
    public void refusal_report_names_the_reason_and_does_not_claim_deleted()
    {
        var model = new ReportModel
        {
            RunId = "run",
            ExitCode = 2,
            Verdict = "RED",
            OutputNames = ["bin-safe/"],
            CleanedOutputs = false,
            CleanupRefusals = ["tracked"],
        };
        var text = ReportWriter.Markdown(model);
        text.ShouldContain("refused: tracked");
        text.ShouldNotContain("deleted");
        ReportValidator.Validate(model, new string('a', 40)).ShouldBe("cleanup_refused");
    }

    [Test]
    public void nested_repository_inside_an_owned_output_is_refused()
    {
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-c1199");
        var nested = Path.Combine(output, "nested");
        Directory.CreateDirectory(nested);
        var kept = Path.Combine(nested, "kept.txt");
        File.WriteAllText(kept, "tracked-nested");
        Git(nested, "init", "-q", "-b", "inner");
        Git(nested, "add", "--", "kept.txt");
        Git(nested, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "inner", "--no-verify");

        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1199"], 0, false, false, createdPaths: [output]);

        File.ReadAllText(kept).ShouldBe("tracked-nested");
        result.Deleted.ShouldBeEmpty();
        result.Refusals.ShouldContain("nested_repository");
    }

    [Test]
    public void parent_symlink_does_not_delete_a_tracked_stamp()
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("Windows junction variant is unverified");

        var root = InitGitRepo();
        var output = Path.Combine(root, "proj", "bin-stamp");
        Directory.CreateDirectory(output);
        var stamp = Path.Combine(output, "checkpoint-build-source.json");
        File.WriteAllText(stamp, "original-stamp");
        Git(root, "add", "--", "proj/bin-stamp/checkpoint-build-source.json");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "stamp", "--no-verify");

        var linkRoot = Path.Combine(Path.GetTempPath(), "c1199-parent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(linkRoot);
        var link = Path.Combine(linkRoot, "parent-link");
        Directory.CreateSymbolicLink(link, output);
        var via = Path.Combine(link, "checkpoint-build-source.json");
        var probe = Path.Combine(Path.GetTempPath(), "c1199-stamp-" + Guid.NewGuid().ToString("N") + ".ps1");
        try
        {
            File.WriteAllText(probe, StampProbe);
            var scriptPath = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1");
            var start = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(probe);
            start.Environment["CARD1199_SCRIPT"] = scriptPath;
            start.Environment["CARD1199_STAMP"] = via;
            using var process = Process.Start(start);
            process.ShouldNotBeNull();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30_000).ShouldBeTrue(stdout + stderr);
            process.ExitCode.ShouldBe(0, stdout + stderr);
            File.ReadAllText(stamp).ShouldBe("original-stamp");
            stdout.ShouldContain("REFUSED:reparse-point");

            var script = File.ReadAllText(scriptPath);
            script.ShouldNotContain("if (Test-Path -LiteralPath $stampPath) { $stampRefusal = Test-CheckpointStampRefusal $stampPath }");
            var check = script.IndexOf("$replaceRefusal = Test-CheckpointStampRefusal $stampPath", StringComparison.Ordinal);
            var makeDir = script.IndexOf("New-Item -ItemType Directory -Path $stampDirectory", StringComparison.Ordinal);
            check.ShouldBeGreaterThanOrEqualTo(0);
            makeDir.ShouldBeGreaterThan(check);
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(linkRoot))
                Directory.Delete(linkRoot, recursive: true);
            if (File.Exists(probe))
                File.Delete(probe);
        }
    }

    [Test]
    public async Task stop_refuses_to_overwrite_tracked_state_json()
    {
        var root = InitGitRepo();
        var run = Path.Combine(root, "run-stop");
        Directory.CreateDirectory(run);
        var statePath = Path.Combine(run, "state.json");
        new RunStateStore().Write(statePath, new RunState { RunId = "run-stop", Phase = "running" });
        var original = File.ReadAllBytes(statePath);
        Git(root, "add", "--", "run-stop/state.json");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "state", "--no-verify");
        CheckpointFixtures.MarkRun(run, alive: false);

        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
            ["stop", "--run", run, "--repo-root", root],
            new CheckpointApp.Runtime { Output = output });

        exit.ShouldBe(ExitCodes.Invalid);
        File.ReadAllBytes(statePath).ShouldBe(original);
        var text = output.ToString();
        text.ShouldContain("refused: tracked");
        text.ShouldNotContain("stopped ");
    }

    [Test]
    public async Task cleanup_refusal_exits_2_without_claiming_deleted()
    {
        var root = InitGitRepo();
        var results = Path.Combine(root, "results");
        var run = Path.Combine(results, "20200101-000000-aaaa");
        var nested = Path.Combine(run, "nested");
        Directory.CreateDirectory(nested);
        var kept = Path.Combine(nested, "kept.txt");
        File.WriteAllText(kept, "tracked-nested");
        Git(nested, "init", "-q", "-b", "inner");
        Git(nested, "add", "--", "kept.txt");
        Git(nested, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "inner", "--no-verify");
        CheckpointFixtures.MarkRun(run, alive: false);
        Directory.SetLastWriteTimeUtc(run, DateTime.UtcNow.AddDays(-8));

        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
            ["clean", "--repo-root", root, "--results-root", results, "--older-than", "7d"],
            new CheckpointApp.Runtime { Output = output });

        File.ReadAllText(kept).ShouldBe("tracked-nested");
        exit.ShouldBe(ExitCodes.Invalid);
        var text = output.ToString();
        text.ShouldContain("refused: nested_repository");
        text.ShouldNotContain("deleted ");
    }

    [Test]
    [Timeout(45_000)]
    public void hanging_git_is_refused_and_killed_within_the_bound(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("hanging-git proof runs on Linux");
        _ = cancellationToken.CanBeCanceled;

        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-hang");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "out.dll");
        File.WriteAllText(sentinel, "keep");
        var fake = Path.Combine(Path.GetTempPath(), "c1199-hang-" + Guid.NewGuid().ToString("N"));
        var pidfile = fake + ".pid";
        File.WriteAllText(fake, "#!/bin/sh\necho $$ > '" + pidfile + "'\nexec sleep 90\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var started = Stopwatch.StartNew();
        try
        {
            var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-hang"], 0, false, false,
                createdPaths: [output], gitExecutable: fake);
            started.Elapsed.TotalSeconds.ShouldBeLessThan(25);
            File.ReadAllText(sentinel).ShouldBe("keep");
            result.Deleted.ShouldBeEmpty();
            result.Refusals.ShouldContain("git-timeout");
            var pidText = File.ReadAllText(pidfile).Trim();
            int.TryParse(pidText, out var pid).ShouldBeTrue(pidText);
            Directory.Exists("/proc/" + pid).ShouldBeFalse();
        }
        finally
        {
            if (File.Exists(pidfile) && int.TryParse(File.ReadAllText(pidfile).Trim(), out var pid))
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2_000);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                }

                File.Delete(pidfile);
            }

            if (File.Exists(fake))
                File.Delete(fake);
        }
    }

    [Test]
    public async Task run_refuses_tracked_latest_before_writing()
    {
        var root = InitGitRepo();
        var results = Path.Combine(root, "results");
        Directory.CreateDirectory(results);
        var latest = Path.Combine(results, "latest");
        File.WriteAllText(latest, "original-latest");
        Git(root, "add", "--", "results/latest");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "latest", "--no-verify");
        var original = File.ReadAllBytes(latest);
        var launched = 0;
        var output = new StringWriter();
        var manifest = new CheckpointManifest
        {
            ResultsRoot = "results",
            Builds = [new BuildSpec { Id = "bin-audit", Project = "proj/App.csproj", OutputPath = "bin-audit/" }],
            Checkpoints = [new CheckpointSpec { Id = "CP-1", Build = "bin-audit", Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 1 }],
        };

        var started = await CheckpointApp.StartAsync(manifest, new RunRequest(), root, output,
            new CheckpointApp.Runtime { Launch = _ => { launched++; return 1; } });

        started.ExitCode.ShouldBe(ExitCodes.Invalid);
        launched.ShouldBe(0);
        File.ReadAllBytes(latest).ShouldBe(original);
        output.ToString().ShouldContain("refused: tracked");
        Directory.GetDirectories(results).ShouldBeEmpty();
    }

    [Test]
    public async Task execute_refuses_tracked_paths_before_rejecting_input()
    {
        var root = InitGitRepo();
        var run = Path.Combine(root, "run-exec");
        Directory.CreateDirectory(run);
        var ownership = Path.Combine(run, "executor-ownership.json");
        var report = Path.Combine(run, "report.md");
        var request = Path.Combine(run, "request.json");
        File.WriteAllText(ownership, "owned-bytes");
        File.WriteAllText(report, "report-bytes");
        File.WriteAllText(request, "{not-json");
        Git(root, "add", "--", "run-exec/executor-ownership.json", "run-exec/report.md", "run-exec/request.json");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "tracked-run", "--no-verify");
        var owned = File.ReadAllBytes(ownership);
        var reported = File.ReadAllBytes(report);
        var requested = File.ReadAllBytes(request);
        var output = new StringWriter();

        var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None, new CheckpointApp.Runtime { Output = output });

        exit.ShouldBe(ExitCodes.Invalid);
        exit.ShouldNotBe(ExitCodes.ExecutorCrashed);
        File.ReadAllBytes(ownership).ShouldBe(owned);
        File.ReadAllBytes(report).ShouldBe(reported);
        File.ReadAllBytes(request).ShouldBe(requested);
        output.ToString().ShouldContain("refused: tracked");
    }

    [Test]
    public async Task build_refuses_tracked_output_before_the_driver()
    {
        var root = InitGitRepo();
        var outputDir = Path.Combine(root, "proj", "bin-audit");
        Directory.CreateDirectory(outputDir);
        var dll = Path.Combine(outputDir, "App.dll");
        File.WriteAllText(dll, "dll-bytes");
        Git(root, "add", "--", "proj/bin-audit/App.dll");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "dll", "--no-verify");
        var original = File.ReadAllBytes(dll);
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        var results = Path.Combine(root, "row-results");
        Directory.CreateDirectory(results);
        var output = new StringWriter();
        var result = await new RowRunner(driver, new RuntimePlatform()).RunAsync(new RowRequest
        {
            Name = "CP-1",
            Project = "proj/App.csproj",
            OutputPath = "bin-audit/",
            Filter = "/*/*/ExampleSurfaceTests/*",
            ResultsDirectory = results,
            WorkingDirectory = root,
            Source = new SourceEvidence { Start = CheckpointFixtures.CleanSource() },
        }, output, CancellationToken.None);

        result.ExitCode.ShouldBe(ExitCodes.Invalid);
        driver.Calls.Count.ShouldBe(0);
        File.ReadAllBytes(dll).ShouldBe(original);
        output.ToString().ShouldContain("refused: tracked");
    }

    [Test]
    public async Task stop_refuses_foreign_state_without_writing()
    {
        var root = InitGitRepo();
        var run = Path.Combine(root, "run-foreign");
        Directory.CreateDirectory(run);
        var statePath = Path.Combine(run, "state.json");
        File.WriteAllText(statePath, """
            {
              "runId": "other-run",
              "toolOwner": "not-this-tool",
              "phase": "running"
            }
            """);
        var original = File.ReadAllBytes(statePath);
        CheckpointFixtures.MarkRun(run, alive: false);
        var output = new StringWriter();

        var exit = await Program.RunAsync(["stop", "--run", run, "--repo-root", root],
            new CheckpointApp.Runtime { Output = output });

        exit.ShouldBe(ExitCodes.Invalid);
        File.ReadAllBytes(statePath).ShouldBe(original);
        output.ToString().ShouldContain("refused: foreign_state");
        output.ToString().ShouldNotContain("stopped ");
    }

    [Test]
    public void cleanup_report_follows_the_directory_post_condition()
    {
        var dir = Path.Combine(TempDir(), "bin-audit");
        Directory.CreateDirectory(dir);
        var kept = SafeWrite.FormatOutputs(["bin-audit/"], [dir]);
        Directory.Exists(dir).ShouldBeTrue();
        kept.ShouldBe("outputs: refused/kept bin-audit/");
        kept.ShouldNotContain("deleted");
        Directory.Delete(dir);
        Directory.Exists(dir).ShouldBeFalse();
        SafeWrite.FormatOutputs(["bin-audit/"], [dir]).ShouldBe("outputs: deleted bin-audit/");
    }

    [Test]
    public void safe_write_refuses_tracked_linked_unrecorded_and_git_failure()
    {
        var trackedRoot = InitGitRepo();
        var tracked = Path.Combine(trackedRoot, "keep.txt");
        File.WriteAllText(tracked, "original");
        Git(trackedRoot, "add", "--", "keep.txt");
        Git(trackedRoot, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "keep", "--no-verify");
        SafeWrite.Text(tracked, "changed", SafeWrite.Under(trackedRoot)).ShouldBe("tracked");
        File.ReadAllText(tracked).ShouldBe("original");

        var plain = TempDir();
        var existing = Path.Combine(plain, "keep.txt");
        File.WriteAllText(existing, "original");
        SafeWrite.Text(existing, "changed", SafeWrite.Under(plain)).ShouldBe("unrecorded-existing");
        File.ReadAllText(existing).ShouldBe("original");

        var failing = InitGitRepo();
        var victim = Path.Combine(failing, "keep.txt");
        File.WriteAllText(victim, "original");
        var fake = Path.Combine(Path.GetTempPath(), "c1199-git-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(fake, "#!/bin/sh\necho fail >&2\nexit 1\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            SafeWrite.Text(victim, "changed", SafeWrite.Under(failing, gitExecutable: fake)).ShouldBe("git-failed");
            File.ReadAllText(victim).ShouldBe("original");
        }
        finally
        {
            if (File.Exists(fake))
                File.Delete(fake);
        }

        if (!OperatingSystem.IsLinux())
            return;
        var linkedRoot = TempDir();
        var real = Path.Combine(linkedRoot, "real");
        Directory.CreateDirectory(real);
        var kept = Path.Combine(real, "keep.txt");
        File.WriteAllText(kept, "original");
        var link = Path.Combine(linkedRoot, "link");
        Directory.CreateSymbolicLink(link, real);
        SafeWrite.Text(Path.Combine(link, "keep.txt"), "changed", SafeWrite.Under(linkedRoot)).ShouldBe("link-component");
        File.ReadAllText(kept).ShouldBe("original");
    }

    [Test]
    public async Task preflight_git_failure_exits_2_without_writing()
    {
        var root = InitGitRepo();
        var results = Path.Combine(root, "results");
        var outputDir = Path.Combine(root, "proj", "bin-audit");
        Directory.CreateDirectory(results);
        Directory.CreateDirectory(outputDir);
        var latest = Path.Combine(results, "latest");
        var dll = Path.Combine(outputDir, "App.dll");
        File.WriteAllText(latest, "original-latest");
        File.WriteAllText(dll, "dll-bytes");
        var before = new[] { latest, dll }.ToDictionary(path => path, path => (Bytes: File.ReadAllBytes(path), Mtime: File.GetLastWriteTimeUtc(path)));
        var fake = Path.Combine(Path.GetTempPath(), "c1199-pre-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(fake, "#!/bin/sh\necho fail >&2\nexit 1\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var launched = 0;
        var output = new StringWriter();
        var manifest = new CheckpointManifest
        {
            ResultsRoot = "results",
            Builds = [new BuildSpec { Id = "bin-audit", Project = "proj/App.csproj", OutputPath = "bin-audit/" }],
            Checkpoints = [new CheckpointSpec { Id = "CP-1", Build = "bin-audit", Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 1 }],
        };
        try
        {
            var started = await CheckpointApp.StartAsync(manifest, new RunRequest(), root, output,
                new CheckpointApp.Runtime { GitExecutable = fake, Launch = _ => { launched++; return 1; } });
            started.ExitCode.ShouldBe(ExitCodes.Invalid);
            launched.ShouldBe(0);
            output.ToString().ShouldContain("refused: git-failed");
            Directory.GetDirectories(results).ShouldBeEmpty();
            foreach (var (path, snapshot) in before)
            {
                File.ReadAllBytes(path).ShouldBe(snapshot.Bytes);
                File.GetLastWriteTimeUtc(path).ShouldBe(snapshot.Mtime);
            }
        }
        finally
        {
            if (File.Exists(fake))
                File.Delete(fake);
        }
    }

    private const string StampProbe = """
        $ErrorActionPreference = 'Stop'
        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($env:CARD1199_SCRIPT, [ref]$tokens, [ref]$errors)
        if ($errors -and @($errors).Count -gt 0) { throw (($errors | ForEach-Object { $_.ToString() }) -join "`n") }
        $names = @('Test-CheckpointComponentLink','Find-CheckpointGitDir','Invoke-CheckpointGit','Test-CheckpointStampRefusal')
        $chunks = New-Object System.Collections.Generic.List[string]
        foreach ($name in $names) {
            $fn = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true) | Select-Object -First 1
            if ($null -ne $fn) { $chunks.Add($fn.Extent.Text) }
        }
        $invoke = ($chunks -join "`n") + @'

        $reason = Test-CheckpointStampRefusal $env:CARD1199_STAMP
        if ([string]::IsNullOrEmpty($reason)) {
            if (Test-Path -LiteralPath $env:CARD1199_STAMP) { [System.IO.File]::Delete($env:CARD1199_STAMP) }
            Write-Output 'DELETED'
        } else {
            Write-Output ('REFUSED:' + $reason)
        }
        '@
        & ([scriptblock]::Create($invoke))
        """;

    private string InitGitRepo()
    {
        var root = TempDir();
        Git(root, "init", "-b", "card1199");
        return root;
    }

    private static void Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment.Remove("GIT_DIR");
        start.Environment.Remove("GIT_WORK_TREE");
        start.Environment.Remove("GIT_INDEX_FILE");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000).ShouldBeTrue();
        process.ExitCode.ShouldBe(0, stdout + stderr);
    }

    private string Tree()
    {
        var root = TempDir();
        Git(root, "init", "-q", "-b", "card1199");
        Write(root, "bin-c723a", "out.dll");
        Write(root, Path.Combine("src", "bin-c723a"), "out.dll");
        Write(root, "bin-foreign", "keep.txt");
        Write(root, "obj", "keep.o");
        Write(root, "workspace", "keep.txt");
        Write(root, "bin", "keep.dll");
        return root;
    }

    private static void Write(string root, string dir, string file)
    {
        var path = Path.Combine(root, dir);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, file), "x");
    }
}
