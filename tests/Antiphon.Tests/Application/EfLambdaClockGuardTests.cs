using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1913. An instance clock call inside an EF query lambda is inlined as a literal and
/// recompiles the query. Static <see cref="DateTime.UtcNow"/> and <see cref="DateTimeOffset.UtcNow"/>
/// are Npgsql intrinsics translated to PostgreSQL <c>now()</c> and stay inline.
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
            await _db.Rows.Where(d => d.LeaseUntil > clock.GetUtcNow().UtcDateTime).ToListAsync(ct);
            """;
        var hits = EfLambdaClockScanner.FindIn(sample, "sample.cs");
        hits.Count.ShouldBe(3);
        hits.ShouldContain("sample.cs:1");
        hits.ShouldContain("sample.cs:2");
        hits.ShouldContain("sample.cs:3");
    }

    [Test]
    public void Static_provider_clocks_stay_inline()
    {
        const string sample = """
            await _db.Rows.Where(d => d.LeaseUntil > DateTime.UtcNow).ToListAsync(ct);
            await _db.Rows.Where(d => d.LeaseUntil > DateTimeOffset.UtcNow).ToListAsync(ct);
            """;
        EfLambdaClockScanner.FindIn(sample, "sample.cs").ShouldBeEmpty();
    }

    [Test]
    public void Whitespace_before_an_instance_clock_call_is_a_hit()
    {
        const string sample = """
            await _db.Rows.Where(x => x.At < UtcNow ()).ToListAsync();
            """;
        var hits = EfLambdaClockScanner.FindIn(sample, "sample.cs");
        hits.ShouldBe(["sample.cs:1"]);
    }

    [Test]
    public void Query_finished_with_to_array_async_is_a_hit()
    {
        const string sample = """
            await query.Where(x => x.At < UtcNow()).ToArrayAsync();
            """;
        var hits = EfLambdaClockScanner.FindIn(sample, "sample.cs");
        hits.ShouldBe(["sample.cs:1"]);
    }

    [Test]
    public void In_memory_where_beside_a_database_count_is_not_a_hit()
    {
        const string sample = """
            return cached.Where(x => x.At < UtcNow()).Count() > await db.Rows.CountAsync();
            """;
        EfLambdaClockScanner.FindIn(sample, "sample.cs").ShouldBeEmpty();
    }

    [Test]
    public void Outbound_conversion_lease_keeps_database_now()
    {
        var source = File.ReadAllText(Path.Combine(ServerRoot(),
            "Application", "Services", "OutboundConversionTaskRunner.cs"));
        source.ShouldContain("d.LeaseUntil > DateTime.UtcNow");
        source.ShouldNotContain("= DateTime.UtcNow");
    }

    [Test]
    public void Bulk_optional_work_expiry_keeps_a_per_row_token()
    {
        var source = File.ReadAllText(Path.Combine(ServerRoot(),
            "Application", "Services", "AgentTaskDispatcher.cs"));
        const string marker = "Optional work expired before execution.";
        var bulk = Slice(source, marker, 0, 80, 160);
        bulk.ShouldContain(".SetProperty(t => t.ConcurrencyToken, Guid.NewGuid())");
        var single = Slice(source, marker, 2, 400, 160);
        single.ShouldContain("t.Id == claimed.Id");
        single.ShouldContain(".SetProperty(t => t.ConcurrencyToken, expiryToken)");
    }

    [Test]
    public void Pinned_provider_translates_static_clocks_and_parameters_differently()
    {
        using var db = new ClockProbeContext();

        var inline = db.Rows
            .Where(row => row.LeaseUntil > DateTime.UtcNow)
            .Select(row => new { row.Id, Token = Guid.NewGuid() })
            .ToQueryString();
        inline.ShouldContain("now()");
        inline.ShouldContain("gen_random_uuid()");

        var offset = db.Rows
            .Where(row => row.OffsetUntil > DateTimeOffset.UtcNow)
            .Select(row => row.Id)
            .ToQueryString();
        offset.ShouldContain("now()");

        var now = DateTime.UtcNow;
        var token = Guid.NewGuid();
        var hoisted = db.Rows
            .Where(row => row.LeaseUntil > now)
            .Select(row => new { row.Id, Token = token })
            .ToQueryString();
        hoisted.ShouldContain("@__now_0");
        hoisted.ShouldContain("@__token_1");
        hoisted.ShouldNotContain("now()");
        hoisted.ShouldNotContain("gen_random_uuid()");
    }

    private static string Slice(string source, string marker, int occurrence, int before, int after)
    {
        var at = -1;
        for (var n = 0; n <= occurrence; n++)
        {
            at = source.IndexOf(marker, at + 1, StringComparison.Ordinal);
            if (at < 0)
                throw new InvalidOperationException("missing marker occurrence " + occurrence.ToString());
        }

        var start = Math.Max(0, at - before);
        var end = Math.Min(source.Length, at + marker.Length + after);
        return source.Substring(start, end - start);
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
        "ExecuteUpdateAsync", "ExecuteUpdate", "ToListAsync", "ToArrayAsync", "AnyAsync", "CountAsync",
        "LongCountAsync", "FirstOrDefaultAsync", "SingleOrDefaultAsync", "FirstAsync", "SingleAsync",
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
            @"(?<![A-Za-z0-9_])(?:GetUtcNow|UtcNow)\s*\(\s*\)"))
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

    private static bool WhereIsQuery(string text, Frame frame) =>
        ChainHasTerminal(text, frame.Close) || ReceiverIsDb(text, frame.Open);

    // The terminal has to belong to this Where's call chain. A later database call in the
    // same statement, after a comparison, is a different expression.
    private static bool ChainHasTerminal(string text, int closeParen)
    {
        var i = closeParen + 1;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= text.Length || text[i] != '.')
                return false;
            i++;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            var start = i;
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                i++;
            if (i == start)
                return false;
            var name = text.Substring(start, i - start);
            if (Terminals.Contains(name, StringComparer.Ordinal))
                return true;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= text.Length || text[i] != '(')
                continue;
            var depth = 0;
            for (; i < text.Length; i++)
            {
                if (text[i] == '(')
                    depth++;
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        i++;
                        break;
                    }
                }
            }
        }

        return false;
    }

    private static bool ReceiverIsDb(string text, int whereParen)
    {
        var i = whereParen - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
            i--;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
            i--;
        var end = i;
        while (i >= 0)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i--;
                continue;
            }

            var c = text[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
            {
                i--;
                continue;
            }

            if (c is ')' or ']')
            {
                var open = c == ')' ? '(' : '[';
                var depth = 1;
                i--;
                while (i >= 0 && depth > 0)
                {
                    if (text[i] == c)
                        depth++;
                    else if (text[i] == open)
                        depth--;
                    i--;
                }

                continue;
            }

            break;
        }

        var receiver = text.Substring(i + 1, Math.Max(0, end - i));
        if (receiver.Contains("_db", StringComparison.Ordinal))
            return true;
        return System.Text.RegularExpressions.Regex.IsMatch(
            receiver, @"(?<![A-Za-z0-9_])db(?![A-Za-z0-9_])");
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

internal sealed class ClockProbeContext : DbContext
{
    public DbSet<ClockProbeRow> Rows => Set<ClockProbeRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(
            "Host=unreachable;Database=diagnostic;Username=diagnostic",
            npgsql => npgsql.SetPostgresVersion(16, 0));

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ClockProbeRow>().HasKey(row => row.Id);
}

internal sealed class ClockProbeRow
{
    public Guid Id { get; set; }
    public DateTime LeaseUntil { get; set; }
    public DateTimeOffset OffsetUntil { get; set; }
}
