using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class PhoneHomeTaskCreateTests
{
    [Test]
    public async Task Runner_bound_create_admits_claude_and_refuses_codex()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var repoRoot = RepoRoot();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var service = new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { AllowedRoots = [repoRoot] }),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            phoneHome: new PhoneHomeLaunchPolicy(Options.Create(new PhoneHomeRunnerSettings
            {
                Enabled = true, AllowedRunnerId = "server2", AllowDelegatedTasks = true,
                HostWorkspaceRoot = repoRoot, CallbackOrigin = "https://antiphon.desktop.codeperf.net",
                SharedSecret = "x",
            })));
        var caller = new AgentTaskService.Caller(null, null, repoRoot);
        var request = new CreateAgentTaskRequest("do remote work", Kind: AgentTaskKind.Worker,
            Role: AgentTaskRole.Code, AgentKind: AgentKind.ClaudeCode,
            Workspace: WorkspaceMode.Worktree, RunnerId: "server2");

        var created = await service.CreateAsync(request, caller, CancellationToken.None);
        var stored = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        stored.RunnerId.ShouldBe("server2");
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);

        var refused = await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(request with { Goal = "run Codex", AgentKind = AgentKind.Codex },
                caller, CancellationToken.None));
        refused.Errors[nameof(CreateAgentTaskRequest.RunnerId)].Single()
            .ShouldContain("Grok or Claude Code");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }
}
