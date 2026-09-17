using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0547: the overdue-deadline sweep's container, extracted verbatim from
/// <c>AgentTaskOverdueDeadlineTests.CreateHarness()</c> so a settlement suite can run the same sweep
/// against the same database. The limits are hermetic: far beyond anything any other suite seeds, so
/// a fleet-global sweep can only judge rows the calling test back-dated past them.
/// </summary>
public static class OverdueSweepHarness
{
    /// <summary>Minutes. Far past anything any other suite seeds; the preview gate alone is 80 000.</summary>
    public const int Ceiling = 100_000;
    public const int ModelWait = 50_000;
    public const int LocalExecution = 60_000;
    public const int BootModelWait = 40_000;
    public const int BootStallRepeatHold = 30;

    public sealed record Built(AgentTaskDispatcher Dispatcher, RecordingSessionStopper Stopper, ServiceProvider Provider);

    /// <param name="saveInterceptor">
    /// CARD-0547 G-547-11d: wired exactly as <c>AgentTaskReplyIntegrationTests.C527Factory</c>
    /// wires its own, so a sweep test can see WHICH rows shared a <c>SaveChangesAsync</c>.
    /// </param>
    public static Built Create(
        Action<DelegationSettings>? configure = null,
        RecordingGitWorkspaceService? gitSpy = null,
        string? claudeProjectsRoot = null,
        SaveChangesInterceptor? saveInterceptor = null)
    {
        var stopper = new RecordingSessionStopper();
        var settings = new DelegationSettings
        {
            DefaultTimeoutMinutes = Ceiling,
            ModelWaitDeadlineMinutes = ModelWait,
            LocalExecutionDeadlineMinutes = LocalExecution,
            BootModelWaitDeadlineMinutes = BootModelWait,
            BootStallRepeatHoldMinutes = BootStallRepeatHold,
            RolePolicy = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = new DelegationSettings.RolePolicyEntry { TimeoutMinutes = Ceiling },
            },
        };
        configure?.Invoke(settings);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(TestDbFixture.ConnectionString);
            if (saveInterceptor is not null)
                o.AddInterceptors(saveInterceptor);
        });
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(settings));
        services.AddOptions<AgentRegistrySettings>();
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-overdue-wt"),
        }, workspaceGit: gitSpy);
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<AgentTaskReplyService>();
        // CARD-0085's recovery gate, armed exactly as the delivery watchdog's suite arms it: with
        // no repo and no CARD-NNNN in the title it finds nothing and the Failed stands. Its
        // GitWorkspaceService comes from AddDelegationWorktreeGraph above. The empty projects root
        // stops Arm B from scanning the machine's real ~/.claude/projects during a fleet-global sweep.
        services.AddSingleton(Options.Create(new DelegateBindRefusalRecoverySettings
        {
            ClaudeProjectsRoot = claudeProjectsRoot
                ?? Path.Combine(Path.GetTempPath(), "antiphon-overdue-no-jsonl"),
        }));
        services.AddSingleton<DelegateBindRefusalRecovery>();
        // CARD-0353 S2 step 5: the repeat hold. Scoped like production; absent, the hold arm is
        // simply not armed and the rest of the boot tail behaves identically.
        services.AddScoped<ModelAvailability>();
        // CARD-0153 S2's workspace arm, which CARD-0353 S2 reuses as the boot arm's second,
        // independent guard: files that moved mean the kill is off.
        services.AddScoped<AgentReviewCheckpointService>();
        services.AddScoped<AgentFilesService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return new(provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), stopper, provider);
    }
}
