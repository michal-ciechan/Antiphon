using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Antiphon.Server.Application.Services;

public sealed partial class WorkflowDefinitionLoader
{
    public const string WorkflowFileName = "WORKFLOW.md";

    private static readonly Regex TemplateVariableRegex = CreateTemplateVariableRegex();

    private readonly AppDbContext _db;
    private readonly IWorkflowFileStore _fileStore;
    private readonly IFileSystemWatcher _fileWatcher;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public WorkflowDefinitionLoader(
        AppDbContext db,
        IWorkflowFileStore fileStore,
        IFileSystemWatcher fileWatcher,
        IEventBus eventBus,
        TimeProvider timeProvider)
    {
        _db = db;
        _fileStore = fileStore;
        _fileWatcher = fileWatcher;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<BoardWorkflowDto> GetAsync(Guid boardId, CancellationToken ct)
    {
        var board = await LoadBoardAsync(boardId, ct);
        WatchBoard(board);
        var active = GetActiveDefinition(board);
        var fileContent = await _fileStore.ReadAsync(board, ct);
        if (!string.IsNullOrWhiteSpace(fileContent))
        {
            try
            {
                var reloaded = await ReloadContentAsync(board, fileContent, publish: false, ct);
                if (reloaded is not null)
                    return ToDto(board, reloaded);
            }
            catch (ValidationException) when (active is not null)
            {
                return ToDto(board, active);
            }
        }

        if (active is not null)
            return ToDto(board, active);

        return new BoardWorkflowDto(
            board.Id,
            DefinitionId: null,
            Version: 0,
            Name: WorkflowFileName,
            Content: CreateDefaultContent(board),
            FilePath: _fileStore.GetWorkflowFilePath(board),
            UpdatedAt: null);
    }

    public async Task<BoardWorkflowDto> UpdateAsync(Guid boardId, UpdateBoardWorkflowRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Content))
            throw new ValidationException(nameof(UpdateBoardWorkflowRequest.Content), "Workflow content is required.");

        var board = await LoadBoardAsync(boardId, ct);
        WatchBoard(board);
        ParsedWorkflowDefinition parsed;
        try
        {
            parsed = Parse(request.Content);
        }
        catch (ValidationException ex)
        {
            await PublishReloadedAsync(board.Id, ok: false, version: null, error: FlattenValidationErrors(ex), ct);
            throw;
        }

        await _fileStore.WriteAsync(board, request.Content, ct);
        WatchBoard(board);
        var definition = await SaveNewVersionAsync(board, request.Content, parsed.Name, ct);

