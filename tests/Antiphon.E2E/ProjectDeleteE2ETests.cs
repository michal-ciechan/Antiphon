using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.E2E.Fixtures;
using Microsoft.Playwright;
using Shouldly;
using TUnit.Core;
using static Microsoft.Playwright.Assertions;

namespace Antiphon.E2E;

/// <summary>
/// End-to-end coverage for deleting projects and boards (GitHub issue #2).
///
/// The API tests are the regression guard for the original defect: the <c>Board -&gt; Project</c> FK
/// is Restrict, so <c>DELETE /api/projects/{id}</c> on a project that owned a board raised an
/// unmapped <c>DbUpdateException</c> and the client got a 500 with a Postgres error string. It must
/// answer 409 with something a human can act on, and only destroy anything when explicitly forced.
///
/// The browser test covers the flow the issue actually describes: open the delete dialog from the
/// project screen, see what is about to be destroyed, confirm, watch the row go.
/// </summary>
[NotInParallel]
public class ProjectDeleteE2ETests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly AntiphonAppFixture _appFixture = new();
    private readonly PlaywrightFixture _playwrightFixture = new();
    private TestDiagnostics _diagnostics = null!;

    [Before(Test)]
    public async Task SetupAsync(TestContext context)
    {
        // Server log, browser log and any page dump land under TestOutput/Logs/<test>/.
        _diagnostics = TestDiagnostics.For(context.Metadata.TestName);
        _appFixture.DiagnosticsDirectory = _diagnostics.ServerLogDirectory;
        _appFixture.UsePrebuiltFrontend = true;
        await _appFixture.InitializeAsync();
        await _playwrightFixture.InitializeAsync();
    }

    [After(Test)]
    public async Task TeardownAsync()
    {
        await _playwrightFixture.DisposeAsync();
        await _appFixture.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // API
    // -------------------------------------------------------------------------

    [Test]
    public async Task Deleting_a_project_that_owns_a_board_is_a_409_not_a_500()
    {
        var projectId = await CreateProjectAsync($"Delete Guard {Suffix()}");
        await CreateBoardAsync(projectId, "Delivery");

        var response = await _appFixture.HttpClient.DeleteAsync($"/api/projects/{projectId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        problem.GetProperty("detail").GetString().ShouldContain("board");

        // Still there — a refused delete destroys nothing.
        (await _appFixture.HttpClient.GetAsync($"/api/projects/{projectId}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_empty_project_deletes_without_force()
    {
        var projectId = await CreateProjectAsync($"Empty Project {Suffix()}");

        var response = await _appFixture.HttpClient.DeleteAsync($"/api/projects/{projectId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _appFixture.HttpClient.GetAsync($"/api/projects/{projectId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Forced_delete_takes_the_board_and_its_cards_with_it()
    {
        var projectId = await CreateProjectAsync($"Force Delete {Suffix()}");
        var boardId = await CreateBoardAsync(projectId, "Delivery");
        await CreateCardAsync(boardId, "Outstanding work");

        var response = await _appFixture.HttpClient.DeleteAsync($"/api/projects/{projectId}?force=true");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _appFixture.HttpClient.GetAsync($"/api/projects/{projectId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await _appFixture.HttpClient.GetAsync($"/api/boards/{boardId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Deletion_impact_reports_what_would_be_destroyed()
    {
        var projectId = await CreateProjectAsync($"Impact {Suffix()}");
        var boardId = await CreateBoardAsync(projectId, "Delivery");
        await CreateCardAsync(boardId, "Outstanding work");

        var response = await _appFixture.HttpClient.GetAsync($"/api/projects/{projectId}/deletion-impact");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var impact = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        impact.GetProperty("boardCount").GetInt32().ShouldBe(1);
        impact.GetProperty("cardCount").GetInt32().ShouldBe(1);
        impact.GetProperty("openCardCount").GetInt32().ShouldBe(1);
        impact.GetProperty("requiresConfirmation").GetBoolean().ShouldBeTrue();
        impact.GetProperty("canDelete").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task Deleting_the_last_board_deletes_its_project()
    {
        var projectId = await CreateProjectAsync($"Last Board {Suffix()}");
        var boardId = await CreateBoardAsync(projectId, "Only Board");

        var response = await _appFixture.HttpClient.DeleteAsync($"/api/boards/{boardId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        result.GetProperty("projectDeleted").GetBoolean().ShouldBeTrue();
        (await _appFixture.HttpClient.GetAsync($"/api/projects/{projectId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Deleting_one_of_two_boards_leaves_the_project_and_the_other_board()
    {
        var projectId = await CreateProjectAsync($"Two Boards {Suffix()}");
        var first = await CreateBoardAsync(projectId, "First");
        var second = await CreateBoardAsync(projectId, "Second");

        var response = await _appFixture.HttpClient.DeleteAsync($"/api/boards/{first}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        result.GetProperty("projectDeleted").GetBoolean().ShouldBeFalse();
        (await _appFixture.HttpClient.GetAsync($"/api/projects/{projectId}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await _appFixture.HttpClient.GetAsync($"/api/boards/{second}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    // -------------------------------------------------------------------------
    // Browser
    // -------------------------------------------------------------------------

    [Test]
    public async Task Project_delete_dialog_warns_about_attached_work_before_destroying_it()
    {
        var suffix = Suffix();
        var projectName = $"Delete Flow {suffix}";
        var projectId = await CreateProjectAsync(projectName);
        var boardId = await CreateBoardAsync(projectId, $"Delivery {suffix}");
        await CreateCardAsync(boardId, $"Outstanding work {suffix}");

        var (page, context) = await _playwrightFixture.NewPageAsync();
        _diagnostics.Attach(page);
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/settings");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Projects" }).ClickAsync();

            var row = page.GetByRole(AriaRole.Row).Filter(new LocatorFilterOptions { HasText = projectName });
            await Expect(row).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
            await PlaywrightFixture.CapturePageAsync(page, "01_projects_tab");

            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete project" }).ClickAsync();

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

            // The warning the issue asks for: say what is attached before it is destroyed.
            await Expect(dialog.GetByTestId("project-deletion-impact"))
                .ToContainTextAsync("1 board", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
            await Expect(dialog.GetByTestId("project-deletion-impact")).ToContainTextAsync("1 card");
            await PlaywrightFixture.CapturePageAsync(page, "02_delete_dialog_with_warning");

            // Destructive confirm is gated behind an explicit acknowledgement.
            var confirm = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true });
            await Expect(confirm).ToBeDisabledAsync();
            await dialog.GetByRole(AriaRole.Checkbox).CheckAsync();
            await Expect(confirm).ToBeEnabledAsync();
            await confirm.ClickAsync();

            await Expect(row).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
            await PlaywrightFixture.CapturePageAsync(page, "03_project_row_gone");

            (await _appFixture.HttpClient.GetAsync($"/api/projects/{projectId}")).StatusCode
                .ShouldBe(HttpStatusCode.NotFound);
            (await _appFixture.HttpClient.GetAsync($"/api/boards/{boardId}")).StatusCode
                .ShouldBe(HttpStatusCode.NotFound);

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await _diagnostics.CompleteAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    /// <summary>
    /// "Currently cannot delete items from project screen" — boards are now listed against their
    /// project and removable from there, and the dialog warns when removing one will take the
    /// project with it.
    /// </summary>
    [Test]
    public async Task Board_can_be_deleted_from_the_project_screen_and_warns_when_it_is_the_last_one()
    {
        var suffix = Suffix();
        var projectName = $"Board Delete {suffix}";
        var boardName = $"Only Board {suffix}";
        var projectId = await CreateProjectAsync(projectName);
        var boardId = await CreateBoardAsync(projectId, boardName);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        _diagnostics.Attach(page);
        var passed = false;
        try
        {
            await page.GotoAsync($"{_appFixture.PlaywrightAddress}/settings");
            await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Projects" }).ClickAsync();

            var row = page.GetByRole(AriaRole.Row).Filter(new LocatorFilterOptions { HasText = projectName });
            await Expect(row).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
            // The board is listed against its project.
            await Expect(row).ToContainTextAsync(boardName);

            await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = $"Delete board {boardName}" })
                .ClickAsync();

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
            await Expect(dialog).ToContainTextAsync("last board");
            await PlaywrightFixture.CapturePageAsync(page, "01_board_delete_warns_last_board");

            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true })
                .ClickAsync();

            // The project row goes too — the reverse cascade the issue asks for.
            await Expect(row).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
            await PlaywrightFixture.CapturePageAsync(page, "02_project_gone_with_its_last_board");

            (await _appFixture.HttpClient.GetAsync($"/api/boards/{boardId}")).StatusCode
                .ShouldBe(HttpStatusCode.NotFound);
            (await _appFixture.HttpClient.GetAsync($"/api/projects/{projectId}")).StatusCode
                .ShouldBe(HttpStatusCode.NotFound);

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await _diagnostics.CompleteAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private async Task<Guid> CreateProjectAsync(string name)
    {
        var payload = new
        {
            name,
            gitRepositoryUrl = "https://github.com/example/e2e-test-repo.git",
            constitutionPath = (string?)null,
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false,
            localRepositoryPath = (string?)null
        };

        var response = await _appFixture.HttpClient.PostAsJsonAsync("/api/projects", payload, JsonOptions);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBoardAsync(Guid projectId, string name)
    {
        var payload = new { projectId, name, description = string.Empty, maxConcurrentSessions = 1 };
        var response = await _appFixture.HttpClient.PostAsJsonAsync("/api/boards", payload, JsonOptions);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCardAsync(Guid boardId, string title)
    {
        var payload = new { boardColumnId = (Guid?)null, title, description = string.Empty, priority = 0, labels = Array.Empty<string>() };
        var response = await _appFixture.HttpClient.PostAsJsonAsync($"/api/boards/{boardId}/cards", payload, JsonOptions);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }
}
