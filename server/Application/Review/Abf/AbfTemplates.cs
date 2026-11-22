namespace Antiphon.Server.Application.Review.Abf;

/// <summary>
/// The answer shapes the engine shows a model that must reply in ABF (docs/specs/block-format/v1.md):
/// a reviewer's <c>review-findings/1.0</c> and a chat model's <c>decision-response/1.0</c>. A reviewer's
/// checkout is the reviewed repository, not Antiphon, so the spec itself is never something it can read.
/// </summary>
public static class AbfTemplates
{
    /// <summary>The review-findings/1.0 contract (spec 7.1), with the placeholders a reviewer fills.</summary>
    public const string ReviewFindings = """
        Reply with exactly one ABF document of this shape, not inside a code fence. Structural lines start at column 0.
        A single-line field is `name: value`; a multi-line field starts with a `--- name` line and runs to the next structural line.

        @abf 1 review-findings/1.0
        === finding F1
        check: <an assigned check id, or none for a defect no check asked about>
        severity: <nit | minor | major | blocker>
        confidence: <probability from 0 to 1 that this is a real defect>
        file: <repo-relative path, / separators>
        anchor: <one line of code copied verbatim from that file at the reviewed head>
        title: <one line>
        --- body
        <what is wrong, why, and what breaks; markdown allowed>
        --- suggestion
        <optional: replacement code for the anchored line>
        === checks
        <check id> pass
        <check id> finding F1
        <check id> n/a
        <check id> unverified <why it could not be verified>
        === context
        opened <path> <why you read it beyond the pack>
        === summary
        body: <one line overall note>
        === end

        Rules: number findings F1, F2, ...; omit `--- suggestion` when you have none; every assigned check gets exactly one
        row in `checks` (`finding` names the finding ids, comma-separated); `context` and `summary` are optional. No findings
        is a valid answer: then `checks` lists every check as pass, n/a or unverified.
        """;

    /// <summary>The decision-response/1.0 contract for noul (probability) questions (spec 7.3).</summary>
    public static string DecisionResponse(string requestId) => $"""
        Reply with exactly one ABF document of this shape, not inside a code fence. Structural lines start at column 0.

        @abf 1 decision-response/1.0
        === response {requestId}
        === answer <question name>
        value: <probability from 0 to 1 that the answer is yes>
        confidence: <your confidence in that value, from 0 to 1>
        === end

        One `=== answer` record per question, named exactly as asked. To decline a question, give it `refused: true` instead of a value.
        """;
}
