using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Antiphon.Checkpoints;

public sealed record StrictTrxCaseResult(string TestId, string ExecutionId, string ClassName, string Method,
    string Outcome, string? DebugTrace, string? SkipReason)
{
    public string Name => ClassName + "." + Method;
}

public sealed class StrictTrxParseResult
{
    public bool Ok { get; init; }
    public int ExitCode { get; init; } = ExitCodes.Invalid;
    public string? Error { get; init; }
    public int Total { get; init; }
    public int Executed { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public IReadOnlyList<StrictTrxCaseResult> Results { get; init; } = [];
}

/// <summary>Opt-in conditional evidence projection. Diagnostic TrxReport.Parse remains permissive.</summary>
public static class StrictTrxReport
{
    public static StrictTrxParseResult Parse(string path, int expectedTotal)
    {
        try { return ParseText(ReceiptFile.ReadStrictText(path), expectedTotal); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Bad("strict TRX unreadable"); }
    }

    public static StrictTrxParseResult ParseText(string text, int expectedTotal)
    {
        try
        {
            if (text.Length > 16 * 1024 * 1024) return Bad("strict TRX document limit");
            // The non-normalizing XmlTextReader below also admits illegal numeric
            // character references. Validate the original XML with the strict reader
            // first; discard its normalized values, retaining the original text for
            // projection. A checking wrapper around XmlTextReader does not restore
            // validation of references that reader has already expanded.
            using (var validation = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            {
                CheckCharacters = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            }))
            {
                while (validation.Read()) { }
            }
            // XmlReaderSettings normalizes literal CR/CRLF before exposing Value. The
            // conditional contract binds the literal reason, so retain those characters.
            using var reader = new XmlTextReader(new StringReader(text))
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, Normalization = false,
            };
            var doc = XDocument.Load(reader);
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            if (doc.Root?.Name != ns + "TestRun") return Bad("expected TeamTest/2010 TestRun root");
            var root = doc.Root;
            var results = One(root, ns + "Results");
            var definitions = One(root, ns + "TestDefinitions");
            var summary = One(root, ns + "ResultSummary");
            var counters = One(summary, ns + "Counters");
            var methods = new Dictionary<string, (string ClassName, string Method)>(StringComparer.Ordinal);
            foreach (var definition in definitions.Elements())
            {
                if (definition.Name != ns + "UnitTest") return Bad("unexpected definition");
                var id = Required(definition, "id");
                var method = One(definition, ns + "TestMethod");
                if (!methods.TryAdd(id, (Required(method, "className"), Required(method, "name")))) return Bad("duplicate definition ID");
            }
            var executions = new HashSet<string>(StringComparer.Ordinal);
            var cases = new List<StrictTrxCaseResult>();
            foreach (var result in results.Elements())
            {
                if (result.Name != ns + "UnitTestResult") return Bad("unexpected result");
                var id = Required(result, "testId");
                var execution = Required(result, "executionId");
                if (!executions.Add(execution)) return Bad("duplicate execution ID");
                if (!methods.TryGetValue(id, out var method)) return Bad("missing qualified definition");
                var outcome = Required(result, "outcome");
                if (outcome is not ("Passed" or "NotExecuted")) return Bad("unsupported conditional outcome");
                string? trace = null, reason = null;
                if (outcome == "NotExecuted")
                {
                    var output = One(result, ns + "Output");
                    var debug = One(output, ns + "DebugTrace");
                    if (debug.HasElements) return Bad("skip reason must be text");
                    trace = debug.Value;
                    const string prefix = "Skipped: ";
                    if (!trace.StartsWith(prefix, StringComparison.Ordinal) || trace.Length == prefix.Length) return Bad("missing result-local skip reason");
                    reason = trace[prefix.Length..];
                }
                cases.Add(new StrictTrxCaseResult(id, execution, method.ClassName, method.Method, outcome, trace, reason));
            }
            var total = Counter(counters, "total");
            var executed = Counter(counters, "executed");
            var passed = Counter(counters, "passed");
            var failed = Counter(counters, "failed");
            var skipped = Counter(counters, "notExecuted");
            var observedPassed = cases.Count(c => c.Outcome == "Passed");
            var observedSkipped = cases.Count(c => c.Outcome == "NotExecuted");
            if (expectedTotal <= 0 || total != expectedTotal || total != cases.Count
                || executed != observedPassed || passed != observedPassed || failed != 0 || skipped != observedSkipped
                || executed <= 0 || (long)executed + skipped != total)
                return Bad("conditional TRX counter/observation mismatch");
            return new StrictTrxParseResult
            {
                Ok = true, ExitCode = ExitCodes.Green, Total = total, Executed = executed, Passed = passed,
                Failed = failed, Skipped = skipped, Results = cases.AsReadOnly(),
            };
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        { return Bad("malformed strict TRX: " + ex.Message); }
    }

    private static XElement One(XElement parent, XName name)
    {
        var matches = parent.Descendants(name).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].Parent != parent) throw new InvalidDataException("expected one direct " + name.LocalName);
        return matches[0];
    }

    private static string Required(XElement element, string attribute)
    {
        var value = (string?)element.Attribute(attribute);
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException("missing " + attribute);
    }

    private static int Counter(XElement counters, string name)
    {
        var raw = (string?)counters.Attribute(name);
        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value : throw new InvalidDataException("invalid counter " + name);
    }

    private static StrictTrxParseResult Bad(string message) => new() { Error = message };
}
