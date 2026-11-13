using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseImpactEnvelopeTests
{
    [Test]
    public async Task Fixture_repositories_live_under_system_temp_outside_the_checkout()
    {
        var fixture = await Fixture.CreateAsync();
        Path.GetRelativePath(Path.GetTempPath(), fixture.Root).ShouldNotStartWith("..");
        Path.GetRelativePath(RepoRoot(), fixture.Root).ShouldStartWith("..");
        (await fixture.GitAsync("rev-parse", "--is-inside-work-tree")).Trim().ShouldBe("true");
    }

    [Test]
    public async Task MissingIndexIsIncomplete()
    {
        await using var fixture = await Fixture.CreateAsync();
        File.Exists(Path.Combine(fixture.Root, "tests/derived-impact-index.json")).ShouldBeFalse();
        var result = await fixture.ReleaseAsync();
        AssertIncomplete(result, fixture.Baseline, fixture.Candidate, await fixture.AlgorithmShaAsync());
        result.GetProperty("incompleteReasons").EnumerateArray().Select(x => x.GetString())
            .ShouldContain("release-graph-unavailable");
        result.GetProperty("inputManifestDigest").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetProperty("universeDigest").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task StaleInputIsIncomplete()
    {
        await using var fixture = await Fixture.CreateAsync(withIndex: true);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "input.txt"), "uncommitted input\n");
        (await fixture.GitAsync("status", "--porcelain")).ShouldContain("input.txt");
        var result = await fixture.ReleaseAsync();
        AssertIncomplete(result, fixture.Baseline, fixture.Candidate, await fixture.AlgorithmShaAsync());
        // A schema-1 advisory index and working bytes must never become a release digest.
        result.GetProperty("candidateGraphDigest").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetProperty("baselineGraphDigest").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task CandidateMismatchIsIncomplete()
    {
        await using var fixture = await Fixture.CreateAsync(withIndex: true);
        var result = await fixture.ReleaseAsync(candidate: fixture.Baseline);
        AssertIncomplete(result, fixture.Baseline, fixture.Baseline, await fixture.AlgorithmShaAsync());
        result.GetProperty("candidateSha").GetString().ShouldNotBe(fixture.Candidate);
        result.GetProperty("candidateGraphDigest").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetProperty("classes").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task OrdinaryModeRemainsAdvisory()
    {
        await using var fixture = await Fixture.CreateAsync(withIndex: true);
        var ordinary = await fixture.SelectAsync("-Path", "input.txt", "-Json");
        ordinary.Exit.ShouldBe(0, ordinary.Text);
        using var json = JsonDocument.Parse(ordinary.Stdout);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(new[]
        {
            "kinds", "classes", "files", "derivedClasses", "keywordIds", "rosterIds", "skippedByCap",
            "owed", "treenodeFilter", "filters", "windowsFilters",
        });
        json.RootElement.GetProperty("classes").EnumerateArray().Select(x => x.GetString()).ShouldBe(new[] { "EnvelopeProbeTests" });
        var result = await fixture.ReleaseAsync();
        var algorithmSha = await fixture.AlgorithmShaAsync();
        AssertIncomplete(result, fixture.Baseline, fixture.Candidate, algorithmSha);
        result.GetProperty("legacySelection").ValueKind.ShouldBe(JsonValueKind.Null);

        // CLI refusal is distinct from a valid but incomplete request. Both deny carry.
        foreach (var conflict in new[]
                 {
                     new[] { "-Range", fixture.Baseline + ".." + fixture.Candidate },
                     new[] { "-Path", "input.txt" }, new[] { "-FileList", "input.txt" },
                     new[] { "-MergeSubset" }, new[] { "-Audit" }, new[] { "-Table", "custom.json" },
                 })
        {
            var refused = await fixture.SelectAsync(fixture.ReleaseArguments().Concat(conflict).ToArray());
            refused.Exit.ShouldBe(2, refused.Text);
            AssertRefused(refused.Stdout);
        }
        foreach (var replacement in new[]
                 {
                     ("-BaselineSha", "HEAD"), ("-CandidateSha", "deadbeef"),
                     ("-CandidateSha", new string('0', 40)), ("-TargetPlatform", "unknown"),
                 })
        {
            var arguments = fixture.ReleaseArguments();
            arguments[Array.IndexOf(arguments, replacement.Item1) + 1] = replacement.Item2;
            var refused = await fixture.SelectAsync(arguments);
            refused.Exit.ShouldBe(2, refused.Text);
            AssertRefused(refused.Stdout);
        }
        var missingEndpoint = await fixture.SelectAsync("-ReleaseEvidence", "-Json", "-TargetPlatform", "linux");
        missingEndpoint.Exit.ShouldBe(2, missingEndpoint.Text);
        AssertRefused(missingEndpoint.Stdout);
        var noJson = await fixture.SelectAsync(fixture.ReleaseArguments().Where(x => x != "-Json").ToArray());
        noJson.Exit.ShouldBe(2, noJson.Text);
        var noOptIn = await fixture.SelectAsync("-CandidateSha", fixture.Candidate, "-Json");
        noOptIn.Exit.ShouldBe(2, noOptIn.Text);
        AssertRefused(noOptIn.Stdout);
        var windows = await fixture.SelectAsync(fixture.ReleaseArguments().Select(x => x == "linux" ? "windows" : x).ToArray());
        windows.Exit.ShouldBe(0, windows.Text);
        using var windowsJson = JsonDocument.Parse(windows.Stdout);
        AssertIncomplete(windowsJson.RootElement, fixture.Baseline, fixture.Candidate, algorithmSha, "windows");
    }

    // CARD-1866 A-1 / M7: exercise a private committed copy of the real algorithm.
    // Text attributes can conceal changed raw bytes from filtered hash-object.
    [Test]
    [Arguments("scripts/impacted-classes.ps1", false)]
    [Arguments("scripts/lib/release-impact.ps1", false)]
    [Arguments("scripts/impacted-classes.ps1", true)]
    [Arguments("scripts/lib/release-impact.ps1", true)]
    public async Task AlgorithmSourceDriftIsRefused(string path, bool normalized)
    {
        await using var fixture = await Fixture.CreateAsync();
        var clean = await fixture.SelectCopiedAlgorithmAsync();
        clean.Exit.ShouldBe(0, clean.Text);
        using (var json = JsonDocument.Parse(clean.Stdout))
            AssertIncomplete(json.RootElement, fixture.Baseline, fixture.Candidate, fixture.Candidate);

        var full = Path.Combine(fixture.Root, path);
        var original = await File.ReadAllTextAsync(full);
        await File.WriteAllTextAsync(full, normalized
            ? original.Replace("\n", "\r\n", StringComparison.Ordinal)
            : original + "\n# changed algorithm bytes\n");
        if (normalized)
        {
            var committed = (await fixture.GitAsync("rev-parse", "HEAD:" + path)).Trim();
            (await fixture.GitAsync("hash-object", "--", path)).Trim().ShouldBe(committed,
                "text normalization must conceal the changed bytes from the old guard");
            (await fixture.GitAsync("hash-object", "--no-filters", "--", path)).Trim().ShouldNotBe(committed);
        }
        else
            (await fixture.GitAsync("status", "--porcelain")).ShouldContain(path);

        var refused = await fixture.SelectCopiedAlgorithmAsync();
        refused.Exit.ShouldBe(2, refused.Text);
        AssertRefused(refused.Stdout);
        using var result = JsonDocument.Parse(refused.Stdout);
        result.RootElement.GetProperty("error").GetString().ShouldContain("algorithm source drift");
        result.RootElement.TryGetProperty("algorithmDigest", out _).ShouldBeFalse(
            "refusal must not publish a usable algorithm identity");
    }

    [Test]
    public async Task AlgorithmDigestBindsCommittedBlobs()
    {
        await using var fixture = await Fixture.CreateAsync();
        async Task<string> DigestAsync()
        {
            var inventory = new StringBuilder();
            foreach (var path in new[] { "scripts/impacted-classes.ps1", "scripts/lib/release-impact.ps1" })
                inventory.Append(path).Append('\0').Append((await fixture.GitAsync("rev-parse", "HEAD:" + path)).Trim()).Append('\n');
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inventory.ToString())));
        }
        async Task<string> AssertIdentityAsync()
        {
            var result = await fixture.SelectCopiedAlgorithmAsync();
            result.Exit.ShouldBe(0, result.Text);
            using var json = JsonDocument.Parse(result.Stdout);
            var value = json.RootElement;
            // The algorithm can be newer than either analyzed endpoint.
            AssertIncomplete(value, fixture.Baseline, fixture.Candidate, (await fixture.GitAsync("rev-parse", "HEAD")).Trim());
            var digest = value.GetProperty("algorithmDigest").GetString()!;
            digest.ShouldBe(await DigestAsync());
            return digest;
        }
        var before = await AssertIdentityAsync();
        await File.AppendAllTextAsync(Path.Combine(fixture.Root, "scripts/lib/release-impact.ps1"), "\n# new committed algorithm\n");
        await fixture.GitAsync("add", ".");
        await fixture.CommitAsync();
        (await AssertIdentityAsync()).ShouldNotBe(before);
    }

    [Test]
    public async Task FixtureCleansUpAfterFailure()
    {
        string? root = null;
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            root = fixture.Root;
            Directory.Exists(root).ShouldBeTrue();
            throw new InvalidOperationException("fixture body failed");
        });
        root.ShouldNotBeNull();
        Directory.Exists(root).ShouldBeFalse("fixture teardown must run even when the body throws");
    }

    private static void AssertRefused(string stdout)
    {
        using var document = JsonDocument.Parse(stdout);
        var value = document.RootElement;
        value.GetProperty("ok").GetBoolean().ShouldBeFalse();
        value.GetProperty("complete").GetBoolean().ShouldBeFalse();
        value.GetProperty("fallback").GetString().ShouldBe("Full");
    }

    private static void AssertIncomplete(JsonElement value, string baseline, string candidate, string algorithmSha, string platform = "linux")
    {
        value.TryGetProperty("complete", out var complete).ShouldBeTrue("advisory success lacks a release completeness bit");
        complete.ValueKind.ShouldBe(JsonValueKind.False);
        value.GetProperty("schema").GetString().ShouldBe("antiphon-release-impact-v1");
        value.GetProperty("fallback").GetString().ShouldBe("Full");
        value.GetProperty("baselineSha").GetString().ShouldBe(baseline);
        value.GetProperty("candidateSha").GetString().ShouldBe(candidate);
        value.GetProperty("repository").GetString().ShouldNotBeNullOrWhiteSpace();
        value.GetProperty("algorithmSha").GetString().ShouldBe(algorithmSha);
        value.GetProperty("algorithmDigest").GetString().ShouldMatch("^[0-9a-f]{64}$");
        value.GetProperty("target").GetProperty("platform").GetString().ShouldBe(platform);
        value.GetProperty("target").GetProperty("verified").GetBoolean().ShouldBeFalse();
        value.GetProperty("incompleteReasons").GetArrayLength().ShouldBeGreaterThan(0);
        value.GetProperty("classes").GetArrayLength().ShouldBe(0);
        value.GetProperty("compatibility").GetProperty("globalIncompatibilityReasons").GetArrayLength().ShouldBeGreaterThan(0);
    }

    private sealed record Result(int Exit, string Stdout, string Text);

    private sealed class Fixture(string root, string baseline, string candidate) : IAsyncDisposable
    {
        public string Root { get; } = root;
        public string Baseline { get; } = baseline;
        public string Candidate { get; } = candidate;

        public static async Task<Fixture> CreateAsync(bool withIndex = false)
        {
            var root = Directory.CreateTempSubdirectory("antiphon-envelope-").FullName;
            var owner = new Fixture(root, "", "");
            try
            {
                foreach (var path in new[] { "scripts/impacted-classes.ps1", "scripts/lib/release-impact.ps1" })
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
                    await File.WriteAllTextAsync(Path.Combine(root, path),
                        (await File.ReadAllTextAsync(Path.Combine(RepoRoot(), path))).Replace("\r\n", "\n", StringComparison.Ordinal));
                }
                await File.WriteAllTextAsync(Path.Combine(root, ".gitattributes"), "*.ps1 text eol=lf\n");
                Directory.CreateDirectory(Path.Combine(root, "tests", "Antiphon.Tests"));
                await File.WriteAllTextAsync(Path.Combine(root, "tests", "Antiphon.Tests", "EnvelopeProbeTests.cs"), "public sealed class EnvelopeProbeTests { }\n");
                await File.WriteAllTextAsync(Path.Combine(root, "tests", "impacted-classes.json"),
                    "{\"schema\":1,\"cache\":\"none\",\"kinds\":[{\"id\":\"probe\",\"globs\":[\"input.txt\"],\"classes\":[\"EnvelopeProbeTests\"]}]}\n");
                if (withIndex)
                    await File.WriteAllTextAsync(Path.Combine(root, "tests", "derived-impact-index.json"), "{\"schema\":1,\"edges\":[],\"cappedPaths\":[]}\n");
                await File.WriteAllTextAsync(Path.Combine(root, "input.txt"), "baseline\n");
                var fixture = new Fixture(root, "", "");
                await fixture.GitAsync("init", "-q");
                await fixture.GitAsync("config", "core.autocrlf", "false");
                await fixture.GitAsync("add", ".");
                await fixture.CommitAsync();
                var baseline = (await fixture.GitAsync("rev-parse", "HEAD")).Trim();
                await File.WriteAllTextAsync(Path.Combine(root, "input.txt"), "candidate\n");
                await fixture.GitAsync("add", ".");
                await fixture.CommitAsync();
                return new Fixture(root, baseline, (await fixture.GitAsync("rev-parse", "HEAD")).Trim());
            }
            catch
            {
                await owner.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            var watch = Stopwatch.StartNew();
            // Only this fixture's exclusively created directory is owned. Delete leaf
            // entries individually; never follow links or recursively delete directories.
            void Remove(DirectoryInfo directory)
            {
                if (directory.LinkTarget is not null) throw new IOException("fixture link refused");
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("fixture teardown timed out");
                    if (entry.LinkTarget is not null) throw new IOException("fixture link refused");
                    if (entry is DirectoryInfo child) Remove(child);
                    else
                    {
                        entry.Attributes &= ~FileAttributes.ReadOnly;
                        entry.Delete();
                    }
                }
                directory.Delete();
            }
            Remove(new DirectoryInfo(Root));
            Console.WriteLine($"C1820 phase=cleanup elapsedMs={watch.ElapsedMilliseconds} fixture={Path.GetFileName(Root)}");
            return ValueTask.CompletedTask;
        }

        public Task<string> CommitAsync() => GitAsync("-c", "user.name=Envelope fixture", "-c", "user.email=envelope@example.invalid", "-c", "commit.gpgsign=false", "commit", "-qm", "fixture");

        public async Task<string> AlgorithmShaAsync() => (await GitAsync("-C", RepoRoot(), "rev-parse", "HEAD")).Trim();

        public async Task<string> GitAsync(params string[] arguments)
        {
            var result = await RunAsync("git", arguments, 60);
            result.Exit.ShouldBe(0, result.Text);
            return result.Stdout;
        }

        public string[] ReleaseArguments(string? candidate = null) =>
            ["-ReleaseEvidence", "-BaselineSha", Baseline, "-CandidateSha", candidate ?? Candidate, "-TargetPlatform", "linux", "-Json"];

        public async Task<JsonElement> ReleaseAsync(string? candidate = null)
        {
            // Red-first adapter: before S1R1 exists, exercise the real successful ordinary selector.
            // Its advisory payload must fail the same envelope assertions, rather than a missing API.
            var arguments = File.Exists(Path.Combine(RepoRoot(), "scripts", "lib", "release-impact.ps1"))
                ? ReleaseArguments(candidate)
                : ["-Range", Baseline + ".." + (candidate ?? Candidate), "-Json"];
            var result = await SelectAsync(arguments);
            result.Exit.ShouldBe(0, result.Text);
            using var document = JsonDocument.Parse(result.Stdout);
            return document.RootElement.Clone();
        }

        public Task<Result> SelectCopiedAlgorithmAsync() => RunAsync("pwsh",
            new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(Root, "scripts", "impacted-classes.ps1"), "-Repo", Root }
                .Concat(ReleaseArguments()).ToArray(), 120);

        public Task<Result> SelectAsync(params string[] arguments) => RunAsync("pwsh",
            new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(RepoRoot(), "scripts", "impacted-classes.ps1"), "-Repo", Root }.Concat(arguments).ToArray(), 120);

        private async Task<Result> RunAsync(string command, string[] arguments, int seconds)
        {
            var start = new ProcessStartInfo(command) { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            // Fixture Git is independent of the invoking worktree's optional Git environment.
            foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" }) start.Environment.Remove(key);
            var watch = Stopwatch.StartNew();
            using var process = Process.Start(start) ?? throw new InvalidOperationException(command + " did not start");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                await process.WaitForExitAsync(deadline.Token);
                await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10));
                return new Result(process.ExitCode, stdout.Result, stdout.Result + stderr.Result);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                }
                Console.WriteLine($"C1820 phase={command} elapsedMs={watch.ElapsedMilliseconds} fixture={Path.GetFileName(Root)}");
            }
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found");
    }
}
