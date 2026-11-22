namespace Antiphon.Server.Application.Review.Abf;

/// <summary>
/// The Antiphon Block Format v1 parser (docs/specs/block-format/v1.md). Pure and stateless:
/// lexing (<see cref="AbfScanner"/>), document selection, the schema (canonical form and tolerant
/// repairs), typed validation, then the mode's severities.
/// </summary>
public static class AbfParser
{
    public static AbfDocument Parse(string? text, AbfParseOptions options)
    {
        var lines = AbfLines.Prepare(text, options.ReportEnvelope);
        var found = AbfScanner.FindHeaderDocuments(lines);
        var document = Select(lines, found, options);
        if (options.Mode == AbfMode.Strict)
            ApplyStrict(document);
        Sort(document.Diagnostics);
        return document;
    }

    /// <summary>Parses and projects a <c>review-findings/1.x</c> document (spec 7.1).</summary>
    public static AbfTyped<ReviewFindingsDocument> ParseReviewFindings(
        string? text, AbfMode mode = AbfMode.Tolerant, IReadOnlyCollection<string>? assignedChecks = null)
    {
        var document = Parse(text, new AbfParseOptions
        {
            Mode = mode,
            Expect = new AbfTypeRef("review-findings", 1, 0),
            AssignedChecks = assignedChecks,
        });
        return new(document, ReviewFindingsDocument.From(document));
    }

    /// <summary>Parses a <c>decision-request/1.x</c> document (spec 7.2).</summary>
    public static AbfTyped<DecisionRequestDocument> ParseDecisionRequest(string? text, AbfMode mode = AbfMode.Strict)
    {
        var document = Parse(text, new AbfParseOptions { Mode = mode, Expect = new AbfTypeRef("decision-request", 1, 0) });
        return new(document, DecisionRequestDocument.From(document));
    }

    /// <summary>Parses and validates a <c>decision-response/1.x</c> against its request (spec 7.3).</summary>
    public static AbfTyped<DecisionResponseDocument> ParseDecisionResponse(
        string? text, DecisionRequestDocument? request, AbfMode mode = AbfMode.Tolerant)
    {
        var document = Parse(text, new AbfParseOptions
        {
            Mode = mode,
            Expect = new AbfTypeRef("decision-response", 1, 0),
            Request = request,
        });
        return new(document, DecisionResponseDocument.From(document, request));
    }

    private static AbfDocument Select(string[] lines, List<AbfRawDocument> found, AbfParseOptions options)
    {
        if (options.Expect is not { } expect)
        {
            var any = found.LastOrDefault(d => !d.Wrapped) ?? found.LastOrDefault();
            if (any is null)
                return Missing(null, AbfCodes.MissingDocument);
            var schema = AbfSchemas.Find(any.Header!.Value.Type);
            AbfTypeRef? consumer = schema is null ? null : new AbfTypeRef(schema.Type, schema.Major, schema.Minor);
            return Build(any, consumer, options);
        }

        var matching = found.Where(d => d.Header!.Value.Type == expect.Type).ToList();
        if (matching.Count > 0)
        {
            var selected = matching.LastOrDefault(d => !d.Wrapped) ?? matching[^1];
            var document = Build(selected, expect, options);
            foreach (var other in matching.Where(d => !ReferenceEquals(d, selected)))
                document.Diagnostics.Add(new(other.HeaderLine, AbfCodes.DuplicateDocument, AbfSeverity.Warning));
            return document;
        }

        if (found.Count > 0)
        {
            var document = Missing(expect, AbfCodes.WrongType, found[^1].HeaderLine);
            return document;
        }

        var headerless = AbfScanner.FindHeaderless(lines);
        return headerless is null ? Missing(expect, AbfCodes.MissingDocument) : Build(headerless, expect, options);
    }

    private static AbfDocument Missing(AbfTypeRef? expect, string code, int line = 1)
    {
        var document = new AbfDocument(1, expect?.Type, expect is { } e ? $"{e.Major}.{e.Minor}" : null, line);
        document.Diagnostics.Add(new(line, code, AbfSeverity.Error));
        return document;
    }

