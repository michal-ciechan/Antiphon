// The desktop origin proof as a command (unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): the two entry
// points the CARD-1609 native harness (scripts/fixtures/c1609-retirement-real-cases.mjs) called after dot-sourcing
// scripts/c1008-host-proof.ps1, now C1609HostProof on the production children (C1609BoundedProcesses: git found on PATH):
//
//   antiphon-scripts c1609-host-proof write --origin-url <url> --repo-relative <repo> --operation-id <c1008 id> --proof-directory <dir>
//       --created-unix <seconds> --log-path <file> --phase-nonce <32 hex> --inventory-digest <64 hex> --approved-origins <url,...>
//       [--network-budget-ms <ms>] [--required-objects <oid:type,...>] [--previous-proof-file <file>] [--fresh-graph]
//   antiphon-scripts c1609-host-proof live-check --origin-url <url> --repo-relative <repo> --operation-id <c1008 id>
//       --proof-file <file> --approved-origins <url,...> [--network-budget-ms <ms>]
//
// write is Write-C1008HostOriginProof -ProofVersion 2 (Write-C1609HostOriginProof), live-check Assert-C1609LiveOriginProof. Exit 0
// proven; 1 refused (one stderr line: the refusal, which never holds a URL, a credential or Git stderr), as the harness's pwsh
// child exited on the throw; usage 2. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>The c1609-host-proof command's options.</summary>
    public sealed class C1609HostProofOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "c1609-host-proof write|live-check --origin-url <url> --repo-relative <repo> --operation-id <c1008 id> --approved-origins <url,...>"
            + " (write: --proof-directory <dir> --created-unix <s> --log-path <file> --phase-nonce <hex> --inventory-digest <hex> [--required-objects <oid:type,...>]"
            + " [--previous-proof-file <file>] [--fresh-graph]; live-check: --proof-file <file>) [--network-budget-ms <ms>]";

        /// <summary>write or live-check.</summary>
        public string Verb { get; init; } = "";

        /// <summary>The proof request (write).</summary>
        public C1609ProofRequest Request { get; init; }

        /// <summary>The proof file (live-check).</summary>
        public string ProofFile { get; init; } = "";

        /// <summary>The budget (live-check; null: the control window).</summary>
        public int? NetworkBudgetMs { get; init; }

        /// <summary>The arguments after the command name.</summary>
        public static C1609HostProofOptions Parse(IReadOnlyList<string> arguments)
        {
            if (arguments == null || arguments.Count == 0) throw new NightlyUsageException("expected write or live-check");
            var verb = arguments[0];
            var rest = arguments.Skip(1).ToArray();
            if (verb == "write")
            {
                var flags = NightlyArgs.Parse(rest,
                    new[] { "--origin-url", "--repo-relative", "--operation-id", "--proof-directory", "--created-unix", "--log-path", "--phase-nonce", "--inventory-digest",
                        "--approved-origins", "--network-budget-ms", "--required-objects", "--previous-proof-file" },
                    new[] { "--fresh-graph" });
                if (!long.TryParse(flags.Required("--created-unix"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var created))
                    throw new NightlyUsageException("--created-unix expects whole seconds");
                var required = new List<C1609RequiredObject>();
                foreach (var entry in flags.List("--required-objects"))
                {
                    var colon = entry.IndexOf(':');
                    if (colon <= 0) throw new NightlyUsageException("--required-objects expects <oid>:<type> entries, not '" + entry + "'");
                    required.Add(new C1609RequiredObject(entry.Substring(0, colon), entry.Substring(colon + 1)));
                }
                return new C1609HostProofOptions
                {
                    Verb = verb,
                    Request = new C1609ProofRequest
                    {
                        OriginUrl = flags.Required("--origin-url"), RepoRelative = flags.Required("--repo-relative"), OperationId = flags.Required("--operation-id"),
                        ProofDirectory = flags.Required("--proof-directory"), CreatedUnix = created, LogPath = flags.Required("--log-path"),
                        PhaseNonce = flags.Required("--phase-nonce"), InventoryDigest = flags.Required("--inventory-digest"),
                        ApprovedOrigins = flags.List("--approved-origins"), NetworkBudgetMs = flags.Int("--network-budget-ms", 600000), RequiredObjects = required,
                        PreviousProofFile = flags.Value("--previous-proof-file", ""), FreshGraph = flags.Switch("--fresh-graph"),
                    },
                };
            }
            if (verb == "live-check")
            {
                var flags = NightlyArgs.Parse(rest, new[] { "--origin-url", "--repo-relative", "--operation-id", "--proof-file", "--approved-origins", "--network-budget-ms" },
                    Array.Empty<string>());
                return new C1609HostProofOptions
                {
                    Verb = verb,
                    Request = new C1609ProofRequest
                    {
                        OriginUrl = flags.Required("--origin-url"), RepoRelative = flags.Required("--repo-relative"), OperationId = flags.Required("--operation-id"),
                        ApprovedOrigins = flags.List("--approved-origins"),
                    },
                    ProofFile = flags.Required("--proof-file"),
                    NetworkBudgetMs = flags.Has("--network-budget-ms") ? flags.Int("--network-budget-ms", 0) : null,
                };
            }
            throw new NightlyUsageException("expected write or live-check, not '" + verb + "'");
        }
    }

    /// <summary>The c1609-host-proof command.</summary>
    public static class C1609HostProofCommand
    {
        /// <summary>Runs the verb on proof (its children and files); exit 0 proven, 1 refused.</summary>
        public static int Run(C1609HostProofOptions options, NightlyHost host, C1609HostProof proof)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (proof == null) throw new ArgumentNullException(nameof(proof));
            try
            {
                var r = options.Request;
                if (options.Verb == "write") proof.WriteOriginProof(r);
                else proof.AssertLiveOriginProof(r.OriginUrl, r.RepoRelative, r.OperationId, options.ProofFile, r.ApprovedOrigins, options.NetworkBudgetMs);
                return 0;
            }
            catch (C1609Refusal e)
            {
                host.Error.WriteLine("c1609-host-proof: " + e.Message);
                return 1;
            }
        }

        /// <summary>The production proof: bounded children on PATH, the real file system, the host's clock and variables.</summary>
        public static C1609HostProof Production(NightlyHost host, IFileSystem files) =>
            new C1609HostProof(new C1609BoundedProcesses(files), files, () => host.Clock.GetUtcNow(), Guid.NewGuid, host.Variables);
    }
}
