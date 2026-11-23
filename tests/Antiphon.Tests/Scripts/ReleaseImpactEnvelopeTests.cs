using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1820 S1R1. impacted-classes --release-evidence (Antiphon.Nightly.ReleaseImpactEvidence, in process; until
/// 2026-11-22 scripts/impacted-classes.ps1 -ReleaseEvidence and scripts/lib/release-impact.ps1). The envelope is always
/// incomplete/Full, and its algorithm identity binds the running selector's own bytes to the source checkout's blobs.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseImpactEnvelopeTests
{
    // ImpactedClasses.SelectorFiles, as literals the derived impact index reads.
    private static readonly string[] AlgorithmFiles =
    [
        "scripts/lib/Antiphon.Nightly/ImpactedClasses.cs",
        "scripts/lib/Antiphon.Nightly/ImpactedClassesInputs.cs",
        "scripts/lib/Antiphon.Nightly/ReleaseImpactEvidence.cs",
    ];

    [Test]
    public async Task Fixture_repositories_live_under_system_temp_outside_the_checkout()
    {
        await using var fixture = await Fixture.CreateAsync();
        Path.GetRelativePath(Path.GetTempPath(), fixture.Root).ShouldNotStartWith("..");
        var fromCheckout = Path.GetRelativePath(RepoRoot(), fixture.Root);
        (Path.IsPathRooted(fromCheckout) || fromCheckout.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ShouldBeTrue("the fixture is outside the checkout, including when temp is on another drive");
        (await fixture.GitAsync("rev-parse", "--is-inside-work-tree")).Trim().ShouldBe("true");
    }

    [Test]
    public void Algorithm_files_are_the_selector_identity()
    {
        AlgorithmFiles.ShouldBe(ImpactedClasses.SelectorFiles);
        foreach (var path in AlgorithmFiles)
            ImpactedClasses.RunningSource(path).ShouldBe(File.ReadAllBytes(Path.Combine(RepoRoot(), path)), path + " runs as checked out");
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
        var ordinary = fixture.Select("--path", "input.txt", "--json");
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
                     new[] { "--range", fixture.Baseline + ".." + fixture.Candidate },
                     new[] { "--path", "input.txt" }, new[] { "--file-list", "input.txt" },
                     new[] { "--merge-subset" }, new[] { "--audit" }, new[] { "--table", "custom.json" },
                 })
        {
            var refused = fixture.Select(fixture.ReleaseArguments().Concat(conflict).ToArray());
            refused.Exit.ShouldBe(2, refused.Text);
            AssertRefused(refused.Stdout);
        }
        foreach (var replacement in new[]
                 {
                     ("--baseline-sha", "HEAD"), ("--candidate-sha", "deadbeef"),
                     ("--candidate-sha", new string('0', 40)), ("--target-platform", "unknown"),
                 })
        {
            var arguments = fixture.ReleaseArguments();
            arguments[Array.IndexOf(arguments, replacement.Item1) + 1] = replacement.Item2;
            var refused = fixture.Select(arguments);
            refused.Exit.ShouldBe(2, refused.Text);
            AssertRefused(refused.Stdout);
        }
        var missingEndpoint = fixture.Select("--release-evidence", "--json", "--target-platform", "linux");
        missingEndpoint.Exit.ShouldBe(2, missingEndpoint.Text);
        AssertRefused(missingEndpoint.Stdout);
        var noJson = fixture.Select(fixture.ReleaseArguments().Where(x => x != "--json").ToArray());
        noJson.Exit.ShouldBe(2, noJson.Text);
        var noOptIn = fixture.Select("--candidate-sha", fixture.Candidate, "--json");
        noOptIn.Exit.ShouldBe(2, noOptIn.Text);
        AssertRefused(noOptIn.Stdout);
        var windows = fixture.Select(fixture.ReleaseArguments().Select(x => x == "linux" ? "windows" : x).ToArray());
        windows.Exit.ShouldBe(0, windows.Text);
        using var windowsJson = JsonDocument.Parse(windows.Stdout);
        AssertIncomplete(windowsJson.RootElement, fixture.Baseline, fixture.Candidate, algorithmSha, "windows");
    }

    // CARD-1866 A-1 / M7: a private source checkout whose committed blobs are the running selector's own bytes. A
    // committed algorithm that is not the one running is refused, also when only its line endings differ: the identity
    // binds raw blobs, so a normalised comparison (which would accept the CRLF copy) must not be what decides.
    [Test]
    [Arguments("scripts/lib/Antiphon.Nightly/ImpactedClasses.cs", false)]
    [Arguments("scripts/lib/Antiphon.Nightly/ReleaseImpactEvidence.cs", false)]
    [Arguments("scripts/lib/Antiphon.Nightly/ImpactedClasses.cs", true)]
    [Arguments("scripts/lib/Antiphon.Nightly/ReleaseImpactEvidence.cs", true)]
    public async Task AlgorithmSourceDriftIsRefused(string path, bool normalized)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var source = await Fixture.CreateSourceAsync();
        var clean = fixture.Select(source.Root, fixture.ReleaseArguments());
        clean.Exit.ShouldBe(0, clean.Text);
        using (var json = JsonDocument.Parse(clean.Stdout))
            AssertIncomplete(json.RootElement, fixture.Baseline, fixture.Candidate, (await source.GitAsync("rev-parse", "HEAD")).Trim());

        var running = Encoding.UTF8.GetString(ImpactedClasses.RunningSource(path));
        var changed = normalized ? running.Replace("\n", "\r\n", StringComparison.Ordinal) : running + "\n// changed algorithm bytes\n";
        await File.WriteAllTextAsync(Path.Combine(source.Root, path), changed);
        await source.GitAsync("add", "--", path);
        await source.CommitAsync();
        var committed = (await source.GitAsync("rev-parse", "HEAD:" + path)).Trim();
        committed.ShouldNotBe(ReleaseImpactEvidence.BlobId(ImpactedClasses.RunningSource(path), committed.Length));
        if (normalized)
            (await source.GitAsync("show", "HEAD:" + path)).Replace("\r\n", "\n", StringComparison.Ordinal)
                .ShouldBe(running, "line-ending normalisation must conceal the changed bytes from a text compare");

        var refused = fixture.Select(source.Root, fixture.ReleaseArguments());
        refused.Exit.ShouldBe(2, refused.Text);
        AssertRefused(refused.Stdout);
        using var result = JsonDocument.Parse(refused.Stdout);
        result.RootElement.GetProperty("error").GetString().ShouldContain("algorithm source drift");
        result.RootElement.TryGetProperty("algorithmDigest", out _).ShouldBeFalse(
            "refusal must not publish a usable algorithm identity");
    }

    // The digest is over the committed blobs of exactly the algorithm files at the source commit: another commit moves
    // algorithmSha but not the digest, and a committed change to an algorithm file is refused rather than given a digest
    // (the running selector is not that algorithm).
    [Test]
    public async Task AlgorithmDigestBindsCommittedBlobs()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var source = await Fixture.CreateSourceAsync();
        async Task<string> DigestAsync()
        {
            var inventory = new StringBuilder();
            foreach (var path in AlgorithmFiles)
                inventory.Append(path).Append('\0').Append((await source.GitAsync("rev-parse", "HEAD:" + path)).Trim()).Append('\n');
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inventory.ToString())));
        }
        async Task<string> AssertIdentityAsync()
        {
            var result = fixture.Select(source.Root, fixture.ReleaseArguments());
            result.Exit.ShouldBe(0, result.Text);
            using var json = JsonDocument.Parse(result.Stdout);
            var value = json.RootElement;
            // The algorithm can be newer than either analyzed endpoint.
            AssertIncomplete(value, fixture.Baseline, fixture.Candidate, (await source.GitAsync("rev-parse", "HEAD")).Trim());
            var digest = value.GetProperty("algorithmDigest").GetString()!;
            digest.ShouldBe(await DigestAsync());
            return digest;
        }
        var before = await AssertIdentityAsync();
        var firstSha = (await source.GitAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(source.Root, "notes.txt"), "not an algorithm file\n");
        await source.GitAsync("add", ".");
        await source.CommitAsync();
        (await source.GitAsync("rev-parse", "HEAD")).Trim().ShouldNotBe(firstSha);
        (await AssertIdentityAsync()).ShouldBe(before);

        await File.AppendAllTextAsync(Path.Combine(source.Root, AlgorithmFiles[2]), "\n// new committed algorithm\n");
        await source.GitAsync("add", ".");
        await source.CommitAsync();
        var changed = fixture.Select(source.Root, fixture.ReleaseArguments());
        changed.Exit.ShouldBe(2, changed.Text);
        changed.Stdout.ShouldContain("algorithm source drift");
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
                Directory.CreateDirectory(Path.Combine(root, "tests", "Antiphon.Tests"));
                await File.WriteAllTextAsync(Path.Combine(root, "tests", "Antiphon.Tests", "EnvelopeProbeTests.cs"), "public sealed class EnvelopeProbeTests { }\n");
                await File.WriteAllTextAsync(Path.Combine(root, "tests", "impacted-classes.json"),
                    "{\"schema\":1,\"cache\":\"none\",\"kinds\":[{\"id\":\"probe\",\"globs\":[\"input.txt\"],\"classes\":[\"EnvelopeProbeTests\"]}]}\n");
                if (withIndex)
                    await File.WriteAllTextAsync(Path.Combine(root, "tests", "derived-impact-index.json"), "{\"schema\":1,\"edges\":[],\"cappedPaths\":[]}\n");
                await File.WriteAllTextAsync(Path.Combine(root, "input.txt"), "baseline\n");
                await owner.GitAsync("init", "-q");
                await owner.GitAsync("config", "core.autocrlf", "false");
                await owner.GitAsync("add", ".");
                await owner.CommitAsync();
                var baseline = (await owner.GitAsync("rev-parse", "HEAD")).Trim();
                await File.WriteAllTextAsync(Path.Combine(root, "input.txt"), "candidate\n");
                await owner.GitAsync("add", ".");
                await owner.CommitAsync();
                return new Fixture(root, baseline, (await owner.GitAsync("rev-parse", "HEAD")).Trim());
            }
            catch
            {
                await owner.DisposeAsync();
                throw;
            }
        }

        // A source checkout holding the running selector's own bytes of the algorithm files, committed as they are.
        public static async Task<Fixture> CreateSourceAsync()
        {
            var root = Directory.CreateTempSubdirectory("antiphon-envelope-source-").FullName;
            var owner = new Fixture(root, "", "");
            try
            {
                foreach (var path in AlgorithmFiles)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
                    await File.WriteAllBytesAsync(Path.Combine(root, path), ImpactedClasses.RunningSource(path));
                }
                await File.WriteAllTextAsync(Path.Combine(root, ".gitattributes"), "* -text\n");
                await owner.GitAsync("init", "-q");
                await owner.GitAsync("config", "core.autocrlf", "false");
                await owner.GitAsync("add", ".");
                await owner.CommitAsync();
                return owner;
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

        // The selector's source root here is this checkout, so the algorithm is the checkout's HEAD.
        public async Task<string> AlgorithmShaAsync() => (await GitAsync("-C", RepoRoot(), "rev-parse", "HEAD")).Trim();

        public async Task<string> GitAsync(params string[] arguments)
        {
            var result = await RunAsync("git", arguments, 60);
            result.Exit.ShouldBe(0, result.Text);
            return result.Stdout;
        }

        public string[] ReleaseArguments(string? candidate = null) =>
            ["--release-evidence", "--baseline-sha", Baseline, "--candidate-sha", candidate ?? Candidate, "--target-platform", "linux", "--json"];

        public Task<JsonElement> ReleaseAsync(string? candidate = null)
        {
            var result = Select(ReleaseArguments(candidate));
            result.Exit.ShouldBe(0, result.Text);
            using var document = JsonDocument.Parse(result.Stdout);
            return Task.FromResult(document.RootElement.Clone());
        }

        // The selector on this fixture's repository, its algorithm source the checkout (or the source given).
        public (int Exit, string Stdout, string Text) Select(params string[] arguments) => Select(RepoRoot(), arguments);

        public (int Exit, string Stdout, string Text) Select(string sourceRoot, string[] arguments)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                return ImpactedClassesRun.Select(sourceRoot, ["--repo", Root, .. arguments]);
            }
            finally
            {
                Console.WriteLine($"C1820 phase=select elapsedMs={watch.ElapsedMilliseconds} fixture={Path.GetFileName(Root)}");
            }
        }

        private async Task<(int Exit, string Stdout, string Text)> RunAsync(string command, string[] arguments, int seconds)
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
                return (process.ExitCode, stdout.Result, stdout.Result + stderr.Result);
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
