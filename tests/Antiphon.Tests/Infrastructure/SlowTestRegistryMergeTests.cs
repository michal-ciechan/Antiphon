using Antiphon.Server.Infrastructure.Git;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.TestClassificationPolicyTests;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class SlowTestRegistryMergeTests
{
    private const string Registry = "tests/Antiphon.Tests/slow-tests-allowlist.txt";

    [Test]
    [Arguments("common-base")]
    [Arguments("target-only")]
    public Task C1383_Rebase_preserves_distinct_appended_pairs(string placement) =>
        RunScenario(placement, "distinct");

    [Test]
    [Arguments("same-reason-different-classes")]
    [Arguments("different-reasons-same-class")]
    public Task C1383_Union_collapsed_lines_are_rejected(string shape) =>
        RunScenario("common-base", shape);

    [Test]
    public Task C1383_Source_only_attribute_does_not_rescue_first_rebase() =>
        RunScenario("source-only", "distinct");

    private static async Task RunScenario(string placement, string shape)
    {
        await using var fixture = new LandingGitFixture();
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var git = fixture.Git;
        var repo = fixture.Repository;
        var land = Path.Combine(fixture.Root, "land");
        var entries = C1383Entries();
        var left = entries.Single(e => e.Name == C1383Smoke);
        var right = entries.Single(e => e.Name == C1383Other);
        if (shape == "same-reason-different-classes")
        {
            left = left with { Reason = "CARD-1383 shared reason" };
            right = right with { Reason = left.Reason };
        }
        else if (shape == "different-reasons-same-class")
        {
            left = left with { Reason = "CARD-1383 target reason" };
            right = left with { Reason = "CARD-1383 source reason" };
        }
        var kept = entries.Where(e => e.Name != left.Name && e.Name != right.Name).ToArray();
        var seed = C1383RegistryText(kept);
        var registryPath = TestClassificationMetadata.FindRegistryPath(typeof(SlowTestRegistryMergeTests).Assembly);
        var actualAttributes = await File.ReadAllBytesAsync(
            Path.Combine(Directory.GetParent(registryPath)!.Parent!.Parent!.FullName, ".gitattributes"), budget.Token);

        async Task<string> Required(string path, params string[] args)
        {
            var result = await git.RunAsync(path, args, budget.Token);
            result.Succeeded.ShouldBeTrue($"setup/observation {string.Join(" ", args)}: {result.Diagnostic}");
            return result.Output.TrimEnd('\r', '\n');
        }
        async Task Commit(string message)
        {
            await Required(repo, "add", ".");
            await Required(repo, "commit", "-m", message);
        }
        async Task WriteAttributes() =>
            await File.WriteAllBytesAsync(Path.Combine(repo, ".gitattributes"), actualAttributes, budget.Token);
        async Task<string> AttributesAt(string sha) =>
            await Required(repo, "ls-tree", "--name-only", sha, "--", ".gitattributes");

        await Required(repo, "init", "-b", "master");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo, Registry))!);
        await File.WriteAllTextAsync(Path.Combine(repo, Registry), seed, budget.Token);
        if (placement == "common-base") await WriteAttributes();
        await Commit("base registry");
        var baseSha = await Required(repo, "rev-parse", "HEAD");
        await Required(repo, "branch", "source");
        if (placement == "target-only")
        {
            await WriteAttributes();
            await Commit("target attributes before append");
        }
        await File.AppendAllTextAsync(Path.Combine(repo, Registry), C1383RegistryText([left]), budget.Token);
        await Commit("target append");
        var targetSha = await Required(repo, "rev-parse", "HEAD");
        await Required(repo, "checkout", "source");
        if (placement == "source-only") await WriteAttributes();
        await File.AppendAllTextAsync(Path.Combine(repo, Registry), C1383RegistryText([right]), budget.Token);
        await Commit("source append");
        var sourceSha = await Required(repo, "rev-parse", "HEAD");
        (await AttributesAt(baseSha)).ShouldBe(placement == "common-base" ? ".gitattributes" : "");
        (await AttributesAt(targetSha)).ShouldBe(placement == "source-only" ? "" : ".gitattributes");
        (await AttributesAt(sourceSha)).ShouldBe(placement == "target-only" ? "" : ".gitattributes");
        await Required(repo, "worktree", "add", "--detach", land, sourceSha);
        (await git.RunAsync(land, ["config", "--get", "merge.union.driver"], budget.Token)).ExitCode.ShouldBe(1);
        string[] vector = ["-c", "rebase.autoStash=false", "-c", "rebase.updateRefs=false", "rebase", targetSha];
        try
        {
            var result = await git.RunAsync(land, vector, budget.Token);
            git.Trace.ShouldContain(a => a.SequenceEqual(vector));
            if (placement == "source-only")
            {
                result.Succeeded.ShouldBeFalse("the replayed attribute cannot rescue its own conflicting commit");
                (await Required(land, "ls-files", "-u")).ShouldContain(Registry);
                return;
            }
            result.Succeeded.ShouldBeTrue("real registry rebase must succeed with the repository union rule");
            result.RebaseHeadSha.ShouldNotBeNull();
            result.RebaseHeadSha.ShouldBe(await Required(land, "rev-parse", "HEAD"));
            (await Required(land, "ls-files", "-u")).ShouldBe("");
            var merged = await Required(land, "show", "HEAD:" + Registry);
            merged.ShouldNotContain("<<<<<<<");
            merged.ShouldNotContain("=======");
            merged.ShouldNotContain(">>>>>>>");
            (await Required(land, "check-attr", "merge", "--", Registry)).ShouldBe(Registry + ": merge: union");
            foreach (var sibling in new[] { "tests/Antiphon.SessionRunner.Tests/slow-tests-allowlist.txt", "docs/example.md" })
                (await Required(land, "check-attr", "merge", "--", sibling)).ShouldBe(sibling + ": merge: unspecified");
            var lines = merged.Split('\n');
            var errors = C1383Errors(merged);
            foreach (var entry in kept)
                CountPair(lines, entry).ShouldBe(1, entry.Name);
            if (shape == "distinct")
            {
                CountPair(lines, left).ShouldBe(1);
                CountPair(lines, right).ShouldBe(1);
                lines.Count(l => !l.StartsWith('#') && !string.IsNullOrWhiteSpace(l)).ShouldBe(entries.Count);
                errors.ShouldBe("");
            }
            else if (shape == "same-reason-different-classes")
            {
                lines.Count(l => l == "# " + left.Reason).ShouldBe(1);
                lines.Count(l => l == left.Name).ShouldBe(1);
                lines.Count(l => l == right.Name).ShouldBe(1);
                var first = Array.FindIndex(lines, l => l == "# " + left.Reason);
                new[] { lines[first + 1], lines[first + 2] }.Order().ShouldBe(new[] { left.Name, right.Name }.Order());
                errors.ShouldContain($"missing-adjacent-reason line {first + 3} {lines[first + 2]}");
            }
            else
            {
                lines.Count(l => l == left.Name).ShouldBe(1);
                lines.Count(l => l == "# " + left.Reason).ShouldBe(1);
                lines.Count(l => l == "# " + right.Reason).ShouldBe(1);
                var row = Array.IndexOf(lines, left.Name);
                new[] { lines[row - 2], lines[row - 1] }.Order()
                    .ShouldBe(new[] { "# " + left.Reason, "# " + right.Reason }.Order());
                errors.ShouldContain($"orphan-reason line {row - 1}");
            }
        }
        finally
        {
            // A failed assertion after a real conflict still leaves no active fixture rebase.
            await git.RunAsync(land, ["rebase", "--abort"], CancellationToken.None);
        }
    }

    private static int CountPair(string[] lines, TestClassificationMetadata.RegistryEntry entry) =>
        Enumerable.Range(1, lines.Length - 1).Count(i => lines[i - 1] == "# " + entry.Reason && lines[i] == entry.Name);
}
