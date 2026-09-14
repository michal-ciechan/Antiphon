using System.Text;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class WorktreeHandleCsv
{
    public (IReadOnlyList<WorktreeLockOwner> Owners, bool Partial, int Omitted) Parse(string csv, string root)
    {
        var rows = ParseRows(csv).ToList();
        if (rows.Count == 0 || !rows[0].SequenceEqual(new[] { "Process", "PID", "Type", "Handle", "Name" }))
            throw new FormatException("UnknownCsvSchema");
        var owners = new List<WorktreeLockOwner>(); var partial = false; var omitted = 0;
        foreach (var row in rows.Skip(1))
        {
            if (row.Length != 5 || !int.TryParse(row[1], out var pid) || pid <= 0)
                throw new FormatException("MalformedCsv");
            if (row[2] != "File") continue;
            if (!Path.IsPathFullyQualified(row[4]) || row[4].StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
            { partial = true; continue; }
            var path = WorktreeNativeIO.Normalize(row[4]);
            if (!WorktreeNativeIO.Within(path, root)) continue;
            var name = Sanitize(row[0], 80);
            var relative = Path.GetRelativePath(Path.TrimEndingDirectorySeparator(root), path);
            if (relative.Length > 512) { partial = true; omitted++; continue; }
            var owner = new WorktreeLockOwner(name, pid, Sanitize(relative, 512));
            if (owners.Contains(owner)) continue;
            if (owners.Count >= 32) { partial = true; omitted++; continue; }
            owners.Add(owner);
        }
        return (owners, partial, omitted);
    }

    internal static string Sanitize(string value, int max) => new(value.Where(c => !char.IsControl(c)).Take(max).ToArray());

    private static IEnumerable<string[]> ParseRows(string csv)
    {
        var row = new List<string>(); var field = new StringBuilder();
        var quoted = false; var closed = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else field.Append(c);
                continue;
            }
            if (c == '"')
            {
                if (field.Length != 0 || closed) throw new FormatException("MalformedCsv");
                quoted = true; continue;
            }
            if (c is ',' or '\r' or '\n')
            {
                row.Add(field.ToString()); field.Clear(); closed = false;
                if (c != ',')
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    if (row.Count > 1 || row[0].Length != 0) yield return row.ToArray();
                    row.Clear();
                }
            }
            else
            {
                if (closed) throw new FormatException("MalformedCsv");
                field.Append(c);
            }
        }
        if (quoted) throw new FormatException("MalformedCsv");
        if (field.Length > 0 || row.Count > 0 || closed) { row.Add(field.ToString()); yield return row.ToArray(); }
    }
}
