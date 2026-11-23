using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;

namespace Antiphon.Tests.Scripts;

/// <summary>One publisher or promoter run: its record and the [publish-release] / [promote-release] lines it wrote.</summary>
public sealed record ReleaseGateRun<T>(T Result, IReadOnlyList<string> Lines);

/// <summary>Get-C599AGate's answer: the gate's verdict, the gate's and the credit verdict's reasons, the ledger's suite rows.</summary>
public sealed record ReleaseGateAnswer(bool Ok, IReadOnlyList<string> Reasons, IReadOnlyList<OrderedDictionary> Suites);

/// <summary>One matrix variant's outcome (Invoke-C599AVariant's { Pass, Detail }).</summary>
public sealed record ReleaseGateCheck(bool Pass, string Detail);

/// <summary>
/// Unit 14 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the C# twin of scripts/test-release-gate.ps1's
/// publisher fixture (New-C599PublishFx and its helpers), for the publish-release and promote-release cases that moved in
/// process (ReleasePublishTests, ReleasePromoteTests, ReleaseGateAuthorityContractTests, ReleaseGatePublicationAuthorityTests,
/// ReleaseTagAllocatorTests). CARD-0599 D-15: the release clone is a real git repository whose commit holds the policy, so
/// the candidate SHA is real and the publisher re-reads the pinned blob itself; the authority, plan and ledger are frozen by
/// the production ReleaseAuthority and every chunk has a real TRX. The remote (FakeReleaseRemote) and GitHub
/// (FakeReleaseGitHub) are in-memory recipients behind the seams, read by separate observers; the release card is served over
/// real loopback HTTP (ReleaseCardApi) and read back by the production ReleaseAuthority.ReadReleaseCard. The publisher's
/// clock is injected (Now), never the wall clock. Every run passes TestMode and both fakes, so nothing real is reached.
/// </summary>
public sealed class ReleaseGateFixture : IDisposable
{
    public const string StampCandidateId = "rc-20260923T083000Z";
    public const string StampCandidateRef = "release/rc-20260923T083000Z";
    public const string LongLivedRef = "rc/2026-10-15";
    public const string IntentId = "intent-20260923T083000Z";
    public const string RunId = "rc-run-1";
    public const string ReleaseCardId = "5b0e6c1e-8f1c-4c3e-9a55-0c5990c5990a";
    public const string DefaultRepository = "michal-ciechan/Antiphon";
    public static readonly string ShaA = new('a', 40);
    public static readonly string ShaB = new('b', 40);
    public static readonly string ShaC = new('c', 40);

    /// <summary>The injected publisher clock: after every fixture instant, never the wall clock.</summary>
    public static readonly DateTime Now = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>The eight rc suites a schema-2 policy requires.</summary>
    public static readonly string[] EightSuites = ["antiphon", "session-runner", "pty-host", "agents-pty", "messaging", "client", "scripts", "e2e"];

    /// <summary>Get-C599Discovery's native suites (the live policy's rc set without the two roster suites).</summary>
    public static readonly string[] NativeSuites =
        ["antiphon", "session-runner", "pty-host", "agents-pty", "messaging", "e2e", "markdown-pdf", "observability", "server-git", "resilience", "git-in-process"];

    /// <summary>The declared roster entries of the client and scripts suites.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Rosters = new Dictionary<string, string[]>
    {
        ["client"] = ["lint", "build", "vitest"], ["scripts"] = ["test-release-gate.ps1", "test-nightly.ps1"],
    };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private readonly NightlyFixture _root;

    private ReleaseGateFixture(NightlyFixture root, string repository)
    {
        _root = root;
        Repository = repository;
        Remote = new FakeReleaseRemote();
        GitHub = new FakeReleaseGitHub();
        Git = Remote.Run;
        ClockSource = () => Now;
    }

    /// <summary>The repository root (the worktree under test).</summary>
    public static string Repo => NightlyPlanCases.Repo;

    /// <summary>The scripts directory whose pinned scripts and source trees the authority binds.</summary>
    public static string ScriptsRoot => Path.Combine(Repo, "scripts");

    /// <summary>The fixture root.</summary>
    public string Root => _root.Root;

    /// <summary>A leaf of the root, unique per fixture (the card API's route).</summary>
    public string RouteId { get; } = Guid.NewGuid().ToString("N");

    public string ReleaseRoot => Path.Combine(Root, "releases");
    public string Checkout => Path.Combine(Root, "checkout");
    public string Head { get; set; } = "";
    public string CandidateId { get; set; } = StampCandidateId;
    public string CandidateRef { get; set; } = StampCandidateRef;
    public string Repository { get; }

    /// <summary>The file-backed remote's in-memory twin; its Run is the default git seam.</summary>
    public FakeReleaseRemote Remote { get; }

    /// <summary>The stateful GitHub fixture: write acceptance is separate from stored state.</summary>
    public FakeReleaseGitHub GitHub { get; }

    /// <summary>The release card as the Antiphon API serves it.</summary>
    public ReleaseCardApi CardApi { get; private set; } = null!;

    /// <summary>The git seam handed to the publisher and promoter (Remote.Run unless a case binds a real-git adapter).</summary>
    public NightlyGit Git { get; set; }

    /// <summary>The journal writer seam (null: the atomic replace).</summary>
    public Action<string, string>? JournalWrite { get; set; }

    /// <summary>The injected clock's answers (default: Now); ClockReads counts the reads.</summary>
    public Func<DateTime> ClockSource { get; set; }

    private int _clockReads;

    /// <summary>How often the publisher's clock seam was read.</summary>
    public int ClockReads => Volatile.Read(ref _clockReads);

    public void ResetClockReads() => Interlocked.Exchange(ref _clockReads, 0);

    public string PathOf(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());
    public string CandidateRoot => ReleaseGateCredit.CandidateRoot(ReleaseRoot, CandidateId);
    public string AuthorityDir => ReleaseAuthority.Dir(CandidateRoot);
    public string AuthorityPath => Path.Combine(AuthorityDir, "authority.json");
    public string PlanPath => Path.Combine(AuthorityDir, "execution-plan.json");
    public string LedgerPath => Path.Combine(AuthorityDir, "ledger.json");
    public string DiscoveryPath => Path.Combine(AuthorityDir, "discovery.json");
    public string PrivatePolicyPath => Path.Combine(AuthorityDir, "policy.json");
    public string PublicationAuthorityPath => Path.Combine(AuthorityDir, "publication-authority.json");
    public string GreenPath => Path.Combine(CandidateRoot, "complete-green.json");
    public string JournalPath => Path.Combine(CandidateRoot, "candidate.json");
    public string SummaryPath => Path.Combine(CandidateRoot, "summary.json");
    public string ManifestPath => Path.Combine(CandidateRoot, "release-manifest.json");
    public string PublishJournalPath => Path.Combine(CandidateRoot, "publish.json");
    public string NotesPath => Path.Combine(CandidateRoot, "release-notes.md");
    public string PublicationsPath => Path.Combine(ReleaseRoot, "publications.json");
    public string CheckoutPolicyPath => Path.Combine(Checkout, "tests", "test-execution-policy.json");

