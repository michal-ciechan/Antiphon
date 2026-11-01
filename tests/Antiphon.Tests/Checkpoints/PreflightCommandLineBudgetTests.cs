using System.ComponentModel;
using System.Globalization;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-1618: checkpoint preflight put every write root on one git command line.
[TUnit.Core.Category("Unit")]
public sealed class PreflightCommandLineBudgetTests
{
    [Test]
    [Timeout(30_000)]
    public void oversized_path_list_stays_within_the_injected_budget_and_matches_one_call()
    {
        var f = new HostCleanupCheckpointFixture();
        var paths = Paths(f, 6, "root");
        paths.Add(Path.Combine(f.Parent, "tracked-root"));
        f.Io.GitResult = request => new(0, Tracked(request) ? "tracked\0" : "", "", false);

        var budget = OnePathBudget(f, paths[0]);
        f.Io.GitRequests.Clear();
        var wide = f.Delete.PreflightRoots(f.Parent, paths, timeoutMs: 3_000, commandLineBudget: int.MaxValue);
        var wideCalls = LsFiles(f);
        wideCalls.Count.ShouldBe(1);
        wide.ShouldBe("tracked");
        wideCalls[0].Arguments.ShouldContain(paths[^1]);

        f.Io.GitRequests.Clear();
        var narrow = f.Delete.PreflightRoots(f.Parent, paths, timeoutMs: 3_000, commandLineBudget: budget);
        var narrowCalls = LsFiles(f);
        narrowCalls.Count.ShouldBeGreaterThan(1);
        foreach (var call in narrowCalls)
            CheckpointDelete.ArgumentCharacters(call.Arguments).ShouldBeLessThanOrEqualTo(budget);
        narrow.ShouldBe(wide);
    }

    [Test]
    [Timeout(30_000)]
    public void a_failing_batch_fails_the_preflight()
    {
        var f = new HostCleanupCheckpointFixture();
        var paths = Paths(f, 4, "batch");
        var budget = OnePathBudget(f, paths[0]);
        var listings = 0;
        f.Io.GitResult = request =>
        {
            if (!request.Arguments.Contains("ls-files")) return new(0, "", "", false);
            listings++;
            return listings == 1 ? new(0, "", "", false) : new(1, "", "batch failed", false);
        };
        f.Io.GitRequests.Clear();

        var result = f.Delete.PreflightRoots(f.Parent, paths, timeoutMs: 3_000, commandLineBudget: budget);

        result.ShouldBe("git-failed");
        listings.ShouldBeGreaterThan(1);
    }

    [Test]
    [Timeout(30_000)]
    public void win32_start_failure_reports_the_cause_and_argument_length()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Io.Put(Path.Combine("/bin", "git"), "executable");
        f.Io.GitResult = _ => throw new Win32Exception(206);

        var result = f.Delete.PreflightRoots(f.Parent, [f.Root], timeoutMs: 3_000);

        result.ShouldNotBeNull();
        result.ShouldContain("win32=206");
        var length = CheckpointDelete.ArgumentCharacters(f.Io.GitRequests[0].Arguments)
            .ToString(CultureInfo.InvariantCulture);
        result.ShouldContain("argumentCharacters=" + length);
    }

    private static int OnePathBudget(HostCleanupCheckpointFixture f, string path)
    {
        f.Io.GitRequests.Clear();
        f.Delete.PreflightRoots(f.Parent, [path], timeoutMs: 3_000, commandLineBudget: int.MaxValue).ShouldBeNull();
        f.Io.GitRequests.Count.ShouldBe(1);
        return CheckpointDelete.ArgumentCharacters(f.Io.GitRequests[0].Arguments);
    }

    private static List<string> Paths(HostCleanupCheckpointFixture f, int count, string name)
    {
        var paths = new List<string>(count);
        for (var i = 0; i < count; i++)
            paths.Add(Path.Combine(f.Parent, name + "-" + i.ToString("00", CultureInfo.InvariantCulture) + "-" + new string('p', 48)));
        return paths;
    }

    private static bool Tracked(CheckpointGitRequest request)
    {
        foreach (var argument in request.Arguments)
            if (argument.EndsWith("tracked-root", StringComparison.Ordinal)) return true;
        return false;
    }

    private static List<CheckpointGitRequest> LsFiles(HostCleanupCheckpointFixture f) =>
        f.Io.GitRequests.Where(request => request.Arguments.Contains("ls-files")).ToList();
}
