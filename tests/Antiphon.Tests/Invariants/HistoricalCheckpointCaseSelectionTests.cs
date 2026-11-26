using System.Reflection;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Requests;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class HistoricalCheckpointCaseSelectionTests
{
    [Test]
    public void C2082_Historical_rows_keep_their_expanded_cases()
    {
        var root = DockerStackDocuments.RepoRoot;
        var scratch = OwnedTempRoot.CreateOwned();
        try
        {
            var assembly = typeof(HistoricalCheckpointCaseSelectionTests).Assembly;
            var discovery = LiveLaneDiscovery.Run(root, assembly.GetName().Name!, assembly.Location, scratch);
            discovery.Ok.ShouldBeTrue(discovery.Error);
            var failures = HistoricalCheckpointCaseAudit.Check(root, discovery.Nodes);
            failures.ShouldBeEmpty(string.Join('\n', failures));
        }
        finally
        {
            OwnedTempRoot.Release(scratch);
        }
    }
}

internal static class HistoricalCheckpointCaseAudit
{
    internal const string BaselinePath = "tests/fixtures/checkpoint-historical-case-selections.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Baseline(string BaselineSource, string[] ImportablePlans, RequiredRow[] Rows);
    private sealed record RequiredRow(string Plan, string Row, string Project, string[] Uids);

    internal static IReadOnlyList<string> Check(string root, IReadOnlyList<LiveLaneNode> discovered)
    {
        var baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(Path.Combine(root, BaselinePath)), Json)
            ?? throw new InvalidDataException("Empty historical checkpoint case baseline.");
        if (baseline.ImportablePlans.Length == 0 || baseline.Rows.Length == 0)
            throw new InvalidDataException("The historical checkpoint baseline must contain import and case obligations.");

        var cases = discovered.Select(node => (
            node.Project, node.Uid,
            Path: "/" + string.Join('/', new[] { node.Project, node.Namespace, node.TypeOrClass, node.Method }.Select(Uri.EscapeDataString)),
            Properties: new PropertyBag(node.Categories.Select(category => new TestMetadataProperty("Category", Uri.EscapeDataString(category))))))
            .ToArray();
        var failures = new List<string>();
        foreach (var isWindows in new[] { false, true })
        {
            var platform = isWindows ? "windows" : "linux";
            var manifests = new Dictionary<string, CheckpointManifest>(StringComparer.Ordinal);
            foreach (var plan in baseline.ImportablePlans.Concat(baseline.Rows.Select(row => row.Plan)).Distinct(StringComparer.Ordinal))
            {
                var result = PlanTableImporter.ImportMarkdown(File.ReadAllText(Path.Combine(root, plan)), plan, isWindows);
                if (result.ExitCode != 0 || result.Manifest is null)
                    failures.Add($"{platform} {plan}: {result.Error}");
                else
                    manifests.Add(plan, result.Manifest);
            }

            foreach (var expect in baseline.Rows)
            {
                if (!manifests.TryGetValue(expect.Plan, out var manifest))
                    continue;
                var key = $"{platform} {expect.Plan} {expect.Row}";
                var row = manifest.Checkpoints.SingleOrDefault(candidate => candidate.Id == expect.Row);
                var build = manifest.Builds.SingleOrDefault(candidate => candidate.Id == row?.Build);
                if (row?.Filter is null || build?.Project != expect.Project || expect.Uids.Length == 0)
                {
                    failures.Add(key + ": missing case obligation or project binding");
                    continue;
                }
#pragma warning disable TPEXP // Match with the pinned MTP parser rather than approximating its selection semantics.
                var filter = (TreeNodeFilter)(Activator.CreateInstance(typeof(TreeNodeFilter),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, [row.Filter], culture: null)
                    ?? throw new InvalidOperationException("Pinned MTP treenode matcher is unavailable."));
                var project = Path.GetFileNameWithoutExtension(expect.Project.EndsWith(".csproj", StringComparison.Ordinal)
                    ? expect.Project : expect.Project + ".csproj");
                var selected = cases.Where(test => test.Project == project && filter.MatchesFilter(test.Path, test.Properties))
                    .Select(test => test.Uid).ToHashSet(StringComparer.Ordinal);
#pragma warning restore TPEXP
                var missing = expect.Uids.Where(uid => !selected.Contains(uid)).ToArray();
                if (missing.Length > 0)
                    failures.Add($"{key}: missing {missing.Length}/{expect.Uids.Length} UIDs ({string.Join(", ", missing.Take(5))})");
            }
        }
        return failures;
    }
}