    // ---- building --------------------------------------------------------------------------------------------------

    /// <summary>
    /// New-C599PublishFx: a real policy checkout (policyBytes, else the live policy) at origin
    /// https://github.com/&lt;repository&gt;, the frozen authority (unless noAuthority), the RC green set, the remote holding
    /// the candidate at the tested sha and the release card at revision 4. longLived names the candidate rc/2026-10-15 at
    /// rc-2026-10-15-&lt;sha12&gt; instead of the legacy stamp.
    /// </summary>
    public static ReleaseGateFixture Create(byte[]? policyBytes = null, bool noAuthority = false, bool longLived = false, string repository = DefaultRepository)
    {
        var fx = new ReleaseGateFixture(NightlyFixture.Create("c599-gate"), repository);
        try
        {
            Directory.CreateDirectory(Path.Combine(fx.Checkout, "tests"));
            File.WriteAllBytes(fx.CheckoutPolicyPath, policyBytes ?? File.ReadAllBytes(Path.Combine(Repo, "tests", "test-execution-policy.json")));
            fx.RealGit("init", "-q");
            fx.RealGit("config", "core.autocrlf", "false");
            fx.RealGit("remote", "add", "origin", "https://github.com/" + repository);
            fx.RealGit("add", "--", "tests/test-execution-policy.json");
            fx.RealGit("-c", "user.name=c599", "-c", "user.email=c599@example.invalid", "commit", "-q", "-m", "policy");
            fx.Head = fx.RealGit("rev-parse", "HEAD");
            NightlyFixture.WriteJson(Path.Combine(fx.Checkout, ".antiphon-release-owned"), new { kind = "release-clone" });
            if (longLived)
            {
                fx.CandidateRef = LongLivedRef;
                fx.CandidateId = "rc-2026-10-15-" + fx.Head[..12];
            }
            fx.Remote.HeadSha = fx.Head;
            fx.Remote.Replace((fx.CandidateRef, fx.Head));
            var policyHash = noAuthority ? "policy-1" : fx.NewAuthority(fx.Head, fx.CandidateId, fx.CandidateRef).PolicyHash;
            fx.NewRcGreen(fx.Head, fx.CandidateId, fx.CandidateRef, policyHash);
            fx.CardApi = new ReleaseCardApi(fx.RouteId);
            fx.CardApi.Cards[ReleaseCardId] = Card(fx.CandidateId, fx.Head);
            return fx;
        }
        catch
        {
            fx.Dispose();
            throw;
        }
    }

    /// <summary>The release card the receipt names (revision 4, a body correlated to this intent, candidate, SHA and run).</summary>
    public static JsonObject Card(string candidateId, string sha) => new()
    {
        ["id"] = ReleaseCardId, ["identifier"] = "CARD-0601", ["title"] = "Release " + candidateId, ["description"] = CardBody(sha, candidate: candidateId),
        ["revisionCount"] = 4, ["status"] = "Review", ["concurrencyToken"] = "0b0e6c1e-8f1c-4c3e-9a55-0c5990c59900",
    };

    /// <summary>Get-C599CardBody: the release card body carrying a run's correlation line (D-14 readback).</summary>
    public static string CardBody(string sha, string intentId = IntentId, string candidate = StampCandidateId, string runId = RunId) =>
        "Release candidate " + candidate + "\n\nrelease-correlation: intentId=" + intentId + " candidateId=" + candidate + " sha=" + sha + " runId=" + runId
        + "\n\nTests: all eight suites passed.";

    /// <summary>A git command in the checkout through ReleaseAuthority.Git; throws on failure; the trimmed output.</summary>
    public string RealGit(params string[] arguments)
    {
        var run = ReleaseAuthority.Git(Checkout, arguments);
        if (run.ExitCode != 0) throw new InvalidOperationException("fixture git " + string.Join(" ", arguments) + " failed: " + run.Error);
        return run.Text.Trim();
    }

