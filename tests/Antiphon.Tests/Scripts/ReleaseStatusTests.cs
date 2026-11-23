using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 D-9 (V-7): the release status (ReleaseStatus, `antiphon-scripts release-status`), in process. These cases
/// replaced C599_StatusIdentity and the release-status rows of C599_NoActivation of scripts/test-release-gate.ps1, which ran
/// the deleted scripts/release-status.ps1 (unit 12 of 2026-11-22-remaining-ps1-to-csharp.md). The publication journal is a
/// real file written by the production ReleaseGateJournal; the version endpoint is a fake transport. The decisive
/// behaviours: the running SHA is matched EXACTLY against published rows, so a newer build reports 'unreleased
/// integration build' rather than borrowing the latest release's identity; a reserved-but-unpublished tag is not released;
/// missing data is 'unknown', never the latest release; and the status starts no process and runs no git. Not ported: the
/// script-text scan for restart-apphost, Stop-Process, taskkill, git checkout and deploy-local (a PowerShell-source check;
/// the behavioural row below replaces it).
/// </summary>
[Category("Unit")]
public sealed class ReleaseStatusTests
{
    private const string ShaB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ShaC = "cccccccccccccccccccccccccccccccccccccccc";
    private const string CandidateId = "rc-20260923T083000Z";

    private sealed class Fixture : IDisposable
    {
        public readonly NightlyFixture Files = NightlyFixture.Create("c599-status");
        public readonly List<string> Requests = new();

        public string ReleaseRoot => Files.PathOf("releases");
        public string Journal => Path.Combine(ReleaseRoot, "publications.json");

        public ReleaseStatusOptions Options(string sha = "") => new() { ReleaseRoot = ReleaseRoot, Sha = sha, VersionUrl = "http://127.0.0.1:1/api/version" };

        public NightlyReportHttp Version(int status, string body) => (method, uri, headers, payload) =>
        {
            Requests.Add(method + " " + uri);
            return new NightlyReportHttpResponse(status, body);
        };

        public NightlyReportHttp NoNetwork => (method, uri, headers, payload) =>
        {
            Requests.Add(method + " " + uri);
            throw new HttpRequestException("no network in this test");
        };

        public void Dispose() => Files.Dispose();
    }

    [Test]
    public void C599_StatusIdentity()
    {
        using var fx = new Fixture();
        Directory.CreateDirectory(fx.ReleaseRoot);
        var rows = new NightlyCaseRows();
        var journals = new ReleaseGateJournal(TimeProvider.System);

        var unknown = ReleaseStatus.Observe(fx.Options(ShaB), fx.NoNetwork);
        rows.Check(unknown.State == ReleaseStatusResult.Unknown, "C599 StatusIdentity no publication journal is unknown, not latest", unknown.State);

        journals.Reserve(fx.Journal, CandidateId, ShaB, new DateTime(2026, 9, 23, 8, 30, 0, DateTimeKind.Utc), []);
        var pending = ReleaseStatus.Observe(fx.Options(ShaB), fx.NoNetwork);
        rows.Check(pending.State == ReleaseStatusResult.Unreleased, "C599 StatusIdentity a reserved but unpublished tag is not released", pending.State);

        journals.MarkPublished(fx.Journal, CandidateId, "R1", "https://example/releases/tag/v2026.09.23.1");
        var released = ReleaseStatus.Observe(fx.Options(ShaB), fx.NoNetwork);
        rows.Check(released.State == ReleaseStatusResult.Released && released.Tag == "v2026.09.23.1",
            "C599 StatusIdentity an exact sha match reports released with its tag", released.Tag);
        rows.Check(released.ReleaseUrl.Contains("releases/tag/v2026.09.23.1", StringComparison.Ordinal),
            "C599 StatusIdentity the release url is the GitHub tag address", released.ReleaseUrl);

        var newer = ReleaseStatus.Observe(fx.Options(ShaC), fx.NoNetwork);
        rows.Check(newer.State == ReleaseStatusResult.Unreleased, "C599 StatusIdentity a newer sha is NOT labelled as the latest release", newer.State);
        rows.Check(newer.Tag.Length == 0, "C599 StatusIdentity a newer sha carries no tag", newer.Tag);

        var upper = ReleaseStatus.Observe(fx.Options(ShaB.ToUpperInvariant()), fx.NoNetwork);
        rows.Check(upper.State == ReleaseStatusResult.Released, "C599 StatusIdentity sha matching is case-insensitive", upper.State);

        rows.Check(fx.Requests.Count == 0, "C599 StatusIdentity a given sha reads no version endpoint", string.Join(",", fx.Requests));
        rows.ShouldAllPass(8);
    }

