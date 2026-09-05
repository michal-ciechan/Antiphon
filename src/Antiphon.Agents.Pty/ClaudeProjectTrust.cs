using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Agents.Pty;

public enum ClaudeProjectTrustOutcome
{
    Seeded = 0,
    AlreadyTrusted = 1,
    NoConfigFile = 2,
    Unparseable = 3,
    Failed = 4,
}

public readonly record struct ClaudeProjectTrustResult(
    ClaudeProjectTrustOutcome Outcome,
    string ConfigPath,
    string Key,
    string? Error = null);

/// <summary>
/// Exact-key seed of <c>projects[&lt;key&gt;].hasTrustDialogAccepted</c> in the runner user's
/// <c>.claude.json</c>. Sidesteps the trust dialog for seats Antiphon creates. Never throws, never
/// creates the file, never walks ancestors (Claude itself does that).
/// </summary>
public static class ClaudeProjectTrust
{
    private static readonly ConcurrentDictionary<string, ClaudeProjectTrustResult> Memo = new();
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// <c>CLAUDE_CONFIG_DIR</c> set → <c>{dir}\.claude.json</c> (measured);
    /// else <c>%UserProfile%\.claude.json</c>.
    /// </summary>
    public static string DefaultConfigPath()
    {
        var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(dir))
            return Path.Combine(dir, ".claude.json");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude.json");
    }

    /// <summary>
    /// <see cref="Path.GetFullPath(string)"/>, trailing separators trimmed, <c>\</c> → <c>/</c>,
    /// case preserved (Claude's <c>D$</c>).
    /// </summary>
    public static string ProjectKey(string directory)
    {
        var full = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Replace('\\', '/');
    }

    /// <summary>Exact-key read. Missing file, unparseable JSON, or absent flag → false.</summary>
    public static bool IsTrusted(string directory, string? configPath = null)
    {
        try
        {
            var path = configPath ?? DefaultConfigPath();
            var key = ProjectKey(directory);
            if (!File.Exists(path))
                return false;
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node?["projects"] is not JsonObject projects)
                return false;
            return projects[key]?["hasTrustDialogAccepted"]?.GetValue<bool>() == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Idempotent, atomic, never throws, memoised per process on Seeded/AlreadyTrusted.
    /// </summary>
    public static ClaudeProjectTrustResult Seed(string directory, string? configPath = null)
    {
        string path;
        string key;
        try
        {
            path = configPath ?? DefaultConfigPath();
            key = ProjectKey(directory);
        }
        catch (Exception ex)
        {
            path = configPath ?? "";
            return new ClaudeProjectTrustResult(
                ClaudeProjectTrustOutcome.Failed, path, directory, ex.Message);
        }

        var memoKey = MemoKey(path, key);
        if (Memo.TryGetValue(memoKey, out var cached))
            return cached;

        try
        {
            var result = SeedCore(path, key);
            if (result.Outcome is ClaudeProjectTrustOutcome.Seeded
                or ClaudeProjectTrustOutcome.AlreadyTrusted)
            {
                Memo[memoKey] = result;
            }
            return result;
        }
        catch (Exception ex)
        {
            return new ClaudeProjectTrustResult(
                ClaudeProjectTrustOutcome.Failed, path, key, ex.Message);
        }
    }

    /// <summary>Test-only counterpart used by the canary's Dispose; not called from production.</summary>
    public static bool Remove(string directory, string? configPath = null)
    {
        try
        {
            var path = configPath ?? DefaultConfigPath();
            var key = ProjectKey(directory);
            Memo.TryRemove(MemoKey(path, key), out _);
            if (!File.Exists(path))
                return false;

            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node?["projects"] is not JsonObject projects)
                return false;
            if (!projects.Remove(key))
                return false;

            return WriteAtomic(path, node);
        }
        catch
        {
            return false;
        }
    }

    private static ClaudeProjectTrustResult SeedCore(string path, string key)
    {
        if (!File.Exists(path))
            return new ClaudeProjectTrustResult(ClaudeProjectTrustOutcome.NoConfigFile, path, key);

        JsonObject node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new JsonException("root is not an object");
        }
        catch (JsonException ex)
        {
            return new ClaudeProjectTrustResult(
                ClaudeProjectTrustOutcome.Unparseable, path, key, ex.Message);
        }

        if (node["projects"] is null)
            node["projects"] = new JsonObject();
        if (node["projects"] is not JsonObject projects)
        {
            return new ClaudeProjectTrustResult(
                ClaudeProjectTrustOutcome.Failed, path, key, "projects is not an object");
        }

        if (projects[key] is JsonObject existing
            && existing["hasTrustDialogAccepted"]?.GetValue<bool>() == true)
        {
            return new ClaudeProjectTrustResult(ClaudeProjectTrustOutcome.AlreadyTrusted, path, key);
        }

        if (projects[key] is null)
            projects[key] = new JsonObject();
        if (projects[key] is not JsonObject project)
        {
            return new ClaudeProjectTrustResult(
                ClaudeProjectTrustOutcome.Failed, path, key, "project entry is not an object");
        }

        project["hasTrustDialogAccepted"] = true;

        if (!WriteAtomic(path, node))
        {
            return new ClaudeProjectTrustResult(
                ClaudeProjectTrustOutcome.Failed, path, key, "atomic replace failed");
        }

        return new ClaudeProjectTrustResult(ClaudeProjectTrustOutcome.Seeded, path, key);
    }

    private static bool WriteAtomic(string path, JsonObject node)
    {
        var tmp = path + ".antiphon-tmp";
        var json = node.ToJsonString(WriteOptions);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                File.WriteAllText(tmp, json, Utf8NoBom);
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch (IOException)
            {
                if (attempt == 0)
                    Thread.Sleep(200);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch (IOException) { }
            }
        }
        return false;
    }

    private static string MemoKey(string path, string key) => path + "\0" + key;
}