    /// <summary>Get-C599Discovery: two classes per native suite (antiphon three chunks) plus the pinned-excluded nodes.</summary>
    public static Hashtable Discovery()
    {
        var discovery = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var suite in NativeSuites)
        {
            var tag = Regex.Replace(suite, "[^a-z]", "");
            var nodes = new List<object>
            {
                new Hashtable { ["uid"] = suite + "-u1", ["class"] = tag + "AlphaTests", ["method"] = "One" },
                new Hashtable { ["uid"] = suite + "-u2", ["class"] = tag + "AlphaTests", ["method"] = "Two" },
                new Hashtable { ["uid"] = suite + "-u3", ["class"] = tag + "BetaTests", ["method"] = "Three(1)" },
            };
            if (suite == "antiphon")
            {
                nodes.Add(new Hashtable { ["uid"] = "antiphon-u4", ["class"] = "antiphonGammaTests", ["method"] = "Four" });
                nodes.Add(new Hashtable { ["uid"] = "antiphon-x1", ["class"] = "ClaudeAdapterIntegrationTests", ["method"] = "Live" });
            }
            if (suite == "e2e") nodes.Add(new Hashtable { ["uid"] = "e2e-x1", ["class"] = "DelegationPipelineE2ETests", ["method"] = "Live" });
            discovery[suite] = nodes.ToArray();
        }
        return discovery;
    }

    /// <summary>
    /// New-C599Authority: freeze the authority and plan for the candidate through the production functions, lay down one real
    /// TRX per frozen chunk and the ledger that binds them (the prepublication receipt names the release card, revision 4).
    /// </summary>
    public AuthorityRecord NewAuthority(string sha, string candidateId, string candidateRef)
    {
        var root = ReleaseGateCredit.CandidateRoot(ReleaseRoot, candidateId);
        var auth = ReleaseAuthority.Freeze(Checkout, root, sha, Repository, candidateId, candidateRef, IntentId, ReleaseCardId,
            new DateTime(2026, 9, 23, 8, 30, 30, DateTimeKind.Utc), ScriptsRoot);
        var discovery = Discovery();
        var rosters = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, entries) in Rosters) rosters[id] = entries.Cast<object>().ToArray();
        var plan = ReleaseAuthority.Plan(root, discovery, rosters);
        var dir = ReleaseAuthority.Dir(root);
        var byUid = new Dictionary<string, Hashtable>(StringComparer.Ordinal);
        foreach (DictionaryEntry suite in discovery) foreach (Hashtable n in (object[])suite.Value!) byUid[(string)n["uid"]!] = n;
        var chunks = new List<object>();
        var minute = 0;
        foreach (IDictionary suite in (IEnumerable)plan["suites"]!)
        {
            if ((string?)suite["kind"] != "native") continue;
            foreach (IDictionary c in (IEnumerable)suite["chunks"]!)
            {
                minute++;
                var rel = "evidence/" + c["id"] + ".trx";
                var trx = Path.Combine(root, rel);
                var rows = ((IEnumerable)c["uids"]!).Cast<object>()
                    .Select(u => new NightlyTrxRow((string)u, (string)byUid[(string)u]["class"]!, (string)byUid[(string)u]["method"]!)).ToArray();
                NightlyFixture.WriteTrx(trx, rows);
                var start = new DateTime(2026, 9, 23, 8, 31, 0, DateTimeKind.Utc).AddMinutes(minute);
                chunks.Add(new OrderedDictionary
                {
                    { "suite", suite["id"] }, { "chunk", c["id"] }, { "intentId", IntentId }, { "candidateId", candidateId }, { "sha", sha }, { "runId", RunId },
                    { "exitCode", 0 }, { "startedAt", start.ToString("o", CultureInfo.InvariantCulture) },
                    { "completedAt", start.AddSeconds(30).ToString("o", CultureInfo.InvariantCulture) }, { "trx", rel }, { "trxSha256", ReleaseAuthority.FileSha256(trx) },
                    { "executed", rows.Length }, { "passed", rows.Length }, { "failed", 0 }, { "skipped", 0 },
                });
            }
        }
        var rosterRows = Rosters.Keys.Select(id => (object)new OrderedDictionary
        {
            { "suite", id }, { "intentId", IntentId }, { "candidateId", candidateId }, { "sha", sha }, { "runId", RunId }, { "exitCode", 0 },
            { "startedAt", "2026-09-23T09:30:00Z" }, { "completedAt", "2026-09-23T09:35:00Z" },
            { "entries", Rosters[id].Select(e => (object)new OrderedDictionary { { "id", e }, { "outcome", "passed" } }).ToArray() },
        }).ToArray();
        NightlyFixture.WriteJson(Path.Combine(dir, "ledger.json"), new OrderedDictionary
        {
            { "schemaVersion", 1 }, { "intentId", IntentId }, { "candidateId", candidateId }, { "candidateRef", candidateRef }, { "sha", sha }, { "runId", RunId },
            { "authorityDigest", ReleaseAuthority.FileSha256(Path.Combine(dir, "authority.json")) },
            { "executionPlanDigest", ReleaseAuthority.FileSha256(Path.Combine(dir, "execution-plan.json")) },
            { "startedAt", "2026-09-23T08:31:00Z" }, { "completedAt", "2026-09-23T09:50:00Z" }, { "teardownSucceeded", true }, { "seamed", false },
            { "noReport", false }, { "diagnostic", false }, { "selection", "full" },
            { "prepublicationReceipt", new OrderedDictionary
                {
                    { "kind", "prepublication" }, { "recipient", new OrderedDictionary { { "kind", "release-card" }, { "cardId", ReleaseCardId }, { "revision", 4 } } },
                    { "correlation", new OrderedDictionary { { "intentId", IntentId }, { "candidateId", candidateId }, { "sha", sha }, { "runId", RunId } } },
                    { "acceptedAt", "2026-09-23T09:52:00Z" },
                } },
            { "buildHash", "build-1" }, { "bundleHash", "bundle-1" },
            { "prerequisites", new object[]
                {
                    new OrderedDictionary { { "id", "build" }, { "exitCode", 0 }, { "succeeded", true } },
                    new OrderedDictionary { { "id", "client-bundle" }, { "exitCode", 0 }, { "succeeded", true } },
                    new OrderedDictionary { { "id", "script-census" }, { "exitCode", 0 }, { "succeeded", true } },
                } },
            { "chunks", chunks.ToArray() }, { "rosters", rosterRows },
        });
        return auth;
    }

    /// <summary>New-C599RcGreen: the candidate journal, summary and complete-green, all on the same candidate, SHA and policy hash.</summary>
    public void NewRcGreen(string sha, string candidateId, string candidateRef, string policyHash)
    {
        var root = ReleaseGateCredit.CandidateRoot(ReleaseRoot, candidateId);
        Directory.CreateDirectory(root);
        NightlyFixture.WriteJson(Path.Combine(root, "candidate.json"), new OrderedDictionary
        {
            { "schemaVersion", 1 }, { "candidateId", candidateId }, { "ref", candidateRef }, { "sha", sha }, { "cutUtc", "2026-09-23T08:30:00Z" },
            { "scheduleSlot", "u/lndcobra/antiphon_release_candidates" }, { "pushed", true },
        });
        var summaryPath = Path.Combine(root, "summary.json");
        NightlyFixture.WriteJson(summaryPath, new OrderedDictionary
        {
            { "profile", "rc" }, { "policyHash", policyHash }, { "requiredSuites", new object[] { "antiphon", "e2e" } },
            { "suites", new object[]
                {
                    new OrderedDictionary { { "id", "antiphon" }, { "chunk", "all" }, { "result", "pass" }, { "exitCode", 0 }, { "skipped", false }, { "requiredUidCount", 12 },
                        { "excludedUidCount", 1 }, { "censusDigest", "d1" }, { "exclusions", Array.Empty<object>() } },
                    new OrderedDictionary { { "id", "e2e" }, { "chunk", "all" }, { "result", "pass" }, { "exitCode", 0 }, { "skipped", false }, { "requiredUidCount", 8 },
                        { "excludedUidCount", 3 }, { "censusDigest", "d2" },
                        { "exclusions", new object[] { new OrderedDictionary { { "uid", "u1" }, { "class", "LiveCanaryTests" }, { "reason", "real headed provider" }, { "owner", "CARD-0599" } } } } },
                } },
        });
        NightlyFixture.WriteJson(Path.Combine(root, "complete-green.json"), new OrderedDictionary
        {
            { "runId", RunId }, { "policyHash", policyHash }, { "profile", "rc" }, { "trigger", "rc" }, { "ref", candidateRef }, { "coverageComplete", true },
            { "testsPassed", true }, { "reportDelivered", true }, { "exitCode", 0 }, { "sha", sha }, { "expectedSha", sha }, { "candidateId", candidateId },
            { "startedAt", "2026-09-23T08:31:00Z" }, { "completedAt", "2026-09-23T09:50:00Z" }, { "summaryPath", summaryPath }, { "capabilities", new object[] { "land-v2" } },
        });
    }

    // ---- running ---------------------------------------------------------------------------------------------------

    /// <summary>The seams a -SeamsPath file handed the scripts: the git seam, the GitHub fixture, the counted clock, the journal writer.</summary>
    public ReleaseGateSeams Seams() => new()
    {
        Git = Git, GitHub = GitHub.Run, JournalWrite = JournalWrite,
        UtcNow = () =>
        {
            Interlocked.Increment(ref _clockReads);
            return ClockSource();
        },
    };

    /// <summary>A host writing to output, with the seams (null: none) and the production HTTP card readback.</summary>
    public static ReleaseGateHost Host(TextWriter output, ReleaseGateSeams? seams) => new()
    {
        Out = output, Clock = TimeProvider.System, Seams = seams, CardReader = ReleaseAuthority.ReadReleaseCard,
    };

    private static IReadOnlyList<string> Lines(StringWriter output) =>
        output.ToString().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();

    /// <summary>The publisher's options for this fixture: test mode, the candidate, the checkout, the repository and the card API.</summary>
    public ReleasePublishOptions PublishOptions(string? candidateId = null) => new()
    {
        ReleaseRoot = ReleaseRoot, CandidateId = candidateId ?? CandidateId, CheckoutRoot = Checkout, Repository = Repository, AntiphonApiUrl = CardApi.Url,
        ScriptsRoot = ScriptsRoot, TestMode = true,
    };

    /// <summary>
    /// Invoke-C599Publish: the production publisher in test mode over this fixture's seams (or the given ones). edit changes
    /// the options first. A run outside test mode without seams would reach the real remote, so it is never made.
    /// </summary>
    public ReleaseGateRun<ReleasePublishResult> Publish(Action<ReleasePublishOptions>? edit = null, string? candidateId = null, ReleaseGateSeams? seams = null,
        bool noSeams = false)
    {
        var options = PublishOptions(candidateId);
        edit?.Invoke(options);
        var bound = noSeams ? null : seams ?? Seams();
        if (!options.TestMode && bound == null) throw new InvalidOperationException("a publisher run outside test mode without seams would reach the real remote");
        var output = new StringWriter();
        var result = ReleasePublish.Run(options, Host(output, bound));
        return new ReleaseGateRun<ReleasePublishResult>(result, Lines(output));
    }

    /// <summary>The promoter's options for this fixture.</summary>
    public ReleasePromoteOptions PromoteOptions(string tag, string? candidateId = null) => new()
    {
        ReleaseRoot = ReleaseRoot, Tag = tag, CandidateId = candidateId ?? CandidateId, CheckoutRoot = Checkout, Repository = Repository,
        AntiphonApiUrl = CardApi.Url, ScriptsRoot = ScriptsRoot, TestMode = true,
    };

    /// <summary>The production promoter in test mode over this fixture's seams (or the given ones); see Publish.</summary>
    public ReleaseGateRun<ReleasePromoteResult> Promote(string tag, Action<ReleasePromoteOptions>? edit = null, string? candidateId = null, ReleaseGateSeams? seams = null,
        bool noSeams = false)
    {
        var options = PromoteOptions(tag, candidateId);
        edit?.Invoke(options);
        var bound = noSeams ? null : seams ?? Seams();
        if (!options.TestMode && bound == null) throw new InvalidOperationException("a promoter run outside test mode without seams would reach the real remote");
        var output = new StringWriter();
        var result = ReleasePromote.Run(options, Host(output, bound));
        return new ReleaseGateRun<ReleasePromoteResult>(result, Lines(output));
    }

    /// <summary>
    /// Get-C599AGate: the publisher's pre-write decision (ReleaseAuthority.Check and the publication gate) with the remote answer
    /// read from the recipient, plus the credit verdict's reasons.
    /// </summary>
    public ReleaseGateAnswer Gate()
    {
        var green = ReleaseGateHost.ReadDocument(GreenPath);
        var candidate = ReleaseGateHost.ReadDocument(JournalPath);
        var authority = ReleaseAuthority.Check(CandidateRoot, Checkout, green, candidate, Repository, Now, CardApi.Url, ScriptsRoot, ReleaseAuthority.ReadReleaseCard);
        var gate = ReleaseGatePublication.Gate(green, candidate, Remote.Read(CandidateRef), authority, "", []);
        var verdict = ReleaseGateCredit.Verdict(green);
        return new ReleaseGateAnswer(gate.Ok, gate.Reasons.Concat(verdict.Reasons).ToArray(), authority.Suites);
    }

    /// <summary>Test-C599NoRemoteWrite: the remote saw no push or tag and the GitHub recipient holds no release.</summary>
    public bool NoRemoteWrite() =>
        !Remote.Trace.Any(line => Regex.IsMatch(line, "^GIT (push|tag)")) && GitHub.Releases.Count == 0;

    /// <summary>
    /// Invoke-C599AVariant: apply mutate to the control and require a refusal naming every expected token (case-insensitive);
    /// publish drives the real publisher (with edit) and the observer instead of the gate. The snapshot is restored in finally,
    /// after cleanup.
    /// </summary>
    public ReleaseGateCheck Variant(ReleaseGateSnapshot snapshot, string label, string[] expect, Action mutate, bool publish = false,
        Action<ReleasePublishOptions>? edit = null, Action? cleanup = null)
    {
        string[] got = [];
        var refused = false;
        var error = "";
        try
        {
            mutate();
            if (publish)
            {
                var run = Publish(edit);
                got = run.Result.Refusal.Split(',', StringSplitOptions.RemoveEmptyEntries);
                refused = !run.Result.Published && run.Result.ExitCode != 0 && NoRemoteWrite();
            }
            else
            {
                var gate = Gate();
                got = gate.Reasons.ToArray();
                refused = !gate.Ok;
            }
        }
        catch (Exception e)
        {
            error = "threw: " + e.Message;
        }
        finally
        {
            try { cleanup?.Invoke(); }
            finally { Restore(snapshot); }
        }
        var missing = expect.Where(token => !got.Contains(token, StringComparer.OrdinalIgnoreCase)).ToArray();
        return new ReleaseGateCheck(refused && missing.Length == 0 && error.Length == 0,
            label + ": refused=" + refused + " missing=[" + string.Join(" ", missing) + "] got=[" + string.Join(" ", got) + "] " + error);
    }

    /// <summary>Assert-C599AMatrix: one row that passes when there is at least one check and every check passed.</summary>
    public static void Matrix(NightlyCaseRows rows, string name, IReadOnlyCollection<ReleaseGateCheck> checks) =>
        rows.Check(checks.Count > 0 && checks.All(c => c.Pass), name, string.Join(" | ", checks.Where(c => !c.Pass).Select(c => c.Detail)));

    // ---- snapshot ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Save-C599ASnapshot: every file under the release root, the checkout's policy file and the in-memory recipients (the
    /// remote with its trace, the GitHub store with its trace, the served card store).
    /// </summary>
    public ReleaseGateSnapshot Save()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(ReleaseRoot, "*", Walk)) files[file] = File.ReadAllBytes(file);
        return new ReleaseGateSnapshot(files, File.Exists(CheckoutPolicyPath) ? File.ReadAllBytes(CheckoutPolicyPath) : null, Remote.Save(), GitHub.Save(), CardApi.Save());
    }

    private static readonly EnumerationOptions Walk = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };

    /// <summary>Restore-C599ASnapshot: extra files deleted, changed or missing files (and their parents) rewritten, recipients reset.</summary>
    public void Restore(ReleaseGateSnapshot snapshot)
    {
        foreach (var file in Directory.EnumerateFiles(ReleaseRoot, "*", Walk).ToArray())
            if (!snapshot.Files.ContainsKey(file)) File.Delete(file);
        foreach (var (path, bytes) in snapshot.Files)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        if (snapshot.Policy != null) File.WriteAllBytes(CheckoutPolicyPath, snapshot.Policy);
        Remote.Restore(snapshot.Remote);
        GitHub.Restore(snapshot.GitHub);
        CardApi.Restore(snapshot.Card);
    }

    // ---- mutating the evidence ---------------------------------------------------------------------------------------

    /// <summary>A JSON file as a node (a missing file throws).</summary>
    public static JsonObject ReadObject(string path) => (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

    /// <summary>Write-NightlyAtomicJson of a node: indented UTF-8 without a BOM.</summary>
    public static void WriteNode(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, node.ToJsonString(Indented), new UTF8Encoding(false));
    }

    /// <summary>Update-C599Json.</summary>
    public static void UpdateJson(string path, Action<JsonObject> mutate)
    {
        var node = ReadObject(path);
        mutate(node);
        WriteNode(path, node);
    }

    /// <summary>[string] of a JSON value: a string as is, anything else its JSON text, null "".</summary>
    public static string Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToJsonString() ?? "";

    /// <summary>The array's entries that keep accepts, cloned into a new array (@($a | Where-Object ...)).</summary>
    public static JsonArray Where(JsonNode? array, Func<JsonNode?, bool> keep) =>
        new((array as JsonArray ?? new JsonArray()).Where(keep).Select(n => n?.DeepClone()).ToArray());

    /// <summary>The array's entries followed by extra (each cloned): @($a) + @($b).</summary>
    public static JsonArray Plus(JsonNode? array, params JsonNode?[] extra) =>
        new((array as JsonArray ?? new JsonArray()).Select(n => n?.DeepClone()).Concat(extra.Select(n => n?.DeepClone())).ToArray());

    /// <summary>New-C599AWrapped: a one-element JSON array holding the value (an array value is nested, not flattened).</summary>
    public static JsonArray Wrapped(JsonNode? value) => new(value?.DeepClone());

    /// <summary>Update-C599Ledger: the plan first (when given), then the ledger re-bound to the plan's digest and mutated.</summary>
    public void UpdateLedger(Action<JsonObject>? ledger = null, Action<JsonObject>? plan = null)
    {
        if (plan != null) UpdateJson(PlanPath, plan);
        var planDigest = ReleaseAuthority.FileSha256(PlanPath);
        UpdateJson(LedgerPath, l =>
        {
            l["executionPlanDigest"] = planDigest;
            ledger?.Invoke(l);
        });
    }

    /// <summary>Update-C599AAuthority: mutate authority.json and re-bind the plan and ledger to its new digest.</summary>
    public void UpdateAuthority(Action<JsonObject> mutate)
    {
        UpdateJson(AuthorityPath, mutate);
        var digest = ReleaseAuthority.FileSha256(AuthorityPath);
        UpdateLedger(l => l["authorityDigest"] = digest, p => p["authorityDigest"] = digest);
    }

    /// <summary>Update-C599ADiscovery: mutate discovery.json keeping the plan's discovery digest bound.</summary>
    public void UpdateDiscovery(Action<JsonObject> mutate, Action<JsonObject>? plan = null, Action<JsonObject>? ledger = null)
    {
        UpdateJson(DiscoveryPath, mutate);
        var digest = ReleaseAuthority.FileSha256(DiscoveryPath);
        UpdateLedger(ledger, p =>
        {
            p["discoveryDigest"] = digest;
            plan?.Invoke(p);
        });
    }

    /// <summary>Update-C599AChunkRow.</summary>
    public void UpdateChunkRow(string chunk, Action<JsonObject> mutate) =>
        UpdateLedger(l =>
        {
            foreach (var row in l["chunks"]!.AsArray())
                if (row is JsonObject c && Str(c["chunk"]) == chunk) mutate(c);
        });

    /// <summary>Update-C599ARosterRow.</summary>
    public void UpdateRosterRow(string suite, Action<JsonObject> mutate) =>
        UpdateLedger(l =>
        {
            foreach (var row in l["rosters"]!.AsArray())
                if (row is JsonObject r && Str(r["suite"]) == suite) mutate(r);
        });

    /// <summary>Update-C599AGreen.</summary>
    public void UpdateGreen(Action<JsonObject> mutate) => UpdateJson(GreenPath, mutate);

    /// <summary>Update-C599AReceipt: mutate the ledger's prepublication receipt, keeping every other binding valid.</summary>
    public void UpdateReceipt(Action<JsonObject> mutate) => UpdateLedger(l => mutate(l["prepublicationReceipt"]!.AsObject()));

    /// <summary>The frozen plan's native chunks: suite, chunk id and UIDs, in plan order.</summary>
    public IReadOnlyList<(string Suite, string Id, string[] Uids)> NativeChunks()
    {
        var chunks = new List<(string, string, string[])>();
        foreach (var suite in ReadObject(PlanPath)["suites"]!.AsArray())
        {
            if (Str(suite?["kind"]) != "native") continue;
            foreach (var chunk in suite!["chunks"]!.AsArray())
                chunks.Add((Str(suite["id"]), Str(chunk?["id"]), chunk!["uids"]!.AsArray().Select(Str).ToArray()));
        }
        return chunks;
    }

    /// <summary>A chunk's TRX path, from its ledger row.</summary>
    public string TrxPath(string chunk)
    {
        foreach (var row in ReadObject(LedgerPath)["chunks"]!.AsArray())
            if (Str(row?["chunk"]) == chunk) return Path.Combine(CandidateRoot, Str(row!["trx"]));
        throw new InvalidOperationException("no ledger row for chunk " + chunk);
    }

    /// <summary>A chunk's TRX rows as the production reader reads them.</summary>
    public static List<NightlyTrxRow> TrxRows(string path) =>
        NightlyTrx.ReadIdentities(path).Select(r => new NightlyTrxRow(r.TestId, r.ClassName, r.MethodName, r.Outcome)).ToList();

    /// <summary>Update-C599ATrx: rewrite a chunk's TRX rows and (unless keepBinding) re-bind its digest and all four counts.</summary>
    public void UpdateTrx(string chunk, Func<List<NightlyTrxRow>, IEnumerable<NightlyTrxRow>> rows, bool keepBinding = false)
    {
        var path = TrxPath(chunk);
        var next = rows(TrxRows(path)).ToList();
        NightlyFixture.WriteTrx(path, next);
        if (keepBinding) return;
        UpdateChunkRow(chunk, c =>
        {
            c["trxSha256"] = ReleaseAuthority.FileSha256(path);
            c["executed"] = next.Count;
            c["passed"] = next.Count(r => r.Outcome == "Passed");
            c["failed"] = next.Count(r => r.Outcome == "Failed");
            c["skipped"] = next.Count(r => r.Outcome != "Passed" && r.Outcome != "Failed");
        });
    }

    /// <summary>Set-C599TrxOutcome: one UID's outcome rewritten in its chunk's TRX, the digest and the passed/failed/skipped counts re-bound.</summary>
    public void SetTrxOutcome(string chunk, string uid, string outcome)
    {
        var path = TrxPath(chunk);
        var rows = TrxRows(path).Select(r => r.Id == uid ? r with { Outcome = outcome } : r).ToList();
        NightlyFixture.WriteTrx(path, rows);
        UpdateChunkRow(chunk, c =>
        {
            c["trxSha256"] = ReleaseAuthority.FileSha256(path);
            c["passed"] = rows.Count(r => r.Outcome == "Passed");
            c["failed"] = rows.Count(r => r.Outcome == "Failed");
            c["skipped"] = rows.Count(r => r.Outcome != "Passed" && r.Outcome != "Failed");
        });
    }

    /// <summary>
    /// Move-C599AEvidenceTime: every evidence instant (authority, run, chunks, rosters, receipt) shifted by offset with every
    /// digest re-bound; the order stays valid, so only a bound against the publisher's clock can refuse.
    /// </summary>
    public void MoveEvidenceTime(TimeSpan offset)
    {
        string Shift(JsonNode? value) => ReleaseAuthority.Instant(Str(value))!.Value.Add(offset).ToString("o", CultureInfo.InvariantCulture);
        UpdateAuthority(a => a["createdAt"] = Shift(a["createdAt"]));
        UpdateLedger(l =>
        {
            l["startedAt"] = Shift(l["startedAt"]);
            l["completedAt"] = Shift(l["completedAt"]);
            foreach (var row in l["chunks"]!.AsArray().Concat(l["rosters"]!.AsArray()))
            {
                row!["startedAt"] = Shift(row["startedAt"]);
                row["completedAt"] = Shift(row["completedAt"]);
            }
            var receipt = l["prepublicationReceipt"]!;
            receipt["acceptedAt"] = Shift(receipt["acceptedAt"]);
        });
    }

    /// <summary>Get-C599ABasePolicyBytes: the explicit historical schema-2 policy of the eight-suite authority controls.</summary>
    public static byte[] BasePolicyBytes() => File.ReadAllBytes(Path.Combine(ScriptsRoot, "fixtures", "c599-policy-v2.json"));

    /// <summary>New-C599APolicyBytes: the base policy with one edit and its canonical hash restated (unless staleHash).</summary>
    public static byte[] PolicyBytes(Action<JsonObject>? edit = null, bool staleHash = false)
    {
        var policy = (JsonObject)JsonNode.Parse(Encoding.UTF8.GetString(BasePolicyBytes()).TrimStart('﻿'))!;
        edit?.Invoke(policy);
        policy["policyHash"] = NightlyPolicy.HashText(policy.ToJsonString());
        if (staleHash) policy["policyHash"] = new string('0', 64);
        return Encoding.UTF8.GetBytes(policy.ToJsonString(Indented));
    }

    /// <summary>
    /// Set-C599APinnedPolicy: commit policyBytes on the real checkout and re-pin the whole candidate to that commit: journal,
    /// green, remote, authority blob identity, digests and hash, private copy, plan, ledger, every chunk and roster row, the
    /// receipt correlation and the release card body. Only the policy content differs.
    /// </summary>
    public void SetPinnedPolicy(byte[] policyBytes)
    {
        File.WriteAllBytes(CheckoutPolicyPath, policyBytes);
        RealGit("add", "--", "tests/test-execution-policy.json");
        RealGit("-c", "user.name=c599", "-c", "user.email=c599@example.invalid", "commit", "-q", "--allow-empty", "-m", "repin");
        var pinSha = RealGit("rev-parse", "HEAD");
        string pinHash;
        try { pinHash = NightlyPolicy.HashText(Encoding.UTF8.GetString(policyBytes)); }
        catch (Exception) { pinHash = new string('0', 64); }
        File.WriteAllBytes(PrivatePolicyPath, policyBytes);
        UpdateJson(JournalPath, c => c["sha"] = pinSha);
        UpdateGreen(g =>
        {
            g["sha"] = pinSha;
            g["expectedSha"] = pinSha;
            g["policyHash"] = pinHash;
        });
        Remote.Replace((CandidateRef, pinSha));
        UpdateJson(AuthorityPath, a =>
        {
            a["sha"] = pinSha;
            a["policyBlobId"] = ReleaseAuthority.GitBlobId(policyBytes);
            a["policyRawSha256"] = ReleaseAuthority.Sha256(policyBytes);
            a["policyHash"] = pinHash;
        });
        var digest = ReleaseAuthority.FileSha256(AuthorityPath);
        UpdateLedger(l =>
        {
            l["sha"] = pinSha;
            l["authorityDigest"] = digest;
            foreach (var row in l["chunks"]!.AsArray().Concat(l["rosters"]!.AsArray())) row!["sha"] = pinSha;
            l["prepublicationReceipt"]!["correlation"]!["sha"] = pinSha;
        }, p =>
        {
            p["sha"] = pinSha;
            p["authorityDigest"] = digest;
        });
        UpdateCard(card: c => c["description"] = CardBody(pinSha, candidate: CandidateId));
    }

    /// <summary>Update-C599Card: mutate the served store (status, raw) or the release card itself.</summary>
    public void UpdateCard(Action<JsonObject>? card = null, Action<ReleaseCardApi>? store = null)
    {
        lock (CardApi.Gate)
        {
            store?.Invoke(CardApi);
            if (card != null && CardApi.Cards.TryGetValue(ReleaseCardId, out var served)) card(served);
        }
    }

    /// <summary>
    /// New-C599AEvidenceLink: a directory link pointing at target (a junction on Windows, no privilege needed; a symbolic
    /// link elsewhere).
    /// </summary>
    public static void DirectoryLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("mklink /J failed for " + link);
            return;
        }
        Directory.CreateSymbolicLink(link, target);
    }

    public void Dispose()
    {
        CardApi?.Dispose();
        _root.Dispose();
    }
}

