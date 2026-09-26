using System.Xml.Linq;

namespace Antiphon.Checkpoints;

public sealed class TrxFailure
{
    public string Name { get; init; } = "";
    public string Outcome { get; init; } = "";
    public string Message { get; init; } = "";
    public string StackTrace { get; init; } = "";
    public string StdOut { get; init; } = "";
    public double DurationSeconds { get; init; }
}

public sealed class SlowClass
{
    public string ClassName { get; init; } = "";
    public double Seconds { get; init; }
    public int Tests { get; init; }
}

public sealed class TrxParseResult
{
    public bool Ok { get; init; }
    public int ExitCode { get; init; }
    public string? Error { get; init; }
    public int Executed { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public List<string> ExecutedNames { get; init; } = [];
    public List<string> SkippedNames { get; init; } = [];
    public List<string> FailureNames { get; init; } = [];
    public List<TrxFailure> Failures { get; init; } = [];
    public List<SlowClass> SlowClasses { get; init; } = [];
}

public static class TrxReport
{
    private static readonly HashSet<string> FailureOutcomes = new(StringComparer.Ordinal)
    {
        "Failed", "Error", "Timeout", "Aborted",
    };

    public static TrxParseResult Parse(string path)
    {
        if (!File.Exists(path))
            return Bad("no TRX at " + path);

        XDocument doc;
        try
        {
            doc = XDocument.Load(path);
        }
        catch (Exception ex)
        {
            return Bad("malformed TRX: " + ex.Message);
        }

        if (doc.Root is null)
            return Bad("malformed TRX: empty document");

        var ns = doc.Root.Name.Namespace;
        var definitions = new Dictionary<string, (string ClassName, string Method)>(StringComparer.Ordinal);
        foreach (var unit in doc.Descendants(ns + "UnitTest"))
        {
            var id = (string?)unit.Attribute("id");
            var method = unit.Element(ns + "TestMethod");
            if (string.IsNullOrEmpty(id) || method is null)
                continue;
            definitions[id] = ((string?)method.Attribute("className") ?? "", (string?)method.Attribute("name") ?? "");
        }

        var executedNames = new List<string>();
        var skippedNames = new List<string>();
        var failureNames = new List<string>();
        var failures = new List<TrxFailure>();
        var classSeconds = new Dictionary<string, (double Seconds, int Tests)>(StringComparer.Ordinal);

        foreach (var result in doc.Descendants(ns + "UnitTestResult"))
        {
            var outcome = (string?)result.Attribute("outcome") ?? "";
            var id = (string?)result.Attribute("testId");
            string name;
            string className;
            if (!string.IsNullOrEmpty(id) && definitions.TryGetValue(id, out var def) && def.Method.Length > 0)
            {
                className = def.ClassName;
                name = def.ClassName + "." + def.Method;
            }
            else
            {
                name = (string?)result.Attribute("testName") ?? "";
                className = "";
            }

            if (outcome.Equals("NotExecuted", StringComparison.Ordinal))
            {
                if (name.Length > 0)
                    skippedNames.Add(name);
                continue;
            }

            var seconds = ParseDuration((string?)result.Attribute("duration"));
            if (className.Length > 0)
            {
                classSeconds.TryGetValue(className, out var acc);
                classSeconds[className] = (acc.Seconds + seconds, acc.Tests + 1);
            }

            executedNames.Add(name);
            if (!FailureOutcomes.Contains(outcome))
                continue;

            failureNames.Add(name);
            var output = result.Element(ns + "Output");
            var error = output?.Element(ns + "ErrorInfo");
            failures.Add(new TrxFailure
            {
                Name = name,
                Outcome = outcome,
                Message = error?.Element(ns + "Message")?.Value ?? "",
                StackTrace = error?.Element(ns + "StackTrace")?.Value ?? "",
                StdOut = output?.Element(ns + "StdOut")?.Value ?? "",
                DurationSeconds = seconds,
            });
        }

        var counters = doc.Descendants(ns + "Counters").FirstOrDefault();
        var executed = Counter(counters, "executed", executedNames.Count);
        var failed = Counter(counters, "failed", failureNames.Count);
        var total = Counter(counters, "total", executed);
        var passed = Counter(counters, "passed", Math.Max(0, executed - failed));
        var skipped = total - executed;
        if (skipped < 0)
            skipped = 0;

        return new TrxParseResult
        {
            Ok = true,
            ExitCode = ExitCodes.Green,
            Executed = executed,
            Passed = passed,
            Failed = failed,
            Skipped = skipped,
            ExecutedNames = executedNames,
            SkippedNames = skippedNames,
            FailureNames = failureNames,
            Failures = failures,
            SlowClasses = classSeconds
                .Select(pair => new SlowClass { ClassName = pair.Key, Seconds = pair.Value.Seconds, Tests = pair.Value.Tests })
                .OrderByDescending(c => c.Seconds)
                .ToList(),
        };
    }

    private static TrxParseResult Bad(string message) =>
        new() { Ok = false, ExitCode = ExitCodes.Invalid, Error = message };

    private static int Counter(XElement? counters, string name, int fallback)
    {
        var raw = (string?)counters?.Attribute(name);
        return int.TryParse(raw, out var value) ? value : fallback;
    }

    private static double ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return TimeSpan.TryParse(text, out var span) ? span.TotalSeconds : 0;
    }
}
