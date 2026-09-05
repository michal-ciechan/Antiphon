using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0398 DTO census and docs pins that do not need a database.</summary>
[Category("Unit")]
public class DelegationCapabilityContractTests
{
    [Test]
    public void get_dto_serialization_has_no_token_shaped_property()
    {
        var forbidden = new[] { "Token", "RawToken", "Secret", "Bearer" };
        foreach (var property in typeof(DelegationCapabilityDto).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var ignored = property.GetCustomAttribute<JsonIgnoreAttribute>() is not null;
            if (forbidden.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                ignored.ShouldBeTrue($"{property.Name} must be [JsonIgnore] or absent on the GET DTO");
        }

        var dto = new DelegationCapabilityDto(
            Guid.NewGuid(),
            "codex-antiphon",
            ["C:\\src\\Antiphon"],
            null,
            null,
            DateTime.UtcNow,
            null,
            null,
            null);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(dto, options);
        using var doc = JsonDocument.Parse(json);
        foreach (var name in forbidden)
        {
            doc.RootElement.TryGetProperty(char.ToLowerInvariant(name[0]) + name[1..], out _)
                .ShouldBeFalse($"GET JSON must not contain '{name}'");
            json.Contains($"\"{name}\"", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        }
    }

    [Test]
    public void docs_contain_capability_ux_sentence()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var ops = File.ReadAllText(Path.Combine(root, "docs", "ops-http.md"));
        ops.ShouldContain("Run `delegate.ps1 -Capability");

        var kinds = File.ReadAllText(Path.Combine(root, "docs", "agent-kinds.md"));
        kinds.ShouldContain("An orchestrator is `ClaudeCode` only");
        kinds.ShouldContain("remoteControl");
        kinds.ShouldContain("409 remote_control_refused");

        var boardApi = File.ReadAllText(Path.Combine(root, "server", "Bundles", "board-api.md"));
        boardApi.ShouldContain("-Capability");
        boardApi.ShouldContain("-Kind Codex");
    }
}