/// <summary>Save-C599ASnapshot's state: the release root's files, the checkout policy and the recipients.</summary>
public sealed record ReleaseGateSnapshot(IReadOnlyDictionary<string, byte[]> Files, byte[]? Policy, object Remote, object GitHub, object Card);

/// <summary>
/// The C# twin of New-C599GitSeams' file-backed fake remote: ls-remote reads it, push writes it, and Read is the separate
/// observer read that proves the recipient state rather than the request. Each call is traced "GIT &lt;arguments&gt;". A
/// failed subcommand answers exit 1; with commitsAnyway a failed push still lands (commit-then-lose-response). Thread-safe.
/// </summary>
public sealed class FakeReleaseRemote
{
    private readonly object _lock = new();
    private Dictionary<string, string> _state = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fullRefs = new(StringComparer.Ordinal);
    private Dictionary<string, bool> _fail = new(StringComparer.Ordinal);
    private List<string> _trace = [];
    private string _privateRef = "";

    /// <summary>What rev-parse, rev-list and show-ref answer, and what a pushed tag holds.</summary>
    public string HeadSha { get; set; } = new('b', 40);

    /// <summary>Get-RefKey: refs/heads/ and a peel suffix dropped, every other non-alphanumeric character '_'.</summary>
    public static string Key(string reference) =>
        Regex.Replace(Regex.Replace(Regex.Replace(reference ?? "", "^refs/heads/", "", RegexOptions.IgnoreCase), @"\^\{\}$", ""), "[^A-Za-z0-9]", "_");

