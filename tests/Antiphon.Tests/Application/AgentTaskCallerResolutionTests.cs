using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// What a request with NO delegation token is allowed to do (CARD-0020 S1). It used to inherit
/// <c>Directory.GetCurrentDirectory()</c> — the SERVER PROCESS's own cwd — which produced two
/// failures that were both silent to the caller: the server's own folder was authorised as a
/// working directory without anyone naming it, and that folder's PARENT (the repo root) was refused
/// as "outside the allowed roots" with advice to edit config the caller did not need to edit. Both
/// were reproduced live against the running server on 2026-08-20.
///
/// <para>The fix is a refusal, not a wider boundary: <c>Delegation:AllowedRoots</c> is a security
/// control, and widening it to cure an ergonomics bug trades a silent failure for a silent
/// authorisation.</para>
/// </summary>
[Category("Integration")]
public class AgentTaskCallerResolutionTests
{
    [Test]
    public async Task a_request_with_no_token_inherits_no_directory()
    {
        // The no-token branch returns before it ever touches the service, which is why it can be
        // asserted without one — and is exactly the branch that used to reach for the process cwd.
        var caller = await AgentTaskEndpoints.ResolveCallerAsync(
            new DefaultHttpContext(), service: null!, CancellationToken.None);

        caller.WorkingDirectory.ShouldBe(
            string.Empty, "a token-less caller has no directory to inherit — least of all the server's");
        caller.Task.ShouldBeNull();
        caller.SessionId.ShouldBeNull("no session means no reply routing, which is the other half of S1");
        caller.MayDelegate.ShouldBeTrue("the manual/UI path still creates tasks");
    }

    [Test]
    public async Task a_status_poll_with_an_unknown_token_stays_unattributed()
    {
        var (service, _) = CreateService();
        var http = new DefaultHttpContext();
        http.Request.Headers[AgentTaskEndpoints.TokenHeader] = "stale-token";

        var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, service, CancellationToken.None);

        caller.ShouldBeNull("GET /{id} remains a public status read when a stale token is present");
    }

    [Test]
    public async Task a_token_less_request_with_no_directory_is_refused_and_told_why()
    {
        var (service, _) = CreateService();

        var ex = await Should.ThrowAsync<ValidationException>(
            () => service.CreateAsync(Request(directory: null), NoTokenCaller, CancellationToken.None));

        // The old behaviour authorised the server's own folder here and returned 201. The reason a
        // caller reads is the FIELD error, not the exception's generic "validation errors occurred".
        var detail = Detail(ex);
        detail.ShouldContain("none to inherit");
        detail.ShouldContain(
            "X-Antiphon-Task-Token",
            customMessage: "the refusal must name the thing the caller is missing");
    }

    [Test]
    public async Task a_token_less_request_into_an_allowed_root_succeeds_and_says_no_reply_is_routed()
    {
        var root = Directory.CreateTempSubdirectory("antiphon-c20-root");
        try
        {
            var (service, _) = CreateService(allowedRoots: [root.FullName]);

            var created = await service.CreateAsync(
                Request(directory: root.FullName), NoTokenCaller, CancellationToken.None);

            try
            {
                created.NoReplyRouting.ShouldBeTrue(
                    "no token means no parent task and no parent session, so nothing is routed back");

                await using var verify = CreateContext();
                var task = await verify.AgentTasks.SingleAsync(t => t.Id == created.Id);
                task.ReplyTo.ShouldBe(AgentTaskReplyTo.None);
                task.WorkingDirectory.ShouldBe(root.FullName, "the caller's explicit directory, not the server's");
            }
            finally
            {
                await DeleteTaskAsync(created.Id);
            }
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    public async Task a_token_less_request_outside_the_roots_is_told_it_inherits_nothing()
    {
        // The live 422 said only "Add it to Delegation:AllowedRoots to permit it", which reads as
        // "your path is wrong" and sends an agent to edit a security boundary.
        var stranger = Directory.CreateTempSubdirectory("antiphon-c20-stranger");
        try
        {
            var (service, _) = CreateService();

            var ex = await Should.ThrowAsync<ValidationException>(
                () => service.CreateAsync(
                    Request(directory: stranger.FullName), NoTokenCaller, CancellationToken.None));

            var detail = Detail(ex);
            detail.ShouldContain("outside the allowed roots");
            detail.ShouldContain("Delegation:AllowedRoots");
            detail.ShouldContain(
                "inherits NOTHING",
                customMessage: "say WHY the caller has no root of its own, not just which rule it broke");
        }
        finally
        {
            stranger.Delete(true);
        }
    }

    [Test]
    public async Task the_token_path_is_unchanged_it_inherits_and_routes_a_reply()
    {
        var callerDirectory = Directory.CreateTempSubdirectory("antiphon-c20-caller");
        try
        {
            var (service, sessionId) = CreateService(withSession: true);
            // What AuthenticateAsync builds for a session-scoped token: no parent task, a parent
            // session, and the caller's own directory as the implicit root.
            var caller = new AgentTaskService.Caller(null, sessionId, callerDirectory.FullName);

            var created = await service.CreateAsync(
                Request(directory: null), caller, CancellationToken.None);

            try
            {
                created.NoReplyRouting.ShouldBeFalse("a session-scoped caller gets its report back");

                await using var verify = CreateContext();
                var task = await verify.AgentTasks.SingleAsync(t => t.Id == created.Id);
                task.ReplyTo.ShouldBe(AgentTaskReplyTo.Session);
                task.ParentSessionId.ShouldBe(sessionId);
                task.WorkingDirectory.ShouldBe(
                    callerDirectory.FullName,
                    "inheriting from a REAL caller is the case that must keep working with no roots configured");
            }
            finally
            {
                await DeleteTaskAsync(created.Id);
            }
        }
        finally
        {
            callerDirectory.Delete(true);
        }
    }

    /// <summary>
    /// What the caller actually reads. <c>ValidationException.Message</c> is the generic envelope
    /// ("One or more validation errors occurred."); the sentence that explains the refusal is the
    /// per-field error, which is what reaches the problem-details response.
    /// </summary>
    private static string Detail(ValidationException ex) =>
        string.Join(" ", ex.Errors.SelectMany(e => e.Value));

    private static readonly AgentTaskService.Caller NoTokenCaller = new(null, null, string.Empty);

    private static CreateAgentTaskRequest Request(string? directory) => new(
        Goal: "Do the thing.",
        Kind: AgentTaskKind.Worker,
        Role: AgentTaskRole.Docs,
        WorkingDirectory: directory);

    private static (AgentTaskService Service, Guid SessionId) CreateService(
        IReadOnlyList<string>? allowedRoots = null, bool withSession = false)
    {
        var settings = new DelegationSettings();
        if (allowedRoots is not null)
            settings.AllowedRoots = [.. allowedRoots];

        var sessionId = withSession ? SeedSessionAsync().GetAwaiter().GetResult() : Guid.Empty;
        var service = new AgentTaskService(
            CreateContext(),
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);
        return (service, sessionId);
    }

    private static async Task<Guid> SeedSessionAsync()
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    /// <summary>
    /// Every row this file creates is deleted again: the delegation sweeps are GLOBAL scans of the
    /// shared test database, and a Queued task left behind is a task somebody else's tick dispatches.
    /// </summary>
    private static async Task DeleteTaskAsync(Guid taskId)
    {
        await using var db = CreateContext();
        await db.AgentTaskEvents.Where(e => e.AgentTaskId == taskId).ExecuteDeleteAsync();
        await db.AgentTasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
}
