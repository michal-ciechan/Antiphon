using System.Text.Json.Nodes;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1448: turn a completed <c>WorktreeManager</c> creation into what an interrupted
/// <c>git worktree add</c> leaves behind: the admin <c>locked</c> file, a partial checkout
/// (only <c>.git</c> left), and the schema-2 intent with <c>CreationComplete=false</c>.
/// </summary>
internal static class InterruptedWorktreeCreation
{
    public static string Simulate(string worktreeRoot, string worktreePath, string lockReason = "initializing")
    {
        var gitFile = File.ReadAllText(Path.Combine(worktreePath, ".git")).Trim();
        const string prefix = "gitdir:";
        if (!gitFile.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Not a linked worktree: {worktreePath}");
        var admin = Path.GetFullPath(Path.Combine(worktreePath, gitFile[prefix.Length..].Trim()));
        File.WriteAllText(Path.Combine(admin, "locked"), lockReason);

        foreach (var entry in Directory.EnumerateFileSystemEntries(worktreePath))
        {
            if (Path.GetFileName(entry) == ".git") continue;
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }

        var rewritten = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(worktreeRoot, ".antiphon", "worktrees"), "*.json"))
        {
            var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            if (Path.GetFullPath((string)node["path"]!) != Path.GetFullPath(worktreePath)) continue;
            node["creationComplete"] = false;
            node.Remove("gitDirectory");
            File.WriteAllText(file, node.ToJsonString());
            rewritten++;
        }
        if (rewritten != 1)
            throw new InvalidOperationException($"Expected one creation record for {worktreePath}, found {rewritten}.");
        return admin;
    }
}
