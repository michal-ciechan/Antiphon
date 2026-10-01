using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class ReportMergerTests
{
    [Test]
    public void latest_per_cp_wins_and_earlier_attempts_count_as_reruns()
    {
        var first = Run("r1", DateTimeOffset.Parse("2026-09-25T19:00:00Z"), 1, "red");
        var second = Run("r2", DateTimeOffset.Parse("2026-09-25T19:10:00Z"), 0, "green");
        first.MaxConcurrentBuilds = 2;
        first.MaxConcurrentRows = 3;
        second.MaxConcurrentBuilds = 1;
        second.MaxConcurrentRows = 1;
        var merged = ReportMerger.Merge([first, second]);
        var row = merged.Rows.ShouldHaveSingleItem();
        row.State.ShouldBe("green");
        row.ExitCode.ShouldBe(0);
        row.Reruns.ShouldBe(1);
        merged.ExitCode.ShouldBe(0);
        merged.MaxConcurrentBuilds.ShouldBe(2);
        merged.MaxConcurrentRows.ShouldBe(3);
    }

    private static ReportModel Run(string id, DateTimeOffset ended, int exit, string state)
    {
        var observed = new SourceObservation(new string('a', 40), 0, new string('1', 64), ended,
            "known");
        SourceEvidence Source(string binding) => new()
        {
            Start = observed, End = observed, State = "clean", BuildSource = binding,
        };
        return new ReportModel
        {
            RunId = id,
            ManifestHash = "same",
            EndedAt = ended,
            StartedAt = ended.AddMinutes(-5),
            Commit = new string('a', 40),
            Source = Source("verified"),
            Rows =
            [
                new ReportRow
                {
                    Id = "CP-1", State = state, ExitCode = exit, Seconds = 10, Source = Source("verified"),
                    Line = "CHECKPOINT CP-1 commit=" + new string('a', 40) + " build=ok filter=/ executed=1 passed=1 failed=0 skipped=0 trx=/t slot=unavailable waited=0s dirty=0 source=" + new string('a', 40) + " sourceState=clean buildSource=verified",
                },
            ],
        };
    }
}
