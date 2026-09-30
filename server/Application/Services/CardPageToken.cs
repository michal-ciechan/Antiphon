using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

internal sealed record CardPageToken(
    int Version, string Kind, Guid? BoardId, CardStatus? Status, DateTime? UpdatedSince,
    string? QueryHash, bool IncludeArchived, int Limit, DateTime AfterUpdatedAt, Guid AfterId,
    string Fingerprint)
{
    internal const int CurrentVersion = 2;
    internal const int MaxLength = 4096;

    internal static string? HashQuery(string? query) => query is null ? null :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));

    internal string Encode()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static CardPageToken Decode(string value)
    {
        if (value.Length is 0 or > MaxLength)
            throw Invalid();
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            if (base64.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '+' and not '/' and not '='))
                throw Invalid();
            var bytes = Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                new[] { "Version", "Kind", "BoardId", "Status", "UpdatedSince", "QueryHash",
                    "IncludeArchived", "Limit", "AfterUpdatedAt", "AfterId", "Fingerprint" }
                    .Any(name => !document.RootElement.TryGetProperty(name, out _)))
                throw Invalid();
            var token = JsonSerializer.Deserialize<CardPageToken>(bytes);
            if (token is null || token.Version != CurrentVersion ||
                token.Kind is not ("list" or "search") || token.Limit < 1 ||
                token.AfterUpdatedAt.Kind != DateTimeKind.Utc || token.AfterId == Guid.Empty ||
                (token.UpdatedSince is DateTime since && since.Kind != DateTimeKind.Utc) ||
                token.Fingerprint is null || token.Fingerprint.Length != 64 ||
                !token.Fingerprint.All(Uri.IsHexDigit) ||
                (token.Kind == "search" && (token.QueryHash is null ||
                    token.QueryHash.Length != 64 || !token.QueryHash.All(Uri.IsHexDigit))) ||
                (token.Kind == "list" && token.QueryHash is not null))
                throw Invalid();
            return token;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or NullReferenceException)
        {
            throw Invalid();
        }
    }

    internal void RequireScope(string kind, Guid? boardId, CardStatus? status, DateTime? updatedSince,
        string? query, bool includeArchived, int limit)
    {
        if (Kind != kind || BoardId != boardId || Status != status || UpdatedSince != updatedSince ||
            QueryHash != HashQuery(query) || IncludeArchived != includeArchived || Limit != limit)
            throw Invalid();
    }

    private static ValidationException Invalid() =>
        new("pageToken", "Page token is invalid for this query; restart without pageToken.");
}
