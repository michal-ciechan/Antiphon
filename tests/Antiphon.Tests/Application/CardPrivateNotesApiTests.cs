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
public class CardPrivateNotesApiTests(CardFileSyncEndpointWebAppFactory factory)
{
    private async Task<Guid> BoardAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = new Project { Id = Guid.NewGuid(), Name = $"c408-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.invalid/c408.git", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var board = await scope.ServiceProvider.GetRequiredService<BoardService>().CreateAsync(
            new(project.Id, "C408 test board"), default);
        return board.Id;
    }

    [Test]
    [Arguments("0", 422)]
    [Arguments("-1", 422)]
    [Arguments("2147483648", 400)]
    [Arguments("text", 400)]
    [Arguments("1&revisionNumber=2", 400)]
    public async Task Explicit_notes_revision_errors_are_no_store(string query, int expected)
    {
        var board = await BoardAsync();
        using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync($"/api/boards/{board}/cards", new { title = "public", privateNotes = "C408_DO_NOT_ECHO" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var response = await client.GetAsync($"/api/cards/{id}/private-notes?revisionNumber={query}");
        ((int)response.StatusCode).ShouldBe(expected);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("C408_DO_NOT_ECHO");
    }

    [Test]
    public async Task Historical_note_read_rejects_move_revision_and_distinguishes_unknown_snapshot()
    {
        var board = await BoardAsync();
        using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync($"/api/boards/{board}/cards", new { title = "public" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CardRevisions.Add(new CardRevision { Id = Guid.NewGuid(), CardId = id, RevisionNumber = 1, Kind = CardRevisionKind.Move, Reason = "synthetic", CreatedAt = DateTime.UtcNow });
            db.CardRevisions.Add(new CardRevision { Id = Guid.NewGuid(), CardId = id, RevisionNumber = 2, Kind = CardRevisionKind.ContentEdit, Reason = "synthetic", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var move = await client.GetAsync($"/api/cards/{id}/private-notes?revisionNumber=1");
        move.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        move.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var old = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}/private-notes?revisionNumber=2");
        old.GetProperty("privateNotes").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Create_and_atomic_public_to_private_edit_preserve_notes_and_history()
    {
        var boardId = await BoardAsync();
        using var client = factory.CreateClient();
        const string oldNotes = "  C408_NOTE_OLD\r\n雪 😀 ` $()\n  ";
        const string excerpt = " C409_PRIVATE_EXCERPT\r\n雪 😀 ` $()\n ";
        const string notes = oldNotes + "\n\nCARD-0409 synthetic migration\n" + excerpt;
        var create = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new { title = "public", description = excerpt, privateNotes = oldNotes });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var raw = await create.Content.ReadAsStringAsync();
        raw.ShouldNotContain(oldNotes);
        var created = JsonDocument.Parse(raw).RootElement;
        created.TryGetProperty("privateNotes", out _).ShouldBeFalse();
        created.GetProperty("hasPrivateNotes").GetBoolean().ShouldBeTrue();
        created.GetProperty("cardFileVisibility").GetString().ShouldBe("Inherit");
        var id = created.GetProperty("id").GetGuid();
        var oldToken = created.GetProperty("concurrencyToken").GetGuid();
        var patch = await client.PatchAsJsonAsync($"/api/cards/{id}/content", new {
            concurrencyToken = oldToken, reason = "atomic correction", description = "C408_PUBLIC_BODY",
            privateNotes = notes, cardFileVisibility = "Inherit" });
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await patch.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("concurrencyToken").GetGuid().ShouldNotBe(oldToken);
        updated.GetProperty("revisionCount").GetInt32().ShouldBe(1);
        updated.GetProperty("description").GetString().ShouldBe("C408_PUBLIC_BODY");
        updated.GetProperty("cardFileVisibility").GetString().ShouldBe("Inherit");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var saved = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == id);
            saved.Description.ShouldBe("C408_PUBLIC_BODY");
            saved.PrivateNotes.ShouldBe(notes);
            saved.PrivateNotes!.Length.ShouldBe(notes.Length);
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(saved.PrivateNotes))
                .ShouldBe(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(notes)));
            saved.Title.ShouldBe("public");
            saved.BoardId.ShouldBe(boardId);
            (await db.CardRevisions.CountAsync(r => r.CardId == id && r.Kind == CardRevisionKind.ContentEdit)).ShouldBe(1);
        }
        var current = await client.GetAsync($"/api/cards/{id}/private-notes");
        current.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var currentJson = await current.Content.ReadFromJsonAsync<JsonElement>();
        currentJson.GetProperty("privateNotes").GetString().ShouldBe(notes);
        currentJson.EnumerateObject().Select(p => p.Name).Order().ShouldBe(new[] { "cardId", "privateNotes", "concurrencyToken", "revisionNumber" }.Order());
        var historical = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}/private-notes?revisionNumber=1");
        historical.GetProperty("privateNotes").GetString().ShouldBe(oldNotes);
        foreach (var route in new[] { $"/api/cards/{id}", $"/api/cards?boardId={boardId}", $"/api/boards/{boardId}", $"/api/cards/{id}/revisions", $"/api/cards/{id}/thread" })
        {
            var response = await client.GetAsync(route);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldNotContain("C408_NOTE_");
            body.ShouldNotContain("\"privateNotes\"");
        }
        var stale = await client.PatchAsJsonAsync($"/api/cards/{id}/content", new {
            concurrencyToken = oldToken, reason = "stale", description = "C409_LOSER_BODY", privateNotes = "C408_LOSER", cardFileVisibility = "Private" });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}/private-notes")).GetProperty("privateNotes").GetString().ShouldBe(notes);
        var afterStale = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}");
        foreach (var field in new[] { "description", "cardFileVisibility", "concurrencyToken", "revisionCount" })
            afterStale.GetProperty(field).GetRawText().ShouldBe(updated.GetProperty(field).GetRawText());

        var boundaryNote = new string('x', 19998) + "😀";
        var boundary = await client.PatchAsJsonAsync($"/api/cards/{id}/content", new {
            concurrencyToken = updated.GetProperty("concurrencyToken").GetGuid(), reason = "boundary",
            description = "C409_BOUNDARY_BODY", privateNotes = boundaryNote, cardFileVisibility = "Inherit" });
        boundary.StatusCode.ShouldBe(HttpStatusCode.OK);
        var boundaryCard = await boundary.Content.ReadFromJsonAsync<JsonElement>();
        var overflow = await client.PatchAsJsonAsync($"/api/cards/{id}/content", new {
            concurrencyToken = boundaryCard.GetProperty("concurrencyToken").GetGuid(), reason = "overflow",
            description = "C409_OVERFLOW_BODY", privateNotes = boundaryNote + "x", cardFileVisibility = "Private" });
        overflow.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var afterOverflow = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}");
        foreach (var field in new[] { "description", "cardFileVisibility", "concurrencyToken", "revisionCount" })
            afterOverflow.GetProperty(field).GetRawText().ShouldBe(boundaryCard.GetProperty(field).GetRawText());
        (await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}/private-notes")).GetProperty("privateNotes").GetString().ShouldBe(boundaryNote);
    }

    [Test]
    [Arguments("0", 422)]
    [Arguments("99", 422)]
    [Arguments("0.5", 422)]
    [Arguments("\"0\"", 422)]
    [Arguments("\"bogus\"", 422)]
    [Arguments("\"\"", 422)]
    [Arguments("\" Private \"", 422)]
    [Arguments("true", 400)]
    [Arguments("[]", 400)]
    [Arguments("{}", 400)]
    public async Task Invalid_card_file_visibility_never_falls_back_to_Inherit(string token, int status)
    {
        var boardId = await BoardAsync();
        using var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/boards/{boardId}/cards", new StringContent(
            "{\"title\":\"public\",\"privateNotes\":\"C408_NO_ECHO\",\"cardFileVisibility\":" + token + "}", Encoding.UTF8, "application/json"));
        ((int)response.StatusCode).ShouldBe(status);
        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain("C408_NO_ECHO");
        if (status == 422) JsonDocument.Parse(raw).RootElement.GetProperty("errors").TryGetProperty("cardFileVisibility", out _).ShouldBeTrue();
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Cards.CountAsync(c => c.BoardId == boardId)).ShouldBe(0);
    }

    [Test]
    public async Task Omitted_null_empty_and_limit_inputs_have_distinct_semantics()
    {
        var boardId = await BoardAsync();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new { title = "public", privateNotes = new string('x', 20000) });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = card.GetProperty("id").GetGuid();
        var token = card.GetProperty("concurrencyToken").GetGuid();
        foreach (var value in new string?[] { null, "", " \n " })
        {
            var patch = await client.PatchAsJsonAsync($"/api/cards/{id}/content", new { concurrencyToken = token, reason = "edit", title = "public", privateNotes = value });
            patch.StatusCode.ShouldBe(HttpStatusCode.OK);
            token = (await patch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyToken").GetGuid();
            var notes = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}/private-notes");
            notes.GetProperty("privateNotes").GetString().ShouldBe(value ?? new string('x', 20000));
        }
        var invalid = await client.PatchAsJsonAsync($"/api/cards/{id}/content", new { concurrencyToken = Guid.Empty, reason = "invalid", privateNotes = new string('x', 20001) });
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errors.TryGetProperty("privateNotes", out _).ShouldBeTrue();
        errors.TryGetProperty("ConcurrencyToken", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Settings_require_both_fields_and_enable_requires_a_safe_target()
    {
        var board = await BoardAsync();
        using var client = factory.CreateClient();
        var missing = await client.PutAsJsonAsync($"/api/boards/{board}/card-files/settings", new { });
        missing.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var errors = (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errors.TryGetProperty("syncCardFiles", out _).ShouldBeTrue();
        errors.TryGetProperty("expectedSyncCardFiles", out _).ShouldBeTrue();
        var refused = await client.PutAsJsonAsync($"/api/boards/{board}/card-files/settings", new { syncCardFiles = true, expectedSyncCardFiles = false });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("card_file_policy_refused");
        var status = await client.GetFromJsonAsync<JsonElement>($"/api/boards/{board}/card-files/status");
        status.GetProperty("syncCardFiles").GetBoolean().ShouldBeFalse();
        status.GetProperty("directory").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("removalPending").GetBoolean().ShouldBeFalse();
    }
}
