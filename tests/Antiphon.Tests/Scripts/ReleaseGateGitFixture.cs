using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// One scripted answer to an ls-remote whose last argument is Ref (Set-C1339Read's reads entry): replace answers Exit and
/// Output instead of git; prepend puts Output (and a newline) before git's own answer. Ordinal 0 answers every such read,
/// otherwise only that read (1-based, counted per ref for the fixture's lifetime).
/// </summary>
public sealed record ReleaseGateGitRead(string Ref, string Output = "", string Mode = "prepend", int Ordinal = 0, int Exit = 0);

/// <summary>An independent writer's annotated tag on the bare remote (the fault file's pushRace and inventoryRace).</summary>
public sealed record ReleaseGateGitRace(string Tag, string Sha);

/// <summary>
/// The fault file's journalCut: at a nonempty answer to the read whose last argument is Ref (the final tag's direct ref,
/// observed after the push), the publication journal at JournalPath loses its reservations (remove) or its first row's
/// sha becomes Sha (replace).
/// </summary>
public sealed record ReleaseGateJournalCut(string Ref, string JournalPath, string Mode, string Sha);

/// <summary>
/// scripts/fixtures/c1339-release-git.ps1's fault file as an in-memory object: push before (refused without pushing) or
/// after (refused after git pushed), pushRace (a conflicting tag lands on the remote before each push), journalCut,
/// inventoryRace (a tag lands right after the first inventory read), reads, and the journal writer fault (throw or silent,
/// Install-C1339WriterFault). Null on the fixture is no fault file.
/// </summary>
public sealed class ReleaseGateGitFaults
{
    public string Push { get; init; } = "";
    public ReleaseGateGitRace? PushRace { get; init; }
    public ReleaseGateJournalCut? JournalCut { get; init; }
    public ReleaseGateGitRace? InventoryRace { get; init; }
    public IReadOnlyList<ReleaseGateGitRead> Reads { get; init; } = [];
    public string Writer { get; init; } = "";
}

/// <summary>One real git child (Invoke-C1339GitProcess): the exit code, stdout trimmed, stderr.</summary>
public sealed record ReleaseGateGitRun(int ExitCode, string Output, string Error);

/// <summary>
/// Unit 14 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the C# twin of scripts/test-release-gate.ps1's
/// CARD-1339 real-git fixture (New-C1339FixtureSeed and its helpers) and of the confined transport adapter
/// scripts/fixtures/c1339-release-git.ps1 (Assert-C1339ContainedPath, Invoke-C1339GitProcess, Invoke-C1339FixtureGit),
/// for the publisher's and promoter's C1339 cases that moved in process (ReleaseTagAllocatorTests).
///
/// It composes a ReleaseGateFixture (Fx: the real policy checkout, the frozen authority, the RC green set, the in-memory
/// GitHub recipient and the loopback release card) built with the base policy for repository fixture/release, and adds a
/// real local bare remote (Bare, &lt;root&gt;/remote.git) holding the candidate ref and two further real commits B and C
/// (refs/heads/fixture-objects). Fx.Git is the adapter (Transport): it admits only the git forms the publisher and
/// promoter use, rewrites origin to the bare remote, traces every admitted call (GitTrace, the real-git-trace.log lines)
/// and applies Faults; every git child runs with the credential, provider, hook, signing and protocol environment
/// scrubbed. This process's own environment is never changed. The publisher's clock is injected (2026-10-15T12:00:00Z,
/// or SetClock). Every case builds its own fixture; the PowerShell seeded once per kind and cloned per case.
/// </summary>
public sealed class ReleaseGateGitFixture : IDisposable
{
    /// <summary>The seed's publisher clock.</summary>
    public const string SeedUtc = "2026-10-15T12:00:00Z";

