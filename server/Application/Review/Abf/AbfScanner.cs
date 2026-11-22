namespace Antiphon.Server.Application.Review.Abf;

internal enum AbfItemKind
{
    Field,
    MultiLineField,
    Row,
}

/// <summary>One lexed line (or multi-line field) of a record, before the schema is applied.</summary>
internal sealed record AbfRawItem(AbfItemKind Kind, int Line, string Key, string Text, bool HeadingSuffix = false, bool Escaped = false);

internal sealed class AbfRawRecord(string writtenType, string? id, int line)
{
    public string WrittenType { get; } = writtenType;
    public string? Id { get; } = id;
    public int Line { get; } = line;
    public List<AbfRawItem> Items { get; } = [];
}

/// <summary>A document found in the text, with its structural records and diagnostics.</summary>
internal sealed class AbfRawDocument
{
    public required int HeaderLine { get; init; }
    public required bool Headerless { get; init; }
    public required bool Wrapped { get; init; }
    public AbfLines.Header? Header { get; init; }

    /// <summary>0-based inclusive span (spec 6.2): header line to the line that ends it.</summary>
    public int SpanStart { get; set; }

    public int SpanEnd { get; set; }
    public List<AbfRawRecord> Records { get; } = [];
    public List<AbfDiagnostic> Diagnostics { get; } = [];
}

/// <summary>
/// The structural pass (spec sections 2 and 3): finds documents and lexes their lines into records,
/// single-line fields, multi-line fields and rows. It knows no schema.
/// </summary>
internal static class AbfScanner
{
    /// <summary>Every document that has a header line, wrapped or not, in source order.</summary>
    public static List<AbfRawDocument> FindHeaderDocuments(string[] lines)
    {
        var documents = new List<AbfRawDocument>();
        AbfFence? outsideFence = null;
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (AbfLines.TryHeader(line, out var header))
            {
                var document = new AbfRawDocument
                {
                    HeaderLine = i + 1,
                    Headerless = false,
                    Wrapped = outsideFence is not null,
                    Header = header,
                };
                if (document.Wrapped)
                    document.Diagnostics.Add(new(i + 1, AbfCodes.WrappedDocument, AbfSeverity.Warning));
                i = ParseBody(lines, i + 1, document, outsideFence);
                documents.Add(document);
                continue;
            }

            if (outsideFence is { } open)
            {
                if (AbfLines.IsFenceClose(line, open))
                    outsideFence = null;
            }
            else if (AbfLines.TryFenceOpen(line, out var fence))
            {
                outsideFence = fence;
            }

            i++;
        }

