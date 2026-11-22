namespace Antiphon.Server.Application.Review.Abf;

/// <summary>Value types (spec 7.0).</summary>
public enum AbfValueType
{
    Text,
    Id,
    Enum,
    Bool,
    Prob,
    Int,
    List,
    Sha,
    Path,
    Lines,
    TypeRef,
}

public sealed record AbfFieldSchema(
    string Name,
    AbfValueType Type,
    bool Required = false,
    bool Repeatable = false,
    IReadOnlyList<string>? Members = null)
{
    /// <summary>A list field is always repeatable (7.0).</summary>
    public bool AllowsRepeat => Repeatable || Type == AbfValueType.List;
}

public sealed record AbfColumnSchema(string Name, AbfValueType Type = AbfValueType.Text, bool Optional = false, IReadOnlyList<string>? Members = null);

public sealed record AbfRecordSchema(
    string Type,
    int MinCount,
    int? MaxCount,
    IReadOnlyList<AbfFieldSchema> Fields,
    IReadOnlyList<AbfColumnSchema>? Columns = null,
    bool DuplicateIdIsError = false)
{
    public bool DeclaresColumns => Columns is { Count: > 0 };

    public AbfFieldSchema? FindField(string written)
    {
        var fold = AbfNames.Fold(written);
        return Fields.FirstOrDefault(f => AbfNames.Fold(f.Name) == fold);
    }

    public AbfFieldSchema? FindFieldByWords(string words)
    {
        var fold = AbfNames.FoldWords(words);
        return Fields.FirstOrDefault(f => AbfNames.Fold(f.Name) == fold);
    }

    /// <summary>Cardinality 1 or 0..1 (spec 3.4 repeated records).</summary>
    public bool Single => MaxCount == 1;
}

public sealed record AbfDocumentSchema(string Type, int Major, int Minor, IReadOnlyList<AbfRecordSchema> Records)
{
    public AbfRecordSchema? FindRecord(string written)
    {
        var fold = AbfNames.Fold(written);
        return Records.FirstOrDefault(r => AbfNames.Fold(r.Type) == fold);
    }
}

/// <summary>The document-type registry (spec section 7).</summary>
public static class AbfSchemas
{
    public static readonly IReadOnlyList<string> Severities = ["nit", "minor", "major", "blocker"];
    public static readonly IReadOnlyList<string> CheckStatuses = ["pass", "finding", "n/a", "unverified"];
    public static readonly IReadOnlyList<string> ContextKinds = ["opened", "missing"];
    public static readonly IReadOnlyList<string> QuestionTypes = ["noul", "choice", "score"];
    public static readonly IReadOnlyList<string> Scopes = ["instance", "path", "repo", "team"];

    public static readonly IReadOnlyList<string> ReasonCategories =
        ["false-positive", "by-design", "out-of-scope", "accepted-risk", "fix-later", "duplicate", "style-preference"];

    public static readonly IReadOnlyList<string> Intents =
    [
        "dismiss", "dispute", "claims-fixed", "will-fix", "question-finding", "question-repo", "noise",
        "request-recheck", "new-issue", "defer", "instruction", "approval", "request-fix",
    ];

    public static readonly IReadOnlyList<string> ProvenanceKinds = ["doc", "commit", "issue", "pr", "incident", "card", "url", "other"];

    private static AbfFieldSchema Text(string name, bool required = false) => new(name, AbfValueType.Text, required);
    private static AbfFieldSchema Typed(string name, AbfValueType type, bool required = false) => new(name, type, required);
    private static AbfFieldSchema Enum(string name, IReadOnlyList<string> members, bool required = false) =>
        new(name, AbfValueType.Enum, required, Members: members);

    public static readonly AbfDocumentSchema ReviewFindings = new("review-findings", 1, 0,
    [
        new("finding", 0, null,
        [
            Typed("check", AbfValueType.Id, required: true),
            Enum("severity", Severities, required: true),
            Typed("confidence", AbfValueType.Prob, required: true),
            Typed("file", AbfValueType.Path, required: true),
            Text("anchor", required: true),
            Text("body", required: true),
            Text("title"),
            Typed("flags", AbfValueType.List),
            Typed("lines", AbfValueType.Lines),
            Text("suggestion"),
        ]),
        new("checks", 1, 1, [],
        [
            new("check-id", AbfValueType.Id),
            new("status", AbfValueType.Enum, Members: CheckStatuses),
            new("detail", Optional: true),
        ]),
        new("context", 0, 1, [],
        [
            new("kind", AbfValueType.Enum, Members: ContextKinds),
            new("path", AbfValueType.Path),
            new("reason", Optional: true),
        ]),
        new("summary", 0, 1, [Text("body", required: true)]),
    ]);