    /// <summary>The fixture's publication destination (the authority's and the checkout origin's).</summary>
    public const string FixtureRepository = "fixture/release";

    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly Regex Scrubbed = new("TOKEN|SECRET|PASSWORD|CREDENTIAL|ANTIPHON|CODEX|CLAUDE|OPENAI|GH_|GITHUB|GIT_|ASKPASS", RegexOptions.IgnoreCase);
    private static readonly Regex RefForm = new(@"^(refs/(heads|tags)/|rc/|release/)[A-Za-z0-9./^{}-]+$", RegexOptions.IgnoreCase);
    private static readonly Regex PrivatePushSpec = new(@"^refs/antiphon-release-push/[a-f0-9]{32}/\*:refs/tags/\*$");
    private static readonly Regex PushSpec = new(@"^(refs/tags/[A-Za-z0-9./-]+|[a-f0-9]{40}:refs/heads/[A-Za-z0-9./-]+)$", RegexOptions.IgnoreCase);
    private static readonly Regex AnyCaseSha = new("^[a-f0-9]{40}$", RegexOptions.IgnoreCase);
    private static readonly Regex Sha = new("^[a-f0-9]{40}$");
    private static readonly Regex ShowRefForm = new(@"^refs/tags/[A-Za-z0-9.-]+$", RegexOptions.IgnoreCase);
    private static readonly Regex PrivateRef = new(@"^refs/antiphon-release-push/[a-f0-9]{32}/v[0-9.rc-]+$");
    private static readonly Regex GitWrite = new("^(tag|push) ", RegexOptions.IgnoreCase);
    private static readonly Regex GitHubWrite = new("^GH release-(create|upload|publish)", RegexOptions.IgnoreCase);

    private readonly object _gate = new();
    private readonly List<string> _trace = [];
    private readonly Dictionary<string, int> _readCounts = new(StringComparer.Ordinal);
    private bool _inventoryRaceFired;
    private int _clockSamples;
    private int _runClockReads;

    private ReleaseGateGitFixture(ReleaseGateFixture fx)
    {
        Fx = fx;
        Bare = Path.Combine(fx.Root, "remote.git");
    }

    /// <summary>The composed publisher fixture.</summary>
    public ReleaseGateFixture Fx { get; }

    /// <summary>The fixture root.</summary>
    public string Root => Fx.Root;

    /// <summary>The real local bare remote origin is rewritten to.</summary>
    public string Bare { get; }

    /// <summary>A real commit on top of Head (distinct B); the promotion fixture's checkout is detached here.</summary>
    public string B { get; private set; } = "";

    /// <summary>A real commit on top of B (distinct C).</summary>
    public string C { get; private set; } = "";

    /// <summary>The long-lived candidate's first pre-release tag.</summary>
    public string PreTag { get; } = "v2026.10.15.1-rc.1";

    /// <summary>PreTag's final spelling.</summary>
    public string FinalTag { get; } = "v2026.10.15.1";

    public string Checkout => Fx.Checkout;
    public string ReleaseRoot => Fx.ReleaseRoot;
    public string Head => Fx.Head;
    public string CandidateId => Fx.CandidateId;
    public string CandidateRef => Fx.CandidateRef;
    public string Repository => Fx.Repository;
    public string PublicationsPath => Fx.PublicationsPath;

    /// <summary>The fault file (null: none). Replaced as a whole, as the driver rewrote the file.</summary>
    public ReleaseGateGitFaults? Faults { get; set; }

    /// <summary>The adapter's admitted calls, their arguments joined by spaces (real-git-trace.log).</summary>
    public IReadOnlyList<string> GitTrace
    {
        get { lock (_gate) return _trace.ToArray(); }
    }

    /// <summary>Clear-Content of real-git-trace.log.</summary>
    public void ClearTrace()
    {
        lock (_gate) _trace.Clear();
    }

    /// <summary>The reads of a SetClock clock so far (clock.log's lines).</summary>
    public int ClockSamples => Volatile.Read(ref _clockSamples);

    // ---- building --------------------------------------------------------------------------------------------------