    /// <summary>The calls so far.</summary>
    public IReadOnlyList<string> Trace
    {
        get { lock (_lock) return _trace.ToArray(); }
    }

    /// <summary>Read-C599Remote: the value stored under the reference's key ('' when none).</summary>
    public string Read(string reference)
    {
        lock (_lock) return _state.TryGetValue(Key(reference), out var value) ? value : "";
    }

    /// <summary>Stores a reference's value.</summary>
    public void Set(string reference, string sha)
    {
        lock (_lock) _state[Key(reference)] = sha;
    }

    /// <summary>Replaces the whole remote state with these references (Write-NightlyAtomicJson of the remote file).</summary>
    public void Replace(params (string Reference, string Sha)[] refs)
    {
        lock (_lock)
        {
            _state = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (reference, sha) in refs) _state[Key(reference)] = sha;
        }
    }

    /// <summary>Set-C599GitFailure: the subcommand fails from now on; commitsAnyway lands a push before failing.</summary>
    public void Fail(string subcommand, bool commitsAnyway = false)
    {
        lock (_lock) _fail[subcommand] = commitsAnyway;
    }

    /// <summary>Set-C599GitFailure -Clear.</summary>
    public void Clear(string subcommand)
    {
        lock (_lock) _fail.Remove(subcommand);
    }

    /// <summary>The NightlyGit seam.</summary>
    public NightlyGitResult Run(string workingDirectory, IReadOnlyList<string> arguments)
    {
        lock (_lock)
        {
            _trace.Add("GIT " + string.Join(" ", arguments));
            var sub = arguments.Count > 0 ? arguments[0] : "";
            if (_fail.TryGetValue(sub, out var commits))
            {
                if (commits && sub == "push") Push(arguments);
                return new NightlyGitResult(1, "injected failure");
            }
            switch (sub)
            {
                case "fetch":
                case "clone":
                case "tag":
                    return new NightlyGitResult(0, "");
                case "rev-parse":
                case "rev-list":
                case "show-ref":
                    return new NightlyGitResult(0, HeadSha);
                case "update-ref":
                    _privateRef = arguments.Count > 1 && arguments[1] == "-d" ? "" : arguments.Count > 1 ? arguments[1] : "";
                    return new NightlyGitResult(0, "");
                case "log":
                    return new NightlyGitResult(0, "abc1234 feat: something");
                case "ls-remote":
                    if (arguments.Contains("--refs"))
                        return new NightlyGitResult(0, string.Join("\n", _fullRefs.Where(r => r.Key.StartsWith("refs/tags/", StringComparison.Ordinal)).Select(r => r.Value + "\t" + r.Key)));
                    var reference = arguments[^1];
                    return _state.TryGetValue(Key(reference), out var value) && !string.IsNullOrWhiteSpace(value)
                        ? new NightlyGitResult(0, value + "\t" + reference)
                        : new NightlyGitResult(0, "");
                case "push":
                    Push(arguments);
                    return new NightlyGitResult(0, "");
                default:
                    return new NightlyGitResult(0, "");
            }
        }
    }

    // A push: the private pattern refspec stands for the tag it maps; a tag source holds HeadSha.
    private void Push(IReadOnlyList<string> arguments)
    {
        var spec = arguments.Count > 2 ? arguments[2] : "";
        if (spec.StartsWith("refs/antiphon-release-push/", StringComparison.Ordinal))
            spec = Regex.Replace(_privateRef, "^refs/antiphon-release-push/[^/]+/", "refs/tags/");
        var parts = spec.Split(':');
        var target = parts[^1];
        var value = parts[0].StartsWith("refs/tags/", StringComparison.OrdinalIgnoreCase) ? HeadSha : parts[0];
        _state[Key(target)] = value;
        if (target.StartsWith("refs/", StringComparison.Ordinal)) _fullRefs[target] = value;
    }

    internal object Save()
    {
        lock (_lock) return (new Dictionary<string, string>(_state), new Dictionary<string, string>(_fullRefs), new Dictionary<string, bool>(_fail), _trace.ToList(), _privateRef);
    }

    internal void Restore(object saved)
    {
        var (state, fullRefs, fail, trace, privateRef) = ((Dictionary<string, string>, Dictionary<string, string>, Dictionary<string, bool>, List<string>, string))saved;
        lock (_lock)
        {
            _state = new Dictionary<string, string>(state);
            _fullRefs = new Dictionary<string, string>(fullRefs);
            _fail = new Dictionary<string, bool>(fail);
            _trace = trace.ToList();
            _privateRef = privateRef;
        }
    }
}

