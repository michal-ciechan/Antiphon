using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Operator issue / rotate / revoke / list for Delegation Capability principals (CARD-0398).
/// The CLI is the only writer of the DPAPI store; this service never touches LocalAppData.
/// </summary>
public sealed class DelegationCapabilityService
{
    private static readonly Regex NamePattern = new(
        @"^[A-Za-z0-9_.-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DelegationCapabilityService> _logger;

    public DelegationCapabilityService(
        AppDbContext db,
        TimeProvider timeProvider,
        ILogger<DelegationCapabilityService> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DelegationCapabilityIssuedDto> IssueAsync(
        IssueDelegationCapabilityRequest request,
        CancellationToken ct)
    {
        var name = ValidateName(request.Name);
        var roots = ValidateRoots(request.Roots);
        await EnsureBoardAndProjectAsync(request.BoardId, request.ProjectId, ct);

        var taken = await _db.DelegationCapabilities
            .AnyAsync(c => c.Name == name && c.RevokedAt == null, ct);
        if (taken)
        {
            throw new ConflictException(
                $"Capability '{name}' already exists.",
                "capability_name_conflict");
        }

        var now = UtcNow();
        var (token, hash) = AgentTaskService.NewToken();
        var row = new DelegationCapability
        {
            Id = Guid.NewGuid(),
            Name = name,
            TokenHash = hash,
            RootsJson = DelegationCapabilityRoots.Serialize(roots),
            BoardId = request.BoardId,
            ProjectId = request.ProjectId,
            CreatedAt = now,
        };
        _db.DelegationCapabilities.Add(row);
        AddEvent(row, DelegationCapabilityEventType.Issued, now);
        await SaveAsync(ct);

        _logger.LogInformation(
            "Delegation capability '{Name}' issued with {RootCount} root(s)",
            name,
            roots.Count);

        return ToIssued(row, roots, token);
    }

    public async Task<DelegationCapabilityIssuedDto> RotateAsync(Guid id, CancellationToken ct)
    {
        var row = await LoadAsync(id, ct);
        if (row.RevokedAt is not null)
            throw new ConflictException($"Capability '{row.Name}' is revoked.", "capability_revoked");

        var now = UtcNow();
        var (token, hash) = AgentTaskService.NewToken();
        row.TokenHash = hash;
        row.RotatedAt = now;
        AddEvent(row, DelegationCapabilityEventType.Rotated, now);
        await SaveAsync(ct);

        _logger.LogInformation("Delegation capability '{Name}' rotated", row.Name);
        return ToIssued(row, DelegationCapabilityRoots.Parse(row.RootsJson), token);
    }

    public async Task<DelegationCapabilityDto> RevokeAsync(Guid id, CancellationToken ct)
    {
        var row = await LoadAsync(id, ct);
        if (row.RevokedAt is null)
        {
            var now = UtcNow();
            row.RevokedAt = now;
            AddEvent(row, DelegationCapabilityEventType.Revoked, now);
            await SaveAsync(ct);
            _logger.LogWarning("Delegation capability '{Name}' revoked", row.Name);
        }

        return ToDto(row);
    }

    public async Task<IReadOnlyList<DelegationCapabilityDto>> ListAsync(CancellationToken ct)
    {
        var rows = await _db.DelegationCapabilities.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<DelegationCapabilityDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await _db.DelegationCapabilities.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(DelegationCapability), id);
        return ToDto(row);
    }

    /// <summary>
    /// Hint path the CLI writes. The server never creates this file.
    /// </summary>
    public static string DefaultStorePath(string name) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Antiphon",
            "capabilities",
            $"{name}.dpapi");

    private async Task<DelegationCapability> LoadAsync(Guid id, CancellationToken ct) =>
        await _db.DelegationCapabilities.FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException(nameof(DelegationCapability), id);

