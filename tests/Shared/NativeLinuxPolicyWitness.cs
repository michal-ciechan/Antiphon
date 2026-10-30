using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.TestSupport;

public sealed record InspectionRequest(
    Assembly Assembly,
    string SuiteId,
    string PolicyJson,
    string BaselineJson,
    bool CompareBaseline = true,
    bool CheckAttributePin = true);

public sealed record WitnessDiagnostic(
    string Code,
    string Suite,
    string Row,
    string Concrete,
    string Declaring,
    string Signature,
    string Detail)
{
    public override string ToString() =>
        Code
        + " suite=" + Suite
        + " row=" + Row
        + " concrete=" + Concrete
        + " declaring=" + Declaring
        + " signature=" + Signature
        + (Detail.Length == 0 ? "" : " " + Detail);
}

public sealed record InspectionReport(IReadOnlyList<WitnessDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.Count == 0;

    public string Text => string.Join('\n', Diagnostics);
}

public static class NativeLinuxPolicyWitness
{
    public const string AttributeSha256 = "31ca5740f4127fe7b3b0fc18639e75262cdb3ae49c3bc545e62a130b99d68ac4";
    public const string LegacyExclusionDigest = "1cb55254e3b430df51de4990129af68f145e38c0711eddc3dc7fc6307c6ac685";
    public const string LinuxReasonPrefix = "Linux-only by construction:";
    public const string AttributeResourceName = "Antiphon.TestSupport.NativeLinuxOnlyAttribute.cs";
    public const string BaselineResourceName = "Antiphon.TestSupport.NativeLinuxWindowsBaseline.json";

    public static readonly string[] RequiredSuiteIds =
    [
        "antiphon",
        "session-runner",
        "pty-host",
        "agents-pty",
        "messaging",
        "client",
        "scripts",
        "e2e",
        "markdown-pdf",
        "observability",
    ];