    /// <summary>
    /// New-C1339FixtureSeed (and New-C1339Fixture): the base-policy publisher fixture for fixture/release (long-lived unless
    /// stamp), the publisher clock at 2026-10-15T12:00:00Z, the bare remote holding the candidate at Head (unless
    /// noCandidateRemote) and C at refs/heads/fixture-objects, the checkout detached at Head again, a long-lived candidate's
    /// cutUtc 2026-10-15T00:00:00Z, and the adapter bound as the git seam.
    /// </summary>
    public static ReleaseGateGitFixture Create(bool stamp = false, bool noCandidateRemote = false)
    {
        var fx = new ReleaseGateGitFixture(ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes(), longLived: !stamp, repository: FixtureRepository));
        try
        {
            var seed = Instant(SeedUtc);
            fx.Fx.ClockSource = () => seed;
            Directory.CreateDirectory(fx.Bare);
            File.WriteAllText(Path.Combine(fx.Root, "empty-git-config"), "");
            fx.LocalGit(["init", "--bare", "-q"], remote: true);
            if (!noCandidateRemote) fx.LocalGit(["push", fx.Bare, fx.Head + ":refs/heads/" + fx.CandidateRef]);
            fx.LocalGit(["commit", "--allow-empty", "-q", "-m", "distinct B"]);
            fx.B = fx.LocalGit(["rev-parse", "HEAD"]);
            fx.LocalGit(["commit", "--allow-empty", "-q", "-m", "distinct C"]);
            fx.C = fx.LocalGit(["rev-parse", "HEAD"]);
            fx.LocalGit(["push", fx.Bare, fx.C + ":refs/heads/fixture-objects"]);
            // Keep the checkout at A for publication; the other commits remain real and addressable.
            fx.LocalGit(["checkout", "--detach", "-q", fx.Head]);
            if (!stamp) ReleaseGateFixture.UpdateJson(fx.Fx.JournalPath, c => c["cutUtc"] = "2026-10-15T00:00:00Z");
            fx.Fx.Git = fx.Transport;
            return fx;
        }
        catch
        {
            fx.Dispose();
            throw;
        }
    }

    /// <summary>
    /// New-C1339PromotionFixture: the candidate's reservation published as fixture-pre, the pre-release tag on the remote at
    /// Head, the GitHub store holding that one non-draft release, and the checkout detached at B (not the promoted SHA).
    /// </summary>
    public static ReleaseGateGitFixture CreatePromotion()
    {
        var fx = Create();
        try
        {
            fx.InitializePromotionReservation();
            fx.AddTag(fx.PreTag, fx.Head);
            fx.Fx.GitHub.Seed(new FakeReleaseGitHub.StoredRelease { Id = "fixture-pre", TagName = fx.PreTag, IsDraft = false });
            fx.LocalGit(["checkout", "--detach", "-q", fx.B]);
            return fx;
        }
        catch
        {
            fx.Dispose();
            throw;
        }
    }

    /// <summary>Initialize-C1339PromotionReservation: the candidate's reservation (cut 2026-10-15) published as fixture-pre.</summary>
    public void InitializePromotionReservation()
    {
        var journals = ReleaseGateJournal.Production();
        journals.Reserve(PublicationsPath, CandidateId, Head, new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), []);
        journals.MarkPublished(PublicationsPath, CandidateId, "fixture-pre", "https://example.invalid/fixture/pre");
    }

    /// <summary>
    /// Set-C1339Candidate: a new long-lived candidate rc-2026-10-15-&lt;sha12&gt; at sha on the same ref - its authority, RC
    /// green set and release card - with the remote ref moved to sha and the checkout detached there.
    /// </summary>
    public void SetCandidate(string sha)
    {
        var candidateId = "rc-2026-10-15-" + sha[..12];
        var auth = Fx.NewAuthority(sha, candidateId, CandidateRef);
        Fx.NewRcGreen(sha, candidateId, CandidateRef, auth.PolicyHash);
        Fx.CandidateId = candidateId;
        Fx.Head = sha;
        Fx.UpdateCard(store: api => api.Cards[ReleaseGateFixture.ReleaseCardId] = ReleaseGateFixture.Card(candidateId, sha));
        LocalGit(["push", Bare, sha + ":refs/heads/" + CandidateRef]);
        LocalGit(["checkout", "--detach", "-q", sha]);
    }

    /// <summary>
    /// Set-C1339Clock: the publisher's clock answers utc at a run's first read and nextUtc (default utc) at every later read
    /// of that run; every read is counted (ClockSamples). The driver's seams file was loaded afresh by each run, which reset
    /// its read counter, so the Publish and Promote wrappers reset the per-run count.
    /// </summary>
    public void SetClock(string utc, string nextUtc = "")
    {
        if (nextUtc.Length == 0) nextUtc = utc;
        var first = Instant(utc);
        var next = Instant(nextUtc);
        Fx.ClockSource = () =>
        {
            Interlocked.Increment(ref _clockSamples);
            return Interlocked.Increment(ref _runClockReads) > 1 ? next : first;
        };
    }

    /// <summary>Set-C1339Read: the fault file replaced by one scripted read (and an optional push fault).</summary>
    public void SetRead(string reference, string output = "", string mode = "prepend", int ordinal = 0, int exit = 0, string push = "") =>
        Faults = new ReleaseGateGitFaults { Push = push, Reads = [new ReleaseGateGitRead(reference, output, mode, ordinal, exit)] };

    /// <summary>
    /// Install-C1339WriterFault: the journal writer seam. A write of publications.json under a writer fault throws
    /// (fixture-atomic-write-failure) or is silently dropped; every other write, and an unfaulted one, is the production
    /// atomic replace.
    /// </summary>
    public void InstallWriterFault()
    {
        var journal = Path.GetFullPath(PublicationsPath);
        Fx.JournalWrite = (path, text) =>
        {
            if (string.Equals(Path.GetFullPath(path), journal, StringComparison.Ordinal) && Faults is { } fault)
            {
                if (Ci(fault.Writer, "throw")) throw new IOException("fixture-atomic-write-failure");
                if (Ci(fault.Writer, "silent")) return;
            }
            ReleaseGateJournal.WriteAtomic(path, text);
        };
    }

    // ---- running ---------------------------------------------------------------------------------------------------

    /// <summary>Invoke-C1339Publish: the production publisher over this fixture for fixture/release (WhatIf when asked).</summary>
    public ReleasePublishResult Publish(bool whatIf = false)
    {
        Interlocked.Exchange(ref _runClockReads, 0);
        return Fx.Publish(o =>
        {
            o.Repository = FixtureRepository;
            o.WhatIf = whatIf;
        }).Result;
    }

    /// <summary>Invoke-C1339Promote: the production promoter of PreTag for fixture/release; edit changes the options last.</summary>
    public ReleasePromoteResult Promote(Action<ReleasePromoteOptions>? edit = null)
    {
        Interlocked.Exchange(ref _runClockReads, 0);
        return Fx.Promote(PreTag, o =>
        {
            o.Repository = FixtureRepository;
            edit?.Invoke(o);
        }).Result;
    }

    /// <summary>
    /// Invoke-C1339AuthorityVariant: mutate, then a promotion must refuse (exit not 0, neither promoted nor resumed) with
    /// reason among its comma-separated refusals (ignoring case), write nothing remote, leave the recipients and the
    /// publication journal unchanged. The snapshot is restored in finally.
    /// </summary>
    public ReleaseGateCheck AuthorityVariant(ReleaseGateSnapshot snapshot, Action mutate, string reason, Action<ReleasePromoteOptions>? edit = null)
    {
        try
        {
            mutate();
            var journal = File.ReadAllText(PublicationsPath);
            var before = RecipientState();
            var r = Promote(edit);
            var refusals = (r.Refusal ?? "").Split(',');
            return new ReleaseGateCheck(r.ExitCode != 0 && !r.Promoted && !r.Resumed && refusals.Contains(reason, StringComparer.OrdinalIgnoreCase) && NoWrites()
                && RecipientState() == before && File.ReadAllText(PublicationsPath) == journal, reason + ": " + r.Refusal);
        }
        finally { Fx.Restore(snapshot); }
    }

    /// <summary>
    /// Invoke-C1339ReservationVariant: mutate, then a promotion must refuse with exactly reason (a throw counts as
    /// threw:&lt;message&gt;), write nothing remote and leave the recipients and the journal (or its absence) unchanged. The
    /// snapshot is restored in finally.
    /// </summary>
    public ReleaseGateCheck ReservationVariant(ReleaseGateSnapshot snapshot, Action mutate, string reason)
    {
        try
        {
            var path = PublicationsPath;
            mutate();
            var journal = File.Exists(path) ? File.ReadAllText(path) : "<absent>";
            var before = RecipientState();
            ReleasePromoteResult r;
            try { r = Promote(); }
            catch (Exception e) { r = new ReleasePromoteResult { ExitCode = 1, Refusal = "threw:" + e.Message }; }
            var after = File.Exists(path) ? File.ReadAllText(path) : "<absent>";
            return new ReleaseGateCheck(r.ExitCode != 0 && !r.Promoted && !r.Resumed && r.Refusal == reason && NoWrites() && RecipientState() == before && after == journal,
                reason + ": " + r.Refusal);
        }
        finally { Fx.Restore(snapshot); }
    }

    // ---- observing -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Invoke-C1339LocalGit: one git child outside the adapter (untraced, unfaulted) in the checkout or, with remote, in the
    /// bare remote; a failure throws unless allowAbsent. The trimmed stdout.
    /// </summary>
    public string LocalGit(string[] arguments, bool remote = false, bool allowAbsent = false)
    {
        var run = RunGitProcess(Root, remote ? Bare : Checkout, arguments);
        if (run.ExitCode != 0 && !allowAbsent) throw new InvalidOperationException("C1339 fixture git: " + run.Error);
        return run.Output;
    }

    /// <summary>Add-C1339Tag: an annotated tag written straight into the bare remote (an independent recipient seed).</summary>
    public void AddTag(string tag, string sha) => LocalGit(["tag", "-a", tag, sha, "-m", "fixture"], remote: true);

    /// <summary>Get-C1339Peel: the remote tag's peeled commit through exact show-ref, '' when the tag is absent.</summary>
    public string Peel(string tag)
    {
        var obj = LocalGit(["show-ref", "--verify", "--hash", "refs/tags/" + tag], remote: true, allowAbsent: true);
        if (!ReleaseGateCredit.FullSha(obj)) return "";
        return LocalGit(["rev-parse", "--verify", obj + "^{}"], remote: true, allowAbsent: true);
    }

    /// <summary>Test-C1339NoWrites: no tag or push reached the adapter and no GitHub release write reached the recipient.</summary>
    public bool NoWrites() =>
        !GitTrace.Any(line => GitWrite.IsMatch(line)) && !Fx.GitHub.Trace.Any(line => GitHubWrite.IsMatch(line));

    /// <summary>Get-C1339RecipientState: the remote's tags, the checkout's tags and the GitHub store, read independently.</summary>
    public string RecipientState() => string.Join("\n",
        LocalGit(["for-each-ref", "--format=%(refname):%(objectname)", "refs/tags"], remote: true),
        LocalGit(["for-each-ref", "--format=%(refname):%(objectname)", "refs/tags"]),
        Fx.GitHub.State());

    /// <summary>Get-C1339PreReleaseState: the pre-release tag object, its peel and the GitHub store.</summary>
    public string PreReleaseState() =>
        LocalGit(["show-ref", "--verify", "--hash", "refs/tags/" + PreTag], remote: true) + "/" + Peel(PreTag) + "/" + Fx.GitHub.State();

    // ---- the confined transport ------------------------------------------------------------------------------------

    /// <summary>
    /// Assert-C1339ContainedPath: path must lie strictly under root (ordinal), and neither it nor any ancestor (to the
    /// filesystem root, read fresh each time; a missing one is skipped) may be a link or junction.
    /// </summary>
    public static void AssertContained(string root, string path)
    {
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("fixture-path-outside-root");
        var cursor = full;
        while (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("fixture-linked-path");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            cursor = Path.GetDirectoryName(cursor);
        }
    }

    /// <summary>
    /// Invoke-C1339GitProcess: git in a contained working directory with the inherited credential, provider, Antiphon and
    /// git variables removed, only the file protocol, no system or global configuration, no prompt, no hooks, no signing and
    /// a fixed identity. 15 s, then the tree is killed (fixture-git-timeout).
    /// </summary>
    public static ReleaseGateGitRun RunGitProcess(string root, string workingDirectory, IReadOnlyList<string> arguments)
    {
        AssertContained(root, workingDirectory);
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Utf8, StandardErrorEncoding = Utf8, CreateNoWindow = true,
        };
        foreach (var key in start.Environment.Keys.ToArray())
            if (Scrubbed.IsMatch(key)) start.Environment.Remove(key);
        start.Environment["GIT_ALLOW_PROTOCOL"] = "file";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(root, "empty-git-config");
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        string[] options =
        [
            "-c", "core.hooksPath=" + Path.Combine(root, "no-hooks"), "-c", "commit.gpgSign=false", "-c", "tag.gpgSign=false", "-c", "user.name=c1339",
            "-c", "user.email=c1339@example.invalid",
        ];
        foreach (var argument in options.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("fixture-git-start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(true);
            process.WaitForExit();
            throw new InvalidOperationException("fixture-git-timeout");
        }
        return new ReleaseGateGitRun(process.ExitCode, stdout.GetAwaiter().GetResult().Trim(), stderr.GetAwaiter().GetResult());
    }

    /// <summary>The NightlyGit seam: Invoke-C1339FixtureGit's answer (exit code and trimmed stdout).</summary>
    public NightlyGitResult Transport(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var run = Adapt(workingDirectory, arguments);
        return new NightlyGitResult(run.ExitCode, run.Output);
    }

    /// <summary>
    /// Invoke-C1339FixtureGit: never forward a caller-supplied transport. The checkout and the bare remote must be
    /// contained, the call must run in the checkout, and only these forms pass: ls-remote --heads|--tags [--refs] origin
    /// [exact refs], push origin &lt;the private tag pattern | refs/tags/x | sha:refs/heads/x&gt;, fetch origin, tag -a &lt;tag&gt;
    /// &lt;sha&gt; -m &lt;message&gt;, rev-parse HEAD, rev-list -n 1 &lt;x&gt;, show-ref --verify --hash refs/tags/x, update-ref [-d] of a
    /// private push ref, log --no-merges --format=%h %s &lt;range&gt;; origin becomes the bare remote. Anything else throws its
    /// fixture-... refusal before git runs. Admitted calls are traced, then the faults apply.
    /// </summary>
    private ReleaseGateGitRun Adapt(string workingDirectory, IReadOnlyList<string> arguments)
    {
        AssertContained(Root, Checkout);
        AssertContained(Root, Bare);
        if (!string.Equals(Path.GetFullPath(workingDirectory), Path.GetFullPath(Checkout), StringComparison.Ordinal))
            throw new InvalidOperationException("fixture-working-directory");
        var args = arguments == null ? Array.Empty<string>() : arguments.Select(a => a ?? "").ToArray();
        var forward = (string[])args.Clone();
        string At(int index) => index < args.Length ? args[index] : "";
        var sub = At(0);
        switch (sub.ToLowerInvariant())
        {
            case "ls-remote":
            {
                if (args.Length < 3 || !(Ci(args[1], "--heads") || Ci(args[1], "--tags"))) throw new InvalidOperationException("fixture-transport-form");
                var remoteIndex = Ci(args[2], "--refs") ? 3 : 2;
                if (!string.Equals(At(remoteIndex), "origin", StringComparison.Ordinal)) throw new InvalidOperationException("fixture-transport-refused");
                foreach (var reference in args.Skip(remoteIndex + 1))
                    if (!RefForm.IsMatch(reference)) throw new InvalidOperationException("fixture-ref-form");
                forward[remoteIndex] = Bare;
                break;
            }
            case "push":
            {
                var spec = At(2);
                var tagSpec = PrivatePushSpec.IsMatch(spec);
                if (args.Length != 3 || !string.Equals(At(1), "origin", StringComparison.Ordinal) || (!tagSpec && !PushSpec.IsMatch(spec)))
                    throw new InvalidOperationException("fixture-push-form");
                forward[1] = Bare;
                break;
            }
            case "fetch":
                if (args.Length != 2 || !string.Equals(At(1), "origin", StringComparison.Ordinal)) throw new InvalidOperationException("fixture-fetch-form");
                forward[1] = Bare;
                break;
            case "tag":
                if (args.Length != 6 || At(1) != "-a" || At(4) != "-m" || !AnyCaseSha.IsMatch(At(3))) throw new InvalidOperationException("fixture-tag-form");
                break;
            case "rev-parse":
                if (args.Length != 2 || !Ci(At(1), "HEAD")) throw new InvalidOperationException("fixture-read-form");
                break;
            case "rev-list":
                if (args.Length != 4 || !Ci(At(1), "-n") || !Ci(At(2), "1")) throw new InvalidOperationException("fixture-read-form");
                break;
            case "show-ref":
                if (args.Length != 4 || !Ci(At(1), "--verify") || !Ci(At(2), "--hash") || !ShowRefForm.IsMatch(At(3))) throw new InvalidOperationException("fixture-read-form");
                break;
            case "update-ref":
            {
                var refIndex = At(1) == "-d" ? 2 : 1;
                if (args.Length != 4 || !PrivateRef.IsMatch(At(refIndex)) || !Sha.IsMatch(At(3))) throw new InvalidOperationException("fixture-private-ref-form");
                break;
            }
            case "log":
                if (args.Length != 4 || !Ci(At(1), "--no-merges") || !Ci(At(2), "--format=%h %s")) throw new InvalidOperationException("fixture-read-form");
                break;
            default:
                throw new InvalidOperationException("fixture-command-refused");
        }
        lock (_gate) _trace.Add(string.Join(" ", args));
        var fault = Faults;
        var isPush = Ci(sub, "push");
        var isRead = Ci(sub, "ls-remote");
        if (isPush && Ci(fault?.Push, "before")) return new ReleaseGateGitRun(1, "", "");
        if (isPush && fault?.PushRace is { } pushRace) Race(pushRace, "fixture-push-race: ");
        var r = RunGitProcess(Root, workingDirectory, forward);
        if (isRead && fault?.JournalCut is { } cut && string.Equals(args[^1], cut.Ref, StringComparison.Ordinal) && r.Output.Length > 0)
        {
            // A nonempty final direct ref is observed after the push. Change only the persisted row at the final
            // verification callback; never replace a gate.
            CutJournal(cut);
        }
        if (isRead && args.Contains("--refs", StringComparer.OrdinalIgnoreCase) && fault?.InventoryRace is { } inventoryRace && !_inventoryRaceFired)
        {
            // An independent writer accepts a tag after the inventory snapshot.
            Race(inventoryRace, "fixture-race: ");
            _inventoryRaceFired = true;
        }
        if (isPush && Ci(fault?.Push, "after")) return new ReleaseGateGitRun(1, "", "");
        if (isRead && fault != null)
        {
            var reference = args[^1];
            foreach (var response in fault.Reads)
            {
                if (!string.Equals(reference, response.Ref, StringComparison.Ordinal)) continue;
                int ordinal;
                lock (_gate) ordinal = _readCounts[reference] = (_readCounts.TryGetValue(reference, out var seen) ? seen : 0) + 1;
                if (response.Ordinal != 0 && response.Ordinal != ordinal) continue;
                if (Ci(response.Mode, "replace")) return new ReleaseGateGitRun(response.Exit, response.Output, "");
                if (Ci(response.Mode, "prepend")) return new ReleaseGateGitRun(r.ExitCode, response.Output + "\n" + r.Output, r.Error);
            }
        }
        return r;
    }

    // An independent annotated tag written straight into the bare remote.
    private void Race(ReleaseGateGitRace race, string prefix)
    {
        var raced = RunGitProcess(Root, Bare, ["tag", "-a", race.Tag, race.Sha, "-m", "fixture-race"]);
        if (raced.ExitCode != 0) throw new InvalidOperationException(prefix + raced.Error);
    }

    private void CutJournal(ReleaseGateJournalCut cut)
    {
        AssertContained(Root, cut.JournalPath);
        var journal = ReleaseGateFixture.ReadObject(cut.JournalPath);
        if (Ci(cut.Mode, "remove")) journal["reservations"] = new JsonArray();
        else if (Ci(cut.Mode, "replace")) journal["reservations"]![0]!["sha"] = cut.Sha;
        else throw new InvalidOperationException("fixture-journal-cut-mode");
        ReleaseGateJournal.WriteAtomic(cut.JournalPath, ReleaseGateJournal.Text(journal));
    }

    private static bool Ci(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>[datetime]'...' read as an instant: the UTC instant the text names (an offset is honoured, none is UTC).</summary>
    public static DateTime Instant(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcDateTime;

    public void Dispose() => Fx.Dispose();
}
