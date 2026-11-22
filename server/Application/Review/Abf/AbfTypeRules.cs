namespace Antiphon.Server.Application.Review.Abf;

/// <summary>Per-type validation and typed projections (spec 7.1 to 7.6).</summary>
internal static class AbfTypeRules
{
    public static void Validate(AbfDocument document, AbfParseOptions options)
    {
        switch (document.Type)
        {
            case "review-findings":
                ReviewFindings(document, options.AssignedChecks);
                break;
            case "decision-request":
                DecisionRequest(document);
                break;
            case "decision-response":
                DecisionResponse(document, options.Request);
                break;
            case "completion-request":
                CompletionRequest(document);
                break;
            case "comment-triage":
                CommentTriage(document);
                break;
            case "seed-candidates":
                SeedCandidates(document);
                break;
        }
    }

    private static void ReviewFindings(AbfDocument document, IReadOnlyCollection<string>? assigned)
    {
        var diagnostics = document.Diagnostics;
        var findings = new Dictionary<string, AbfFinding>(StringComparer.Ordinal);
        foreach (var record in document.RecordsOf("finding"))
        {
            if (record.Id is null)
                continue;
            var finding = ToFinding(record);
            if (finding is not null)
                findings[AbfNames.Fold(record.Id)] = finding;
            else
                findings.Remove(AbfNames.Fold(record.Id));
        }

        var findingIds = document.RecordsOf("finding").Where(r => r.Id is not null)
            .Select(r => AbfNames.Fold(r.Id!)).ToHashSet(StringComparer.Ordinal);
        var checkRecords = document.RecordsOf("checks").ToList();
        var checks = new Dictionary<string, AbfCheckStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in checkRecords.SelectMany(r => r.Rows).Where(r => r.Valid && r.Columns is not null))
        {
            var checkId = row.Columns![0];
            var status = (string)row.TypedColumns[1]!;
            var detail = row.Columns.Count > 2 ? row.Columns[2] : null;
            var ids = status == "finding" && detail is not null ? AbfValues.SplitList(detail) : [];
            if (checks.ContainsKey(checkId))
                diagnostics.Add(new(row.Line, AbfCodes.DuplicateCheck, AbfSeverity.Warning, checkId));
            checks[checkId] = new AbfCheckStatus(checkId, status, detail, ids, row.Line);
            foreach (var id in ids.Where(id => !findingIds.Contains(AbfNames.Fold(id))))
                diagnostics.Add(new(row.Line, AbfCodes.DanglingFinding, AbfSeverity.Warning, id));
        }

        if (checkRecords.Count > 0)
        {
            foreach (var record in document.RecordsOf("finding"))
            {
                var check = record.TypedValue<string>("check");
                if (check is null || check.Equals("none", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!checks.TryGetValue(check, out var status) || status.Status != "finding")
                    diagnostics.Add(new(record.Line, AbfCodes.CheckStatusMismatch, AbfSeverity.Warning, check));
            }
        }

        if (assigned is not null)
        {
            var assignedSet = assigned.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var checksLine = checkRecords.FirstOrDefault()?.Line ?? document.HeaderLine;
            foreach (var missing in assigned.Where(a => !checks.ContainsKey(a)))
                diagnostics.Add(new(checksLine, AbfCodes.MissingCheck, AbfSeverity.Error, missing));
            foreach (var listed in checks.Values.Where(c => !assignedSet.Contains(c.CheckId)))
                diagnostics.Add(new(listed.Line, AbfCodes.UnknownCheck, AbfSeverity.Warning, listed.CheckId));
        }

        var context = document.RecordsOf("context").SelectMany(r => r.Rows)
            .Where(r => r.Valid && r.Columns is not null)
            .Select(r => new AbfContextItem((string)r.TypedColumns[0]!, r.Columns![1], r.Columns.Count > 2 ? r.Columns[2] : null, r.Line))
            .ToList();
        var summary = document.RecordsOf("summary").Select(r => r.TypedValue<string>("body")).LastOrDefault(b => b is not null);

        document.Projection = new ReviewFindingsDocument
        {
            Findings = findings.Values.OrderBy(f => f.Line).ToList(),
            Checks = checks.Values.OrderBy(c => c.Line).ToList(),
            Context = context,
            Summary = summary,
        };
    }

