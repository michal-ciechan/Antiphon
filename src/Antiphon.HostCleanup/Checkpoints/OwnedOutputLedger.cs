using Antiphon.HostCleanup.Checkpoints;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class OwnedOutputLedger
{
    public static string? Seal(string root, DateTimeOffset? buildExited = null) =>
        new CheckpointOutputLedger(CheckpointCleanupDefaults.Context).Seal(root, buildExited);
    public static int Adopt(string root, DateTimeOffset? hostExited) =>
        new CheckpointOutputLedger(CheckpointCleanupDefaults.Context).Adopt(root, hostExited);
    public static string? Check(string root) =>
        new CheckpointOutputLedger(CheckpointCleanupDefaults.Context).Check(root);
    public static string? Remove(string root, bool dryRun = false) =>
        new CheckpointOutputLedger(CheckpointCleanupDefaults.Context).Remove(root, dryRun);
}

public sealed class CheckpointOutputLedger(CheckpointCleanupContext context)
{
    private ICheckpointCleanupIo Io => context.Io;
    private CheckpointWrite SafeWrite => new(context);
    private const string Ledger = ".checkpoint-output-ledger.json";
    public string? Seal(string root, DateTimeOffset? buildExited = null)
    {
        try
        {
            if (!Io.FileExists(Path.Combine(root, ".checkpoint-output-owner.json"))) return "output-owner-missing";
            var entries = Inventory(root);
            // CARD-1242: adopt only entries inside the custody window of this process's fresh root
            // and the driver-observed build exit, with size and hash taken now (OutputCustody holds
            // the threat model). Later additions, changes and links still refuse; a root this
            // process did not create, or a seal without an observed exit, adopts nothing.
            var window = SafeWrite.WasCreated(root) ? OutputCustody.For(context, root, buildExited) : null;
            if (window is not null)
                foreach (var relative in entries.Keys)
                    SafeWrite.AdoptCreated(Path.Combine(root, relative), window);
            return SafeWrite.Text(Path.Combine(root, Ledger), JsonSerializer.Serialize(entries), SafeWrite.Under(root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return "output-inventory-failed"; }
    }

    // CARD-1368: adopt new host-written entries only inside the fresh root's custody window.
    // Existing records win; no observed exit, a foreign root or a linked tree grants no authority.
    // The disk ledger remains the diagnostic build-time inventory.
    public int Adopt(string root, DateTimeOffset? hostExited)
    {
        try
        {
            if (!Io.DirectoryExists(root) || !Io.FileExists(Path.Combine(root, Ledger))) return 0;
            var window = SafeWrite.WasCreated(root) ? OutputCustody.For(context, root, hostExited) : null;
            if (window is null) return 0;
            var adopted = 0;
            foreach (var relative in Inventory(root).Keys)
            {
                var full = Path.Combine(root, relative);
                if (SafeWrite.CreationRecord(full) is not null) continue;
                SafeWrite.AdoptCreated(full, window);
                if (SafeWrite.CreationRecord(full) is not null) adopted++;
            }
            return adopted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    public string? Check(string root)
    {
        try
        {
            var preflight = SafeWrite.Preflight(root, [root]);
            if (preflight is not null) return preflight;
            if (!Io.FileExists(Path.Combine(root, Ledger))) return "output-ledger-missing";
            if (!SafeWrite.WasCreated(root)) return "output-memory-authority-missing; left in place: " + root + "; remove it manually";
            if (!SafeWrite.MatchesIdentity(root)) return "output-identity-mismatch; left in place: " + root;
            var actual = Inventory(root);
            foreach (var path in actual.Keys.Append(Ledger))
                if (!SafeWrite.MatchesCreation(Path.Combine(root, path)))
                    return "output-ledger-mismatch; left in place: " + Path.Combine(root, path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return "output-inventory-failed"; }
    }

    public string? Remove(string root, bool dryRun = false) => Remove(root, dryRun, requireRepository: false);

    // CARD-2052 Review 2b79e113 (round 3) blocker 2: the repository that owns the output when the removal starts is
    // the one every entry's tracked check must be answered in. requireRepository (a caller that admitted the output
    // only inside a repository) refuses the whole removal when none is found now; with or without it, a removal that
    // started under a repository refuses any entry whose checks no longer see that same repository. An output
    // deliberately outside every repository keeps its separate policy (no repository, nothing tracked).
    internal string? Remove(string root, bool dryRun, bool requireRepository)
    {
        var refusal = Check(root);
        if (refusal is not null || dryRun) return refusal;
        var repository = new CheckpointDelete(context).OwnerIdentity(root);
        if (repository is null && requireRepository)
            return PerEntryRefusal(root, "directory", "preflight=repository-unavailable");
        // No recursive deletion: an unrecorded late addition keeps its directory alive.
        var entries = Inventory(root).Keys.Append(Ledger).OrderByDescending(path => path.Count(c => c == '/')).ToList();
        // CARD-2052: every entry is still checked against the index before its unlink; the scope reads the index
        // again only when its identity changed, instead of once per entry.
        var index = new TrackedIndexScope();
        foreach (var relative in entries.Where(path => !Io.DirectoryExists(Path.Combine(root, path))))
        {
            var stopped = DeleteRecorded(root, Path.Combine(root, relative), directory: false, index, repository);
            if (stopped is not null) return stopped;
        }
        foreach (var relative in entries.Where(path => Io.DirectoryExists(Path.Combine(root, path))))
        {
            var stopped = DeleteRecorded(root, Path.Combine(root, relative), directory: true, index, repository);
            if (stopped is not null) return stopped;
        }
        return DeleteRecorded(root, root, directory: true, index, repository);
    }

    private string? DeleteRecorded(string root, string full, bool directory, TrackedIndexScope index,
        RepositoryIdentity? repository)
    {
        // CARD-1520: the persisted refusal must keep which gate stopped the
        // unlink. Preflight already names tracked, link, and git outcomes.
        // A content or creation-record mismatch is a different fact.
        var stage = directory ? "directory" : "file";
        index.BeginDecision();
        if (RepositoryRefusal(full, repository) is { } before)
            return PerEntryRefusal(full, stage, "preflight=" + before);
        if (SafeWrite.Preflight(root, [full], index) is { } preflight)
            return PerEntryRefusal(full, stage, "preflight=" + preflight);
        if (!SafeWrite.MatchesCreation(full))
            return PerEntryRefusal(full, stage, "content-or-creation-record-mismatch");
        if (context.Registry.Identity(full) is not { } expected)
            return "left in place: " + full;
        context.BeforeOwnedUnlink?.Invoke(full);
        // CARD-2052 Review d1e0f4ca D-1: the unlink happens only while the index is still the version the tracked
        // decision above was read from; a stage, reset or rewrite since then refuses this entry.
        // Review 2ff6a0bc (round 2) D-1: a decision that bound no version (git answered it, or the layout has no
        // stamp) is asked again here, after content validation and creation identity: only a fresh untracked answer
        // permits the unlink, and a tracked answer or any check that cannot answer keeps the entry.
        if (index.DecisionBound)
        {
            if (!index.DecisionStillHolds(context))
                return PerEntryRefusal(full, stage, "preflight=index-changed-before-unlink");
        }
        else
        {
            if (SafeWrite.Preflight(root, [full]) is { } recheck)
                return PerEntryRefusal(full, stage, "preflight-after-validation=" + recheck);
            // Review 2b79e113 (round 3) blocker 1: the stamp is re-read after content validation and that fresh lookup.
            // A change to the index or any config input (an included file among them) since the decision keeps the
            // entry, whatever git made of it.
            if (index.ObservationChanged(context))
                return PerEntryRefusal(full, stage, "preflight-after-validation=index-or-config-changed-before-unlink");
        }
        // Review 2b79e113 (round 3) blocker 2: the last observation before the unlink. A fallback check that found no
        // repository (moved, removed or unreadable) answered "nothing tracked" from the absence of evidence; the entry
        // is kept unless the repository the removal started under is still the one that owns it.
        if (RepositoryRefusal(full, repository) is { } after)
            return PerEntryRefusal(full, stage, "preflight-after-validation=" + after);
        try
        {
            Io.DeleteAnchored(full, expected, directory);
        }
        catch (IOException ex) when (directory && IsNonEmptyDirectory(ex))
        {
            return "left in place: " + full;
        }
        context.AfterOwnedUnlink?.Invoke(full);
        return null;
    }

    private string? RepositoryRefusal(string full, RepositoryIdentity? required)
    {
        if (required is null)
            return null;
        var now = new CheckpointDelete(context).OwnerIdentity(full);
        return now is null ? "repository-unavailable" : now == required ? null : "repository-changed";
    }

    private static string PerEntryRefusal(string full, string stage, string detail) =>
        "left in place: " + full + "; stage=" + stage + "; " + detail;

    // Linux ENOTEMPTY is errno 39. Windows ERROR_DIR_NOT_EMPTY is 145. The virtual
    // fixture says "directory not empty". The code must be the whole token: win32=145
    // is not ERROR_NO_SYSTEM_RESOURCES (1450). A late entry keeps the directory.
    private static bool IsNonEmptyDirectory(IOException ex)
    {
        var message = ex.Message;
        return message.Contains("directory not empty", StringComparison.OrdinalIgnoreCase)
            || HasExactCode(message, "errno=", "39")
            || HasExactCode(message, "win32=", "145");
    }

    private static bool HasExactCode(string message, string label, string code)
    {
        var token = label + code;
        var index = 0;
        while ((index = message.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            var end = index + token.Length;
            if (end >= message.Length || !char.IsAsciiDigit(message[end]))
                return true;
            index = end;
        }
        return false;
    }

    private Dictionary<string, string> Inventory(string root)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(Path.GetFullPath(root));
        while (queue.TryDequeue(out var directory))
        {
            foreach (var path in Io.Enumerate(directory))
            {
                if (Io.LinkTarget(path) is not null
                    || (Io.Attributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("linked output");
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative == Ledger) continue;
                if (Io.DirectoryExists(path)) { entries.Add(relative, "directory"); queue.Enqueue(path); }
                else { entries.Add(relative, "file-size:" + Io.Length(path)); }
            }
        }
        return entries;
    }
}
