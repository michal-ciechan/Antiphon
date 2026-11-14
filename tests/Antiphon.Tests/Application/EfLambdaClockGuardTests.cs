using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1913. A clock read inside an EF query lambda is a compiled-query-cache miss.
/// In-memory comparisons are outside the marker parentheses and are not hits.
/// </summary>
[Category("Unit")]
public class EfLambdaClockGuardTests
{
    [Test]
    public void Server_ef_lambdas_do_not_call_the_clock()
    {
        var hits = EfLambdaClockScanner.Find(ServerRoot());
        hits.ShouldBeEmpty("\n" + string.Join("\n", hits));
    }

    [Test]
    public void In_memory_clocks_are_not_ef_hits()
    {
        const string sample = """
            var expired = messages.Where(m => m.ExecutionDeadlineAt <= UtcNow()).ToList();
            settings.Directives.Where(d => HasEffects(settings, d, time.GetUtcNow())).ToList();
            if (delivery.DeadlineAt <= UtcNow()) { }
            if (await _db.Foo.AnyAsync(ct) && UtcNow() > deadline) { }
            private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;
            """;
        EfLambdaClockScanner.FindIn(sample, "sample.cs").ShouldBeEmpty();
    }

    [Test]
    public void Ef_query_clocks_are_hits()
    {
        const string sample = """
            await _db.AgentTasks.Where(t => t.ExecutionDeadlineAt <= UtcNow())
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CompletedAt, UtcNow()), ct);
            await _db.Rows.Where(d => d.LeaseUntil > DateTime.UtcNow)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Version, d => d.Version + 1), ct);
            """;
        var hits = EfLambdaClockScanner.FindIn(sample, "sample.cs");
        hits.Count.ShouldBe(3);
        hits.ShouldContain("sample.cs:1");
        hits.ShouldContain("sample.cs:2");
        hits.ShouldContain("sample.cs:3");
    }

    private static string ServerRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "server", "Antiphon.Server.csproj");
            if (File.Exists(candidate))
                return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("server project root");
    }
}

internal static class EfLambdaClockScanner
{
    private static readonly string[] Markers =
    [
        "ExecuteUpdateAsync", "ExecuteUpdate", "SetProperty", "ToListAsync", "AnyAsync", "CountAsync",
        "LongCountAsync", "FirstOrDefaultAsync", "SingleOrDefaultAsync", "FirstAsync", "SingleAsync", "Where",
    ];

    private static readonly string[] Terminals =
    [
        "ExecuteUpdateAsync", "ExecuteUpdate", "ToListAsync", "AnyAsync", "CountAsync", "LongCountAsync",
        "FirstOrDefaultAsync", "SingleOrDefaultAsync", "FirstAsync", "SingleAsync",
    ];

