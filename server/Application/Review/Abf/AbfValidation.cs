namespace Antiphon.Server.Application.Review.Abf;

/// <summary>
/// Schema-generic validation (spec 3.4, 3.5, 5.1, 7.0): repeated fields, records and ids, required
/// records and fields, and value conversion for fields and row columns. The typed values land in
/// <see cref="AbfRecord.Typed"/> and <see cref="AbfRow.TypedColumns"/>.
/// </summary>
internal static class AbfValidation
{
    public static void Validate(AbfDocument document, AbfDocumentSchema schema)
    {
        var diagnostics = document.Diagnostics;

        foreach (var recordSchema in schema.Records)
        {
            var records = document.Records.Where(r => ReferenceEquals(r.Schema, recordSchema)).ToList();
            if (records.Count < recordSchema.MinCount && records.Count == 0)
                diagnostics.Add(new(document.HeaderLine, AbfCodes.MissingRecord, AbfSeverity.Error, recordSchema.Type));
            if (recordSchema.Single)
            {
                foreach (var repeat in records.Skip(1))
                    diagnostics.Add(new(repeat.Line, AbfCodes.DuplicateRecord, AbfSeverity.Warning, recordSchema.Type));
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (record.Id is null)
                    continue;
                if (!seenIds.Add(AbfNames.Fold(record.Id)))
                {
                    diagnostics.Add(new(record.Line, AbfCodes.DuplicateId,
                        recordSchema.DuplicateIdIsError ? AbfSeverity.Error : AbfSeverity.Warning, record.Id));
                }
            }
        }

        foreach (var record in document.Records)
        {
            if (record.Schema is { } recordSchema)
                ValidateRecord(record, recordSchema, diagnostics);
        }
    }

    private static void ValidateRecord(AbfRecord record, AbfRecordSchema schema, List<AbfDiagnostic> diagnostics)
    {
        foreach (var fieldSchema in schema.Fields)
        {
            var field = record.Fields.FirstOrDefault(f => f.Name == fieldSchema.Name);
            var present = field is not null && field.Values.Any(v => v.Value.Trim().Length > 0);
            if (!present)
            {
                if (fieldSchema.Required)
                    diagnostics.Add(new(record.Line, AbfCodes.MissingRequired, AbfSeverity.Error, fieldSchema.Name));
                continue;
            }

            if (!fieldSchema.AllowsRepeat)
            {
                foreach (var repeat in field!.Values.Skip(1))
                    diagnostics.Add(new(repeat.Line, AbfCodes.DuplicateField, AbfSeverity.Warning, fieldSchema.Name));
            }

            if (fieldSchema.Type == AbfValueType.List)
            {
                record.Typed[fieldSchema.Name] = field!.Values.SelectMany(v => AbfValues.SplitList(v.Value)).ToList();
                continue;
            }

            var last = field!.Last;
            var outcome = AbfValues.Convert(fieldSchema.Type, fieldSchema.Members, last.Value, out var value);
            var failure = fieldSchema.Required ? AbfSeverity.Error : AbfSeverity.Warning;
            switch (outcome)
            {
                case AbfValues.Outcome.Ok:
                    record.Typed[fieldSchema.Name] = value;
                    break;
                case AbfValues.Outcome.Repaired:
                    diagnostics.Add(new(last.Line, AbfCodes.ValueRepaired, AbfSeverity.Warning, fieldSchema.Name));
                    record.Typed[fieldSchema.Name] = value;
                    break;
                case AbfValues.Outcome.ShortSha:
                    diagnostics.Add(new(last.Line, AbfCodes.ShortSha, AbfSeverity.Warning, fieldSchema.Name));
                    record.Typed[fieldSchema.Name] = value;
                    break;
                case AbfValues.Outcome.BadEnum:
                    diagnostics.Add(new(last.Line, AbfCodes.BadEnum, failure, fieldSchema.Name));
                    break;
                default:
                    diagnostics.Add(new(last.Line, AbfCodes.BadValue, failure, fieldSchema.Name));
                    break;
            }
        }

        if (!schema.DeclaresColumns)
            return;

        var columns = schema.Columns!;
        foreach (var row in record.Rows)
        {
            if (row.Columns is null)
                continue;
            var typed = new object?[columns.Count];
            for (var c = 0; c < columns.Count; c++)
            {
                if (c >= row.Columns.Count)
                    break;
                var column = columns[c];
                var outcome = AbfValues.Convert(column.Type, column.Members, row.Columns[c], out var value);
                switch (outcome)
                {
                    case AbfValues.Outcome.Ok:
                        typed[c] = value;
                        break;
                    case AbfValues.Outcome.Repaired:
                        diagnostics.Add(new(row.Line, AbfCodes.ValueRepaired, AbfSeverity.Warning, column.Name));
                        typed[c] = value;
                        break;
                    case AbfValues.Outcome.ShortSha:
                        diagnostics.Add(new(row.Line, AbfCodes.ShortSha, AbfSeverity.Warning, column.Name));
                        typed[c] = value;
                        break;
                    case AbfValues.Outcome.BadEnum:
                        diagnostics.Add(new(row.Line, AbfCodes.BadEnum, AbfSeverity.Warning, column.Name));
                        row.Valid = false;
                        break;
                    default:
                        diagnostics.Add(new(row.Line, AbfCodes.BadValue, AbfSeverity.Warning, column.Name));
                        row.Valid = false;
                        break;
                }
            }

            row.TypedColumns = typed;
        }
    }
}
