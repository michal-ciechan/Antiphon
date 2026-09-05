using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0383 S1: Grok resume vs create is decided from the on-disk session directory, never from
/// the row. <see cref="AgentSessionService.EffectiveResumeMode"/> is the one funnel.
/// </summary>
[Category("Unit")]
public class GrokNativeSessionResumeTests
{
    [Test]
    public void Resume_with_a_directory_under_any_cwd_encoding_keeps_resume()
    {
        var id = Guid.NewGuid();
        var home = SeedHome(id, encodedCwd: Uri.EscapeDataString(@"D:\src\OTHER-machine\repo"));
        try
        {
            var spec = Spec(home);
            AgentSessionService.EffectiveResumeMode(GrokSession(id), spec, AgentSessionResumeMode.Resume)
                .ShouldBe(AgentSessionResumeMode.Resume);

            var args = AgentSessionService.BuildSessionIdentityArgs(
                ["--always-approve"], id, AgentSessionResumeMode.Resume);
            args.ShouldContain("--resume");
            args.ShouldContain(id.ToString("D"));
            args.ShouldNotContain("--session-id");
        }
        finally
        {
            BestEffortDelete(home);
        }
    }

    [Test]
    public void Resume_with_no_directory_downgrades_to_create()
    {
        var id = Guid.NewGuid();
        var home = EmptyHome();
        try
        {
            var spec = Spec(home);
            AgentSessionService.EffectiveResumeMode(GrokSession(id), spec, AgentSessionResumeMode.Resume)
                .ShouldBeNull();

            var args = AgentSessionService.BuildSessionIdentityArgs(
                ["--always-approve"], id, resumeMode: null);
            args.ShouldContain("--session-id");
            args.ShouldContain(id.ToString("D"));
            args.ShouldNotContain("--resume");
        }
        finally
        {
            BestEffortDelete(home);
        }
    }

    [Test]
    [Arguments(AgentSessionResumeMode.New)]
    [Arguments(AgentSessionResumeMode.Continue)]
    public void New_and_continue_are_untouched_even_without_a_directory(AgentSessionResumeMode requested)
    {
        var id = Guid.NewGuid();
        var home = EmptyHome();
        try
        {
            AgentSessionService.EffectiveResumeMode(GrokSession(id), Spec(home), requested)
                .ShouldBe(requested);
        }
        finally
        {
            BestEffortDelete(home);
        }
    }

    [Test]
    public void Claude_and_codex_resume_are_untouched_without_a_directory()
    {
        var id = Guid.NewGuid();
        var home = EmptyHome();
        try
        {
            var spec = Spec(home);
            AgentSessionService.EffectiveResumeMode(
                    new AgentSession { Id = id, AgentKind = AgentKind.ClaudeCode },
                    spec,
                    AgentSessionResumeMode.Resume)
                .ShouldBe(AgentSessionResumeMode.Resume);
            AgentSessionService.EffectiveResumeMode(
                    new AgentSession { Id = id, AgentKind = AgentKind.Codex },
                    spec,
                    AgentSessionResumeMode.Resume)
                .ShouldBe(AgentSessionResumeMode.Resume);
        }
        finally
        {
            BestEffortDelete(home);
        }
    }

    [Test]
    public void Launch_env_GROK_HOME_beats_process_env()
    {
        var id = Guid.NewGuid();
        var seeded = SeedHome(id);
        var empty = EmptyHome();
        var previous = Environment.GetEnvironmentVariable("GROK_HOME");
        try
        {
            Environment.SetEnvironmentVariable("GROK_HOME", seeded);
            GrokNativeSessionStore.Exists(seeded, id).ShouldBeTrue();

            AgentSessionService.EffectiveResumeMode(GrokSession(id), Spec(empty), AgentSessionResumeMode.Resume)
                .ShouldBeNull("the child's GROK_HOME is empty even though the process env is seeded");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GROK_HOME", previous);
            BestEffortDelete(seeded);
            BestEffortDelete(empty);
        }
    }

    private static AgentSession GrokSession(Guid id) => new() { Id = id, AgentKind = AgentKind.Grok };

    private static AgentLaunchSpec Spec(string grokHome) =>
        new("grok", AgentKind.Grok, "grok.exe", [],
            new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
            Path.GetTempPath(), 120, 30);

    private static string EmptyHome()
    {
        var home = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        return home;
    }

    private static string SeedHome(Guid id, string? encodedCwd = null)
    {
        var home = EmptyHome();
        var encoded = encodedCwd ?? Uri.EscapeDataString(@"C:\work\card-0383");
        Directory.CreateDirectory(Path.Combine(home, "sessions", encoded, id.ToString("D")));
        return home;
    }

    private static void BestEffortDelete(string? dir)
    {
        if (dir is null) return;
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
