// CARD-0448: inspect or recover crash-orphaned repository child journals. It replaced scripts/recover-repository-children.ps1
// (unit 23 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; the .ps1 is now the cutover's thin launcher); the CLI runs it as
// `antiphon-scripts recover-repository-children --repository <checkout> [--execute] [--confirm-descendants-exited]`.
//
// Preview is the default. Executing needs an explicit confirmation that the surviving descendants of the recorded children
// have exited: a dead or reused root PID alone cannot prove that, nor can a completed record (its root exited before its start
// identity was read; CARD-0661, its PID is never looked up). Live or ambiguous records are always retained. No process is
// killed, and no Git lock, worktree or landing evidence is removed.
//
// The repository's Git common directory is git's answer canonicalised the way the server's LandingGit does (each reparse
// point resolved to its final target), so a record the server wrote compares equal. The command holds the server's
// RepositoryMutationLease for the whole pass: <common>/antiphon/landing.lock opened with no sharing (never unlinked: the open
// handle, not the file, is the exclusion). Each file in <common>/antiphon/children is one record; only a plain *.json file
// whose schema 1 record names this common directory and a positive PID is read, and the PID is looked up by its exact start
// identity. Prints "recovered (<state>): <path>" or "retained (<state>): <path>[ purpose=<p>][ task=<32 hex>]" per file, then
// the hint when anything was retained. Exit 0 no record retained; 3 busy, retained or unconfirmed evidence, or refused (one
// "WARNING: Repository recovery refused: <reason>" line on stderr). Usage errors are NightlyUsageException (the CLI's exit 2).
// Git runs on the host's git seam, files on IFileSystem, and the PID lookup on the identity delegate. Plain C#: no PowerShell
// types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>The recover-repository-children command's options (recover-repository-children.ps1 -Repository -Execute -ConfirmDescendantsExited).</summary>
    public sealed class RepositoryChildRecoveryOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "recover-repository-children --repository <checkout> [--execute] [--confirm-descendants-exited]";

        /// <summary>The checkout (any worktree of the repository).</summary>
        public string Repository { get; init; } = "";

        /// <summary>Remove the recoverable records (with ConfirmDescendantsExited).</summary>
        public bool Execute { get; init; }

        /// <summary>The operator confirms that the recorded children's descendants have exited.</summary>
        public bool ConfirmDescendantsExited { get; init; }

        /// <summary>The flags after the command name; --repository is required (NightlyUsageException otherwise).</summary>
        public static RepositoryChildRecoveryOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--repository" }, new[] { "--execute", "--confirm-descendants-exited" });
            return new RepositoryChildRecoveryOptions
            {
                Repository = flags.Required("--repository"),
                Execute = flags.Switch("--execute"),
                ConfirmDescendantsExited = flags.Switch("--confirm-descendants-exited"),
            };
        }
    }

    /// <summary>
    /// A recorded child's identity now: "alive" (that PID runs with that start time), "dead" (no such process, or it has
    /// exited), "reused" (the PID runs with another start time) or "unknown" (it could not be read).
    /// </summary>
    public delegate string RepositoryChildIdentity(int processId, long startTicks);

    /// <summary>The repository child journal recovery (recover-repository-children.ps1).</summary>
    public static class RepositoryChildRecovery
    {
        /// <summary>No record retained.</summary>
        public const int Clear = 0;

        /// <summary>Busy, retained or unconfirmed evidence, or refused.</summary>
        public const int Retained = 3;

        /// <summary>The line printed after any retained record.</summary>
        public const string Hint = "After inspecting descendants, use --execute --confirm-descendants-exited to recover only dead/reused/completed records.";

        /// <summary>The states a confirmed execute removes.</summary>
        public static readonly IReadOnlyList<string> Recoverable = Array.AsReadOnly(new[] { "dead", "reused", "completed" });

        /// <summary>Runs the pass: the per-record lines and the hint on host.Out, a refusal on host.Error; Clear or Retained.</summary>
        public static int Run(RepositoryChildRecoveryOptions options, NightlyHost host, IFileSystem files, RepositoryChildIdentity identify)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (identify == null) throw new ArgumentNullException(nameof(identify));
            Stream lease = null;
            try
            {
                var answer = host.RunGit(options.Repository, "rev-parse", "--path-format=absolute", "--git-common-dir");
                var lines = answer.Output.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();
                if (answer.ExitCode != 0 || lines.Length != 1) throw new RepositoryRecoveryRefusal("Cannot resolve Git common directory.");
                var common = CanonicalDirectory(files, lines[0]);
                var admission = files.Path.Combine(common, "antiphon");
                files.Directory.CreateDirectory(admission);
                // Same exclusion as RepositoryMutationLease, including across linked worktrees.
                lease = files.FileStream.New(files.Path.Combine(admission, "landing.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var directory = files.Path.Combine(admission, "children");
                var retained = 0;
                if (files.Directory.Exists(directory) || files.File.Exists(directory))
                {
                    var attributes = files.File.GetAttributes(directory);
                    if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                        throw new RepositoryRecoveryRefusal("Child journal directory is not a plain directory.");
                    foreach (var path in files.Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
                    {
                        var state = "unknown";
                        var tag = "";
                        try
                        {
                            if (string.Equals(files.Path.GetExtension(path), ".json", StringComparison.Ordinal)
                                && (files.File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                            {
                                using var record = JsonDocument.Parse(files.File.ReadAllText(path));
                                state = State(record.RootElement, common, files, identify);
                                tag = Tag(record.RootElement);
                            }
                            if (Recoverable.Contains(state, StringComparer.Ordinal) && options.Execute && options.ConfirmDescendantsExited)
                            {
                                Remove(files, path);
                                host.Out.WriteLine("recovered (" + state + "): " + path);
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            // An unreadable record, or one that could not be removed, is retained as unknown.
                            state = "unknown";
                        }
                        retained++;
                        host.Out.WriteLine("retained (" + state + "): " + path + tag);
                    }
                }
                if (retained > 0)
                {
                    host.Out.WriteLine(Hint);
                    return Retained;
                }
                return Clear;
            }
            catch (Exception e)
            {
                host.Error.WriteLine("WARNING: Repository recovery refused: " + e.Message);
                return Retained;
            }
            finally
            {
                // Never unlink landing.lock: its open handle, not its existence, owns exclusion.
                lease?.Dispose();
            }
        }

        /// <summary>
        /// The production identity: the PID looked up now. No such process or an exited one is "dead", another start time
        /// "reused", the recorded start time "alive", and anything that cannot be read "unknown".
        /// </summary>
        public static string Identify(int processId, long startTicks)
        {
            try
            {
                Process process;
                try
                {
                    process = Process.GetProcessById(processId);
                }
                catch (ArgumentException)
                {
                    return "dead";
                }
                using (process)
                {
                    if (process.HasExited) return "dead";
                    return process.StartTime.ToUniversalTime().Ticks == startTicks ? "alive" : "reused";
                }
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        /// <summary>
        /// LandingGit's canonical directory: the full path, each component that is a reparse point (junction or symbolic link)
        /// replaced by its final target, without a trailing separator. A missing component or an alias that does not resolve
        /// refuses.
        /// </summary>
        public static string CanonicalDirectory(IFileSystem files, string path)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            var full = files.Path.GetFullPath(path);
            var root = files.Path.GetPathRoot(full);
            var current = root;
            foreach (var part in full.Substring(root.Length).Split(new[] { files.Path.DirectorySeparatorChar, files.Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = files.Path.Combine(current, part);
                var info = files.DirectoryInfo.New(current);
                if (!info.Exists) throw new RepositoryRecoveryRefusal("Git common directory is missing or inaccessible.");
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    current = info.ResolveLinkTarget(true)?.FullName
                        ?? throw new RepositoryRecoveryRefusal("Git common directory alias cannot be resolved.");
            }
            return files.Path.TrimEndingDirectorySeparator(files.Path.GetFullPath(current));
        }

        /// <summary>
        /// A record's state against the canonical common directory: "unknown" unless it is a schema 1 object with a positive
        /// PID and a CommonDirectory equal to common (ignoring case and a trailing separator); then "completed" for a record
        /// with no start ticks whose owner saw its root exit (Completed true), "unknown" for one without positive start ticks,
        /// and otherwise the identity's answer. The keys are the server's spelling (RepositoryChildJournal.ChildRecord).
        /// </summary>
        public static string State(JsonElement record, string common, IFileSystem files, RepositoryChildIdentity identify)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (identify == null) throw new ArgumentNullException(nameof(identify));
            if (record.ValueKind != JsonValueKind.Object) return "unknown";
            if (!record.TryGetProperty("SchemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number
                || !schema.TryGetInt32(out var version) || version != 1)
                return "unknown";
            if (!record.TryGetProperty("ProcessId", out var pid) || pid.ValueKind != JsonValueKind.Number
                || !pid.TryGetInt32(out var processId) || processId <= 0)
                return "unknown";
            if (!record.TryGetProperty("CommonDirectory", out var recorded) || recorded.ValueKind != JsonValueKind.String
                || !string.Equals(files.Path.GetFullPath(recorded.GetString()).TrimEnd('\\', '/'), (common ?? "").TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                return "unknown";
            // CARD-0661: the owner saw this child's exact handle exit before it could read a start identity, then died before
            // draining output. The root is gone; its PID may be reused and is never looked up. Only descendants remain in
            // question, as for a dead record.
            if (!record.TryGetProperty("StartTicks", out var start)) return "unknown";
            if (start.ValueKind == JsonValueKind.Null)
                return record.TryGetProperty("Completed", out var completed) && completed.ValueKind == JsonValueKind.True ? "completed" : "unknown";
            if (start.ValueKind != JsonValueKind.Number || !start.TryGetInt64(out var ticks) || ticks <= 0) return "unknown";
            return identify(processId, ticks);
        }

        /// <summary>A record's tag: " purpose=&lt;Purpose&gt;" and " task=&lt;TaskId as 32 hex digits&gt;" for each that is set (the lease fence sentence's form, CARD-1092).</summary>
        public static string Tag(JsonElement record)
        {
            if (record.ValueKind != JsonValueKind.Object) return "";
            var tag = "";
            var purpose = Text(record, "Purpose");
            if (purpose.Length > 0) tag += " purpose=" + purpose;
            var task = Text(record, "TaskId");
            if (task.Length > 0) tag += " task=" + (Guid.TryParse(task, out var id) ? id.ToString("N") : task);
            return tag;
        }

        private static string Text(JsonElement record, string name)
        {
            if (!record.TryGetProperty(name, out var value)) return "";
            switch (value.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return "";
                case JsonValueKind.String:
                    return value.GetString() ?? "";
                default:
                    return value.GetRawText();
            }
        }

        // Remove-Item -Force: a read-only record is removed too.
        private static void Remove(IFileSystem files, string path)
        {
            var attributes = files.File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0) files.File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            files.File.Delete(path);
        }

        private sealed class RepositoryRecoveryRefusal : Exception
        {
            public RepositoryRecoveryRefusal(string message) : base(message) { }
        }
    }
}
