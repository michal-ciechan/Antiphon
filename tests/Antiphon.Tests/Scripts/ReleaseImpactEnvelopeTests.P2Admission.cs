using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using P2 = Antiphon.Nightly.ReleaseImpactAdmission;

namespace Antiphon.Tests.Scripts;

public sealed partial class ReleaseImpactEnvelopeTests
{
    private static async Task CheckAdmission(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        await using var world = await P2World.Create();
        var authority = world.Authority();
        var prep = P2.Prepare(world.Request, world.Chunks, authority);
        prep.Evidence.ShouldNotBeNull(prep.Reason);
        var proposal = prep.Evidence!.WriteProposal();
        var wire = JsonNode.Parse(proposal)!.AsObject();
        var projections = wire["projections"]!.AsObject();
        var schema = P2Json("schema");
        var rows = new NightlyCaseRows();
        var roster = new List<string>();
        void Case(string id, bool ok, string detail = "")
        {
            cancellationToken.ThrowIfCancellationRequested();
            roster.Add(id);
            rows.Check(ok, id, detail);
        }

        P2.Decision Admit(byte[] bytes, P2.Preparation? evidence = null, P2.Request? request = null,
            IReadOnlyList<P2.Chunk>? discovery = null, int limit = ReleaseImpactIntegrity.EnvelopeLimit) =>
            P2.AdmitPrepared(request ?? world.Request, discovery ?? world.Chunks, () => new MemoryStream(bytes, writable: false),
                evidence ?? prep, limit);
        static string[] Ids(IEnumerable<P2.Row> chunks) => chunks.Select(chunk => chunk.Chunk).ToArray();
        static bool Full(P2.Decision result, IReadOnlyList<P2.Chunk> expected, string why) =>
            result.EffectiveMode == "Full" && result.VerifiedHandle is null && !result.CarryCandidates.Any() &&
            result.CreditedCount == 0 && Ids(result.Owed).SequenceEqual(expected.Select(row => row.Id)) &&
            result.Owed.SelectMany(row => row.Cases).SequenceEqual(expected.SelectMany(row => row.Cases)) &&
            result.Reason.StartsWith(why, StringComparison.Ordinal);

        void CheckFull(string id, P2.Decision result, string why, IReadOnlyList<P2.Chunk>? expected = null) =>
            Case(id, Full(result, expected ?? world.Chunks, why), result.Reason);

        var good = Admit(proposal);
        Case("T.valid.candidates", good.VerifiedHandle is not null && Ids(good.CarryCandidates).SequenceEqual(["Q"]), good.Reason);
        Case("T.valid.owed", Ids(good.Owed).SequenceEqual(["H"]) && good.Owed.SelectMany(row => row.Cases).Select(row => row.CaseUid).SequenceEqual(["h1"]));
        Case("T.valid.creditedCount", good.CreditedCount == 0);
        Case("T.valid.dispositions", good.Rows.Select(row => row.Disposition).SequenceEqual(["execute", "carry-candidate"]));
        var quiet = authority.Origins.Single(c => c.Chunk == "Q");
        Case("T.valid.origin", Equals(good.CarryCandidates.SingleOrDefault()?.Origin,
            new P2.Origin(quiet.Sha, quiet.RunId, quiet.Attempt, quiet.InventorySeal, quiet.Evidence.Id, quiet.Evidence.Digest)));
        Case("T.valid.reason", projections["selection"]!["decisions"]![0]!["reasons"]!.AsArray().Any(reason =>
            reason?["path"]?.GetValue<string>() == P2World.HotInput && reason["kind"]!.GetValue<string>() == "derived"));
        Case("T.valid.retry", Admit(proposal).VerifiedHandle == good.VerifiedHandle);
        Case("T.valid.independentSeal", IndependentEnvelope(schema, wire, projections)["rootDigest"]!.GetValue<string>() ==
            good.VerifiedHandle?.RootDigest);

        if (good.Rows.Count > 1 && good.Rows is System.Collections.Generic.IList<P2.Row> writable)
        {
            try { writable[1] = good.Rows[0] with { Disposition = "carry-candidate" }; }
            catch (NotSupportedException) { }
        }
        Case("T.snapshot.eligibleAfterCallerEdit", Ids(good.CarryCandidates).SequenceEqual(["Q"]));

        foreach (var (field, value) in new (string Field, JsonNode Value)[]
        {
            ("repositoryId", JsonValue.Create("another-repository")!), ("line", JsonValue.Create("rc/other")!),
            ("baselineSha", JsonValue.Create(world.Sibling)!), ("baselineTree", JsonValue.Create(projections["algorithm"]!["sources"]![0]!["blobId"]!.GetValue<string>())!),
            ("candidateSha", JsonValue.Create(world.Request.BaselineSha)!), ("candidateTree", JsonValue.Create(wire["baselineTree"]!.GetValue<string>())!),
            ("runId", JsonValue.Create("other-run")!), ("attempt", JsonValue.Create(10)!), ("revision", JsonValue.Create(11)!),
            ("algorithmSha", JsonValue.Create(world.Request.BaselineSha)!),
        })
        {
            var root = wire.DeepClone().AsObject();
            root[field] = value;
            var bytes = Reseal(root, projections);
            CheckFull("T.reseal.context." + field, Admit(bytes), field == "algorithmSha" ? "p2-input:" : "p2-context:");
        }

        byte[] Reseal(JsonObject root, JsonObject values, string? originalDigest = null)
        {
            var amended = IndependentEnvelope(schema, root, values);
            var bytes = Encoding.UTF8.GetBytes(amended.ToJsonString());
            using var stream = new MemoryStream(bytes);
            var transport = ReleaseImpactIntegrity.Decode(stream, foldedPaths: authority.ObservedTarget.FoldedPaths);
            transport.RootDigest.ShouldBe(amended["rootDigest"]!.GetValue<string>(),
                "the checksum-only diagnostic accepts the internally coherent proposal");
            transport.RootDigest.ShouldNotBe(originalDigest ?? wire["rootDigest"]!.GetValue<string>(), "the alternative changed an independently bound leaf");
            return bytes;
        }

        void ResealRole(string id, string role, Action<JsonNode> edit, string why)
        {
            var changed = projections.DeepClone().AsObject();
            edit(changed[role]!);
            CheckFull(id, Admit(Reseal(wire, changed)), why);
        }

        string[] context = ["os", "architecture", "profile", "tfm", "rid", "configuration"];
        foreach (var member in context)
            ResealRole("T.reseal.target." + member, "target", value => value[member] = member == "os"
                ? (authority.ObservedTarget.Os == "windows" ? "linux" : "windows") : "altered-" + member, "p2-context:");
        ResealRole("T.reseal.target.buildProperty", "target", value => value["buildProperties"]![0]!["value"] = "changed", "p2-context:");
        ResealRole("T.reseal.target.argv", "target", value => value["argv"]![0] = "changed", "p2-context:");

        foreach (var kind in new[] { "rawClosureDigest", "replaySelector", "evaluator", "graph", "extractor" })
        {
            ResealRole("T.reseal.algorithm." + kind, "algorithm", value => value[kind] =
                kind is "rawClosureDigest" or "replaySelector" ? new string('1', 64) : "changed-" + kind, "p2-input:");
        }

        foreach (var (side, name) in new[] { ("B", "baseline"), ("A", "candidate") })
        {
            var role = name + "Manifest";
            void ChangeFile(string id, Action<JsonNode> edit) =>
                ResealRole("T.reseal.manifest." + side + "." + id, role, value =>
                    edit(value.AsArray().Single(row => row!["path"]!.GetValue<string>() == P2World.ExtraInput)!), "p2-input:");
            ChangeFile("path", row => row["path"] = "shared/replacement.txt");
            ChangeFile("mode", row => row["gitMode"] = "100755");
            ChangeFile("blob", row => row["blobId"] = new string('2', 40));
            ChangeFile("rawSize", row => row["rawByteCount"] = 10);
            ChangeFile("rawSha", row => row["rawSha256"] = new string('3', 64));
            ResealRole("T.reseal.manifest." + side + ".omittedMember.mode", role, value => value.AsArray().Remove(
                value.AsArray().Single(row => row!["path"]!.GetValue<string>() == P2World.ExtraInput)), "p2-input:");

            foreach (var member in new[] { "catalogHash", "policyHash", "runtimeHash", "selectorHash" })
                ResealRole("T.reseal.compatibility." + side + "." + member, "compatibility",
                    value => value[name]![member] = new string('4', 64), "p2-input:");
            var index = name + "Index";
            ResealRole("T.reseal.index." + side + ".side", index, value => value["side"] = side == "B" ? "A" : "B", "p2-input:");
            ResealRole("T.reseal.index." + side + ".commit", index, value => value["commit"] = new string('5', 40), "p2-input:");
            ResealRole("T.reseal.index." + side + ".blob", index, value => value["blobId"] = new string('5', 40), "p2-input:");
            ResealRole("T.reseal.index." + side + ".rawSize", index, value => value["rawByteCount"] = 1, "p2-input:");
            ResealRole("T.reseal.index." + side + ".rawSha", index, value => value["rawSha256"] = new string('5', 64), "p2-input:");
            ResealRole("T.reseal.index." + side + ".schema", index, value => value["schema"] = 2, "p2-input:");
            ResealRole("T.reseal.index." + side + ".builder", index, value => value["builderDigest"] = new string('5', 64), "p2-input:");
            ResealRole("T.reseal.index." + side + ".edge", index, value => value["edges"]![0]!["class"] = "QuietTests", "p2-input:");
            ResealRole("T.reseal.index." + side + ".builderInput", index, value => value["builderInputs"]!.AsArray().Clear(), "p2-input:");
            ResealRole("T.reseal.graph." + side + ".edge", name + "Graph", value => value.AsArray().RemoveAt(0), "p2-input:");

            foreach (var input in authority.ObservedInputs.Where(input => input.Side == side))
                foreach (var member in new[] { "digest", "provenanceDigest", "sourceSha" })
                    ResealRole($"T.reseal.targetInputs.{side}.{input.Chunk ?? "target"}.{input.Role}.{member}", "target", value =>
                    {
                        var evidence = value["observed"]!.AsArray().Single(row => row!["side"]!.GetValue<string>() == input.Side &&
                            row["chunk"]?.GetValue<string>() == input.Chunk && row["role"]!.GetValue<string>() == input.Role)!;
                        evidence[member] = new string('6', member == "sourceSha" ? 40 : 64);
                    }, "p2-context:");
        }
        ResealRole("T.reseal.diff.path", "diff", value => value[0]!["path"] = "tests/Hot/not-changed.txt", "p2-input:");
        ResealRole("T.reseal.diff.status", "diff", value => value[0]!["status"] = "T", "p2-input:");
        ResealRole("T.reseal.diff.membership", "diff", value => value.AsArray().RemoveAt(0), "p2-input:");
        ResealRole("T.reseal.compatibility.scoped", "compatibility", value => value["scoped"]![0]!["value"] = "other", "p2-input:");
        ResealRole("T.reseal.selection.reasonOnly.mode", "selection",
            value => value["decisions"]![0]!["reasons"]![0]!["kind"] = "alternate-reason", "p2-selection:");
        ResealRole("T.selection.lateRefusal.candidates", "selection", value => value["decisions"]![0]!["disposition"] = "carry-candidate", "p2-selection:");
        ResealRole("T.reseal.obligations.watermark", "obligations", value => value["watermark"] = "prior-history", "p2-receipt:");
        ResealRole("T.reseal.obligations.withdraw", "obligations", value => value["withdrawn"] = true, "p2-receipt:");
        ResealRole("T.incomplete.unexpectedFalse", "selection", value => value["complete"] = false, "p2-selection:");
        ResealRole("T.incomplete.globalReason", "selection", value => value["globalReasons"]!.AsArray().Add("unknown"), "p2-selection:");
        ResealRole("T.reseal.trust.policy", "trust", value => value["candidatePolicy"]!["rawSha256"] = new string('7', 64), "p2-input:");
        foreach (var field in new[] { "format", "runId", "attempt", "revision", "sha", "rawByteCount", "rawSha256" })
            ResealRole("T.reseal.inventory." + field, "inventory", value => value[field] = field switch
            {
                "attempt" or "revision" or "rawByteCount" => JsonValue.Create(123),
                "sha" => JsonValue.Create(new string('7', 40)),
                "rawSha256" => JsonValue.Create(new string('7', 64)),
                _ => JsonValue.Create("changed-" + field),
            }, "p2-receipt:");
        foreach (var (chunk, index) in new[] { ("H", 0), ("Q", 1) })
        {
            foreach (var member in new[] { "suite", "executionKind", "contentDigest", "baselineInputDigest", "candidateInputDigest" })
                ResealRole("T.reseal.roster." + chunk + "." + member, "roster", value => value[index]![member] =
                    member.EndsWith("Digest", StringComparison.Ordinal) ? new string('7', 64) : "different", "p2-selection:");
            foreach (var member in new[] { "sha", "runId", "attempt", "inventorySeal", "evidenceId", "evidenceDigest" })
                ResealRole("T.reseal.receipts." + chunk + "." + member, "receipts", value => value[index]!["origin"]![member] = member switch
                {
                    "attempt" => JsonValue.Create(5), "sha" => JsonValue.Create(new string('8', 40)),
                    "inventorySeal" or "evidenceDigest" => JsonValue.Create(new string('8', 64)),
                    _ => JsonValue.Create("different-" + member),
                }, "p2-receipt:");
        }

        var noOrigin = world.Authority(omitQuietRow: true);
        var fullDiscovery = P2.Prepare(world.Request, world.Chunks, noOrigin);
        var narrowed = new[] { world.Chunks[0], world.Chunks[1] with { Cases = [world.Chunks[1].Cases[0]] } };
        var omitted = P2.Prepare(world.Request, narrowed, noOrigin);
        var narrowWire = JsonNode.Parse(omitted.Evidence!.WriteProposal())!.AsObject();
        var narrowProjections = narrowWire["projections"]!.AsObject();
        CheckFull("T.missing.currentCase.mode", Admit(Reseal(narrowWire, narrowProjections), fullDiscovery), "p2-selection:");
        ResealRole("T.missing.equalCount", "universe", value => value[2]!["caseUid"] = "another-q2", "p2-selection:");
        ResealRole("T.missing.emptyUniverse", "universe", value => value.AsArray().Clear(), "p2-selection:");
        ResealRole("T.missing.extraIdentity", "universe", value =>
        {
            var additional = value[2]!.DeepClone();
            additional["caseUid"] = "q3";
            value.AsArray().Add(additional);
        }, "p2-selection:");
        ResealRole("T.missing.wholeChunk", "roster", value => value.AsArray().RemoveAt(1), "p2-selection:");
        ResealRole("T.missing.overlappingDispositions", "selection", value =>
        {
            var duplicate = value["decisions"]![1]!.DeepClone();
            duplicate["disposition"] = "execute";
            duplicate["origin"] = null;
            value["decisions"]!.AsArray().Add(duplicate);
        }, "p2-selection:");
        CheckFull("T.ordinaryProvider", P2.Admit(world.Request, world.Chunks, () => new MemoryStream(proposal)), "p2-incomplete:");
        var unavailable = authority with { Evaluate = _ => null };
        CheckFull("T.incomplete.unavailableEvaluator", Admit(proposal, P2.Prepare(world.Request, world.Chunks, unavailable)), "p2-incomplete:evaluator");
        var missingObservation = authority with { ObservedInputs = authority.ObservedInputs.Skip(1).ToArray() };
        CheckFull("T.incomplete.missingObservation", Admit(proposal, P2.Prepare(world.Request, world.Chunks, missingObservation)), "p2-input:observations");
        var otherTarget = authority.ObservedTarget with { Os = authority.ObservedTarget.Os == "windows" ? "linux" : "windows" };
        var otherRequest = world.Request with { TargetIntentDigest = P2.TargetIntentDigest(otherTarget) };
        var differentlyObserved = authority with
        {
            ObservedTarget = otherTarget,
            Approval = authority.Approval with { ApprovedRequest = otherRequest },
        };
        CheckFull("T.incomplete.wrongActualTarget", Admit(proposal,
            P2.Prepare(otherRequest, world.Chunks, differentlyObserved), otherRequest), "p2-incomplete:target-contract");
        var undiscovered = P2.Admit(world.Request, null, () => new MemoryStream(proposal));
        Case("T.discovery.outstanding", undiscovered.DiscoveryOutstanding && Full(undiscovered, [], "p2-incomplete:"));

        var wrongSeal = wire.DeepClone();
        wrongSeal["rootDigest"] = new string('0', 64);
        CheckFull("T.seal.mode", Admit(Encoding.UTF8.GetBytes(wrongSeal.ToJsonString())), "p2-format:rootDigest");
        CheckFull("T.truncated", Admit(proposal[..^1]), "p2-format:json");
        var transportText = Encoding.UTF8.GetString(proposal);
        var duplicateNested = transportText.Replace("\"configuration\":", "\"configuration\":\"earlier\",\"configuration\":", StringComparison.Ordinal);
        CheckFull("T.shape.nestedDuplicate", Admit(Encoding.UTF8.GetBytes(duplicateNested)), "p2-format:target");
        var missingSeal = wire.DeepClone().AsObject();
        missingSeal.Remove("rootDigest");
        CheckFull("T.shape.missingRequired", Admit(Encoding.UTF8.GetBytes(missingSeal.ToJsonString())), "p2-format:envelope");
        CheckFull("T.unconsumed", Admit([.. proposal, .. "{}"u8]), "p2-format:json");
        var unknown = wire.DeepClone().AsObject();
        unknown["format"] = "antiphon-p2-integrity-v2";
        CheckFull("T.version", Admit(Encoding.UTF8.GetBytes(IndependentEnvelope(schema, unknown, projections).ToJsonString())), "p2-format:format");
        CheckFull("T.open", P2.AdmitPrepared(world.Request, world.Chunks, () => throw new UnauthorizedAccessException(), prep), "p2-format:read");
        P2.Decision? interrupted = null;
        try { interrupted = P2.AdmitPrepared(world.Request, world.Chunks, () => new FailingStream(proposal[..64]), prep); }
        catch (IOException) { }
        Case("T.read.returnedFull", interrupted is not null && Full(interrupted, world.Chunks, "p2-format:read"));

        const int cap = 8_388_608;
        var pad = new byte[cap + 1];
        pad.AsSpan().Fill((byte)' ');
        proposal.CopyTo(pad, 0);
        Case("T.limit.atLimit.mode", P2.AdmitPrepared(world.Request, world.Chunks,
            () => new MemoryStream(pad, 0, cap, false), prep).VerifiedHandle is not null);
        CheckFull("T.limit.oneOver.mode", Admit(pad), "p2-format:envelope-size");
        var streamLimit = proposal.Length;
        var atLimit = new CountedStream(proposal, streamLimit + 1);
        var overLimit = new CountedStream([.. proposal, (byte)' ', (byte)' '], streamLimit + 1);
        var unknownAtLimit = P2.AdmitPrepared(world.Request, world.Chunks, () => atLimit, prep, streamLimit);
        Case("T.limit.unknownAtLimit", unknownAtLimit.VerifiedHandle is not null && atLimit.BytesRead == streamLimit);
        var unknownOver = P2.AdmitPrepared(world.Request, world.Chunks, () => overLimit, prep, streamLimit);
        Case("T.limit.unknownOneOver", Full(unknownOver, world.Chunks, "p2-format:envelope-size") && overLimit.BytesRead == streamLimit + 1);
        CheckFull("T.limit.knownLengthNoRead", P2.AdmitPrepared(world.Request, world.Chunks,
            () => new OversizeStream(cap + 1), prep), "p2-format:envelope-size");

        var attachment = Encoding.UTF8.GetBytes(projections["algorithm"]!.ToJsonString() + " ");
        var address = IndependentDigest(attachment);
        var enclosing = wire.DeepClone();
        enclosing["projections"]!["algorithm"] = new JsonObject { ["address"] = address, ["byteCount"] = attachment.Length };
        var addressed = Encoding.UTF8.GetBytes(enclosing.ToJsonString());
        P2.Preparation Attachment(byte[] payload) => P2.Prepare(world.Request, world.Chunks, authority with
        {
            OpenProjection = id => id == address ? new MemoryStream(payload, false) : throw new FileNotFoundException(),
        });
        Case("T.address.original.mode", Admit(addressed, Attachment(attachment)).VerifiedHandle is not null);
        var different = attachment.ToArray();
        different[^1] = (byte)'\t';
        CheckFull("T.address.sameMeaningChangedBytes.mode", Admit(addressed, Attachment(different)), "p2-input:algorithm");

        var incomplete = P2.Prepare(world.Request, world.Chunks, world.Authority(unresolved: true));
        var honestlyIncomplete = incomplete.Evidence!.WriteProposal();
        CheckFull("T.incomplete.agreedFalse.mode", Admit(honestlyIncomplete, incomplete), "p2-incomplete:");
        var revision = world.Request with { Revision = world.Request.Revision + 1 };
        var lateAuthority = authority with
        {
            Approval = authority.Approval with { ApprovedRequest = revision },
            History = [new(10, revision.CandidateSha, revision.RunId, revision.Attempt, revision.Revision, "Q", "open", "later-failure")],
        };
        var late = P2.Prepare(revision, world.Chunks, lateAuthority);
        CheckFull("T.revision.stale.mode", Admit(proposal, late, revision), "p2-context:revision");
        var lateHonest = late.Evidence!.WriteProposal();
        var lateResult = Admit(lateHonest, late, revision);
        Case("T.owed.later", lateResult.VerifiedHandle is not null && Ids(lateResult.Owed).SequenceEqual(["H", "Q"]));
        var concealed = JsonNode.Parse(lateHonest)!.AsObject();
        var lateProjections = concealed["projections"]!.AsObject();
        lateProjections["obligations"]!["owed"]!.AsArray().Clear();
        lateProjections["obligations"]!["history"]!.AsArray().Clear();
        lateProjections["selection"] = projections["selection"]!.DeepClone();
        CheckFull("T.owed.concealedLaterFailure", Admit(Reseal(concealed, lateProjections), late, revision), "p2-receipt:obligations");

        var withheld = authority with { Approval = authority.Approval with { Withdrawn = true } };
        CheckFull("T.revision.withdrawn", Admit(proposal, P2.Prepare(world.Request, world.Chunks, withheld)), "p2-context:approval");

        var dirty = P2.Prepare(world.Request, world.Chunks, world.Authority(dirtyQuiet: true));
        var cleanCopy = P2World.Trx(world.Chunks[1]);
        var copied = P2.Prepare(world.Request, world.Chunks, world.Authority(removeQuietPin: true) with
        {
            OpenProjection = digest => digest == IndependentDigest(cleanCopy)
                ? new MemoryStream(cleanCopy, writable: false) : throw new FileNotFoundException(),
        });
        var duplicate = P2.Prepare(world.Request, world.Chunks, world.Authority(duplicateQuietPass: true));
        var nonzero = P2.Prepare(world.Request, world.Chunks, world.Authority(failQuietExit: true));
        var missing = P2.Prepare(world.Request, world.Chunks, world.Authority(omitQuietRow: true));
        foreach (var (id, binding) in new (string, P2.Preparation)[]
        {
            ("targetInputs.sameCasesDirtyInputs", dirty), ("receipts.copyOnly", copied), ("receipts.duplicatePass", duplicate),
            ("receipts.failedExit", nonzero), ("owed.missingRow", missing),
        })
        {
            binding.Evidence.ShouldNotBeNull(binding.Reason);
            var honestBytes = binding.Evidence!.WriteProposal();
            var honestResult = Admit(honestBytes, binding);
            Case("T." + id + ".honest", honestResult.VerifiedHandle is not null && Ids(honestResult.Owed).SequenceEqual(["H", "Q"]), honestResult.Reason);
            var disputed = JsonNode.Parse(honestBytes)!.AsObject();
            var outward = disputed["projections"]!.AsObject();
            var originalQ = projections["selection"]!["decisions"]![1]!.DeepClone();
            outward["selection"]!["decisions"]![1] = originalQ;
            outward["selection"]!["classes"] = projections["selection"]!["classes"]!.DeepClone();
            var originalDigest = disputed["rootDigest"]!.GetValue<string>();
            if (id == "receipts.copyOnly")
                outward["receipts"]![1] = projections["receipts"]![1]!.DeepClone();
            else if (id.StartsWith("receipts.", StringComparison.Ordinal))
            {
                var receipt = outward["receipts"]![1]!;
                receipt["reason"] = null;
                receipt["origin"] ??= projections["receipts"]![1]!["origin"]!.DeepClone();
                receipt["inputDigest"] ??= projections["receipts"]![1]!["inputDigest"]!.DeepClone();
            }
            CheckFull("T." + id + ".disputedMode", Admit(Reseal(disputed, outward, originalDigest), binding), "p2-");
        }

        var oneOpen = P2.Prepare(world.Request, world.Chunks, world.Authority(flipQuietRead: true));
        Case("T.snapshot.artifactOpenCount", world.QuietOpens == 1, $"opens={world.QuietOpens}");
        var openDecision = Admit(oneOpen.Evidence!.WriteProposal(), oneOpen);
        Case("T.snapshot.originalEligible", Ids(openDecision.CarryCandidates).SequenceEqual(["Q"]));
        var retained = P2.Prepare(world.Request, world.Chunks, world.Authority(carryOrigin: true));
        var retainedDecision = Admit(retained.Evidence!.WriteProposal(), retained);
        Case("T.receipts.originChain.validOrigin", retainedDecision.CarryCandidates.SingleOrDefault()?.Origin is
            { RunId: "run-b0", Attempt: 1 } origin && origin.Sha == world.OriginBase);

        var stamped = JsonNode.Parse(retained.Evidence.WriteProposal())!.AsObject();
        var carried = stamped["projections"]!.AsObject();
        foreach (var declaredOrigin in new[] { carried["receipts"]![1]!["origin"]!, carried["selection"]!["decisions"]![1]!["origin"]! })
        {
            declaredOrigin["sha"] = world.Request.BaselineSha;
            declaredOrigin["runId"] = "run-b";
            declaredOrigin["attempt"] = 2;
        }
        CheckFull("T.receipts.originChain.falseOrigin", Admit(Reseal(stamped, carried), retained), "p2-receipt:");

        var unrelated = world.Request with { BaselineSha = world.Sibling };
        var unrelatedAuthority = world.Authority(unrelated);
        var unrelatedProof = P2.Prepare(unrelated, world.Chunks, unrelatedAuthority);
        CheckFull("T.context.nonAncestor.mode", Admit(unrelatedProof.Evidence?.WriteProposal() ?? proposal,
            unrelatedProof, unrelated), "p2-context:ancestry");
        var equality = world.Request with { CandidateSha = world.Request.BaselineSha };
        var equalityProof = P2.Prepare(equality, world.Chunks, world.Authority(equality));
        var equalityDecision = Admit(equalityProof.Evidence!.WriteProposal(), equalityProof, equality);
        Case("T.context.equalHeads", Ids(equalityDecision.CarryCandidates).SequenceEqual(["H", "Q"]) && equalityDecision.CreditedCount == 0);
        var brokenTool = authority.Installation with { OpenInstalledFile = _ => new MemoryStream("changed installation"u8.ToArray()) };
        CheckFull("T.algorithm.actualInstall.mode", Admit(proposal,
            P2.Prepare(world.Request, world.Chunks, authority with { Installation = brokenTool })), "p2-input:");

        var digestBeforeEdit = good.VerifiedHandle?.RootDigest;
        proposal[0] = (byte)'!';
        CheckFull("T.snapshot.editedCallerBuffer", Admit(proposal), "p2-format:");
        Case("T.snapshot.originalDigest", good.VerifiedHandle?.RootDigest == digestBeforeEdit && digestBeforeEdit is not null);
        await CheckGraphAndTrust(Case, schema);
        Case("T.snapshot.originalUnchanged", Ids(good.CarryCandidates).SequenceEqual(["Q"]) && good.CreditedCount == 0);
        Console.WriteLine($"C2069 phase=admission elapsedMs={watch.ElapsedMilliseconds} cases={roster.Count}");
        var declared = File.ReadAllLines(Path.Combine(P2Fixtures, "admission-case-ids.txt"));
        rows.ShouldAllPass(declared.Length, declared);
        roster.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(declared)
            .ShouldBeTrue("explicit P2 admission case roster");
    }

