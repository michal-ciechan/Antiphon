namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// CARD-0382: fail-closed Windows Grok rules argv policy. A payload containing CR, LF, or NUL,
/// or more than <see cref="MaxUtf16Units"/> UTF-16 code units, is refused. Callers pass the
/// Windows and Grok decisions explicitly so tests can exercise both branches without mutating
/// the process environment. Validate-only: never normalizes, truncates, or rewrites the value.
/// </summary>
public static class GrokRulesArgvPolicy
{
    public const int MaxUtf16Units = 4096;
    public const string ProblemCode = "grok_rules_argv_unsafe";

    public const string ReasonNul = "nul";
    public const string ReasonLineBreak = "line_break";
    public const string ReasonTokenTooLong = "token_too_long";
    public const string ReasonMissingValue = "missing_value";

    public const string RulesFlag = "--rules";
    public const string AppendSystemPromptFlag = "--append-system-prompt";

    /// <summary>
    /// Inspect a composed rules payload (the value that would be appended after <c>--rules</c>).
    /// </summary>
    public static GrokRulesArgvViolation? ValidatePayload(string? payload, bool isWindows, bool isGrok)
    {
        if (!isWindows || !isGrok)
            return null;
        return InspectValue(payload ?? "", RulesFlag, occurrenceIndex: 1, envTokenName: null);
    }

    /// <summary>
    /// Scan resolved argv for every <c>--rules</c> / <c>--append-system-prompt</c> occurrence,
    /// including equals forms. A later good value does not hide an earlier bad one. Elements
    /// after a bare <c>--</c> are not scanned. <paramref name="envTokenNames"/> is parallel to
    /// <paramref name="args"/>: the env variable name that produced that element, or null.
    /// </summary>
    public static GrokRulesArgvViolation? ValidateArgv(
        IReadOnlyList<string>? args,
        bool isWindows,
        bool isGrok,
        IReadOnlyList<string?>? envTokenNames = null)
    {
        if (!isWindows || !isGrok || args is null || args.Count == 0)
            return null;

        var terminated = false;
        var occurrence = 0;
        for (var i = 0; i < args.Count; i++)
        {
            if (terminated)
                break;

            var arg = args[i] ?? "";
            if (arg == "--")
            {
                terminated = true;
                continue;
            }

            if (TrySplitEqualsFlag(arg, out var equalsFlag, out var equalsValue))
            {
                occurrence++;
                var violation = InspectValue(
                    equalsValue, equalsFlag, occurrence, TokenNameAt(envTokenNames, i));
                if (violation is not null)
                    return violation;
                continue;
            }

            if (!IsSpaceSeparatedFlag(arg))
                continue;

            occurrence++;
            if (i + 1 >= args.Count)
            {
                return new GrokRulesArgvViolation(
                    ReasonMissingValue, arg, occurrence, Length: null, Limit: null, EnvTokenName: null);
            }

            i++;
            var value = args[i] ?? "";
            var valueViolation = InspectValue(
                value, arg, occurrence, TokenNameAt(envTokenNames, i));
            if (valueViolation is not null)
                return valueViolation;
        }

        return null;
    }

    /// <summary>
    /// Diagnostic text. Never includes the payload, an env value, a command line, or the word
    /// "override". Always names the flag and reason; names the count/limit for
    /// <see cref="ReasonTokenTooLong"/>; names the env token when one supplied the value.
    /// </summary>
    public static string Format(GrokRulesArgvViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);
        var text = $"{ProblemCode}: {violation.Flag} is {violation.Reason} (occurrence {violation.OccurrenceIndex}";
        if (violation.Reason == ReasonTokenTooLong)
            text += $", {violation.Length} UTF-16 units exceeds the {violation.Limit} limit";
        if (!string.IsNullOrEmpty(violation.EnvTokenName))
            text += $", from env token {violation.EnvTokenName}";
        return text + ")";
    }

    private static GrokRulesArgvViolation? InspectValue(
        string value, string flag, int occurrenceIndex, string? envTokenName)
    {
        // Precedence (D-T2): nul → line_break → token_too_long → missing_value.
        if (value.Contains('\0'))
            return new GrokRulesArgvViolation(ReasonNul, flag, occurrenceIndex, EnvTokenName: envTokenName);
        if (value.Contains('\n') || value.Contains('\r'))
            return new GrokRulesArgvViolation(ReasonLineBreak, flag, occurrenceIndex, EnvTokenName: envTokenName);
        if (value.Length > MaxUtf16Units)
        {
            return new GrokRulesArgvViolation(
                ReasonTokenTooLong, flag, occurrenceIndex,
                Length: value.Length, Limit: MaxUtf16Units, EnvTokenName: envTokenName);
        }

        return null;
    }

    private static bool IsSpaceSeparatedFlag(string arg) =>
        arg == RulesFlag || arg == AppendSystemPromptFlag;

    private static bool TrySplitEqualsFlag(string arg, out string flag, out string value)
    {
        flag = "";
        value = "";
        if (arg.StartsWith(RulesFlag + "=", StringComparison.Ordinal))
        {
            flag = RulesFlag;
            value = arg[(RulesFlag.Length + 1)..];
            return true;
        }

        if (arg.StartsWith(AppendSystemPromptFlag + "=", StringComparison.Ordinal))
        {
            flag = AppendSystemPromptFlag;
            value = arg[(AppendSystemPromptFlag.Length + 1)..];
            return true;
        }

        return false;
    }

    private static string? TokenNameAt(IReadOnlyList<string?>? names, int index)
    {
        if (names is null || index < 0 || index >= names.Count)
            return null;
        var name = names[index];
        return string.IsNullOrEmpty(name) ? null : name;
    }
}

/// <summary>A policy violation. Contains no payload text.</summary>
public sealed record GrokRulesArgvViolation(
    string Reason,
    string Flag,
    int OccurrenceIndex,
    int? Length = null,
    int? Limit = null,
    string? EnvTokenName = null);
