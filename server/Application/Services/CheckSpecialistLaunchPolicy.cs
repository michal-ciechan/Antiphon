using System.Text.Json.Nodes;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Claude Check launch protection. Arming is not capability certification.</summary>
public static class CheckSpecialistLaunchPolicy
{
    public static AgentLaunchSpec Apply(AgentLaunchSpec launch, SpecialistSpec specialist, SessionBackend backend)
    {
        StandingSpecialistProvisioner.RequireCheckLaunchToolPolicy(specialist, launch.Kind);
        if (backend != SessionBackend.PtyHost)
            throw Refused("Check tool policy has not been certified for this session backend.");
        foreach (var arg in launch.Args)
        {
            var flag = arg.Split('=', 2)[0];
            if (flag is "--bare" or "--safe" or "--settings" or "--setting-sources"
                or "--system-prompt" or "--system-prompt-file" or "--append-system-prompt-file"
                or "--system-prompt-snapshot" or "--agent" or "--agents"
                or "--allowedTools" or "--allowed-tools" or "--disallowedTools" or "--disallowed-tools"
                or "--tools" or "--mcp-config" or "--plugin-dir" or "--chrome")
                throw Refused($"Check launch contains an inherited policy override ({flag}).");
        }
        foreach (var name in new[] { "CLAUDE_CODE_SIMPLE", "CLAUDE_CODE_SAFE_MODE" })
        {
            var value = launch.Env.TryGetValue(name, out var configured)
                ? configured : Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value) && value != "0" && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                throw Refused($"Check launch environment disables its hooks ({name}).");
        }
        var appends = launch.Args.Select((value, index) => (value, index))
            .Where(a => a.value == "--append-system-prompt").ToArray();
        if (appends.Length != 1 || appends[0].index + 1 >= launch.Args.Count
            || !launch.Args[appends[0].index + 1].Contains(specialist.Contract, StringComparison.Ordinal))
            throw Refused("Check launch is missing its current interpreter contract.");

        // A final explicit settings source protects against user/local disableAllHooks. The
        // common RC overlay preserves this object because remoteControlAtStartup is already off.
        // These are owned scratch files; no user settings or credentials are read or modified.
        var settings = JsonNode.Parse(specialist.DenyAllToolsSettingsJson)!.AsObject();
        settings["disableAllHooks"] = false;
        settings["remoteControlAtStartup"] = false;
        var path = Path.Combine(specialist.WorkingDirectory, ".antiphon", "check-tool-policy-v1.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, settings.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConflictException("Check launch policy could not be armed.", "specialist_tool_policy_unavailable");
        }
        return launch with { Args = [.. launch.Args, "--settings", path] };
    }

    private static ConflictException Refused(string reason) => new(reason, "specialist_tool_policy_unsupported");
}
