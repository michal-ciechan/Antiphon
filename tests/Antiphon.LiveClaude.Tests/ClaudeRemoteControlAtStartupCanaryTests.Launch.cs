using System.Text.Json;
using Antiphon.TestSupport.LiveClaude;
using Antiphon.Agents.Pty;
using Antiphon.Agents.Pty.Tests;
using TUnit.Core;

namespace Antiphon.LiveClaude.Tests;

// The startup canary's shared fixture, launch, path and sandbox seams. Antiphon.Agents.Pty.Tests compiles this file too
// (LiveClaudeIsolationWiringTests and LiveClaudeStartupCases call these members with owned fakes); the tests themselves
// are only in this project.
public partial class ClaudeRemoteControlAtStartupCanaryTests
{
    // The framework owns this bag for one test session; no process-global cache or UID change.
    internal static LiveClaudeStartupFixture SharedStartup() => (LiveClaudeStartupFixture)
        TestSessionContext.Current.AllTests.OrderBy(test => test.Id, StringComparer.Ordinal).First().StateBag.Items.GetOrAdd("C1545.StartupControl", _ => new LiveClaudeStartupFixture());

    private const string OffSettingsJson = "{\"remoteControlAtStartup\":false}";

    private static string MarkerFile(LiveClaudeProfile profile) => Path.Combine(profile.Context.OwnedRoot, "startup-settings.jsonl");

    internal static void RequireHookConfiguration(LiveClaudeProfile profile)
    {
        var settings = new[] { ("user", Path.Combine(profile.Paths.ConfigDir,"settings.json")),
            ("project",Path.Combine(profile.Paths.Cwd,".claude","settings.json")),
            ("local",Path.Combine(profile.Paths.Cwd,".claude","settings.local.json")) };
        foreach (var (source, file) in settings)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var retained = root.GetProperty("model").GetString() ?? throw new LiveClaudeRefusal("settings-unobserved");
            var matched = false;
            foreach (var hook in root.GetProperty("hooks").GetProperty("SessionStart").EnumerateArray()
                .SelectMany(m => m.GetProperty("hooks").EnumerateArray()))
            {
                if (hook.GetProperty("type").GetString() != "command") continue;
                var command = hook.GetProperty("command").GetString() ?? "";
                string[] tokens;
                try
                {
                    LiveClaudeProfile.RequireHookCommand(command);
                    tokens = LiveClaudeProfile.HookTokens(command);
                }
                catch (LiveClaudeRefusal) { continue; }
                if (tokens[3] != source || tokens[4] != retained || tokens[2] != MarkerFile(profile)) continue;
                var writer = tokens[1];
                if (!profile.Context.DeclaredFiles.TryGetValue(writer, out var digest) || LiveClaudeProfile.HashFile(writer) != digest)
                    throw new LiveClaudeRefusal("settings-unobserved");
                matched = true;
            }
            if (!matched) throw new LiveClaudeRefusal("settings-unobserved");
        }
    }

    internal static LiveClaudeProfileSpec BuildProfileLaunch(LiveClaudeProfile profile, string role, string session, string? overlay = null)
        => ClSession.BuildProfileLaunch(profile, role, session, overlay);
    internal static string? FindSessionJsonl(LiveClaudeProfilePaths paths, string sessionId) => paths.FindSessionJsonl(sessionId);
    internal static (bool Found,string? BridgeId) ReadSessionFile(LiveClaudeProfilePaths paths,string sessionId,string cwd) => paths.ReadSessionFile(sessionId,cwd);

    internal sealed class CanarySandbox : IDisposable
    {
        public LiveClaudeProfile Profile {get;}
        public string Dir => Profile.Paths.Cwd;
        public string SessionId {get;} = Guid.NewGuid().ToString("D");
        public string? SettingsPath {get;}
        private CanarySandbox(LiveClaudeProfile profile,string role)
        {
            Profile=profile;
            if(role is "off" or "slash")
            {
                SettingsPath=Path.Combine(profile.Context.OwnedRoot,"remote-control-off.json");
                File.WriteAllText(SettingsPath,OffSettingsJson);
            }
            Trust(profile);
        }
        public static CanarySandbox Create(LiveClaudeProfile profile,string role) => new(profile,role);
        private static void Trust(LiveClaudeProfile profile)
        {
            if (!ClaudeProjectTrust.IsTrusted(profile.Paths.Cwd, profile.Paths.GlobalState)) throw new LiveClaudeRefusal("trust-unavailable");
        }
        public void Dispose() => Profile.Complete();
    }
}