    public static readonly AbfDocumentSchema DecisionRequest = new("decision-request", 1, 0,
    [
        new("request", 1, 1, [Typed("respond-with", AbfValueType.TypeRef)]),
        new("state", 1, 1, [Text("body", required: true)]),
        new("question", 1, 128,
        [
            Enum("type", QuestionTypes, required: true),
            Text("instructions", required: true),
        ],
        [
            new("option"),
            new("description", Optional: true),
        ], DuplicateIdIsError: true),
    ]);

    public static readonly AbfDocumentSchema DecisionResponse = new("decision-response", 1, 0,
    [
        new("response", 0, 1, [Text("note")]),
        // value and confidence are "req unless refused": the type rules check them.
        new("answer", 0, null,
        [
            Text("value"),
            Typed("confidence", AbfValueType.Prob),
            Typed("refused", AbfValueType.Bool),
            Text("reason"),
        ],
        [
            new("option"),
            new("probability", AbfValueType.Prob),
        ]),
    ]);

    public static readonly AbfDocumentSchema CompletionRequest = new("completion-request", 1, 0,
    [
        new("request", 1, 1, [Text("expect", required: true)]),
        new("instructions", 1, 1, [Text("body", required: true)]),
        new("input", 0, null, [Text("body", required: true), Text("title"), Text("source")]),
    ]);

    public static readonly AbfDocumentSchema CommentTriage = new("comment-triage", 1, 0,
    [
        new("comment", 1, null,
        [
            Typed("finding", AbfValueType.Id),
            Enum("scope", Scopes),
            Typed("scope-confidence", AbfValueType.Prob),
            Enum("reason-category", ReasonCategories),
            Typed("reason-category-confidence", AbfValueType.Prob),
            Text("reason"),
        ],
        [
            new("intent", AbfValueType.Enum, Members: Intents),
            new("probability", AbfValueType.Prob),
        ]),
    ]);

    public static readonly AbfDocumentSchema SeedCandidates = new("seed-candidates", 1, 0,
    [
        new("source", 0, 1, [Text("uri", required: true), Text("cursor"), Text("shard")]),
        new("check-candidate", 0, null,
        [
            Text("title", required: true),
            Enum("severity", Severities, required: true),
            Text("selector", required: true),
            Text("prompt", required: true),
            Text("humans", required: true),
            Text("rationale", required: true),
            Text("examples"),
            Text("false-positives"),
            Typed("category", AbfValueType.Id),
            Typed("flags", AbfValueType.List),
            Typed("globs", AbfValueType.List),
            Typed("languages", AbfValueType.List),
            Typed("tags", AbfValueType.List),
            Typed("symbols", AbfValueType.List),
            Typed("related-files", AbfValueType.List),
            Typed("requires-human", AbfValueType.Bool),
            Typed("refines", AbfValueType.Id),
        ],
        [
            new("kind", AbfValueType.Enum, Members: ProvenanceKinds),
            new("ref"),
            new("excerpt", Optional: true),
        ]),
        new("eval-case", 0, null,
        [
            Text("repo", required: true),
            Typed("bug-commit", AbfValueType.Sha, required: true),
            Typed("fix-commit", AbfValueType.Sha, required: true),
            Text("description", required: true),
            Typed("check", AbfValueType.Id),
            Typed("files", AbfValueType.List),
        ]),
        new("related-hint", 0, null, [],
        [
            new("when"),
            new("also-load"),
            new("reason", Optional: true),
        ]),
    ]);

    public static readonly IReadOnlyList<AbfDocumentSchema> All =
        [ReviewFindings, DecisionRequest, DecisionResponse, CompletionRequest, CommentTriage, SeedCandidates];

    public static AbfDocumentSchema? Find(string? type) =>
        type is null ? null : All.FirstOrDefault(s => s.Type == type.ToLowerInvariant());
}
