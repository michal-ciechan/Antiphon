using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>Composable PostgreSQL literal search over public card fields.</summary>
internal static class CardReadQuery
{
    internal static IQueryable<Card> Create(AppDbContext db, string? query)
    {
        if (query is null)
            return db.Cards.AsNoTracking();

        var pattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%")
            .Replace("_", "\\_") + "%";
        return db.Cards.FromSqlInterpolated($@"
            SELECT c.* FROM ""Cards"" AS c
            WHERE c.""Identifier"" ILIKE {pattern} ESCAPE '\'
               OR c.""Alias"" ILIKE {pattern} ESCAPE '\'
               OR c.""Title"" ILIKE {pattern} ESCAPE '\'
               OR c.""Description"" ILIKE {pattern} ESCAPE '\'
               OR c.""TerminalReason"" ILIKE {pattern} ESCAPE '\'
               OR EXISTS (SELECT 1 FROM jsonb_array_elements_text(c.""LabelsJson"") AS label(value)
                          WHERE label.value ILIKE {pattern} ESCAPE '\')").AsNoTracking();
    }
}