    [Test]
    public void The_running_sha_and_capabilities_come_from_the_version_endpoint()
    {
        using var fx = new Fixture();
        var journals = new ReleaseGateJournal(TimeProvider.System);
        journals.Reserve(fx.Journal, CandidateId, ShaB, new DateTime(2026, 9, 23, 8, 30, 0, DateTimeKind.Utc), []);
        journals.MarkPublished(fx.Journal, CandidateId, "R1", "https://example/releases/tag/v2026.09.23.1");

        var released = ReleaseStatus.Observe(fx.Options(), fx.Version(200, "{\"version\":\"" + ShaB + "\",\"capabilities\":[\"land-v2\",\"rc\"]}"));
        released.State.ShouldBe(ReleaseStatusResult.Released);
        released.Sha.ShouldBe(ShaB);
        released.Capabilities.ShouldBe(["land-v2", "rc"]);
        fx.Requests.ShouldBe(["GET http://127.0.0.1:1/api/version"]);

        var newer = ReleaseStatus.Observe(fx.Options(), fx.Version(200, "{\"version\":\"" + ShaC + "\"}"));
        newer.State.ShouldBe(ReleaseStatusResult.Unreleased);
        newer.Capabilities.ShouldBeEmpty();
    }

    [Test]
    public void Missing_version_data_is_unknown_never_the_latest_release()
    {
        using var fx = new Fixture();
        var journals = new ReleaseGateJournal(TimeProvider.System);
        journals.Reserve(fx.Journal, CandidateId, ShaB, new DateTime(2026, 9, 23, 8, 30, 0, DateTimeKind.Utc), []);
        journals.MarkPublished(fx.Journal, CandidateId, "R1", "https://example/releases/tag/v2026.09.23.1");
        foreach (var http in new[] { fx.NoNetwork, fx.Version(503, "{}"), fx.Version(200, "not json"), fx.Version(200, "{\"capabilities\":[]}"), fx.Version(200, "[]") })
        {
            var status = ReleaseStatus.Observe(fx.Options(), http);
            status.State.ShouldBe(ReleaseStatusResult.Unknown);
            status.Tag.ShouldBeEmpty();
            status.ReleaseUrl.ShouldBeEmpty();
            status.Detail.ShouldNotBeEmpty();
        }
    }

    [Test]
    public void The_command_prints_one_json_line_and_starts_no_process_or_git()
    {
        using var fx = new Fixture();
        var git = new FakeGit();
        var launcher = new RecordingLauncher();
        var host = fx.Files.Host(git: git, launcher: launcher);
        var result = ReleaseStatus.Run(fx.Options(ShaB), host, fx.NoNetwork);
        result.State.ShouldBe(ReleaseStatusResult.Unknown);
        var lines = fx.Files.Out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Length.ShouldBe(1);
        using var doc = JsonDocument.Parse(lines[0]);
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["sha", "capabilities", "state", "tag", "releaseUrl", "detail"]);
        doc.RootElement.GetProperty("state").GetString().ShouldBe("unknown");
        doc.RootElement.GetProperty("detail").GetString().ShouldBe("no publication journal");
        // C599 NoActivation: no restart, deploy, checkout or kill - nothing is started and git is never run.
        launcher.Launches.ShouldBeEmpty();
        git.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Options_take_the_script_parameters_in_kebab_case()
    {
        var defaults = ReleaseStatusOptions.Parse([]);
        defaults.ReleaseRoot.ShouldBe(@"C:\Antiphon\releases");
        defaults.VersionUrl.ShouldBe("http://127.0.0.1:17202/api/version");
        defaults.Sha.ShouldBeEmpty();
        var given = ReleaseStatusOptions.Parse(["--release-root", "R", "--version-url", "http://h/api/version", "--sha", ShaB]);
        given.ReleaseRoot.ShouldBe("R");
        given.VersionUrl.ShouldBe("http://h/api/version");
        given.Sha.ShouldBe(ShaB);
        Should.Throw<NightlyUsageException>(() => ReleaseStatusOptions.Parse(["-Sha", ShaB]));
    }
}
