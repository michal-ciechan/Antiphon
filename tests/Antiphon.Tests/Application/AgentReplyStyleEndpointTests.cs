using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class AgentReplyStyleEndpointTests(AntiphonWebAppFactory factory)
{
    private readonly List<Guid> _created = [];
    private readonly List<string> _directories = [];

    [Before(Test)]
    public Task ResetAsync() => factory.ResetAsync();

    [After(Test)]
    public async Task CleanupAsync()
    {
        using var client = factory.CreateClient();
        foreach (var id in _created) await client.DeleteAsync($"/api/agents/{id}");
        foreach (var directory in _directories)
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        _created.Clear();
        _directories.Clear();
    }

    [Test]
    public async Task Phone_create_patch_get_and_omission_round_trip()
    {
        using var client = factory.CreateClient();
        var normal = await CreateAsync(client);
        var phone = await CreateAsync(client, "Phone");
        await AssertStyleAsync(client, normal, "Normal", 0);
        await AssertStyleAsync(client, phone, "Phone", 5);
        foreach (var preset in new[] { "worker", "orchestrator" })
        {
            using var scope = factory.Services.CreateScope();
            await SeededWorkflowTemplates.EnsureFullFeaturePipelineAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            var created = await CreateAsync(client, preset: preset);
            await AssertStyleAsync(client, created, "Normal", 0);
        }
        var patch = PreservingPatch(normal);
        patch["replyStyle"] = "Phone";
        normal = await PatchAsync(client, normal, patch);
        await AssertStyleAsync(client, normal, "Phone", 5);
        patch = PreservingPatch(normal);
        patch["details"] = "Changed independently";
        normal = await PatchAsync(client, normal, patch);
        await AssertStyleAsync(client, normal, "Phone", 5);
        patch = PreservingPatch(normal);
        patch["replyStyle"] = "Normal";
        normal = await PatchAsync(client, normal, patch);
        await AssertStyleAsync(client, normal, "Normal", 0);
        using var modelScope = factory.Services.CreateScope();
        var db = modelScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Agent))!;
        entity.GetCheckConstraints().Where(c => c.Sql.Contains("ReplyStyle")).ShouldBeEmpty();
    }

    [Test]
    public async Task Phone_style_update_preserves_other_settings_and_other_agents()
    {
        using var client = factory.CreateClient();
        var created = await CreateAsync(client);
        var sibling = await CreateAsync(client, "Brief");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeededWorkflowTemplates.EnsureFullFeaturePipelineAsync(db);
            var row = await db.Agents.SingleAsync(a => a.Id == Id(created));
            row.Details = "Keep this standing job";
            row.DefaultWorkflowTemplateId = AgentPresets.FullFeaturePipelineTemplateId;
            row.AssignmentPolicy = AgentAssignmentPolicy.ManualConfirm;
            row.AutoCompactEnabled = false;
            row.AutoCompactIdleMinutes = 47;
            row.AutoCompactContextPercent = 71;
            row.SystemPromptAppend = "Keep CRLF\r\nand trailing spaces.  ";
            row.ModelLevel = AgentModelLevel.Medium;
            await AgentBundleAttachments.SetAsync(db, row, [InstructionBundles.BoardApi], DateTime.UtcNow, default);
            await db.SaveChangesAsync();
        }
        created = (await client.GetFromJsonAsync<JsonObject>($"/api/agents/{Id(created)}"))!;
        var body = PreservingPatch(created);
        body["replyStyle"] = "Phone";
        var updated = await PatchAsync(client, created, body);
        await AssertStyleAsync(client, updated, "Phone", 5);
        foreach (var field in created)
        {
            if (field.Key is "replyStyle" or "updatedAt" or "composedBundles") continue;
            JsonNode.DeepEquals(updated[field.Key], field.Value).ShouldBeTrue($"preserve {field.Key}");
        }
        var siblingAfter = await client.GetFromJsonAsync<JsonObject>($"/api/agents/{Id(sibling)}");
        JsonNode.DeepEquals(sibling, siblingAfter).ShouldBeTrue("sibling unchanged");
        using var verifyScope = factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verify.Agents.SingleAsync(a => a.Id == Id(created))).PersistentSessionId.ShouldBeNull();
        (await verify.ChatChannels.AnyAsync(b => b.AgentId == Id(created))).ShouldBeFalse();
    }

    [Test]
    public async Task Phone_manual_bundle_attachment_is_422()
    {
        using var client = factory.CreateClient();
        var created = await CreateAsync(client);
        var request = PreservingPatch(created);
        request["bundleKeys"] = new JsonArray("style-phone");
        foreach (var update in new[] { false, true })
        {
            var response = update
                ? await client.PatchAsJsonAsync($"/api/agents/{Id(created)}", request)
                : await client.PostAsJsonAsync("/api/agents", request);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await response.Content.ReadAsStringAsync()).ShouldContain("two voices");
        }
        var after = await client.GetFromJsonAsync<JsonObject>($"/api/agents/{Id(created)}");
        JsonNode.DeepEquals(created, after).ShouldBeTrue("failed attachment must not partially update the agent");
    }

    private async Task<JsonObject> CreateAsync(HttpClient client, string? style = null, string? preset = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "card0417-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        var body = new JsonObject { ["name"] = "Phone " + Guid.NewGuid().ToString("N"),
            ["workingDirectory"] = directory, ["alwaysOn"] = false };
        if (style is not null) body["replyStyle"] = style;
        if (preset is not null) body["preset"] = preset;
        var response = await client.PostAsJsonAsync("/api/agents", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        _created.Add(Id(created));
        return created;
    }

    private static Guid Id(JsonObject row) => row["id"]!.GetValue<Guid>();

    private static JsonObject PreservingPatch(JsonObject before) => new()
    {
        ["name"] = before["name"]?.DeepClone(),
        ["workingDirectory"] = before["workingDirectory"]?.DeepClone(),
        ["details"] = before["details"]?.DeepClone(),
        ["defaultWorkflowTemplateId"] = before["defaultWorkflowTemplateId"]?.DeepClone(),
        ["assignmentPolicy"] = before["assignmentPolicy"]?.DeepClone(),
        ["autoCompactEnabled"] = before["autoCompactEnabled"]?.DeepClone(),
        ["autoCompactIdleMinutes"] = before["autoCompactIdleMinutes"]?.DeepClone(),
        ["autoCompactContextPercent"] = before["autoCompactContextPercent"]?.DeepClone(),
    };

    private static async Task<JsonObject> PatchAsync(HttpClient client, JsonObject before, JsonObject body)
    {
        var response = await client.PatchAsJsonAsync($"/api/agents/{Id(before)}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task AssertStyleAsync(HttpClient client, JsonObject row, string name, int number)
    {
        row["replyStyle"]!.GetValue<string>().ShouldBe(name);
        var fresh = (await client.GetFromJsonAsync<JsonObject>($"/api/agents/{Id(row)}"))!;
        fresh["replyStyle"]!.GetValue<string>().ShouldBe(name);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ((int)(await db.Agents.AsNoTracking().SingleAsync(a => a.Id == Id(row))).ReplyStyle).ShouldBe(number);
    }
}