    // Frozen row keys. A reason edit cannot move one of these into the legacy digest.
    public static readonly string[] WitnessedRowKeys =
    [
        "antiphon\nPhoneHomeMirrorJoinTests\n",
        "antiphon\nUnixDelegateLaunchArgvTests\n",
        "antiphon\nCheckpointBoundedReaderTests\n",
        "antiphon\nCheckpointSourceGitBoundsTests\n",
        "antiphon\nCheckpointVerifierBoundsTests\n",
        "antiphon\nCheckpointFixtureProcessTests\n",
        "antiphon\nDetachedLauncherTests\n",
        "antiphon\nSafeDeleteProcessBoundaryTests\n",
        "antiphon\nGrokSha256PinTests\n",
        "antiphon\nGithubCredentialHelperTests\n",
        "antiphon\nGitMinimumVersionGuardTests\n",
        "antiphon\nRetiredTempContainerHostTests\n",
        "antiphon\nHostJqPrerequisiteScriptTests\n",
        "antiphon\nScriptHarnessLinuxOwnershipTests\n",
        "antiphon\nRefreshGithubTokenRemoteCommandTests\n",
        "antiphon\nNightlyProbeSupervisorTests\n",
        "antiphon\nRetiredTempContainerDockerTests\n",
        "antiphon\nRollingVolumeRecycleDockerTests\n",
        "antiphon\nRollingVolumeRecycleCensusChurnTests\n",
        "antiphon\nRollingProductionMountTests\n",
        "antiphon\nCheckpointRecoveryLinuxTests\n",
        "antiphon\nCheckpointFixtureRoutingTests\n",
        "antiphon\nCheckpointInheritedFileTests\n",
        "pty-host\nLinuxCgroupCustodyTests\n",
        "agents-pty\nUnixPtyArgvTests\n",
        "antiphon\nPhoneHomeCompletedCardTransportTests\nC1032_TransportQueueRecipientReplay",
        "antiphon\nLandGraphEvaluationTests\nC1483_S1_UnreadableLinkComponentDenies",
        "antiphon\nCheckpointConfinementLedgerTests\nunchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted",
        "antiphon\nCheckpointReceiptSafetyTests\nlinux_fifo_receipts_refuse_within_four_seconds",
        "antiphon\nOutputCleanupTests\nexternal_build_payload_is_adopted_at_seal_and_removed_on_green\nfile_added_after_seal_is_refused_and_kept\nfile_modified_after_seal_is_refused_and_kept\nsymlink_in_a_build_output_is_refused_before_and_after_seal\ntracked_file_in_an_adopted_build_output_is_refused_and_kept\nadopted_build_output_over_the_entry_cap_is_unsafe_tree\nhard_linked_build_file_is_not_adopted_and_outside_bytes_stay\nindependent_writer_after_build_exit_is_not_adopted_and_cleanup_keeps_it\nseal_without_driver_observed_build_exit_adopts_nothing\nhanging_git_is_refused_and_killed_within_the_bound",
        "antiphon\nAnchoredPathDeleteWitnessTests\nlinux_colon_in_a_file_name_stays_deletable\nhard_link_sibling_survives_anchored_delete\nancestor_symlink_to_a_different_inode_is_refused",
        "antiphon\nPlanCoverageHandleTests\nhardlinked_selected_file_is_refused\nin_repo_fifo_project_is_refused_within_15_seconds",
        "antiphon\nCodexRootExecContractTests\nEntrypoint_heal_chowns_the_tmp_directory_once\nEntrypoint_heal_never_names_a_path_outside_tmp",
        "antiphon\nFileUseCaptureTests\nC1539_linux_holder\nC1539_absent_inputs\nC1539_budget_and_caps\nC1539_redaction_before_sinks\nC1539_persistence_fail_safe\nC1539_generation_and_ownership",
        "antiphon\nJqRunnerImageContractTests\nVersion_row_accepts_only_exact_successful_pin_without_stderr\nC1054_Jq_row_rejects_home_shadow\nC1054_Jq_row_accepts_canonical_file_and_alias\nC1054_Jq_row_rejects_canonical_leaf_symlink\nC1054_Jq_row_records_found_and_resolved_paths",
        "antiphon\nRollingVolumeRecycleScriptTests\nC1008_Option_manifest_is_strict\nC1008_Documentation_and_transport_pins_match\nC1008_Refusal_receipts_do_not_leak_secrets\nC1008_Retired_absent_null_is_accepted\nC1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt\nC1594_Cross_sha_cleanup_image_binds_the_retired_temp\nC1594_Older_retirement_receipt_is_ignored\nC1594_Disagreeing_cross_sha_images_refuse\nC1594_Antiphon_evidence_gitdir\nC1008_Preview_helper_removal_is_owned",
        "antiphon\nRetiredTempContainerScriptTests\nC994_Host_strict_volume_admission_remains_independent\nC994_Host_resume_never_runs_container_cleanup\nC994_Host_bridge_transport_preserves_literal_context",
        "antiphon\nBuildSlotScriptTests\nC800_WrapperInterruptKillsChildAndReleasesLease",
        "antiphon\nC1364BoundLandWaitProofTests\nC1364_Host_recycle_admission_reports_waited\nC1364_Host_recycle_post_removal_reports_waited\nC1364_Host_cleanup_reports_waited",
        "antiphon\nRemoteScriptContractTests\nC1374_Host_census_recovers_transient_reads\nC1087_Host_census_filters_and_names_cause\nC1008_Recycle_exact_default_volumes\nC1008_Recycle_refuses_references_and_unknown_census\nC1008_Recycle_audits_work_as_1654\nC1008_Recycle_refuses_unpublished_and_dirty_work\nC1008_Recycle_refuses_uninspectable_git\nC1008_Recycle_reaps_dirty_scratch\nC1008_Recycle_audits_promisor_checkout\nC1105_Git_audit_volume_scale\nC1105_Jq_argv_limit\nC1008_Recycle_preserves_tmp_copyup\nC1105_Verify_tmp_accepts_an_absent_pty_host\nC1008_Recycle_resume_requires_matching_receipt\nC1008_Recycle_receipt_records_disk_and_partial_failure\nC1008_Retire_temp_rechecks_absence_and_retirement\nC1008_Retire_temp_reclaims_below_cache_disk_gate\nC1008_Recycle_dry_run_never_mutates\nC1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes\nC1105_Redeploy_accepts_previous_generation_and_requires_new_bind\nC1105_Dry_run_previews_a_generation_change_without_moving_the_checkout\nC1105_V218_dry_run_reaches_the_generation_line\nC1105_Relative_dry_run_refuses_before_removal\nC1105_Generation_identity_refuses_mismatch\nC1105_Main_state_init_without_session_runner_refuses\nC1105_Compose_cmp_refuses_before_removal\nC1105_Resume_identity_refusals\nC1105_Resume_state_init_image_follows_the_journal\nC1105_Resume_foreign_leftover_is_not_an_image_mismatch\nC1105_Resume_digest_pins_previous_sha12\nC1105_Resume_post_removal_census_tracks_open_work\nC849_Deploy_prepares_and_verifies_before_acceptance\nC1105_Git_audit_recovery_tips\nC1105_Git_audit_seed_content\nC1105_Git_audit_index_content\nC1105_Git_audit_object_completeness\nC1105_Git_audit_overall_timeout\nC1105_Git_audit_resume_preserves_proof\nC1105_Git_audit_hidden_locations\nC1335_Mirror_owner_stamp_in_linked_admin",
        "antiphon\nCheckpointStampSafetyTests\ntracked_stamp_shapes_are_bounded_and_leave_results_in_place",
        "antiphon\nCheckpointTempRootSweepTests\nfifo_or_socket_root_is_reclaimed",
        "antiphon\nPlanCoverageCommandTests\noutside_project_fifo_is_refused_without_opening",
        "session-runner\nCompletedCardWorkspaceRemovalTests\nC1032_InspectionR3Mode\nC1032_RemovalLinuxIgnoredFifoRefuses",
        "session-runner\nHostStatsProbeLiveTests\nLinux_probe_reads_live_proc",
    ];

