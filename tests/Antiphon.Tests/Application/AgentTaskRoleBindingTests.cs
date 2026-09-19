using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0493 S1: every role <c>delegate.ps1</c> will send must bind through the real
/// <c>Program</c> JSON options. An empty goal is refused at the first statement of
/// <c>CreateAsync</c>, so these POSTs create nothing.
/// </summary>
[Category("Integration")]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class AgentTaskRoleBindingTests(AntiphonWebAppFactory factory)
{
    [Test]
    [NotInParallel]
    public async Task C493_every_scripted_role_binds_on_create()
    {
        using var client = factory.CreateClient();
        foreach (var role in ScriptedRoles("delegate.ps1"))
        {
            foreach (var name in new[] { role, role.ToLowerInvariant() })
            {
                using var response = await PostRoleAsync(client, name);
                var body = await response.Content.ReadAsStringAsync();
                var label = $"role={name}";
                response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, $"{label}: {body}");
                using var json = JsonDocument.Parse(body);
                json.RootElement.GetProperty("code").GetString().ShouldBe("validation_failed", $"{label}: {body}");
                GoalErrors(json.RootElement)[0].GetString()
                    .ShouldBe("A goal is required.", $"{label}: {body}");
            }
        }
    }

    [Test]
    [NotInParallel]
    public async Task C493_an_unknown_role_is_the_incident_400()
    {
        using var client = factory.CreateClient();
        using var response = await PostRoleAsync(client, "NotARole");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("detail", out var detail).ShouldBeTrue(body);
        var text = detail.GetString() ?? "";
        text.ShouldContain("could not be converted to Antiphon.Server.Application.Dtos.CreateAgentTaskRequest");
        text.ShouldContain("Path: $.role");
    }

    [Test]
    [NotInParallel]
    public void C493_scripted_roles_equal_the_hand_delegatable_enum()
    {
        var expected = Enum.GetNames<AgentTaskRole>()
            .Where(name => !AgentTaskRoles.IsSpecialist(Enum.Parse<AgentTaskRole>(name)))
            .ToArray();
        ScriptedRoles("delegate.ps1").ShouldBe(expected, ignoreOrder: true);
        ScriptedRoles("routing-pin.ps1").ShouldBe(expected, ignoreOrder: true);
        ScriptedRoles("complexity-chain.ps1")
            .ShouldBe(expected.Append("Any").ToArray(), ignoreOrder: true);
    }

    internal static IReadOnlyList<string> ScriptedRoles(string script)
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", script);
        var lines = File.ReadAllLines(path)
            .Select(static l => l.Trim())
            .Where(static l => l.StartsWith("[ValidateSet('Investigate'", StringComparison.Ordinal))
            .ToArray();
        lines.Length.ShouldBe(1, script + " must have exactly one Role ValidateSet line starting [ValidateSet('Investigate'");
        return Regex.Matches(lines[0], @"'(\w+)'")
            .Select(static m => m.Groups[1].Value)
            .ToArray();
    }

    private static async Task<HttpResponseMessage> PostRoleAsync(HttpClient client, string role)
    {
        using var content = new StringContent(
            "{\"role\":\"" + role + "\",\"goal\":\"\"}",
            Encoding.UTF8,
            "application/json");
        return await client.PostAsync("/api/agent-tasks", content);
    }

    private static JsonElement GoalErrors(JsonElement root)
    {
        var errors = root.GetProperty("errors");
        if (errors.TryGetProperty("Goal", out var goal))
            return goal;
        return errors.GetProperty("goal");
    }
}
