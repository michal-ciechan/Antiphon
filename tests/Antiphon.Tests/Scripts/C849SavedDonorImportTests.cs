using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 68 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the c849-import-saved-donor command
/// (Antiphon.Nightly.C849SavedDonorImport), which replaced scripts/c849-import-saved-donor.ps1, in process on a mock disk. Every
/// scenario captured from the script before its deletion (C849SavedDonorImportCaptures) must give the same exit, stdout, fake calls
/// (the free-space probe, /usr/bin/stat, the mode reads and the mode writes) and stage tree. A directory walk's order is the file
/// system's (NTFS for the capture, the mock here), so a directory scenario compares its calls as a sorted list; an archive's are in
/// order. The rows that ran the script through tests/Antiphon.Tests/Scripts/Fixtures/c997-import-saved-donor.ps1 in pwsh (the
/// C997 space gate, probe failure and default probe, the C849 unsafe-archive, size-bomb and FIFO rows of RemoteScriptContractTests)
/// are cases here. The capture ran pwsh 7.6.10 (.NET 10); the script ran on pwsh 7.5.4 (.NET 9) in the runner image and the command
/// runs on the image's .NET 9, whose archive reader reads a pax path record holding a newline that .NET 10 refuses, so no scenario
/// pins that record. The clock is not read; nothing is started.
/// </summary>
[Category("Unit")]
public sealed class C849SavedDonorImportTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Test]
    public void Every_captured_import_runs_the_same_in_the_command()
    {
        var scenarios = C849SavedDonorImportCaptures.Scenarios.Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
        var captured = C849SavedDonorImportCaptures.Captured.Select(c => JsonNode.Parse(c)!.AsObject()).ToDictionary(c => c["name"]!.GetValue<string>());
        scenarios.Length.ShouldBe(69);
        scenarios.Length.ShouldBe(captured.Count);
        var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            var name = scenario["name"]!.GetValue<string>();
            var expected = captured[name];
            var actual = Replay(scenario);
            var directory = scenario["source"]!["kind"]!.GetValue<string>() == "dir";
            void Check(string key, string e, string a)
            {
                if (e != a) failures.Add(name + " " + key + "\n  script:  " + e + "\n  command: " + a);
            }
            Check("exit", expected["exit"]!.ToJsonString(), actual.Exit.ToString());
            Check("stdout", expected["stdout"]!.GetValue<string>(), actual.Stdout);
            Check("stderr", expected["stderr"]!.GetValue<string>(), "");
            Check("calls", Calls(expected["calls"]!.AsArray(), directory), Calls(actual.Calls, directory));
            Check("tree", expected["tree"]!.ToJsonString(), new JsonArray(actual.Tree.Select(t => (JsonNode)t).ToArray()).ToJsonString());
        }
        failures.ShouldBeEmpty(string.Join("\n", failures));
    }

    // C997: the space gate keeps 20 GiB of headroom beyond both payloads, read on the stage, before anything is copied.
    [Test]
    [Arguments("tar", 21474836499L)]
    [Arguments("tar", 21474836498L)]
    [Arguments("tar", 21474836497L)]
    [Arguments("directory", 21474836499L)]
    [Arguments("directory", 21474836498L)]
    [Arguments("directory", 21474836497L)]
    public void Space_gate_preserves_headroom_and_payload_allowance(string format, long available)
    {
        var (files, source, stage) = Donor(format);
        var probes = new List<string>();
        var output = new StringWriter();
        var exit = C849SavedDonorImport.Run(new C849SavedDonorImportOptions { Source = source, Stage = stage },
            Seams(files, path => { probes.Add(path); return available; }), output);
        string.Join("|", probes).ShouldBe(stage, "C997-PROBE-STAGE one reading, of the stage");
        // pkgdata (7) + npm-content (11) = 18 bytes: 20 GiB + 18 passes, one byte less refuses.
        if (available < 20 * GiB + 18)
        {
            (exit, output.ToString().Trim()).ShouldBe((2, "CacheDiskLow"));
            files.AllFiles.Where(f => f.StartsWith(stage, StringComparison.Ordinal)).ShouldBeEmpty("C997-IMPORT-NO-WRITES");
        }
        else
        {
            (exit, output.ToString()).ShouldBe((0, ""));
            files.File.ReadAllText(files.Path.Combine(stage, "packages", "item")).ShouldBe("pkgdata");
            files.File.ReadAllText(files.Path.Combine(stage, "npm", "item")).ShouldBe("npm-content");
        }
    }

    // C997: a probe that throws refuses before any copy; only a Cache* word passes through as the diagnosis.
    [Test]
    [Arguments("tar", "C997ProbeFault", "CacheSavedDonorReadFailed")]
    [Arguments("directory", "C997ProbeFault", "CacheSavedDonorReadFailed")]
    [Arguments("tar", "CacheProbeWord", "CacheProbeWord")]
    public void Probe_failure_refuses_before_copy(string format, string fault, string diagnosis)
    {
        var (files, source, stage) = Donor(format);
        var output = new StringWriter();
        var exit = C849SavedDonorImport.Run(new C849SavedDonorImportOptions { Source = source, Stage = stage },
            Seams(files, _ => throw new InvalidOperationException(fault)), output);
        (exit, output.ToString().Trim()).ShouldBe((2, diagnosis));
        files.AllFiles.Where(f => f.StartsWith(stage, StringComparison.Ordinal)).ShouldBeEmpty("C997-PROBE-FAILURE-NO-WRITES");
    }

    // C997-DEFAULT-REAL: production reads the stage's own drive, the real stat and the real modes; c590-remote.sh passes no probe.
    [Test]
    public void Production_seams_read_the_stage_drive_and_the_real_platform()
    {
        var seams = new C849SavedDonorImportSeams { Files = new MockFileSystem() };
        seams.AvailableFreeBytes.ShouldNotBeNull();
        var temp = Path.GetTempPath();
        Math.Abs(seams.AvailableFreeBytes(temp) - new DriveInfo(temp).AvailableFreeSpace).ShouldBeLessThan(512L * 1024 * 1024);
        (C849SavedDonorImport.Headroom, C849SavedDonorImport.Budgets["packages"], C849SavedDonorImport.Budgets["npm"]).ShouldBe((20 * GiB, 10 * GiB, 2 * GiB));
        C849SavedDonorImport.Roots.Select(r => r.Prefix + "=" + r.Role).ShouldBe(new[]
        {
            "home/app/.nuget/packages=packages", "home/app/.npm/_cacache=npm", ".nuget/packages=packages", ".npm/_cacache=npm", "packages=packages", "npm=npm",
        });
        var remote = File.ReadAllText(Path.Combine(Antiphon.Tests.Application.DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh")).Replace("\r\n", "\n");
        var caller = remote[remote.IndexOf("c849_saved_copy() {", StringComparison.Ordinal)..];
        caller = caller[..caller.IndexOf("\n}\n", StringComparison.Ordinal)];
        caller.ShouldContain("exec /opt/antiphon-cli/Antiphon.Scripts.dll c849-import-saved-donor --source /saved --stage /stage", customMessage: "C997-CALLER-DEFAULT");
        caller.ShouldNotContain("--available", customMessage: "C997-CALLER-DEFAULT: no probe override");
    }

    [Test]
    public void Arguments_are_the_two_required_paths()
    {
        var options = C849SavedDonorImportOptions.Parse(new[] { "--source", "/saved", "--stage", "/stage" });
        (options.Source, options.Stage).ShouldBe(("/saved", "/stage"));
        Should.Throw<NightlyUsageException>(() => C849SavedDonorImportOptions.Parse(new[] { "--source", "/saved" })).Message.ShouldBe("missing --stage");
        Should.Throw<NightlyUsageException>(() => C849SavedDonorImportOptions.Parse(new[] { "--stage", "/stage" })).Message.ShouldBe("missing --source");
        Should.Throw<NightlyUsageException>(() => C849SavedDonorImportOptions.Parse(new[] { "-Source", "/saved" }));
    }

    // ---- the replay ----

    private sealed record Result(int Exit, string Stdout, JsonArray Calls, string[] Tree);

    private static string Root => OperatingSystem.IsWindows() ? @"C:\c68" : "/c68";

    private static C849SavedDonorImportSeams Seams(IFileSystem files, Func<string, long> probe) => new()
    {
        Files = files,
        AvailableFreeBytes = probe,
        UnixType = path => (0, new[] { files.Directory.Exists(path) ? "directory" : "regular file" }),
        GetMode = _ => (UnixFileMode)420,
        SetMode = (_, _) => { },
    };

    // pkgdata and npm-content, as a tar archive or a directory (C997DonorInvocation's donor).
    private static (MockFileSystem Files, string Source, string Stage) Donor(string format)
    {
        var files = new MockFileSystem();
        var stage = files.Path.Combine(Root, "stage");
        files.AddDirectory(stage);
        var tree = files.Path.Combine(Root, "tree");
        files.AddFile(files.Path.Combine(tree, "packages", "item"), new MockFileData("pkgdata"));
        files.AddFile(files.Path.Combine(tree, "npm", "item"), new MockFileData("npm-content"));
        if (format == "directory") return (files, tree, stage);
        var tar = files.Path.Combine(Root, "donor.tar");
        using (var stream = new MemoryStream())
        {
            using (var writer = new System.Formats.Tar.TarWriter(stream, System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true))
                foreach (var (name, text) in new[] { ("packages/item", "pkgdata"), ("npm/item", "npm-content") })
                    writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, name)
                        { DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)) });
            files.AddFile(tar, new MockFileData(stream.ToArray()));
        }
        return (files, tar, stage);
    }

    // Each scenario on the real disk in its own temporary directory, as the capture ran it (the archive reader's handling of a
    // truncated or odd member depends on the stream, so a mock stream is not the same reader); the junction scenario on a mock disk,
    // where a reparse point is an attribute.
    private static Result Replay(JsonObject scenario)
    {
        var entries = scenario["source"]!["entries"] as JsonArray ?? new JsonArray();
        var junction = entries.Any(e => e!["junction"] != null);
        var temp = junction ? null : Directory.CreateTempSubdirectory("u68-import-");
        try { return Replay(scenario, junction ? new MockFileSystem() : new FileSystem(), temp?.FullName ?? Root, entries); }
        finally { temp?.Delete(true); }
    }

    private static Result Replay(JsonObject scenario, IFileSystem files, string root, JsonArray entries)
    {
        var stage = files.Path.Combine(root, "stage");
        files.Directory.CreateDirectory(stage);
        void Put(string path, string text)
        {
            files.Directory.CreateDirectory(files.Path.GetDirectoryName(path)!);
            files.File.WriteAllText(path, text);
        }
        foreach (var pre in scenario["stage"] as JsonArray ?? new JsonArray())
            Put(files.Path.Combine(stage, Native(pre!["path"]!.GetValue<string>())), pre["text"]!.GetValue<string>());
        var kind = scenario["source"]!["kind"]!.GetValue<string>();
        string source;
        if (kind == "tar")
        {
            source = files.Path.Combine(root, "source.tar");
            files.File.WriteAllBytes(source, Convert.FromBase64String(scenario["source"]!["base64"]!.GetValue<string>()));
        }
        else
        {
            source = files.Path.Combine(root, "source");
            if (kind == "dir")
            {
                files.Directory.CreateDirectory(source);
                foreach (var entry in entries)
                {
                    var path = files.Path.Combine(source, Native(entry!["path"]!.GetValue<string>()));
                    if (entry["dir"]?.GetValue<bool>() == true) files.Directory.CreateDirectory(path);
                    else if (entry["junction"] != null)
                    {
                        // The capture's junction: a directory entry with the reparse point attribute.
                        files.Directory.CreateDirectory(path);
                        files.File.SetAttributes(path, FileAttributes.Directory | FileAttributes.ReparsePoint);
                    }
                    else Put(path, entry["text"]!.GetValue<string>());
                }
            }
        }
        var calls = new JsonArray();
        string Relative(string baseDir, string path) => files.Path.GetRelativePath(baseDir, path).Replace('\\', '/');
        var probeThrow = scenario["probeThrow"]?.GetValue<string>();
        var available = scenario["available"]!.GetValue<long>();
        var stat = scenario["stat"] as JsonObject;
        var modes = scenario["modes"] as JsonObject;
        var seams = new C849SavedDonorImportSeams
        {
            Files = files,
            AvailableFreeBytes = argument =>
            {
                calls.Add(new JsonObject { ["name"] = "probe", ["argument"] = argument.Replace(stage, "<STAGE>") });
                if (!string.IsNullOrEmpty(probeThrow)) throw new InvalidOperationException(probeThrow);
                return available;
            },
            UnixType = path =>
            {
                var relative = Relative(source, path);
                calls.Add(new JsonObject { ["name"] = "stat", ["args"] = new JsonArray("-c", "%F"), ["path"] = relative });
                if (stat != null && stat.ContainsKey(relative))
                {
                    var rule = stat[relative]!;
                    var line = rule["out"]?.GetValue<string>();
                    return (rule["exit"]!.GetValue<int>(), line == null ? Array.Empty<string>() : new[] { line });
                }
                return (0, new[] { files.Directory.Exists(path) ? "directory" : "regular file" });
            },
            GetMode = path =>
            {
                var relative = Relative(source, path);
                var mode = modes != null && modes.ContainsKey(relative) ? modes[relative]!.GetValue<int>() : 420;
                calls.Add(new JsonObject { ["name"] = "getmode", ["path"] = relative, ["mode"] = mode });
                return (UnixFileMode)mode;
            },
            SetMode = (path, mode) => calls.Add(new JsonObject { ["name"] = "setmode", ["path"] = Relative(stage, path), ["mode"] = (int)mode }),
        };
        var output = new StringWriter();
        var exit = C849SavedDonorImport.Run(new C849SavedDonorImportOptions { Source = source, Stage = stage }, seams, output);
        var tree = new List<string>();
        foreach (var directory in files.Directory.EnumerateDirectories(stage, "*", SearchOption.AllDirectories)) tree.Add("d " + Relative(stage, directory));
        foreach (var file in files.Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)) tree.Add("f " + Relative(stage, file) + " " + files.File.ReadAllText(file));
        tree.Sort(StringComparer.Ordinal);
        return new Result(exit, output.ToString().Replace("\r\n", "\n"), calls, tree.ToArray());
    }

    private static string Native(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    // The calls as compared: in order for an archive, sorted for a directory walk (its order is the file system's).
    private static string Calls(JsonArray calls, bool sorted)
    {
        var rows = calls.Select(c => c!.ToJsonString()).ToList();
        if (sorted) rows.Sort(StringComparer.Ordinal);
        return string.Join("\n", rows);
    }
}
