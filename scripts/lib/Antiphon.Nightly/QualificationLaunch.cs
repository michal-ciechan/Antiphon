// CARD-1667, CARD-2044: the launch of one rc qualification run and the checkout preflight. It replaced
// scripts/qualification/launch-rc.ps1 and scripts/qualification/preflight.ps1, deleted 2026-11-23 (unit 7 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). qualification-supervise starts `antiphon-scripts
// qualification-launch --config <launch-record.json>` as its launch child; an operator runs `qualification-preflight
// --checkout-root <checkout>` before qualification-start-supervisor.
//
// The launch record (JSON) names checkoutRoot, expectedSha, nightlyArguments (the scripts/nightly-run.ps1 arguments, e.g.
// -NoSync -Profile rc -Trigger rc -SelectionMode Full -RunId <id>) and an optional buildSlotLabel. nightlyArguments must
// carry -RunId; under the supervisor it must equal ANTIPHON_QUALIFICATION_RUN_ID (the CLI passes it as BoundRunId), the run
// id qualification-observe credits. The launch refuses (exit 3) while the state root's run.lock (-StateRoot, default
// C:\Antiphon\nightly) names a live owner or cannot be read, rather than queueing behind that run; a dead owner (pid gone,
// or a start more than a second from the record's) is left to nightly-run's stale-lock rule. This is a point-in-time
// check: a run that starts while this launch waits for a build slot still makes nightly-run refuse it (exit 3). It refuses
// (exit 2) unless the checkout HEAD equals expectedSha and local core.longpaths is true (reason longpaths-not-enabled; fix:
// git -C <checkout> config --local core.longpaths true), then runs, under a build slot in this process (BuildSlotRunner,
// label <label>; the lease belongs to this process, and the command shares its console and streams),
//   <pwsh> -NoProfile -File <checkout>/scripts/nightly-run.ps1 <nightlyArguments>
// and exits with its exit code (4 when no slot came). git runs through the host. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The qualification-preflight command's options.</summary>
    public sealed class QualificationPreflightOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "qualification-preflight --checkout-root <dir>";

        /// <summary>The release checkout.</summary>
        public string CheckoutRoot { get; init; } = "";

        /// <summary>The arguments after the command name.</summary>
        public static QualificationPreflightOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--checkout-root" }, Array.Empty<string>());
            return new QualificationPreflightOptions { CheckoutRoot = flags.Required("--checkout-root") };
        }
    }

    /// <summary>The qualification-launch command's options.</summary>
    public sealed class QualificationLaunchOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "qualification-launch --config <launch-record.json> [--powershell-path <pwsh>]";

        /// <summary>The launch record.</summary>
        public string Config { get; init; } = "";

        /// <summary>The PowerShell 7 that runs nightly-run.ps1.</summary>
        public string PowerShellPath { get; init; } = "pwsh";

        /// <summary>The supervisor's bound run id (ANTIPHON_QUALIFICATION_RUN_ID, read by the CLI); empty outside a supervisor.</summary>
        public string BoundRunId { get; init; } = "";

        /// <summary>The arguments after the command name; boundRunId and powerShellPath come from the CLI's environment.</summary>
        public static QualificationLaunchOptions Parse(IReadOnlyList<string> arguments, string boundRunId, string powerShellPath)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--config", "--powershell-path" }, Array.Empty<string>());
            var given = flags.Value("--powershell-path");
            return new QualificationLaunchOptions
            {
                Config = flags.Required("--config"),
                PowerShellPath = string.IsNullOrWhiteSpace(given) ? powerShellPath : given,
                BoundRunId = boundRunId ?? "",
            };
        }
    }

    /// <summary>The checkout preflight and the launch (preflight.ps1, launch-rc.ps1).</summary>
    public static class QualificationLaunch
    {
        /// <summary>The launch's line prefix.</summary>
        public const string Prefix = "qualification-launch: ";

        /// <summary>The state root a launch record without -StateRoot names.</summary>
        public const string DefaultStateRoot = @"C:\Antiphon\nightly";

        private static readonly Regex FullSha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

        /// <summary>The fix command a longpaths-not-enabled refusal names.</summary>
        public static string LongPathsFix(string checkout) => "git -C " + checkout + " config --local core.longpaths true";

        /// <summary>git config --local --get core.longpaths is exactly "true" in the checkout (through the host's git).</summary>
        public static bool LongPathsEnabled(NightlyHost host, string checkout)
        {
            var result = host.RunGit(checkout, "config", "--local", "--get", "core.longpaths");
            return result.ExitCode == 0 && result.Output.Trim() == "true";
        }

        /// <summary>
        /// qualification-preflight: refuses (exit 2: "REFUSED: longpaths-not-enabled" and the fix command) a release checkout
        /// whose local core.longpaths is not true; prints "qualification-preflight: core.longpaths true" and exits 0 otherwise.
        /// It does not change the checkout.
        /// </summary>
        public static int Preflight(QualificationPreflightOptions options, NightlyHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!LongPathsEnabled(host, options.CheckoutRoot))
            {
                host.Out.WriteLine("REFUSED: longpaths-not-enabled");
                host.Out.WriteLine(LongPathsFix(options.CheckoutRoot));
                return 2;
            }
            host.Out.WriteLine("qualification-preflight: core.longpaths true");
            return 0;
        }

        /// <summary>
        /// Get-LaunchArgumentValue: the value after "-Name" (case-insensitive), or of "-Name:value"; null when the argument
        /// is not there or has no value.
        /// </summary>
        public static string ArgumentValue(IReadOnlyList<string> arguments, string name)
        {
            for (var index = 0; index < arguments.Count; index++)
            {
                var item = arguments[index] ?? "";
                if (string.Equals(item, "-" + name, StringComparison.OrdinalIgnoreCase)) return index + 1 < arguments.Count ? arguments[index + 1] : null;
                if (item.StartsWith("-" + name + ":", StringComparison.OrdinalIgnoreCase)) return item.Substring(name.Length + 2);
            }
            return null;
        }

        /// <summary>The launch (see the file header). Exit 1 an unreadable record, 2 refused, 3 a live run.lock, else the build slot's exit code.</summary>
        public static int Run(QualificationLaunchOptions options, NightlyHost host, IFileSystem files, BuildSlotRunner buildSlot)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (buildSlot == null) throw new ArgumentNullException(nameof(buildSlot));
            JsonElement record;
            try
            {
                record = RcQualificationRecords.ParseObject(files.File.ReadAllText(options.Config));
            }
            catch (Exception e)
            {
                host.Out.WriteLine(Prefix + "refused, the launch record " + options.Config + " cannot be read: " + e.Message);
                return 1;
            }
            var checkout = RcQualificationRecords.Text(RcQualificationRecords.Member(record, "checkoutRoot"));
            var expected = RcQualificationRecords.Text(RcQualificationRecords.Member(record, "expectedSha"));
            if (!FullSha.IsMatch(expected)) return Refuse(host, "expectedSha must be a full lowercase SHA", 2);
            var nightlyArguments = Arguments(RcQualificationRecords.Member(record, "nightlyArguments"));
            var runId = ArgumentValue(nightlyArguments, "RunId");
            if (string.IsNullOrWhiteSpace(runId)) return Refuse(host, "refused, nightlyArguments requires -RunId <id> so qualification-observe can bind the run", 2);
            if (!string.IsNullOrEmpty(options.BoundRunId) && runId != options.BoundRunId)
                return Refuse(host, "refused, -RunId " + runId + " is not the supervisor run id " + options.BoundRunId, 2);
            var stateRoot = ArgumentValue(nightlyArguments, "StateRoot");
            if (string.IsNullOrWhiteSpace(stateRoot)) stateRoot = DefaultStateRoot;
            var lockPath = NightlyRunLock.LockPath(stateRoot);
            if (files.File.Exists(lockPath) || files.Directory.Exists(lockPath))
            {
                // Refuse unless the recorded owner is provably gone (pid absent or a different start time).
                var (owner, lockRun) = LockOwner(host, files, lockPath);
                if (owner != null) return Refuse(host, "refused, " + lockPath + " is held by " + owner + " (runId " + lockRun + ")", 3);
            }
            var headResult = host.RunGit(checkout, "rev-parse", "HEAD");
            var head = Head(headResult.Output);
            if (headResult.ExitCode != 0 || head != expected) return Refuse(host, "refused, checkout HEAD " + head + " is not " + expected, 2);
            if (!LongPathsEnabled(host, checkout))
            {
                host.Out.WriteLine(Prefix + "refused, longpaths-not-enabled");
                host.Out.WriteLine(LongPathsFix(checkout));
                return 2;
            }
            var label = RcQualificationRecords.Text(RcQualificationRecords.Member(record, "buildSlotLabel"));
            if (string.IsNullOrWhiteSpace(label)) label = "rc-qualification";
            host.Out.WriteLine(Prefix + host.UtcNow.ToString("o", CultureInfo.InvariantCulture) + " build-slot -> nightly-run " + string.Join(" ", nightlyArguments));
            var command = new List<string> { options.PowerShellPath, "-NoProfile", "-File", System.IO.Path.Combine(checkout, "scripts", "nightly-run.ps1") };
            command.AddRange(nightlyArguments);
            var code = buildSlot(new BuildSlotOptions { Label = label, Command = command }, null);
            host.Out.WriteLine(Prefix + host.UtcNow.ToString("o", CultureInfo.InvariantCulture) + " exit " + code.ToString(CultureInfo.InvariantCulture));
            return code;
        }

        private static int Refuse(NightlyHost host, string message, int code)
        {
            host.Out.WriteLine(Prefix + message);
            return code;
        }

        // @($record.nightlyArguments | ForEach-Object { [string]$_ }): an array's items as text, one scalar as one item.
        private static List<string> Arguments(JsonElement? value)
        {
            var list = new List<string>();
            if (!RcQualificationRecords.Present(value)) return list;
            if (value.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.Value.EnumerateArray()) list.Add(RcQualificationRecords.Text(item));
            }
            else
            {
                list.Add(RcQualificationRecords.Text(value));
            }
            return list;
        }

        // The full SHA git printed (its stdout and stderr arrive together); else the whole text, trimmed.
        private static string Head(string output)
        {
            var lines = (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray();
            return lines.FirstOrDefault(line => FullSha.IsMatch(line)) ?? (output ?? "").Trim();
        }

        // The owner text a refusal names, or null when the owner is provably gone. Anything unreadable refuses.
        private static (string Owner, string RunId) LockOwner(NightlyHost host, IFileSystem files, string lockPath)
        {
            var owner = "unreadable owner";
            var run = "";
            try
            {
                var record = RcQualificationRecords.ParseObject(files.File.ReadAllText(lockPath));
                run = RcQualificationRecords.Text(RcQualificationRecords.Member(record, "runId"));
                var pid = Pid(RcQualificationRecords.Member(record, "pid"));
                owner = "live pid " + pid.ToString(CultureInfo.InvariantCulture);
                if (pid <= 0) return (owner, run);
                if (!host.Processes.IsAlive(pid)) return (null, run);
                var recorded = RcQualificationRecords.Member(record, "processStartedAt");
                if (!RcQualificationRecords.Truthy(recorded)) return (owner, run);
                var actual = host.Processes.StartTimeUtc(pid) ?? throw new InvalidOperationException("start unreadable");
                var expected = RcQualificationRecords.Time(recorded) ?? throw new FormatException("processStartedAt");
                if (Math.Abs((actual - expected).TotalSeconds) > 1) return (null, run);
            }
            catch (Exception)
            {
                // The owner stays what was last read: unreadable, or live.
            }
            return (owner, run);
        }

        // [int]$lock.pid: a whole number, digits in a string, or 0 for no value; anything else throws.
        private static int Pid(JsonElement? value)
        {
            if (!RcQualificationRecords.Present(value)) return 0;
            if (RcQualificationRecords.IsInteger(value)) return checked((int)value.Value.GetInt64());
            if (RcQualificationRecords.IsString(value)) return int.Parse(value.Value.GetString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
            throw new FormatException("pid");
        }
    }
}
