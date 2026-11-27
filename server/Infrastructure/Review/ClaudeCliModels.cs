using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Review;

/// <summary>
/// Completions over one-shot <c>claude -p</c> (design 7.2), dialect <c>claude-cli</c>: no tools, an empty
/// scratch folder, the provider's <c>Model</c> (for example <c>haiku</c>), the chain entry's timeout. The
/// prompt is the instructions followed by each input as a labelled section and what the answer must be. It
/// runs on the local Claude Code sign-in, so it needs no key; its cost is api-equivalent (D-20).
/// </summary>
public sealed class ClaudeCliCompletionModel(IClaudeCli cli, IOptions<ReviewCompletionSettings> settings) : ICompletionModel
{
    public bool Serves(string dialect) => dialect.Equals(ReviewCompletionDialects.ClaudeCli, StringComparison.OrdinalIgnoreCase);

    public async Task<CompletionResult> CompleteAsync(CompletionRequest request, CompletionOptions options, CancellationToken ct)
    {
        if (!settings.Value.Providers.TryGetValue(options.ProviderId, out var provider))
            throw new CompletionProviderException(DecisionFailureKind.NotConfigured, $"Completion provider '{options.ProviderId}' is not configured.");
        var model = string.IsNullOrWhiteSpace(options.Model) ? provider.Model : options.Model;
        if (string.IsNullOrWhiteSpace(model))
            throw new CompletionProviderException(DecisionFailureKind.NotConfigured, $"Completion provider '{options.ProviderId}' names no model.");
        var timeout = options.Timeout ?? TimeSpan.FromSeconds(Math.Max(1, provider.TimeoutSeconds));

        ClaudeCliResult result;
        try
        {
            result = await ClaudeCliScratch.RunAsync(cli, Prompt(request), model.Trim(), timeout, ct);
        }
        catch (ClaudeCliException ex)
        {
            throw new CompletionProviderException(ClaudeCliScratch.FailureKind(ex.Kind), ex.Message) { PublicDiagnostic = ex.Message };
        }

        return new CompletionResult(result.Text, new DecisionUsage(result.InputTokens, result.OutputTokens), result.CostUsd, "api-equivalent",
            new DecisionServedBy(options.ProviderId, ClaudeCliScratch.Transport, result.Model));
    }

    /// <summary>The instructions, then each input under its name, then what the answer must be.</summary>
    public static string Prompt(CompletionRequest request) =>
        (request.Instructions.Trim() + "\n\n" + CompletionText.UserMessage(request)).ReplaceLineEndings("\n");
}

/// <summary>
/// Typed decisions over one-shot <c>claude -p</c> (design 7.2), dialect <c>claude-cli</c>: no tools, the
/// provider's <c>Model</c> (for example <c>haiku</c>), the chain entry's timeout, no key. The prompt gives
/// the state and every question and asks for one JSON object <c>{"answers":[{"name","value","p"}]}</c>,
/// where <c>p</c> is the probability that <c>value</c> is right. The answers map as the Jev client maps
/// its own: noul is yes or no with a yes/no distribution (the probability of yes is <c>p</c> for a yes and
/// <c>1 - p</c> for a no), choice is one of the allowed values, score is one of the level labels, each with
/// <c>p</c> as its confidence. A missing or invalid answer is refused for that question only; output with
/// no JSON answers at all is a schema failure, so the chain moves on. The probabilities are not
/// calibrated (design 7.5); the cost is api-equivalent (D-20).
/// </summary>
public sealed class ClaudeCliDecisionModel(IClaudeCli cli, IOptions<ReviewDecisionSettings> settings) : IDecisionModel
{
    public bool Serves(string dialect) => dialect.Equals(ReviewDecisionSettings.ClaudeCliDialect, StringComparison.OrdinalIgnoreCase);

