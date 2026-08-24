namespace Antiphon.Tests.Agents;

/// <summary>
/// Isolated CLAUDE_CONFIG_DIR has no onboarding state, so interactive Claude parks on the
/// first-run theme picker (S4 probe, 2026-08-24) and never reaches the composer.
///
/// Probe-measured mapping: when CLAUDE_CONFIG_DIR is set, the global json is
/// <c>{CLAUDE_CONFIG_DIR}/.claude.json</c> — NOT <c>~/.claude.json</c> (that's only the default
/// when the env var is unset) and NOT a sibling <c>{dir}.json</c>. Pointing CLAUDE_CONFIG_DIR
/// at the real ~/.claude therefore looks for ~/.claude/.claude.json and re-runs onboarding
/// (Claude even printed a restore hint naming that inner path).
/// </summary>
internal static class RealCliStubClaudeConfig
{
    public static void SeedOnboarding(
        string configDir,
        string? approvedApiKey = null,
        string? trustedCwd = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDir);
        Directory.CreateDirectory(configDir);

        File.WriteAllText(
            Path.Combine(configDir, "settings.json"),
            """{"theme":"dark","skipDangerousModePermissionPrompt":true}""");

        // customApiKeyResponses: S4 probe showed "Detected a custom API key in your environment"
        // with default No. Approving the synthetic key (and its last-20 suffix, which is what the
        // TUI displayed) skips that dialog. lastOnboardingVersion matches claude 2.1.241.
        var approved = "[]";
        if (!string.IsNullOrEmpty(approvedApiKey))
        {
            var tail = approvedApiKey.Length <= 20
                ? approvedApiKey
                : approvedApiKey[^20..];
            approved = $"[\"{Escape(approvedApiKey)}\",\"{Escape(tail)}\"]";
        }

        var projects = "{}";
        if (!string.IsNullOrEmpty(trustedCwd))
        {
            var key = Path.GetFullPath(trustedCwd).Replace('\\', '/');
            projects = "{\"" + Escape(key) + "\":{\"hasTrustDialogAccepted\":true,"
                + "\"hasCompletedProjectOnboarding\":true}}";
        }

        File.WriteAllText(
            Path.Combine(configDir, ".claude.json"),
            "{\"hasCompletedOnboarding\":true,\"lastOnboardingVersion\":\"2.1.241\",\"theme\":\"dark\","
            + "\"numStartups\":1,\"customApiKeyResponses\":{\"approved\":" + approved + "},"
            + "\"projects\":" + projects + "}");
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