/// <summary>
/// The C# twin of New-C599GitHubSeams: a stateful GitHub recipient. Write acceptance is separate from stored state, and a
/// release-view readback is the only thing that proves publication. Each call is traced "GH &lt;verb&gt; &lt;tag&gt;[ --prerelease]".
/// A failed key (a verb, or release-upload:&lt;asset name&gt;) answers exit 1; with commitsAnyway the write lands first.
/// </summary>
public sealed class FakeReleaseGitHub
{
    /// <summary>One stored release.</summary>
    public sealed class StoredRelease
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string TagName { get; set; } = "";
        public string Url { get; set; } = "";
        public bool IsDraft { get; set; }
        public List<string> Assets { get; set; } = [];

        public StoredRelease Copy() => new() { Id = Id, Name = Name, TagName = TagName, Url = Url, IsDraft = IsDraft, Assets = Assets.ToList() };

        /// <summary>gh release view's JSON object as the production seam parses it.</summary>
        public OrderedDictionary Body() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "id", Id }, { "name", Name }, { "url", Url }, { "isDraft", IsDraft }, { "tagName", TagName }, { "assets", Assets.Cast<object>().ToArray() },
        };
    }

    private readonly object _lock = new();
    private List<StoredRelease> _releases = [];
    private Dictionary<string, bool> _fail = new(StringComparer.Ordinal);
    private List<string> _trace = [];

    /// <summary>The stored releases (copies).</summary>
    public IReadOnlyList<StoredRelease> Releases
    {
        get { lock (_lock) return _releases.Select(r => r.Copy()).ToArray(); }
    }

    /// <summary>The calls so far.</summary>
    public IReadOnlyList<string> Trace
    {
        get { lock (_lock) return _trace.ToArray(); }
    }

    /// <summary>Get-C599Release: the stored release for the tag (a copy), or null.</summary>
    public StoredRelease? Find(string tag)
    {
        lock (_lock) return _releases.FirstOrDefault(r => r.TagName == tag)?.Copy();
    }

    /// <summary>Seeds the store with a release.</summary>
    public void Seed(StoredRelease release)
    {
        lock (_lock) _releases.Add(release.Copy());
    }

    /// <summary>Set-C599GitHubFailure.</summary>
    public void Fail(string key, bool commitsAnyway = false)
    {
        lock (_lock) _fail[key] = commitsAnyway;
    }

    /// <summary>Set-C599GitHubFailure -Clear.</summary>
    public void Clear(string key)
    {
        lock (_lock) _fail.Remove(key);
    }

    /// <summary>The store as JSON text: an independent observer of the recipient state.</summary>
    public string State()
    {
        lock (_lock)
            return JsonSerializer.Serialize(_releases.Select(r => new { r.Id, r.Name, r.TagName, r.Url, r.IsDraft, r.Assets }));
    }

    /// <summary>The ReleaseGateGitHub seam.</summary>
    public ReleaseGateGitHubResult Run(string verb, IDictionary arguments, string workingDirectory)
    {
        lock (_lock)
        {
            string Arg(string name) => arguments != null && arguments.Contains(name) ? PsValue.Str(arguments[name]) : "";
            var tag = Arg("tag");
            var prerelease = arguments != null && arguments.Contains("prerelease") && PsValue.IsTrue(arguments["prerelease"]);
            _trace.Add("GH " + verb + " " + tag + (prerelease ? " --prerelease" : ""));
            var key = verb == "release-upload" ? "release-upload:" + Path.GetFileName(Arg("asset")) : verb;
            var shouldFail = _fail.TryGetValue(key, out var commits);
            var failed = new ReleaseGateGitHubResult(1, "", verb, null);
            var stored = _releases.FirstOrDefault(r => r.TagName == tag);
            switch (verb)
            {
                case "release-view":
                    if (shouldFail || stored == null) return failed;
                    return new ReleaseGateGitHubResult(0, "", verb, stored.Body());
                case "release-create":
                    if (shouldFail && !commits) return failed;
                    if (stored == null)
                        _releases.Add(new StoredRelease { Id = "R-" + tag, Name = tag, TagName = tag, Url = "https://example/releases/tag/" + tag, IsDraft = true });
                    return shouldFail ? failed : new ReleaseGateGitHubResult(0, "", verb, null);
                case "release-upload":
                    if (shouldFail && !commits) return failed;
                    stored?.Assets.Add(Path.GetFileName(Arg("asset")));
                    return shouldFail ? failed : new ReleaseGateGitHubResult(0, "", verb, null);
                case "release-publish":
                    if (shouldFail && !commits) return failed;
                    if (stored != null) stored.IsDraft = false;
                    return shouldFail ? failed : new ReleaseGateGitHubResult(0, "", verb, null);
                default:
                    throw new InvalidOperationException("unknown verb " + verb);
            }
        }
    }

    internal object Save()
    {
        lock (_lock) return (_releases.Select(r => r.Copy()).ToList(), new Dictionary<string, bool>(_fail), _trace.ToList());
    }

    internal void Restore(object saved)
    {
        var (releases, fail, trace) = ((List<StoredRelease>, Dictionary<string, bool>, List<string>))saved;
        lock (_lock)
        {
            _releases = releases.Select(r => r.Copy()).ToList();
            _fail = new Dictionary<string, bool>(fail);
            _trace = trace.ToList();
        }
    }
}

