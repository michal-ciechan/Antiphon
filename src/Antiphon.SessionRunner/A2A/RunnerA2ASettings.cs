namespace Antiphon.SessionRunner.Agent2Agent;

/// <summary>
/// SessionRunner:A2A. An optional Agent2Agent endpoint backed by headless Codex turns. Off by default; nothing else in
/// the runner reads these settings.
/// </summary>
public sealed class RunnerA2ASettings
{
    public const string SectionName = "SessionRunner:A2A";
    public static readonly string[] Sandboxes = ["read-only", "workspace-write", "danger-full-access"];

    public bool Enabled { get; set; }

    /// <summary>JSON-RPC route. The agent card is served at /.well-known/agent-card.json.</summary>
    public string Path { get; set; } = "/a2a";

    /// <summary>Base URL advertised in the agent card. Defaults to the first --urls entry.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>Default working directory for new contexts, and the root that antiphon.cwd must stay under.</summary>
    public string? WorkspaceRoot { get; set; }

    public string Sandbox { get; set; } = "read-only";
    public string? Model { get; set; }
    public string CodexExecutable { get; set; } = "codex";

    /// <summary>CODEX_HOME for the turns. Unset keeps the runner's own environment (the signed-in user's Codex home).</summary>
    public string? CodexHome { get; set; }

    public int MaxConcurrentTurns { get; set; } = 2;
    public int TurnTimeoutMinutes { get; set; } = 30;

    /// <summary>Idle contexts beyond this are forgotten, least recently used first.</summary>
    public int MaxContexts { get; set; } = 1000;

    /// <summary>
    /// Lets a new context continue an existing Codex thread named by antiphon.codex.threadId, including the user's own
    /// interactive sessions in this Codex home. Off by default.
    /// </summary>
    public bool AllowThreadAttach { get; set; }

    /// <summary>File holding a bearer token. Set, every A2A request must carry it; unset, only loopback callers are served.</summary>
    public string? BearerTokenFile { get; set; }

    public void Validate()
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(Path) || !Path.StartsWith('/'))
            throw new InvalidOperationException($"{SectionName}:Path must start with '/'.");
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) || !System.IO.Path.IsPathFullyQualified(WorkspaceRoot))
            throw new InvalidOperationException($"{SectionName}:WorkspaceRoot must be a fully qualified directory.");
        if (!Directory.Exists(WorkspaceRoot))
            throw new InvalidOperationException($"{SectionName}:WorkspaceRoot '{WorkspaceRoot}' does not exist.");
        if (!Sandboxes.Contains(Sandbox, StringComparer.Ordinal))
            throw new InvalidOperationException($"{SectionName}:Sandbox must be one of {string.Join(", ", Sandboxes)}.");
        if (string.IsNullOrWhiteSpace(CodexExecutable))
            throw new InvalidOperationException($"{SectionName}:CodexExecutable is required.");
        if (MaxConcurrentTurns < 1)
            throw new InvalidOperationException($"{SectionName}:MaxConcurrentTurns must be at least 1.");
        if (TurnTimeoutMinutes < 1)
            throw new InvalidOperationException($"{SectionName}:TurnTimeoutMinutes must be at least 1.");
        if (MaxContexts < 1)
            throw new InvalidOperationException($"{SectionName}:MaxContexts must be at least 1.");
        if (BearerTokenFile is not null && !File.Exists(BearerTokenFile))
            throw new InvalidOperationException($"{SectionName}:BearerTokenFile '{BearerTokenFile}' does not exist.");
    }

    public string? ReadBearerToken()
    {
        if (BearerTokenFile is null) return null;
        var token = File.ReadAllText(BearerTokenFile).Trim();
        if (token.Length < 16)
            throw new InvalidOperationException($"{SectionName}:BearerTokenFile must hold a token of at least 16 characters.");
        return token;
    }
}
