using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Owns the store/image, while each case owns its services and mutable test doubles.</summary>
internal sealed class CheckpointSourceApprovalFamily : IAsyncDisposable
{
    internal static readonly string[] Closure = """
        AgentBundleAttachments AgentIncidents AgentPinOperations AgentPinProjections AgentPinReconciliations
        AgentPinnedInstructionStates AgentPinnedInstructions AgentReviewCheckpoints AgentSessions AgentSupervisionStates
        AgentTaskDecisionQuestions AgentTaskDispatchWarningIntents AgentTaskEvents AgentTaskLandNotifications
        AgentTaskLandRequests AgentTaskLandings AgentTasks Agents ApiErrorRecoveries ApiKeys ArtifactSectionReviews
        AuditRecords BoardColumns BoardWorkflowDefinitions Boards CardComments CardRevisions CardWorkflowRuns
        CardWorkflowStages Cards ChannelOutboundDeliveries ChatChannels CheckCompactionRecoveries CostLedgerEntries
        DelegationCapabilities DelegationCapabilityEvents Diagnoses ExpectationNudges ExternalIssueRefs FileReviewStates
        FileSectionReviews GateDecisions LegacyCheckNotePublications OutputDistillations Projects RemoteControlModalEpisodes
        RetrySchedules ReviewComments ReviewThreads RoutingPins RunAttempts ScheduleFires Schedules SessionQueuedMessages
        StageExecutions StageOutcomes Stages TaskWorktreeRetirementAttempts TaskWorktreeRetirements TokenUsages
        TranscriptEntries VerificationExecutions Workflows WorkspaceUseReservations WorktreeCleanupAttempts
        WorktreeHealthFindings Worktrees CardWorktreeCleanups CardWorktreeCleanupTargets CardWorktreeCleanupEndpoints
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();
    public IsolatedTestSchema Schema { get; private set; } = null!;
    public LandingGitFixture? Fixture { get; private set; }
    public int StoreAllocations { get; private set; }
    public int NativeInitializations { get; private set; }
    public int StoreDrops { get; private set; }
    public IReadOnlyDictionary<string, string> Baseline { get; private set; } = null!;
    private CheckpointSourceFixtureImage? _image;
    private string[] _tables = [];
    private string _database = "";
    private string _connection = "";
    private bool _caseOpen;
    private bool _disposed;

    public static async Task<CheckpointSourceApprovalFamily> CreateAsync(bool native = false)
    {
        var family = new CheckpointSourceApprovalFamily();
        try
        {
            family.Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            family.StoreAllocations++;
            family._connection = family.Schema.ConnectionString;
            await using var connection = new NpgsqlConnection(family._connection);
            await connection.OpenAsync();
            family._database = (string)(await new NpgsqlCommand("SELECT current_database()", connection).ExecuteScalarAsync())!;
            await using (var command = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename COLLATE \"C\"", connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                var names = new List<string>();
                while (await reader.ReadAsync()) names.Add(reader.GetString(0));
                family._tables = names.ToArray();
            }
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', family._tables.Where(t => t != "__EFMigrationsHistory")) + "\n")));
            if (family._tables.Count(t => t != "__EFMigrationsHistory") != 111 || digest != "92e850b6825f14e2af278ce71b04736c3496322524457d5facb8e2730aa28231")
                throw new InvalidOperationException("C886 schema inventory changed: " + digest);
            family.Baseline = await family.ReadRowsAsync();
            if (native)
            {
                family.Fixture = new LandingGitFixture();
                await family.Fixture.InitializeAsync();
                family.NativeInitializations++;
                family._image = CheckpointSourceFixtureImage.Capture(family.Fixture.Root);
            }
            return family;
        }
        catch { await family.DisposeAsync(); throw; }
    }

    public async Task<Dictionary<string, string>> ReadRowsAsync()
    {
        await using var connection = new NpgsqlConnection(_connection);
        await connection.OpenAsync();
        return await ReadRowsAsync(connection, null);
    }

    private async Task<Dictionary<string, string>> ReadRowsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(string.Join(';', _tables.Select(t =>
            $"SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text), '[]'::jsonb)::text FROM public.\"{t}\" t")), connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        foreach (var table in _tables)
        {
            await reader.ReadAsync(); result.Add(table, reader.GetString(0));
            await reader.NextResultAsync();
        }
        return result;
    }

    internal void ValidateDatabaseTarget(IsolatedTestSchema schema, string connectionString, Action destructiveAction)
    {
        var pinned = new NpgsqlConnectionStringBuilder(_connection);
        var supplied = new NpgsqlConnectionStringBuilder(connectionString);
        if (!ReferenceEquals(schema, Schema) || supplied.Host != pinned.Host || supplied.Port != pinned.Port ||
            supplied.Database != _database || supplied.Database != pinned.Database)
            throw new InvalidOperationException("foreign family database");
        destructiveAction();
    }

    public Task ResetAsync()
    {
        if (_caseOpen || _disposed) throw new InvalidOperationException("family case still owns resources");
        ValidateDatabaseTarget(Schema, _connection, () => { });
        // S1: image restoration is deliberately not wired until the contract goes red.
        return Task.CompletedTask; // S1: preserve real dirty state for the named reset assertions.
    }

    private void OpenCase()
    {
        if (_caseOpen || _disposed) throw new InvalidOperationException("family already in use");
        _caseOpen = true;
    }

    public async Task<LandingProtocolHarness> OpenProtocolAsync()
    {
        OpenCase();
        var harness = new LandingProtocolHarness(Schema) { CaseDisposed = () => _caseOpen = false };
        try { await harness.InitializeAsync(); return harness; }
        catch { await harness.DisposeAsync(); throw; }
    }

    public async Task<LandingSafetyHarness> OpenNativeAsync()
    {
        OpenCase();
        var harness = new LandingSafetyHarness(Schema, Fixture ?? throw new InvalidOperationException("not native"))
            { CaseDisposed = () => _caseOpen = false };
        try { await harness.InitializeAsync(); return harness; }
        catch { await harness.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_caseOpen) throw new InvalidOperationException("family disposal before case disposal");
        _disposed = true;
        if (Schema is not null) { await Schema.DisposeAsync(); StoreDrops++; }
        if (Fixture is not null) await Fixture.DisposeAsync();
    }
}
