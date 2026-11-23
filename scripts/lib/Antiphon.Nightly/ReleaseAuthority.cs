// CARD-0599 D-15 (B1): the pinned policy and the full execution-ledger publication authority (moved from
// release-authority.ps1).
//
// The authority for an RC publication is the policy blob at the candidate's pinned commit plus the frozen execution plan
// and its full execution ledger. A summary or report is an OUTPUT of validation and never a source of required suites or
// success.
//
// Files under <candidate root>/authority/:
//   policy.json            raw bytes of <sha>:tests/test-execution-policy.json
//   authority.json         pinned identity: repository/ref/sha, blob id, digests, suites
//   discovery.json         full discovery roster per native suite (incl. excluded rows)
//   execution-plan.json    frozen chunk manifest (native) and declared entry rosters
//   ledger.json            execution records: prerequisites, chunks, rosters and the typed prepublication receipt (the
//                          release card's readback), which the publisher re-reads from the card itself through the
//                          Antiphon API (GET /api/cards/{id}) before it trusts it
//   publication-authority.json  digest over all of the above, written by the publisher
//
// Every git read here is a real git child with a bounded wait: the policy blob is never taken from a working tree.
// JSON types are checked strictly before any comparison: a one-element array is not the scalar it holds, a comma-joined
// string is not a list, and no identity is compared through a string conversion. The digest chain is recomputable by
// whoever writes these files, so repository, coordinator and clock bind to things outside them: the checkout's origin,
// the publication destination, the supported coordinator version and the publisher's (injectable) clock.
//
// Documents are read with System.Text.Json. Values handed in by PowerShell (ConvertFrom-Json objects, hashtables) are
// read through AuthorityValue, which reaches PowerShell types only for such objects (PsFields).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public sealed class GitRun
    {
        public int ExitCode { get; set; }
        public byte[] Bytes { get; set; } = new byte[0];
        public string Text { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public sealed class PinnedBlob
    {
        public string BlobId { get; set; } = "";
        public byte[] Bytes { get; set; } = new byte[0];
    }

    /// <summary>A plain record whose fields AuthorityValue reads directly (by JSON name, ignoring case).</summary>
    internal interface IFieldSource
    {
        OrderedDictionary Fields();
    }

    /// <summary>One pinned exclusion row as authority.json records it.</summary>
    public sealed class AuthorityExclusion : IJsonWritable, IFieldSource
    {
        public string Suite { get; set; } = "";
        public string Class { get; set; } = "";
        public string[] Methods { get; set; } = new string[0];
        public string Reason { get; set; } = "";
        public string Owner { get; set; } = "";

        OrderedDictionary IFieldSource.Fields() => ToMap();

        internal OrderedDictionary ToMap() => new OrderedDictionary
        {
            { "suite", Suite }, { "class", Class }, { "methods", Methods ?? new string[0] }, { "reason", Reason }, { "owner", Owner },
        };

        public void WriteJson(Utf8JsonWriter writer) => PlainJson.Write(writer, ToMap());
    }

    /// <summary>The policy blob as the release authority reads it: the rc suites and their windows exclusions.</summary>
    public sealed class AuthorityPolicy
    {
        public ExecutionPolicy Object { get; set; }
        public string PolicyHash { get; set; } = "";
        public string[] RequiredSuites { get; set; } = new string[0];
        public AuthorityExclusion[] Exclusions { get; set; } = new AuthorityExclusion[0];
    }

    /// <summary>authority.json as consumers read it. The checks read the document itself (strict types).</summary>
    public sealed class AuthorityRecord : IJsonWritable
    {
        public long? SchemaVersion { get; set; }
        public string Kind { get; set; } = "";
        public string Repository { get; set; } = "";
        public string CandidateId { get; set; } = "";
        public string CandidateRef { get; set; } = "";
        public string IntentId { get; set; } = "";
        public string ReleaseCardId { get; set; } = "";
        public string Sha { get; set; } = "";
        public string Profile { get; set; } = "";
        public string CoordinatorVersion { get; set; } = "";
        public string PolicyPath { get; set; } = "";
        public string PolicyBlobId { get; set; } = "";
        public string PolicyRawSha256 { get; set; } = "";
        public string PolicyHash { get; set; } = "";
        public string[] RequiredSuites { get; set; } = new string[0];
        public AuthorityExclusion[] Exclusions { get; set; } = new AuthorityExclusion[0];
        public OrderedDictionary ScriptHashes { get; set; } = new OrderedDictionary();
        public string CreatedAt { get; set; } = "";

        internal object Document { get; set; }

        /// <summary>The authority.json at path as consumers read it; null when it is missing or not JSON.</summary>
        public static AuthorityRecord Read(string path)
        {
            var doc = PlainJson.Read(path);
            return doc == null ? null : From(doc);
        }

        public void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion ?? 0);
            writer.WriteString("kind", Kind);
            writer.WriteString("repository", Repository);
            writer.WriteString("candidateId", CandidateId);
            writer.WriteString("candidateRef", CandidateRef);
            writer.WriteString("intentId", IntentId);
            writer.WriteString("releaseCardId", ReleaseCardId);
            writer.WriteString("sha", Sha);
            writer.WriteString("profile", Profile);
            writer.WriteString("coordinatorVersion", CoordinatorVersion);
            writer.WriteString("policyPath", PolicyPath);
            writer.WriteString("policyBlobId", PolicyBlobId);
            writer.WriteString("policyRawSha256", PolicyRawSha256);
            writer.WriteString("policyHash", PolicyHash);
            writer.WritePropertyName("requiredSuites");
            PlainJson.Write(writer, RequiredSuites);
            writer.WritePropertyName("exclusions");
            PlainJson.Write(writer, Exclusions);
            writer.WritePropertyName("scriptHashes");
            PlainJson.Write(writer, ScriptHashes);
            writer.WriteString("createdAt", CreatedAt);
            writer.WriteEndObject();
        }

        // Lenient: every field as a string conversion would read it. The checks use Document.
        internal static AuthorityRecord From(object doc)
        {
            var record = new AuthorityRecord { Document = doc };
            var schema = AuthorityValue.Get(doc, "schemaVersion");
            if (AuthorityValue.IsInteger(schema)) record.SchemaVersion = Convert.ToInt64(AuthorityValue.Norm(schema), CultureInfo.InvariantCulture);
            record.Kind = AuthorityValue.Str(AuthorityValue.Get(doc, "kind"));
            record.Repository = AuthorityValue.Str(AuthorityValue.Get(doc, "repository"));
            record.CandidateId = AuthorityValue.Str(AuthorityValue.Get(doc, "candidateId"));
            record.CandidateRef = AuthorityValue.Str(AuthorityValue.Get(doc, "candidateRef"));
            record.IntentId = AuthorityValue.Str(AuthorityValue.Get(doc, "intentId"));
            record.ReleaseCardId = AuthorityValue.Str(AuthorityValue.Get(doc, "releaseCardId"));
            record.Sha = AuthorityValue.Str(AuthorityValue.Get(doc, "sha"));
            record.Profile = AuthorityValue.Str(AuthorityValue.Get(doc, "profile"));
            record.CoordinatorVersion = AuthorityValue.Str(AuthorityValue.Get(doc, "coordinatorVersion"));
            record.PolicyPath = AuthorityValue.Str(AuthorityValue.Get(doc, "policyPath"));
            record.PolicyBlobId = AuthorityValue.Str(AuthorityValue.Get(doc, "policyBlobId"));
            record.PolicyRawSha256 = AuthorityValue.Str(AuthorityValue.Get(doc, "policyRawSha256"));
            record.PolicyHash = AuthorityValue.Str(AuthorityValue.Get(doc, "policyHash"));
            record.RequiredSuites = AuthorityValue.Elements(AuthorityValue.Get(doc, "requiredSuites")).Select(AuthorityValue.Str).ToArray();
            record.Exclusions = AuthorityValue.Elements(AuthorityValue.Get(doc, "exclusions")).Select(e => new AuthorityExclusion
            {
                Suite = AuthorityValue.Str(AuthorityValue.Get(e, "suite")),
                Class = AuthorityValue.Str(AuthorityValue.Get(e, "class")),
                Methods = AuthorityValue.Elements(AuthorityValue.Get(e, "methods")).Select(AuthorityValue.Str).ToArray(),
                Reason = AuthorityValue.Str(AuthorityValue.Get(e, "reason")),
                Owner = AuthorityValue.Str(AuthorityValue.Get(e, "owner")),
            }).ToArray();
            var hashes = AuthorityValue.Get(doc, "scriptHashes");
            foreach (var name in AuthorityValue.Names(hashes))
                if (!record.ScriptHashes.Contains(name)) record.ScriptHashes.Add(name, AuthorityValue.Str(AuthorityValue.Get(hashes, name)));
            record.CreatedAt = AuthorityValue.Str(AuthorityValue.Get(doc, "createdAt"));
            return record;
        }
    }

    /// <summary>Test-ReleaseGatePinnedAuthority's result.</summary>
    public sealed class PinnedAuthority
    {
        public bool Ok { get; set; }
        public string[] Reasons { get; set; } = new string[0];
        public AuthorityRecord Authority { get; set; }
        public AuthorityPolicy Policy { get; set; }
        public string Digest { get; set; } = "";
    }

    /// <summary>Test-ReleaseGateExecutionLedger's result: Suites are the validated summary rows.</summary>
    public sealed class LedgerCheck
    {
        public bool Ok { get; set; }
        public string[] Reasons { get; set; } = new string[0];
        public OrderedDictionary[] Suites { get; set; } = new OrderedDictionary[0];
    }

    /// <summary>Test-ReleaseGateAuthority's result.</summary>
    public sealed class AuthorityCheck
    {
        public bool Ok { get; set; }
        public string[] Reasons { get; set; } = new string[0];
        public PinnedAuthority Pinned { get; set; }
        public OrderedDictionary[] Suites { get; set; } = new OrderedDictionary[0];
    }

    public sealed class PublicationAuthority
    {
        public bool Ok { get; set; }
        public string Reason { get; set; } = "";
        public string Digest { get; set; } = "";
        public string Path { get; set; } = "";
    }

    public sealed class CardReadback
    {
        public bool Ok { get; set; }
        /// <summary>The card document (a JSON object); null when unavailable.</summary>
        public object Card { get; set; }
    }

    public static class ReleaseAuthority
    {
        public const string PolicyPath = "tests/test-execution-policy.json";
        public static readonly string[] RcSuites = { "antiphon", "session-runner", "pty-host", "agents-pty", "messaging", "client", "scripts", "e2e" };
        public static readonly string[] RosterSuites = { "client", "scripts" };
        public const int Schema = 1;
        public const string CoordinatorVersion = "c599-b1";
        /// <summary>A git read's bounded wait.</summary>
        public static int GitTimeoutMilliseconds { get; set; } = 30000;
        /// <summary>Evidence may not be dated after the publisher's clock by more than this skew.</summary>
        public const int ClockSkewSeconds = 120;
        public const string ReceiptKind = "prepublication";
        public const string RecipientKind = "release-card";
        /// <summary>
        /// D-14 readback: the release card body carries exactly one line
        /// "release-correlation: intentId=&lt;id&gt; candidateId=&lt;id&gt; sha=&lt;full sha&gt; runId=&lt;id&gt;".
        /// </summary>
        public const string CorrelationPrefix = "release-correlation:";
        public static int CardReadTimeoutSeconds { get; set; } = 15;
        /// <summary>Test seam: reads the bound release card (base URL, card id); null reads it over HTTP (ReadReleaseCard).</summary>
        public static Func<string, string, CardReadback> CardReader { get; set; }
        private static readonly Regex RepositoryShape = new Regex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);
        private static readonly Regex FullShaShape = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex RemoteShape = new Regex(@"^(?:https://github\.com/|ssh://git@github\.com/|git@github\.com:)(?<owner>[A-Za-z0-9_.-]+)/(?<name>[A-Za-z0-9_.-]+?)(?:\.git)?/?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SourceFile = new Regex(@"\.(cs|csproj)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex BuildOutput = new Regex("^(bin|obj)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The scripts pinned by digest, relative to the scripts directory. W2-nightly S27 deleted lib/nightly-coverage.ps1,
        /// unit 12 of 2026-11-22-remaining-ps1-to-csharp.md lib/release-gate.ps1 (now ReleaseGateRemote.cs,
        /// ReleaseGateJournal.cs and ReleaseGateTags.cs) and unit 14 publish-release.ps1 and promote-release.ps1 (now
        /// ReleasePublish.cs, ReleasePromote.cs and ReleaseGateHost.cs, run as antiphon-scripts publish-release and
        /// promote-release). Their C# is in lib/Antiphon.Nightly, pinned by that tree's SourceTrees digest. An older record
        /// that still binds a deleted script verifies, because Pinned checks only the keys listed here and SourceTrees.
        /// </summary>
        public static readonly string[] Scripts =
        {
            "lib/nightly-common.ps1", "lib/nightly-core.ps1",
        };

        /// <summary>
        /// The compiled sources these scripts call (this class, the policy rules and the other core helpers), one digest per
        /// project tree, relative to the scripts directory.
        /// </summary>
        public static readonly string[] SourceTrees = { "lib/Antiphon.Nightly", "../src/Antiphon.IO.WriteBehind" };

        // Get-ReleaseAuthorityPinnedPolicy: each mirrored test project, once declared in projectDispositions, adds its rc
        // suite (CARD-1564 observability, CARD-1546 server-git, CARD-1829 resilience, CARD-1830 git-in-process).
        private static readonly (string Project, string Suite)[] DispositionSuites =
        {
            ("tests/Antiphon.Observability.Tests/Antiphon.Observability.Tests.csproj", "observability"),
            ("tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj", "server-git"),
            ("tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj", "resilience"),
            ("tests/Antiphon.GitInProcess.Tests/Antiphon.GitInProcess.Tests.csproj", "git-in-process"),
        };

        private static NightlyRefusal Refuse(string reason) => new NightlyRefusal(reason);

        // ---- helpers ----------------------------------------------------------------------------------------

        /// <summary>The candidate's authority directory.</summary>
        public static string Dir(string candidateRoot) => System.IO.Path.Combine(candidateRoot ?? "", "authority");

        public static bool IsFullSha(string sha) => !string.IsNullOrWhiteSpace(sha) && FullShaShape.IsMatch(sha);

        /// <summary>A JSON string instant (or a DateTime) in UTC; null for anything else.</summary>
        public static DateTime? Instant(object value) => AuthorityValue.Instant(value);

        public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes ?? new byte[0])).ToLowerInvariant();

        public static string Sha256Text(string text) => Sha256(Encoding.UTF8.GetBytes(text ?? ""));

        /// <summary>A file's SHA-256 (lowercase hex). Refuses a missing file.</summary>
        public static string FileSha256(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw Refuse("missing file for hash " + path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        /// <summary>A file's SHA-256, or "" when it does not exist.</summary>
        public static string FileDigest(string path) => File.Exists(path) ? FileSha256(path) : "";

        /// <summary>git's object id for a blob: SHA-1 of "blob &lt;length&gt;\0" and the bytes, computed locally.</summary>
        public static string GitBlobId(byte[] bytes)
        {
            bytes = bytes ?? new byte[0];
            var header = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture));
            var all = new byte[header.Length + 1 + bytes.Length];
            Buffer.BlockCopy(header, 0, all, 0, header.Length);
            Buffer.BlockCopy(bytes, 0, all, header.Length + 1, bytes.Length);
            return Convert.ToHexString(SHA1.HashData(all)).ToLowerInvariant();
        }

        /// <summary>
        /// A real git child with captured raw stdout bytes and a bounded wait. On timeout the child is killed and joined
        /// before returning; the result is a failure. Command-line config overrides a system core.fsmonitor=true.
        /// </summary>
        public static GitRun Git(string repositoryRoot, params string[] arguments) => GitWithInput(repositoryRoot, null, arguments);

        /// <summary>Git with stdinText written to the child's standard input (closed after it); null leaves it unredirected.</summary>
        public static GitRun GitWithInput(string repositoryRoot, string stdinText, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                RedirectStandardInput = stdinText != null,
            };
            foreach (var a in new[] { "-c", "core.fsmonitor=false", "-c", "gc.auto=0", "-c", "maintenance.auto=false", "-C", repositoryRoot ?? "" }) start.ArgumentList.Add(a);
            foreach (var a in arguments ?? new string[0]) start.ArgumentList.Add(a ?? "");
            Process process = null;
            try
            {
                process = Process.Start(start);
                if (stdinText != null)
                {
                    var input = new UTF8Encoding(false).GetBytes(stdinText);
                    process.StandardInput.BaseStream.Write(input, 0, input.Length);
                    process.StandardInput.Close();
                }
                var buffer = new MemoryStream();
                var copy = process.StandardOutput.BaseStream.CopyToAsync(buffer);
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(GitTimeoutMilliseconds))
                {
                    try { process.Kill(true); } catch (Exception) { }
                    process.WaitForExit();
                    return new GitRun { ExitCode = -1, Error = "git timed out" };
                }
                process.WaitForExit();
                copy.Wait(GitTimeoutMilliseconds);
                var bytes = buffer.ToArray();
                return new GitRun { ExitCode = process.ExitCode, Bytes = bytes, Text = Encoding.UTF8.GetString(bytes), Error = error.Result ?? "" };
            }
            catch (Exception ex) { return new GitRun { ExitCode = -1, Error = ex.Message }; }
            finally { process?.Dispose(); }
        }

        /// <summary>owner/name from a GitHub remote URL (https, ssh or scp form); "" for anything else.</summary>
        public static string RepositoryName(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            var m = RemoteShape.Match(url.Trim());
            return m.Success ? m.Groups["owner"].Value + "/" + m.Groups["name"].Value : "";
        }

        /// <summary>The checkout's origin through real git, as owner/name.</summary>
        public static string OriginRepository(string repositoryRoot)
        {
            var origin = Git(repositoryRoot, "remote", "get-url", "origin");
            return origin.ExitCode == 0 ? RepositoryName(origin.Text) : "";
        }

        // ---- the pinned policy ------------------------------------------------------------------------------

        /// <summary>
        /// The policy blob's bytes: schema, stated canonical hash and the rc profile. Refuses with a stable reason token
        /// (policy-unparseable, policy-schema-unsupported, policy-hash-stale, policy-rc-profile-missing,
        /// policy-rc-suite-missing:..., policy-rc-suite-unknown:..., policy-rc-suite-undefined:...).
        /// </summary>
        public static AuthorityPolicy PolicyFromBytes(byte[] bytes)
        {
            var text = Encoding.UTF8.GetString(bytes ?? new byte[0]);
            ExecutionPolicy policy;
            try { policy = ExecutionPolicy.Parse(text); }
            catch (Exception ex) when (ex is JsonException || ex is NightlyRefusal) { throw Refuse("policy-unparseable"); }
            if (policy.SchemaVersion < 2 || policy.SchemaVersion > 4) throw Refuse("policy-schema-unsupported");
            if (string.IsNullOrWhiteSpace(policy.PolicyHash) || !string.Equals(policy.PolicyHash, policy.ComputedHash, StringComparison.OrdinalIgnoreCase))
                throw Refuse("policy-hash-stale");
            ResolvedProfile profile;
            try { profile = NightlyPolicy.Profile(policy, "rc"); }
            catch (NightlyRefusal) { throw Refuse("policy-rc-profile-missing"); }
            var suites = profile.RequiredSuites.Select(s => s ?? "").ToArray();
            var required = new List<string>(RcSuites);
            if (policy.SchemaVersion >= 3)
            {
                required.Add("markdown-pdf");
                NightlyPolicy.ProjectDispositions(policy);
                foreach (var (project, suite) in DispositionSuites)
                    if ((policy.ProjectDispositions ?? new PolicyProjectDisposition[0]).Any(r => string.Equals(r.Project, project, StringComparison.Ordinal))) required.Add(suite);
            }
            var missing = required.Where(r => !suites.Contains(r, StringComparer.OrdinalIgnoreCase)).ToArray();
            var unknown = suites.Where(s => !required.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (missing.Length > 0) throw Refuse("policy-rc-suite-missing:" + string.Join("+", missing));
            if (unknown.Length > 0) throw Refuse("policy-rc-suite-unknown:" + string.Join("+", unknown));
            foreach (var id in suites) if (!policy.Suites.ContainsKey(id)) throw Refuse("policy-rc-suite-undefined:" + id);
            var exclusions = new List<AuthorityExclusion>();
            // Creation and validation share this reader. The release obligation stays Windows regardless of the host.
            foreach (var id in suites)
                foreach (var row in NightlyPolicy.ProfileExclusions(policy, "rc", id, "windows"))
                    exclusions.Add(new AuthorityExclusion { Suite = row.Suite, Class = row.Class, Methods = row.Methods ?? new string[0], Reason = row.Reason, Owner = row.Owner });
            return new AuthorityPolicy { Object = policy, PolicyHash = policy.ComputedHash, RequiredSuites = suites, Exclusions = exclusions.ToArray() };
        }

        /// <summary>
        /// The policy blob at the pinned commit, read through git (never the working tree) and identity-checked. One git
        /// child: cat-file --batch resolves sha:path as rev-parse does and returns the object's id, type, size and bytes.
        /// An unresolvable name is policy-blob-unresolved; a name that is not a blob, or output that does not hold the
        /// stated size, is policy-blob-unreadable.
        /// </summary>
        public static PinnedBlob ReadPinnedBlob(string repositoryRoot, string sha)
        {
            var name = (sha ?? "") + ":" + PolicyPath;
            if (name.IndexOf('\n') >= 0 || name.IndexOf('\r') >= 0) throw Refuse("policy-blob-unresolved");
            var run = GitWithInput(repositoryRoot, name + "\n", "cat-file", "--batch");
            if (run.ExitCode != 0) throw Refuse("policy-blob-unresolved");
            var bytes = run.Bytes ?? new byte[0];
            var eol = Array.IndexOf(bytes, (byte)'\n');
            if (eol < 0) throw Refuse("policy-blob-unresolved");
            var header = Encoding.ASCII.GetString(bytes, 0, eol).Split(' ');
            if (header.Length != 3 || !IsObjectId(header[0])) throw Refuse("policy-blob-unresolved");
            if (header[1] != "blob" || !int.TryParse(header[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                || size < 0 || bytes.Length < eol + 1 + size) throw Refuse("policy-blob-unreadable");
            var blobId = header[0];
            var blob = new byte[size];
            Buffer.BlockCopy(bytes, eol + 1, blob, 0, size);
            if (!string.Equals(GitBlobId(blob), blobId, StringComparison.OrdinalIgnoreCase)) throw Refuse("policy-blob-identity");
            return new PinnedBlob { BlobId = blobId, Bytes = blob };
        }

        private static bool IsObjectId(string text) =>
            (text.Length == 40 || text.Length == 64) && text.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

        /// <summary>The pinned scripts' digests, then one digest per compiled source tree, keyed by relative path.</summary>
        public static OrderedDictionary ScriptHashes(string scriptsRoot)
        {
            var hashes = new OrderedDictionary();
            foreach (var rel in Scripts) hashes[rel] = FileSha256(System.IO.Path.Combine(scriptsRoot, rel));
            foreach (var rel in SourceTrees) hashes[rel] = SourceTreeDigest(System.IO.Path.Combine(scriptsRoot, rel));
            return hashes;
        }

        /// <summary>
        /// One project's sources: SHA-256 over "&lt;relative path&gt; &lt;file SHA-256&gt;" lines of its .cs and .csproj files (bin
        /// and obj excluded), in ordinal path order. A missing tree digests as the empty list.
        /// </summary>
        public static string SourceTreeDigest(string root)
        {
            var lines = new List<string>();
            if (Directory.Exists(root))
            {
                var full = System.IO.Path.GetFullPath(root);
                var rels = new List<string>();
                foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(full.Length).Replace('\\', '/').TrimStart('/');
                    if (BuildOutput.IsMatch(rel) || !SourceFile.IsMatch(rel)) continue;
                    rels.Add(rel);
                }
                rels.Sort(StringComparer.Ordinal);
                foreach (var rel in rels) lines.Add(rel + " " + FileSha256(System.IO.Path.Combine(full, rel)));
            }
            return Sha256Text(string.Join("\n", lines));
        }

        // ---- freeze -------------------------------------------------------------------------------------------

        /// <summary>
        /// Cut preparation (New-ReleaseGateAuthority): freezes the policy blob at the pinned SHA plus the candidate identity
        /// into authority/policy.json and authority/authority.json. Refuses on a missing/unknown schema, stale hash, missing
        /// rc profile or any missing member of the rc suite set. createdUtc MinValue: now.
        /// </summary>
        public static AuthorityRecord Freeze(string repositoryRoot, string candidateRoot, string sha, string repository, string candidateId,
            string candidateRef, string intentId, string releaseCardId, DateTime createdUtc, string scriptsRoot)
        {
            if (!IsFullSha(sha)) throw Refuse("authority-sha-not-full");
            foreach (var (name, value) in new[] { ("repository", repository), ("candidateId", candidateId), ("candidateRef", candidateRef), ("intentId", intentId), ("releaseCardId", releaseCardId) })
                if (string.IsNullOrWhiteSpace(value)) throw Refuse("authority-missing:" + name);
            if (!RepositoryShape.IsMatch(repository)) throw Refuse("authority-repository-shape");
            if (!Guid.TryParseExact(releaseCardId, "D", out var cardId)) throw Refuse("authority-release-card-shape");
            if (!string.Equals(OriginRepository(repositoryRoot), repository, StringComparison.OrdinalIgnoreCase)) throw Refuse("checkout-origin-repository");
            var blob = ReadPinnedBlob(repositoryRoot, sha);
            var policy = PolicyFromBytes(blob.Bytes);
            var dir = Dir(candidateRoot);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(System.IO.Path.Combine(dir, "policy.json"), blob.Bytes);
            if (createdUtc == DateTime.MinValue) createdUtc = DateTime.UtcNow;
            var record = new AuthorityRecord
            {
                SchemaVersion = Schema, Kind = "release-authority", Repository = repository, CandidateId = candidateId, CandidateRef = candidateRef,
                IntentId = intentId, ReleaseCardId = cardId.ToString("D"), Sha = sha, Profile = "rc", CoordinatorVersion = CoordinatorVersion,
                PolicyPath = PolicyPath, PolicyBlobId = blob.BlobId, PolicyRawSha256 = Sha256(blob.Bytes), PolicyHash = policy.PolicyHash,
                RequiredSuites = policy.RequiredSuites, Exclusions = policy.Exclusions, ScriptHashes = ScriptHashes(scriptsRoot),
                CreatedAt = createdUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            };
            PlainJson.WriteAtomic(System.IO.Path.Combine(dir, "authority.json"), record);
            return record;
        }

        /// <summary>Required = full discovery minus the pinned exclusions (class or class+method) of the suite.</summary>
        public static string[] RequiredUids(object nodes, object exclusions, string suiteId)
        {
            var rows = new List<(string Class, string[] Methods)>();
            foreach (var e in AuthorityValue.Elements(exclusions))
            {
                if (!string.Equals(AuthorityValue.Str(AuthorityValue.Get(e, "suite")), suiteId ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                rows.Add((AuthorityValue.Str(AuthorityValue.Get(e, "class")), AuthorityValue.Elements(AuthorityValue.Get(e, "methods")).Select(AuthorityValue.Str).ToArray()));
            }
            var required = new List<string>();
            foreach (var n in AuthorityValue.Elements(nodes))
            {
                var cls = AuthorityValue.Str(AuthorityValue.Get(n, "class"));
                if (Excluded(rows, cls, AuthorityValue.Str(AuthorityValue.Get(n, "method")))) continue;
                required.Add(AuthorityValue.Str(AuthorityValue.Get(n, "uid")));
            }
            return required.ToArray();
        }

        // D-5: a declared class row (all methods, or the node's method) names the node's simple class name or its class,
        // ignoring case; a row without a class names nothing.
        private static bool Excluded(List<(string Class, string[] Methods)> rows, string className, string method)
        {
            var simple = NightlyClassNames.Simple(className);
            if (string.IsNullOrWhiteSpace(simple)) simple = className;
            var all = false;
            var methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hit = false;
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Class)) continue;
                if (!string.Equals(row.Class, simple, StringComparison.OrdinalIgnoreCase) && !string.Equals(row.Class, className, StringComparison.OrdinalIgnoreCase)) continue;
                hit = true;
                if (row.Methods.Length == 0) { all = true; continue; }
                foreach (var m in row.Methods) methods.Add(m);
            }
            return hit && (all || methods.Contains(method));
        }

        /// <summary>
        /// Freezes the discovery roster and the chunk manifest before execution (New-ReleaseGateExecutionPlan): one native
        /// class per chunk, and each roster suite's own declared entries. discovery: suite id to nodes { uid, class, method };
        /// rosters: suite id to entries.
        /// </summary>
        public static OrderedDictionary Plan(string candidateRoot, IDictionary discovery, IDictionary rosters)
        {
            var dir = Dir(candidateRoot);
            var authorityPath = System.IO.Path.Combine(dir, "authority.json");
            var authority = PlainJson.Read(authorityPath);
            var discoveryDoc = new OrderedDictionary { { "schemaVersion", 1 }, { "suites", new OrderedDictionary(StringComparer.OrdinalIgnoreCase) } };
            var planSuites = new List<object>();
            var plan = new OrderedDictionary
            {
                { "schemaVersion", 1 },
                { "candidateId", AuthorityValue.Str(AuthorityValue.Get(authority, "candidateId")) },
                { "intentId", AuthorityValue.Str(AuthorityValue.Get(authority, "intentId")) },
                { "sha", AuthorityValue.Str(AuthorityValue.Get(authority, "sha")) },
                { "authorityDigest", FileSha256(authorityPath) },
                { "discoveryDigest", "" },
                { "suites", planSuites },
            };
            var exclusions = AuthorityValue.Get(authority, "exclusions");
            foreach (var suite in AuthorityValue.Elements(AuthorityValue.Get(authority, "requiredSuites")))
            {
                var id = AuthorityValue.Str(suite);
                if (RosterSuites.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    planSuites.Add(new OrderedDictionary { { "id", id }, { "kind", "roster" }, { "entries", AuthorityValue.Elements(Lookup(rosters, id)).Select(AuthorityValue.Str).ToArray() } });
                    continue;
                }
                var nodes = AuthorityValue.Elements(Lookup(discovery, id)).Select(n => (object)new OrderedDictionary
                {
                    { "uid", AuthorityValue.Str(AuthorityValue.Get(n, "uid")) },
                    { "class", AuthorityValue.Str(AuthorityValue.Get(n, "class")) },
                    { "method", AuthorityValue.Str(AuthorityValue.Get(n, "method")) },
                }).ToArray();
                ((OrderedDictionary)discoveryDoc["suites"])[id] = nodes;
                var required = new HashSet<string>(RequiredUids(nodes, exclusions, id), StringComparer.OrdinalIgnoreCase);
                var byClass = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
                foreach (OrderedDictionary n in nodes)
                {
                    var uid = (string)n["uid"];
                    if (!required.Contains(uid)) continue;
                    var cls = (string)n["class"];
                    if (!byClass.Contains(cls)) byClass[cls] = new List<string>();
                    ((List<string>)byClass[cls]).Add(uid);
                }
                var chunks = new List<object>();
                var i = 0;
                foreach (DictionaryEntry entry in byClass)
                {
                    i++;
                    chunks.Add(new OrderedDictionary
                    {
                        { "id", id + "-" + i.ToString("d3", CultureInfo.InvariantCulture) },
                        { "classes", new[] { (string)entry.Key } },
                        { "uids", ((List<string>)entry.Value).ToArray() },
                    });
                }
                planSuites.Add(new OrderedDictionary { { "id", id }, { "kind", "native" }, { "chunks", chunks.ToArray() } });
            }
            var discoveryPath = System.IO.Path.Combine(dir, "discovery.json");
            PlainJson.WriteAtomic(discoveryPath, discoveryDoc);
            plan["discoveryDigest"] = FileSha256(discoveryPath);
            plan["suites"] = planSuites.ToArray();
            PlainJson.WriteAtomic(System.IO.Path.Combine(dir, "execution-plan.json"), plan);
            return plan;
        }

        private static object Lookup(IDictionary map, string key)
        {
            if (map == null) return null;
            foreach (DictionaryEntry e in map)
                if (string.Equals(Convert.ToString(e.Key, CultureInfo.InvariantCulture), key, StringComparison.OrdinalIgnoreCase)) return e.Value;
            return null;
        }

        // ---- validation ---------------------------------------------------------------------------------------

        /// <summary>
        /// G-287: evidence must be a relative path that resolves under the candidate root with no link or reparse component.
        /// "" when it escapes, when a component is a reparse point, or when an existing component's attributes cannot be read
        /// (not-found components are skipped). Nothing is cached.
        /// </summary>
        public static string ResolveEvidencePath(string candidateRoot, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative)) return "";
            var root = System.IO.Path.GetFullPath(candidateRoot).TrimEnd('\\', '/');
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
            if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "";
            var cursor = full;
            while (cursor.Length > root.Length)
            {
                try
                {
                    if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return "";
                }
                catch (Exception ex)
                {
                    var inner = ex;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    if (!(inner is FileNotFoundException) && !(inner is DirectoryNotFoundException)) return "";
                }
                cursor = System.IO.Path.GetDirectoryName(cursor);
                if (string.IsNullOrEmpty(cursor)) break;
            }
            return full;
        }

        /// <summary>
        /// D-14 readback: the release card as the Antiphon API serves it, read-only - one GET /api/cards/{id}, no proxy, no
        /// redirect, bounded wait. Only a 200 answer whose body is one JSON object is a readback; anything else is
        /// unavailable, and the caller refuses. Nothing falls back to the ledger's own copy of the receipt.
        /// </summary>
        public static CardReadback ReadReleaseCard(string baseUrl, string cardId)
        {
            var unavailable = new CardReadback { Ok = false };
            if (string.IsNullOrWhiteSpace(baseUrl) || !Guid.TryParseExact(cardId ?? "", "D", out _)) return unavailable;
            if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/api/cards/" + cardId, UriKind.Absolute, out var uri)) return unavailable;
            if (uri.Scheme != "http" && uri.Scheme != "https") return unavailable;
            try
            {
                using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(CardReadTimeoutSeconds) };
                using var response = client.GetAsync(uri).GetAwaiter().GetResult();
                if ((int)response.StatusCode != 200) return unavailable;
                var card = PlainJson.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                if (!AuthorityValue.IsObject(card)) return unavailable;
                return new CardReadback { Ok = true, Card = card };
            }
            catch (Exception) { return unavailable; }
        }

        /// <summary>
        /// The release card body carries exactly one correlation line naming exactly these identities: no key missing,
        /// repeated, renamed (keys are case-sensitive) or added, no second correlation line, and every value equal to a
        /// non-empty expectation.
        /// </summary>
        public static bool CardCorrelation(object description, IDictionary expected)
        {
            if (!(AuthorityValue.Norm(description) is string text)) return false;
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith(CorrelationPrefix, StringComparison.Ordinal)).ToList();
            if (lines.Count != 1) return false;
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var token in Regex.Split(lines[0].Substring(CorrelationPrefix.Length).Trim(), " +"))
            {
                var i = token.IndexOf('=');
                if (i <= 0) return false;
                var key = token.Substring(0, i);
                if (seen.ContainsKey(key)) return false;
                seen[key] = token.Substring(i + 1);
            }
            if (seen.Count != expected.Count) return false;
            foreach (DictionaryEntry e in expected)
            {
                var want = Convert.ToString(e.Value, CultureInfo.InvariantCulture) ?? "";
                var key = Convert.ToString(e.Key, CultureInfo.InvariantCulture) ?? "";
                if (want.Length == 0 || !seen.TryGetValue(key, out var have)) return false;
                if (!string.Equals(have, want, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>
        /// Publisher-side reload of the frozen authority (Test-ReleaseGatePinnedAuthority). The policy blob is re-read at the
        /// pinned SHA through git and must match the private copy, the recorded blob id, raw digest and canonical hash; the
        /// required suite set is exactly the pinned one. Every field is type-checked before it is compared. repository must
        /// name the publication destination AND the checkout's origin; coordinatorVersion must be the one this publisher
        /// supports.
        /// </summary>
        public static PinnedAuthority Pinned(string candidateRoot, string repositoryRoot, string sha, string candidateId, string candidateRef,
            string repository, string scriptsRoot)
        {
            var reasons = new List<string>();
            var dir = Dir(candidateRoot);
            var authorityPath = System.IO.Path.Combine(dir, "authority.json");
            var authority = PlainJson.Read(authorityPath);
            if (authority == null) return new PinnedAuthority { Ok = false, Reasons = new[] { "missing-authority" } };
            object F(string name) => AuthorityValue.Get(authority, name);
            var schema = F("schemaVersion");
            if (!AuthorityValue.IsInteger(schema) || Convert.ToInt64(AuthorityValue.Norm(schema), CultureInfo.InvariantCulture) != Schema) reasons.Add("authority-schema");
            if (!AuthorityValue.IsText(F("kind"), "release-authority")) reasons.Add("authority-kind");
            if (!AuthorityValue.IsText(F("profile"), "rc")) reasons.Add("authority-profile");
            if (!AuthorityValue.IsText(F("coordinatorVersion"), CoordinatorVersion)) reasons.Add("authority-coordinator-version");
            var destination = !string.IsNullOrWhiteSpace(repository) && RepositoryShape.IsMatch(repository) ? repository : "";
            if (!AuthorityValue.IsText(F("repository"), destination, true)) reasons.Add("authority-repository");
            var origin = OriginRepository(repositoryRoot);
            if (destination.Length == 0 || !string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase)) reasons.Add("checkout-origin-repository");
            if (!AuthorityValue.IsText(F("sha"), sha) || !IsFullSha(sha)) reasons.Add("authority-sha");
            if (!AuthorityValue.IsText(F("candidateId"), candidateId)) reasons.Add("authority-candidate");
            if (!AuthorityValue.IsText(F("candidateRef"), candidateRef)) reasons.Add("authority-candidate-ref");
            if (string.IsNullOrWhiteSpace(AuthorityValue.Text(F("intentId")))) reasons.Add("authority-intent");
            if (!AuthorityValue.IsGuid(F("releaseCardId"))) reasons.Add("authority-release-card");
            if (!AuthorityValue.IsText(F("policyPath"), PolicyPath)) reasons.Add("authority-policy-path");
            AuthorityPolicy policy = null;
            try
            {
                var blob = ReadPinnedBlob(repositoryRoot, sha);
                var copyPath = System.IO.Path.Combine(dir, "policy.json");
                var copy = File.Exists(copyPath) ? File.ReadAllBytes(copyPath) : new byte[0];
                var pinnedRaw = Sha256(blob.Bytes);
                if (!AuthorityValue.IsText(F("policyBlobId"), blob.BlobId)) reasons.Add("policy-blob-id-mismatch");
                if (!AuthorityValue.IsText(F("policyRawSha256"), pinnedRaw)) reasons.Add("policy-raw-digest-mismatch");
                if (Sha256(copy) != pinnedRaw) reasons.Add("policy-copy-mismatch");
                policy = PolicyFromBytes(blob.Bytes);
                if (!AuthorityValue.IsText(F("policyHash"), policy.PolicyHash)) reasons.Add("policy-hash-mismatch");

                // requiredSuites: a JSON array of distinct known suite names, then exactly the pinned list.
                var recorded = F("requiredSuites");
                var suitesTyped = AuthorityValue.IsStringList(recorded);
                if (suitesTyped)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in AuthorityValue.Elements(recorded))
                    {
                        var id = (string)AuthorityValue.Norm(item);
                        if (!policy.RequiredSuites.Contains(id, StringComparer.Ordinal) || !seen.Add(id)) suitesTyped = false;
                    }
                }
                if (!suitesTyped) { reasons.Add("authority-suites-type"); reasons.Add("authority-suites-mismatch"); }
                else if (string.Join(",", AuthorityValue.Elements(recorded).Select(AuthorityValue.Str)) != string.Join(",", policy.RequiredSuites)) reasons.Add("authority-suites-mismatch");

                // exclusions: a JSON array of objects with string fields and a string-list methods.
                var recordedExclusions = F("exclusions");
                var exclusionsTyped = AuthorityValue.IsArray(recordedExclusions);
                if (exclusionsTyped)
                {
                    foreach (var e in AuthorityValue.Elements(recordedExclusions))
                    {
                        if (!AuthorityValue.IsObject(e)) { exclusionsTyped = false; break; }
                        foreach (var k in new[] { "suite", "class", "reason", "owner" }) if (!(AuthorityValue.Norm(AuthorityValue.Get(e, k)) is string)) exclusionsTyped = false;
                        if (!AuthorityValue.IsStringList(AuthorityValue.Get(e, "methods"))) exclusionsTyped = false;
                    }
                }
                if (!exclusionsTyped) { reasons.Add("authority-exclusions-type"); reasons.Add("authority-exclusions-mismatch"); }
                else
                {
                    var pinnedEx = PlainJson.Canonical(policy.Exclusions.Select(e => (object)e.ToMap()).ToArray());
                    var recordedEx = PlainJson.Canonical(AuthorityValue.Elements(recordedExclusions).Select(e => (object)new OrderedDictionary
                    {
                        { "suite", AuthorityValue.Norm(AuthorityValue.Get(e, "suite")) },
                        { "class", AuthorityValue.Norm(AuthorityValue.Get(e, "class")) },
                        { "methods", AuthorityValue.Elements(AuthorityValue.Get(e, "methods")).Select(AuthorityValue.Norm).ToArray() },
                        { "reason", AuthorityValue.Norm(AuthorityValue.Get(e, "reason")) },
                        { "owner", AuthorityValue.Norm(AuthorityValue.Get(e, "owner")) },
                    }).ToArray());
                    if (pinnedEx != recordedEx) reasons.Add("authority-exclusions-mismatch");
                }
            }
            catch (Exception ex) { reasons.Add(ex.Message); }
            var current = ScriptHashes(scriptsRoot);
            var hashes = F("scriptHashes");
            var hashesTyped = AuthorityValue.IsObject(hashes);
            var names = hashesTyped ? AuthorityValue.Names(hashes) : new string[0];
            foreach (DictionaryEntry entry in current)
            {
                var key = (string)entry.Key;
                var bound = hashesTyped && names.Contains(key, StringComparer.OrdinalIgnoreCase) && AuthorityValue.IsText(AuthorityValue.Get(hashes, key), (string)entry.Value);
                if (!bound) reasons.Add("script-digest-mismatch:" + key);
            }
            return new PinnedAuthority
            {
                Ok = reasons.Count == 0, Reasons = reasons.ToArray(), Authority = AuthorityRecord.From(authority), Policy = policy, Digest = FileSha256(authorityPath),
            };
        }

        /// <summary>
        /// D-15 (Test-ReleaseGateExecutionLedger): the frozen plan and full ledger. Every chunk, every required expanded UID
        /// and every declared roster entry, each joined to the same intent, candidate, SHA, native RunId and authority. A
        /// passing sibling never hides a failed, skipped, missing, unknown, duplicate or stale row. No instant may be later
        /// than nowUtc (the publisher's injectable clock) plus a small skew, and the D-14 prepublication receipt must be
        /// typed, addressed to the release card bound to the authority, and correlated to this run - and the card itself,
        /// read back through the Antiphon API at cardApiBaseUrl, must be at exactly the receipt's revision with this run's
        /// correlation in its body. green: the run's complete-green document (its runId). cardReader reads the card (base URL,
        /// card id); null is the CardReader test seam when set, else ReadReleaseCard over HTTP.
        /// </summary>
        public static LedgerCheck Ledger(string candidateRoot, PinnedAuthority pinned, object green, DateTime nowUtc, string cardApiBaseUrl,
            Func<string, string, CardReadback> cardReader = null)
        {
            var reasons = new List<string>();
            var dir = Dir(candidateRoot);
            var authority = pinned?.Authority?.Document;
            var planPath = System.IO.Path.Combine(dir, "execution-plan.json");
            var ledgerPath = System.IO.Path.Combine(dir, "ledger.json");
            var discoveryPath = System.IO.Path.Combine(dir, "discovery.json");
            var plan = PlainJson.Read(planPath);
            var ledger = PlainJson.Read(ledgerPath);
            var discovery = PlainJson.Read(discoveryPath);
            if (plan == null) reasons.Add("missing-execution-plan");
            if (ledger == null) reasons.Add("missing-ledger");
            if (discovery == null) reasons.Add("missing-discovery");
            if (nowUtc == DateTime.MinValue) reasons.Add("clock-unavailable");
            var summaryRows = new List<OrderedDictionary>();
            if (reasons.Count > 0) return new LedgerCheck { Ok = false, Reasons = reasons.ToArray() };

            object A(string name) => AuthorityValue.Get(authority, name);
            object L(string name) => AuthorityValue.Get(ledger, name);
            object P(string name) => AuthorityValue.Get(plan, name);
            // Expected identities are taken only from real strings; "" matches nothing.
            var intent = AuthorityValue.Text(A("intentId"));
            var candidateId = AuthorityValue.Text(A("candidateId"));
            var candidateRef = AuthorityValue.Text(A("candidateRef"));
            var sha = AuthorityValue.Text(A("sha"));
            var runId = AuthorityValue.Text(AuthorityValue.Get(green, "runId"));
            var latest = AuthorityValue.Utc(nowUtc).AddSeconds(ClockSkewSeconds);

            // Plan binding.
            if (!AuthorityValue.IsText(P("authorityDigest"), pinned.Digest)) reasons.Add("plan-authority-digest");
            if (!AuthorityValue.IsText(P("discoveryDigest"), FileSha256(discoveryPath))) reasons.Add("plan-discovery-digest");
            if (!AuthorityValue.IsText(P("intentId"), intent)) reasons.Add("plan-intent");
            if (!AuthorityValue.IsText(P("candidateId"), candidateId)) reasons.Add("plan-candidate");
            if (!AuthorityValue.IsText(P("sha"), sha)) reasons.Add("plan-sha");

            // Ledger identity and whole-run markers (real booleans only).
            if (!AuthorityValue.IsText(L("intentId"), intent)) reasons.Add("ledger-intent");
            if (!AuthorityValue.IsText(L("candidateId"), candidateId)) reasons.Add("ledger-candidate");
            if (!AuthorityValue.IsText(L("candidateRef"), candidateRef)) reasons.Add("ledger-candidate-ref");
            if (!AuthorityValue.IsText(L("sha"), sha) || !IsFullSha(sha)) reasons.Add("ledger-sha");
            if (string.IsNullOrWhiteSpace(runId) || !AuthorityValue.IsText(L("runId"), runId)) reasons.Add("ledger-run");
            if (!AuthorityValue.IsText(L("authorityDigest"), pinned.Digest)) reasons.Add("ledger-authority-digest");
            if (!AuthorityValue.IsText(L("executionPlanDigest"), FileSha256(planPath))) reasons.Add("ledger-plan-digest");
            if (!AuthorityValue.IsTrue(L("teardownSucceeded"))) reasons.Add("teardown-not-succeeded");
            if (!AuthorityValue.IsBool(L("seamed")) || AuthorityValue.IsTrue(L("seamed"))) reasons.Add("ledger-seamed");
            if (!AuthorityValue.IsBool(L("noReport")) || AuthorityValue.IsTrue(L("noReport"))) reasons.Add("ledger-no-report");
            if (!AuthorityValue.IsBool(L("diagnostic")) || AuthorityValue.IsTrue(L("diagnostic"))) reasons.Add("ledger-diagnostic");
            if (!AuthorityValue.IsText(L("selection"), "full")) reasons.Add("ledger-selection");

            // Freshness and ordering: authority <= run start <= every chunk start < end <= run end, and nothing dated after
            // the publisher's clock (evidence from the future).
            var authorityAt = AuthorityValue.Instant(A("createdAt"));
            var runStart = AuthorityValue.Instant(L("startedAt"));
            var runEnd = AuthorityValue.Instant(L("completedAt"));
            if (authorityAt == null || runStart == null || runEnd == null || runStart < authorityAt || runEnd <= runStart) reasons.Add("ledger-timestamps");
            bool Future(DateTime? t) => t != null && t > latest;
            bool FutureWindow(object s, object e) => Future(AuthorityValue.Instant(s)) || Future(AuthorityValue.Instant(e));
            if (Future(authorityAt)) reasons.Add("evidence-future:authority");
            if (Future(runStart) || Future(runEnd)) reasons.Add("evidence-future:ledger");
            bool Window(object s, object e)
            {
                var a = AuthorityValue.Instant(s);
                var b = AuthorityValue.Instant(e);
                if (a == null || b == null || runStart == null || runEnd == null) return false;
                return a >= runStart && b > a && b <= runEnd;
            }

            // D-14 / G-306: the prepublication receipt is the release card's readback of the projected test result. Only
            // that kind, from that card, for this run, counts; a final/publication receipt or any boolean acceptance flag
            // confers nothing.
            var receipt = L("prepublicationReceipt");
            if (receipt == null) reasons.Add("receipt-missing");
            else if (!AuthorityValue.IsObject(receipt)) reasons.Add("receipt-type");
            else
            {
                if (!AuthorityValue.IsText(AuthorityValue.Get(receipt, "kind"), ReceiptKind)) reasons.Add("receipt-kind");
                var recipient = AuthorityValue.Get(receipt, "recipient");
                var recipientOk = AuthorityValue.IsObject(recipient)
                    && AuthorityValue.IsText(AuthorityValue.Get(recipient, "kind"), RecipientKind)
                    && AuthorityValue.IsGuid(AuthorityValue.Get(recipient, "cardId")) && AuthorityValue.IsGuid(A("releaseCardId"))
                    && Guid.Parse(AuthorityValue.Text(AuthorityValue.Get(recipient, "cardId"))) == Guid.Parse(AuthorityValue.Text(A("releaseCardId")))
                    && AuthorityValue.IsInteger(AuthorityValue.Get(recipient, "revision")) && AuthorityValue.Long(AuthorityValue.Get(recipient, "revision")) >= 1;
                if (!recipientOk) reasons.Add("receipt-recipient");
                var correlation = AuthorityValue.Get(receipt, "correlation");
                var correlated = AuthorityValue.IsObject(correlation)
                    && AuthorityValue.IsText(AuthorityValue.Get(correlation, "intentId"), intent)
                    && AuthorityValue.IsText(AuthorityValue.Get(correlation, "candidateId"), candidateId)
                    && AuthorityValue.IsText(AuthorityValue.Get(correlation, "sha"), sha)
                    && AuthorityValue.IsText(AuthorityValue.Get(correlation, "runId"), runId);
                if (!correlated) reasons.Add("receipt-correlation");
                var acceptedAt = AuthorityValue.Instant(AuthorityValue.Get(receipt, "acceptedAt"));
                if (acceptedAt == null || runEnd == null || acceptedAt < runEnd) reasons.Add("receipt-timestamps");
                if (Future(acceptedAt)) reasons.Add("evidence-future:receipt");

                // Review bf928242: a receipt is only as good as the card it names. Read the card bound to the authority back
                // (read-only) and require it to be at exactly the receipt's revision - a card that moved on, or a receipt
                // naming any other positive revision, refuses - with this run's correlation in its body. An unavailable
                // readback refuses: publication fails closed.
                var boundCard = AuthorityValue.Text(A("releaseCardId"));
                var reader = cardReader ?? CardReader ?? ReadReleaseCard;
                var readback = reader(cardApiBaseUrl, boundCard) ?? new CardReadback { Ok = false };
                if (!readback.Ok) reasons.Add("release-card-readback-unavailable");
                else
                {
                    var card = readback.Card;
                    var cardIdValue = AuthorityValue.Get(card, "id");
                    if (!(AuthorityValue.IsGuid(cardIdValue) && Guid.Parse(AuthorityValue.Text(cardIdValue)) == Guid.Parse(boundCard))) reasons.Add("release-card-readback-identity");
                    var sameRevision = AuthorityValue.IsObject(recipient) && AuthorityValue.IsInteger(AuthorityValue.Get(recipient, "revision"))
                        && AuthorityValue.IsInteger(AuthorityValue.Get(card, "revisionCount"))
                        && AuthorityValue.Long(AuthorityValue.Get(card, "revisionCount")) == AuthorityValue.Long(AuthorityValue.Get(recipient, "revision"));
                    if (!sameRevision) reasons.Add("release-card-readback-revision");
                    var expected = new OrderedDictionary { { "intentId", intent }, { "candidateId", candidateId }, { "sha", sha }, { "runId", runId } };
                    if (!CardCorrelation(AuthorityValue.Get(card, "description"), expected)) reasons.Add("release-card-readback-correlation");
                }
            }

            // Prerequisites: every declared build/prerequisite row succeeded.
            foreach (var preId in new[] { "build", "client-bundle", "script-census" })
            {
                var rows = AuthorityValue.Elements(L("prerequisites")).Where(r => AuthorityValue.IsText(AuthorityValue.Get(r, "id"), preId)).ToList();
                if (rows.Count != 1) { reasons.Add("prerequisite-missing:" + preId); continue; }
                var exit = AuthorityValue.Get(rows[0], "exitCode");
                if (!AuthorityValue.IsInteger(exit)) { reasons.Add("prerequisite-failed:" + preId); continue; }
                if (AuthorityValue.Long(exit) != 0 || !AuthorityValue.IsTrue(AuthorityValue.Get(rows[0], "succeeded"))) reasons.Add("prerequisite-failed:" + preId);
            }

            // Suite set exactly the pinned one, in plan and in ledger.
            var required = pinned.Policy.RequiredSuites;
            var planSuites = AuthorityValue.Elements(P("suites")).Select(s => AuthorityValue.Text(AuthorityValue.Get(s, "id"))).ToList();
            foreach (var id in required) if (!planSuites.Contains(id, StringComparer.Ordinal)) reasons.Add("required-suite-missing:" + id);
            foreach (var id in planSuites) if (!required.Contains(id, StringComparer.Ordinal)) reasons.Add("plan-suite-unexpected:" + id);

            // Discovery-derived required UIDs, recomputed from the pinned exclusions.
            var allChunkIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var suite in AuthorityValue.Elements(P("suites")))
            {
                var id = AuthorityValue.Text(AuthorityValue.Get(suite, "id"));
                if (!required.Contains(id, StringComparer.Ordinal)) continue;
                if (RosterSuites.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    if (!AuthorityValue.IsStringList(AuthorityValue.Get(suite, "entries"))) { reasons.Add("roster-empty:" + id); continue; }
                    var declared = AuthorityValue.Elements(AuthorityValue.Get(suite, "entries")).Select(AuthorityValue.Str).ToList();
                    if (declared.Count == 0) { reasons.Add("roster-empty:" + id); continue; }
                    var rosterRows = AuthorityValue.Elements(L("rosters")).Where(r => AuthorityValue.IsText(AuthorityValue.Get(r, "suite"), id)).ToList();
                    if (rosterRows.Count != 1) { reasons.Add("roster-missing:" + id); continue; }
                    var r = rosterRows[0];
                    var rosterJoined = AuthorityValue.IsText(AuthorityValue.Get(r, "intentId"), intent) && AuthorityValue.IsText(AuthorityValue.Get(r, "candidateId"), candidateId)
                        && AuthorityValue.IsText(AuthorityValue.Get(r, "sha"), sha) && AuthorityValue.IsText(AuthorityValue.Get(r, "runId"), runId);
                    if (!rosterJoined) reasons.Add("roster-identity:" + id);
                    var rosterExit = AuthorityValue.Get(r, "exitCode");
                    if (!AuthorityValue.IsInteger(rosterExit) || AuthorityValue.Long(rosterExit) != 0) reasons.Add("roster-exit:" + id);
                    if (!Window(AuthorityValue.Get(r, "startedAt"), AuthorityValue.Get(r, "completedAt"))) reasons.Add("roster-timestamps:" + id);
                    if (FutureWindow(AuthorityValue.Get(r, "startedAt"), AuthorityValue.Get(r, "completedAt"))) reasons.Add("evidence-future:roster:" + id);
                    var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in AuthorityValue.Elements(AuthorityValue.Get(r, "entries")))
                    {
                        if (!(AuthorityValue.Norm(AuthorityValue.Get(e, "id")) is string eid) || !(AuthorityValue.Norm(AuthorityValue.Get(e, "outcome")) is string outcome))
                        {
                            reasons.Add("roster-entry-type:" + id);
                            continue;
                        }
                        if (seen.ContainsKey(eid)) { reasons.Add("roster-duplicate:" + id + ":" + eid); continue; }
                        seen[eid] = outcome;
                    }
                    foreach (var eid in declared)
                    {
                        if (!seen.TryGetValue(eid, out var outcome)) reasons.Add("roster-entry-missing:" + id + ":" + eid);
                        else if (outcome != "passed") reasons.Add("roster-entry-not-passed:" + id + ":" + eid);
                    }
                    foreach (var eid in seen.Keys) if (!declared.Contains(eid, StringComparer.OrdinalIgnoreCase)) reasons.Add("roster-entry-unknown:" + id + ":" + eid);
                    summaryRows.Add(new OrderedDictionary
                    {
                        { "id", id }, { "kind", "roster" }, { "required", declared.Count }, { "executed", seen.Count }, { "passed", seen.Values.Count(v => v == "passed") },
                    });
                    continue;
                }

                var suitesDoc = AuthorityValue.Get(discovery, "suites");
                var nodes = AuthorityValue.IsObject(suitesDoc) && AuthorityValue.Names(suitesDoc).Contains(id, StringComparer.OrdinalIgnoreCase)
                    ? AuthorityValue.Elements(AuthorityValue.Get(suitesDoc, id)) : new object[0];
                var requiredUids = RequiredUids(nodes, A("exclusions"), id);
                if (requiredUids.Length == 0) { reasons.Add("required-uids-empty:" + id); continue; }
                var member = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var planChunks = AuthorityValue.Elements(AuthorityValue.Get(suite, "chunks"));
                foreach (var c in planChunks)
                {
                    if (!(AuthorityValue.Norm(AuthorityValue.Get(c, "id")) is string chunkId) || !AuthorityValue.IsStringList(AuthorityValue.Get(c, "uids")))
                    {
                        reasons.Add("plan-chunk-type:" + id);
                        continue;
                    }
                    allChunkIds[chunkId] = id;
                    foreach (var u in AuthorityValue.Elements(AuthorityValue.Get(c, "uids")))
                    {
                        var key = AuthorityValue.Str(u);
                        if (member.ContainsKey(key)) reasons.Add("plan-uid-overlap:" + key); else member[key] = chunkId;
                    }
                }
                foreach (var u in requiredUids) if (!member.ContainsKey(u)) reasons.Add("plan-uid-missing:" + u);
                foreach (var u in member.Keys) if (!requiredUids.Contains(u, StringComparer.OrdinalIgnoreCase)) reasons.Add("plan-uid-unexpected:" + u);

                var terminal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var executed = 0;
                var passed = 0;
                foreach (var c in planChunks)
                {
                    if (!(AuthorityValue.Norm(AuthorityValue.Get(c, "id")) is string cid)) continue;
                    var rows = AuthorityValue.Elements(L("chunks")).Where(r => AuthorityValue.IsText(AuthorityValue.Get(r, "chunk"), cid)).ToList();
                    if (rows.Count == 0) { reasons.Add("chunk-missing:" + cid); continue; }
                    if (rows.Count > 1) { reasons.Add("chunk-duplicate:" + cid); continue; }
                    var row = rows[0];
                    object R(string name) => AuthorityValue.Get(row, name);
                    if (!AuthorityValue.IsText(R("suite"), id)) reasons.Add("chunk-suite:" + cid);
                    if (!AuthorityValue.IsText(R("intentId"), intent)) reasons.Add("chunk-intent:" + cid);
                    if (!AuthorityValue.IsText(R("candidateId"), candidateId)) reasons.Add("chunk-candidate:" + cid);
                    if (!AuthorityValue.IsText(R("sha"), sha)) reasons.Add("chunk-sha:" + cid);
                    if (!AuthorityValue.IsText(R("runId"), runId)) reasons.Add("chunk-run:" + cid);
                    if (!Window(R("startedAt"), R("completedAt"))) reasons.Add("chunk-timestamps:" + cid);
                    if (FutureWindow(R("startedAt"), R("completedAt"))) reasons.Add("evidence-future:chunk:" + cid);
                    if (!AuthorityValue.IsInteger(R("exitCode")) || AuthorityValue.Long(R("exitCode")) != 0) reasons.Add("chunk-exit:" + cid);
                    var trx = ResolveEvidencePath(candidateRoot, AuthorityValue.Text(R("trx")));
                    if (string.IsNullOrWhiteSpace(trx)) { reasons.Add("chunk-evidence-escape:" + cid); continue; }
                    if (!File.Exists(trx)) { reasons.Add("chunk-evidence-missing:" + cid); continue; }
                    if (!AuthorityValue.IsText(R("trxSha256"), FileSha256(trx))) reasons.Add("chunk-evidence-digest:" + cid);
                    TrxIdentity[] results;
                    try { results = NightlyTrx.ReadIdentities(trx); }
                    catch (NightlyRefusal) { reasons.Add("chunk-trx-unreadable:" + cid); continue; }
                    int cx = 0, cp = 0, cf = 0, cs = 0;
                    foreach (var t in results)
                    {
                        var uid = t.TestId;
                        cx++;
                        if (t.Outcome == "Passed") cp++; else if (t.Outcome == "Failed") cf++; else cs++;
                        if (!member.TryGetValue(uid, out var owner)) { reasons.Add("uid-unknown:" + uid); continue; }
                        if (owner != cid) { reasons.Add("uid-wrong-chunk:" + uid); continue; }
                        if (terminal.ContainsKey(uid)) { reasons.Add("uid-duplicate:" + uid); continue; }
                        terminal[uid] = t.Outcome;
                    }
                    foreach (var (name, count) in new[] { ("executed", cx), ("passed", cp), ("failed", cf), ("skipped", cs) })
                    {
                        var v = R(name);
                        if (!AuthorityValue.IsInteger(v) || AuthorityValue.Long(v) != count) reasons.Add("chunk-count-" + name + ":" + cid);
                    }
                    if (cx == 0) reasons.Add("chunk-zero-executed:" + cid);
                    executed += cx;
                    passed += cp;
                }
                foreach (var u in requiredUids)
                {
                    if (!terminal.TryGetValue(u, out var outcome)) reasons.Add("uid-missing:" + u);
                    else if (outcome != "Passed") reasons.Add("uid-not-passed:" + u);
                }
                summaryRows.Add(new OrderedDictionary
                {
                    { "id", id }, { "kind", "native" }, { "chunks", planChunks.Length }, { "required", requiredUids.Length }, { "executed", executed }, { "passed", passed },
                });
            }
            foreach (var row in AuthorityValue.Elements(L("chunks")))
            {
                var rowChunk = AuthorityValue.Text(AuthorityValue.Get(row, "chunk"));
                if (string.IsNullOrEmpty(rowChunk) || !allChunkIds.ContainsKey(rowChunk)) reasons.Add("chunk-unexpected:" + AuthorityValue.Str(AuthorityValue.Get(row, "chunk")));
            }
            return new LedgerCheck { Ok = reasons.Count == 0, Reasons = reasons.ToArray(), Suites = summaryRows.ToArray() };
        }

        /// <summary>
        /// Freezes the digest over the pinned authority, plan, discovery, ledger and every chunk's evidence, plus the
        /// sanitized summary digest (the summary is an output). noWrite (the publisher's -WhatIf) computes and compares the
        /// digest but writes nothing.
        /// </summary>
        public static PublicationAuthority Publication(string candidateRoot, PinnedAuthority pinned, string summaryDigest, bool noWrite)
        {
            var dir = Dir(candidateRoot);
            var ledger = PlainJson.Read(System.IO.Path.Combine(dir, "ledger.json"));
            var evidence = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            foreach (var row in AuthorityValue.Elements(AuthorityValue.Get(ledger, "chunks")))
                evidence[AuthorityValue.Str(AuthorityValue.Get(row, "chunk"))] = AuthorityValue.Str(AuthorityValue.Get(row, "trxSha256"));
            var authority = pinned.Authority;
            var doc = new OrderedDictionary
            {
                { "schemaVersion", 1 },
                { "candidateId", authority.CandidateId },
                { "intentId", authority.IntentId },
                { "sha", authority.Sha },
                { "runId", AuthorityValue.Str(AuthorityValue.Get(ledger, "runId")) },
                { "policyBlobId", authority.PolicyBlobId },
                { "policyHash", authority.PolicyHash },
                { "authorityDigest", pinned.Digest },
                { "executionPlanDigest", FileDigest(System.IO.Path.Combine(dir, "execution-plan.json")) },
                { "discoveryDigest", FileDigest(System.IO.Path.Combine(dir, "discovery.json")) },
                { "ledgerDigest", FileDigest(System.IO.Path.Combine(dir, "ledger.json")) },
                { "evidence", evidence },
                { "summaryDigest", summaryDigest ?? "" },
            };
            var digest = Sha256Text(PlainJson.Canonical(doc));
            var path = System.IO.Path.Combine(dir, "publication-authority.json");
            var existing = PlainJson.Read(path);
            if (existing != null && AuthorityValue.Str(AuthorityValue.Get(existing, "digest")) != digest)
                return new PublicationAuthority { Ok = false, Reason = "publication-authority-changed", Digest = digest, Path = path };
            if (existing == null && !noWrite) PlainJson.WriteAtomic(path, new OrderedDictionary { { "digest", digest }, { "inputs", doc } });
            return new PublicationAuthority { Ok = true, Reason = "", Digest = digest, Path = path };
        }

        /// <summary>
        /// The publisher's single entry point (Test-ReleaseGateAuthority): pinned policy + ledger. repository is the
        /// publication destination; nowUtc the publisher's clock; cardApiBaseUrl the Antiphon API the bound release card is
        /// read back from (through cardReader, see Ledger). green and candidate: the run's complete-green document and the
        /// candidate journal.
        /// </summary>
        public static AuthorityCheck Check(string candidateRoot, string repositoryRoot, object green, object candidate, string repository, DateTime nowUtc,
            string cardApiBaseUrl, string scriptsRoot, Func<string, string, CardReadback> cardReader = null)
        {
            var pinned = Pinned(candidateRoot, repositoryRoot, AuthorityValue.Str(AuthorityValue.Get(candidate, "sha")),
                AuthorityValue.Str(AuthorityValue.Get(candidate, "candidateId")), AuthorityValue.Str(AuthorityValue.Get(candidate, "ref")), repository, scriptsRoot);
            if (pinned.Authority == null) return new AuthorityCheck { Ok = false, Reasons = pinned.Reasons, Pinned = pinned };
            var reasons = new List<string>(pinned.Reasons);
            if (!AuthorityValue.IsText(AuthorityValue.Get(green, "policyHash"), AuthorityValue.Text(AuthorityValue.Get(pinned.Authority.Document, "policyHash"))))
                reasons.Add("green-policy-hash-mismatch");
            if (pinned.Policy == null) return new AuthorityCheck { Ok = false, Reasons = reasons.ToArray(), Pinned = pinned };
            var ledger = Ledger(candidateRoot, pinned, green, nowUtc, cardApiBaseUrl, cardReader);
            reasons.AddRange(ledger.Reasons);
            return new AuthorityCheck { Ok = reasons.Count == 0, Reasons = reasons.ToArray(), Pinned = pinned, Suites = ledger.Suites };
        }
    }

    /// <summary>
    /// Values as the authority checks read them: JSON elements, dictionaries and PowerShell objects. JSON objects become
    /// the element itself; numbers long (integers) or double; arrays object[].
    /// </summary>
    internal static class AuthorityValue
    {
        internal static object FromJson(JsonElement v)
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString();
                case JsonValueKind.Number:
                    var raw = v.GetRawText();
                    if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && v.TryGetInt64(out var whole)) return whole;
                    return v.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Array: return v.EnumerateArray().Select(FromJson).ToArray();
                case JsonValueKind.Object: return v;
                default: return null;
            }
        }

        private static bool Plain(object v) => v == null || v is string || v is ValueType || v is Array || v is IDictionary || v is JsonElement || v is IFieldSource;

        /// <summary>A wrapped PowerShell value's base (a JSON object stays wrapped); a plain record as its field map.</summary>
        internal static object Norm(object v)
        {
            if (v is IFieldSource record) return record.Fields();
            return Plain(v) ? (v is JsonElement e && e.ValueKind != JsonValueKind.Object ? FromJson(e) : v) : PsFields.Base(v);
        }

        /// <summary>The named field (case-insensitive) of an object value; null when absent or not an object.</summary>
        internal static object Get(object o, string name)
        {
            o = Norm(o);
            if (o == null) return null;
            if (o is JsonElement e)
            {
                if (e.ValueKind != JsonValueKind.Object) return null;
                foreach (var p in e.EnumerateObject())
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return FromJson(p.Value);
                return null;
            }
            if (o is IDictionary d)
            {
                foreach (DictionaryEntry kv in d)
                    if (string.Equals(Convert.ToString(kv.Key, CultureInfo.InvariantCulture), name, StringComparison.OrdinalIgnoreCase)) return Norm(kv.Value);
                return null;
            }
            if (o is string || o is ValueType || o is Array) return null;
            return PsFields.Get(o, name);
        }

        /// <summary>The field names of an object value.</summary>
        internal static string[] Names(object o)
        {
            o = Norm(o);
            if (o is JsonElement e) return e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().Select(p => p.Name).ToArray() : new string[0];
            if (o is IDictionary d) return d.Keys.Cast<object>().Select(k => Convert.ToString(k, CultureInfo.InvariantCulture)).ToArray();
            if (o == null || o is string || o is ValueType || o is Array) return new string[0];
            return PsFields.Names(o);
        }

        /// <summary>@($value): an array's elements, nothing for null, otherwise the value itself.</summary>
        internal static object[] Elements(object v)
        {
            v = Norm(v);
            if (v == null) return new object[0];
            if (v is string) return new[] { v };
            if (v is Array a) return a.Cast<object>().Select(Norm).ToArray();
            if (v is IEnumerable list && !(v is IDictionary) && !(v is JsonElement)) return list.Cast<object>().Select(Norm).ToArray();
            return new[] { v };
        }

        /// <summary>[string]$value.</summary>
        internal static string Str(object v)
        {
            v = Norm(v);
            switch (v)
            {
                case null: return "";
                case string s: return s;
                case bool b: return b ? "True" : "False";
                case Array a: return string.Join(" ", a.Cast<object>().Select(Str));
                case JsonElement e: return e.GetRawText();
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return v.ToString() ?? "";
            }
        }

        /// <summary>The value when it is a real string, otherwise "" (which no comparison accepts).</summary>
        internal static string Text(object v) => Norm(v) as string ?? "";

        /// <summary>A real string equal to a non-empty expected value; never a conversion of an array or number.</summary>
        internal static bool IsText(object v, string expected, bool ignoreCase = false) =>
            Norm(v) is string s && !string.IsNullOrEmpty(expected)
            && string.Equals(s, expected, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        internal static bool IsInteger(object v)
        {
            v = Norm(v);
            return v is long || v is int;
        }

        internal static long Long(object v) => Convert.ToInt64(Norm(v), CultureInfo.InvariantCulture);

        internal static bool IsBool(object v) => Norm(v) is bool;

        /// <summary>A real true boolean (a string "true" is not one).</summary>
        internal static bool IsTrue(object v) => Norm(v) is bool b && b;

        /// <summary>A JSON object (ConvertFrom-Json's PSCustomObject); never an array, string, boolean or dictionary.</summary>
        internal static bool IsObject(object v)
        {
            v = Norm(v);
            if (v is JsonElement e) return e.ValueKind == JsonValueKind.Object;
            if (Plain(v)) return false;
            return PsFields.IsCustomObject(v);
        }

        internal static bool IsArray(object v) => Norm(v) is Array;

        /// <summary>A JSON array whose every element is a string. A joined string is not a list.</summary>
        internal static bool IsStringList(object v) => Norm(v) is Array a && a.Cast<object>().All(i => Norm(i) is string);

        internal static bool IsGuid(object v) => Norm(v) is string s && Guid.TryParseExact(s, "D", out _);

        /// <summary>An unspecified kind is UTC (as AssumeUniversal parsing is), never local time.</summary>
        internal static DateTime Utc(DateTime value) =>
            value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

        /// <summary>A JSON string instant (or a DateTime); an array, number or boolean is never an instant.</summary>
        internal static DateTime? Instant(object v)
        {
            v = Norm(v);
            if (v is DateTime d) return Utc(d);
            if (!(v is string s)) return null;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)) return parsed;
            return null;
        }
    }

    /// <summary>JSON for plain values (dictionaries, arrays, strings, numbers, booleans and IJsonWritable records).</summary>
    public static class PlainJson
    {
        private static readonly JsonWriterOptions Indented = new JsonWriterOptions
        {
            Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static void Write(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case null: writer.WriteNullValue(); return;
                case IJsonWritable record: record.WriteJson(writer); return;
                case string s: writer.WriteStringValue(s); return;
                case bool b: writer.WriteBooleanValue(b); return;
                case int i: writer.WriteNumberValue(i); return;
                case long l: writer.WriteNumberValue(l); return;
                case double d: writer.WriteNumberValue(d); return;
                case JsonElement e: e.WriteTo(writer); return;
                case IDictionary map:
                    writer.WriteStartObject();
                    foreach (DictionaryEntry kv in map)
                    {
                        writer.WritePropertyName(Convert.ToString(kv.Key, CultureInfo.InvariantCulture) ?? "");
                        Write(writer, kv.Value);
                    }
                    writer.WriteEndObject();
                    return;
                case IEnumerable items:
                    writer.WriteStartArray();
                    foreach (var item in items) Write(writer, item);
                    writer.WriteEndArray();
                    return;
                default: writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture)); return;
            }
        }

        /// <summary>Writes the value to a temporary file beside path and renames it over path.</summary>
        public static void WriteAtomic(string path, object value)
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            var temp = System.IO.Path.Combine(dir, "." + System.IO.Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new Utf8JsonWriter(stream, Indented))
                {
                    Write(writer, value);
                }
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        /// <summary>One JSON document (comments and trailing commas allowed), or null when it is not one.</summary>
        public static object Parse(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse((text ?? "").TrimStart((char)0xFEFF), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 256,
                });
                if (DuplicateKeys(doc.RootElement)) return null;
                return AuthorityValue.FromJson(doc.RootElement.Clone());
            }
            catch (JsonException) { return null; }
        }

        /// <summary>The JSON file at path, or null when it is missing or not one JSON document.</summary>
        public static object Read(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return Parse(File.ReadAllText(path)); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // ConvertFrom-Json refuses an object whose names differ only in case.
        private static bool DuplicateKeys(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in e.EnumerateObject())
                {
                    if (!names.Add(p.Name)) return true;
                    if (DuplicateKeys(p.Value)) return true;
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray()) if (DuplicateKeys(item)) return true;
            }
            return false;
        }

        /// <summary>
        /// ConvertTo-NightlyCanonicalJson for plain values: keys sorted as Sort-Object sorts them (current culture, ignoring
        /// case; empty keys dropped), only \ " CR LF TAB escaped, numbers in their invariant form.
        /// </summary>
        public static string Canonical(object value)
        {
            var sb = new StringBuilder();
            CanonicalWrite(sb, value);
            return sb.ToString();
        }

        private static void CanonicalWrite(StringBuilder sb, object node)
        {
            node = AuthorityValue.Norm(node);
            switch (node)
            {
                case null: sb.Append("null"); return;
                case string s:
                    sb.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")).Append('"');
                    return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int _:
                case long _:
                case double _:
                case decimal _:
                    sb.Append(((IFormattable)node).ToString(null, CultureInfo.InvariantCulture));
                    return;
                case JsonElement e when e.ValueKind == JsonValueKind.Object:
                    var map = new OrderedDictionary();
                    foreach (var p in e.EnumerateObject()) if (!map.Contains(p.Name)) map.Add(p.Name, AuthorityValue.FromJson(p.Value));
                    CanonicalWrite(sb, map);
                    return;
                case IDictionary dict:
                    var culture = CultureInfo.CurrentCulture;
                    var pairs = dict.Cast<DictionaryEntry>().Select(kv => (Key: Convert.ToString(kv.Key, CultureInfo.InvariantCulture) ?? "", kv.Value))
                        .Where(p => p.Key.Length > 0)
                        .OrderBy(p => p.Key, Comparer<string>.Create((x, y) => string.Compare(x, y, culture, CompareOptions.IgnoreCase)))
                        .ToList();
                    sb.Append('{');
                    for (var i = 0; i < pairs.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('"').Append(pairs[i].Key).Append("\":");
                        CanonicalWrite(sb, pairs[i].Value);
                    }
                    sb.Append('}');
                    return;
                case IEnumerable items:
                    sb.Append('[');
                    var first = true;
                    foreach (var item in items)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        CanonicalWrite(sb, item);
                    }
                    sb.Append(']');
                    return;
                case IJsonWritable record:
                    CanonicalWrite(sb, Parse(CompactJson.Text(record)));
                    return;
                default:
                    sb.Append('"').Append(Convert.ToString(node, CultureInfo.InvariantCulture)).Append('"');
                    return;
            }
        }
    }
}
