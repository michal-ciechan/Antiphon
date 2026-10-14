using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.E2E.Fixtures;
using Microsoft.Playwright;
using Shouldly;
using TUnit.Core;
using static Microsoft.Playwright.Assertions;

namespace Antiphon.E2E;

/// <summary>
/// CARD-0506 V-18 (test design `2026-10-13-card-0506-test-design.md`): project override, clear and
/// live receipt through the real API, SignalR hub and browser, each step checked by an independent GET.
/// </summary>
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("OptIn")]
public class DispatchConcurrencySettingsE2ETests
{
    private const int Width = 390;
    private const float DeadlineMs = 15_000;
    private const string Inheriting = "Inheriting global defaults; no project overrides";

    private readonly AntiphonAppFixture _app = new() { UsePrebuiltFrontend = true };
    private readonly PlaywrightFixture _browser = new();

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _app.InitializeAsync();
        await _browser.InitializeAsync();
    }

    [After(Test)]
    public async Task TeardownAsync()
    {
        await _browser.DisposeAsync();
        await _app.DisposeAsync();
    }

    [Test]
    public async Task Project_override_clear_and_live_receipts_preserve_inheritance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectId = await CreateProjectAsync($"C506 concurrency {suffix}");
        var scopePath = $"/api/projects/{projectId}/dispatch-concurrency";
        var initial = await GetAsync(scopePath);
        var inheritedCode = Role(initial.GetProperty("inherited"), "Code");
        var inheritedSource = inheritedCode.GetProperty("maxParallelSource").GetString()!;
        inheritedSource.ShouldNotBe("project");
        var url = $"{_app.PlaywrightAddress}/settings?tab=concurrency&project={projectId}";

        var (editor, editorContext) = await _browser.NewPageAsync();
        var (clean, cleanContext) = await _browser.NewPageAsync();
        var (dirty, dirtyContext) = await _browser.NewPageAsync();
        var cleanHub = new HubTap();
        var dirtyHub = new HubTap();
        var passed = false;
        try
        {
            // (a) Deep link, Code max parallel = 1, Save; independent GET then reload.
            await editor.SetViewportSizeAsync(Width, 844);
            await editor.GotoAsync(url);
            await Expect(editor.GetByText("Project concurrency", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(editor.GetByTestId("dispatch-concurrency-subtitle")).ToHaveTextAsync(Inheriting);
            var firstReason = $"C506 E2E override {suffix}";
            await SetCodeParallelAsync(editor, "1");
            await editor.GetByLabel("Reason", new() { Exact = true }).FillAsync(firstReason);
            var save = editor.GetByTestId("dispatch-concurrency-save");
            await save.ScrollIntoViewIfNeededAsync();
            var box = await save.BoundingBoxAsync();
            box.ShouldNotBeNull();
            box!.X.ShouldBeGreaterThanOrEqualTo(0);
            (box.X + box.Width).ShouldBeLessThanOrEqualTo(Width + 1);
            var saved = await PutAsync(editor, scopePath, () => save.ClickAsync());
            var savedRevision = saved.GetProperty("revision").GetInt64();
            savedRevision.ShouldBeGreaterThan(initial.GetProperty("revision").GetInt64());

            var afterSave = await GetAsync(scopePath);
            afterSave.GetProperty("revision").GetInt64().ShouldBe(savedRevision);
            var savedCode = Role(afterSave.GetProperty("effective"), "Code");
            savedCode.GetProperty("maxParallel").GetInt32().ShouldBe(1);
            savedCode.GetProperty("maxParallelSource").GetString().ShouldBe("project");
            afterSave.GetProperty("overrides").GetProperty("roles").GetProperty("Code")
                .GetProperty("maxParallel").GetInt32().ShouldBe(1);
            var savedSubtitle = $"Revision {savedRevision} · Human · {firstReason}";
            await Expect(editor.GetByTestId("dispatch-concurrency-subtitle")).ToHaveTextAsync(savedSubtitle);

            await editor.ReloadAsync();
            await Expect(editor.GetByTestId("dispatch-concurrency-subtitle")).ToHaveTextAsync(savedSubtitle);
            await Expect(CodeSwitch(editor)).ToBeCheckedAsync();
            await Expect(CodeInput(editor)).ToHaveValueAsync("1");
            await Expect(editor.GetByTestId("dispatch-concurrency-effective-Code")).ToContainTextAsync("Project override");
            await Expect(editor.GetByTestId("dispatch-concurrency-effective-parallel-Code"))
                .ToHaveTextAsync(Limit(savedCode.GetProperty("combinedParallel")));

            // (b) A clean recipient and a dirty recipient, each with an open hub socket.
            foreach (var (page, hub) in new[] { (clean, cleanHub), (dirty, dirtyHub) })
            {
                await page.SetViewportSizeAsync(Width, 844);
                await hub.AttachAsync(page);
                await page.GotoAsync(url);
                await Expect(page.GetByTestId("dispatch-concurrency-subtitle")).ToHaveTextAsync(savedSubtitle);
                await hub.Handshake.WaitAsync(TimeSpan.FromMilliseconds(DeadlineMs));
            }
            var dirtyReason = $"C506 E2E dirty {suffix}";
            await CodeInput(dirty).FillAsync("2");
            await dirty.GetByLabel("Reason", new() { Exact = true }).FillAsync(dirtyReason);
            var cleanGet = ScopeGetAsync(clean, scopePath);
            var dirtyGet = ScopeGetAsync(dirty, scopePath);

            // (c) Clear from the editor; recipients consume the event without focus or reload.
            await editor.GetByLabel("Reason", new() { Exact = true }).FillAsync($"C506 E2E clear {suffix}");
            await editor.GetByTestId("dispatch-concurrency-clear").ClickAsync();
            var confirm = editor.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Clear overrides", Exact = true });
            var cleared = await PutAsync(editor, scopePath, () => confirm.ClickAsync(), body =>
                body.GetProperty("overrides").EnumerateObject().ShouldBeEmpty());
            var clearRevision = cleared.GetProperty("revision").GetInt64();
            clearRevision.ShouldBeGreaterThan(savedRevision);

            var afterClear = await GetAsync(scopePath);
            afterClear.GetProperty("revision").GetInt64().ShouldBe(clearRevision);
            afterClear.GetProperty("overrides").EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "schemaVersion" });
            afterClear.GetProperty("overrides").GetProperty("schemaVersion").GetInt32().ShouldBe(1);
            var clearedCode = Role(afterClear.GetProperty("effective"), "Code");
            clearedCode.GetProperty("maxParallelSource").GetString().ShouldBe(inheritedSource);
            clearedCode.GetProperty("maxParallel").ToString().ShouldBe(inheritedCode.GetProperty("maxParallel").ToString());
            await Expect(editor.GetByTestId("dispatch-concurrency-subtitle")).ToHaveTextAsync(Inheriting);

            foreach (var (hub, get) in new[] { (cleanHub, cleanGet), (dirtyHub, dirtyGet) })
            {
                var change = await hub.WaitForChangeAsync(clearRevision);
                change.GetProperty("projectRevision").GetInt64().ShouldBe(clearRevision);
                (await get).GetProperty("revision").GetInt64().ShouldBe(clearRevision);
            }
            await Expect(clean.GetByTestId("dispatch-concurrency-subtitle")).ToHaveTextAsync(Inheriting);
            await Expect(CodeSwitch(clean)).Not.ToBeCheckedAsync();
            await Expect(clean.GetByTestId("dispatch-concurrency-effective-Code"))
                .ToContainTextAsync(SourceLabel(inheritedSource));
            await Expect(clean.GetByTestId("dispatch-concurrency-conflict")).ToHaveCountAsync(0);
            await Expect(dirty.GetByTestId("dispatch-concurrency-conflict"))
                .ToContainTextAsync($"project r{clearRevision}");
            await Expect(CodeSwitch(dirty)).ToBeCheckedAsync();
            await Expect(CodeInput(dirty)).ToHaveValueAsync("2");
            await Expect(dirty.GetByLabel("Reason", new() { Exact = true })).ToHaveValueAsync(dirtyReason);

            // (d) Clean recipient offline and its socket closed; a second change; recovery by reload.
            var reconnectFailed = new TaskCompletionSource<IRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            clean.RequestFailed += (_, request) =>
            {
                if (request.Url.Contains("/hubs/antiphon/negotiate", StringComparison.Ordinal))
                    reconnectFailed.TrySetResult(request);
            };
            await cleanContext.SetOfflineAsync(true);
            await cleanHub.DisconnectAsync();
            await reconnectFailed.Task.WaitAsync(TimeSpan.FromMilliseconds(DeadlineMs));

            var secondReason = $"C506 E2E second {suffix}";
            await SetCodeParallelAsync(editor, "3");
            await editor.GetByLabel("Reason", new() { Exact = true }).FillAsync(secondReason);
            var second = await PutAsync(editor, scopePath, () => save.ClickAsync());
            var secondRevision = second.GetProperty("revision").GetInt64();
            secondRevision.ShouldBeGreaterThan(clearRevision);
            var afterSecond = await GetAsync(scopePath);
            afterSecond.GetProperty("revision").GetInt64().ShouldBe(secondRevision);
            var secondCode = Role(afterSecond.GetProperty("effective"), "Code");
            secondCode.GetProperty("maxParallel").GetInt32().ShouldBe(3);
            secondCode.GetProperty("maxParallelSource").GetString().ShouldBe("project");
            await dirtyHub.WaitForChangeAsync(secondRevision);
            cleanHub.HasChange(secondRevision).ShouldBeFalse();
            await Expect(clean.GetByTestId("dispatch-concurrency-subtitle")).ToHaveTextAsync(Inheriting);

            await cleanContext.SetOfflineAsync(false);
            await clean.ReloadAsync();
            await Expect(clean.GetByTestId("dispatch-concurrency-subtitle"))
                .ToHaveTextAsync($"Revision {secondRevision} · Human · {secondReason}");
            await Expect(CodeSwitch(clean)).ToBeCheckedAsync();
            await Expect(CodeInput(clean)).ToHaveValueAsync("3");
            await Expect(clean.GetByTestId("dispatch-concurrency-effective-Code")).ToContainTextAsync("Project override");
            await Expect(clean.GetByTestId("dispatch-concurrency-effective-parallel-Code"))
                .ToHaveTextAsync(Limit(secondCode.GetProperty("combinedParallel")));
            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(editor, passed);
            await editorContext.DisposeAsync();
            await cleanContext.DisposeAsync();
            await dirtyContext.DisposeAsync();
        }
    }

    private static ILocator CodeSwitch(IPage page) =>
        page.GetByRole(AriaRole.Switch, new() { Name = "Code max parallel override", Exact = true });

    private static ILocator CodeInput(IPage page) =>
        page.GetByRole(AriaRole.Textbox, new() { Name = "Code max parallel", Exact = true });

    private static async Task SetCodeParallelAsync(IPage page, string value)
    {
        var toggle = CodeSwitch(page);
        // The Mantine switch input is zero-sized; its label is the click target.
        if (!await toggle.IsCheckedAsync())
            await toggle.Locator("xpath=ancestor::label[1]").ClickAsync();
        await Expect(toggle).ToBeCheckedAsync();
        await CodeInput(page).FillAsync(value);
    }

    /// <summary>Clicks, awaits the scope PUT, checks its request body and returns the 200 reply.</summary>
    private static async Task<JsonElement> PutAsync(IPage page, string scopePath, Func<Task> click, Action<JsonElement>? request = null)
    {
        var response = await page.RunAndWaitForResponseAsync(click,
            r => r.Request.Method == "PUT" && new Uri(r.Url).AbsolutePath == scopePath,
            new() { Timeout = DeadlineMs });
        response.Status.ShouldBe(200);
        var body = response.Request.PostDataJSON();
        body.ShouldNotBeNull();
        body!.Value.GetProperty("provenance").GetString().ShouldBe("Human");
        request?.Invoke(body.Value);
        return (await response.JsonAsync())!.Value;
    }

    /// <summary>The next scope GET this page makes, armed before the change that triggers it.</summary>
    private static async Task<JsonElement> ScopeGetAsync(IPage page, string scopePath)
    {
        var response = await page.WaitForResponseAsync(
            r => r.Request.Method == "GET" && new Uri(r.Url).AbsolutePath == scopePath,
            new() { Timeout = DeadlineMs });
        response.Status.ShouldBe(200);
        return (await response.JsonAsync())!.Value;
    }

    private async Task<Guid> CreateProjectAsync(string name)
    {
        var response = await _app.HttpClient.PostAsJsonAsync("/api/projects", new
        {
            name,
            gitRepositoryUrl = "https://github.com/example/e2e-test-repo.git",
            constitutionPath = (string?)null,
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false,
            localRepositoryPath = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        using var response = await _app.HttpClient.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Role(JsonElement effective, string role) =>
        effective.GetProperty("roles").EnumerateArray().Single(row => row.GetProperty("role").GetString() == role);

    private static string Limit(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? "∞" : value.GetInt32().ToString();

    private static string SourceLabel(string source) => source switch
    {
        "project" => "Project override",
        "global" => "Global override",
        "default" => "Built-in default",
        _ => source,
    };

    /// <summary>
    /// Routes the page's hub socket through to the real server, forwarding every frame and recording
    /// each <c>DispatchConcurrencyChanged</c> payload the page received.
    /// </summary>
    private sealed class HubTap
    {
        private readonly object _gate = new();
        private readonly List<JsonElement> _changes = [];
        private readonly List<(long Revision, TaskCompletionSource<JsonElement> Done)> _waiters = [];
        private readonly TaskCompletionSource _handshake = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IWebSocketRoute? _page;
        private IWebSocketRoute? _server;

        public Task Handshake => _handshake.Task;

        public Task AttachAsync(IPage page) => page.RouteWebSocketAsync(new Regex("/hubs/antiphon"), route =>
        {
            var server = route.ConnectToServer();
            lock (_gate)
            {
                _page = route;
                _server = server;
            }
            server.OnMessage(frame =>
            {
                if (frame.Text is { } text)
                {
                    route.Send(text);
                    Record(text);
                }
                else
                {
                    route.Send(frame.Binary);
                }
            });
        });

        public async Task DisconnectAsync()
        {
            IWebSocketRoute page, server;
            lock (_gate)
            {
                page = _page ?? throw new InvalidOperationException("No hub socket was routed");
                server = _server!;
            }
            await page.CloseAsync();
            await server.CloseAsync();
        }

        public bool HasChange(long revision)
        {
            lock (_gate)
                return _changes.Any(change => change.GetProperty("projectRevision").GetInt64() == revision);
        }

        public Task<JsonElement> WaitForChangeAsync(long revision)
        {
            var done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                var seen = _changes.FirstOrDefault(change => change.GetProperty("projectRevision").GetInt64() == revision);
                if (seen.ValueKind != JsonValueKind.Undefined)
                    return Task.FromResult(seen);
                _waiters.Add((revision, done));
            }
            return done.Task.WaitAsync(TimeSpan.FromMilliseconds(DeadlineMs));
        }

        private void Record(string text)
        {
            // SignalR JSON protocol: records end with 0x1E; the first server record is the handshake.
            foreach (var record in text.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                _handshake.TrySetResult();
                using var document = JsonDocument.Parse(record);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var type) || type.GetInt32() != 1
                    || root.GetProperty("target").GetString() != "DispatchConcurrencyChanged")
                    continue;
                var change = root.GetProperty("arguments")[0].Clone();
                var revision = change.GetProperty("projectRevision").GetInt64();
                lock (_gate)
                {
                    _changes.Add(change);
                    foreach (var waiter in _waiters.Where(w => w.Revision == revision).ToList())
                    {
                        _waiters.Remove(waiter);
                        waiter.Done.TrySetResult(change);
                    }
                }
            }
        }
    }
}
