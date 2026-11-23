// CARD-0097 S-E: preview and snapshot stage pins into role x complexity cells. It replaced
// scripts/migrate-stage-pins-to-cells.ps1 (deleted 2026-11-23, unit 26 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts
// migrate-stage-pins-to-cells [...]` (scripts/antiphon migrate-stage-pins-to-cells on sh, scripts\antiphon.cmd on Windows).
//
//   migrate-stage-pins-to-cells [--role <role>] [--dry-run | --apply | --restore --snapshot <first snapshot>] [--out-dir <dir>]
//
// The default and --dry-run issue GETs only; --apply writes Human cells from a fresh read and keeps the pins. Review the
// read-back (scripts/antiphon complexity-chain get --role <role>) before clearing a pin with scripts/antiphon routing-pin
// clear as a separate command. Every preview and apply first writes a snapshot (pins, cell before-images and the plan) to
// --out-dir (default .antiphon/migration-snapshots, relative to the working directory), a new file that is never replaced.
// Rollback: recreate the Required stage pins, read back, then --restore using the FIRST snapshot (a later apply snapshots
// migrated values, not the original cells). A restore re-checks that each migrated role has an active Required stage pin and
// refuses with zero writes when one is missing; it re-PUTs each Human before-image that has not expired and deletes every
// other cell. Writes carry no concurrency token (last writer wins, as before CARD-1275). Operator text comes from the API
// and the snapshot as UTF-8, including multiline reasons. The task token (ANTIPHON_TASK_TOKEN) is sent when set, never
// printed. Exit 0 done; 1 refused or a failed request (one stderr line; a stopped write run first names the cells written
// and not written on stdout); usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII
// only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The migrate-stage-pins-to-cells command's options (migrate-stage-pins-to-cells.ps1's parameters).</summary>
    public sealed class StagePinMigrationOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "migrate-stage-pins-to-cells [--role <role>] [--dry-run | --apply | --restore --snapshot <first snapshot>] [--out-dir <dir>]";

        /// <summary>The snapshot folder when --out-dir is not given.</summary>
        public const string DefaultOutDir = ".antiphon/migration-snapshots";

        private static readonly string[] Valued = { "--role", "--snapshot", "--out-dir" };
        private static readonly string[] Switches = { "--dry-run", "--apply", "--restore" };

        /// <summary>The one role to migrate or restore, in its listed spelling; empty: every role.</summary>
        public string Role { get; init; } = "";

        /// <summary>Write the previewed cells.</summary>
        public bool Apply { get; init; }

        /// <summary>Put the snapshot's before-images back.</summary>
        public bool Restore { get; init; }

        /// <summary>--restore: the first snapshot.</summary>
        public string Snapshot { get; init; } = "";

        /// <summary>The snapshot folder.</summary>
        public string OutDir { get; init; } = DefaultOutDir;

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>
        /// The flags; the API and the task token come from the CLI. A role outside the stage roles, two of --dry-run, --apply and
        /// --restore, --restore without --snapshot or --snapshot without --restore is a NightlyUsageException.
        /// </summary>
        public static StagePinMigrationOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            var dryRun = flags.Switch("--dry-run");
            var apply = flags.Switch("--apply");
            var restore = flags.Switch("--restore");
            if ((apply && restore) || (dryRun && (apply || restore))) throw new NightlyUsageException("--dry-run, --apply and --restore are mutually exclusive");
            var snapshot = flags.Value("--snapshot", "");
            if (restore && string.IsNullOrWhiteSpace(snapshot)) throw new NightlyUsageException("--restore requires --snapshot <first snapshot>");
            if (!restore && !string.IsNullOrWhiteSpace(snapshot)) throw new NightlyUsageException("--snapshot requires --restore");
            var outDir = flags.Value("--out-dir", "");
            return new StagePinMigrationOptions
            {
                Role = BoardCli.Choice("--role", flags.Value("--role"), StagePinMigration.Roles) ?? "",
                Apply = apply,
                Restore = restore,
                Snapshot = snapshot,
                OutDir = string.IsNullOrWhiteSpace(outDir) ? DefaultOutDir : outDir,
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The stage pin to cell migration and its restore (migrate-stage-pins-to-cells.ps1).</summary>
    public static class StagePinMigration
    {
        /// <summary>Each request's deadline (the script's TimeoutSec).</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

        /// <summary>The stage roles a pin migrates from (the script's ValidateSet).</summary>
        public static readonly IReadOnlyList<string> Roles = new[]
        {
            "Investigate", "Plan", "TestDesign", "Code", "Mutation", "Review", "Debug", "Coverage", "Docs", "Commit", "Test", "Deploy", "Merge", "Custom",
        };

        private static readonly string[] Tiers = { "Hard", "Medium", "Easy" };
        private static readonly string[] Resolutions = { "role", "any", "config", "none" };

        // A refusal: its message is the one stderr line and the exit is 1.
        private sealed class Refusal : Exception
        {
            public Refusal(string message) : base(message) { }
        }

        private sealed class Change
        {
            public string Role { get; init; }
            public string Complexity { get; init; }
            public string Method { get; init; }
            public JsonNode Body { get; init; }
            public string Note { get; init; }
        }

        private sealed class MigrationRun
        {
            public StagePinMigrationOptions Options { get; init; }
            public NightlyHost Host { get; init; }
            public IFileSystem Files { get; init; }
            public NightlyReportHttp Http { get; init; }
            public IReadOnlyDictionary<string, string> Headers { get; init; }
        }

        /// <summary>
        /// Previews (and with Apply writes) or restores. Stdout carries the table, the snapshot paths and the write lines (a HELD
        /// candidate is a "WARNING: HELD ..." line); exit 0, or 1 after one line on host.Error.
        /// </summary>
        public static int Run(StagePinMigrationOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var run = new MigrationRun { Options = options, Host = host, Files = files, Http = http, Headers = BoardCli.Headers(options.TaskToken) };
            try
            {
                if (options.Restore) Restore(run);
                else Preview(run);
                return 0;
            }
            catch (Refusal e)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }

        private static void Restore(MigrationRun run)
        {
            var options = run.Options;
            var path = run.Files.Path.GetFullPath(options.Snapshot);
            if (!run.Files.File.Exists(path)) throw new Refusal("restore refused: snapshot " + path + " does not exist.");
            JsonNode saved;
            try
            {
                saved = JsonNode.Parse(run.Files.File.ReadAllText(path, Encoding.UTF8));
            }
            catch (JsonException e)
            {
                throw new Refusal("restore refused: snapshot " + path + " is not JSON: " + e.Message);
            }
            if (!string.Equals(Text(saved, "apiBase"), options.Api, StringComparison.Ordinal)) throw new Refusal("restore refused: snapshot apiBase differs from the API base in use.");
            if (IsNull(BoardCli.Get(saved, "cellsBefore")) || IsNull(BoardCli.Get(saved, "plan")) || IsNull(BoardCli.Get(saved, "pins")))
                throw new Refusal("restore refused: missing snapshot pins, plan or cellsBefore.");
            var before = BoardCli.Items(BoardCli.Get(saved, "cellsBefore")).Where(cell => options.Role.Length == 0 || Is(Text(cell, "role"), options.Role)).ToList();
            var migratedRoles = before.Select(cell => Text(cell, "role")).Distinct(StringComparer.Ordinal).ToList();
            var plan = BoardCli.Items(BoardCli.Get(saved, "plan"));
            // Complete validation and pin reads before constructing the first write.
            foreach (var cell in before)
            {
                var present = BoardCli.Get(cell, "present");
                if (!Roles.Any(role => Is(role, Text(cell, "role"))) || !Tiers.Any(tier => Is(tier, Text(cell, "complexity")))
                    || present == null || (present.GetValueKind() != JsonValueKind.True && present.GetValueKind() != JsonValueKind.False))
                    throw new Refusal("restore refused: invalid cell before-image.");
                if (plan.Count(row => Is(Text(row, "role"), Text(cell, "role")) && Is(Text(row, "complexity"), Text(cell, "complexity"))) != 1)
                    throw new Refusal("restore refused: before-image has no unique migration plan entry.");
            }
            var now = run.Host.Clock.GetUtcNow();
            foreach (var role in migratedRoles)
            {
                var original = BoardCli.Items(BoardCli.Get(saved, "pins")).Where(pin => Is(Text(pin, "role"), role) && Eligible(pin)).ToList();
                if (original.Count != 1) throw new Refusal("restore refused: no unique migrated pin for " + role + " in the snapshot.");
                var live = Call(run, "GET", "/api/routing-pins?role=" + Uri.EscapeDataString(role), null);
                var required = BoardCli.Items(BoardCli.Get(live, "pins")).Where(pin =>
                    Is(Text(pin, "role"), role) && IsNull(BoardCli.Get(pin, "cardId")) && Is(Text(pin, "strength"), "Required")
                    && Number(BoardCli.Get(pin, "candidateCount")) >= 1 && BoardCli.Items(BoardCli.Get(pin, "candidates")).Count >= 1 && Active(pin, now));
                if (!required.Any())
                    throw new Refusal("restore refused: " + role + " has no Required stage pin; run scripts/antiphon routing-pin set --role " + role
                        + " --provenance Human --strength Required --candidates " + Pairs(BoardCli.Get(original[0], "candidates")) + " --reason <text> first");
            }
            var changes = new List<Change>();
            foreach (var cell in before)
            {
                var method = "DELETE";
                JsonNode body = null;
                string note = null;
                if (BoardCli.Get(cell, "present").GetValueKind() == JsonValueKind.True)
                {
                    var provenance = Text(cell, "provenance");
                    if (Is(provenance, "Auto")) note = "Auto before-image not re-created; cleared";
                    else if (Is(provenance, "Human"))
                    {
                        var notAfter = BoardCli.Get(cell, "notAfter");
                        if (!IsNull(notAfter) && Instant(notAfter) <= now) note = "expired before restore; cleared";
                        else
                        {
                            method = "PUT";
                            body = new JsonObject
                            {
                                ["candidates"] = WriteCandidates(BoardCli.Get(cell, "candidates")),
                                ["provenance"] = "Human",
                                ["reason"] = BoardCli.Get(cell, "reason")?.DeepClone(),
                                ["notAfter"] = notAfter?.DeepClone(),
                            };
                        }
                    }
                    else throw new Refusal("restore refused: unknown before-image provenance.");
                }
                changes.Add(new Change { Role = Text(cell, "role"), Complexity = Text(cell, "complexity"), Method = method, Body = body, Note = note });
            }
            CellChanges(run, changes, "restored", "not restored");
        }

        private static void Preview(MigrationRun run)
        {
            var options = run.Options;
            var output = run.Host.Out;
            var pinList = Call(run, "GET", "/api/routing-pins", null);
            var availability = Call(run, "GET", "/api/model-availability", null);
            if (IsNull(BoardCli.Get(pinList, "pins")) || IsNull(BoardCli.Get(availability, "holds"))) throw new Refusal("Missing pins or holds in API response.");
            var now = run.Host.Clock.GetUtcNow();
            var stagePins = BoardCli.Items(BoardCli.Get(pinList, "pins"))
                .Where(pin => IsNull(BoardCli.Get(pin, "cardId")) && (options.Role.Length == 0 || Is(Text(pin, "role"), options.Role))).ToList();
            var before = new JsonArray();
            var plan = new JsonArray();
            var seenRoles = new List<string>();
            output.WriteLine("role | pin id | pin candidates | Hard/Medium/Easy before | to write");
            foreach (var pin in stagePins)
            {
                var role = Text(pin, "role");
                if (!Eligible(pin))
                {
                    output.WriteLine(role + " | manual | " + Text(pin, "id"));
                    continue;
                }
                if (seenRoles.Any(seen => Is(seen, role))) throw new Refusal("Multiple eligible stage pins for " + role + "; inspect the live pins.");
                seenRoles.Add(role);
                var list = Call(run, "GET", "/api/complexity-chains?role=" + Uri.EscapeDataString(role), null);
                var pairs = Pairs(BoardCli.Get(pin, "candidates"));
                var summaries = new List<string>();
                var effectiveBefore = new List<string>();
                foreach (var tier in Tiers)
                {
                    var matches = BoardCli.Items(BoardCli.Get(list, "chains")).Where(chain => Is(Text(chain, "complexity"), tier)).ToList();
                    if (matches.Count != 1 || !Resolutions.Any(resolution => Is(resolution, Text(matches[0], "resolvedFrom"))))
                        throw new Refusal("Missing or ambiguous cell read for " + role + "/" + tier + ".");
                    var cell = matches[0];
                    var from = Text(cell, "resolvedFrom");
                    var present = Is(from, "role");
                    var prior = new JsonObject
                    {
                        ["role"] = role,
                        ["complexity"] = tier,
                        ["present"] = present,
                        ["candidates"] = new JsonArray(),
                        ["provenance"] = null,
                        ["reason"] = null,
                        ["notAfter"] = null,
                    };
                    var summary = tier + " absent (inherits " + from + ")";
                    if (present)
                    {
                        prior["candidates"] = WriteCandidates(BoardCli.Get(cell, "candidates"));
                        prior["provenance"] = BoardCli.Get(cell, "provenance")?.DeepClone();
                        prior["reason"] = BoardCli.Get(cell, "reason")?.DeepClone();
                        prior["notAfter"] = BoardCli.Get(cell, "notAfter")?.DeepClone();
                        summary = tier + " " + Text(cell, "provenance");
                        if (Is(Text(cell, "provenance"), "Auto")) summary += " (restore clears)";
                        summary += " " + Pairs(BoardCli.Get(cell, "candidates"));
                    }
                    summaries.Add(summary);
                    before.Add(prior);
                    var body = new JsonObject
                    {
                        ["candidates"] = WriteCandidates(BoardCli.Get(pin, "candidates")),
                        ["provenance"] = "Human",
                        ["reason"] = Text(pin, "reason") + " (migrated from stage pin " + Text(pin, "id") + ")",
                    };
                    plan.Add(new JsonObject { ["role"] = role, ["complexity"] = tier, ["body"] = body });
                    var overlay = BoardCli.Get(cell, "effective");
                    if (Is(Text(pin, "strength"), "Required")) effectiveBefore.Add(tier + " effective BEFORE: Required pin " + pairs);
                    else if (!IsNull(overlay)) effectiveBefore.Add(tier + " effective BEFORE: " + Text(overlay, "source") + " " + Pairs(BoardCli.Get(overlay, "candidates")));
                    else
                        effectiveBefore.Add(tier + " effective BEFORE: Preferred pin " + pairs + "; fallback " + from + " " + Pairs(BoardCli.Get(cell, "candidates"))
                            + " (read server effective after activation)");
                }
                output.WriteLine(role + " | " + Text(pin, "id") + " | " + pairs + " | " + string.Join("; ", summaries) + " | Human " + pairs);
                foreach (var line in effectiveBefore) output.WriteLine("  " + line);
                output.WriteLine("  effective AFTER apply: " + Text(pin, "strength") + " pin " + pairs + "; after separate pin clear: role " + pairs);
                HeldWarnings(output, pin, BoardCli.Get(availability, "holds"), now);
            }

            var directory = run.Files.Path.GetFullPath(options.OutDir);
            run.Files.Directory.CreateDirectory(directory);
            var file = run.Files.Path.Combine(directory, "migrate-stage-pins-" + now.UtcDateTime.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N") + ".json");
            var snapshot = new JsonObject
            {
                ["apiBase"] = options.Api,
                ["createdAt"] = now.ToString("o", CultureInfo.InvariantCulture),
                ["pins"] = new JsonArray(stagePins.Select(pin => pin?.DeepClone()).ToArray()),
                ["cellsBefore"] = before,
                ["plan"] = plan,
            };
            var bytes = new UTF8Encoding(false).GetBytes(OperatorApi.Pretty(snapshot));
            using (var stream = run.Files.FileStream.New(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            output.WriteLine("Snapshot folder: " + directory);
            output.WriteLine("Snapshot: " + file);
            output.WriteLine("Keep the FIRST snapshot for rollback; a repeat apply snapshots the migrated cells.");
            if (options.Apply)
            {
                var changes = plan.Select(row => new Change
                {
                    Role = Text(row, "role"),
                    Complexity = Text(row, "complexity"),
                    Method = "PUT",
                    Body = BoardCli.Get(row, "body"),
                }).ToList();
                CellChanges(run, changes, "written", "not written");
            }
            else output.WriteLine("Dry run: zero writes. Use --apply to write the previewed cells from a fresh read.");
            foreach (var role in seenRoles)
            {
                output.WriteLine("Read back with scripts/antiphon complexity-chain get --role " + role + " and review the effective diff BEFORE clearing the pin.");
                output.WriteLine("Separate operator command after review: scripts/antiphon routing-pin clear --role " + role);
            }
        }

        // Invoke-CellChanges: each change in order; a failure prints what was done, the failing cell and what was not, then refuses.
        private static void CellChanges(MigrationRun run, IReadOnlyList<Change> changes, string completedLabel, string remainingLabel)
        {
            var completed = new List<string>();
            for (var i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                var key = change.Role + "/" + change.Complexity;
                try
                {
                    Call(run, change.Method, "/api/complexity-chains/" + key, change.Body);
                }
                catch (Refusal e)
                {
                    var remaining = changes.Skip(i + 1).Select(rest => rest.Role + "/" + rest.Complexity);
                    run.Host.Out.WriteLine(completedLabel + ": " + string.Join(", ", completed) + "; failed: " + key + " " + e.Message + "; " + remainingLabel + ": " + string.Join(", ", remaining));
                    throw;
                }
                completed.Add(key);
                if (!string.IsNullOrEmpty(change.Note)) run.Host.Out.WriteLine(key + ": " + change.Note);
            }
            run.Host.Out.WriteLine(completedLabel + ": " + string.Join(", ", completed));
        }

        // Invoke-MigrationApi: the parsed answer (null for an empty body) of a 2xx; a Refusal "<METHOD> <path> <status> <body>" otherwise.
        private static JsonNode Call(MigrationRun run, string method, string path, JsonNode body)
        {
            NightlyReportHttpResponse response;
            try
            {
                response = run.Http(method, run.Options.Api + path, run.Headers, body == null ? null : OperatorApi.Json(body));
            }
            catch (Exception e) when (!(e is Refusal))
            {
                // A transport failure: no status. The message names the address, never a header.
                throw new Refusal(method + " " + path + "  " + e.Message);
            }
            var text = (response?.Body ?? "").Trim();
            if (!OperatorApi.Succeeded(response))
                throw new Refusal(method + " " + path + " " + (response?.Status.ToString(CultureInfo.InvariantCulture) ?? "") + " " + (text.Length == 0 ? "(no body)" : text));
            if (text.Length == 0) return null;
            try
            {
                return JsonNode.Parse(text);
            }
            catch (JsonException e)
            {
                throw new Refusal(method + " " + path + " " + response.Status.ToString(CultureInfo.InvariantCulture) + " answered with a body that is not JSON: " + e.Message);
            }
        }

        // Write-HeldWarnings: one line per candidate a live hold covers (its kind, and its alias or every alias).
        private static void HeldWarnings(TextWriter output, JsonNode pin, JsonNode holds, DateTimeOffset now)
        {
            foreach (var candidate in BoardCli.Items(BoardCli.Get(pin, "candidates")))
                foreach (var hold in BoardCli.Items(holds))
                {
                    var alias = Text(hold, "modelAlias");
                    var until = BoardCli.Get(hold, "disabledUntil");
                    if (Is(Text(hold, "kind"), Text(candidate, "agentKind")) && (alias == "*" || Is(alias, Text(candidate, "alias"))) && (IsNull(until) || Instant(until) > now))
                        output.WriteLine("WARNING: HELD " + Text(pin, "role") + " " + Text(candidate, "agentKind") + "/" + Text(candidate, "modelLevel") + " (" + Text(candidate, "alias") + "): "
                            + Text(hold, "reason"));
                }
        }

        // Test-Eligible: a stage pin with no card, agent, dates or forbid list, a stage role, and candidates that each name a kind and a level.
        private static bool Eligible(JsonNode pin)
        {
            if (!IsNull(BoardCli.Get(pin, "cardId")) || !IsNull(BoardCli.Get(pin, "agentId")) || !IsNull(BoardCli.Get(pin, "notBefore")) || !IsNull(BoardCli.Get(pin, "notAfter"))
                || BoardCli.Items(BoardCli.Get(pin, "forbiddenAliases")).Count > 0 || BoardCli.Items(BoardCli.Get(pin, "candidates")).Count == 0
                || !Roles.Any(role => Is(role, Text(pin, "role"))))
                return false;
            return BoardCli.Items(BoardCli.Get(pin, "candidates")).All(candidate =>
                !string.IsNullOrWhiteSpace(Text(candidate, "agentKind")) && !string.IsNullOrWhiteSpace(Text(candidate, "modelLevel")));
        }

        // Test-Active: started (no notBefore in the future) and not expired (no notAfter at or before now).
        private static bool Active(JsonNode pin, DateTimeOffset now)
        {
            var notBefore = BoardCli.Get(pin, "notBefore");
            var notAfter = BoardCli.Get(pin, "notAfter");
            if (!IsNull(notBefore) && Instant(notBefore) > now) return false;
            if (!IsNull(notAfter) && Instant(notAfter) <= now) return false;
            return true;
        }

        // Get-WriteCandidates: each candidate as {agentKind, modelLevel}; a partial candidate refuses.
        private static JsonArray WriteCandidates(JsonNode candidates)
        {
            var written = new JsonArray();
            foreach (var candidate in BoardCli.Items(candidates))
            {
                if (string.IsNullOrWhiteSpace(Text(candidate, "agentKind")) || string.IsNullOrWhiteSpace(Text(candidate, "modelLevel")))
                    throw new Refusal("A cell candidate requires both agentKind and modelLevel; use the pin editor for partial candidates.");
                written.Add(new JsonObject { ["agentKind"] = Text(candidate, "agentKind"), ["modelLevel"] = Text(candidate, "modelLevel") });
            }
            return written;
        }

        // Get-Pairs: "Kind/Level" for each candidate, joined with commas.
        private static string Pairs(JsonNode candidates) =>
            string.Join(",", BoardCli.Items(candidates).Select(candidate => Text(candidate, "agentKind") + "/" + Text(candidate, "modelLevel")));

        private static DateTimeOffset Instant(JsonNode node)
        {
            var text = BoardCli.Text(node);
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)) return value;
            throw new Refusal("'" + text + "' is not a date.");
        }

        private static double Number(JsonNode node)
        {
            if (node == null || node.GetValueKind() != JsonValueKind.Number) return double.NaN;
            return node.GetValue<double>();
        }

        private static string Text(JsonNode node, string name) => BoardCli.Text(BoardCli.Get(node, name));

        private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

        // PowerShell's $null: a missing property or a JSON null.
        private static bool IsNull(JsonNode node) => node == null || node.GetValueKind() == JsonValueKind.Null;
    }
}
