using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

internal static class LandFailureDiagnostic
{
    internal sealed record ConflictEntry(string Entity, Guid? Key, Guid? OriginalToken, Guid? AttemptedToken,
        string ModifiedProperties);

    public static IReadOnlyList<ConflictEntry> CaptureConcurrencyEntries(Exception exception)
    {
        if (exception is not DbUpdateConcurrencyException conflict) return [];
        var result = new List<ConflictEntry>(4);
        foreach (var entry in conflict.Entries.Take(4))
        {
            var entity = entry.Entity switch
            {
                AgentTaskLandRequest => nameof(AgentTaskLandRequest),
                AgentTask => nameof(AgentTask),
                _ => "unknown",
            };
            if (entity == "unknown") { result.Add(new(entity, null, null, null, "unknown")); continue; }
            var id = entry.Entity is AgentTaskLandRequest request ? request.Id : ((AgentTask)entry.Entity).Id;
            Guid? original = null;
            Guid? attempted = null;
            try
            {
                original = entry.OriginalValues[nameof(AgentTask.ConcurrencyToken)] as Guid?;
                attempted = entry.CurrentValues[nameof(AgentTask.ConcurrencyToken)] as Guid?;
            }
            catch (InvalidOperationException) { }
            var names = string.Join(",", entry.Properties.Where(p => p.IsModified).Take(8)
                .Select(p => BoundIdentifier(p.Metadata.Name, 40) ?? "unknown"));
            result.Add(new(entity, id, original, attempted, names.Length <= 320 ? names : names[..320]));
        }
        return result;
    }

    public static async Task<string> DescribeConcurrencyAsync(AppDbContext db, Exception exception,
        IReadOnlyList<ConflictEntry> entries, string phase, Guid taskId, Guid requestId, int attempt,
        CancellationToken ct)
    {
        if (exception is not DbUpdateConcurrencyException) return "";
        var parts = new List<string>
        {
            $"phase={BoundIdentifier(phase, 40) ?? "unknown"}",
            $"owner={taskId:N}", $"request={requestId:N}", $"attempt={attempt}",
        };
        if (entries.Count == 0) parts.Add("entries=unavailable; observedDatabaseWriter=unknown");
        foreach (var entry in entries)
        {
            var observedToken = "unavailable";
            var observedWriter = "unknown";
            try
            {
                if (entry.Entity == nameof(AgentTaskLandRequest) && entry.Key is Guid requestKey)
                {
                    var row = await db.AgentTaskLandRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestKey, ct);
                    observedToken = row?.ConcurrencyToken.ToString("N") ?? "deleted";
                    if (row is not null) observedWriter = LandRequestWriteProvenance.ObservedLabel(row);
                }
                else if (entry.Entity == nameof(AgentTask) && entry.Key is Guid taskKey)
                {
                    var row = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskKey, ct);
                    observedToken = row?.ConcurrencyToken.ToString("N") ?? "deleted";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { observedToken = "read_unavailable"; observedWriter = "unknown"; }
            parts.Add($"entity={entry.Entity}; key={entry.Key?.ToString("N") ?? "unknown"}; "
                + $"originalToken={entry.OriginalToken?.ToString("N") ?? "unknown"}; "
                + $"attemptedToken={entry.AttemptedToken?.ToString("N") ?? "unknown"}; "
                + $"databaseToken={observedToken}; observedDatabaseWriter={observedWriter}; "
                + $"modifiedProperties={entry.ModifiedProperties}");
        }
        var summary = string.Join("; ", parts);
        return summary.Length <= 1600 ? summary : summary[..1600];
    }
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
        "git rev-parse --git-path index.lock",
        "filesystem canonicalization",
        "git reset --hard <sha>",
        "git clean -fdx",
        "git worktree add --detach",
        "git worktree lock",
        "git show-ref <ref>",
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
            "worktree" => arguments.Count > 1 && arguments[1] is "add" ? "git worktree add --detach"
                : arguments.Count > 1 && arguments[1] is "lock" ? "git worktree lock" : "git worktree list",
            "reset" => "git reset --hard <sha>",
            "clean" => "git clean -fdx",
            "show-ref" => "git show-ref <ref>",
            "rev-parse" => arguments.Contains("--git-path")
                ? "git rev-parse --git-path index.lock"
                : "git rev-parse <identity>",
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
