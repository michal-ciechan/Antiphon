using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1155. The roster pin records a public E2E method when Test is in the
/// attribute list on that declaration, after comments and string literals are
/// blanked, and compares those names to the plan's E2E checkpoint filters.
/// Queue insert, Sent, or ack does not discharge an E2E row.
/// </summary>
[Category("Unit")]
public class DispatchBaseWarningRosterTests
{
    private const string PlanRelative =
        "docs/superpowers/plans/2026-10-11-card-1155-dispatch-warning-e2e-roster-plan.md";

    private const string BatchPlanRelative =
        "docs/superpowers/plans/2026-10-08-test-hardening-batch-1137-1134-1130-1126-1152-plan.md";

    private const string TestsRelative =
        "tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs";

    private const string PinFilter =
        "/*/*/DispatchBaseWarningRosterTests/*";

    private const string MethodPattern =
        @"\bpublic(?:\s+(?:static|async|unsafe|partial|virtual|override|sealed|new))*\s+(?<type>[A-Za-z_][A-Za-z0-9_]*(?:\s*<[^;\n{}()]*>)?(?:\s*\[\s*\])?\??(?:\s*\.\s*[A-Za-z_][A-Za-z0-9_]*(?:\s*<[^;\n{}()]*>)?(?:\s*\[\s*\])?\??)*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^;\n{}()]*>)?\s*\(";