    private sealed class FailingStream(byte[] prefix) : MemoryStream(prefix, writable: false)
    {
        public override bool CanSeek => false;
        public override int Read(Span<byte> buffer) => Position < Length ? base.Read(buffer) : throw new IOException("fixture read fault");
    }

    private sealed class CountedStream(byte[] bytes, int maximum) : MemoryStream(bytes, writable: false)
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;
        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length > maximum - BytesRead) throw new InvalidOperationException("read exceeded the allowed lookahead");
            var count = base.Read(buffer);
            BytesRead += count;
            return count;
        }

        public override int ReadByte()
        {
            if (BytesRead >= maximum) throw new InvalidOperationException("read exceeded the allowed lookahead");
            var item = base.ReadByte();
            if (item != -1) BytesRead++;
            return item;
        }
    }

    private sealed class OversizeStream(long count) : MemoryStream
    {
        public override long Length => count;
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("a declared oversize body was read");
    }

    private static async Task CheckGraphAndTrust(Action<string, bool, string> check, JsonNode schema)
    {
        static P2.Decision Inspect(P2World world, P2.Preparation proof, byte[]? bytes = null) =>
            P2.AdmitPrepared(world.Request, world.Chunks,
                () => new MemoryStream(bytes ?? proof.Evidence!.WriteProposal(), writable: false), proof);

        static bool Has(P2.Decision decision, string[] owed, string[] carry) =>
            decision.VerifiedHandle is not null && decision.Owed.Select(row => row.Chunk).SequenceEqual(owed) &&
            decision.CarryCandidates.Select(row => row.Chunk).SequenceEqual(carry) && decision.CreditedCount == 0;
        foreach (var side in new[] { "B", "A" })
        {
            await using var graph = await P2World.Create(historicalSide: side);
            var proof = P2.Prepare(graph.Request, graph.Chunks, graph.Authority());
            var honest = Inspect(graph, proof);
            check("T.graph." + side + ".honest", Has(honest, ["H"], ["Q"]), honest.Reason);
            var disputed = JsonNode.Parse(proof.Evidence!.WriteProposal())!.AsObject();
            var projected = disputed["projections"]!.AsObject();
            var candidate = projected["selection"]!["decisions"]![0]!;
            candidate["disposition"] = "carry-candidate";
            candidate["origin"] = projected["receipts"]![0]!["origin"]!.DeepClone();
            candidate["reasons"]!.AsArray().Clear();
            projected["selection"]!["classes"]!.AsArray().Clear();
            var altered = Inspect(graph, proof, Encoding.UTF8.GetBytes(IndependentEnvelope(schema, disputed, projected).ToJsonString()));
            check(side == "B" ? "T.graph.oldOnly.disputedMode" : "T.graph.newOnly.disputedMode",
                altered.EffectiveMode == "Full" && !altered.CarryCandidates.Any() && altered.Owed.Count() == 2, altered.Reason);
        }

        await using (var ambiguous = await P2World.Create(duplicateSimpleName: true))
        {
            var proof = P2.Prepare(ambiguous.Request, ambiguous.Chunks, ambiguous.Authority());
            var decision = Inspect(ambiguous, proof);
            check("T.graph.qualifiedExpansion", Has(decision, ["H", "Q"], []), decision.Reason);
        }

        await using (var corrupt = await P2World.Create(brokenIndex: true))
        {
            var proof = P2.Prepare(corrupt.Request, corrupt.Chunks, corrupt.Authority());
            var decision = Inspect(corrupt, proof);
            check("T.index.B.regenerationMismatch.mode", decision.EffectiveMode == "Full" &&
                decision.Reason.StartsWith("p2-incomplete:index-regeneration:B", StringComparison.Ordinal), decision.Reason);
        }

        await using (var staleCatalog = await P2World.Create(staleCatalog: true))
        {
            var proof = P2.Prepare(staleCatalog.Request, staleCatalog.Chunks, staleCatalog.Authority());
            var decision = Inspect(staleCatalog, proof);
            check("T.compatibility.B.staleCatalogPin.mode", decision.EffectiveMode == "Full" &&
                decision.Reason.StartsWith("p2-input:compatibility", StringComparison.Ordinal), decision.Reason);
        }

        await using var trust = await P2World.Create(trust: true);
        var heldProof = P2.Prepare(trust.Request, trust.Chunks, trust.Authority());
        var held = Inspect(trust, heldProof);
        check("T.trust.held", Has(held, ["H", "Q"], []) && held.Rows[1].Disposition == "held", held.Reason);
        var released = P2.Prepare(trust.Request, trust.Chunks, trust.Authority(ownExecuted: true));
        var authorized = Inspect(trust, released);
        check("T.trust.released", Has(authorized, ["H"], ["Q"]), authorized.Reason);

        foreach (var (id, proof) in new[]
        {
            ("carriedOwn", P2.Prepare(trust.Request, trust.Chunks, trust.Authority(ownExecuted: false))),
            ("contractDrift", P2.Prepare(trust.Request, trust.Chunks, trust.Authority(ownExecuted: true, contractDrift: true))),
        })
            TrustRefusal(trust, proof, id);

        await using (var candidateOnly = await P2World.Create(trust: true, candidateOnlyTrust: true))
            TrustRefusal(candidateOnly, P2.Prepare(candidateOnly.Request, candidateOnly.Chunks, candidateOnly.Authority(ownExecuted: true)), "candidateOnly");
        await using (var directHit = await P2World.Create(trust: true, directHit: true))
            TrustRefusal(directHit, P2.Prepare(directHit.Request, directHit.Chunks, directHit.Authority(ownExecuted: true)), "directHit");

        void TrustRefusal(P2World world, P2.Preparation proof, string id)
        {
            var honest = Inspect(world, proof);
            check("T.trust." + id + ".honest", Has(honest, ["H", "Q"], []), honest.Reason);
            var variant = JsonNode.Parse(proof.Evidence!.WriteProposal())!.AsObject();
            var projected = variant["projections"]!.AsObject();
            var candidate = projected["selection"]!["decisions"]![1]!;
            candidate["disposition"] = "carry-candidate";
            candidate["origin"] = projected["receipts"]![1]!["origin"]!.DeepClone();
            candidate["reasons"] = new JsonArray(new JsonObject
            {
                ["kind"] = "trust", ["path"] = null, ["side"] = null,
                ["chain"] = new JsonArray("trust-gate-passed:" + P2World.ProducerProject),
            });
            projected["selection"]!["classes"] = new JsonArray(projected["selection"]!["classes"]!.AsArray()
                .Where(item => item!["project"]!.GetValue<string>() != P2World.QuietProject).Select(item => item!.DeepClone()).ToArray());
            var sealedVariant = IndependentEnvelope(schema, variant, projected);
            var refusal = Inspect(world, proof, Encoding.UTF8.GetBytes(sealedVariant.ToJsonString()));
            check("T.trust." + id + ".disputedMode", refusal.EffectiveMode == "Full" &&
                !refusal.CarryCandidates.Any() && refusal.Owed.Count() == 2, refusal.Reason);
        }
    }
}
