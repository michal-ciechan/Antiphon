using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// The review engine's browser E2E scenario: a real local git repository served by the real
/// <c>LocalGitCodeHost</c>, and scripted stand-ins for the model seams the engine calls (decisions,
/// the reviewer agents and the verifier). Everything else is the real host, executor and Postgres.
/// The fakes are trimmed copies of <c>tests/Antiphon.Tests/Review/ReviewEndToEndFakes.cs</c>.
/// </summary>
internal sealed class ReviewEngineScenario : IDisposable
{
    public const string Repo = "acme/widgets";
    public const string Author = "dana";
    public const string AuthorName = "Dana Author";
    public const string Reviewer = "lee";
    public const string ReviewerName = "Lee Reviewer";
    public const string LeaseAnchor = "finally { _lock.Release(); }";
    public const string FindingTitle = "The lock is released before the flush completes";

    public ReviewEngineScenario()
    {
        Git = new LocalReviewRepo();
        Decisions.Categorise(externalContract: 0.9).Applies().Yes("still_present_f1", 0.1);
        Reviewer1.Script(1, new ScriptedReviewFinding("SEED-0004", "major", 0.9, "src/Lease.cs", LeaseAnchor, FindingTitle,
            "The release runs while the save's flush is still pending."));
    }

    public LocalReviewRepo Git { get; }
    public E2EScriptedDecisions Decisions { get; } = new();
    public E2EScriptedReviewer Reviewer1 { get; } = new();
    public E2EScriptedCompletions Completions { get; } = new();

    /// <summary>The review section: a Local repo row, the viewer header, display names and a fast executor poll.</summary>
    public void Configure(Dictionary<string, string?> settings)
    {
        settings["Review:CodeHosts:Repos:0:Repo"] = Repo;
        settings["Review:CodeHosts:Repos:0:LocalPath"] = Git.Path;
        settings["Review:DefaultViewerLogin"] = Author;
        settings["Review:AllowViewerHeader"] = "true";
        settings["Review:DemoSeedEnabled"] = "false";
        settings[$"Review:Viewers:{Author}:DisplayName"] = AuthorName;
        settings[$"Review:Viewers:{Reviewer}:DisplayName"] = ReviewerName;
        settings["Review:Executor:PollSeconds"] = "1";
    }

    /// <summary>Swaps the model seams for the scripted ones; the local git code host stays real.</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IDecisionModel>();
        services.AddSingleton<IDecisionModel>(Decisions);
        services.RemoveAll<IReviewAgentRunner>();
        services.AddSingleton<IReviewAgentRunner>(Reviewer1);
        services.RemoveAll<ICompletionModel>();
        services.AddSingleton<ICompletionModel>(Completions);
        services.PostConfigure<ReviewCompletionSettings>(s =>
        {
            s.Providers["scripted-verifier"] = new ReviewCompletionProvider { Dialect = "scripted", Model = E2EScriptedCompletions.Model };
            s.Chains["completions-cheap"] = [new ReviewDecisionChainEntry { Provider = "scripted-verifier" }];
        });
    }

    public void Dispose() => Git.Dispose();
}

/// <summary>
/// A throwaway repository: <c>main</c> holds the base commit, <c>feature</c> the change under review.
/// LF line endings and a repo-local identity, so the diff the engine reads is the same on every host.
/// </summary>
internal sealed class LocalReviewRepo : IDisposable
{
    public const string BaseBranch = "main";
    public const string HeadBranch = "feature";

    private static readonly string BaseLease = """
        namespace Widgets;

        public sealed class Lease
        {
            private readonly SemaphoreSlim _lock = new(1, 1);
            private readonly Db _db = new();
            private bool _released;

            public async Task SaveAsync(CancellationToken ct)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        """.ReplaceLineEndings("\n");

