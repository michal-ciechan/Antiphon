namespace Antiphon.Server.Application.Review.Abf;

/// <summary>ABF parsing modes (spec section 5).</summary>
public enum AbfMode
{
    /// <summary>Model output: repairs are warnings, only unrepairable problems are errors.</summary>
    Tolerant,

    /// <summary>Antiphon-produced documents and the corpus: every warning becomes an error.</summary>
    Strict,
}

public enum AbfSeverity
{
    Error,
    Warning,
    Info,
}

/// <summary>One diagnostic (spec section 4). <see cref="Line"/> is 1-based.</summary>
public sealed record AbfDiagnostic(int Line, string Code, AbfSeverity Severity, string? Name = null, string? Message = null);

/// <summary>A single value of a field, in source order.</summary>
public sealed record AbfFieldValue(string Value, int Line);

/// <summary>A field of a record. Repeated names keep every value in source order (spec 3.4).</summary>
public sealed class AbfField
{
    internal AbfField(string name) => Name = name;

    /// <summary>The declared spelling, or the first spelling seen for an undeclared name.</summary>
    public string Name { get; }

    public List<AbfFieldValue> Values { get; } = [];

    /// <summary>The last value, which is the typed value of a non-repeatable field.</summary>
    public AbfFieldValue Last => Values[^1];
}

/// <summary>A row of a record. <see cref="Columns"/> is null in a record type that declares no columns.</summary>
public sealed class AbfRow
{
    internal AbfRow(int line, string text, IReadOnlyList<string>? columns)
    {
        Line = line;
        Text = text;
        Columns = columns;
    }

    public int Line { get; }
    public string Text { get; }
    public IReadOnlyList<string>? Columns { get; }

    /// <summary>False when typed validation dropped the row (bad enum, bad value, unknown option).</summary>
    public bool Valid { get; internal set; } = true;

    /// <summary>Typed column values (null entries for absent optional columns), set by validation.</summary>
    public object?[] TypedColumns { get; internal set; } = [];
}

/// <summary>One record in canonical form.</summary>
public sealed class AbfRecord
{
    internal AbfRecord(string type, string? id, int line, AbfRecordSchema? schema)
    {
        Type = type;
        Id = id;
        Line = line;
        Schema = schema;
    }

    public string Type { get; }
    public string? Id { get; }
    public int Line { get; }
    public AbfRecordSchema? Schema { get; }
    public List<AbfField> Fields { get; } = [];
    public List<AbfRow> Rows { get; } = [];

    /// <summary>Converted field values by declared name (spec 7.0), filled by validation.</summary>
    public Dictionary<string, object?> Typed { get; } = new(StringComparer.Ordinal);

    public AbfField? Field(string name) =>
        Fields.FirstOrDefault(f => AbfNames.Fold(f.Name) == AbfNames.Fold(name));

    public T? TypedValue<T>(string name) => Typed.TryGetValue(name, out var v) && v is T t ? t : default;
}

/// <summary>A selected ABF document in canonical form with its diagnostics.</summary>
public sealed class AbfDocument
{
    internal AbfDocument(int? syntax, string? type, string? version, int headerLine)
    {
        Syntax = syntax;
        Type = type;
        Version = version;
        HeaderLine = headerLine;
    }

    /// <summary>The syntax version (1), or null for a generic headerless parse.</summary>
    public int? Syntax { get; }

    public string? Type { get; }

    /// <summary><c>MAJOR.MINOR</c>.</summary>
    public string? Version { get; }

    /// <summary>The header line, or the first record line of a headerless document.</summary>
    public int HeaderLine { get; }

    public List<AbfRecord> Records { get; } = [];
    public List<AbfDiagnostic> Diagnostics { get; } = [];

    public bool HasErrors => Diagnostics.Any(d => d.Severity == AbfSeverity.Error);

    /// <summary>The typed projection the type rules built while validating (spec 4, typed JSON).</summary>
    internal object? Projection { get; set; }

    public IEnumerable<AbfRecord> RecordsOf(string type) =>
        Records.Where(r => AbfNames.Fold(r.Type) == AbfNames.Fold(type));
}