        await PublishReloadedAsync(board.Id, ok: true, definition.Version, error: null, ct);
        await _eventBus.PublishToAllAsync("BoardChanged", new { boardId = board.Id }, ct);
        return ToDto(board, definition);
    }

    public async Task<BoardWorkflowDto?> ReloadFromFileAsync(Guid boardId, CancellationToken ct)
    {
        var board = await LoadBoardAsync(boardId, ct);
        WatchBoard(board);
        var content = await _fileStore.ReadAsync(board, ct);
        if (string.IsNullOrWhiteSpace(content))
            return GetActiveDefinition(board) is { } active ? ToDto(board, active) : null;

        try
        {
            var definition = await ReloadContentAsync(board, content, publish: true, ct);
            return definition is not null ? ToDto(board, definition) : null;
        }
        catch (ValidationException ex)
        {
            await PublishReloadedAsync(board.Id, ok: false, version: null, FlattenValidationErrors(ex), ct);
            return GetActiveDefinition(board) is { } active ? ToDto(board, active) : null;
        }
    }

    public static ParsedWorkflowDefinition Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ValidationException(nameof(content), "Workflow content is required.");

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
            throw new ValidationException("workflow", "WORKFLOW.md must start with YAML front matter delimited by '---'.");

        var closingIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                closingIndex = i;
                break;
            }
        }

        if (closingIndex < 0)
            throw new ValidationException("workflow", "WORKFLOW.md front matter must end with a closing '---' line.");

        var frontMatter = string.Join(Environment.NewLine, lines.Skip(1).Take(closingIndex - 1));
        var prompt = string.Join(Environment.NewLine, lines.Skip(closingIndex + 1));
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ValidationException("workflow", "WORKFLOW.md must include a Markdown prompt body.");

        try
        {
            var hooks = WorkflowDefinitionParser.ParseYamlHooks(frontMatter);
            var name = ParseName(frontMatter) ?? WorkflowFileName;
            return new ParsedWorkflowDefinition(name, frontMatter, prompt, hooks);
        }
        catch (YamlException ex)
        {
            throw new ValidationException("yaml", ex.Message);
        }
    }

    public static bool TryParseContent(
        string? content,
        out ParsedWorkflowDefinition? definition,
        out string? error)
    {
        definition = null;
        error = null;
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            definition = Parse(content);
            return true;
        }
        catch (ValidationException ex)
        {
            error = FlattenValidationErrors(ex);
            return false;
        }
    }

    public static string RenderPrompt(
        string prompt,
        IReadOnlyDictionary<string, string?> variables)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ValidationException(nameof(prompt), "Prompt template must not be empty.");

        var rendered = TemplateVariableRegex.Replace(prompt, match =>
        {
            var key = match.Groups["name"].Value;
            if (!variables.TryGetValue(key, out var value))
                throw new ValidationException("prompt", $"Unknown workflow prompt variable '{key}'.");

            return value ?? string.Empty;
        });

        if (rendered.Contains("{{", StringComparison.Ordinal) || rendered.Contains("}}", StringComparison.Ordinal))
            throw new ValidationException("prompt", "Workflow prompt contains a malformed template expression.");

        return rendered;
    }

    public static IReadOnlyDictionary<string, string?> BuildPromptVariables(Card card, Worktree? worktree)
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["issue.identifier"] = card.Identifier,
            ["issue.title"] = card.Title,
            ["issue.description"] = card.Description,
            ["issue.priority"] = card.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["card.identifier"] = card.Identifier,
            ["card.title"] = card.Title,
            ["card.description"] = card.Description,
            ["card.priority"] = card.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["board.id"] = card.BoardId.ToString(),
            ["workspace.path"] = worktree?.Path,
            ["workspace.branch"] = worktree?.Branch,
            ["workspace.repo_path"] = worktree?.RepoPath,
            ["workspace.base_ref"] = worktree?.BaseRef
        };
    }

    public static string CreateDefaultContent(Board board) =>
        $$$"""
        ---
        name: {{{EscapeYamlScalar(board.Name)}}}
        agent:
          max_concurrent: {{{Math.Max(1, board.MaxConcurrentSessions)}}}
        ---
        Work on card {{ issue.identifier }}: {{ issue.title }}

        Priority: {{ issue.priority }}

        {{ issue.description }}
        """;

    private async Task<Board> LoadBoardAsync(Guid boardId, CancellationToken ct)
    {
        return await _db.Boards
            .Include(b => b.Project)
            .Include(b => b.WorkflowDefinitions)
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException(nameof(Board), boardId);
    }

    private void WatchBoard(Board board)
    {
        var path = _fileStore.GetWorkflowFilePath(board);
        if (path is null)
            return;

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(fileName))
            _fileWatcher.Watch(board.Id, directory, fileName);
    }

    private async Task<BoardWorkflowDefinition?> ReloadContentAsync(
        Board board,
        string content,
        bool publish,
        CancellationToken ct)
    {
        var active = GetActiveDefinition(board);
        if (active is not null && active.Content == content)
        {
            if (publish)
                await PublishReloadedAsync(board.Id, ok: true, active.Version, error: null, ct);

            return active;
        }

        var parsed = Parse(content);
        var definition = await SaveNewVersionAsync(board, content, parsed.Name, ct);
        if (publish)
            await PublishReloadedAsync(board.Id, ok: true, definition.Version, error: null, ct);

        return definition;
    }

    private async Task<BoardWorkflowDefinition> SaveNewVersionAsync(
        Board board,
        string content,
        string name,
        CancellationToken ct)
    {
        var now = UtcNow();
        var active = GetActiveDefinition(board);
        if (active is not null)
            active.IsActive = false;

        var nextVersion = board.WorkflowDefinitions.Count == 0
            ? 1
            : board.WorkflowDefinitions.Max(d => d.Version) + 1;
        var definition = new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = nextVersion,
            Name = name,
            Content = content,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.WorkflowDefinitions.Add(definition);
        board.UpdatedAt = now;
        _db.BoardWorkflowDefinitions.Add(definition);
        await _db.SaveChangesAsync(ct);
        return definition;
    }

    private BoardWorkflowDto ToDto(Board board, BoardWorkflowDefinition definition) =>
        new(
            board.Id,
            definition.Id,
            definition.Version,
            definition.Name,
            definition.Content,
            _fileStore.GetWorkflowFilePath(board),
            definition.UpdatedAt);

    private static BoardWorkflowDefinition? GetActiveDefinition(Board board) =>
        board.WorkflowDefinitions
            .Where(d => d.IsActive)
            .OrderByDescending(d => d.Version)
            .FirstOrDefault();

    private async Task PublishReloadedAsync(
        Guid boardId,
        bool ok,
        int? version,
        string? error,
        CancellationToken ct)
    {
        await _eventBus.PublishToAllAsync(
            "WorkflowReloaded",
            new { boardId, ok, version, error },
            ct);
    }

    private static string FlattenValidationErrors(ValidationException ex) =>
        string.Join(" ", ex.Errors.SelectMany(kvp => kvp.Value.Select(v => $"{kvp.Key}: {v}")));

    private static string? ParseName(string yaml)
    {
        var yamlStream = new YamlStream();
        using var reader = new StringReader(yaml);
        yamlStream.Load(reader);
        if (yamlStream.Documents.Count == 0
            || yamlStream.Documents[0].RootNode is not YamlMappingNode rootMapping)
        {
            return null;
        }

        var key = new YamlScalarNode("name");
        return rootMapping.Children.TryGetValue(key, out var value)
            && value is YamlScalarNode scalar
            && !string.IsNullOrWhiteSpace(scalar.Value)
                ? scalar.Value.Trim()
                : null;
    }

    private static string EscapeYamlScalar(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "WORKFLOW"
            : value.Replace(":", "-", StringComparison.Ordinal).Trim();

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    [GeneratedRegex(@"\{\{\s*(?<name>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex CreateTemplateVariableRegex();
}

public sealed record ParsedWorkflowDefinition(
    string Name,
    string FrontMatter,
    string PromptMarkdown,
    WorkflowHooks Hooks);