    [Test]
    public void C1155_RosterNamesEveryDispatchWarningE2EMethod()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var planPath = Path.Combine(root, PlanRelative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(planPath).ShouldBeTrue(planPath);
        var plan = File.ReadAllText(planPath).Replace("\r\n", "\n");
        var collapsed = Regex.Replace(plan.Replace("`", ""), @"\s+", " ");
        collapsed.ShouldContain("complete UserPrompt transcript from the real producer to the recipient");
        collapsed.ShouldContain("Queue insert, Sent, or ack alone does not discharge");
        collapsed.ShouldContain("LandDeliveryFixture.InitializeAsync requires Windows");
        collapsed.ShouldContain("CP-1 does not discharge");
        collapsed.ShouldContain("CARD-0550");
        collapsed.ShouldContain("CP-24");

        var batch = File.ReadAllText(Path.Combine(root, BatchPlanRelative.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n");
        Section(batch, "Checkpoints").ShouldNotContain("DispatchBaseWarningDeliveryE2ETests");

        var source = File.ReadAllText(Path.Combine(root, TestsRelative.Replace('/', Path.DirectorySeparatorChar)));
        var methods = TestMethods(source);
        methods.Count.ShouldBeGreaterThan(0);

        var rows = CheckpointRows(Section(plan, "Checkpoints"));
        var pin = rows.Single(row => row.Filter == PinFilter);
        pin.Build.ShouldContain("tests/Antiphon.Tests -> ");
        pin.Covers.ShouldContain("V-1");
        pin.Min.ShouldBe("5");
        int.TryParse(pin.EstimatedMinutes, out var pinMinutes).ShouldBeTrue(pin.Id);
        pinMinutes.ShouldBeGreaterThan(0);

        var byMethod = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var match = Regex.Match(
                row.Filter,
                @"^/\*/\*/DispatchBaseWarningDeliveryE2ETests/([A-Za-z_][A-Za-z0-9_]*)\*$",
                RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;
            byMethod.ContainsKey(match.Groups[1].Value).ShouldBeFalse(match.Groups[1].Value);
            byMethod.Add(match.Groups[1].Value, row);
        }

        SortedNames(byMethod.Keys).ShouldBe(SortedNames(methods));

        var builds = byMethod.Values.Where(row => row.Build.StartsWith("tests/Antiphon.E2E -> ", StringComparison.Ordinal)).ToArray();
        builds.Length.ShouldBe(1);
        var build = builds[0];
        foreach (var row in byMethod.Values)
        {
            row.Min.ShouldBe("1");
            row.Covers.ShouldContain("V-2");
            int.TryParse(row.EstimatedMinutes, out var minutes).ShouldBeTrue(row.Id);
            minutes.ShouldBeGreaterThan(0);
            if (row.Id == build.Id)
                continue;
            Regex.IsMatch(row.Build, @"^CP-\d+$").ShouldBeTrue(row.Id + " " + row.Build);
            rows.Single(prior => prior.Id == row.Build).Build.ShouldBe(build.Build);
        }
    }

    [Test]
    public void C1155_ExtraAttributeAfterTestIsDiscovered()
    {
        const string source = """
            [Test]
            [Category("E2E")]
            public async Task WithFollowingAttribute() { }

            [Test, Timeout(1)]
            public async Task WithCombinedAttribute() { }

            [Test][Category("E2E")]
            public async Task WithStackedAttribute() { }

            [Arguments(1)]
            [Test]
            public async Task WithEarlierAttribute() { }

            [TestAttribute]
            public async Task WithSpelledAttribute() { }

            [Latest]
            public async Task NamedLatest() { }
            """;

        TestMethods(source).ShouldBe(new[]
        {
            "WithFollowingAttribute",
            "WithCombinedAttribute",
            "WithStackedAttribute",
            "WithEarlierAttribute",
            "WithSpelledAttribute",
        });
    }

    [Test]
    public void C1155_CommentedOutDeclarationIsNotCounted()
    {
        const string source = """
            // [Test] public async Task CommentedOut() { }
            // [Test]
            public async Task AttributeOnlyCommented() { }
            const string quoted = "[Test] public async Task Quoted() { }";
            @"[Test] public async Task Verbatim() { }"
            [Test]
            public async Task Live() { }
            """;

        TestMethods(source).ShouldBe(new[] { "Live" });
    }

    [Test]
    public void C1155_BlockCommentMethodIsNotCounted()
    {
        const string source = """
            /*
            [Test]
            public async Task Hidden() { }
            */
            [Test]
            public async Task Live() { }
            /* [Test] public async Task HiddenInline() { } */
            """;

        TestMethods(source).ShouldBe(new[] { "Live" });
    }

    [Test]
    public void C1155_RemovedAndRenamedMethodDiffersFromTheRoster()
    {
        const string source = """
            [Test]
            [Category("E2E")]
            public async Task Renamed() { }

            [Test, Timeout(1)]
            public async Task Kept() { }

            // [Test] public async Task Removed() { }
            """;

        var discovered = TestMethods(source);
        discovered.ShouldBe(new[] { "Renamed", "Kept" });
        var removedAndRenamed = new[] { "Kept", "OldName", "Removed" };
        SortedNames(discovered).SequenceEqual(SortedNames(removedAndRenamed)).ShouldBeFalse();
        SortedNames(discovered).ShouldBe(SortedNames(new[] { "Kept", "Renamed" }));
    }

    private static string[] SortedNames(IEnumerable<string> names) =>
        names.OrderBy(name => name, StringComparer.Ordinal).ToArray();

    private static List<string> TestMethods(string source)
    {
        var stripped = StripCommentsAndStrings(source);
        var methods = new List<string>();
        foreach (Match match in Regex.Matches(stripped, MethodPattern, RegexOptions.CultureInvariant))
        {
            var type = match.Groups["type"].Value;
            if (type is "class" or "struct" or "interface" or "enum" or "record" or "delegate" or "event" or "namespace")
                continue;
            if (!AttributesContainTest(stripped, match.Index))
                continue;
            var name = match.Groups["name"].Value;
            if (!methods.Contains(name))
                methods.Add(name);
        }

        return methods;
    }

    private static bool AttributesContainTest(string source, int methodAt)
    {
        var i = methodAt - 1;
        var found = false;
        while (i >= 0 && char.IsWhiteSpace(source[i]))
            i--;
        while (i >= 0 && source[i] == ']')
        {
            var close = i;
            var depth = 0;
            var open = -1;
            for (var j = i; j >= 0; j--)
            {
                if (source[j] == ']')
                    depth++;
                else if (source[j] == '[')
                {
                    depth--;
                    if (depth == 0)
                    {
                        open = j;
                        break;
                    }
                }
            }

            if (open < 0)
                return false;
            if (BodyNamesTest(source.Substring(open + 1, close - open - 1)))
                found = true;
            i = open - 1;
            while (i >= 0 && char.IsWhiteSpace(source[i]))
                i--;
        }

        return found;
    }

    private static bool BodyNamesTest(string body)
    {
        foreach (var segment in SplitAttributeList(body))
        {
            if (IsTestAttribute(segment))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> SplitAttributeList(string body)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            switch (body[i])
            {
                case '(':
                case '[':
                case '<':
                case '{':
                    depth++;
                    break;
                case ')':
                case ']':
                case '>':
                case '}':
                    if (depth > 0)
                        depth--;
                    break;
                case ',' when depth == 0:
                    yield return body[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return body[start..];
    }

    private static bool IsTestAttribute(string segment)
    {
        var i = 0;
        SkipSpace(segment, ref i);
        if (!TryReadIdentifier(segment, ref i, out var name))
            return false;
        if (i < segment.Length && segment[i] == ':' && (i + 1 >= segment.Length || segment[i + 1] != ':'))
        {
            i++;
            SkipSpace(segment, ref i);
            if (!TryReadIdentifier(segment, ref i, out name))
                return false;
        }

        while (true)
        {
            if (i + 1 < segment.Length && segment[i] == ':' && segment[i + 1] == ':')
            {
                i += 2;
                if (!TryReadIdentifier(segment, ref i, out name))
                    return false;
                continue;
            }

            if (i < segment.Length && segment[i] == '.')
            {
                i++;
                if (!TryReadIdentifier(segment, ref i, out name))
                    return false;
                continue;
            }

            break;
        }

        return name is "Test" or "TestAttribute";
    }

    private static void SkipSpace(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
    }

    private static bool TryReadIdentifier(string text, ref int i, out string name)
    {
        name = "";
        if (i < text.Length && text[i] == '@')
            i++;
        var start = i;
        if (i >= text.Length || !IsIdentStart(text[i]))
            return false;
        i++;
        while (i < text.Length && IsIdentPart(text[i]))
            i++;
        name = text[start..i];
        return true;
    }

    private static bool IsIdentStart(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';

    private static bool IsIdentPart(char c) => IsIdentStart(c) || c is >= '0' and <= '9';

    private static string StripCommentsAndStrings(string source)
    {
        var chars = source.ToCharArray();
        var n = chars.Length;
        var i = 0;
        while (i < n)
        {
            if (i + 1 < n && chars[i] == '/' && chars[i + 1] == '/')
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i += 2;
                while (i < n && chars[i] != '\n')
                {
                    chars[i] = ' ';
                    i++;
                }

                continue;
            }

            if (i + 1 < n && chars[i] == '/' && chars[i + 1] == '*')
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i += 2;
                while (i < n)
                {
                    if (chars[i] == '*' && i + 1 < n && chars[i + 1] == '/')
                    {
                        chars[i] = ' ';
                        chars[i + 1] = ' ';
                        i += 2;
                        break;
                    }

                    if (chars[i] != '\n')
                        chars[i] = ' ';
                    i++;
                }

                continue;
            }

            if (TryBlankString(chars, ref i))
                continue;
            i++;
        }

        return new string(chars);
    }

    private static bool TryBlankString(char[] chars, ref int i)
    {
        var n = chars.Length;
        var start = i;
        var verbatim = false;
        if (chars[i] == '@' && i + 1 < n && chars[i + 1] == '$' && i + 2 < n && chars[i + 2] == '"')
        {
            verbatim = true;
            chars[i] = ' ';
            chars[i + 1] = ' ';
            i += 2;
        }
        else if (chars[i] == '$' && i + 1 < n && chars[i + 1] == '@' && i + 2 < n && chars[i + 2] == '"')
        {
            verbatim = true;
            chars[i] = ' ';
            chars[i + 1] = ' ';
            i += 2;
        }
        else if (chars[i] == '@' && i + 1 < n && chars[i + 1] == '"')
        {
            verbatim = true;
            chars[i] = ' ';
            i++;
        }
        else if (chars[i] == '$' && i + 1 < n && chars[i + 1] == '"')
        {
            chars[i] = ' ';
            i++;
        }

        if (i >= n || chars[i] != '"')
        {
            i = start;
            return false;
        }

        var quotes = 0;
        while (i + quotes < n && chars[i + quotes] == '"')
            quotes++;
        if (quotes >= 3)
        {
            for (var k = start; k < i + quotes; k++)
            {
                if (chars[k] != '\n')
                    chars[k] = ' ';
            }

            i += quotes;
            while (i < n)
            {
                if (chars[i] == '"')
                {
                    var run = 0;
                    while (i + run < n && chars[i + run] == '"')
                        run++;
                    if (run >= quotes)
                    {
                        for (var k = 0; k < run; k++)
                            chars[i + k] = ' ';
                        i += run;
                        return true;
                    }
                }

                if (chars[i] != '\n')
                    chars[i] = ' ';
                i++;
            }

            return true;
        }

        chars[i] = ' ';
        i++;
        if (verbatim)
        {
            while (i < n)
            {
                if (chars[i] == '"')
                {
                    if (i + 1 < n && chars[i + 1] == '"')
                    {
                        chars[i] = ' ';
                        chars[i + 1] = ' ';
                        i += 2;
                        continue;
                    }

                    chars[i] = ' ';
                    i++;
                    return true;
                }

                if (chars[i] != '\n')
                    chars[i] = ' ';
                i++;
            }

            return true;
        }

        while (i < n)
        {
            if (chars[i] == '\\')
            {
                chars[i] = ' ';
                i++;
                if (i < n)
                {
                    if (chars[i] != '\n')
                        chars[i] = ' ';
                    i++;
                }

                continue;
            }

            if (chars[i] == '"')
            {
                chars[i] = ' ';
                i++;
                return true;
            }

            if (chars[i] != '\n')
                chars[i] = ' ';
            i++;
        }

        return true;
    }

    private static string Section(string markdown, string heading)
    {
        var marker = "\n### " + heading + "\n";
        var start = markdown.StartsWith("### " + heading + "\n", StringComparison.Ordinal)
            ? 0
            : markdown.IndexOf(marker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, marker.Trim());
        if (start > 0)
            start++;
        var next = markdown.IndexOf("\n##", start + marker.Trim().Length, StringComparison.Ordinal);
        return next < 0 ? markdown[start..] : markdown[start..next];
    }

    private static List<Row> CheckpointRows(string section)
    {
        List<string>? header = null;
        var rows = new List<Row>();
        foreach (var line in section.Split('\n'))
        {
            if (!line.TrimStart().StartsWith('|'))
                continue;
            var cells = SplitRow(line);
            if (cells.All(cell => cell.Length == 0 || cell.Trim('-', ':', ' ').Length == 0))
                continue;
            if (header is null)
            {
                header = cells;
                header.Count.ShouldBeGreaterThanOrEqualTo(9);
                header[4].ShouldBe("Filter");
                header[5].ShouldBe("Covers");
                header[7].ShouldBe("Min");
                header[8].ShouldBe("EstimatedMinutes");
                continue;
            }

            cells.Count.ShouldBe(header.Count, cells.FirstOrDefault());
            rows.Add(new Row(
                cells[0],
                StripBackticks(cells[2]),
                StripBackticks(cells[4]),
                cells[5],
                cells[7],
                cells[8]));
        }

        header.ShouldNotBeNull();
        return rows;
    }

    private static List<string> SplitRow(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] == '|')
            {
                current.Append('|');
                i++;
                continue;
            }

            if (line[i] == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(line[i]);
        }

        cells.Add(current.ToString().Trim());
        if (cells.Count > 0 && cells[0].Length == 0)
            cells.RemoveAt(0);
        if (cells.Count > 0 && cells[^1].Length == 0)
            cells.RemoveAt(cells.Count - 1);
        return cells;
    }

    private static string StripBackticks(string cell)
    {
        var text = cell.Trim();
        if (text.Length >= 2 && text.StartsWith('`') && text.EndsWith('`'))
            text = text[1..^1].Trim();
        return text;
    }

    private sealed record Row(
        string Id,
        string Build,
        string Filter,
        string Covers,
        string Min,
        string EstimatedMinutes);
}