    public LocalReviewRepo()
    {
        Path = System.IO.Path.Combine(Directory.CreateTempSubdirectory("antiphon-e2e-review-").FullName, "widgets");
        Directory.CreateDirectory(Path);
        Run("init", "--initial-branch", BaseBranch);
        Run("config", "core.autocrlf", "false");
        Run("config", "user.name", "Dana Author");
        Run("config", "user.email", "dana@widgets.test");
        Write("README.md", "# Widgets\n");
        Write("src/Lease.cs", BaseLease);
        Commit("Add the lease");
        BaseSha = Run("rev-parse", "HEAD").Trim();

        Run("checkout", "-b", HeadBranch);
        Write("src/Lease.cs", Changed(BaseLease,
            "        await _db.SaveChangesAsync(ct);\n",
            "        try { await _db.SaveChangesAsync(ct); }\n        finally { _lock.Release(); }\n        _released = true;\n"));
        Write("tests/LeaseTests.cs", "// covers the release\npublic class LeaseTests { }\n");
        HeadSha = Commit("Release the lease after the save");
    }

    public string Path { get; }
    public string BaseSha { get; }
    public string HeadSha { get; private set; }

    /// <summary>The author's fix on <c>feature</c>: the release now waits for the flush. Returns the new head.</summary>
    public string PushFix()
    {
        var lease = File.ReadAllText(System.IO.Path.Combine(Path, "src", "Lease.cs"));
        Write("src/Lease.cs", Changed(lease, ReviewEngineScenario.LeaseAnchor, "finally { await _db.FlushAsync(ct); _lock.Release(); }"));
        HeadSha = Commit("Flush before releasing the lease");
        return HeadSha;
    }

    /// <summary>Replaces <paramref name="from"/>, failing loudly when it is absent: a no-op edit would make an empty commit.</summary>
    private static string Changed(string text, string from, string to) =>
        text.Contains(from, StringComparison.Ordinal) ? text.Replace(from, to, StringComparison.Ordinal)
            : throw new InvalidOperationException($"The scenario source has no '{from.Trim()}' to change.");

    private void Write(string relative, string text)
    {
        var path = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
    }

    private string Commit(string message)
    {
        Run("add", "-A");
        Run("commit", "-q", "-m", message);
        return Run("rev-parse", "HEAD").Trim();
    }

    private string Run(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = Path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"git {string.Join(' ', args)} did not finish in 30s");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} exited {process.ExitCode}: {stderr.Result}");
        return stdout.Result;
    }

    public void Dispose()
    {
        var root = Directory.GetParent(Path)!.FullName;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
        }
    }
}

/// <summary>A decision model with scripted answers per question name; an unscripted question is refused.</summary>
internal sealed class E2EScriptedDecisions : IDecisionModel
{
    public const string Model = "jev-scripted-1";

    private readonly ConcurrentDictionary<string, DecisionAnswer> _answers = new(StringComparer.Ordinal);

    public ConcurrentQueue<DecisionRequest> Requests { get; } = new();

    public bool Serves(string dialect) => true;

    public E2EScriptedDecisions Yes(string name, double probability)
    {
        _answers[name] = new DecisionAnswer(name, DecisionQuestionKind.Noul, probability >= 0.5 ? "yes" : "no", Math.Max(probability, 1 - probability),
            new Dictionary<string, double> { ["yes"] = probability, ["no"] = 1 - probability }, false);
        return this;
    }

    public E2EScriptedDecisions Pick(string name, string value, DecisionQuestionKind kind = DecisionQuestionKind.Score)
    {
        _answers[name] = new DecisionAnswer(name, kind, value, 0.9, null, false);
        return this;
    }

    /// <summary>The categorise answers (design 4.3).</summary>
    public E2EScriptedDecisions Categorise(double externalContract)
    {
        Pick("change_kind", "bugfix", DecisionQuestionKind.Choice);
        Yes("external_contract", externalContract);
        Yes("persistent_data", 0.1);
        Pick("reach", "A few");
        Pick("rollback_at_release", "Easy");
        Pick("reversal_after_months", "Easy");
        Pick("exposure", "Low");
        return this;
    }

    /// <summary>The seed checks' <c>applies</c> answers, as in the API end-to-end test's first PR.</summary>
    public E2EScriptedDecisions Applies() =>
        Yes("applies_seed_0001", 0.9).Yes("applies_seed_0002", 0.6).Yes("applies_seed_0003", 0.05).Yes("applies_seed_0004", 0.8)
            .Yes("applies_seed_0005", 0.2).Yes("applies_seed_0006", 0.7).Yes("applies_seed_0007", 0.1);