    private static AbfDocument Build(AbfRawDocument raw, AbfTypeRef? consumer, AbfParseOptions options)
    {
        int? syntax = 1;
        string? type;
        string? version;
        var newerMinor = false;
        if (raw.Header is { } header)
        {
            type = header.Type;
            version = $"{header.Major}.{header.Minor}";
            syntax = int.TryParse(header.Syntax, out var s) ? s : null;
            var document = new AbfDocument(syntax, type, version, raw.HeaderLine);
            if (header.Syntax != "1")
            {
                document.Diagnostics.Add(new(raw.HeaderLine, AbfCodes.UnsupportedSyntax, AbfSeverity.Error));
                return document;
            }

            if (consumer is { } c && header.Major != c.Major)
            {
                document.Diagnostics.Add(new(raw.HeaderLine, AbfCodes.UnsupportedVersion, AbfSeverity.Error));
                return document;
            }

            newerMinor = consumer is { } k && header.Minor > k.Minor;
            Fill(document, raw, consumer is null ? null : AbfSchemas.Find(consumer.Value.Type), newerMinor, options);
            return document;
        }

        type = consumer?.Type;
        version = consumer is { } v ? $"{v.Major}.{v.Minor}" : null;
        var headerless = new AbfDocument(consumer is null ? null : syntax, type, version, raw.HeaderLine);
        Fill(headerless, raw, AbfSchemas.Find(type), newerMinor: false, options);
        return headerless;
    }

    private static void Fill(AbfDocument document, AbfRawDocument raw, AbfDocumentSchema? schema, bool newerMinor, AbfParseOptions options)
    {
        document.Diagnostics.AddRange(raw.Diagnostics);
        var unknownSeverity = newerMinor ? AbfSeverity.Info : AbfSeverity.Warning;
        var recordSpellings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawRecord in raw.Records)
        {
            var recordSchema = schema?.FindRecord(rawRecord.WrittenType);
            string recordType;
            if (recordSchema is not null)
            {
                recordType = recordSchema.Type;
                if (rawRecord.WrittenType != recordSchema.Type)
                    document.Diagnostics.Add(new(rawRecord.Line, AbfCodes.NameSpelling, AbfSeverity.Warning, recordSchema.Type));
            }
            else
            {
                var fold = AbfNames.Fold(rawRecord.WrittenType);
                if (!recordSpellings.TryGetValue(fold, out recordType!))
                    recordSpellings[fold] = recordType = rawRecord.WrittenType;
                if (schema is not null)
                    document.Diagnostics.Add(new(rawRecord.Line, AbfCodes.UnknownRecord, unknownSeverity, recordType));
            }

            var record = new AbfRecord(recordType, rawRecord.Id, rawRecord.Line, recordSchema);
            foreach (var item in rawRecord.Items)
                ApplyItem(document, record, item, unknownSeverity);
            document.Records.Add(record);
        }

        if (schema is null)
            return;