    public static IReadOnlyList<string> Find(string serverRoot)
    {
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(serverRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(serverRoot, file).Replace('\\', '/');
            if (relative.StartsWith("Migrations/", StringComparison.Ordinal)
                || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/obj/", StringComparison.Ordinal))
                continue;
            hits.AddRange(FindIn(File.ReadAllText(file), relative));
        }

        hits.Sort(StringComparer.Ordinal);
        return hits;
    }

    public static IReadOnlyList<string> FindIn(string source, string relativePath)
    {
        var text = MaskNonCode(source);
        var frames = Frames(text);
        var hits = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
            text,
            @"(?<![A-Za-z0-9_])(?:GetUtcNow\(\)|UtcNow\(\)|DateTimeOffset\.UtcNow|DateTime\.UtcNow)"))
        {
            if (IsDefinition(text, match.Index + match.Length))
                continue;
            var flagged = false;
            foreach (var frame in frames)
            {
                if (match.Index < frame.Open || match.Index >= frame.Close)
                    continue;
                if (frame.Kind == "Where")
                {
                    if (!WhereIsQuery(text, frame))
                        continue;
                }
                else if (frame.Kind is null)
                    continue;
                flagged = true;
                break;
            }

            if (!flagged)
                continue;
            var line = LineOf(source, match.Index);
            var label = relativePath + ":" + line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!hits.Contains(label))
                hits.Add(label);
        }

        return hits;
    }

    private static bool WhereIsQuery(string text, Frame frame)
    {
        var start = text.LastIndexOf(';', frame.Open);
        var end = text.IndexOf(';', frame.Close);
        if (end < 0)
            end = text.Length;
        var statement = text.Substring(start + 1, end - start - 1);
        foreach (var terminal in Terminals)
        {
            if (statement.Contains(terminal, StringComparison.Ordinal))
                return true;
        }

        return statement.Contains("_db.", StringComparison.Ordinal)
            || statement.Contains("db.", StringComparison.Ordinal);
    }

    private static List<Frame> Frames(string text)
    {
        var frames = new List<Frame>();
        var stack = new Stack<FrameStart>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
                stack.Push(new FrameStart(i, MarkerBefore(text, i)));
            else if (text[i] == ')' && stack.Count > 0)
            {
                var open = stack.Pop();
                if (open.Kind is not null)
                    frames.Add(new Frame(open.Index, i, open.Kind));
            }
        }

        return frames;
    }

    private static string? MarkerBefore(string text, int parenIndex)
    {
        var end = parenIndex - 1;
        while (end >= 0 && char.IsWhiteSpace(text[end]))
            end--;
        var start = end;
        while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] == '_'))
            start--;
        if (end < start)
            return null;
        var name = text.Substring(start + 1, end - start);
        return Markers.Contains(name, StringComparer.Ordinal) ? name : null;
    }

    private static bool IsDefinition(string text, int matchEnd)
    {
        var i = matchEnd;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        if (i < text.Length && text[i] == '{')
            return true;
        return i + 1 < text.Length && text[i] == '=' && text[i + 1] == '>';
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        var end = Math.Min(index, text.Length);
        for (var i = 0; i < end; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    private static string MaskNonCode(string text)
    {
        var chars = text.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                while (i < chars.Length && chars[i] != '\n')
                    chars[i++] = ' ';
                continue;
            }

            if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i += 2;
                while (i + 1 < chars.Length && !(chars[i] == '*' && chars[i + 1] == '/'))
                {
                    if (chars[i] != '\n')
                        chars[i] = ' ';
                    i++;
                }

                if (i < chars.Length)
                    chars[i++] = ' ';
                if (i < chars.Length)
                    chars[i++] = ' ';
                continue;
            }

            if (StartsRawString(chars, i, out var opener, out var quotes))
            {
                for (var n = 0; n < opener && i < chars.Length; n++)
                    chars[i++] = ' ';
                while (i < chars.Length && !StartsQuoteRun(chars, i, quotes))
                {
                    if (chars[i] != '\n')
                        chars[i] = ' ';
                    i++;
                }

                for (var q = 0; q < quotes && i < chars.Length; q++)
                    chars[i++] = ' ';
                continue;
            }

            if (chars[i] == '@' && i + 1 < chars.Length && chars[i + 1] == '"')
            {
                chars[i++] = ' ';
                chars[i++] = ' ';
                while (i < chars.Length)
                {
                    if (chars[i] == '"' && i + 1 < chars.Length && chars[i + 1] == '"')
                    {
                        chars[i++] = ' ';
                        chars[i++] = ' ';
                        continue;
                    }

                    var done = chars[i] == '"';
                    if (chars[i] != '\n')
                        chars[i] = ' ';
                    i++;
                    if (done)
                        break;
                }

                continue;
            }

            if (chars[i] == '"')
            {
                chars[i++] = ' ';
                while (i < chars.Length)
                {
                    if (chars[i] == '\\' && i + 1 < chars.Length)
                    {
                        chars[i++] = ' ';
                        if (chars[i] != '\n')
                            chars[i] = ' ';
                        i++;
                        continue;
                    }

                    var done = chars[i] == '"';
                    if (chars[i] != '\n')
                        chars[i] = ' ';
                    i++;
                    if (done)
                        break;
                }

                continue;
            }

            if (chars[i] == '\'')
            {
                chars[i++] = ' ';
                while (i < chars.Length)
                {
                    if (chars[i] == '\\' && i + 1 < chars.Length)
                    {
                        chars[i++] = ' ';
                        chars[i++] = ' ';
                        continue;
                    }

                    var done = chars[i] == '\'';
                    chars[i++] = ' ';
                    if (done)
                        break;
                }

                continue;
            }

            i++;
        }

        return new string(chars);
    }

    private static bool StartsRawString(char[] chars, int index, out int opener, out int quotes)
    {
        opener = 0;
        quotes = 0;
        var i = index;
        var dollars = 0;
        while (i < chars.Length && chars[i] == '$')
        {
            dollars++;
            i++;
        }

        if (dollars > 0 && index < chars.Length && chars[index] != '$')
            return false;
        if (i >= chars.Length || chars[i] != '"')
            return false;
        var run = 0;
        while (i + run < chars.Length && chars[i + run] == '"')
            run++;
        if (run < 3)
            return false;
        quotes = run;
        opener = dollars + run;
        return true;
    }

    private static bool StartsQuoteRun(char[] chars, int index, int quotes)
    {
        if (index + quotes > chars.Length)
            return false;
        for (var n = 0; n < quotes; n++)
        {
            if (chars[index + n] != '"')
                return false;
        }

        return true;
    }

    private readonly record struct FrameStart(int Index, string? Kind);
    private readonly record struct Frame(int Open, int Close, string? Kind);
}
