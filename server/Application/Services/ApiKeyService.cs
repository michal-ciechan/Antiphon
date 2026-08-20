using System.Security.Cryptography;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CRUD over the API key store (CARD-0106 S1). Write-only by design: a value goes in, encrypted
/// under a purpose chain keyed on the row's own id, and the only thing that ever takes one out is
/// <c>ApiKeyEnvResolver</c> on its way into a child process's environment.
///
/// <para>Every failure arm here names the KEY NAME and the scope and nothing else. Not one message
/// in this file interpolates a value, and none of them logs one — a 409 or a 503 about a secret is
/// exactly the sort of string that ends up in a task failure reason, an incident message and a
/// check-in digest.</para>
/// </summary>
public sealed class ApiKeyService
{
    private const string ProtectionVersion = "v1";

    private readonly AppDbContext _db;
    private readonly IApiKeyProtector _protector;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(
        AppDbContext db,
        IApiKeyProtector protector,
        ILogger<ApiKeyService> logger)
    {
        _db = db;
        _protector = protector;
        _logger = logger;
    }

    private DateTime UtcNow() => DateTime.UtcNow;

    /// <summary>Every key in the installation, global and project-scoped, metadata only.</summary>
    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken ct) =>
        await QueryDtos(_db.ApiKeys.AsNoTracking()).ToListAsync(ct);

    /// <summary>The keys scoped to ONE project. Global keys are not included — this is the project's own list.</summary>
    public async Task<IReadOnlyList<ApiKeyDto>> ListForProjectAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectExistsAsync(projectId, ct);
        return await QueryDtos(_db.ApiKeys.AsNoTracking().Where(k => k.ProjectId == projectId))
            .ToListAsync(ct);
    }

    /// <summary>The keys visible to a launch in this project: its own, plus the globals.</summary>
    public async Task<IReadOnlyList<ApiKeyDto>> ListGlobalAsync(CancellationToken ct) =>
        await QueryDtos(_db.ApiKeys.AsNoTracking().Where(k => k.ProjectId == null)).ToListAsync(ct);

    /// <summary>
    /// Creates or replaces the value of the key called <paramref name="name"/> in the given scope.
    /// An existing row keeps its ID — which is what keeps its ciphertext decryptable, since the
    /// purpose chain is keyed on that id.
    /// </summary>
    public async Task<ApiKeyDto> PutAsync(
        string name,
        Guid? projectId,
        string value,
        CancellationToken ct)
    {
        var keyName = ApiKeyNaming.Validate(name);
        ValidateValue(value, keyName);
        if (projectId is { } scopedProjectId)
            await EnsureProjectExistsAsync(scopedProjectId, ct);

        var now = UtcNow();
        var existing = await _db.ApiKeys
            .FirstOrDefaultAsync(k => k.ProjectId == projectId && k.Name == keyName, ct);

        var key = existing ?? new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = keyName,
            ProjectId = projectId,
            CreatedAt = now,
        };

        key.Ciphertext = ProtectOrThrow(key.Id, keyName, value);
        key.ProtectionVersion = ProtectionVersion;
        key.UpdatedAt = now;
        if (existing is null)
            _db.ApiKeys.Add(key);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception)
        {
            // Two writers racing on the same (scope, name) hit one of the filtered unique indexes.
            // The inner exception names the constraint — kept attached, per the "never report a DB
            // failure without the DB's own message" rule.
            throw new ConflictException(
                $"API key '{keyName}' ({DescribeScope(projectId)}) was modified by another operation.",
                exception,
                "api_key_conflict");
        }

        // Names and scope only, and only on a WRITE — never the value, never on a resolution.
        _logger.LogInformation(
            "API key '{KeyName}' ({Scope}) {Action}",
            keyName,
            DescribeScope(projectId),
            existing is null ? "created" : "replaced");

        return await GetDtoAsync(key.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct)
            ?? throw new NotFoundException(nameof(ApiKey), id);
        var name = key.Name;
        var scope = DescribeScope(key.ProjectId);
        _db.ApiKeys.Remove(key);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("API key '{KeyName}' ({Scope}) deleted", name, scope);
    }

    /// <summary>"the global scope" / "project {id}" — the phrase every message about a key uses.</summary>
    internal static string DescribeScope(Guid? projectId) =>
        projectId is { } id ? $"project {id:D}" : "the global scope";

    private string ProtectOrThrow(Guid keyId, string keyName, string value)
    {
        try
        {
            return _protector.Protect(keyId, value);
        }
        catch (CryptographicException exception)
        {
            // The exception carries the key name, never the value it failed to encrypt.
            throw new ServiceUnavailableException(
                $"API key protection is unavailable; '{keyName}' was not stored.",
                "api_key_protection_unavailable",
                exception);
        }
    }

    private static void ValidateValue(string? value, string keyName)
    {
        if (string.IsNullOrEmpty(value))
            throw new ValidationException("value", $"API key '{keyName}' needs a value.");
        if (value.Length > ApiKeyNaming.MaxValueLength)
        {
            // The length is the operator's own input, so naming it leaks nothing about the value.
            throw new ValidationException(
                "value",
                $"API key '{keyName}' is {value.Length} characters; the maximum an environment "
                + $"value may carry is {ApiKeyNaming.MaxValueLength}.");
        }
    }

    private async Task EnsureProjectExistsAsync(Guid projectId, CancellationToken ct)
    {
        if (!await _db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw new NotFoundException(nameof(Project), projectId);
    }

    private async Task<ApiKeyDto> GetDtoAsync(Guid id, CancellationToken ct) =>
        await QueryDtos(_db.ApiKeys.AsNoTracking().Where(k => k.Id == id)).SingleAsync(ct);

    private static IQueryable<ApiKeyDto> QueryDtos(IQueryable<ApiKey> source) =>
        source
            .OrderBy(k => k.ProjectId == null ? 0 : 1)
            .ThenBy(k => k.Name)
            .Select(k => new ApiKeyDto(
                k.Id,
                k.Name,
                k.ProjectId,
                k.Project == null ? null : k.Project.Name,
                k.CreatedAt,
                k.UpdatedAt));
}
