using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;
using static Antiphon.Tests.Scripts.IncrementalSelectionSoundnessTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class IncrementalSelectionSoundnessInvariantTests
{
    // In-process: the HistoricalSelectionSoundness case (RcInputOwnersMapTests) replays the corpus through the committed
    // owner inventory (Antiphon.Nightly.RcInputInventory), with its three rows: contract, unowned and composer-inputs.
    [Test, Timeout(150_000)]
    public void Recorded_owner_map_status_matches_the_committed_map(CancellationToken cancellationToken)
    {
        _ = LoadCorpus(RepoRoot());
        cancellationToken.ThrowIfCancellationRequested();
        RcInputOwnersMapTests.HistoricalSelectionSoundness();
    }

    [Test, Timeout(60_000)]
    public void Project_closure_misses_match_the_recorded_known_unsound_set()
    {
        var root = RepoRoot();
        var corpus = LoadCorpus(root);
        var graph = ProjectGraph.Load(root);
        var missed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var item in corpus.Cases!)
        {
            var status = graph.Selects(item.SuiteProject, item.ChangedPaths!) ? "selected" : "missed";
            status.ShouldBe(item.M1, item.Id);
            if (status == "missed")
                missed.Add(item.Id);
        }

        var recorded = new SortedSet<string>(corpus.KnownUnsound!, StringComparer.Ordinal);
        string.Join(",", missed).ShouldBe(string.Join(",", recorded));
    }
}
