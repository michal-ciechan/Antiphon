using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

internal static class LandFailureDiagnostic
{
    public const int CodeMaxLength = 100;
    public const int ExceptionTypeMaxLength = 200;
    public const int CommandMaxLength = 160;

    public const string ConcurrencyConflict = "landing_concurrency_conflict";
    public const string SourceStateChanged = "source_resolution_state_changed";
    public const string PersistenceFailed = "landing_persistence_failed";
    public const string Timeout = "landing_timeout";
    public const string IoError = "landing_io_error";
    public const string AccessDenied = "landing_access_denied";
    public const string Unexpected = "landing_unexpected_exception";
    public const string InterruptedAfterPublication = "landing_interrupted_after_publication";

    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "git status --porcelain=v1",
        "git ls-files --others --ignored",
        "git rev-parse <identity>",
        "git worktree list",
        "git check-ref-format <ref>",
        "filesystem canonicalization",
    };

    public static string Classify(Exception exception, bool afterPublication = false)
    {
        if (afterPublication) return InterruptedAfterPublication;
        if (exception is LandSourceResolutionConflictException) return SourceStateChanged;
        if (exception is DbUpdateConcurrencyException) return ConcurrencyConflict;
        if (exception is DbUpdateException) return PersistenceFailed;
        if (exception is TimeoutException) return Timeout;
        if (exception is UnauthorizedAccessException) return AccessDenied;
        if (exception is IOException) return IoError;
        return Unexpected;
    }

    public static string? ExceptionTypeName(Exception? exception)
    {
        if (exception is null) return null;
        return BoundIdentifier(exception.GetType().Name, ExceptionTypeMaxLength);
    }

    public static string? BoundIdentifier(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var c in value)
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
                builder.Append(c);
            if (builder.Length == maxLength) break;
        }
        return builder.Length == 0 ? null : builder.ToString();
    }

    public static string? BoundAsciiCode(string? value, int maxLength = CodeMaxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength) return null;
        foreach (var c in value)
        {
            if (c is < ' ' or > '~' or '\n' or '\r' or '\x1b') return null;
        }
        return value;
    }

    public static string? BoundCommand(string? template)
    {
        if (template is null || template.Length > CommandMaxLength) return null;
        return AllowedCommands.Contains(template) ? template : null;
    }

    public static string CommandTemplate(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) return "";
        return arguments[0] switch
        {
            "status" => "git status --porcelain=v1",
            "ls-files" => "git ls-files --others --ignored",
            "worktree" => "git worktree list",
            "rev-parse" => "git rev-parse <identity>",
            "check-ref-format" => "git check-ref-format <ref>",
            _ => "",
        };
    }

    public static LandInspectionDiagnostic? FromCommand(IReadOnlyList<string> arguments, int exitCode)
    {
        var template = BoundCommand(CommandTemplate(arguments));
        var code = BoundAsciiCode($"git_exit_{exitCode}");
        return template is null && code is null
            ? null
            : new LandInspectionDiagnostic(template, exitCode, code, null);
    }

    public static LandInspectionDiagnostic? FromCommandException(LandingGitCommandException exception)
        => new(
            BoundCommand(exception.Command),
            exception.ExitCode,
            BoundAsciiCode(exception.DiagnosticCode),
            ExceptionTypeName(exception));

    public static LandInspectionDiagnostic? FromIo(Exception exception)
    {
        var type = ExceptionTypeName(exception);
        if (exception.Message is "path_missing_or_inaccessible" or "path_alias_unresolved")
            return new(BoundCommand("filesystem canonicalization"), null, BoundAsciiCode(exception.Message), type);
        if (exception.Message == "git_start_failed")
            return new(null, null, BoundAsciiCode("git_start_failed"), type);
        return new(null, null, null, type);
    }

    public static LandInspectionDiagnostic? Sanitize(LandInspectionDiagnostic? diagnostic)
    {
        if (diagnostic is null) return null;
        return new(
            BoundCommand(diagnostic.Command),
            diagnostic.ExitCode,
            BoundAsciiCode(diagnostic.Code),
            BoundIdentifier(diagnostic.ExceptionType, ExceptionTypeMaxLength));
    }

    public static string FormatUnconfirmed(string code, Guid diagnosticId, string? exceptionType,
        AgentTaskLandRequest request)
    {
        var type = exceptionType ?? "Exception";
        return $"land unconfirmed: {code}; diagnostic={diagnosticId:N}; exception={type}; "
            + $"expected={request.ExpectedSourceSha ?? "null"}; local={request.LocalBeforeSha ?? "null"}; "
            + $"remote={request.RemoteSourceSha ?? "null"}; candidate={request.CandidateSourceSha ?? "null"}";
    }

    public static string AppendInspection(string core, AgentTaskLandRequest request)
    {
        if (request.SourceDiagnosticCommand is null && request.SourceDiagnosticExitCode is null
            && request.SourceDiagnosticCode is null)
            return core;
        var parts = new List<string> { core };
        if (request.SourceDiagnosticCommand is not null)
            parts.Add($"command={request.SourceDiagnosticCommand}");
        if (request.SourceDiagnosticExitCode is not null)
            parts.Add($"exit={request.SourceDiagnosticExitCode}");
        if (request.SourceDiagnosticCode is not null)
            parts.Add($"diagnostic={request.SourceDiagnosticCode}");
        return string.Join("; ", parts);
    }

    public static void ApplyInspection(AgentTaskLandRequest request, LandInspectionDiagnostic? diagnostic)
    {
        var safe = Sanitize(diagnostic);
        request.SourceDiagnosticCommand = safe?.Command;
        request.SourceDiagnosticExitCode = safe?.ExitCode;
        request.SourceDiagnosticCode = safe?.Code;
        request.SourceDiagnosticExceptionType = safe?.ExceptionType;
    }
}
