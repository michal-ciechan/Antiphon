using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>
/// CARD-0550 D-4: which E2E waits renew on protocol git progress and which stay plain. Source-pinned.
/// CARD-1412: the pin reads the comment-stripped call that owns each evidence string (its name and
/// its argument list) and the renewal body of each renewing helper, so a probe smuggled into a
/// plain call (M2b), a helper that drops its protocol probe (M5) or a comment that names another
/// wait (M6) all go red instead of surviving a name-only search.
/// </summary>
[Category("Unit")]
public sealed class LandDeliveryWaitRoutingTests
{
    private const string LandTests = "tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs";
    private const string DispatchTests = "tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs";
    private const string Fixture = "tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs";
    private const string DispatchFixture = "tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs";
    private static readonly string[] PlainLand = ["both hold age receipts", "outcome warning and error obligations"];
    private static readonly string[] ProtocolLand = ["busy caller has queued outcome", "two persisted enqueue failures",
        "unreceived outcome held at actual queue", "busy outcome queued"];
    private static readonly string[] PlainFixture = ["native caller ready", "owned child startup", "two additional completed notification scans"];
    private static readonly string[] ProtocolFixture = ["complete native Land receipt"];
    private static readonly string[] PlainDispatchFixture = ["two completed notification scans", "interrupted post-prompt queue rows recovered after eligibility"];
    private static readonly string[] ProtocolDispatchFixture = ["two committed reduced dispatch intents"];
    private static readonly string[] PlainDispatchTests = ["two owned enqueue failures"];
    private static readonly string[] WaitNames = ["UntilAsync(", "UntilProtocolAsync(", "UntilDispatchAsync("];

    /// <summary>A wait call: its name token and its top-level arguments, in comment-stripped source.</summary>
    private sealed record WaitCall(string Name, IReadOnlyList<string> Arguments);

    [Test]
    public void Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not()
    {
        var root = RepositoryRoot();
        var land = File.ReadAllText(Path.Combine(root, LandTests));
        var dispatchTests = File.ReadAllText(Path.Combine(root, DispatchTests));
        var fixture = File.ReadAllText(Path.Combine(root, Fixture));
        var dispatchFixture = File.ReadAllText(Path.Combine(root, DispatchFixture));
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
        foreach (var e in PlainDispatchTests) NearestWait(dispatchTests, e).ShouldBe("UntilAsync(", e);
        // CARD-1359: the other crash cuts call the dispatch-aware boundary helper (DispatchBaseWarningDeliveryE2ETests.cs:139).
        dispatchTests.ShouldContain("else await f.WaitForDispatchBoundaryAsync(boundary);");
    }

    /// <summary>
    /// CARD-1412 M2b: a plain wait is plain because it hands the shared wait no progress probe.
    /// <c>UntilAsync(predicate, evidence, seconds = 60, progress = null, capSeconds = 600)</c>: at most
    /// the quiet seconds follow the evidence, and nothing names the progress parameter.
    /// </summary>
    [Test]
    public void Plain_waits_pass_no_progress_probe()
    {
        var root = RepositoryRoot();
        foreach (var (file, evidences) in new[] { (LandTests, PlainLand), (Fixture, PlainFixture),
                     (DispatchFixture, PlainDispatchFixture), (DispatchTests, PlainDispatchTests) })
        {
            var text = StripComments(File.ReadAllText(Path.Combine(root, file)));
            foreach (var evidence in evidences)
            {
                var call = OwningWait(text, evidence);
                call.Name.ShouldBe("UntilAsync(", evidence);
                call.Arguments.Count.ShouldBeLessThanOrEqualTo(3, $"{evidence}: a plain wait passes predicate, evidence and at most the quiet seconds");
                call.Arguments.ShouldAllBe(a => !a.StartsWith("progress:", StringComparison.Ordinal), $"{evidence}: a plain wait never names the progress probe");
            }
        }
    }

    /// <summary>
    /// CARD-1412 M5: the renewing helpers are renewing because of what they forward to the shared wait.
    /// <c>UntilProtocolAsync</c> forwards the protocol-git probe itself, then the optional clock;
    /// <c>UntilDispatchAsync</c> forwards the protocol-git count plus the dispatch total its predicate
    /// wrapper refreshes on every poll.
    /// </summary>
    [Test]
    public void Renewing_helpers_forward_the_protocol_probe_to_the_shared_wait()
    {
        var root = RepositoryRoot();
        var fixture = StripComments(File.ReadAllText(Path.Combine(root, Fixture)));
        var dispatchFixture = StripComments(File.ReadAllText(Path.Combine(root, DispatchFixture)));
        var protocol = InnerWait(fixture, "public async Task UntilProtocolAsync(");
        protocol.Arguments.Count.ShouldBe(5, "UntilProtocolAsync forwards predicate, evidence, quietSeconds, the probe and the clock");
        protocol.Arguments[3].ShouldBe("ProtocolProgress");
        protocol.Arguments[4].ShouldBe("clock: clock");
        var dispatch = InnerWait(dispatchFixture, "public async Task UntilDispatchAsync(");
        dispatch.Arguments.Count.ShouldBe(4, "UntilDispatchAsync forwards its predicate wrapper, evidence, quietSeconds and the probe");
        dispatch.Arguments[3].ShouldBe("() => ProtocolProgress() + dispatch");
        dispatch.Arguments[0].ShouldContain("last = await DispatchProgressAsync();", Case.Sensitive, "the wrapper refreshes the dispatch markers on every poll");
        dispatch.Arguments[0].ShouldContain("dispatch = last.Total;", Case.Sensitive, "the probe reads the refreshed dispatch total");
    }

