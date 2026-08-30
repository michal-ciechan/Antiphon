using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0247 S3: pure classifier + run detector over already-classified rows. No I/O.
/// Ports <c>scripts/hooks/orchestrator-investigation.mjs</c> so the JS hook and this sweep
/// agree the way the three working/idle implementations do.
/// </summary>
public static class OrchestratorInvestigationDetector
{
    public const int R = 3;
    public const int NReport = 25;
    public const int NDispatch = 10;

    /// <summary>
    /// Prefix S2 injects via <c>additionalContext</c>. The stored transcript may or may not
    /// carry it (Claude does not persist hook context as its own row); the sweep treats
    /// presence as <c>nudged=yes</c> and absence as <c>nudged=no</c>.
    /// </summary>
    public const string NudgeMarker = "[antiphon-orchestrator]";

    public static readonly string[] SourceRoots =
    [
        "server/",
        "src/",
        "tests/",
        "scripts/",
        "client/src/",
        "Antiphon.AppHost/",
    ];

    private static readonly string[] ExcludedPrefixes =
    [
        "docs/",
        ".antiphon/",
        "scratchpad/",
        "memory/",
    ];

    private static readonly HashSet<string> ReadVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "cat", "grep", "rg", "head", "tail", "get-content", "select-string", "gc",
    };

    private static readonly HashSet<string> NeverReadBins = new(StringComparer.OrdinalIgnoreCase)
    {
        "git", "dotnet", "npm", "npx", "docker", "psql", "curl", "wget",
    };

    private static readonly HashSet<string> SourceRootTokens = new(StringComparer.Ordinal)
    {
        "server", "src", "tests", "scripts", "client/src", "Antiphon.AppHost",
        "./server", "./src", "./tests", "./scripts", "./client/src",
    };

    private static readonly HashSet<string> FileReadTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "Read", "Grep", "Glob",
        "read_file", "grep_search", "glob_file_search", "codebase_search", "list_dir",
    };

    private static readonly HashSet<string> ShellTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bash", "PowerShell",
        "run_terminal_command", "Shell", "shell",
    };

    private static readonly Regex CardIdRegex = new(@"CARD-\d{4,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SourcePathRegex = new(
        @"(?:server|src|tests|scripts|client/src|Antiphon\.AppHost)[/\\][A-Za-z0-9_./\\-]+\.\w+",
        RegexOptions.Compiled);
    private static readonly Regex BasenameRegex = new(
        @"\b[A-Za-z0-9_.-]+\.(?:cs|ts|tsx|js|mjs|ps1|json)\b",
        RegexOptions.Compiled);
    private static readonly Regex PascalRegex = new(
        @"\b[A-Z][a-zA-Z0-9]{5,}(?:Tests)?\b",
        RegexOptions.Compiled);
    private static readonly Regex ReportTaskRegex = new(
        @"\[task\s+[^\]]+\s+(done|blocked|failed)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReportCheckRegex = new(
        @"\[check\s+[^\]]+\s+#\d+\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DispatchFlagRegex = new(
        @"(?:^|[\s])-(?:Goal|GoalFile|Reply|Refine|Title)\b",
        RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"\""[^\""]*\""|'[^']*'|\S+", RegexOptions.Compiled);

    public enum EventKind
    {
        SourceRead,
        OtherTool,
        Dispatch,
        Report,
        Human,
    }

    public sealed record ClassifiedEvent(
        EventKind Kind,
        long Sequence,
        DateTime? Timestamp,
        string? ToolUseId,
        string? ToolName,
        IReadOnlyList<string> Identifiers,
        string? Text = null);

    public sealed record CallClassification(bool IsSourceRead, EventKind Kind);

    public sealed record InvestigationRun(
        long StartSequence,
        long EndSequence,
        int ReadCount,
        TimeSpan Duration,
        IReadOnlyList<string> Files,
        bool Nudged);

    public static CallClassification ClassifyCall(string? toolName, string? toolInput)
    {
        var name = toolName ?? "";
        var raw = FlattenToolInput(toolInput);
        if (FileReadTools.Contains(name))
        {
            var pathish = ToolPath(name, raw);
            if (IsSourcePath(pathish) || (!IsExactReadTool(name) && CommandNamesSource(pathish)))
                return new CallClassification(true, EventKind.SourceRead);
            return new CallClassification(false, EventKind.OtherTool);
        }

        if (ShellTools.Contains(name))
        {
            var command = ShellCommand(raw);
            if (IsDispatchCommand(command))
                return new CallClassification(false, EventKind.Dispatch);
            if (IsNeverReadCommand(command))
                return new CallClassification(false, EventKind.OtherTool);
            if (IsShellSourceRead(command))
                return new CallClassification(true, EventKind.SourceRead);
            return new CallClassification(false, EventKind.OtherTool);
        }

        if (string.Equals(name, "Agent", StringComparison.Ordinal))
            return new CallClassification(false, EventKind.Dispatch);

        return new CallClassification(false, EventKind.OtherTool);
    }

    public static bool IsSourcePath(string? pathish)
    {
        if (string.IsNullOrEmpty(pathish))
            return false;
        var rel = RepoRelative(pathish);
        if (string.IsNullOrEmpty(rel))
            return false;
        var lower = rel.ToLowerInvariant();
        if (lower.Contains("/scratchpad/", StringComparison.Ordinal) || lower.StartsWith("scratchpad/", StringComparison.Ordinal))
            return false;
        if (lower.Contains("/.claude/", StringComparison.Ordinal) || lower.Contains(".claude/", StringComparison.Ordinal))
            return false;
        if (lower.Contains("\\temp\\claude\\", StringComparison.Ordinal) || lower.Contains("/temp/claude/", StringComparison.Ordinal))
            return false;
        foreach (var ex in ExcludedPrefixes)
        {
            if (lower.StartsWith(ex, StringComparison.Ordinal) || lower == ex[..^1])
                return false;
        }

        foreach (var root in SourceRoots)
        {
            if (rel.StartsWith(root, StringComparison.Ordinal) || rel == root[..^1])
                return true;
            if (lower.StartsWith(root.ToLowerInvariant(), StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static IReadOnlyList<string> IdentifiersFromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in CardIdRegex.Matches(text))
            found.Add(m.Value.ToUpperInvariant());
        foreach (Match m in SourcePathRegex.Matches(text))
        {
            var norm = m.Value.Replace('\\', '/');
            found.Add(norm);
            var baseName = norm.Split('/').LastOrDefault();
            if (!string.IsNullOrEmpty(baseName))
            {
                found.Add(baseName);
                found.Add(Path.GetFileNameWithoutExtension(baseName));
            }
        }

        foreach (Match m in BasenameRegex.Matches(text))
        {
            found.Add(m.Value);
            found.Add(Path.GetFileNameWithoutExtension(m.Value));
        }

        foreach (Match m in PascalRegex.Matches(text))
            found.Add(m.Value);

        return [.. found];
    }

    public static IReadOnlyList<string> IdentifiersFromCall(string? toolName, string? toolInput)
    {
        var obj = FlattenToolInput(toolInput);
        var parts = new List<string>();
        var command = ShellCommand(obj);
        if (!string.IsNullOrEmpty(command))
            parts.Add(command);
        var pathish = ToolPath(toolName ?? "", obj);
        if (!string.IsNullOrEmpty(pathish))
            parts.Add(pathish);
        return IdentifiersFromText(string.Join(' ', parts));
    }

    public static bool IsReportText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        if (ReportTaskRegex.IsMatch(text))
            return true;
        if (ReportCheckRegex.IsMatch(text))
            return true;
        if (text.Contains("<task-notification>", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("<tool-use-id>", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static bool IsHumanPrompt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (IsReportText(text))
            return false;
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("<command-name>", StringComparison.OrdinalIgnoreCase))
            return false;
        if (trimmed.Contains("This session is being continued from a previous conversation", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public static bool ContainsNudge(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains(NudgeMarker, StringComparison.Ordinal);

    /// <summary>
    /// Find investigation runs: ≥ <see cref="R"/> consecutive source reads with no
    /// dispatch/report/human between them, none of the files named in the last report, and
    /// last report/dispatch farther than <see cref="NReport"/>/<see cref="NDispatch"/> tool calls.
    /// </summary>
    public static IReadOnlyList<InvestigationRun> FindRuns(IReadOnlyList<ClassifiedEvent> events)
    {
        if (events.Count == 0)
            return [];

        var acc = new Dictionary<long, RunAccum>();
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Kind != EventKind.SourceRead)
                continue;

            var walk = WalkBack(events, i);
            if (walk.NamedInReport)
                continue;
            if (walk.LastReport is not null && walk.LastReportToolDistance <= NReport)
                continue;
            if (walk.LastDispatch is not null && walk.LastDispatchToolDistance <= NDispatch)
                continue;
            if (walk.RunLength < R)
                continue;

            if (!acc.TryGetValue(walk.RunStartSequence, out var run))
            {
                run = new RunAccum
                {
                    StartSequence = walk.RunStartSequence,
                    StartTimestamp = events[i].Timestamp,
                };
                acc[walk.RunStartSequence] = run;
            }

            run.ReadCount = walk.RunLength;
            run.EndSequence = events[i].Sequence;
            run.EndTimestamp = events[i].Timestamp;
            CollectFiles(run.Files, events[i].Identifiers);
        }

        if (acc.Count == 0)
            return [];

        foreach (var run in acc.Values)
        {
            for (var i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev.Sequence < run.StartSequence || ev.Sequence > run.EndSequence)
                    continue;
                if (ContainsNudge(ev.Text))
                    run.Nudged = true;
                if (ev.Kind == EventKind.SourceRead)
                    CollectFiles(run.Files, ev.Identifiers);
            }
        }

        return acc.Values
            .OrderBy(r => r.StartSequence)
            .Select(r => new InvestigationRun(
                r.StartSequence,
                r.EndSequence,
                r.ReadCount,
                Duration(r.StartTimestamp, r.EndTimestamp),
                r.Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray(),
                r.Nudged))
            .ToList();
    }

    public static string FormatMessage(InvestigationRun run)
    {
        var seconds = (int)Math.Round(run.Duration.TotalSeconds);
        var files = run.Files.Count;
        return $"{run.ReadCount} reads over {seconds}s across {files} files, no dispatch; nudged={(run.Nudged ? "yes" : "no")}";
    }

    public static string RunStartKey(long startSequence) => $"runStartSeq={startSequence}";

    public static bool TryParseRunStart(string? failureReason, out long startSequence)
    {
        startSequence = 0;
        const string prefix = "runStartSeq=";
        if (string.IsNullOrEmpty(failureReason) || !failureReason.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        return long.TryParse(failureReason[prefix.Length..], out startSequence);
    }

    private static Walk WalkBack(IReadOnlyList<ClassifiedEvent> events, int idx)
    {
        var current = events[idx];
        var identifiers = current.Identifiers;
        ClassifiedEvent? lastReport = null;
        var lastReportToolDistance = int.MaxValue;
        ClassifiedEvent? lastDispatch = null;
        var lastDispatchToolDistance = int.MaxValue;
        var runLength = 1;
        var runStartSequence = current.Sequence;
        var toolsSeen = 0;
        var runOpen = true;

        for (var i = idx - 1; i >= 0; i--)
        {
            var ev = events[i];
            if (ev.Kind is EventKind.SourceRead or EventKind.OtherTool or EventKind.Dispatch)
                toolsSeen++;
            if (ev.Kind == EventKind.Report && lastReport is null)
            {
                lastReport = ev;
                lastReportToolDistance = toolsSeen;
            }

            if (ev.Kind == EventKind.Dispatch && lastDispatch is null)
            {
                lastDispatch = ev;
                lastDispatchToolDistance = toolsSeen;
            }

            if (!runOpen)
                continue;
            if (ev.Kind is EventKind.Human or EventKind.Report or EventKind.Dispatch)
            {
                runOpen = false;
                continue;
            }

            if (ev.Kind == EventKind.SourceRead)
            {
                runLength++;
                runStartSequence = ev.Sequence;
            }
        }

        return new Walk(
            runLength,
            runStartSequence,
            SetsOverlap(identifiers, lastReport?.Identifiers),
            lastReport,
            lastReportToolDistance,
            lastDispatch,
            lastDispatchToolDistance);
    }

    private static bool IsExactReadTool(string name) =>
        string.Equals(name, "Read", StringComparison.Ordinal)
        || string.Equals(name, "read_file", StringComparison.OrdinalIgnoreCase);

    private static bool IsDispatchCommand(string command)
    {
        if (string.IsNullOrEmpty(command))
            return false;
        if (command.IndexOf("delegate.ps1", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return DispatchFlagRegex.IsMatch(command);
    }

    private static bool IsNeverReadCommand(string command)
    {
        if (string.IsNullOrEmpty(command))
            return false;
        var segments = SplitShell(command);
        var sawRead = false;
        var sawNever = false;
        foreach (var seg in segments)
        {
            var head = PipelineHead(seg);
            var bin = FirstBin(head);
            if (string.IsNullOrEmpty(bin) || bin == "cd")
                continue;
            if (NeverReadBins.Contains(bin))
                sawNever = true;
            else if (IsReadVerb(bin, head) || ContainsScript(head, "delegate.ps1") || ContainsScript(head, "card.ps1"))
            {
                if (ContainsScript(head, "delegate.ps1") || ContainsScript(head, "card.ps1"))
                    sawNever = true;
                else
                    sawRead = true;
            }
        }

        if (sawRead)
            return false;
        return sawNever;
    }

    private static bool IsShellSourceRead(string command)
    {
        if (string.IsNullOrEmpty(command))
            return false;
        if ((ContainsScript(command, "delegate.ps1") || ContainsScript(command, "card.ps1"))
            && !IsReadVerbCommand(command))
            return false;
        foreach (var seg in SplitShell(command))
        {
            var head = PipelineHead(seg);
            var bin = FirstBin(head);
            if (string.IsNullOrEmpty(bin) || bin == "cd")
                continue;
            if (NeverReadBins.Contains(bin))
                continue;
            if (ContainsScript(head, "delegate.ps1") || ContainsScript(head, "card.ps1"))
                continue;
            if (!IsReadVerb(bin, head))
                continue;
            if (CommandNamesSource(head) || IsRecursiveGrepOnSourceRoot(head))
                return true;
        }

        return false;
    }

    private static bool IsReadVerbCommand(string command) =>
        SplitShell(command).Any(seg => IsReadVerb(FirstBin(PipelineHead(seg)), PipelineHead(seg)));

    private static bool IsReadVerb(string bin, string segment)
    {
        if (string.IsNullOrEmpty(bin))
            return false;
        if (bin == "sed")
            return Regex.IsMatch(segment, @"(^|\s)-n(\s|$)");
        return ReadVerbs.Contains(bin);
    }

    private static bool IsRecursiveGrepOnSourceRoot(string segment)
    {
        var bin = FirstBin(segment);
        if (bin != "grep" && bin != "rg")
            return false;
        var recursive = Regex.IsMatch(segment, @"(^|\s)(-r|-rn|-rI|-R|--recursive)(\s|$)")
            || (bin == "rg" && !Regex.IsMatch(segment, @"(^|\s)--no-ignore(\s|$)") && CommandNamesSource(segment));
        if (!recursive && bin == "grep")
            return false;
        return CommandNamesSource(segment) || HasSourceRootToken(segment);
    }

    private static bool HasSourceRootToken(string text)
    {
        foreach (var tok in Tokenize(text))
        {
            var cleaned = StripQuotes(tok).Replace('\\', '/').TrimEnd('/');
            if (SourceRootTokens.Contains(cleaned))
                return true;
            if (cleaned.StartsWith("./", StringComparison.Ordinal)
                && SourceRootTokens.Contains(cleaned[2..]))
                return true;
        }

        return false;
    }

    private static bool CommandNamesSource(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        if (HasSourceRootToken(text))
            return true;
        foreach (var tok in Tokenize(text))
        {
            var cleaned = StripQuotes(tok);
            if (string.IsNullOrEmpty(cleaned))
                continue;
            if (IsSourcePath(cleaned))
                return true;
        }

        return false;
    }

    private static List<string> SplitShell(string command) =>
        SplitOutsideQuotes(command, ["&&", "||", ";"]);

    private static string PipelineHead(string segment)
    {
        var parts = SplitOutsideQuotes(segment, ["|"]);
        return parts.Count > 0 ? parts[0].Trim() : "";
    }

    private static List<string> SplitOutsideQuotes(string text, string[] seps)
    {
        var outList = new List<string>();
        var buf = new System.Text.StringBuilder();
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quote is not null)
            {
                buf.Append(ch);
                if (ch == quote && (i == 0 || text[i - 1] != '\\'))
                    quote = null;
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                buf.Append(ch);
                continue;
            }

            var rest = text.AsSpan(i);
            var matched = false;
            foreach (var sep in seps)
            {
                if (rest.StartsWith(sep, StringComparison.Ordinal))
                {
                    outList.Add(buf.ToString());
                    buf.Clear();
                    i += sep.Length - 1;
                    matched = true;
                    break;
                }
            }

            if (matched)
                continue;
            buf.Append(ch);
        }

        if (buf.Length > 0)
            outList.Add(buf.ToString());
        return outList.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }

    private static string FirstBin(string segment)
    {
        foreach (var t in Tokenize(segment))
        {
            if (t.Contains('=') && !t.StartsWith('-'))
                continue;
            var raw = StripQuotes(t);
            var baseName = raw.Replace('\\', '/');
            var slash = baseName.LastIndexOf('/');
            if (slash >= 0)
                baseName = baseName[(slash + 1)..];
            if (baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                baseName = baseName[..^4];
            return baseName.ToLowerInvariant();
        }

        return "";
    }

    private static List<string> Tokenize(string s)
    {
        var outList = new List<string>();
        foreach (Match m in TokenRegex.Matches(s))
            outList.Add(m.Value);
        return outList;
    }

    private static string StripQuotes(string t)
    {
        if (t.Length >= 2
            && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
            return t[1..^1];
        return t;
    }

    private static JsonElement FlattenToolInput(string? toolInput)
    {
        if (string.IsNullOrWhiteSpace(toolInput))
            return default;
        try
        {
            using var doc = JsonDocument.Parse(toolInput);
            return UnwrapUnparsed(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["_raw"] = toolInput });
        }
    }

    private static JsonElement UnwrapUnparsed(JsonElement toolInput)
    {
        if (toolInput.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return default;
        if (toolInput.ValueKind == JsonValueKind.String)
            return FlattenToolInput(toolInput.GetString());
        if (toolInput.ValueKind != JsonValueKind.Object
            || !toolInput.TryGetProperty("__unparsedToolInput", out var unparsed)
            || unparsed.ValueKind != JsonValueKind.Object
            || !unparsed.TryGetProperty("raw", out var rawEl))
            return toolInput;

        var raw = rawEl.GetString() ?? "";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var cloned = doc.RootElement.Clone();
            if (cloned.ValueKind == JsonValueKind.Object)
                return cloned;
        }
        catch (JsonException)
        {
            var m = Regex.Match(raw, @"file_path[""']?\s*:\s*[""']([^""']+)[""']");
            if (m.Success)
            {
                var path = m.Groups[1].Value.Replace("\\\\", "\\");
                return JsonSerializer.SerializeToElement(new Dictionary<string, string>
                {
                    ["file_path"] = path,
                    ["_raw"] = raw,
                });
            }
        }

        return JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["_raw"] = raw });
    }

    private static string ShellCommand(JsonElement toolInput)
    {
        if (toolInput.ValueKind == JsonValueKind.Object)
        {
            if (toolInput.TryGetProperty("command", out var cmd) && cmd.ValueKind == JsonValueKind.String)
                return cmd.GetString() ?? "";
            if (toolInput.TryGetProperty("_raw", out var raw) && raw.ValueKind == JsonValueKind.String)
                return raw.GetString() ?? "";
        }

        return "";
    }

    private static string ToolPath(string toolName, JsonElement toolInput)
    {
        if (toolInput.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "file_path", "target_file", "path", "pattern", "glob" })
            {
                if (toolInput.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                    return val.GetString() ?? "";
            }

            if (toolInput.TryGetProperty("_raw", out var raw) && raw.ValueKind == JsonValueKind.String)
                return raw.GetString() ?? "";
            return toolInput.GetRawText();
        }

        return toolInput.ValueKind == JsonValueKind.String ? toolInput.GetString() ?? "" : "";
    }

    private static string RepoRelative(string p)
    {
        var n = Regex.Replace(p.Replace('\\', '/'), "/{2,}", "/");
        var lower = n.ToLowerInvariant();
        const string marker = "/antiphon/";
        var idx = lower.LastIndexOf(marker, StringComparison.Ordinal);
        var rel = idx >= 0 ? n[(idx + marker.Length)..] : n.TrimStart('.').TrimStart('/');
        return rel.TrimStart('/');
    }

    private static bool SetsOverlap(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (a is null || b is null || a.Count == 0 || b.Count == 0)
            return false;
        var set = new HashSet<string>(b.Select(x => x.ToLowerInvariant()), StringComparer.Ordinal);
        foreach (var x in a)
        {
            if (set.Contains(x.ToLowerInvariant()))
                return true;
        }

        return false;
    }

    private static bool ContainsScript(string text, string name) =>
        text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void CollectFiles(HashSet<string> files, IReadOnlyList<string> identifiers)
    {
        foreach (var id in identifiers)
        {
            if (BasenameRegex.IsMatch(id) || SourcePathRegex.IsMatch(id) || IsSourcePath(id))
            {
                var name = id.Replace('\\', '/').Split('/').Last();
                if (!string.IsNullOrEmpty(name))
                    files.Add(name);
            }
        }
    }

    private static TimeSpan Duration(DateTime? start, DateTime? end)
    {
        if (start is null || end is null)
            return TimeSpan.Zero;
        var delta = end.Value - start.Value;
        return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
    }

    private sealed class RunAccum
    {
        public long StartSequence;
        public long EndSequence;
        public int ReadCount;
        public DateTime? StartTimestamp;
        public DateTime? EndTimestamp;
        public bool Nudged;
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct Walk(
        int RunLength,
        long RunStartSequence,
        bool NamedInReport,
        ClassifiedEvent? LastReport,
        int LastReportToolDistance,
        ClassifiedEvent? LastDispatch,
        int LastDispatchToolDistance);
}