    // Suites whose assemblies compile NativeLinuxPolicyWitnessTests. A prefixed or frozen
    // row for any other suite is unwitnessed-suite on every host.
    public static readonly string[] CompiledSuiteIds =
    [
        "antiphon",
        "session-runner",
        "pty-host",
        "agents-pty",
    ];

    public static string? KnownSuite(Assembly assembly) => assembly.GetName().Name switch
    {
        "Antiphon.Tests" => "antiphon",
        "Antiphon.SessionRunner.Tests" => "session-runner",
        "Antiphon.PtyHost.Tests" => "pty-host",
        "Antiphon.Agents.Pty.Tests" => "agents-pty",
        _ => null,
    };

    public static string Sha256Utf8(string text) => Sha256Utf8(Encoding.UTF8.GetBytes(text));

    public static string Sha256Utf8(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var part in hash)
            builder.Append(part.ToString("x2"));
        return builder.ToString();
    }

    public static string PolicyCanonical(string policyJson)
    {
        using var document = JsonDocument.Parse(policyJson);
        return Canonical(document.RootElement, "policyHash");
    }

    public static string CanonicalJson(JsonElement element) => Canonical(element, null);

    public static string SealPolicyHash(string policyJson)
    {
        var hash = Sha256Utf8(PolicyCanonical(policyJson));
        using var document = JsonDocument.Parse(policyJson);
        var current = document.RootElement.GetProperty("policyHash").GetString() ?? "";
        var quoted = "\"" + current + "\"";
        var keyAt = policyJson.IndexOf("\"policyHash\"", StringComparison.Ordinal);
        if (keyAt < 0)
            throw new InvalidOperationException("policyHash field missing");
        var valueAt = policyJson.IndexOf(quoted, keyAt, StringComparison.Ordinal);
        if (valueAt < 0)
            throw new InvalidOperationException("policyHash value missing");
        return policyJson[..valueAt] + "\"" + hash + "\"" + policyJson[(valueAt + quoted.Length)..];
    }

    public static bool Excluded(string? className, string? typeName, string? method, JsonElement exclusions)
    {
        var simple = SimpleClass(className);
        if (string.IsNullOrWhiteSpace(simple))
            simple = typeName ?? "";
        foreach (var row in exclusions.EnumerateArray())
        {
            var cls = row.TryGetProperty("class", out var classElement) ? classElement.GetString() ?? "" : "";
            if (cls.Length == 0)
                continue;
            var full = className ?? "";
            var match = cls.Equals(simple, StringComparison.OrdinalIgnoreCase)
                || cls.Equals(full, StringComparison.OrdinalIgnoreCase);
            if (!match)
                continue;
            if (!row.TryGetProperty("methods", out var methods) || methods.GetArrayLength() == 0)
                return true;
            foreach (var item in methods.EnumerateArray())
            {
                if (string.Equals(item.GetString(), method, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    public static InspectionReport Inspect(InspectionRequest request)
    {
        var diagnostics = new List<WitnessDiagnostic>();
        var witnessed = new HashSet<string>(WitnessedRowKeys, StringComparer.Ordinal);
        if (witnessed.Count != 45)
            diagnostics.Add(Diag("witness-key-count", "", "", "", "", "", witnessed.Count.ToString()));

        JsonDocument policy;
        try
        {
            policy = JsonDocument.Parse(request.PolicyJson);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Diag("policy-schema", request.SuiteId, "", "", "", "", ex.Message));
            return Finish(diagnostics);
        }

        using (policy)
        {
            var root = policy.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || schema.GetRawText() != "4")
                diagnostics.Add(Diag("policy-schema", request.SuiteId, "", "", "", "", "schemaVersion"));
            var storedHash = root.TryGetProperty("policyHash", out var hashElement) ? hashElement.GetString() ?? "" : "";
            var computedHash = Sha256Utf8(Canonical(root, "policyHash"));
            if (!string.Equals(storedHash, computedHash, StringComparison.Ordinal))
                diagnostics.Add(Diag("policy-hash", request.SuiteId, "", "", "", "", storedHash + " != " + computedHash));

            var suiteNames = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("suites", out var suites) && suites.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in suites.EnumerateObject())
                    suiteNames.Add(property.Name);
            }
            foreach (var required in RequiredSuiteIds)
            {
                if (!suiteNames.Contains(required))
                    diagnostics.Add(Diag("unknown-exclusion", request.SuiteId, required, "", "", "", "omitted suite"));
            }

            var mapped = KnownSuite(request.Assembly);
            if (mapped is not null && !string.Equals(mapped, request.SuiteId, StringComparison.Ordinal))
                diagnostics.Add(Diag("suite-mismatch", request.SuiteId, mapped, "", "", "", request.Assembly.GetName().Name ?? ""));

            if (!root.TryGetProperty("profiles", out var profiles)
                || !profiles.TryGetProperty("rc", out var rc)
                || !rc.TryGetProperty("exclusions", out var exclusions)
                || exclusions.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(Diag("policy-schema", request.SuiteId, "", "", "", "", "profiles.rc.exclusions"));
                return Finish(diagnostics);
            }

            var rows = new List<PolicyRow>();
            var legacyParts = new List<string>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in exclusions.EnumerateArray())
            {
                var row = ReadRow(element);
                rows.Add(row);
                foreach (var duplicate in DuplicatePropertyNames(element))
                    diagnostics.Add(Diag("duplicate-key", row.Suite, row.Class, "", "", "", duplicate));
                if (!suiteNames.Contains(row.Suite))
                    diagnostics.Add(Diag("unknown-exclusion", row.Suite, row.Class, "", "", "", "unknown suite"));
                if (!seenKeys.Add(row.Key))
                    diagnostics.Add(Diag("duplicate-row", row.Suite, row.Class, "", "", "", row.Key.Replace('\n', '|')));
                if (IsFrozenKey(row.Key) && !(row.Platform == "windows" && row.Reason.StartsWith(LinuxReasonPrefix, StringComparison.Ordinal)))
                    diagnostics.Add(Diag("witnessed-row-reshaped", row.Suite, row.Class, "", "", "", row.Platform));
                if (witnessed.Contains(row.Key))
                    continue;
                if (row.Platform == "windows" && row.Reason.StartsWith(LinuxReasonPrefix, StringComparison.Ordinal))
                    continue;
                legacyParts.Add(Canonical(element, null));
            }

            var frozenPresent = 0;
            foreach (var key in WitnessedRowKeys)
            {
                if (seenKeys.Contains(key))
                    frozenPresent++;
            }
            if (frozenPresent > 0)
            {
                foreach (var key in WitnessedRowKeys)
                {
                    if (seenKeys.Contains(key))
                        continue;
                    var parts = key.Split('\n');
                    diagnostics.Add(Diag("witnessed-row-missing", parts.Length > 0 ? parts[0] : "", parts.Length > 1 ? parts[1] : "", "", "", "", ""));
                }
            }

            var legacyPayload = "[" + string.Join(",", legacyParts) + "]";
            var legacyDigest = Sha256Utf8(legacyPayload);
            if (!string.Equals(legacyDigest, LegacyExclusionDigest, StringComparison.Ordinal))
                diagnostics.Add(Diag("legacy-digest-mismatch", request.SuiteId, "", "", "", "", legacyDigest));

            if (request.CheckAttributePin)
                diagnostics.AddRange(AttributePinDiagnostics(request.Assembly, request.SuiteId));

            Type[] types;
            try
            {
                types = request.Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                diagnostics.Add(Diag("unresolved-row", request.SuiteId, "", "", "", "", "ReflectionTypeLoadException " + (ex.LoaderExceptions.FirstOrDefault()?.Message ?? "")));
                return Finish(diagnostics);
            }

            var nodes = new List<TestNode>();
            foreach (var type in types)
            {
                if (type is not { IsClass: true } || type.IsAbstract && !type.IsSealed)
                    continue;
                nodes.AddRange(TestNodes(type, request.SuiteId));
            }

            var byIdentity = new Dictionary<string, TestNode>(StringComparer.Ordinal);
            foreach (var node in nodes)
                byIdentity[node.Identity] = node;

            if (request.CompareBaseline && request.BaselineJson.Length > 0)
                CompareBaseline(request, byIdentity, diagnostics);

            var compiledSuites = new HashSet<string>(CompiledSuiteIds, StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row.Witnessed && !compiledSuites.Contains(row.Suite))
                    diagnostics.Add(Diag("unwitnessed-suite", row.Suite, row.Class, "", "", "", row.Key.Replace('\n', '|')));
            }

            var covered = new HashSet<TestNode>();
            var matchedTypes = new HashSet<Type>();
            foreach (var row in rows)
            {
                if (!string.Equals(row.Suite, request.SuiteId, StringComparison.Ordinal))
                    continue;
                if (!row.Witnessed && !(row.Platform == "windows" && row.Reason.StartsWith(LinuxReasonPrefix, StringComparison.Ordinal)))
                    continue;
                var matched = nodes.Where(node => RowSelects(row, node)).ToList();
                if (matched.Count == 0)
                    diagnostics.Add(Diag("unresolved-row", row.Suite, row.Class, "", "", "", row.Key.Replace('\n', '|')));
                foreach (var name in row.Methods)
                {
                    if (!matched.Any(node => node.Method.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        diagnostics.Add(Diag("unresolved-method", row.Suite, row.Class, "", "", name, ""));
                }
                foreach (var node in matched)
                {
                    covered.Add(node);
                    matchedTypes.Add(node.Type);
                    RefuseUnsupported(node, row, diagnostics);
                    var reason = MarkerReason(node);
                    if (string.IsNullOrEmpty(reason))
                        diagnostics.Add(Diag("missing-marker", row.Suite, row.Class, node.Concrete, node.Declaring, node.Signature, ""));
                }
            }

            foreach (var node in nodes)
            {
                if (!HasDirectMarker(node.Type) && !HasDirectMarker(node.Method))
                    continue;
                if (covered.Contains(node))
                    continue;
                diagnostics.Add(Diag("reverse-coverage", request.SuiteId, node.Type.Name, node.Concrete, node.Declaring, node.Signature, ""));
            }

            foreach (var type in types)
            {
                if (type is not { IsClass: true })
                    continue;
                if (!matchedTypes.Contains(type) && !HasDirectMarker(type) && !type.GetMethods(AllMethods).Any(HasDirectMarker))
                    continue;
                RefuseUnsupportedMembers(type, request.SuiteId, diagnostics);
            }
        }

        return Finish(diagnostics);
    }

    public static IReadOnlyList<WitnessDiagnostic> AttributePinDiagnostics(Assembly assembly, string suiteId)
    {
        var diagnostics = new List<WitnessDiagnostic>();
        var attribute = typeof(NativeLinuxOnlyAttribute);
        if (!attribute.IsSealed || attribute.BaseType != typeof(TUnit.Core.SkipAttribute))
            diagnostics.Add(Diag("attribute-pin", suiteId, "", attribute.FullName ?? "", "", "", "shape"));
        var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>();
        if (usage is null
            || usage.ValidOn != (AttributeTargets.Class | AttributeTargets.Method)
            || usage.AllowMultiple
            || usage.Inherited)
            diagnostics.Add(Diag("attribute-pin", suiteId, "", "", "", "", "usage"));
        if (attribute.Assembly != assembly)
            diagnostics.Add(Diag("attribute-pin", suiteId, "", attribute.Assembly.GetName().Name ?? "", "", "", "module"));
        var embedded = ReadResource(assembly, AttributeResourceName);
        if (embedded is null || !string.Equals(Sha256Utf8(embedded), AttributeSha256, StringComparison.Ordinal))
            diagnostics.Add(Diag("attribute-pin", suiteId, "", "", "", "", Sha256Utf8(embedded ?? [])));
        return diagnostics;
    }

    public static byte[]? ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static string SimpleClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return "";
        var bare = className;
        var open = bare.IndexOf('(');
        if (open > 0 && bare.EndsWith(')'))
            bare = bare[..open];
        var dot = bare.LastIndexOf('.');
        return dot < 0 ? bare : bare[(dot + 1)..];
    }

    public static string MethodSignature(MethodInfo method)
    {
        var builder = new StringBuilder();
        builder.Append(method.Name);
        if (method.IsGenericMethod)
            builder.Append('`').Append(method.GetGenericArguments().Length);
        builder.Append('(');
        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append(FormatReflectedType(parameters[i].ParameterType));
        }
        builder.Append(')');
        return builder.ToString();
    }

    static void CompareBaseline(InspectionRequest request, Dictionary<string, TestNode> byIdentity, List<WitnessDiagnostic> diagnostics)
    {
        using var baseline = JsonDocument.Parse(request.BaselineJson);
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        if (baseline.RootElement.TryGetProperty("Reasons", out var reasonArray))
        {
            foreach (var item in reasonArray.EnumerateArray())
            {
                var identity = IdentityOf(item);
                if (!identity.StartsWith(request.SuiteId + "|", StringComparison.Ordinal))
                    continue;
                if (!reasons.TryAdd(identity, item.GetProperty("Reason").GetString() ?? ""))
                    diagnostics.Add(Diag("baseline-duplicate", request.SuiteId, "", "", "", identity, ""));
            }
        }
        if (!baseline.RootElement.TryGetProperty("Roster", out var roster))
            return;
        foreach (var item in roster.EnumerateArray())
        {
            var identity = IdentityOf(item);
            if (!identity.StartsWith(request.SuiteId + "|", StringComparison.Ordinal))
                continue;
            if (!byIdentity.TryGetValue(identity, out var node))
            {
                diagnostics.Add(Diag("baseline-unresolved", request.SuiteId, item.GetProperty("RowClass").GetString() ?? "", item.GetProperty("Concrete").GetString() ?? "", item.GetProperty("Declaring").GetString() ?? "", item.GetProperty("Signature").GetString() ?? "", ""));
                continue;
            }
            if (!reasons.TryGetValue(identity, out var expected))
            {
                diagnostics.Add(Diag("baseline-omitted-reason", request.SuiteId, "", node.Concrete, node.Declaring, node.Signature, ""));
                continue;
            }
            var actual = MarkerReason(node);
            if (string.IsNullOrEmpty(actual))
            {
                var rowClass = item.TryGetProperty("RowClass", out var rowElement) ? rowElement.GetString() ?? "" : "";
                diagnostics.Add(Diag("missing-marker", request.SuiteId, rowClass, node.Concrete, node.Declaring, node.Signature, ""));
                continue;
            }
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                diagnostics.Add(Diag("reason-mismatch", request.SuiteId, "", node.Concrete, node.Declaring, node.Signature, actual));
        }
    }

    static string IdentityOf(JsonElement item) =>
        (item.GetProperty("Suite").GetString() ?? "")
        + "|" + (item.GetProperty("Concrete").GetString() ?? "")
        + "|" + (item.GetProperty("Declaring").GetString() ?? "")
        + "|" + (item.GetProperty("Signature").GetString() ?? "");

    static bool RowSelects(PolicyRow row, TestNode node)
    {
        var simple = SimpleClass(node.Concrete);
        if (!row.Class.Equals(simple, StringComparison.OrdinalIgnoreCase)
            && !row.Class.Equals(node.Concrete, StringComparison.OrdinalIgnoreCase))
            return false;
        return row.Methods.Count == 0
            || row.Methods.Any(name => node.Method.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    static void RefuseUnsupported(TestNode node, PolicyRow row, List<WitnessDiagnostic> diagnostics)
    {
        RefuseMember(node.Type, row.Suite, row.Class, node, diagnostics);
        RefuseMember(node.Method, row.Suite, row.Class, node, diagnostics);
    }

    static void RefuseUnsupportedMembers(Type type, string suite, List<WitnessDiagnostic> diagnostics)
    {
        var concrete = FormatReflectedType(type);
        foreach (var current in RefusalChain(type))
        {
            var declaring = FormatReflectedType(current);
            RefuseMember(current, suite, type.Name, new TestNode(type, null!, suite, concrete, declaring, ""), diagnostics);
            foreach (var method in current.GetMethods(AllMethods))
                RefuseMember(method, suite, type.Name, new TestNode(type, method, suite, concrete, FormatReflectedType(method.DeclaringType ?? current), MethodSignature(method)), diagnostics);
        }
    }

    static IEnumerable<Type> RefusalChain(Type type)
    {
        yield return type;
        if (!HasInherits(type))
            yield break;
        for (var cursor = type.BaseType; cursor is not null && cursor != typeof(object); cursor = cursor.BaseType)
            yield return cursor;
    }

    static void RefuseMember(MemberInfo member, string suite, string row, TestNode node, List<WitnessDiagnostic> diagnostics)
    {
        foreach (var attribute in member.GetCustomAttributesData())
        {
            if (!IsUnsupportedTestAttribute(attribute.AttributeType))
                continue;
            diagnostics.Add(Diag("unsupported-test-attribute", suite, row, node.Concrete, node.Declaring, node.Signature, attribute.AttributeType.FullName ?? attribute.AttributeType.Name));
        }
    }

    static bool IsUnsupportedTestAttribute(Type type)
    {
        var framework = typeof(TUnit.Core.TestAttribute).Assembly;
        for (var cursor = type; cursor is not null && cursor != typeof(object); cursor = cursor.BaseType)
        {
            if (cursor.Assembly != framework)
                continue;
            if (cursor == typeof(TUnit.Core.TestAttribute))
                return false;
            if (typeof(TUnit.Core.BaseTestAttribute).IsAssignableFrom(cursor))
                return true;
        }
        return false;
    }

    static IEnumerable<TestNode> TestNodes(Type type, string suite)
    {
        var chain = new List<Type>();
        if (HasInherits(type))
        {
            for (var cursor = type; cursor is not null && cursor != typeof(object); cursor = cursor.BaseType)
                chain.Add(cursor);
            chain.Reverse();
        }
        else
            chain.Add(type);

        // TUnit still runs a base [Test] hidden by a non-test `new` method. A real override
        // replaces that method, and a `new` [Test] is the only node TUnit runs for the signature.
        var chosen = new List<MethodInfo>();
        foreach (var current in chain)
        {
            foreach (var method in current.GetMethods(AllMethods))
            {
                if (method.IsSpecialName)
                    continue;
                var root = method.GetBaseDefinition();
                if (!root.Equals(method))
                {
                    chosen.RemoveAll(existing => existing.GetBaseDefinition().Equals(root));
                    if (IsTest(method))
                        chosen.Add(method);
                    continue;
                }

                if (!IsTest(method))
                    continue;
                var signature = MethodSignature(method);
                chosen.RemoveAll(existing => MethodSignature(existing).Equals(signature, StringComparison.Ordinal));
                chosen.Add(method);
            }
        }

        foreach (var method in chosen)
        {
            var concrete = FormatReflectedType(type);
            var declaring = FormatReflectedType(method.DeclaringType ?? type);
            yield return new TestNode(type, method, suite, concrete, declaring, MethodSignature(method));
        }
    }

    static bool HasInherits(Type type)
    {
        var inherits = typeof(TUnit.Core.TestAttribute).Assembly.GetType("TUnit.Core.InheritsTestsAttribute");
        return inherits is not null && type.GetCustomAttributesData().Any(attribute => attribute.AttributeType == inherits);
    }

    static bool IsTest(MethodInfo method)
    {
        for (var cursor = method; cursor is not null; cursor = cursor == cursor.GetBaseDefinition() ? null : cursor.GetBaseDefinition())
        {
            if (cursor.GetCustomAttributesData().Any(attribute => attribute.AttributeType == typeof(TUnit.Core.TestAttribute)))
                return true;
            if (cursor == method.GetBaseDefinition())
                break;
        }
        return method.GetCustomAttributesData().Any(attribute => attribute.AttributeType == typeof(TUnit.Core.TestAttribute));
    }

    static bool HasDirectMarker(MemberInfo member) =>
        member.GetCustomAttributesData().Any(attribute => attribute.AttributeType == typeof(NativeLinuxOnlyAttribute));

    static string? MarkerReason(TestNode node)
    {
        var method = ReasonOn(node.Method);
        if (method is not null)
            return method;
        return ReasonOn(node.Type);
    }

    static string? ReasonOn(MemberInfo member)
    {
        var data = member.GetCustomAttributesData().FirstOrDefault(attribute => attribute.AttributeType == typeof(NativeLinuxOnlyAttribute));
        if (data is null)
            return null;
        if (data.ConstructorArguments.Count != 1 || data.ConstructorArguments[0].Value is not string reason || reason.Length == 0)
            return "";
        return reason;
    }

    static PolicyRow ReadRow(JsonElement element)
    {
        var suite = element.GetProperty("suite").GetString() ?? "";
        var klass = element.GetProperty("class").GetString() ?? "";
        var methods = element.TryGetProperty("methods", out var methodElement) && methodElement.ValueKind == JsonValueKind.Array
            ? methodElement.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList()
            : new List<string>();
        var platform = element.TryGetProperty("platform", out var platformElement) ? platformElement.GetString() ?? "" : "";
        var reason = element.GetProperty("reason").GetString() ?? "";
        var key = suite + "\n" + klass + "\n" + string.Join("\n", methods);
        var shaped = platform == "windows" && reason.StartsWith(LinuxReasonPrefix, StringComparison.Ordinal);
        return new PolicyRow(suite, klass, methods, platform, reason, key, shaped || IsFrozenKey(key));
    }

    static IEnumerable<string> DuplicatePropertyNames(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (seen.Add(property.Name) || !reported.Add(property.Name))
                continue;
            yield return property.Name;
        }
    }

    static bool IsFrozenKey(string key)
    {
        foreach (var item in WitnessedRowKeys)
        {
            if (string.Equals(item, key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    static string FormatReflectedType(Type type)
    {
        if (type.IsByRef)
            return FormatReflectedType(type.GetElementType()!) + "&";
        if (type.IsPointer)
            return FormatReflectedType(type.GetElementType()!) + "*";
        if (type.IsArray)
            return FormatReflectedType(type.GetElementType()!) + "[" + new string(',', Math.Max(0, type.GetArrayRank() - 1)) + "]";
        if (type.IsGenericParameter)
            return type.Name;
        if (type.IsGenericType)
            return (type.GetGenericTypeDefinition().FullName ?? type.Name).Replace('+', '+');
        return type.FullName ?? type.Name;
    }

    static InspectionReport Finish(List<WitnessDiagnostic> diagnostics)
    {
        var unique = new List<WitnessDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var diagnostic in diagnostics)
        {
            if (seen.Add(diagnostic.ToString()))
                unique.Add(diagnostic);
        }
        unique.Sort(static (left, right) => string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal));
        return new InspectionReport(unique);
    }

    static WitnessDiagnostic Diag(string code, string suite, string row, string concrete, string declaring, string signature, string detail) =>
        new(code, suite, row, concrete, declaring, signature, detail);

    static string Canonical(JsonElement element, string? skipProperty)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return "null";
            case JsonValueKind.String:
                return Quote(element.GetString() ?? "");
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Number:
                return element.GetRawText();
            case JsonValueKind.Array:
                return "[" + string.Join(",", element.EnumerateArray().Select(item => Canonical(item, null))) + "]";
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var pairs = new List<(string Key, JsonElement Value)>();
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Length == 0 || property.Name == skipProperty || !seen.Add(property.Name))
                        continue;
                    pairs.Add((property.Name, property.Value));
                }
                pairs.Sort(static (left, right) => string.Compare(left.Key, right.Key, StringComparison.CurrentCultureIgnoreCase));
                return "{" + string.Join(",", pairs.Select(pair => Quote(pair.Key) + ":" + Canonical(pair.Value, null))) + "}";
            default:
                throw new InvalidOperationException("unsupported JSON " + element.ValueKind);
        }
    }

    static string Quote(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(ch); break;
            }
        }
        builder.Append('"');
        return builder.ToString();
    }

    const BindingFlags AllMethods = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    sealed record PolicyRow(string Suite, string Class, IReadOnlyList<string> Methods, string Platform, string Reason, string Key, bool Witnessed);

    sealed record TestNode(Type Type, MethodInfo Method, string Suite, string Concrete, string Declaring, string Signature)
    {
        public string Identity => Suite + "|" + Concrete + "|" + Declaring + "|" + Signature;
    }
}
