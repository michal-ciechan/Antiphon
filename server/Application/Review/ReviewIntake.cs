using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review;

/// <summary>What intake read from the code host: the PR with its pinned SHAs, and its changed files.</summary>
public sealed record IntakeSnapshot(CodeHostPullRequest PullRequest, IReadOnlyList<CodeHostChangedFile> Files)
{
    public int Additions => Files.Sum(f => f.Additions);
    public int Deletions => Files.Sum(f => f.Deletions);
    public string SizeBucket => ReviewIntake.SizeBucket(Additions + Deletions);
}

/// <summary>
/// Intake (design 4.1) as far as this slice goes: resolves the subject's code host, pins the base
/// and head SHAs and lists the changed files. Mirrors, policy at the base SHA, areas, languages and
/// code owners come later. The intake node's summary carries the size bucket and the file list.
/// </summary>
public sealed class ReviewIntake(CodeHostRouter router)
{
    public const string NodeId = "intake";

    /// <summary>Files listed in a node summary before it says how many more there are.</summary>
    public const int SummaryFileLimit = 200;

    public Task<IntakeSnapshot> ReadAsync(ReviewHost host, string repo, int? prNumber, string? baseRef, string? headRef, CancellationToken ct) =>
        ReadAsync(router.Resolve(host, repo, prNumber, baseRef, headRef), ct);

    public Task<IntakeSnapshot> ReadAsync(ReviewSubject subject, CancellationToken ct) => ReadAsync(router.Resolve(subject), ct);

    private static async Task<IntakeSnapshot> ReadAsync(CodeHostBinding binding, CancellationToken ct)
    {
        var pr = await binding.Host.GetPullRequestAsync(binding.Target, ct);
        var files = await binding.Host.ListChangedFilesAsync(binding.Target, new CodeHostRange(pr.BaseSha, pr.HeadSha), ct);
        return new IntakeSnapshot(pr, files);
    }

    /// <summary>
    /// Pins the snapshot's refs and SHAs on the subject and the iteration, and completes the intake
    /// node with the summary. The title and author are the caller's: a manual title wins at creation.
    /// </summary>
    public static void Apply(ReviewSubject subject, ReviewIteration iteration, ReviewNodeExecution node, IntakeSnapshot snapshot, DateTime now,
        string? note = null)
    {
        var pr = snapshot.PullRequest;
        subject.BaseRef = pr.BaseRef;
        subject.HeadRef = pr.HeadRef;
        subject.BaseSha = pr.BaseSha;
        subject.HeadSha = pr.HeadSha;
        if (pr.Url is not null)
            subject.Url = pr.Url;
        iteration.BaseSha = pr.BaseSha;
        iteration.HeadSha = pr.HeadSha;
        node.Kind = ReviewNodeKind.Code;
        node.Status = ReviewNodeStatus.Succeeded;
        node.StartedAt ??= now;
        node.CompletedAt = now;
        node.Outcome = null;
        node.Summary = Summary(snapshot.Files, $"base {Short(pr.BaseSha)}, head {Short(pr.HeadSha)}", note);
    }

    /// <summary>
    /// <c>12 files, +340 -20, size M</c>, the detail, the note, then one line per file (status, path,
    /// stats) up to <see cref="SummaryFileLimit"/>.
    /// </summary>
    public static string Summary(IReadOnlyList<CodeHostChangedFile> files, string? detail = null, string? note = null)
    {
        var additions = files.Sum(f => f.Additions);
        var deletions = files.Sum(f => f.Deletions);
        var text = new StringBuilder()
            .Append(files.Count).Append(files.Count == 1 ? " file" : " files")
            .Append(", +").Append(additions).Append(" -").Append(deletions)
            .Append(", size ").Append(SizeBucket(additions + deletions));
        if (!string.IsNullOrWhiteSpace(detail))
            text.Append(", ").Append(detail);
        if (!string.IsNullOrWhiteSpace(note))
            text.Append('\n').Append(note);
        foreach (var file in files.Take(SummaryFileLimit))
            text.Append('\n').Append(FileLine(file));
        if (files.Count > SummaryFileLimit)
            text.Append("\n... and ").Append(files.Count - SummaryFileLimit).Append(" more");
        return text.ToString();
    }

    public static string FileLine(CodeHostChangedFile file)
    {
        var path = file.PreviousPath is { Length: > 0 } previous && previous != file.Path ? $"{previous} -> {file.Path}" : file.Path;
        return $"{StatusLetter(file.Status)} {path} (+{file.Additions} -{file.Deletions})";
    }

    /// <summary>Changed lines (added plus deleted): XS under 10, S under 50, M under 250, L under 1000, else XL.</summary>
    public static string SizeBucket(int changedLines) => changedLines switch
    {
        < 10 => "XS",
        < 50 => "S",
        < 250 => "M",
        < 1000 => "L",
        _ => "XL",
    };

    public static string Short(string? sha) => string.IsNullOrEmpty(sha) ? "?" : sha.Length <= 7 ? sha : sha[..7];

    private static char StatusLetter(CodeHostFileStatus status) => status switch
    {
        CodeHostFileStatus.Added => 'A',
        CodeHostFileStatus.Modified => 'M',
        CodeHostFileStatus.Deleted => 'D',
        CodeHostFileStatus.Renamed => 'R',
        CodeHostFileStatus.Copied => 'C',
        _ => '?',
    };
}

/// <summary>Wakes the workflow executor when a run has work, so it need not wait for its next poll.</summary>
public sealed class ReviewExecutorSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Wake()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    /// <summary>Waits until woken or <paramref name="timeout"/> passes. Throws only when <paramref name="ct"/> is cancelled.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}