    private static string ValidateName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > DelegationCapability.NameMaxLength
            || !NamePattern.IsMatch(trimmed))
        {
            throw new ValidationException(
                "name",
                "Capability name must be [A-Za-z0-9_.-]+ and at most 64 characters.");
        }

        return trimmed;
    }

    private static IReadOnlyList<string> ValidateRoots(IReadOnlyList<string>? roots)
    {
        if (roots is null || roots.Count < DelegationCapability.MinRoots
            || roots.Count > DelegationCapability.MaxRoots)
        {
            throw new ValidationException(
                "roots",
                $"A capability needs {DelegationCapability.MinRoots} to {DelegationCapability.MaxRoots} existing directories.");
        }

        var normalized = new List<string>(roots.Count);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        foreach (var raw in roots)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new ValidationException("roots", "Each root must be a non-empty directory path.");
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ValidationException("roots", $"'{raw}' is not a usable directory path.");
            }

            if (!Directory.Exists(full))
                throw new ValidationException("roots", $"Directory does not exist: {full}");

            var pathRoot = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(pathRoot)
                && string.Equals(
                    DelegationWorkspaceResolver.NormalizeSeparators(full),
                    DelegationWorkspaceResolver.NormalizeSeparators(pathRoot),
                    comparison))
            {
                throw new ValidationException(
                    "roots",
                    $"'{full}' is a filesystem root and cannot be a capability root.");
            }

            if (normalized.Any(existing => string.Equals(existing, full, comparison)))
                continue;
            normalized.Add(full);
        }

        if (normalized.Count < DelegationCapability.MinRoots)
        {
            throw new ValidationException(
                "roots",
                $"A capability needs {DelegationCapability.MinRoots} to {DelegationCapability.MaxRoots} existing directories.");
        }

        return normalized;
    }

    private async Task EnsureBoardAndProjectAsync(Guid? boardId, Guid? projectId, CancellationToken ct)
    {
        if (boardId is { } bid
            && !await _db.Boards.AsNoTracking().AnyAsync(b => b.Id == bid, ct))
        {
            throw new ValidationException("boardId", $"Board '{bid}' was not found.");
        }

        if (projectId is { } pid
            && !await _db.Projects.AsNoTracking().AnyAsync(p => p.Id == pid, ct))
        {
            throw new ValidationException("projectId", $"Project '{pid}' was not found.");
        }
    }

    private void AddEvent(DelegationCapability row, DelegationCapabilityEventType type, DateTime at)
    {
        var roots = DelegationCapabilityRoots.Parse(row.RootsJson);
        var detail =
            $"name={row.Name}; roots={string.Join(", ", roots)}; boardId={row.BoardId}; projectId={row.ProjectId}";
        if (detail.Length > DelegationCapabilityEvent.DetailMaxLength)
            detail = detail[..DelegationCapabilityEvent.DetailMaxLength];

        _db.DelegationCapabilityEvents.Add(new DelegationCapabilityEvent
        {
            Id = Guid.NewGuid(),
            CapabilityId = row.Id,
            Type = type,
            Detail = detail,
            At = at,
        });
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            throw new ConflictException(
                "Capability was modified by another operation.",
                ex,
                "capability_conflict");
        }
    }

    private static DelegationCapabilityIssuedDto ToIssued(
        DelegationCapability row,
        IReadOnlyList<string> roots,
        string token) =>
        new(
            row.Id,
            row.Name,
            roots,
            row.BoardId,
            row.ProjectId,
            token,
            DefaultStorePath(row.Name),
            row.CreatedAt,
            row.LastUsedAt,
            row.RotatedAt,
            row.RevokedAt);

    private static DelegationCapabilityDto ToDto(DelegationCapability row) =>
        new(
            row.Id,
            row.Name,
            DelegationCapabilityRoots.Parse(row.RootsJson),
            row.BoardId,
            row.ProjectId,
            row.CreatedAt,
            row.LastUsedAt,
            row.RotatedAt,
            row.RevokedAt);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
