using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Antiphon.Server.Infrastructure.Data;
using System.Text.Json;

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
    private string _metadata = "";
    private bool _qualified = true;
    private string _connection = "";
    private bool _caseOpen;
    private bool _disposed;

    public static async Task<CheckpointSourceApprovalFamily> CreateAsync(bool native = false, Guid? taskId = null)
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
            await family.QualifyMetadataAsync(connection);
            family._metadata = await MetadataAsync(connection);
            family.Baseline = await family.ReadRowsAsync();
            foreach (var (table, rows) in family.Baseline)
                if (table != "Users" && table != "__EFMigrationsHistory" && rows != "[]")
                    throw new InvalidOperationException("unexpected family seed in " + table);
            using (var users = JsonDocument.Parse(family.Baseline["Users"]))
            {
                var row = users.RootElement.EnumerateArray().Single();
                if (row.GetProperty("Id").GetGuid() != Guid.Parse("a0000000-0000-0000-0000-000000000001") ||
                    row.GetProperty("UserName").GetString() != "admin" || row.GetProperty("Email").GetString() != "admin@antiphon.local" ||
                    !row.GetProperty("IsAdmin").GetBoolean() || row.GetProperty("CreatedAt").GetDateTime().ToUniversalTime() != new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                    throw new InvalidOperationException("unexpected family admin seed");
            }
            if (native)
            {
                family.Fixture = new LandingGitFixture(taskId: taskId);
                family.Fixture.Git.FixedCommitTime = "2026-10-01T00:00:00Z";
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

    private static async Task<string> MetadataAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT jsonb_build_object(
              'columns', (SELECT jsonb_agg(to_jsonb(c) ORDER BY table_name, ordinal_position) FROM information_schema.columns c WHERE table_schema='public'),
              'constraints', (SELECT jsonb_agg(jsonb_build_array(n.nspname,t.relname,c.conname,pg_get_constraintdef(c.oid)) ORDER BY n.nspname,t.relname,c.conname)
                FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname='public'))::text
            """;
        return (string)(await new NpgsqlCommand(sql, connection).ExecuteScalarAsync())!;
    }

    private async Task QualifyMetadataAsync(NpgsqlConnection connection)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_connection));
        var model = db.Model.GetRelationalModel();
        var expected = model.Tables.SelectMany(t => t.Columns.Where(c => c.Name != "xmin").Select(c => t.Name + "/" + c.Name)).ToHashSet(StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("SELECT table_name,column_name FROM information_schema.columns WHERE table_schema='public' AND table_name <> '__EFMigrationsHistory'", connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) actual.Add(reader.GetString(0) + "/" + reader.GetString(1));
        if (!expected.SetEquals(actual)) throw new InvalidOperationException("family EF/PostgreSQL columns disagree: missing=" + string.Join(',', expected.Except(actual)) + "; extra=" + string.Join(',', actual.Except(expected)));
        var foreignKeys = model.Tables.SelectMany(t => t.ForeignKeyConstraints.Select(c => t.Name + "/" + c.Name)).ToHashSet(StringComparer.Ordinal);
        var actualForeignKeys = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("SELECT t.relname,c.conname FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname='public' AND c.contype='f'", connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) actualForeignKeys.Add(reader.GetString(0) + "/" + reader.GetString(1));
        if (!foreignKeys.SetEquals(actualForeignKeys)) throw new InvalidOperationException("family EF/PostgreSQL foreign keys disagree");
        const string guards = """
            SELECT (SELECT count(*) FROM pg_constraint c JOIN pg_class child ON child.oid=c.conrelid JOIN pg_class parent ON parent.oid=c.confrelid
              WHERE c.contype='f' AND parent.relname = ANY(@tables) AND NOT(child.relname = ANY(@tables))) +
              (SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name = ANY(@tables)
                AND (is_identity='YES' OR column_default LIKE '%nextval%'))
            """;
        await using var guard = new NpgsqlCommand(guards, connection);
        guard.Parameters.AddWithValue("tables", Closure);
        if ((long)(await guard.ExecuteScalarAsync())! != 0) throw new InvalidOperationException("family closure or sequence changed");
    }

    public async Task ResetAsync()
    {
        if (_caseOpen || _disposed || !_qualified) throw new InvalidOperationException("family case still owns resources or reset unqualified");
        _qualified = false;
        ValidateDatabaseTarget(Schema, _connection, () => { });
        await using (var connection = new NpgsqlConnection(_connection))
        {
            await connection.OpenAsync();
            if ((string)(await new NpgsqlCommand("SELECT current_database()", connection).ExecuteScalarAsync())! != _database)
                throw new InvalidOperationException("family database identity changed");
            if (await MetadataAsync(connection) != _metadata) throw new InvalidOperationException("family metadata changed");
            await using var transaction = await connection.BeginTransactionAsync();
            await using var truncate = new NpgsqlCommand("TRUNCATE TABLE " + string.Join(',', Closure.Select(t => "public.\"" + t + "\"")) + " CONTINUE IDENTITY RESTRICT", connection, transaction);
            await truncate.ExecuteNonQueryAsync();
            if (!MatchesBaseline(await ReadRowsAsync(connection, transaction))) return;
            await transaction.CommitAsync();
        }
        if (!MatchesBaseline(await ReadRowsAsync())) return;
        _image?.Restore();
        Fixture?.RenewRecorder();
        _qualified = _image is null || _image.Matches();
    }

    private bool MatchesBaseline(IReadOnlyDictionary<string, string> rows) =>
        Baseline.All(pair => rows.TryGetValue(pair.Key, out var actual) && actual == pair.Value);

    private void OpenCase()
    {
        if (_caseOpen || _disposed || !_qualified) throw new InvalidOperationException("family already in use or reset unqualified");
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
