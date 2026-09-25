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
        var merged = ReportMerger.Merge([first, second]);
        var row = merged.Rows.ShouldHaveSingleItem();
        row.State.ShouldBe("green");
        row.ExitCode.ShouldBe(0);
        row.Reruns.ShouldBe(1);
        merged.ExitCode.ShouldBe(0);
    }

    private static ReportModel Run(string id, DateTimeOffset ended, int exit, string state) => new()
    {
        RunId = id,
        ManifestHash = "same",
        EndedAt = ended,
        StartedAt = ended.AddMinutes(-5),
        Commit = new string('a', 40),
        Rows =
        [
            new ReportRow
            {
                Id = "CP-1",
                State = state,
                ExitCode = exit,
                Seconds = 10,
                Line = "CHECKPOINT CP-1 commit=" + new string('a', 40) + " build=ok filter=/ executed=1 passed=1 failed=0 skipped=0 trx=/t slot=unavailable waited=0s",
            },
        ],
    };
}