    public async Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct)
    {
        if (!settings.Value.Providers.TryGetValue(options.ProviderId, out var provider))
            throw new DecisionProviderException(DecisionFailureKind.NotConfigured, $"Provider '{options.ProviderId}' is not configured.");
        var model = string.IsNullOrWhiteSpace(options.Model) ? provider.Model : options.Model;
        if (string.IsNullOrWhiteSpace(model))
            throw new DecisionProviderException(DecisionFailureKind.NotConfigured, $"Provider '{options.ProviderId}' names no model.");
        var timeout = options.Timeout ?? TimeSpan.FromSeconds(Math.Max(1, provider.TimeoutSeconds));

        ClaudeCliResult result;
        try
        {
            result = await ClaudeCliScratch.RunAsync(cli, Prompt(request), model.Trim(), timeout, ct);
        }
        catch (ClaudeCliException ex)
        {
            throw new DecisionProviderException(ClaudeCliScratch.FailureKind(ex.Kind), ex.Message) { PublicDiagnostic = ex.Message };
        }

        var servedBy = new DecisionServedBy(options.ProviderId, ClaudeCliScratch.Transport, result.Model);
        var answers = ParseAnswers(request, result.Text, servedBy);
        return new DecisionResult(answers, new DecisionUsage(result.InputTokens, result.OutputTokens), result.CostUsd, "api-equivalent", servedBy);
    }

    /// <summary>The state, every question with its kind and allowed values or levels, and the JSON answer shape.</summary>
    public static string Prompt(DecisionRequest request)
    {
        var text = new StringBuilder()
            .AppendLine("Answer typed questions about the state below for the Antiphon review engine. Use only the state; you have no tools.")
            .AppendLine()
            .AppendLine("# State")
            .AppendLine(Fenced(request.State))
            .AppendLine()
            .AppendLine("# Questions");
        var number = 0;
        foreach (var question in request.Questions)
        {
            number++;
            text.Append(number).Append(". name: ").AppendLine(question.Name);
            switch (question.Kind)
            {
                case DecisionQuestionKind.Noul:
                    text.AppendLine("   kind: noul (value is \"yes\" or \"no\")");
                    break;
                case DecisionQuestionKind.Choice:
                    text.AppendLine("   kind: choice (value is exactly one of these values)");
                    AppendOptions(text, question);
                    break;
                default:
                    text.AppendLine("   kind: score (value is exactly one of these level labels)");
                    AppendOptions(text, question);
                    break;
            }

            text.AppendLine("   question:");
            foreach (var line in question.Instructions.Trim().ReplaceLineEndings("\n").Split('\n'))
                text.Append("     ").AppendLine(line);
        }

        return text
            .AppendLine()
            .AppendLine("# Answer")
            .AppendLine("Reply with one JSON object and nothing else: no prose before or after it and no code fence.")
            .AppendLine("""{"answers":[{"name":"<question name>","value":"<your answer>","p":0.0}]}""")
            .AppendLine("Give one entry per question, named exactly as above. For noul, value is \"yes\" or \"no\". For choice and score,")
            .AppendLine("copy value exactly from that question's list. p is your probability, from 0.0 to 1.0, that value is the right answer.")
            .ToString()
            .ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Reads the answers from <paramref name="text"/>: the JSON object may be fenced or surrounded by prose,
    /// and <c>answers</c> may be an array of <c>{name, value, p}</c> or an object keyed by name. Each
    /// question gets an answer; a missing or invalid one is refused. No JSON object with <c>answers</c> is a
    /// <see cref="DecisionFailureKind.SchemaFailure"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, DecisionAnswer> ParseAnswers(DecisionRequest request, string text, DecisionServedBy servedBy)
    {
        var root = ReadObject(text)
            ?? throw new DecisionProviderException(DecisionFailureKind.SchemaFailure, "The answer is not a JSON object.");
        var entries = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        switch (root["answers"])
        {
            case JsonArray array:
                foreach (var item in array.OfType<JsonObject>())
                {
                    if (Text(item["name"]) is { } name)
                        entries.TryAdd(name.Trim(), item);
                }

                break;
            case JsonObject byName:
                foreach (var (name, node) in byName)
                {
                    if (node is JsonObject item)
                        entries.TryAdd(name.Trim(), item);
                    else if (node is JsonValue)
                        entries.TryAdd(name.Trim(), new JsonObject { ["value"] = node.DeepClone() });
                }

                break;
            default:
                throw new DecisionProviderException(DecisionFailureKind.SchemaFailure, "The answer has no 'answers' list.");
        }

        var answers = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
        foreach (var question in request.Questions)
        {
            answers[question.Name] = entries.TryGetValue(question.Name, out var entry)
                ? Map(question, entry, servedBy)
                : Refused(question, servedBy);
        }

        return answers;
    }

    private static DecisionAnswer Map(DecisionQuestion question, JsonObject entry, DecisionServedBy servedBy)
    {
        var p = Probability(entry["p"] ?? entry["probability"] ?? entry["confidence"]);
        if (p is not { } confidence)
            return Refused(question, servedBy);

        if (question.Kind == DecisionQuestionKind.Noul)
        {
            if (!YesNo(entry["value"] ?? entry["answer"], out var yes))
                return Refused(question, servedBy);
            var pYes = yes ? confidence : 1 - confidence;
            var map = new Dictionary<string, double> { ["yes"] = pYes, ["no"] = 1 - pYes };
            return new DecisionAnswer(question.Name, question.Kind, pYes >= 0.5 ? "yes" : "no", Math.Max(pYes, 1 - pYes), map, false, servedBy);
        }

        var given = Text(entry["value"] ?? entry["answer"] ?? entry["label"])?.Trim();
        if (given is null)
            return Refused(question, servedBy);
        var option = question.Options.FirstOrDefault(o => o.Value == given)
            ?? question.Options.FirstOrDefault(o => string.Equals(o.Value.Trim(), given, StringComparison.OrdinalIgnoreCase));
        return option is null
            ? Refused(question, servedBy)
            : new DecisionAnswer(question.Name, question.Kind, option.Value, confidence, null, false, servedBy);
    }

    private static DecisionAnswer Refused(DecisionQuestion question, DecisionServedBy servedBy) =>
        new(question.Name, question.Kind, null, null, null, Refused: true, servedBy);

    /// <summary>The JSON object in the answer: inside its first code fence when there is one, from the first <c>{</c> to the last <c>}</c>.</summary>
    private static JsonObject? ReadObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var body = text.Trim();
        var fence = body.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var start = body.IndexOf('\n', fence);
            var end = start < 0 ? -1 : body.IndexOf("```", start, StringComparison.Ordinal);
            if (start >= 0 && end > start)
                body = body[(start + 1)..end];
        }

        var open = body.IndexOf('{');
        var close = body.LastIndexOf('}');
        if (open < 0 || close <= open)
            return null;
        try
        {
            return JsonNode.Parse(body[open..(close + 1)]) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AppendOptions(StringBuilder text, DecisionQuestion question)
    {
        foreach (var option in question.Options)
        {
            text.Append("   - \"").Append(option.Value).Append('"');
            if (!string.IsNullOrWhiteSpace(option.Description))
                text.Append(": ").Append(option.Description.Trim().ReplaceLineEndings(" "));
            text.AppendLine();
        }
    }

    private static string Fenced(string body)
    {
        int longest = 0, run = 0;
        foreach (var c in body)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var fence = new string('`', Math.Max(3, longest + 1));
        return $"{fence}\n{body.TrimEnd()}\n{fence}";
    }

    private static double? Probability(JsonNode? node)
    {
        double value;
        if (node is JsonValue v && v.TryGetValue<double>(out var number))
            value = number;
        else if (Text(node) is { } s && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            value = parsed;
        else
            return null;
        return value is >= 0 and <= 1 && !double.IsNaN(value) ? value : null;
    }

    private static bool YesNo(JsonNode? node, out bool yes)
    {
        yes = false;
        if (node is JsonValue v && v.TryGetValue<bool>(out var flag))
        {
            yes = flag;
            return true;
        }

        var word = Text(node)?.Trim();
        if (word is null)
            return false;
        yes = word.Equals("yes", StringComparison.OrdinalIgnoreCase) || word.Equals("true", StringComparison.OrdinalIgnoreCase);
        return yes || word.Equals("no", StringComparison.OrdinalIgnoreCase) || word.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Text(JsonNode? node) => node is JsonValue v
        ? v.TryGetValue<string>(out var s) ? s : v.ToString()
        : null;
}

/// <summary>A tool-less <c>claude -p</c> call in its own empty scratch folder, removed afterwards.</summary>
internal static class ClaudeCliScratch
{
    /// <summary>The <see cref="DecisionServedBy.Transport"/> of every one-shot call.</summary>
    public const string Transport = ReviewDecisionChainEntry.OneShotTransport;

    public static DecisionFailureKind FailureKind(ClaudeCliFailureKind kind) => kind switch
    {
        ClaudeCliFailureKind.Start or ClaudeCliFailureKind.Authentication => DecisionFailureKind.Rejected,
        ClaudeCliFailureKind.Timeout => DecisionFailureKind.Timeout,
        _ => DecisionFailureKind.Unavailable,
    };

    public static async Task<ClaudeCliResult> RunAsync(IClaudeCli cli, string prompt, string model, TimeSpan timeout, CancellationToken ct)
    {
        var directory = Path.Combine(Path.GetTempPath(), "antiphon-review-cli", "calls", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var result = await cli.RunAsync(new ClaudeCliRequest(prompt, model, [], directory, timeout), ct);
            if (string.IsNullOrWhiteSpace(result.Text))
                throw new ClaudeCliException("claude -p answered with no result text.");
            return result;
        }
        finally
        {
            ReviewChildProcess.TryDeleteDirectory(directory);
        }
    }
}