    private static AbfFinding? ToFinding(AbfRecord record)
    {
        var check = record.TypedValue<string>("check");
        var severity = record.TypedValue<string>("severity");
        var file = record.TypedValue<string>("file");
        var anchor = record.TypedValue<string>("anchor");
        var body = record.TypedValue<string>("body");
        if (check is null || severity is null || file is null || anchor is null || body is null
            || !record.Typed.TryGetValue("confidence", out var confidence) || confidence is not double c)
            return null;
        return new AbfFinding(record.Id!, check, severity, c, file.Trim(), anchor.Trim(), body,
            record.TypedValue<string>("title"),
            record.TypedValue<List<string>>("flags") ?? [],
            record.Typed.TryGetValue("lines", out var lines) && lines is AbfLineRange range ? range : null,
            record.TypedValue<string>("suggestion"),
            record.Line);
    }

    private static void DecisionRequest(AbfDocument document)
    {
        var diagnostics = document.Diagnostics;
        var questions = new List<AbfQuestion>();
        foreach (var record in document.RecordsOf("question"))
        {
            var type = record.TypedValue<string>("type");
            var options = new List<AbfOption>();
            if (type == "noul")
            {
                foreach (var row in record.Rows)
                    diagnostics.Add(new(row.Line, AbfCodes.UnexpectedRow, AbfSeverity.Warning));
            }
            else if (type is "choice" or "score")
            {
                if (record.Rows.Count == 0)
                    diagnostics.Add(new(record.Line, AbfCodes.MissingOptions, AbfSeverity.Error, record.Id));
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in record.Rows.Where(r => r.Columns is not null))
                {
                    var value = row.Columns![0];
                    if (!seen.Add(value))
                    {
                        diagnostics.Add(new(row.Line, AbfCodes.DuplicateOption, AbfSeverity.Error, value));
                        continue;
                    }

                    options.Add(new AbfOption(value, row.Columns.Count > 1 ? row.Columns[1] : null));
                }
            }

            if (record.Id is not null && type is not null)
                questions.Add(new AbfQuestion(record.Id, type, record.TypedValue<string>("instructions") ?? "", options));
        }