/// <summary>A document type and version, e.g. <c>review-findings/1.0</c>.</summary>
public readonly record struct AbfTypeRef(string Type, int Major, int Minor)
{
    public override string ToString() => $"{Type}/{Major}.{Minor}";

    public static bool TryParse(string? text, out AbfTypeRef value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var match = AbfLines.TypeRefPattern.Match(text.Trim());
        if (!match.Success)
            return false;
        if (!int.TryParse(match.Groups["major"].Value, out var major))
            return false;
        var minor = 0;
        if (match.Groups["minor"].Success && !int.TryParse(match.Groups["minor"].Value, out minor))
            return false;
        value = new AbfTypeRef(match.Groups["type"].Value.ToLowerInvariant(), major, minor);
        return true;
    }

    public static AbfTypeRef Parse(string text) =>
        TryParse(text, out var value) ? value : throw new FormatException($"Not an ABF type reference: '{text}'.");
}

/// <summary>Context and expectations for one parse.</summary>
public sealed record AbfParseOptions
{
    public AbfMode Mode { get; init; } = AbfMode.Tolerant;

    /// <summary>The type the consumer expects. Null is a generic parse of the last document.</summary>
    public AbfTypeRef? Expect { get; init; }

    /// <summary>Cut the text at the closing report token first (spec 6.1). On by default.</summary>
    public bool ReportEnvelope { get; init; } = true;

    /// <summary>For <c>decision-response</c>: the request it answers (spec 7.3).</summary>
    public DecisionRequestDocument? Request { get; init; }

    /// <summary>For <c>review-findings</c>: the checks assigned in the pack (spec 7.1).</summary>
    public IReadOnlyCollection<string>? AssignedChecks { get; init; }

    public static AbfParseOptions Expecting(string typeRef, AbfMode mode = AbfMode.Tolerant) =>
        new() { Expect = AbfTypeRef.Parse(typeRef), Mode = mode };
}

/// <summary>Diagnostic codes (spec 5.1 and section 7).</summary>
public static class AbfCodes
{
    public const string MissingDocument = "missing-document";
    public const string WrongType = "wrong-type";
    public const string UnsupportedSyntax = "unsupported-syntax";
    public const string UnsupportedVersion = "unsupported-version";
    public const string MissingHeader = "missing-header";
    public const string MissingEnd = "missing-end";
    public const string WrappedDocument = "wrapped-document";
    public const string DuplicateDocument = "duplicate-document";
    public const string StrayText = "stray-text";
    public const string UnterminatedFence = "unterminated-fence";
    public const string UnescapedInFence = "unescaped-in-fence";
    public const string FieldHeadingSuffix = "field-heading-suffix";
    public const string NameSpelling = "name-spelling";
    public const string UnknownRecord = "unknown-record";
    public const string UnknownKey = "unknown-key";
    public const string DuplicateField = "duplicate-field";
    public const string DuplicateRecord = "duplicate-record";
    public const string DuplicateId = "duplicate-id";
    public const string MissingRecord = "missing-record";
    public const string MissingRequired = "missing-required";
    public const string BadEnum = "bad-enum";
    public const string BadValue = "bad-value";
    public const string ValueRepaired = "value-repaired";
    public const string ShortSha = "short-sha";
    public const string UnexpectedRow = "unexpected-row";
    public const string BadRow = "bad-row";
    public const string MissingColumn = "missing-column";
    public const string RowBullet = "row-bullet";
    public const string FieldAsRow = "field-as-row";
    public const string RowAsField = "row-as-field";

    // review-findings/1
    public const string MissingCheck = "missing-check";
    public const string UnknownCheck = "unknown-check";
    public const string DuplicateCheck = "duplicate-check";
    public const string DanglingFinding = "dangling-finding";
    public const string CheckStatusMismatch = "check-status-mismatch";

    // decision-request/1 and decision-response/1
    public const string DuplicateOption = "duplicate-option";
    public const string MissingOptions = "missing-options";
    public const string RequestMismatch = "request-mismatch";
    public const string MissingAnswer = "missing-answer";
    public const string UnknownAnswer = "unknown-answer";
    public const string MissingOption = "missing-option";
    public const string UnknownOption = "unknown-option";
    public const string ProbabilitiesRenormalised = "probabilities-renormalised";
    public const string ValueNotMostProbable = "value-not-most-probable";

    // comment-triage/1
    public const string MissingIntent = "missing-intent";
    public const string DuplicateIntent = "duplicate-intent";

    // seed-candidates/1
    public const string MissingProvenance = "missing-provenance";
    public const string MissingTrigger = "missing-trigger";
}
