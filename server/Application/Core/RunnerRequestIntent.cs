using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0659 D-1. What a create request said about its execution host, captured BEFORE the
/// reserved <c>local</c> sentinel is normalized out of the request.
/// </summary>
public enum RunnerRequestSource
{
    /// <summary>Omitted, null or whitespace: automatic selection.</summary>
    Unset = 0,

    /// <summary>The reserved <c>local</c> token (any case): the desktop, persisted as null.</summary>
    ExplicitLocal = 1,

    /// <summary>Any other value: an explicit remote runner id, never replaced or fallen back.</summary>
    ExplicitRemote = 2,
}

public sealed record RunnerRequestIntent(RunnerRequestSource Source, string? RemoteRunnerId, string? RequestedToken = null)
{
    public const int MaxRunnerIdLength = 64;

    /// <summary>Case-insensitive. <c>local</c> and <c>desktop</c> are the same explicit desktop choice.</summary>
    public static bool IsLocalToken(string? value) => IsDesktopAlias(value);

    public static bool IsDesktopAlias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim();
        return string.Equals(trimmed, PhoneHomeProtocol.LocalRunnerId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, RunnerPlatformWire.DesktopId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Public catalogue id for a desktop alias: always <c>desktop</c>.</summary>
    public static string DesktopPublicId => RunnerPlatformWire.DesktopId;

    public static string? CanonicalRunnerId(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || IsDesktopAlias(runnerId))
            return null;
        return runnerId.Trim();
    }

    public static string DisplayRunnerId(string? storedRunnerId) =>
        string.IsNullOrWhiteSpace(storedRunnerId) ? DesktopPublicId : storedRunnerId;

    public static RunnerRequestIntent Parse(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId))
            return new RunnerRequestIntent(RunnerRequestSource.Unset, null);
        var trimmed = runnerId.Trim();
        if (DescribeInvalid(trimmed) is { } problem)
            throw new ValidationException(nameof(Dtos.CreateAgentTaskRequest.RunnerId), $"runnerId {problem}.", "runner_id_invalid");
        if (IsDesktopAlias(trimmed))
        {
            var token = string.Equals(trimmed, RunnerPlatformWire.DesktopId, StringComparison.OrdinalIgnoreCase)
                ? RunnerPlatformWire.DesktopId
                : PhoneHomeProtocol.LocalRunnerId;
            return new RunnerRequestIntent(RunnerRequestSource.ExplicitLocal, null, token);
        }

        return new RunnerRequestIntent(RunnerRequestSource.ExplicitRemote, trimmed, trimmed);
    }

    /// <summary>Null when the identifier is acceptable; otherwise what is wrong with it.</summary>
    public static string? DescribeInvalid(string trimmed) =>
        trimmed.Length > MaxRunnerIdLength ? $"must be at most {MaxRunnerIdLength} characters"
        : trimmed.Any(char.IsControl) ? "must not contain control characters"
        : null;
}
