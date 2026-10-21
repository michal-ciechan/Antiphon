using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>CARD-0550 D-4: which E2E waits renew on protocol git progress and which stay plain. Source-pinned.</summary>
[Category("Unit")]
public sealed class LandDeliveryWaitRoutingTests
{
    private static readonly string[] PlainLand = ["both hold age receipts", "outcome warning and error obligations"];
    private static readonly string[] ProtocolLand = ["busy caller has queued outcome", "two persisted enqueue failures",
        "unreceived outcome held at actual queue", "busy outcome queued"];
    private static readonly string[] PlainFixture = ["native caller ready", "owned child startup", "two additional completed notification scans"];
    private static readonly string[] ProtocolFixture = ["complete native Land receipt"];
    private static readonly string[] PlainDispatchFixture = ["two completed notification scans", "interrupted post-prompt queue rows recovered after eligibility"];
    private static readonly string[] ProtocolDispatchFixture = ["two committed reduced dispatch intents"];

    [Test]
    public void Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not()
    {
        var root = RepositoryRoot();
        var land = File.ReadAllText(Path.Combine(root, "tests", "Antiphon.E2E", "AgentTaskLandDeliveryE2ETests.cs"));
        var dispatchTests = File.ReadAllText(Path.Combine(root, "tests", "Antiphon.E2E", "DispatchBaseWarningDeliveryE2ETests.cs"));
        var fixture = File.ReadAllText(Path.Combine(root, "tests", "Antiphon.E2E", "Fixtures", "LandDeliveryFixture.cs"));
        var dispatchFixture = File.ReadAllText(Path.Combine(root, "tests", "Antiphon.E2E", "Fixtures", "LandDeliveryFixture.Dispatch.cs"));
        foreach (var e in PlainLand) NearestWait(land, e).ShouldBe("UntilAsync(", e);
        foreach (var e in ProtocolLand) NearestWait(land, e).ShouldBe("UntilProtocolAsync(", e);
        foreach (var e in PlainFixture) NearestWait(fixture, e).ShouldBe("UntilAsync(", e);
        foreach (var e in ProtocolFixture) NearestWait(fixture, e).ShouldBe("UntilProtocolAsync(", e);
        foreach (var e in PlainDispatchFixture) NearestWait(dispatchFixture, e).ShouldBe("UntilAsync(", e);
        foreach (var e in ProtocolDispatchFixture) NearestWait(dispatchFixture, e).ShouldBe("UntilProtocolAsync(", e);
        // The three crash-cut barriers go through the instance helper; the inline lambdas are gone.
        land.ShouldContain("WaitForBoundaryAsync(\"terminal-committed\")");
        land.ShouldContain("WaitForBoundaryAsync(\"queue-inserted\")");
        land.ShouldContain("WaitForBoundaryAsync(barrier)");
        land.ShouldNotContain("terminal committed crash cut");
        land.ShouldNotContain("queue inserted crash cut");
        land.ShouldNotContain("native prompt persisted before receipt/verdict save");
        dispatchFixture.ShouldContain("public Task WaitForBoundaryAsync(string boundary) => UntilProtocolAsync(");
        dispatchFixture.ShouldContain("public Task WaitForDispatchBoundaryAsync(string boundary) => UntilDispatchAsync(");
        // C540: the inline queue-cut wait renews on protocol-git plus dispatch markers and keeps its key-retention assertion; the enqueue-failure wait stays plain.
        NearestWait(dispatchTests, "every dispatch warning must retain its notification key before the insert barrier").ShouldBe("UntilDispatchAsync(");
        NearestWait(dispatchTests, "two owned enqueue failures").ShouldBe("UntilAsync(");
        // CARD-1359: the other crash cuts call the dispatch-aware boundary helper (DispatchBaseWarningDeliveryE2ETests.cs:139).
        dispatchTests.ShouldContain("else await f.WaitForDispatchBoundaryAsync(boundary);");
    }

    private static string NearestWait(string text, string evidence)
    {
        var at = text.IndexOf("\"" + evidence + "\"", StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, evidence);
        var plain = text.LastIndexOf("UntilAsync(", at, StringComparison.Ordinal);
        var protocol = text.LastIndexOf("UntilProtocolAsync(", at, StringComparison.Ordinal);
        var dispatch = text.LastIndexOf("UntilDispatchAsync(", at, StringComparison.Ordinal);
        if (dispatch > plain && dispatch > protocol) return "UntilDispatchAsync(";
        return protocol > plain ? "UntilProtocolAsync(" : "UntilAsync(";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Antiphon.sln not found above " + AppContext.BaseDirectory);
    }
}
