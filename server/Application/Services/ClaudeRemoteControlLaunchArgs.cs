using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0306. Per-launch Claude argv overlay that turns auto-connect off.
/// <c>remoteControlAtStartup: false</c> via <c>--settings &lt;file&gt;</c> — never inline JSON
/// (CARD-0101 quote hazard), never a user/project settings edit, never
/// <c>disableRemoteControl</c> (that would refuse the opt-in). Kind-gated to
/// <see cref="AgentKind.ClaudeCode"/>: this is a claude.exe flag, not a
/// <see cref="RemoteControlPolicy"/> decision.
/// </summary>
public static class ClaudeRemoteControlLaunchArgs
{
    public const string SettingsFlag = "--settings";
    public const string OffFileName = "claude-remote-control-off.json";
    public const string OffSettingsJson = "{\"remoteControlAtStartup\":false}";
    internal const string MergedFilePrefix = "claude-remote-control-off-merged-";

    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };
    private static readonly object FileGate = new();

    /// <summary>
    /// Absolute path of the shipped one-key overlay, next to the running assembly.
    /// Written on first use if the copy-to-output file is missing (test hosts).
    /// </summary>
    public static string OffSettingsPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, OffFileName));

    public static IReadOnlyList<string> ApplyOff(AgentKind kind, IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (kind != AgentKind.ClaudeCode)
            return args;

        var offPath = EnsureOffSettingsFile();
        if (FindSettings(args) is not { } existing)
            return AppendSettings(args, offPath);

        if (AlreadyOff(existing.Payload))
            return CountSettingsFlags(args) == 1
                ? args
                : CollapseToSingleSettings(args, existing.Index, existing.EqualsForm, existing.Payload);

        var mergedPath = WriteMerged(existing.Payload);
        return ReplaceSettingsValue(args, existing, mergedPath);
    }

    internal static string EnsureOffSettingsFile()
    {
        var path = OffSettingsPath;
        lock (FileGate)
        {
            if (File.Exists(path) && AlreadyOff(path))
                return path;
            File.WriteAllText(path, OffSettingsJson);
            return path;
        }
    }

    private static IReadOnlyList<string> AppendSettings(IReadOnlyList<string> args, string path)
    {
        var copy = new List<string>(args.Count + 2);
        copy.AddRange(args);
        copy.Add(SettingsFlag);
        copy.Add(path);
        return copy;
    }

    private static IReadOnlyList<string> CollapseToSingleSettings(
        IReadOnlyList<string> args,
        int keepIndex,
        bool equalsForm,
        string value)
    {
        var copy = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            if (i == keepIndex)
            {
                copy.Add(equalsForm ? SettingsFlag + "=" + value : SettingsFlag);
                if (!equalsForm)
                {
                    copy.Add(value);
                    if (i + 1 < args.Count && !args[i + 1].StartsWith('-'))
                        i++;
                }

                continue;
            }

            if (IsSettingsFlag(args[i]))
            {
                if (!args[i].StartsWith(SettingsFlag + "=", StringComparison.Ordinal)
                    && i + 1 < args.Count
                    && !args[i + 1].StartsWith('-'))
                {
                    i++;
                }

                continue;
            }

            copy.Add(args[i]);
        }

        return copy;
    }

    private static IReadOnlyList<string> ReplaceSettingsValue(
        IReadOnlyList<string> args,
        SettingsHit existing,
        string newValue)
    {
        var copy = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            if (i == existing.Index)
            {
                copy.Add(existing.EqualsForm ? SettingsFlag + "=" + newValue : SettingsFlag);
                if (!existing.EqualsForm)
                {
                    copy.Add(newValue);
                    if (i + 1 < args.Count && !args[i + 1].StartsWith('-'))
                        i++;
                }

                continue;
            }

            if (IsSettingsFlag(args[i]))
            {
                if (!args[i].StartsWith(SettingsFlag + "=", StringComparison.Ordinal)
                    && i + 1 < args.Count
                    && !args[i + 1].StartsWith('-'))
                {
                    i++;
                }

                continue;
            }

            copy.Add(args[i]);
        }

        return copy;
    }

    private static SettingsHit? FindSettings(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith(SettingsFlag + "=", StringComparison.Ordinal))
                return new SettingsHit(i, arg[(SettingsFlag.Length + 1)..], EqualsForm: true);

            if (arg == SettingsFlag)
            {
                var payload = i + 1 < args.Count && !args[i + 1].StartsWith('-')
                    ? args[i + 1]
                    : "";
                return new SettingsHit(i, payload, EqualsForm: false);
            }
        }

        return null;
    }

    private static bool IsSettingsFlag(string arg) =>
        arg == SettingsFlag || arg.StartsWith(SettingsFlag + "=", StringComparison.Ordinal);

    private static int CountSettingsFlags(IReadOnlyList<string> args)
    {
        var n = 0;
        for (var i = 0; i < args.Count; i++)
        {
            if (!IsSettingsFlag(args[i]))
                continue;
            n++;
            if (args[i] == SettingsFlag
                && i + 1 < args.Count
                && !args[i + 1].StartsWith('-'))
            {
                i++;
            }
        }

        return n;
    }

    private static bool AlreadyOff(string value)
    {
        var json = ReadJson(value);
        if (json is null)
            return false;
        return json["remoteControlAtStartup"] is JsonValue flag
            && flag.GetValueKind() == JsonValueKind.False;
    }

    private static string WriteMerged(string existingValue)
    {
        var obj = ReadJson(existingValue) ?? new JsonObject();
        obj["remoteControlAtStartup"] = false;
        var json = obj.ToJsonString(CompactJson);
        var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..8];
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, MergedFilePrefix + stamp + ".json"));
        lock (FileGate)
        {
            if (!File.Exists(path))
                File.WriteAllText(path, json);
        }

        return path;
    }

    private static JsonObject? ReadJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.TrimStart().StartsWith('{')
            ? value
            : File.Exists(value) ? File.ReadAllText(value) : null;
        if (text is null)
            return null;

        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private readonly record struct SettingsHit(int Index, string Payload, bool EqualsForm);
}
