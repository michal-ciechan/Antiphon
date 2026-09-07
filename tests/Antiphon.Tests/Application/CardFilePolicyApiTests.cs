using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ClassDataSource<CardFileSyncEndpointWebAppFactory>(Shared = SharedType.PerClass)]
public class CardFilePolicyApiTests(CardFileSyncEndpointWebAppFactory factory)
{
    private async Task<Guid> BoardAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = new Project { Id = Guid.NewGuid(), Name = $"c408-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Projects.Add(project); await db.SaveChangesAsync();
        return (await scope.ServiceProvider.GetRequiredService<BoardService>().CreateAsync(new(project.Id, "C408 scoped notes"), default)).Id;
    }

    [Test]
    [Arguments("Inherit", "Unknown")] [Arguments("Private", "Private")] [Arguments("Public", "Public")]
    [Arguments("pRiVaTe", "pUbLiC")]
    public async Task Valid_enum_names_round_trip_and_project_omission_preserves_permission(string cardPolicy, string repositoryPolicy)
    {
        var board = await BoardAsync(); using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/boards/{board}/cards", new { title = "public", cardFileVisibility = cardPolicy });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cardFileVisibility").GetString().ShouldBe(Enum.Parse<CardFileVisibility>(cardPolicy, true).ToString());
        var create = await client.PostAsJsonAsync("/api/projects", new { name = $"c408-enum-{Guid.NewGuid():N}", gitRepositoryUrl = "https://example.invalid/c408.git", repositoryVisibility = repositoryPolicy });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var project = await create.Content.ReadFromJsonAsync<JsonElement>();
        project.GetProperty("repositoryVisibility").GetString().ShouldBe(Enum.Parse<RepositoryVisibility>(repositoryPolicy, true).ToString());
        var update = await client.PutAsJsonAsync($"/api/projects/{project.GetProperty("id").GetGuid()}", new { name = project.GetProperty("name").GetString(), gitRepositoryUrl = "https://example.invalid/c408.git" });
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("repositoryVisibility").GetString().ShouldBe(project.GetProperty("repositoryVisibility").GetString());
    }

    [Test]
    public async Task Every_non_content_revision_is_refused_and_empty_historical_snapshot_is_no_store()
    {
        var board = await BoardAsync(); using var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync($"/api/boards/{board}/cards", new { title = "public" });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var kind in Enum.GetValues<CardRevisionKind>()) db.CardRevisions.Add(new CardRevision { Id = Guid.NewGuid(), CardId = id, Kind = kind, RevisionNumber = 100+(int)kind, PrivateNotes = kind == CardRevisionKind.ContentEdit ? "" : "C408_NONCONTENT", Reason = "synthetic", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        foreach (var kind in Enum.GetValues<CardRevisionKind>())
        {
            var response = await client.GetAsync($"/api/cards/{id}/private-notes?revisionNumber={100+(int)kind}");
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            response.StatusCode.ShouldBe(kind == CardRevisionKind.ContentEdit ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            if (kind == CardRevisionKind.ContentEdit) (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("privateNotes").GetString().ShouldBe("");
            else (await response.Content.ReadAsStringAsync()).ShouldNotContain("C408_NONCONTENT");
        }
    }

    [Test]
    [Arguments("0.5", 422)] [Arguments("\" Public \"", 422)]
    [Arguments("0", 422)] [Arguments("99", 422)] [Arguments("\"0\"", 422)]
    [Arguments("\"bogus\"", 422)] [Arguments("\"\"", 422)]
    [Arguments("true", 400)] [Arguments("[]", 400)] [Arguments("{}", 400)]
    public async Task Invalid_repository_visibility_rejects_create_atomically(string token, int expected)
    {
        using var client = factory.CreateClient();
        var name = $"c408-invalid-{Guid.NewGuid():N}";
        var response = await client.PostAsync("/api/projects", new StringContent("{\"name\":\""+name+"\",\"gitRepositoryUrl\":\"https://example.invalid/c408.git\",\"repositoryVisibility\":"+token+"}", Encoding.UTF8, "application/json"));
        ((int)response.StatusCode).ShouldBe(expected);
        if (expected == 422) (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("repositoryVisibility", out _).ShouldBeTrue();
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Projects.AnyAsync(p => p.Name == name)).ShouldBeFalse();
    }

    [Test]
    [Arguments("{\"syncCardFiles\":false}", 422)]
    [Arguments("{\"expectedSyncCardFiles\":false}", 422)]
    [Arguments("{\"syncCardFiles\":null,\"expectedSyncCardFiles\":false}", 422)]
    [Arguments("{\"syncCardFiles\":false,\"expectedSyncCardFiles\":null}", 422)]
    [Arguments("{\"syncCardFiles\":0,\"expectedSyncCardFiles\":false}", 400)]
    [Arguments("{\"syncCardFiles\":\"true\",\"expectedSyncCardFiles\":false}", 400)]
    public async Task Settings_reject_missing_null_and_wrong_primitive_without_saving(string body, int expected)
    {
        var board = await BoardAsync(); using var client = factory.CreateClient();
        var response = await client.PutAsync($"/api/boards/{board}/card-files/settings", new StringContent(body, Encoding.UTF8, "application/json"));
        ((int)response.StatusCode).ShouldBe(expected);
        (await client.GetFromJsonAsync<JsonElement>($"/api/boards/{board}/card-files/status")).GetProperty("syncCardFiles").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task Explicit_notes_identifier_scope_collision_missing_and_cross_card_history_are_no_store()
    {
        var first = await BoardAsync(); var second = await BoardAsync(); var empty = await BoardAsync();
        using var client = factory.CreateClient();
        async Task<JsonElement> Create(Guid board, string notes) => await (await client.PostAsJsonAsync($"/api/boards/{board}/cards", new { title = "public", privateNotes = notes })).Content.ReadFromJsonAsync<JsonElement>();
        var a = await Create(first, "C408_FIRST"); var b = await Create(second, "C408_SECOND");
        var idA = a.GetProperty("id").GetGuid(); var idB = b.GetProperty("id").GetGuid();
        // Use one unique board-scoped identifier to make the ambiguity fixture independent of other tests.
        var identifier = "CARD-" + Random.Shared.Next(100000000, 999999999);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Cards.Where(c => c.Id == idA || c.Id == idB).ExecuteUpdateAsync(s => s.SetProperty(c => c.Identifier, identifier));
            db.CardRevisions.Add(new CardRevision { Id = Guid.NewGuid(), CardId = idB, RevisionNumber = 51, Kind = CardRevisionKind.ContentEdit, PrivateNotes = "C408_OTHER_HISTORY", CreatedAt = DateTime.UtcNow, Reason = "synthetic" });
            await db.SaveChangesAsync();
        }
        var current = await client.GetAsync($"/api/cards/{identifier}/private-notes?boardId={first}");
        current.StatusCode.ShouldBe(HttpStatusCode.OK); current.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await current.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("privateNotes").GetString().ShouldBe("C408_FIRST");
        foreach (var (path, code) in new[] {
            ($"{identifier}/private-notes", HttpStatusCode.Conflict),
            ($"{identifier}/private-notes?boardId={empty}", HttpStatusCode.NotFound),
            ($"{Guid.NewGuid()}/private-notes", HttpStatusCode.NotFound),
            ($"{idA}/private-notes?revisionNumber=51", HttpStatusCode.NotFound) })
        {
            var response = await client.GetAsync("/api/cards/"+path);
            response.StatusCode.ShouldBe(code); response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            (await response.Content.ReadAsStringAsync()).ShouldNotContain("C408_OTHER_HISTORY");
        }
        var root = Directory.CreateTempSubdirectory("c408-note-scope").FullName;
        try {
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            var token = Guid.NewGuid().ToString("N"); var taskId = Guid.NewGuid();
            using (var scope = factory.Services.CreateScope()) {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var projectId = await db.Boards.Where(bd => bd.Id == first).Select(bd => bd.ProjectId).SingleAsync();
                await db.Projects.Where(pr => pr.Id == projectId).ExecuteUpdateAsync(set => set.SetProperty(pr => pr.LocalRepositoryPath, root));
                db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, Title = "C408 synthetic scoped read", Goal = "Explicitly read synthetic private notes in this test.", WorkingDirectory = root, CardId = idA, TokenHash = AgentTaskService.HashToken(token), Kind = AgentTaskKind.Orchestrator, Status = AgentTaskStatus.Working, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var cwd = await client.GetAsync($"/api/cards/{identifier}/private-notes?cwd={Uri.EscapeDataString(nested.Replace('\\', '/'))}");
            cwd.StatusCode.ShouldBe(HttpStatusCode.OK); cwd.Headers.CacheControl!.NoStore.ShouldBeTrue();
            (await cwd.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("privateNotes").GetString().ShouldBe("C408_FIRST");
            client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
            var scoped = await client.GetAsync($"/api/cards/{identifier}/private-notes");
            scoped.StatusCode.ShouldBe(HttpStatusCode.OK); scoped.Headers.CacheControl!.NoStore.ShouldBeTrue();
            (await scoped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("privateNotes").GetString().ShouldBe("C408_FIRST");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Notes_limit_counts_UTF16_and_omitted_edit_preserves_note_and_policy()
    {
        var board = await BoardAsync(); using var client = factory.CreateClient();
        var notes = string.Concat(Enumerable.Repeat("\U0001F512", 10000));
        notes.Length.ShouldBe(20000);
        var created = await client.PostAsJsonAsync($"/api/boards/{board}/cards", new { title = "public", privateNotes = notes, cardFileVisibility = "Private" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var card = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = card.GetProperty("id").GetGuid();
        var edited = await client.PatchAsJsonAsync($"/api/cards/{id}/content", new { title = "new public", concurrencyToken = card.GetProperty("concurrencyToken").GetGuid(), reason = "title only" });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cardFileVisibility").GetString().ShouldBe("Private");
        (await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}/private-notes")).GetProperty("privateNotes").GetString().ShouldBe(notes);
        var refused = await client.PostAsJsonAsync($"/api/boards/{board}/cards", new { title = "public", privateNotes = notes + "x" });
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var limits = await client.GetFromJsonAsync<JsonElement>("/api/cards/limits");
        limits.GetProperty("maxPrivateNotesLength").GetInt32().ShouldBe(20000);
    }
    [Test]
    [Arguments("true")] [Arguments("5")] [Arguments("[]")] [Arguments("{}")]
    public async Task Wrong_private_notes_primitive_is_sanitized_400_before_create(string value)
    {
        var board = await BoardAsync(); using var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/boards/{board}/cards", new StringContent("{\"title\":\"C408_NO_ECHO\",\"privateNotes\":"+value+"}", Encoding.UTF8, "application/json"));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest); (await response.Content.ReadAsStringAsync()).ShouldNotContain("C408_NO_ECHO");
        using var scope = factory.Services.CreateScope(); (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Cards.AnyAsync(c => c.BoardId == board)).ShouldBeFalse();
    }

    [Test]
    public async Task Board_status_has_the_closed_wire_field_set_with_explicit_nullable_fields()
    {
        var board = await BoardAsync(); using var client = factory.CreateClient();
        var status = await client.GetFromJsonAsync<JsonElement>($"/api/boards/{board}/card-files/status");
        status.EnumerateObject().Select(p => p.Name).Order().ShouldBe(new[] { "boardId", "enabled", "syncCardFiles", "repositoryVisibility", "visibilitySource", "repositoryPath", "directory", "eligible", "reason", "warnings", "ignored", "workingTreeRemovalPending", "gitRemovalPending", "removalPending", "autoCommit", "intervalSeconds" }.Order());
        status.GetProperty("repositoryVisibility").GetString().ShouldBe("Unknown"); status.GetProperty("ignored").ValueKind.ShouldBe(JsonValueKind.Null);
    }

}
