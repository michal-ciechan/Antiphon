using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0383: a Grok <c>--resume &lt;uuid&gt;</c> with no native directory is refused; title
/// resumes, <c>--session-id</c>, and non-Grok kinds pass through.
/// </summary>
public class HerdrGrokResumeGuardTests
{
    [Test]
    public void Guid_resume_with_no_directory_throws_the_named_code()
    {
        var sessionId = Guid.NewGuid();
        var resumeId = Guid.NewGuid();
        var home = EmptyHome();
        try
        {
            using var pin = new GrokHomePin(home);
            var request = GrokRequest(sessionId, home, ["--always-approve", "--resume", resumeId.ToString("D")]);
            var ex = Should.Throw<HerdrLaunchException>(() =>
                HerdrGrokResumeGuard.Require(sessionId, request, HerdrAgentKinds.Grok, NullLogger.Instance));
            ex.Code.ShouldBe(HerdrProblemTypes.GrokNativeSessionMissing);
            ex.Message.ShouldContain(resumeId.ToString("D"));
            ex.Message.ShouldContain(sessionId.ToString("D"));
            ex.Message.ShouldContain(Path.Combine(home, "sessions"));
            ex.Message.ShouldContain("--session-id");
        }
        finally
        {
            BestEffortDelete(home);
        }
    }

    [Test]
    public void Guid_resume_with_a_directory_under_a_foreign_cwd_encoding_passes()
    {
        var sessionId = Guid.NewGuid();
        var resumeId = Guid.NewGuid();
        var home = EmptyHome();
        try
        {
            var encoded = Uri.EscapeDataString(@"D:\src\OTHER-machine\repo");
            Directory.CreateDirectory(Path.Combine(home, "sessions", encoded, resumeId.ToString("D")));
            using var pin = new GrokHomePin(home);
            var request = GrokRequest(sessionId, home, ["--resume", resumeId.ToString("D")]);
            Should.NotThrow(() =>
                HerdrGrokResumeGuard.Require(sessionId, request, HerdrAgentKinds.Grok, NullLogger.Instance));
        }
        finally
        {
            BestEffortDelete(home);
        }
    }

    [Test]
    public void Session_id_create_is_never_gated()
    {
        var sessionId = Guid.NewGuid();
        var home = EmptyHome();
        try
        {
            using var pin = new GrokHomePin(home);
            var request = GrokRequest(sessionId, home, ["--session-id", sessionId.ToString("D")]);
            Should.NotThrow(() =>
                HerdrGrokResumeGuard.Require(sessionId, request, HerdrAgentKinds.Grok, NullLogger.Instance));
        }
        finally
        {
            BestEffortDelete(home);
        }
    }

    [Test]
    public void Title_resume_passes()
    {
        var sessionId = Guid.NewGuid();
        var home = EmptyHome();
        try
        {
            using var pin = new GrokHomePin(home);
            var request = GrokRequest(sessionId, home, ["--resume", "yesterday's thread"]);
            Should.NotThrow(() =>
                HerdrGrokResumeGuard.Require(sessionId, request, HerdrAgentKinds.Grok, NullLogger.Instance));
        }
        finally
        {
            BestEffortDelete(home);
        }
    }

    [Test]
    public void Claude_kind_passes_even_with_a_guid_resume()
    {
        var sessionId = Guid.NewGuid();
        var home = EmptyHome();
        try
        {
            using var pin = new GrokHomePin(home);
            var request = new RunnerLaunchRequest(
                sessionId, "claude.exe",
                ["--resume", Guid.NewGuid().ToString("D")],
                new Dictionary<string, string> { ["GROK_HOME"] = home },
                home, 120, 30, Backend: SessionBackends.Herdr,
                Herdr: new HerdrLaunchOptions("k", "l", home, "t", AgentKind: HerdrAgentKinds.Claude));
            Should.NotThrow(() =>
                HerdrGrokResumeGuard.Require(sessionId, request, HerdrAgentKinds.Claude, NullLogger.Instance));
        }
        finally
        {
            BestEffortDelete(home);
        }
    }

    [Test]
    public void Short_and_equals_forms_are_recognised()
    {
        var resumeId = Guid.NewGuid();
        HerdrGrokResumeGuard.TryReadResumeId(["-r", resumeId.ToString("D")], out var parsed).ShouldBeTrue();
        parsed.ShouldBe(resumeId);
        HerdrGrokResumeGuard.TryReadResumeId(["--resume=" + resumeId.ToString("D")], out parsed).ShouldBeTrue();
        parsed.ShouldBe(resumeId);
        HerdrGrokResumeGuard.TryReadResumeId(["--resume", resumeId.ToString("D")], out parsed).ShouldBeTrue();
        parsed.ShouldBe(resumeId);
        HerdrGrokResumeGuard.TryReadResumeId(["--session-id", resumeId.ToString("D")], out _).ShouldBeFalse();
        HerdrGrokResumeGuard.TryReadResumeId(["--resume", "not-a-guid"], out _).ShouldBeFalse();
        HerdrGrokResumeGuard.TryReadResumeId(null, out _).ShouldBeFalse();
    }

    private static RunnerLaunchRequest GrokRequest(Guid sessionId, string home, IReadOnlyList<string> args) =>
        new(
            sessionId, "grok.exe", args,
            new Dictionary<string, string> { ["GROK_HOME"] = home },
            home, 120, 30,
            Backend: SessionBackends.Herdr,
            TranscriptFormat: TranscriptFormats.Grok,
            Herdr: new HerdrLaunchOptions("k", "l", home, "t", AgentKind: HerdrAgentKinds.Grok));

    private static string EmptyHome()
    {
        var home = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        return home;
    }

    private static void BestEffortDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class GrokHomePin : IDisposable
    {
        private readonly IDisposable _scope;
        public GrokHomePin(string home) => _scope = GrokTranscriptTailer.OverrideGrokHome(home);
        public void Dispose() => _scope.Dispose();
    }
}
