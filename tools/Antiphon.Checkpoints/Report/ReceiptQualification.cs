using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.Checkpoints;

/// <summary>
/// The validate verb's offline receipt qualification (CARD-0835 D-5): an executable validator, never a regex match for
/// <c>commit=</c>. Since unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md
/// scripts/validate-checkpoint-receipt.ps1 is its thin launcher (the server's CheckpointReceipts:ValidatorScript names that
/// file), so this holds that script's contract as well as the tool's own:
/// <list type="bullet">
/// <item>A receipt with rows is a report.json (the tool's runs and the row verb, schema 2 or 3): <see cref="ReportValidator"/>,
/// which admits a row whose only skips are Windows-only tests recorded on a non-Windows host (<see cref="PlacementSkips"/>).</item>
/// <item>A receipt without rows is the source.json envelope scripts/run-checkpoint.ps1 wrote before unit 20 (version 1, or
/// version 2 with the repeat evidence), still qualified so an earlier receipt keeps its verdict.</item>
/// </list>
/// The answer is null when the receipt qualifies, else the refusal reason. A receipt that cannot be read or parsed throws
/// (IOException, JsonException, InvalidDataException, XmlException): the verb reports the exception's type.
/// </summary>
public static class ReceiptQualification
{
    private static readonly Regex FullObjectId = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant);
    private static readonly Regex Fingerprint = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Field = new(@"(?:^|\s)([A-Za-z][A-Za-z0-9]*)=([^\s]*)", RegexOptions.CultureInvariant);

    // The OSDescription forms the supported non-Windows hosts emit. The recorded execution host is judged, never the
    // machine qualifying the receipt.
    private static readonly Regex NonWindowsHost = new(
        "^(?:Linux|Debian GNU/Linux|Ubuntu|Alpine Linux|Fedora Linux|Rocky Linux|AlmaLinux|CentOS Linux|CentOS Stream|Red Hat Enterprise Linux|Arch Linux|openSUSE Leap|openSUSE Tumbleweed|SUSE Linux Enterprise Server|Amazon Linux|Oracle Linux Server|NixOS|Gentoo Linux|Kali GNU/Linux|Darwin)(?: |$)",
        RegexOptions.CultureInvariant);
    private static readonly Regex RosterArray = new(@"internal static readonly Entry\[\] WindowsOnly\s*=\s*\[(.*?)\];",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex RosterEntry = new(
        "^\\s*new\\(\"([A-Za-z_][A-Za-z0-9_.]*)\",\\s*\"([A-Za-z_][A-Za-z0-9_]*)\",\\s*\"([A-Za-z_][A-Za-z0-9_]*)\",\\s*([1-9][0-9]*),\\s*(?:\"(?:[^\"\\\\]|\\\\.)*\"|[A-Za-z_][A-Za-z0-9_]*)\\),",
        RegexOptions.CultureInvariant);
    private const string TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>The roster the placement skips are read from, at the receipt's exact source commit.</summary>
    public const string SkipRoster = "tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs";

    /// <summary>
    /// Qualifies one receipt's text for expectedSha. rows: the selected row ids (empty: every row). repository: the checkout
    /// whose git answers the skip roster at expectedSha.
    /// </summary>
    public static string? Validate(string raw, string expectedSha, IReadOnlyList<string> rows, int expectedRepeat,
        string repository, CancellationToken cancellationToken = default)
    {
        if (!FullObjectId.IsMatch(expectedSha)) return "expected_sha_invalid";
        if (expectedRepeat < 1) return "expected_repeat_invalid";
        using var document = JsonDocument.Parse(raw);
        try { AssertUnique(document.RootElement); }
        catch (InvalidDataException duplicate) { return duplicate.Message; }
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rows", out var listed) && listed.ValueKind != JsonValueKind.Null)
        {
            var report = ReadReport(root, raw);
            if (report.Refusal is not null) return report.Refusal;
            return ReportValidator.Validate(report.Model, expectedSha, rows, expectedRepeat,
                row => PlacementSkips(report.Model, row, expectedSha, expectedRepeat, repository, cancellationToken));
        }
        return Envelope(root, expectedSha, expectedRepeat);
    }

    // A report.json as any tool version wrote it. The tool's own receipt reader (ReceiptJson.Parse) requires every
    // property the current writer serializes, but a retained receipt keeps the shape of the writer that made it: a report
    // from before 2026-10-23 has no budget or attempts, one from before 2026-10-13 no markdownSha256, and the review
    // evidence correction imports such packs (CARD-1281's producer pack is from 2026-10-15). The PowerShell validator this
    // verb replaced read every report that way, so a missing member takes its default and only the source facts decide.
    // Still refused: a member that is present but null where the model forbids null, a blank list entry (both
    // JsonException), cleanupRefusals that is not an array, and a report that records outputPaths but leaves them empty
    // for its declared outputNames.
    private static (ReportModel Model, string? Refusal) ReadReport(JsonElement root, string raw)
    {
        var model = JsonSerializer.Deserialize<ReportModel>(raw, ReportWriter.Json) ?? throw new InvalidDataException("report_empty");
        CheckpointReceiptJson.Validate(model);
        if (!root.TryGetProperty("cleanupRefusals", out var cleanup) || cleanup.ValueKind != JsonValueKind.Array)
            return (model, "cleanup_refusals_invalid");
        if (root.TryGetProperty("outputPaths", out _) && model.OutputNames.Count > 0 && model.OutputPaths.Count == 0)
            return (model, "output_paths_missing");
        return (model, null);
    }

    /// <summary>
    /// A row's skips are admitted only when the recorded host is not Windows, one repetition was asked, and every skipped
    /// result of the row's TRX is a WindowsOnly entry of the skip roster at expectedSha (each entry at most its stated
    /// count), every other result passed, and the TRX counters and the row agree. Null when admitted, else the reason.
    /// </summary>
    public static string? PlacementSkips(ReportModel report, ReportRow row, string expectedSha, int expectedRepeat,
        string repository, CancellationToken cancellationToken = default)
    {
        if (!NonWindowsHost.IsMatch(report.Host?.Os ?? "")) return "skip_host_ineligible";
        if (expectedRepeat != 1) return "skip_repeat_ineligible";
        if (string.IsNullOrWhiteSpace(row.Trx)) return "skip_trx_missing";
        var allowed = new Dictionary<string, int>(StringComparer.Ordinal);
        string roster;
        try { roster = new UTF8Encoding(false, true).GetString(GitShow(repository, expectedSha + ":" + SkipRoster)); }
        catch (Exception ex) when (ex is IOException or DecoderFallbackException or System.ComponentModel.Win32Exception)
        { return "skip_roster_unreadable"; }
        var arrays = RosterArray.Matches(roster);
        if (arrays.Count != 1) return "skip_roster_invalid";
        var remaining = arrays[0].Groups[1].Value;
        while (!string.IsNullOrWhiteSpace(remaining))
        {
            var entry = RosterEntry.Match(remaining);
            if (!entry.Success) return "skip_roster_invalid";
            var identity = entry.Groups[1].Value + "." + entry.Groups[2].Value + "." + entry.Groups[3].Value;
            if (!allowed.TryAdd(identity, int.Parse(entry.Groups[4].Value, CultureInfo.InvariantCulture))) return "skip_roster_invalid";
            remaining = remaining[entry.Length..];
        }

        var settings = new XmlReaderSettings { CheckCharacters = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        var trx = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(new StringReader(ReceiptFile.ReadStrictText(row.Trx)), settings))
            trx.Load(reader);
        var names = new XmlNamespaceManager(trx.NameTable);
        names.AddNamespace("t", TrxNamespace);
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XmlElement unit in trx.SelectNodes("/t:TestRun/t:TestDefinitions/t:UnitTest", names)!)
        {
            var methods = unit.SelectNodes("t:TestMethod", names)!;
            var id = unit.GetAttribute("id");
            if (id.Length == 0 || methods.Count != 1 || definitions.ContainsKey(id)) return "skip_trx_identity";
            var method = (XmlElement)methods[0]!;
            definitions.Add(id, method.GetAttribute("className") + "." + method.GetAttribute("name"));
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int passed = 0, skipped = 0;
        foreach (XmlElement result in trx.SelectNodes("/t:TestRun/t:Results/t:UnitTestResult", names)!)
        {
            var id = result.GetAttribute("testId");
            if (id.Length == 0 || !seen.Add(id) || !definitions.TryGetValue(id, out var identity)) return "skip_trx_identity";
            switch (result.GetAttribute("outcome"))
            {
                case "Passed":
                    passed++;
                    break;
                case "NotExecuted":
                    if (!allowed.TryGetValue(identity, out var left) || left < 1) return "skip_identity_unlisted";
                    allowed[identity] = left - 1;
                    skipped++;
                    break;
                default:
                    return "skip_trx_outcome";
            }
        }
        if (passed != row.Passed || passed != row.Executed || skipped != row.Skipped) return "skip_trx_counts";
        var counters = trx.SelectNodes("/t:TestRun/t:ResultSummary/t:Counters", names)!;
        if (counters.Count != 1) return "skip_trx_counts";
        var executed = row.Executed ?? 0;
        var expected = new Dictionary<string, int>
        {
            ["total"] = executed + skipped, ["executed"] = executed, ["passed"] = row.Passed ?? 0, ["failed"] = 0, ["notExecuted"] = skipped,
        };
        foreach (var (key, value) in expected)
            if (((XmlElement)counters[0]!).GetAttribute(key) != value.ToString(CultureInfo.InvariantCulture)) return "skip_trx_counts";
        return null;
    }

    // The run-checkpoint.ps1 source.json envelope: version 1 (the source facts beside the receipt) or version 2 (a repeat:
    // the source facts under "source" and the repeat evidence).
    private static string? Envelope(JsonElement data, string expectedSha, int expectedRepeat)
    {
        if (data.ValueKind != JsonValueKind.Object) return "source_ineligible";
        var version = Number(data, "version");
        if (version == 2)
        {
            if (expectedRepeat == 1 || !data.TryGetProperty("source", out var source) || !SourceEligible(source, expectedSha, false))
                return "source_ineligible";
        }
        else if (expectedRepeat != 1 || !SourceEligible(data, expectedSha, false))
            return "source_ineligible";
        var executed = Number(data, "executed");
        if (Number(data, "exitCode") != 0 || Number(data, "failed") != 0 || executed is not >= 1
            || Number(data, "passed") != executed || Number(data, "skipped") != 0)
            return "receipt_failed";
        var receipt = Text(data, "receipt") ?? "";
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Field.Matches(receipt))
            if (!fields.TryAdd(match.Groups[1].Value, match.Groups[2].Value)) return "duplicate_receipt_token";
        if (!receipt.StartsWith("CHECKPOINT " + (Text(data, "name") ?? "") + " ", StringComparison.Ordinal)) return "receipt_name";
        if (Get(fields, "commit") != expectedSha || Get(fields, "source") != expectedSha || Get(fields, "dirty") != "0"
            || Get(fields, "sourceState") != "clean" || Get(fields, "buildSource") != "verified")
            return "receipt_disagreement";
        foreach (var key in new[] { "executed", "passed", "failed", "skipped" })
            if (Get(fields, key) != Scalar(data, key)) return "receipt_counts";
        if (version != 2) return null;
        if (Get(fields, "repeat") != expectedRepeat.ToString(CultureInfo.InvariantCulture)
            || Get(fields, "repetitions") != expectedRepeat + "/" + expectedRepeat || Get(fields, "hostInvocations") != "1")
            return "repeat_receipt_disagreement";
        RepeatEvidence? stored;
        try
        {
            stored = data.TryGetProperty("repeat", out var repeat) && repeat.ValueKind == JsonValueKind.Object
                ? repeat.Deserialize<RepeatEvidence>(ReportWriter.Json) : null;
        }
        catch (JsonException) { stored = null; }
        if (!RepeatSummary(stored, Number(data, "executed"), Number(data, "passed"), Number(data, "failed"), Number(data, "skipped"), expectedRepeat))
            return "repeat_summary_invalid";
        var trxPath = Get(fields, "trx");
        if (string.IsNullOrWhiteSpace(trxPath) || !File.Exists(trxPath)) return "repeat_trx_missing";
        var trx = TrxReport.Parse(trxPath);
        var actual = RepeatEvidenceValidator.Validate(trx, expectedRepeat, stored!.Nonce, 1, []);
        if (!actual.Ok || JsonSerializer.Serialize(actual.Evidence, ReportWriter.Json) != JsonSerializer.Serialize(stored, ReportWriter.Json))
            return "repeat_trx_disagreement";
        return null;
    }

    // run-checkpoint.ps1's Test-CheckpointRepeatSummary.
    private static bool RepeatSummary(RepeatEvidence? repeat, int? executed, int? passed, int? failed, int? skipped, int expected)
    {
        if (repeat is null || repeat.Requested != expected || repeat.Completed != expected || repeat.Passed != expected
            || repeat.HostInvocations != 1 || repeat.HostPid < 1 || string.IsNullOrWhiteSpace(repeat.Nonce)
            || repeat.Repetitions.Count != expected)
            return false;
        int sumExecuted = 0, sumPassed = 0, sumFailed = 0, sumSkipped = 0;
        string[]? first = null;
        for (var i = 0; i < expected; i++)
        {
            var ordinal = repeat.Repetitions[i];
            if (ordinal.Ordinal != i || ordinal.Failed != 0 || ordinal.Skipped != 0 || ordinal.Executed != ordinal.Cases.Count
                || ordinal.Passed != ordinal.Executed)
                return false;
            var keys = ordinal.Cases.Select(item => item.CaseKey).Order(StringComparer.Ordinal).ToArray();
            if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) return false;
            if (first is null) first = keys;
            else if (!first.SequenceEqual(keys, StringComparer.Ordinal)) return false;
            sumExecuted += ordinal.Executed;
            sumPassed += ordinal.Passed;
            sumFailed += ordinal.Failed;
            sumSkipped += ordinal.Skipped;
        }
        return sumExecuted == executed && sumPassed == passed && sumFailed == failed && sumSkipped == skipped;
    }

    // run-checkpoint.ps1's Test-CheckpointSourceEvidence over a source evidence object.
    private static bool SourceEligible(JsonElement evidence, string expectedSha, bool allowCommand)
    {
        if (evidence.ValueKind != JsonValueKind.Object || Number(evidence, "version") != 1
            || !evidence.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.Object
            || !evidence.TryGetProperty("end", out var end) || end.ValueKind != JsonValueKind.Object)
            return false;
        if (Text(start, "captureStatus") != "known" || Text(end, "captureStatus") != "known") return false;
        if (Text(start, "commit") != expectedSha || Text(end, "commit") != expectedSha) return false;
        if (Number(start, "dirtyFiles") != 0 || Number(end, "dirtyFiles") != 0 || Text(evidence, "state") != "clean") return false;
        var fingerprint = Text(start, "fingerprint");
        if (fingerprint is null || !Fingerprint.IsMatch(fingerprint) || fingerprint != Text(end, "fingerprint")) return false;
        var build = Text(evidence, "buildSource");
        return build == "verified" || allowCommand && build == "notApplicable";
    }

    private static byte[] GitShow(string repository, string revision)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = repository };
        foreach (var argument in new[] { "show", revision })
            start.ArgumentList.Add(argument);
        var result = CheckpointProcessRunner.RunBytes(start, 30_000, git: true);
        if (result.TimedOut || result.Exit != 0) throw new IOException("git show " + revision + " failed");
        return result.Stdout;
    }

    private static void AssertUnique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("duplicate_json_property");
                AssertUnique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) AssertUnique(item);
    }

    private static string? Get(Dictionary<string, string> fields, string key) => fields.TryGetValue(key, out var value) ? value : null;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static int? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static string? Scalar(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer.ToString(CultureInfo.InvariantCulture) : value.GetRawText(),
            JsonValueKind.String => value.GetString(),
            _ => null,
        };
    }
}
