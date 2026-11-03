using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// What a migrated test database is made of: each migration's ID and the operations its
/// <c>Up</c> emits for the Npgsql provider, serialized deterministically and hashed. Server code
/// that is not a migration changes nothing here, so an ordinary server edit keeps the schema cache
/// and the baked Postgres image warm, while any change to a migration (new, removed, or edited in
/// place, including its seed data and raw SQL) changes the fingerprint.
/// Builds operations only (no target models, no SQL generation): a few hundred milliseconds of
/// reflection, against the ~10 s of EF CPU a full migrate costs.
/// </summary>
internal static class TestMigrationFingerprint
{
    public const string Provider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int MaxDepth = 24;

    public sealed record Result(byte[] Sha256, IReadOnlyList<string> MigrationIds);

    private static readonly Lazy<Result> DefaultResult = new(() =>
    {
        using var span = TestTrace.Span("db-schema fingerprint");
        return Compute(typeof(Antiphon.Server.Infrastructure.Data.AppDbContext));
    });

    /// <summary><c>AppDbContext</c>'s fingerprint, computed once per process.</summary>
    public static Result Default => DefaultResult.Value;

    public static Result Compute(Type contextType)
    {
        var migrations = contextType.Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Migration).IsAssignableFrom(t)
                && t.GetCustomAttribute<DbContextAttribute>()?.ContextType == contextType
                && t.GetCustomAttribute<MigrationAttribute>() is not null)
            .Select(t => (Id: t.GetCustomAttribute<MigrationAttribute>()!.Id, Type: t))
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToArray();

        var text = new StringBuilder(1 << 20);
        foreach (var (id, type) in migrations)
        {
            var migration = (Migration)Activator.CreateInstance(type)!;
            migration.ActiveProvider = Provider;
            text.Append("migration ").Append(id).Append('\n');
            foreach (var operation in migration.UpOperations)
            {
                Write(text, operation, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
                text.Append('\n');
            }
        }

        return new Result(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())), [.. migrations.Select(m => m.Id)]);
    }

    /// <summary>The serialized form, for tests that pin what the fingerprint sees.</summary>
    internal static string Serialize(object? value)
    {
        var text = new StringBuilder();
        Write(text, value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return text.ToString();
    }

    private static void Write(StringBuilder text, object? value, int depth, HashSet<object> path)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                return;
            case string s:
                text.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                return;
            case Type t:
                text.Append("type:").Append(t.FullName);
                return;
            case byte[] bytes:
                text.Append("0x").Append(Convert.ToHexString(bytes));
                return;
            case DateTime dt:
                text.Append(dt.ToString("O", CultureInfo.InvariantCulture)).Append(':').Append(dt.Kind);
                return;
            case DateTimeOffset dto:
                text.Append(dto.ToString("O", CultureInfo.InvariantCulture));
                return;
            case Enum e:
                text.Append(e.GetType().Name).Append('.').Append(e);
                return;
            case IFormattable f when value.GetType().IsPrimitive || value is decimal or Guid or TimeSpan or DateOnly or TimeOnly:
                text.Append(value.GetType().Name).Append(':').Append(f.ToString(null, CultureInfo.InvariantCulture));
                return;
            case bool b:
                text.Append(b ? "true" : "false");
                return;
            case char c:
                text.Append("char:").Append((int)c);
                return;
        }

        if (depth >= MaxDepth || !path.Add(value))
        {
            text.Append("<cycle>");
            return;
        }

        try
        {
            if (value is Array array)
            {
                text.Append("array[");
                for (var r = 0; r < array.Rank; r++)
                    text.Append(r == 0 ? "" : ",").Append(array.GetLength(r));
                text.Append("](");
                var first = true;
                foreach (var item in array)
                {
                    if (!first) text.Append(',');
                    first = false;
                    Write(text, item, depth + 1, path);
                }
                text.Append(')');
                return;
            }

            if (value is IEnumerable sequence && value is not IAnnotatable)
            {
                text.Append('[');
                var first = true;
                foreach (var item in sequence)
                {
                    if (!first) text.Append(',');
                    first = false;
                    Write(text, item, depth + 1, path);
                }
                text.Append(']');
                return;
            }

            var type = value.GetType();
            // EF operation graphs are walked property by property. Anything else (a provider value
            // object, a JSON default) is identified by its type and invariant text.
            if (!IsEfType(type))
            {
                text.Append(type.FullName).Append('=');
                text.Append(value is IFormattable formattable
                    ? formattable.ToString(null, CultureInfo.InvariantCulture)
                    : value.ToString());
                return;
            }

            text.Append(type.Name).Append('{');
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                         .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                text.Append(property.Name).Append('=');
                Write(text, property.GetValue(value), depth + 1, path);
                text.Append(';');
            }

            if (value is IReadOnlyAnnotatable annotatable)
            {
                text.Append("@[");
                foreach (var annotation in annotatable.GetAnnotations().OrderBy(a => a.Name, StringComparer.Ordinal))
                {
                    text.Append(annotation.Name).Append('=');
                    Write(text, annotation.Value, depth + 1, path);
                    text.Append(';');
                }
                text.Append(']');
            }
            text.Append('}');
        }
        finally
        {
            path.Remove(value);
        }
    }

    private static bool IsEfType(Type type) =>
        !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        && (type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("Npgsql.EntityFrameworkCore", StringComparison.Ordinal) == true);
}