        return documents;
    }

    /// <summary>
    /// A headerless document at the first record line (spec 2). Only called when the text has no
    /// header line and the consumer expects a type.
    /// </summary>
    public static AbfRawDocument? FindHeaderless(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (!AbfLines.TryRecord(lines[i], out var record) || record.IsEnd)
                continue;
            var document = new AbfRawDocument { HeaderLine = i + 1, Headerless = true, Wrapped = false };
            document.Diagnostics.Add(new(i + 1, AbfCodes.MissingHeader, AbfSeverity.Warning));
            ParseBody(lines, i, document, wrapper: null);
            return document;
        }

        return null;
    }

    /// <summary>Lexes a document body from <paramref name="start"/>. Returns the index where scanning resumes.</summary>
    private static int ParseBody(string[] lines, int start, AbfRawDocument document, AbfFence? wrapper)
    {
        document.SpanStart = document.HeaderLine - 1;
        AbfRawRecord? record = null;
        string? fieldName = null;
        var fieldLine = 0;
        var fieldSuffix = false;
        var content = new List<string>();
        var fenceCloseAt = -1;

        void CloseField()
        {
            if (fieldName is null || record is null)
            {
                fieldName = null;
                return;
            }

            record.Items.Add(new AbfRawItem(AbfItemKind.MultiLineField, fieldLine, fieldName, JoinContent(content), fieldSuffix));
            fieldName = null;
            content.Clear();
            fenceCloseAt = -1;
        }

        int EndOutside(int index)
        {
            // Ended by a line that does not belong to the document.
            CloseField();
            document.Diagnostics.Add(new(index + 1, AbfCodes.MissingEnd, AbfSeverity.Warning));
            document.SpanEnd = index - 1;
            return index;
        }

        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            var number = i + 1;

            if (fieldName is not null && fenceCloseAt >= 0)
            {
                // Protected fence content inside a multi-line field.
                if (i == fenceCloseAt)
                {
                    content.Add(line);
                    fenceCloseAt = -1;
                    continue;
                }

                if (AbfLines.TryRecord(line, out var endLine) && endLine.IsEnd)
                    return EndDocument(i, endLine);
                if (AbfLines.TryUnescape(line, out var unescapedInFence))
                {
                    content.Add(unescapedInFence);
                    continue;
                }

                if (AbfLines.IsStructuralInField(line))
                    document.Diagnostics.Add(new(number, AbfCodes.UnescapedInFence, AbfSeverity.Warning));
                content.Add(line);
                continue;
            }

            if (wrapper is { } wrap && AbfLines.IsFenceClose(line, wrap))
                return EndOutside(i);

            if (AbfLines.TryUnescape(line, out var unescaped))
            {
                if (fieldName is not null)
                    content.Add(unescaped);
                else if (record is not null)
                    record.Items.Add(new AbfRawItem(AbfItemKind.Row, number, "", unescaped.Trim(), Escaped: true));
                else
                    document.Diagnostics.Add(new(number, AbfCodes.StrayText, AbfSeverity.Warning));
                continue;
            }

            if (AbfLines.TryHeader(line, out _))
                return EndOutside(i);

            if (AbfLines.TryRecord(line, out var recordLine))
            {
                if (recordLine.IsEnd)
                    return EndDocument(i, recordLine);
                CloseField();
                record = new AbfRawRecord(recordLine.Type, recordLine.Id, number);
                document.Records.Add(record);
                if (recordLine.Stray is not null)
                    document.Diagnostics.Add(new(number, AbfCodes.StrayText, AbfSeverity.Warning));
                continue;
            }

            if (AbfLines.TryLegacyHeading(line, out var words))
            {
                if (words.Contains(' '))
                    return EndOutside(i);
                CloseField();
                if (record is null)
                {
                    document.Diagnostics.Add(new(number, AbfCodes.StrayText, AbfSeverity.Warning));
                    continue;
                }

                StartField(words, number, suffix: true);
                continue;
            }

            if (AbfLines.TryFieldHeading(line, out var heading))
            {
                CloseField();
                if (record is null)
                {
                    document.Diagnostics.Add(new(number, AbfCodes.StrayText, AbfSeverity.Warning));
                    continue;
                }

                StartField(heading, number, suffix: false);
                continue;
            }

            if (fieldName is not null)
            {
                if (AbfLines.TryFenceOpen(line, out var fence))
                {
                    var close = FindFenceClose(lines, i + 1, fence);
                    if (close < 0)
                        document.Diagnostics.Add(new(number, AbfCodes.UnterminatedFence, AbfSeverity.Warning));
                    else
                        fenceCloseAt = close;
                }

                content.Add(line);
                continue;
            }

            if (AbfLines.IsBlank(line))
                continue;

            if (record is null)
            {
                document.Diagnostics.Add(new(number, AbfCodes.StrayText, AbfSeverity.Warning));
                continue;
            }

            if (AbfLines.TryField(line, out var key, out var value))
                record.Items.Add(new AbfRawItem(AbfItemKind.Field, number, key, value));
            else
                record.Items.Add(new AbfRawItem(AbfItemKind.Row, number, "", line.Trim()));
        }

        // End of input.
        CloseField();
        var last = Math.Max(lines.Length, 1);
        document.Diagnostics.Add(new(last, AbfCodes.MissingEnd, AbfSeverity.Warning));
        document.SpanEnd = lines.Length - 1;
        return lines.Length;

        void StartField(string name, int line, bool suffix)
        {
            fieldName = name;
            fieldLine = line;
            fieldSuffix = suffix;
            content.Clear();
            fenceCloseAt = -1;
        }

        int EndDocument(int index, AbfLines.RecordLine endLine)
        {
            CloseField();
            if (endLine.Stray is not null)
                document.Diagnostics.Add(new(index + 1, AbfCodes.StrayText, AbfSeverity.Warning));
            document.SpanEnd = index;
            return index + 1;
        }
    }

    /// <summary>The closing line of a fence, searched up to the next <c>=== end</c> or the end of input (3.6).</summary>
    private static int FindFenceClose(string[] lines, int from, AbfFence fence)
    {
        for (var j = from; j < lines.Length; j++)
        {
            if (AbfLines.IsEnd(lines[j]))
                return -1;
            if (AbfLines.IsFenceClose(lines[j], fence))
                return j;
        }

        return -1;
    }

    /// <summary>Multi-line values drop leading and trailing blank lines and join with LF (3.4).</summary>
    private static string JoinContent(List<string> content)
    {
        var first = 0;
        var last = content.Count - 1;
        while (first <= last && AbfLines.IsBlank(content[first]))
            first++;
        while (last >= first && AbfLines.IsBlank(content[last]))
            last--;
        return first > last ? "" : string.Join('\n', content.GetRange(first, last - first + 1));
    }
}