        AbfValidation.Validate(document, schema);
        AbfTypeRules.Validate(document, options);
    }

    private static void ApplyItem(AbfDocument document, AbfRecord record, AbfRawItem item, AbfSeverity unknownSeverity)
    {
        var schema = record.Schema;
        switch (item.Kind)
        {
            case AbfItemKind.Field:
            case AbfItemKind.MultiLineField:
            {
                if (schema is null)
                {
                    AddValue(record, item.Key, item.Text, item.Line);
                    return;
                }

                var field = schema.FindField(item.Key);
                if (field is not null)
                {
                    if (item.Key != field.Name)
                        document.Diagnostics.Add(new(item.Line, AbfCodes.NameSpelling, AbfSeverity.Warning, field.Name));
                    if (item.HeadingSuffix)
                        document.Diagnostics.Add(new(item.Line, AbfCodes.FieldHeadingSuffix, AbfSeverity.Warning, field.Name));
                    AddValue(record, field.Name, item.Text, item.Line);
                    return;
                }

                if (item.Kind == AbfItemKind.Field && schema.DeclaresColumns)
                {
                    document.Diagnostics.Add(new(item.Line, AbfCodes.FieldAsRow, AbfSeverity.Warning));
                    var text = item.Text.Length == 0 ? item.Key : item.Key + " " + item.Text;
                    AddRow(document, record, item.Line, text, repairs: false);
                    return;
                }

                var name = AddValue(record, item.Key, item.Text, item.Line);
                if (item.HeadingSuffix)
                    document.Diagnostics.Add(new(item.Line, AbfCodes.FieldHeadingSuffix, AbfSeverity.Warning, name));
                document.Diagnostics.Add(new(item.Line, AbfCodes.UnknownKey, unknownSeverity, name));
                return;
            }

            case AbfItemKind.Row:
            {
                if (schema is null)
                {
                    record.Rows.Add(new AbfRow(item.Line, item.Text, null));
                    return;
                }

                if (!item.Escaped && AbfLines.TryRowAsField(item.Text, out var words, out var value)
                    && schema.FindFieldByWords(words) is { } field)
                {
                    document.Diagnostics.Add(new(item.Line, AbfCodes.RowAsField, AbfSeverity.Warning, field.Name));
                    AddValue(record, field.Name, value, item.Line);
                    return;
                }

                AddRow(document, record, item.Line, item.Text, repairs: !item.Escaped);
                return;
            }
        }
    }

    private static void AddRow(AbfDocument document, AbfRecord record, int line, string text, bool repairs)
    {
        var schema = record.Schema!;
        if (!schema.DeclaresColumns)
        {
            document.Diagnostics.Add(new(line, AbfCodes.UnexpectedRow, AbfSeverity.Warning));
            record.Rows.Add(new AbfRow(line, text, null) { Valid = false });
            return;
        }

        if (repairs && AbfLines.TryRemoveBullet(text, out var rest))
        {
            document.Diagnostics.Add(new(line, AbfCodes.RowBullet, AbfSeverity.Warning));
            text = rest;
        }

        var columns = schema.Columns!;
        if (!AbfLines.TrySplitColumns(text, columns.Count, out var values))
        {
            document.Diagnostics.Add(new(line, AbfCodes.BadRow, AbfSeverity.Warning));
            return;
        }

        var required = columns.Count(c => !c.Optional);
        if (values.Count < required)
        {
            document.Diagnostics.Add(new(line, AbfCodes.MissingColumn, AbfSeverity.Warning, columns[values.Count].Name));
            return;
        }

        record.Rows.Add(new AbfRow(line, text, values));
    }

    /// <summary>Adds a value under the field whose fold matches, keeping the first spelling. Returns that spelling.</summary>
    private static string AddValue(AbfRecord record, string name, string value, int line)
    {
        var fold = AbfNames.Fold(name);
        var field = record.Fields.FirstOrDefault(f => AbfNames.Fold(f.Name) == fold);
        if (field is null)
        {
            field = new AbfField(name);
            record.Fields.Add(field);
        }

        field.Values.Add(new AbfFieldValue(value, line));
        return field.Name;
    }

    private static void ApplyStrict(AbfDocument document)
    {
        for (var i = 0; i < document.Diagnostics.Count; i++)
        {
            if (document.Diagnostics[i].Severity == AbfSeverity.Warning)
                document.Diagnostics[i] = document.Diagnostics[i] with { Severity = AbfSeverity.Error };
        }
    }

    private static void Sort(List<AbfDiagnostic> diagnostics) =>
        diagnostics.Sort(static (a, b) =>
        {
            var byLine = a.Line.CompareTo(b.Line);
            if (byLine != 0)
                return byLine;
            var byCode = string.CompareOrdinal(a.Code, b.Code);
            return byCode != 0 ? byCode : string.CompareOrdinal(a.Name, b.Name);
        });
}

/// <summary>A canonical document with its typed projection (null when the document is unusable).</summary>
public sealed record AbfTyped<T>(AbfDocument Document, T? Value) where T : class
{
    public bool HasErrors => Document.HasErrors;
}
