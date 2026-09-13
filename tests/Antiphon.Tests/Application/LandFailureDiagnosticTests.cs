using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class LandFailureDiagnosticTests
{
    private const string Marker = "synthetic-secret-marker://user:pw@host/?q=1\n\u001b[31mxxxxx";

    [Test]
    [Arguments(typeof(IOException), "landing_io_error")]
    [Arguments(typeof(FileNotFoundException), "landing_io_error")]
    [Arguments(typeof(TimeoutException), "landing_timeout")]
    [Arguments(typeof(UnauthorizedAccessException), "landing_access_denied")]
    [Arguments(typeof(InvalidOperationException), "landing_unexpected_exception")]
    [Arguments(typeof(TaskCanceledException), "landing_unexpected_exception")]
    public async Task C498_ClassifierMapsByType(Type exceptionType, string code)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, Marker)!;
        await Task.CompletedTask;
        LandFailureDiagnostic.Classify(exception).ShouldBe(code);
        LandFailureDiagnostic.Classify(exception).ShouldNotContain(Marker);
    }

    [Test]
    public async Task C498_ClassifierMapsConcurrencyBeforePersistence()
    {
        await Task.CompletedTask;
        LandFailureDiagnostic.Classify(new DbUpdateConcurrencyException(Marker, [])).ShouldBe("landing_concurrency_conflict");
        LandFailureDiagnostic.Classify(new DbUpdateException(Marker, (Exception?)null)).ShouldBe("landing_persistence_failed");
        LandFailureDiagnostic.Classify(new LandSourceResolutionConflictException()).ShouldBe("source_resolution_state_changed");
        LandFailureDiagnostic.Classify(new IOException(Marker), afterPublication: true)
            .ShouldBe("landing_interrupted_after_publication");
    }

    [Test]
    public async Task C498_ExceptionTypeNameIsBoundedIdentifier()
    {
        await Task.CompletedTask;
        var generic = LandFailureDiagnostic.BoundIdentifier(typeof(Dictionary<string, int>).Name, 200);
        generic.ShouldNotBeNull();
        generic.ShouldNotContain("`");
        generic!.All(c => char.IsAsciiLetterOrDigit(c) || c == '_').ShouldBeTrue();
        var nested = LandFailureDiagnostic.BoundIdentifier(new string('A', 250), 200);
        nested!.Length.ShouldBeLessThanOrEqualTo(200);
        LandFailureDiagnostic.ExceptionTypeName(new IOException(Marker)).ShouldBe("IOException");
        LandFailureDiagnostic.ExceptionTypeName(new IOException(Marker)).ShouldNotContain(Marker);
    }

    [Test]
    public async Task C498_CodeAndCommandBounds()
    {
        await Task.CompletedTask;
        LandFailureDiagnostic.BoundAsciiCode($"git_exit_{int.MinValue}")!.Length.ShouldBeLessThanOrEqualTo(100);
        LandFailureDiagnostic.BoundCommand("git status --porcelain=v1").ShouldBe("git status --porcelain=v1");
        LandFailureDiagnostic.BoundCommand("git mystery").ShouldBeNull();
        LandFailureDiagnostic.BoundAsciiCode("git_exit_128\n").ShouldBeNull();
        LandFailureDiagnostic.BoundAsciiCode("git_exit_\u001b").ShouldBeNull();
        LandFailureDiagnostic.BoundAsciiCode(Marker).ShouldBeNull();
        LandFailureDiagnostic.BoundCommand(Marker).ShouldBeNull();
    }

    [Test]
    public async Task C498_FormatTerminalDetail()
    {
        await Task.CompletedTask;
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var request = new AgentTaskLandRequest { ExpectedSourceSha = new string('e', 40) };
        var detail = LandFailureDiagnostic.FormatUnconfirmed("landing_concurrency_conflict", id, "DbUpdateConcurrencyException", request);
        detail.ShouldStartWith("land unconfirmed: landing_concurrency_conflict; diagnostic=aaaaaaaabbbbccccddddeeeeeeeeeeee; exception=DbUpdateConcurrencyException;");
        detail.ShouldContain("expected=" + new string('e', 40));
        detail.ShouldContain("local=null");
        detail.ShouldContain("remote=null");
        detail.ShouldContain("candidate=null");
        request.SourceRefusalReason = "status_error";
        request.ExpectedSourceSha = new string('e', 40);
        request.LocalBeforeSha = new string('a', 40);
        request.RemoteSourceSha = new string('b', 40);
        request.CandidateSourceSha = new string('c', 40);
        request.SourceDiagnosticCommand = "git status --porcelain=v1";
        request.SourceDiagnosticExitCode = 128;
        request.SourceDiagnosticCode = "git_exit_128";
        LandFailureDiagnostic.AppendInspection(
                $"status_error expected={request.ExpectedSourceSha} local={request.LocalBeforeSha} remote={request.RemoteSourceSha} candidate={request.CandidateSourceSha}",
                request)
            .ShouldBe($"status_error expected={request.ExpectedSourceSha} local={request.LocalBeforeSha} remote={request.RemoteSourceSha} candidate={request.CandidateSourceSha}; command=git status --porcelain=v1; exit=128; diagnostic=git_exit_128");
    }

    [Test]
    public async Task C498_DtoMapsDiagnosticFields()
    {
        await Task.CompletedTask;
        var id = Guid.NewGuid();
        var request = new AgentTaskLandRequest
        {
            Id = Guid.NewGuid(), RequestedAt = DateTime.UtcNow, LastEvaluatedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow,
            TerminalFailureCode = "landing_io_error", FailureDiagnosticId = id, FailureExceptionType = "IOException",
            SourceDiagnosticCommand = "git status --porcelain=v1", SourceDiagnosticExitCode = 128,
            SourceDiagnosticCode = "git_exit_128", SourceDiagnosticExceptionType = "LandingGitCommandException",
        };
        var dto = LandRequestStatusDto.From(request, DateTime.UtcNow, []);
        dto.TerminalFailureCode.ShouldBe("landing_io_error");
        dto.FailureDiagnosticId.ShouldBe(id);
        dto.FailureExceptionType.ShouldBe("IOException");
        dto.SourceDiagnosticCommand.ShouldBe("git status --porcelain=v1");
        dto.SourceDiagnosticExitCode.ShouldBe(128);
        dto.SourceDiagnosticCode.ShouldBe("git_exit_128");
        dto.SourceDiagnosticExceptionType.ShouldBe("LandingGitCommandException");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        json.RootElement.GetProperty("terminalFailureCode").GetString().ShouldBe("landing_io_error");
        json.RootElement.GetProperty("failureDiagnosticId").GetGuid().ShouldBe(id);
        json.RootElement.GetProperty("failureExceptionType").GetString().ShouldBe("IOException");
        json.RootElement.GetProperty("sourceDiagnosticCommand").GetString().ShouldBe("git status --porcelain=v1");
        json.RootElement.GetProperty("sourceDiagnosticExitCode").GetInt32().ShouldBe(128);
        json.RootElement.GetProperty("sourceDiagnosticCode").GetString().ShouldBe("git_exit_128");
        json.RootElement.GetProperty("sourceDiagnosticExceptionType").GetString().ShouldBe("LandingGitCommandException");
        var empty = LandRequestStatusDto.From(new AgentTaskLandRequest
        {
            Id = Guid.NewGuid(), RequestedAt = DateTime.UtcNow, LastEvaluatedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow,
        }, DateTime.UtcNow, []);
        using var absent = JsonDocument.Parse(JsonSerializer.Serialize(empty, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        absent.RootElement.GetProperty("terminalFailureCode").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task C498_PayloadSnapshotsDiagnosticFromEvent()
    {
        await Task.CompletedTask;
        var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), ReplyTo = AgentTaskReplyTo.None };
        var first = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = request.TaskId, Type = AgentTaskEventType.LandRefused, At = DateTime.UtcNow,
            Detail = "land unconfirmed: landing_io_error; diagnostic=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; exception=IOException; expected=null; local=null; remote=null; candidate=null",
        };
        var second = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = request.TaskId, Type = AgentTaskEventType.LandRefused, At = DateTime.UtcNow,
            Detail = "land unconfirmed: landing_timeout; diagnostic=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb; exception=TimeoutException; expected=null; local=null; remote=null; candidate=null",
        };
        var a = LandNotificationPayload.Create(request, first, LandNotificationKind.Outcome);
        var b = LandNotificationPayload.Create(request, second, LandNotificationKind.Outcome);
        a.Body.ShouldContain("landing_io_error; diagnostic=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; exception=IOException");
        b.Body.ShouldContain("landing_timeout; diagnostic=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb; exception=TimeoutException");
        a.ContentDigest.ShouldNotBe(b.ContentDigest);
    }
}
