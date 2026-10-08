using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Counts every command a context sends: readers, non-queries and scalars, sync and async.
/// Executed callbacks are not counted, so one round-trip is one statement.
/// </summary>
public sealed class FullCommandCounter : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();

    public int Total => _commands.Count;

    public IReadOnlyList<string> Commands => _commands.ToArray();

    public void Reset()
    {
        while (_commands.TryDequeue(out _))
        {
        }
    }

    public string Roster()
    {
        var lines = _commands.ToArray();
        return string.Join('\n', lines.Select((sql, index) =>
        {
            var flat = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (flat.Length > 180)
                flat = flat[..180];
            return $"{index + 1}. {flat}";
        }));
    }

    /// <summary>
    /// Appends every recorded command, untruncated, to a run-owned evidence file and returns
    /// the text written. <see cref="Roster"/> stays the bounded console view.
    /// </summary>
    public string AppendFullCommands(string path, string window)
    {
        var lines = _commands.ToArray();
        var text = new StringBuilder()
            .Append("=== ").Append(window).Append(" statements=").Append(lines.Length).Append('\n');
        for (var index = 0; index < lines.Length; index++)
            text.Append("--- ").Append(index + 1).Append('\n').Append(lines[index]).Append('\n');
        File.AppendAllText(path, text.ToString());
        return text.ToString();
    }

    /// <summary>
    /// A new evidence file in the test run's <c>--results-directory</c> (the checkpoint row
    /// directory), or under the ignored <c>.antiphon/test-evidence</c> when run without one.
    /// The file is attached to the TRX result as an artifact.
    /// </summary>
    public static string CreateEvidenceFile(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.IndexOf(args, "--results-directory");
        var directory = at >= 0 && at + 1 < args.Length
            ? Path.GetFullPath(args[at + 1])
            : Path.Combine(RepositoryRoot(), ".antiphon", "test-evidence");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{name}-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Environment.ProcessId}.sql.txt");
        File.WriteAllText(path, "");
        TUnit.Core.TestContext.Current?.Output.AttachArtifact(path, Path.GetFileName(path), "complete SQL of every counted command");
        return path;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }

    private void Record(DbCommand command) => _commands.Enqueue(command.CommandText);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
