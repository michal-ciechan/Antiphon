using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0334 S1 — instruction-file stamps share <see cref="InstructionBundle.Version"/>'s hash
/// rule, omit missing files, and are CRLF-insensitive.
/// </summary>
[Category("Unit")]
public class InstructionFileStampTests
{
    [Test]
    public void the_hash_rule_matches_InstructionBundle_Version()
    {
        var bundle = InstructionBundles.Get(InstructionBundles.BoardApi);
        var cwd = NewCwd();
        try
        {
            File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), bundle.Text.Replace("\n", "\r\n"));

            var stamp = InstructionFileStamps.Compute(cwd, ["AGENTS.md"]);

            stamp.Files.ShouldHaveSingleItem().Version.ShouldBe(bundle.Version);
            stamp.StampLine.ShouldBe($"AGENTS.md v{bundle.Version}");
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
        }
    }

    [Test]
    public void a_missing_file_is_omitted_and_does_not_fail_the_stamp()
    {
        var cwd = NewCwd();
        try
        {
            File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), "floor\n");

            var stamp = InstructionFileStamps.Compute(
                cwd, ["CLAUDE.md", "AGENTS.md", "docs/orchestration-loop.md"]);

            stamp.Files.Select(f => f.RelativePath).ShouldBe(["AGENTS.md"]);
            stamp.StampLine.ShouldNotContain("CLAUDE.md");
            stamp.StampLine.ShouldNotContain("orchestration-loop");
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
        }
    }

    [Test]
    public void crlf_and_lf_checkouts_of_the_same_text_share_a_version()
    {
        var cwdLf = NewCwd();
        var cwdCrlf = NewCwd();
        try
        {
            const string body = "You are the floor.\nSecond line.\n";
            File.WriteAllText(Path.Combine(cwdLf, "AGENTS.md"), body);
            File.WriteAllText(Path.Combine(cwdCrlf, "AGENTS.md"), body.Replace("\n", "\r\n"));

            var lf = InstructionFileStamps.Compute(cwdLf, ["AGENTS.md"]);
            var crlf = InstructionFileStamps.Compute(cwdCrlf, ["AGENTS.md"]);

            lf.StampLine.ShouldBe(crlf.StampLine);
            lf.Files.ShouldHaveSingleItem().Version.Length.ShouldBe(8);
        }
        finally
        {
            Directory.Delete(cwdLf, recursive: true);
            Directory.Delete(cwdCrlf, recursive: true);
        }
    }

    [Test]
    public void composition_order_follows_the_file_list_not_the_filesystem()
    {
        var cwd = NewCwd();
        try
        {
            Directory.CreateDirectory(Path.Combine(cwd, "docs"));
            File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), "agents\n");
            File.WriteAllText(Path.Combine(cwd, "CLAUDE.md"), "claude\n");
            File.WriteAllText(Path.Combine(cwd, "docs", "ops-http.md"), "ops\n");

            var stamp = InstructionFileStamps.Compute(
                cwd, ["CLAUDE.md", "AGENTS.md", "docs/ops-http.md"]);

            stamp.Files.Select(f => f.RelativePath)
                .ShouldBe(["CLAUDE.md", "AGENTS.md", "docs/ops-http.md"]);
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
        }
    }

    [Test]
    public void parent_segments_and_absolute_paths_are_skipped()
    {
        var cwd = NewCwd();
        try
        {
            File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), "ok\n");
            var outside = Path.GetFullPath(Path.Combine(cwd, "..", "escape.md"));

            var stamp = InstructionFileStamps.Compute(
                cwd, ["../escape.md", outside, "AGENTS.md"]);

            stamp.Files.Select(f => f.RelativePath).ShouldBe(["AGENTS.md"]);
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
        }
    }

    [Test]
    public void default_instruction_files_are_the_plan_list()
    {
        PolicyRefreshSettings.DefaultInstructionFiles.ShouldBe([
            "CLAUDE.md",
            "AGENTS.md",
            "docs/orchestration-loop.md",
            "docs/agent-card-lifecycle.md",
            "docs/ops-http.md",
            ".claude/skills/antiphon-delegate/SKILL.md",
        ]);
        var settings = new PolicyRefreshSettings();
        settings.IdleMinutes.ShouldBe(2);
        settings.CooldownMinutes.ShouldBe(30);
        settings.Enabled.ShouldBeTrue();
        settings.InstructionFiles.ShouldBe(PolicyRefreshSettings.DefaultInstructionFiles);
    }

    [Test]
    public void validator_rejects_idle_below_one_and_cooldown_below_five()
    {
        var validator = new SupervisionSettingsValidator();

        var result = validator.Validate(null, new SupervisionSettings
        {
            PolicyRefresh = new PolicyRefreshSettings { IdleMinutes = 0, CooldownMinutes = 4 },
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("IdleMinutes"));
        result.Failures.ShouldContain(f => f.Contains("CooldownMinutes"));
    }

    [Test]
    public void validator_rejects_absolute_paths_and_parent_segments()
    {
        var validator = new SupervisionSettingsValidator();

        var result = validator.Validate(null, new SupervisionSettings
        {
            PolicyRefresh = new PolicyRefreshSettings
            {
                InstructionFiles = ["AGENTS.md", "C:\\Windows\\win.ini", "docs/../secrets.md"],
            },
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("relative"));
        result.Failures.ShouldContain(f => f.Contains(".."));
    }

    [Test]
    public void validator_accepts_the_shipped_defaults()
    {
        var validator = new SupervisionSettingsValidator();

        validator.Validate(null, new SupervisionSettings()).Succeeded.ShouldBeTrue();
    }

    [Test]
    public void a_null_stamp_is_no_evidence_and_never_drift()
    {
        var current = InstructionBundleComposer.Compose([InstructionBundles.BoardApi]);

        var drift = PolicyDrift.Of(null, current.StampLine, null, "AGENTS.md vabcd1234");

        drift.Bundles.ShouldBeEmpty();
        drift.Files.ShouldBeEmpty();
        drift.HasDrift.ShouldBeFalse();
    }

    [Test]
    public void an_edited_bundle_and_an_edited_file_are_listed_separately()
    {
        var current = InstructionBundleComposer.Compose([InstructionBundles.BoardApi]);

        var drift = PolicyDrift.Of(
            "board-api v0000dead",
            current.StampLine,
            "AGENTS.md v0000dead",
            "AGENTS.md vabcd1234, docs/orchestration-loop.md v1111aaaa");

        drift.Bundles.ShouldBe(["board-api"]);
        drift.Files.ShouldBe(["AGENTS.md", "docs/orchestration-loop.md"]);
        drift.HasDrift.ShouldBeTrue();
    }

    [Test]
    public void file_only_drift_does_not_count_as_bundle_drift()
    {
        var current = InstructionBundleComposer.Compose([InstructionBundles.BoardApi]);

        var drift = PolicyDrift.Of(
            current.StampLine,
            current.StampLine,
            "AGENTS.md v0000dead",
            "AGENTS.md vabcd1234");

        drift.Bundles.ShouldBeEmpty();
        drift.Files.ShouldBe(["AGENTS.md"]);
        (drift.Bundles.Count > 0).ShouldBeFalse();
    }

    [Test]
    public void an_empty_launch_stamp_is_drift_once_something_is_composed()
    {
        var current = InstructionBundleComposer.Compose([InstructionBundles.BoardApi]);

        var drift = PolicyDrift.Of(string.Empty, current.StampLine, string.Empty, "AGENTS.md vabcd1234");

        drift.Bundles.ShouldBe(["board-api"]);
        drift.Files.ShouldBe(["AGENTS.md"]);
    }

    [Test]
    public void json_round_trips_camel_case_and_enum_names()
    {
        var drift = new PolicyDrift(
            ["orchestrator"],
            ["AGENTS.md"],
            PolicyRefreshMode.Notify,
            new DateTime(2026, 9, 3, 18, 0, 0, DateTimeKind.Utc));
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));

        var json = JsonSerializer.Serialize(drift, options);
        json.ShouldContain("\"bundles\":[\"orchestrator\"]");
        json.ShouldContain("\"files\":[\"AGENTS.md\"]");
        json.ShouldContain("\"mode\":\"Notify\"");

        var back = JsonSerializer.Deserialize<PolicyDrift>(json, options);
        back!.Bundles.ShouldBe(["orchestrator"]);
        back.Files.ShouldBe(["AGENTS.md"]);
        back.Mode.ShouldBe(PolicyRefreshMode.Notify);
        back.LastRefreshedAt.ShouldBe(drift.LastRefreshedAt);
    }

    [Test]
    public void an_update_that_omits_policyRefreshMode_leaves_it_alone()
    {
        new UpdateAgentRequest("A", "C:\\tmp", null, null, AgentAssignmentPolicy.AutoPick)
            .PolicyRefreshMode.ShouldBeNull();
    }

    private static string NewCwd()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-file-stamp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        return cwd;
    }
}
