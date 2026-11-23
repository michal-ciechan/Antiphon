using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Checks;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Antiphon.Server.Application.Review.Execution;

/// <summary>One candidate check as select recorded it (design 4.4 step 4: selected and rejected are both stored).</summary>
public sealed record CheckSelection(string Id, string Title, string Category, ReviewSeverity Severity, double? Probability, bool Selected, string Reason);

public sealed record SelectOutput(string Sources, IReadOnlyList<string> Problems, IReadOnlyList<CheckSelection> Candidates)
{
    public IReadOnlyList<string> Selected => Candidates.Where(c => c.Selected).Select(c => c.Id).ToList();
}

public sealed record GroupOutput(IReadOnlyList<ReviewGroup> Groups);

public sealed record PackRef(string Group, string Hash, int Tokens);

public sealed record PacksOutput(string PrefixHash, IReadOnlyList<PackRef> Packs, int LedgerIncluded, int LedgerExcluded);

/// <summary>Check selection, grouping and context packs (design 4.4 to 4.6).</summary>
public static class ReviewSelectionSteps
{
    /// <summary>The LiteLLM route's question limit (design 7.3): candidates are split across calls.</summary>
    public const int MaxQuestionsPerCall = 128;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp", [".ts"] = "typescript", [".tsx"] = "typescript", [".js"] = "javascript", [".jsx"] = "javascript",
        [".py"] = "python", [".go"] = "go", [".java"] = "java", [".kt"] = "kotlin", [".rs"] = "rust", [".rb"] = "ruby",
        [".sql"] = "sql", [".ps1"] = "powershell", [".sh"] = "shell", [".yaml"] = "yaml", [".yml"] = "yaml", [".md"] = "markdown",
    };

    /// <summary>
    /// Select (design 4.4): a code prefilter by triggers, the round's bar dropping checks whose
    /// severity cannot clear it (bypass checks stay), then one <c>applies</c> question per candidate.
    /// A candidate refused by every chain entry is included and says so: more review, not less.
    /// </summary>
    public static async Task<NodeResult> SelectChecksAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var set = await walk.ChecksAsync(ct);
        var scope = await walk.ScopeAsync(ct);
        var diff = await walk.DiffAsync(scope.Range, ct) ?? "";
        var policy = walk.Policy;
        var bar = ReviewRoundsPolicy.BarFor(policy, walk.Run.Round);
        var minimum = ReviewRoundsPolicy.MinSeverity(bar);
        var tags = walk.Run.Categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var languages = scope.Files.Select(f => Languages.GetValueOrDefault(Path.GetExtension(f.Path))).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var records = new List<CheckSelection>();
        var candidates = new List<ReviewCheck>();
        foreach (var check in set.Checks.Where(c => c.IsActive))
        {
            var bypass = ReviewRoundsPolicy.IsBypass(check.Flags, policy);
            if (!bypass && check.Severity < minimum)
            {
                records.Add(Record(check, null, false, $"severity {check.Severity} cannot clear round {walk.Run.Round}'s bar ({minimum})"));
                continue;
            }

            if (Trigger(check, scope.Files, languages, tags, diff) is not { } why)
            {
                records.Add(Record(check, null, false, "no trigger matched"));
                continue;
            }

            candidates.Add(check);
            records.Add(Record(check, null, false, why));
        }

        NodeUsage? usage = null;
        string? degraded = null;
        if (candidates.Count > 0)
        {
            try
            {
            var chain = node.StringParam("chain") is { Length: > 0 } named ? named : ReviewCategorisation.DefaultChain;
            var state = ReviewCategorisation.BuildState(walk.Run.Subject.Title, null, scope.Description, scope.Files, diff, null)
                + $"\nCategories: {string.Join(", ", walk.Run.Categories)}\n";
            var decisions = walk.Services.GetRequiredService<DecisionChain>();
            foreach (var chunk in candidates.Chunk(MaxQuestionsPerCall))
            {
                var questions = chunk.Select(c => DecisionQuestion.Noul(AppliesName(c.Id), c.Selector)).ToList();
                var decided = await decisions.DecideAsync(chain, new DecisionRequest(state, questions), ct);
                usage = NodeUsage.Add(usage, NodeUsage.From(decided));
                foreach (var check in chunk)
                {
                    var index = records.FindIndex(r => r.Id == check.Id);
                    var trigger = records[index].Reason;
                    if (!decided.Answers.TryGetValue(AppliesName(check.Id), out var answer) || answer.Refused)
                    {
                        records[index] = Record(check, null, true, $"{trigger}; refused by every entry, included");
                        continue;
                    }

                    var p = ReviewIntakeSteps.YesProbability(answer);
                    var selected = p >= policy.SelectThreshold;
                    records[index] = Record(check, p, selected, $"{trigger}; applies p={p:0.00}");
                }
            }
            }
            catch (DecisionChainException ex) when (walk.ContinuesOnFailure(node))
            {
                // Selection is a filter: without a decision model every check whose trigger matched is reviewed.
                degraded = $"no decision model answered ({ex.Message}); every triggered candidate is included";
                foreach (var check in candidates)
                {
                    var index = records.FindIndex(r => r.Id == check.Id);
                    if (records[index].Probability is null)
                        records[index] = Record(check, null, true, $"{records[index].Reason}; selection unavailable, included");
                }
            }
        }

        var output = new SelectOutput(set.Sources, set.Problems, records);
        var picked = output.Selected;
        await walk.SetStatusAsync(ReviewWalk.ReviewStatusContext, CodeHostStatusState.Pending, $"{picked.Count} checks selected", ct);
        var summary = $"{set.Checks.Count} checks ({set.Sources}): {candidates.Count} candidates, {picked.Count} selected"
            + (picked.Count > 0 ? $": {string.Join(", ", picked)}" : "")
            + (set.Problems.Count > 0 ? $"\n{set.Problems.Count} check files skipped: {string.Join("; ", set.Problems.Take(5))}" : "");
        var result = NodeResult.Ok(null, summary, output, usage);
        return degraded is null ? result : result.AsDegraded(degraded);
    }

    /// <summary>Grouping (design 4.5) of the checks select picked in this iteration.</summary>
    public static async Task<NodeResult> GroupChecksAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var selected = await SelectedChecksAsync(walk, ct);
        var groups = ReviewGrouping.Group(selected, walk.Policy.EffectiveBypassCategories, walk.Policy.GroupTokenBudget);
        var summary = groups.Count == 0
            ? "No checks selected: no reviewer groups"
            : $"{groups.Count} groups: " + string.Join(", ", groups.Select(g => $"{g.Key} ({g.CheckIds.Count}{(g.Bypass ? ", bypass" : "")})"));
        return NodeResult.Ok(null, summary, new GroupOutput(groups));
    }

    /// <summary>
    /// Context packs (design 4.6), kept simple: a shared prefix (PR metadata, intent, categorisation,
    /// round and steer, outstanding findings, the diff up to its budget and the changed files) that is
    /// byte-identical across the iteration's packs, then each group's checks and the output contract.
    /// Each pack is stored with its hash; the files considered for the prefix get a ledger row each.
    /// </summary>
    public static async Task<NodeResult> BuildPacksAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution execution, CancellationToken ct)
    {
        var groups = ReviewWalk.Output<GroupOutput>(walk.LatestOf("GroupChecks"))?.Groups ?? [];
        var scope = await walk.ScopeAsync(ct);
        var diff = await walk.DiffAsync(scope.Range, ct);
        var checks = (await walk.ChecksAsync(ct)).Checks.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var open = await ReviewIntakeSteps.OpenFindingsAsync(walk, ct);
        var (prefix, ledger) = Prefix(walk, scope, diff, open);
        var prefixHash = Sha(prefix);

        var db = walk.Db;
        var iteration = walk.Iteration.Iteration;
        await db.ReviewPackLedgerEntries.Where(l => l.RunId == walk.Run.Id && l.Iteration == iteration).ExecuteDeleteAsync(ct);
        foreach (var row in ledger)
            db.ReviewPackLedgerEntries.Add(row);
        var existing = await db.ReviewContextPacks.Where(p => p.RunId == walk.Run.Id && p.Iteration == iteration).ToListAsync(ct);

        var refs = new List<PackRef>();
        foreach (var group in groups)
        {
            var content = prefix + Suffix(group, checks);
            var hash = Sha(content);
            var tokens = content.Length / 4;
            var pack = existing.FirstOrDefault(p => p.GroupKey == group.Key);
            if (pack is null)
            {
                pack = new ReviewContextPack { Id = Guid.NewGuid(), RunId = walk.Run.Id, Iteration = iteration, GroupKey = group.Key };
                db.ReviewContextPacks.Add(pack);
            }

            pack.Content = content;
            pack.Hash = hash;
            pack.PrefixHash = prefixHash;
            pack.TokenEstimate = tokens;
            pack.CreatedAt = walk.Now();
            refs.Add(new PackRef(group.Key, hash, tokens));
        }

        foreach (var stale in existing.Where(p => groups.All(g => g.Key != p.GroupKey)))
            db.ReviewContextPacks.Remove(stale);

        var included = ledger.Count(l => l.Included);
        execution.InputsHash = prefixHash;
        var summary = refs.Count == 0
            ? "No reviewer groups: no packs"
            : $"{refs.Count} packs, prefix {prefixHash[..12]} ({prefix.Length / 4} tokens); ledger {included} included, {ledger.Count - included} over budget";
        return NodeResult.Ok(null, summary, new PacksOutput(prefixHash, refs, included, ledger.Count - included));
    }

    public static string AppliesName(string checkId) =>
        "applies_" + new string(checkId.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

    /// <summary>The checks select picked in this iteration, from the catalogue.</summary>
    public static async Task<IReadOnlyList<ReviewCheck>> SelectedChecksAsync(ReviewWalk walk, CancellationToken ct)
    {
        var ids = ReviewWalk.Output<SelectOutput>(walk.LatestOf("SelectChecks"))?.Selected ?? [];
        var checks = (await walk.ChecksAsync(ct)).Checks.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        return ids.Select(id => checks.GetValueOrDefault(id)).OfType<ReviewCheck>().ToList();
    }

    /// <summary>
    /// The first trigger that matches, or null. A check with no triggers is always a candidate; tags
    /// match the run's categories, symbols are matched as text in the diff. Embedding similarity
    /// (design 4.4 step 1) comes later.
    /// </summary>
    public static string? Trigger(ReviewCheck check, IReadOnlyList<CodeHostChangedFile> files, IReadOnlySet<string> languages,
        IReadOnlySet<string> tags, string diff)
    {
        var t = check.Triggers;
        if (t.IsEmpty)
            return "no triggers: always a candidate";
        if (t.Globs.Count > 0)
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddIncludePatterns(t.Globs);
            if (files.FirstOrDefault(f => matcher.Match(f.Path).HasMatches) is { } file)
                return $"glob matched {file.Path}";
        }

        if (t.Languages.FirstOrDefault(languages.Contains) is { } language)
            return $"language {language}";
        if (t.Tags.FirstOrDefault(tags.Contains) is { } tag)
            return $"tag {tag}";
        if (t.Symbols.FirstOrDefault(s => diff.Contains(s, StringComparison.Ordinal)) is { } symbol)
            return $"symbol {symbol.Trim()}";
        return null;
    }

    private static CheckSelection Record(ReviewCheck check, double? p, bool selected, string reason) =>
        new(check.Id, check.Title, check.Category, check.Severity, p, selected, reason);

    private static (string Prefix, List<ReviewPackLedgerEntry> Ledger) Prefix(ReviewWalk walk, DeltaScope scope, string? diff,
        IReadOnlyList<ReviewFinding> open)
    {
        var run = walk.Run;
        var subject = run.Subject;
        var text = new StringBuilder()
            .AppendLine("# Review context")
            .Append("Repo: ").Append(subject.Repo).Append(subject.PrNumber is { } n ? $" #{n}" : "").AppendLine()
            .Append("Title: ").AppendLine(subject.Title)
            .Append("Author: ").AppendLine(subject.AuthorLogin)
            .Append("Base: ").Append(subject.BaseRef).Append(' ').Append(walk.Iteration.BaseSha).Append("; head: ").Append(subject.HeadRef).Append(' ')
            .AppendLine(walk.Iteration.HeadSha)
            .Append("Round ").Append(run.Round).Append(", iteration ").Append(walk.Iteration.Iteration).Append(" (").Append(walk.Iteration.Trigger)
            .Append("); scope: ").AppendLine(scope.Description)
            .Append("Categories: ").AppendLine(string.Join(", ", run.Categories));
        if (run.BlastReach is { } reach)
        {
            text.Append("Blast radius (1-5): reach ").Append(reach).Append(", external contract ").Append(run.BlastExternalContract)
                .Append(", data ").Append(run.BlastData).Append(", rollback ").Append(run.BlastRollbackAtRelease).Append(", reversal ")
                .Append(run.BlastReversalAfterMonths).Append(", exposure ").Append(run.BlastExposure).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(run.SteerPrompt))
            text.Append("Human steer: ").AppendLine(run.SteerPrompt);
        if (ReviewWalk.Output<BaseDriftOutput>(walk.LatestAnywhereOf("BaseDriftCheck", e => e.Outcome == "affected" && e.Iteration < walk.Iteration.Iteration))
                is { Steer: { Length: > 0 } steer }
            && walk.Iteration.Trigger == "base-moved")
            text.Append("Base steer: ").AppendLine(steer);

        text.AppendLine().AppendLine("## Outstanding findings");
        if (open.Count == 0)
            text.AppendLine("(none)");
        foreach (var finding in open)
        {
            text.Append("- ").Append(finding.Key).Append(" [").Append(finding.Severity).Append(", ").Append(finding.Status).Append("] ")
                .Append(finding.FilePath).Append(": ").AppendLine(finding.Title);
        }

        text.AppendLine().AppendLine("## Changed files");
        foreach (var file in scope.Files)
            text.AppendLine(ReviewIntake.FileLine(file));

        var ledger = new List<ReviewPackLedgerEntry>();
        var sections = ReviewDiffAnchors.Sections(diff);
        var budget = Math.Max(1, walk.Policy.PackDiffTokenBudget) * 4;
        var used = 0;
        text.AppendLine().AppendLine("## Diff");
        if (diff is null)
            text.AppendLine("(the code host will not render this diff; review from the file list)");
        foreach (var file in scope.Files)
        {
            var section = sections.GetValueOrDefault(file.Path) ?? "";
            var fits = section.Length > 0 && used + section.Length <= budget;
            if (fits)
            {
                text.Append(section);
                used += section.Length;
            }

            ledger.Add(new ReviewPackLedgerEntry
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                Iteration = walk.Iteration.Iteration,
                Path = file.Path.Length <= 1000 ? file.Path : file.Path[..1000],
                Source = "diff",
                Reason = section.Length == 0 ? "changed, no diff text" : fits ? "changed in this scope" : "changed; over the diff budget",
                Score = 1.0,
                Tokens = section.Length / 4,
                Included = fits,
            });
        }

        return (text.ToString(), ledger);
    }

    private static string Suffix(ReviewGroup group, IReadOnlyDictionary<string, ReviewCheck> checks)
    {
        var text = new StringBuilder().AppendLine().Append("# Reviewer group: ").Append(group.Key).Append(" (").Append(group.Tier)
            .AppendLine(" tier)");
        foreach (var id in group.CheckIds)
        {
            if (!checks.TryGetValue(id, out var check))
                continue;
            text.AppendLine().Append("## Check ").Append(check.Id).Append(" [").Append(check.Severity.ToString().ToLowerInvariant()).Append("]: ")
                .AppendLine(check.Title);
            foreach (var section in check.ModelSections.Where(s => s.Name != "Selector"))
                text.Append("### ").AppendLine(section.Name).AppendLine(section.Body);
        }

        text.AppendLine().AppendLine("# Output contract")
            .AppendLine("Answer with one ABF review-findings/1.0 document (docs/specs/block-format/v1.md section 7.1).")
            .AppendLine("List every assigned check in the checks record with pass, finding, n/a or unverified:")
            .AppendLine(string.Join(", ", group.CheckIds))
            .AppendLine("Each finding anchors on one verbatim line of code at the reviewed head. Do not re-report the outstanding findings.");
        return text.ToString();
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