    /// <summary>
    /// CARD-1412 M6: the owning call is decided by code, never by a comment between the call and its
    /// evidence, and the evidence must sit inside that call's argument list.
    /// </summary>
    [Test]
    public void A_comment_naming_another_wait_does_not_route_the_evidence()
    {
        const string masked = "await LandDeliveryFixture.UntilAsync(() => Task.FromResult(true), // UntilDispatchAsync(\n" +
            "    /* UntilProtocolAsync( */ \"two owned enqueue failures\"); // trailing \"UntilDispatchAsync(\"\n";
        NearestWait(masked, "two owned enqueue failures").ShouldBe("UntilAsync(");
        var call = OwningWait(StripComments(masked), "two owned enqueue failures");
        call.Arguments.Count.ShouldBe(2);
        call.Arguments[1].ShouldBe("\"two owned enqueue failures\"");
        StripComments("var s = \"// not a comment\"; var q = '\"'; var v = @\"a\"\"b\"; // gone\n/* and */ x")
            .ShouldBe("var s = \"// not a comment\"; var q = '\"'; var v = @\"a\"\"b\"; \n  x");
        // Evidence that follows a wait call without being one of its arguments has no owning wait.
        Should.Throw<ShouldAssertException>(() => OwningWait("UntilProtocolAsync(p, \"a\"); var x = \"b\";", "b"));
    }

    private static string NearestWait(string text, string evidence) => OwningWait(StripComments(text), evidence).Name;

    /// <summary>The nearest preceding wait call whose parentheses enclose the quoted evidence.</summary>
    private static WaitCall OwningWait(string stripped, string evidence)
    {
        var at = stripped.IndexOf("\"" + evidence + "\"", StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, evidence);
        var (name, index) = WaitNames.Select(n => (name: n, index: stripped.LastIndexOf(n, at, StringComparison.Ordinal))).MaxBy(x => x.index);
        index.ShouldBeGreaterThanOrEqualTo(0, $"\"{evidence}\" has no preceding wait call");
        var open = index + name.Length - 1;
        var close = MatchingBracket(stripped, open, '(', ')');
        close.ShouldBeGreaterThan(at, $"\"{evidence}\" must be inside the argument list of the nearest {name} call");
        return new(name, SplitArguments(stripped.Substring(open + 1, close - open - 1)));
    }

    /// <summary>The first shared-wait call inside the body of the helper whose declaration starts with <paramref name="declaration"/>.</summary>
    private static WaitCall InnerWait(string stripped, string declaration)
    {
        var at = stripped.IndexOf(declaration, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, declaration);
        var parameters = MatchingBracket(stripped, at + declaration.Length - 1, '(', ')');
        var bodyOpen = stripped.IndexOf('{', parameters);
        var bodyClose = MatchingBracket(stripped, bodyOpen, '{', '}');
        var call = stripped.IndexOf("UntilAsync(", bodyOpen, StringComparison.Ordinal);
        (call >= 0 && call < bodyClose).ShouldBeTrue(declaration + " must call UntilAsync in its body");
        var open = call + "UntilAsync(".Length - 1;
        var close = MatchingBracket(stripped, open, '(', ')');
        return new("UntilAsync(", SplitArguments(stripped.Substring(open + 1, close - open - 1)));
    }

    /// <summary>Removes line and block comments; string and character literals are kept verbatim.</summary>
    internal static string StripComments(string source)
    {
        var result = new StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + 2;
                result.Append(' ');
                continue;
            }
            if (c is '"' or '\'')
            {
                var end = LiteralEnd(source, i);
                result.Append(source, i, end - i);
                i = end;
                continue;
            }
            result.Append(c);
            i++;
        }
        return result.ToString();
    }

    /// <summary>Index just past the string or character literal that opens at <paramref name="start"/>.</summary>
    private static int LiteralEnd(string text, int start)
    {
        var quote = text[start];
        var verbatim = quote == '"' && (start > 0 && text[start - 1] == '@' || start > 1 && text[start - 2] == '@' && text[start - 1] == '$');
        var i = start + 1;
        while (i < text.Length)
        {
            if (verbatim && text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"') { i += 2; continue; }
            if (!verbatim && text[i] == '\\') { i += 2; continue; }
            if (text[i] == quote) return i + 1;
            i++;
        }
        return text.Length;
    }

    private static int MatchingBracket(string text, int open, char opening, char closing)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'') { i = LiteralEnd(text, i) - 1; continue; }
            if (c == opening) depth++;
            else if (c == closing && --depth == 0) return i;
        }
        throw new InvalidOperationException($"no {closing} matches the {opening} at index {open}");
    }

    /// <summary>Top-level arguments of a call, split on commas outside nested brackets and literals.</summary>
    private static IReadOnlyList<string> SplitArguments(string span)
    {
        List<string> arguments = [];
        var depth = 0;
        var start = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (c is '"' or '\'') { i = LiteralEnd(span, i) - 1; continue; }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == ',' && depth == 0) { arguments.Add(span[start..i].Trim()); start = i + 1; }
        }
        var last = span[start..].Trim();
        if (last.Length > 0 || arguments.Count > 0) arguments.Add(last);
        return arguments;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Antiphon.sln not found above " + AppContext.BaseDirectory);
    }
}