        var request = document.RecordsOf("request").LastOrDefault();
        object? respond = null;
        request?.Typed.TryGetValue("respond-with", out respond);
        document.Projection = new DecisionRequestDocument
        {
            RequestId = request?.Id ?? "",
            RespondWith = respond is AbfTypeRef r ? r : new AbfTypeRef("decision-response", 1, 0),
            State = document.RecordsOf("state").Select(s => s.TypedValue<string>("body")).LastOrDefault(b => b is not null) ?? "",
            Questions = questions
                .GroupBy(q => AbfNames.Fold(q.Name))
                .Select(g => g.Last())
                .ToList(),
        };
    }

    private static void DecisionResponse(AbfDocument document, DecisionRequestDocument? request)
    {
        var diagnostics = document.Diagnostics;
        var response = document.RecordsOf("response").LastOrDefault();
        var note = response?.TypedValue<string>("note");
        if (request is not null && response?.Id is { } responseId && responseId != request.RequestId)
        {
            diagnostics.Add(new(response.Line, AbfCodes.RequestMismatch, AbfSeverity.Error, responseId));
            document.Projection = new DecisionResponseDocument { RequestId = responseId, Note = note, Rejected = true, Answers = [] };
            return;
        }

        var answers = new Dictionary<string, AbfRecord>(StringComparer.Ordinal);
        foreach (var record in document.RecordsOf("answer"))
        {
            if (record.Id is not null)
                answers[AbfNames.Fold(record.Id)] = record;
        }

        var typed = new List<AbfDecisionAnswer>();
        if (request is not null)
        {
            foreach (var question in request.Questions)
            {
                if (!answers.TryGetValue(AbfNames.Fold(question.Name), out var record))
                {
                    diagnostics.Add(new(document.HeaderLine, AbfCodes.MissingAnswer, AbfSeverity.Warning, question.Name));
                    typed.Add(new AbfDecisionAnswer(question.Name, question.Type, null, null, null, null, Refused: true, Reason: null));
                    continue;
                }

                typed.Add(ToAnswer(record, question, diagnostics));
            }

            foreach (var record in answers.Values.Where(a => request.Question(a.Id!) is null))
                diagnostics.Add(new(record.Line, AbfCodes.UnknownAnswer, AbfSeverity.Warning, record.Id));
        }
        else
        {
            typed.AddRange(answers.Values.Select(a => ToAnswer(a, null, diagnostics)));
        }

        document.Projection = new DecisionResponseDocument { RequestId = response?.Id, Note = note, Answers = typed };
    }

    private static AbfDecisionAnswer ToAnswer(AbfRecord record, AbfQuestion? question, List<AbfDiagnostic> diagnostics)
    {
        var name = question?.Name ?? record.Id!;
        var kind = question?.Type;
        var reason = record.TypedValue<string>("reason");
        if (record.Typed.TryGetValue("refused", out var refusedValue) && refusedValue is true)
            return new AbfDecisionAnswer(name, kind, null, null, null, null, Refused: true, reason);

        var valueField = record.Field("value");
        var confidence = record.Typed.TryGetValue("confidence", out var c) && c is double d ? d : (double?)null;
        var missing = false;
        if (valueField is null || valueField.Last.Value.Trim().Length == 0)
        {
            diagnostics.Add(new(record.Line, AbfCodes.MissingRequired, AbfSeverity.Error, "value"));
            missing = true;
        }

        if (record.Field("confidence") is null)
        {
            diagnostics.Add(new(record.Line, AbfCodes.MissingRequired, AbfSeverity.Error, "confidence"));
            missing = true;
        }

        if (missing)
            return new AbfDecisionAnswer(name, kind, null, null, confidence, null, Refused: true, reason);

        var value = valueField!.Last.Value.Trim();
        if (kind == "noul")
        {
            foreach (var row in record.Rows)
                diagnostics.Add(new(row.Line, AbfCodes.UnexpectedRow, AbfSeverity.Warning));
            var outcome = AbfValues.TryProbability(value, out var probability);
            if (outcome == AbfValues.Outcome.BadValue)
            {
                diagnostics.Add(new(valueField.Last.Line, AbfCodes.BadValue, AbfSeverity.Error, "value"));
                return new AbfDecisionAnswer(name, kind, value, null, confidence, null, Refused: true, reason);
            }

            if (outcome == AbfValues.Outcome.Repaired)
                diagnostics.Add(new(valueField.Last.Line, AbfCodes.ValueRepaired, AbfSeverity.Warning, "value"));
            return new AbfDecisionAnswer(name, kind, value, probability, confidence, null, Refused: false, reason);
        }

        var rows = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var row in record.Rows.Where(r => r.Valid && r.Columns is { Count: >= 2 }))
        {
            var option = row.Columns![0];
            if (question is not null && question.Options.All(o => o.Value != option))
            {
                diagnostics.Add(new(row.Line, AbfCodes.UnknownOption, AbfSeverity.Warning, option));
                continue;
            }

            rows[option] = (double)row.TypedColumns[1]!;
        }

        if (question is null)
            return new AbfDecisionAnswer(name, kind, value, null, confidence, rows, Refused: false, reason);

        if (question.Options.All(o => o.Value != value))
        {
            diagnostics.Add(new(valueField.Last.Line, AbfCodes.BadValue, AbfSeverity.Error, "value"));
            return new AbfDecisionAnswer(name, kind, value, null, confidence, null, Refused: true, reason);
        }

        foreach (var option in question.Options.Where(o => !rows.ContainsKey(o.Value)))
        {
            diagnostics.Add(new(record.Line, AbfCodes.MissingOption, AbfSeverity.Warning, option.Value));
            rows[option.Value] = 0;
        }

        var sum = rows.Values.Sum();
        if (sum <= 0)
        {
            diagnostics.Add(new(record.Line, AbfCodes.BadValue, AbfSeverity.Warning, name));
            return new AbfDecisionAnswer(name, kind, value, null, confidence, null, Refused: true, reason);
        }

        if (sum is < 0.99 or > 1.01)
        {
            diagnostics.Add(new(record.Line, AbfCodes.ProbabilitiesRenormalised, AbfSeverity.Warning, name));
            foreach (var key in rows.Keys.ToList())
                rows[key] /= sum;
        }

        var ordered = question.Options.Select(o => o.Value).ToDictionary(v => v, v => rows[v], StringComparer.Ordinal);
        var best = ordered.MaxBy(p => p.Value).Key;
        if (ordered[best] > ordered[value])
            diagnostics.Add(new(record.Line, AbfCodes.ValueNotMostProbable, AbfSeverity.Warning, name));
        return new AbfDecisionAnswer(name, kind, value, ordered[value], confidence, ordered, Refused: false, reason);
    }

    private static void CompletionRequest(AbfDocument document)
    {
        foreach (var record in document.RecordsOf("request"))
        {
            var expect = record.Field("expect");
            if (expect is null)
                continue;
            var text = expect.Last.Value.Trim();
            if (text.Equals("text", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!AbfTypeRef.TryParse(text, out var typeRef) || AbfSchemas.Find(typeRef.Type) is null
                || typeRef.Type is "decision-request" or "completion-request")
                document.Diagnostics.Add(new(expect.Last.Line, AbfCodes.BadValue, AbfSeverity.Error, "expect"));
        }
    }

    private static void CommentTriage(AbfDocument document)
    {
        foreach (var record in document.RecordsOf("comment"))
        {
            var valid = record.Rows.Where(r => r.Valid && r.Columns is { Count: >= 2 }).ToList();
            if (valid.Count == 0)
                document.Diagnostics.Add(new(record.Line, AbfCodes.MissingIntent, AbfSeverity.Error, record.Id));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in valid)
            {
                var intent = (string)row.TypedColumns[0]!;
                if (!seen.Add(intent))
                    document.Diagnostics.Add(new(row.Line, AbfCodes.DuplicateIntent, AbfSeverity.Warning, intent));
            }
        }
    }

    private static void SeedCandidates(AbfDocument document)
    {
        var candidates = document.RecordsOf("check-candidate").ToList();
        foreach (var record in candidates)
        {
            if (record.Rows.Count == 0)
                document.Diagnostics.Add(new(record.Line, AbfCodes.MissingProvenance, AbfSeverity.Error, record.Id));
            if (new[] { "globs", "languages", "tags", "symbols" }.All(t => record.Field(t) is null))
                document.Diagnostics.Add(new(record.Line, AbfCodes.MissingTrigger, AbfSeverity.Warning, record.Id));
        }

        var candidateIds = candidates.Where(c => c.Id is not null).Select(c => AbfNames.Fold(c.Id!)).ToHashSet(StringComparer.Ordinal);
        foreach (var record in document.RecordsOf("eval-case"))
        {
            var bug = record.TypedValue<string>("bug-commit");
            var fix = record.TypedValue<string>("fix-commit");
            if (bug is not null && bug == fix)
                document.Diagnostics.Add(new(record.Field("fix-commit")!.Last.Line, AbfCodes.BadValue, AbfSeverity.Error, "fix-commit"));
            var check = record.TypedValue<string>("check");
            if (check is not null && !candidateIds.Contains(AbfNames.Fold(check))
                && !check.StartsWith("CHK-", StringComparison.OrdinalIgnoreCase))
                document.Diagnostics.Add(new(record.Field("check")!.Last.Line, AbfCodes.UnknownCheck, AbfSeverity.Warning, check));
        }
    }
}