/// <summary>
/// The fake Antiphon API of the D-14 readback over real loopback HTTP (the driver's Get-C599CardStub): one listener per
/// fixture answers GET /fx/&lt;route&gt;/api/cards/&lt;id&gt; from this store, so each fixture's card is its own recipient state; every
/// request line is logged for the read-only proof. A non-200 Status or a Raw body replaces the card answer; a method other
/// than GET answers 405.
/// </summary>
public sealed class ReleaseCardApi : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Thread _thread;
    private readonly List<string> _requests = [];

    public ReleaseCardApi(string route)
    {
        Route = route;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Url = "http://127.0.0.1:" + Port + "/fx/" + route;
        _thread = new Thread(Serve) { IsBackground = true, Name = "c599-card-api" };
        _thread.Start();
    }

    /// <summary>The store's lock.</summary>
    public object Gate { get; } = new();

    public string Route { get; }
    public int Port { get; }

    /// <summary>The URL a run reads the card from (a variant may point it elsewhere).</summary>
    public string Url { get; set; }

    /// <summary>The answer status (200: the card).</summary>
    public int Status { get; set; } = 200;

    /// <summary>A raw body that replaces the card answer (null: none).</summary>
    public string? Raw { get; set; }

    /// <summary>The cards by requested id.</summary>
    public Dictionary<string, JsonObject> Cards { get; private set; } = new(StringComparer.Ordinal);

    /// <summary>The request lines so far.</summary>
    public IReadOnlyList<string> Requests
    {
        get { lock (Gate) return _requests.ToArray(); }
    }

    /// <summary>The request line a GET of the bound card sends.</summary>
    public string ExpectedLine(string cardId) => "GET /fx/" + Route + "/api/cards/" + cardId + " HTTP/1.1";

    public void ClearRequests()
    {
        lock (Gate) _requests.Clear();
    }

    internal object Save()
    {
        lock (Gate) return (Status, Raw, Cards.ToDictionary(c => c.Key, c => (JsonObject)c.Value.DeepClone()), Url);
    }

    internal void Restore(object saved)
    {
        var (status, raw, cards, url) = ((int, string?, Dictionary<string, JsonObject>, string))saved;
        lock (Gate)
        {
            Status = status;
            Raw = raw;
            Cards = cards.ToDictionary(c => c.Key, c => (JsonObject)c.Value.DeepClone(), StringComparer.Ordinal);
            Url = url;
        }
    }

    private void Serve()
    {
        while (true)
        {
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch (Exception) { return; }
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    var lines = new List<string>();
                    while (true)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line)) break;
                        lines.Add(line);
                    }
                    if (lines.Count == 0) continue;
                    var parts = lines[0].Split(' ');
                    var status = 404;
                    var body = "{}";
                    var match = Regex.Match(parts.Length > 1 ? parts[1] : "", "^/fx/" + Regex.Escape(Route) + "/api/cards/(?<id>[^/?]+)$");
                    if (match.Success)
                    {
                        lock (Gate)
                        {
                            _requests.Add(lines[0]);
                            var id = Uri.UnescapeDataString(match.Groups["id"].Value);
                            if (parts[0] != "GET") status = 405;
                            else if (Status != 200) status = Status;
                            else if (Raw != null) { status = 200; body = Raw; }
                            else if (Cards.TryGetValue(id, out var card)) { status = 200; body = card.ToJsonString(); }
                        }
                    }
                    var reason = status == 200 ? "OK" : status == 404 ? "Not Found" : "Error";
                    var bytes = Encoding.UTF8.GetBytes(body);
                    var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "
                        + bytes.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(head, 0, head.Length);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
            }
            catch (Exception)
            {
                // A client that went away: the next request is served.
            }
        }
    }

    /// <summary>Get-C599ClosedPort: a loopback port that was just free, so a connect is refused.</summary>
    public static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        try { _listener.Stop(); }
        catch (Exception) { }
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