    public Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct)
    {
        Requests.Enqueue(request);
        var answers = request.Questions.Where(q => _answers.ContainsKey(q.Name)).ToDictionary(q => q.Name, q => _answers[q.Name]);
        return Task.FromResult(new DecisionResult(answers, new DecisionUsage(100 * request.Questions.Count, 0), 0.0001m * request.Questions.Count,
            "actual", new DecisionServedBy(options.ProviderId, "Http", Model)));
    }
}

/// <summary>A finding a scripted reviewer reports.</summary>
internal sealed record ScriptedReviewFinding(string Check, string Severity, double Confidence, string File, string Anchor, string Title, string Body);

/// <summary>
/// The reviewer agents: per round, findings that a group reports only when the finding's check is
/// one of the group's assigned checks, so the script does not depend on how checks are grouped.
/// Every other assigned check passes.
/// </summary>
internal sealed class E2EScriptedReviewer : IReviewAgentRunner
{
    private readonly ConcurrentDictionary<int, ScriptedReviewFinding[]> _scripts = new();

    public ConcurrentQueue<ReviewAgentRequest> Requests { get; } = new();

    public void Script(int round, params ScriptedReviewFinding[] findings) => _scripts[round] = findings;

    public Task<ReviewAgentResult> RunAsync(ReviewAgentRequest request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        var findings = (_scripts.GetValueOrDefault(request.Round) ?? [])
            .Where(f => request.AssignedChecks.Contains(f.Check, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(ReviewAgentResult.Completed(Document(request.AssignedChecks, findings),
            new ReviewAgentUsage("scripted-reviewer", "claude-scripted", ReviewTransport.Seat, 2000, 300, 0.02m, "api-equivalent")));
    }

    private static string Document(IReadOnlyList<string> assigned, IReadOnlyList<ScriptedReviewFinding> findings)
    {
        var text = new StringBuilder("@abf 1 review-findings/1.0\n");
        for (var i = 0; i < findings.Count; i++)
        {
            var f = findings[i];
            text.Append("=== finding F").Append(i + 1).Append('\n')
                .Append("check: ").Append(f.Check).Append('\n')
                .Append("severity: ").Append(f.Severity).Append('\n')
                .Append("confidence: ").Append(f.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n')
                .Append("file: ").Append(f.File).Append('\n')
                .Append("anchor: ").Append(f.Anchor).Append('\n')
                .Append("title: ").Append(f.Title).Append('\n')
                .Append("body: ").Append(f.Body).Append('\n');
        }

        text.Append("=== checks\n");
        foreach (var check in assigned)
        {
            var ids = findings.Select((f, i) => (f, i)).Where(x => x.f.Check == check).Select(x => $"F{x.i + 1}").ToList();
            text.Append(check).Append(ids.Count > 0 ? $" finding {string.Join(",", ids)}" : " pass").Append('\n');
        }

        return text.Append("=== end\n").ToString();
    }
}

/// <summary>The verifier: holds every finding (p 0.9), answering the request it was sent.</summary>
internal sealed partial class E2EScriptedCompletions : ICompletionModel
{
    public const string Model = "verify-scripted-1";

    public bool Serves(string dialect) => dialect == "scripted";

    [GeneratedRegex("request (?<id>verify-[0-9a-f]+-\\d+)")]
    private static partial Regex RequestId();

    [GeneratedRegex("holds_c\\d+")]
    private static partial Regex Holds();

    public Task<CompletionResult> CompleteAsync(CompletionRequest request, CompletionOptions options, CancellationToken ct)
    {
        var text = new StringBuilder("@abf 1 decision-response/1.0\n=== response ").Append(RequestId().Match(request.Instructions).Groups["id"].Value).Append('\n');
        foreach (var name in Holds().Matches(request.Instructions).Select(m => m.Value).Distinct())
            text.Append("=== answer ").Append(name).Append("\nvalue: 0.9\nconfidence: 0.8\n");
        text.Append("=== end\n");
        return Task.FromResult(new CompletionResult(text.ToString(), new DecisionUsage(500, 50), 0.001m, "actual",
            new DecisionServedBy(options.ProviderId, "Http", Model)));
    }
}
