# CARD-2069 P2 integrity positive-control candidates

Code and landing owner `61d71670-581e-4328-ab78-dbb3602ee237`. The [verification appendix](../plans/2026-11-26-card-2069-p2-integrity-test-design.md) and the [round 2 supplement](../../investigations/2026-11-26-card-2069-code.md#round-2-verification-supplement) define the paired fixtures. The [mechanical contract](../../mechanical-pc-contract.md) governs binding at the verified landing.

C selects `/*/*/ReleaseImpactEnvelopeTests/P2CanonicalBytes`. T selects `/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse`. G selects `/*/*/ReleaseImpactEnvelopeTests/GraphAndTrustBindingsRefuse`. H selects `/*/*/ReleaseImpactEnvelopeTests/GlobalContractChangesRefuse`. Each variant has one method per phase with exit codes 0/1/0 and executed counts 1/1/1. Inspect the decisive labels as well as the method verdict. Product paths below are relative to `scripts/lib/Antiphon.Nightly/`.

| Variant | Product site | Deliberate replacement | Case |
|---|---|---|---|
| PC-1/a | `ReleaseImpactIntegrity.cs`, `EncodeRoot` | Replace the field lookup with a lookup that swaps only `baselineTree` and `candidateTree`. | C `C.root.bytes`, 1,441 literal bytes differ |
| PC-2/a | `ReleaseImpactIntegrity.cs`, `ProjectionBytes` | Use the `baselineManifest` domain when role is `candidateManifest`. | C `C.manifest.A.sha`, B SHA instead of A SHA |
| PC-3/a | `ReleaseImpactIntegrity.Schema.cs`, `reason` record | Encode `chain` as `Set(Text)` in place of `Sequence(Text)`. | C `C.ordered.chainDigestsDiffer`, becomes false |
| PC-4/a | `ReleaseImpactIntegrity.cs`, `RawSha256` | Remove a carriage-return byte immediately before each newline byte before hashing. | C `C.raw.crlfSha`, LF SHA replaces CRLF SHA |
| PC-5/a | `ReleaseImpactIntegrity.cs`, `ReadObject` | For the `target` record only, assign the last value for a repeated property after the allowed-name check. | C `C.shape.nestedDuplicate.decodeAccepted`, duplicate is accepted |
| PC-6/a | `ReleaseImpactIntegrity.cs`, `CanonicalWriter._aliases` | Change its comparer to `StringComparer.Ordinal`. Keep Unicode normalization. | C `C.keys.foldedPairAccepted`, alias is accepted |
| PC-7/a | `ReleaseImpactIntegrity.cs`, `EncodeRoot` | Accept `antiphon-p2-integrity-v2` in the v1 discriminator comparison. | C `C.version.unknownRecognized`, passes the discriminator |
| PC-8/a | `ReleaseImpactIntegrity.cs`, `EnvelopeLimit` | Add one byte to the constant. The fixture uses its independently fixed limit. | T `T.limit.oneOver.mode`, Scoped |
| PC-9/a | `ReleaseImpactAdmission.cs`, `Admit` | Remove `IOException` from the envelope-open/read catch filter. | T `T.read.returnedFull`, public call throws |
| PC-10/a | `ReleaseImpactIntegrity.cs`, `Decode` | Remove the comparison of the supplied root digest with the computed root bytes. | T `T.seal.mode`, Scoped |
| PC-11/a | `ReleaseImpactAdmission.cs`, `Admit` | Replace the verified return with `Full(discovery, "p2-incomplete:admission")`. | T `T.valid.candidates`, empty |
| PC-12/a | `ReleaseImpactAdmission.Derive.cs`, `Derive` | Remove the ancestry requirement after the commit-type checks. | T `T.context.nonAncestor.mode`, Scoped |
| PC-13/a | `ReleaseImpactAdmission.cs`, `EvidenceSnapshot.Match` | For baseline manifest comparison compute the expected bytes from the independent rows filtered by the paths in the submitted manifest. Retain field-by-field equality for present paths. | T `T.reseal.manifest.B.omittedMember.mode`, Scoped |
| PC-14/a | `ReleaseImpactAdmission.Source.cs`, `ReadSide` | Supply the committed raw index in the regeneration byte comparison in place of `evaluation.RegeneratedIndex`. | G `T.index.B.regenerationMismatch.mode`, Scoped |
| PC-15/a | `ReleaseImpactAdmission.Selection.cs`, `Hits` | Keep ownership validation and suppress B-side deletion hits when adding direct edges to the hit set. | G `T.graph.oldOnly.disputedMode`, H omitted |
| PC-16/a | `ReleaseImpactAdmission.Selection.cs`, `Select` | Compare the B/A case digests in the equivalence predicate in place of `RcChunkDigest.Compare`. | T `T.targetInputs.sameCasesDirtyInputs.disputedMode`, Q eligible |
| PC-17/a | `ReleaseImpactAdmission.cs`, `Admit` | When independent comparison refuses the receipts role, substitute the addressed clean proposal artifact for missing custody and return its asserted origin as eligible. Preserve the Full result for a missing copy. | T `T.receipts.copyOnly.disputedMode`, Scoped with a copied original |
| PC-18/a | `ReleaseImpactAdmission.Receipts.cs`, `ReadReceipt` | Replace ordered multiset equality with containment of each required UID among observed passed cases. | T `T.receipts.duplicatePass.disputedMode`, Q eligible |
| PC-19/a | `ReleaseImpactAdmission.cs`, `EvidenceSnapshot.Match` | Permit an already-seen proposal for the same candidate on a revision mismatch, bypassing the obligations mismatch. Keep the broader candidate identity guard. | T `T.revision.stale.mode`, Scoped |
| PC-20/a | `ReleaseImpactAdmission.Selection.cs`, `ComputeTrust` | Read grants from `policyA` and skip the raw policy-drift veto. Preserve both raw policy evidence projections. | G `T.trust.candidateOnly.disputedMode`, consumer eligible |
| PC-21/a | `ReleaseImpactAdmission.Selection.cs`, `ComputeTrust` | For an otherwise clean carried own result, map both disposition and result to an executed pass in the `RcOwnResult` projection. Preserve the actual carried receipt. | G `T.trust.carriedOwn.disputedMode`, consumer eligible |
| PC-22/a | `ReleaseImpactAdmission.cs`, `Admit` | Replace the current-discovery and roster reconciliation inputs with the proposal’s current cases. Preserve the execute-only Q receipt state. | T `T.missing.currentCase.mode`, Scoped with q2 missing |
| PC-23/a | `ReleaseImpactAdmission.cs`, `Admit` | Remove the completeness check after independent projection equality. | T `T.incomplete.agreedFalse.mode`, Scoped. H `T.incomplete.globalContract` is a separate cross-check |
| PC-24/a | `ReleaseImpactAdmission.cs`, `Admit` | Bypass `snapshot.Match(proposed)` and its refusal, retaining the strict wire and completeness checks. | T `T.reseal.selection.reasonOnly.mode` and every coherent reseal, Scoped |
| PC-25/a | `ReleaseImpactAdmission.cs`, `Admit` | In the semantic mismatch return, preserve the computed rows in the refused `Decision` instead of rebuilding from discovery. | T `T.selection.lateRefusal.candidates`, Q leaks |
| PC-26/a | `ReleaseImpactAdmission.cs`, `Decision` constructor | Assign `Rows = rows.ToArray()` in place of the frozen collection. | T `T.snapshot.eligibleAfterCallerEdit`, H replaces Q |
| PC-27/a | `ReleaseImpactAdmission.cs`, `Decision` constructor | Emit `carried` for rows whose disposition is `carry-candidate`. | T `T.valid.creditedCount`, becomes 1 |
| PC-28/a | `ReleaseImpactIntegrity.cs`, `ProofObjects.Get` | Preserve length validation and remove the proof body SHA comparison. | T `T.address.sameMeaningChangedBytes.mode`, Scoped |
| PC-29/a | `ReleaseImpactAdmission.Derive.cs`, `ReadTool` | Use the reviewed file pins without checking the actual installed file against its pin. Keep all context and tool source commit checks. | T `T.algorithm.actualInstall.mode`, Scoped |
| PC-30/a | `ReleaseImpactAdmission.Receipts.cs`, `ReadReceipt` | Keep the verified custody read and open the artifact again for the TRX parser. | T `T.snapshot.artifactOpenCount` becomes 2 and `T.snapshot.originalEligible` becomes false |
| PC-31/a | `RcTrustedProjectGate.cs`, `GateFailure` | Remove the hash inequality while preserving extractor version equality and receipt existence. | G `T.trust.contractDrift.disputedMode`, consumer eligible |
| PC-32/a | `ReleaseImpactAdmission.Selection.cs`, `ComputeTrust` and `Select` | Suppress the direct hit in the gate input and clear the corresponding direct selection reason after a passed gate. Keep the non-gate reasons and typed graph projection. | G `T.trust.directHit.disputedMode`, directly hit consumer eligible |
| PC-33/a | `ReleaseImpactAdmission.cs`, verified result construction | Stamp accepted carried-through provenance with the comparison run/attempt identity in place of the execution pin. | T `T.receipts.originChain.validOrigin`, original B0 replaced with B |
| PC-34/a | `RcChunkReceipt.cs`, `Reusable` | Skip the exit-code refusal and retain exact scope and case checks. | T `T.receipts.failedExit.disputedMode`, Q eligible |
| PC-35/a | `TestSelection.cs`, `Contract` | Remove only the execution-policy-to-catalog hash comparison. | G `T.compatibility.B.staleCatalogPin.mode`, Scoped |
| PC-36/a | `ReleaseImpactAdmission.Derive.cs`, `Derive` | Write empty selection global reasons. Keep the compatibility reasons and Full fallback. | H `T.incomplete.globalContract`, Scoped with Q eligible |
| PC-37/a | `ReleaseImpactAdmission.cs`, `Admit` | Remove `SameDiscovery` reconciliation. | T `T.discovery.changedAtAdmit`, Scoped without newly discovered q3 |
| PC-38/a | `ReleaseImpactAdmission.Receipts.cs`, `Custody.Read` | Keep the raw byte-count check and remove the raw SHA comparison. | T `T.receipts.custody.rawTrxSha`, Scoped with Q eligible, and `T.receipts.custody.rawInventorySha`, wrong refusal reason |
| PC-39/a | `ReleaseImpactAdmission.Receipts.cs`, `ReadReceipt` | Remove only the original input digest comparison. | T `T.receipts.originChain.changedInputs`, Q eligible |
| PC-40/a | `ReleaseImpactAdmission.Receipts.cs`, `ReadReceipt` | Require an original commit object and skip the inventory ancestry check. | T `T.receipts.originChain.nonAncestor`, Scoped with Q eligible |

All variants are pending SourceLanding Mutation. At binding, verify exact compiling replacements and the aggregate assertion failure set against the landed preimage. PC-17, PC-19, PC-22, PC-25, and PC-32 need particular attention to overlapping checks at the decision boundary. Preserve the independently approved twin and the exact one-method filters.

## Exact replacement payloads

These are candidate edits at the Code preimage. The landing binder records the actual preimage and predicted mutant hashes after confirming the landed source. Each row names the complete allowed file set.

```json
[
  {
    "id": "PC-1",
    "variant": "a",
    "method": "P2CanonicalBytes",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/P2CanonicalBytes",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs",
        "before": "var value = root[field];",
        "after": "var value = root[field switch { \"baselineTree\" => \"candidateTree\", \"candidateTree\" => \"baselineTree\", _ => field }];",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-2",
    "variant": "a",
    "method": "P2CanonicalBytes",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/P2CanonicalBytes",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs",
        "before": "writer.String(Format + \"/\" + role);",
        "after": "writer.String(Format + \"/\" + (role == \"candidateManifest\" ? \"baselineManifest\" : role));",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-3",
    "variant": "a",
    "method": "P2CanonicalBytes",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/P2CanonicalBytes",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.Schema.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.Schema.cs",
        "before": "(\"chain\", Sequence(Text))",
        "after": "(\"chain\", Set(Text))",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-4",
    "variant": "a",
    "method": "P2CanonicalBytes",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/P2CanonicalBytes",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs",
        "before": "public static string RawSha256(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));",
        "after": "public static string RawSha256(ReadOnlySpan<byte> bytes)\n    {\n        using var changed = new MemoryStream();\n        for (var index = 0; index < bytes.Length; index++)\n            if (!(bytes[index] == 13 && index + 1 < bytes.Length && bytes[index + 1] == 10))\n                changed.WriteByte(bytes[index]);\n        return Convert.ToHexStringLower(SHA256.HashData(changed.ToArray()));\n    }",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-5",
    "variant": "a",
    "method": "P2CanonicalBytes",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/P2CanonicalBytes",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs",
        "before": "            if (!fields.Contains(member.Name, StringComparer.Ordinal) || !members.TryAdd(member.Name, member.Value))\n                throw Invalid(label);",
        "after": "            if (!fields.Contains(member.Name, StringComparer.Ordinal)) throw Invalid(label);\n            if (label == \"target\") members[member.Name] = member.Value;\n            else if (!members.TryAdd(member.Name, member.Value)) throw Invalid(label);",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-6",
    "variant": "a",
    "method": "P2CanonicalBytes",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/P2CanonicalBytes",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs",
        "before": "private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);",
        "after": "private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-7",
    "variant": "a",
    "method": "P2CanonicalBytes",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/P2CanonicalBytes",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs",
        "before": "RequireText(root[\"format\"], \"format\", value => value == Format);",
        "after": "RequireText(root[\"format\"], \"format\", value => value == Format || value == \"antiphon-p2-integrity-v2\");",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-8",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs",
        "before": "public const int EnvelopeLimit = 8 * 1024 * 1024;",
        "after": "public const int EnvelopeLimit = 8 * 1024 * 1024 + 1;",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-9",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "catch (Exception error) when (error is IOException or UnauthorizedAccessException)",
        "after": "catch (UnauthorizedAccessException)",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-10",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs",
        "before": "if (rootDigest != RawSha256(rootBytes)) throw Invalid(\"rootDigest\");",
        "after": "_ = RawSha256(rootBytes);",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-11",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "return new Decision(new Verified(request, proposed.RootDigest), snapshot.Rows, false, \"\");",
        "after": "return Full(discovery, \"p2-incomplete:admission\");",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-12",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Derive.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Derive.cs",
        "before": "Require(git.Ancestor(request.BaselineSha, request.CandidateSha), \"p2-context:ancestry\");",
        "after": "_ = git.Ancestor(request.BaselineSha, request.CandidateSha);",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-13",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "            foreach (var role in ReleaseImpactIntegrity.StructuredRoles)\n                if (!_canonical[role].AsSpan().SequenceEqual(ReleaseImpactIntegrity.ProjectionBytes(role, proposed.Projections[role], FoldedPaths)))\n                    return RoleFamily(role) + \":\" + role;",
        "after": "            foreach (var role in ReleaseImpactIntegrity.StructuredRoles)\n            {\n                var expected = _canonical[role];\n                if (role == \"baselineManifest\")\n                {\n                    var admitted = proposed.Projections[role].EnumerateArray().Select(row => row.GetProperty(\"path\").GetString()).ToHashSet(StringComparer.Ordinal);\n                    var filtered = _projections[role].EnumerateArray().Where(row => admitted.Contains(row.GetProperty(\"path\").GetString())).ToArray();\n                    expected = ReleaseImpactIntegrity.ProjectionBytes(role, JsonSerializer.SerializeToElement(filtered), FoldedPaths);\n                }\n                if (!expected.AsSpan().SequenceEqual(ReleaseImpactIntegrity.ProjectionBytes(role, proposed.Projections[role], FoldedPaths)))\n                    return RoleFamily(role) + \":\" + role;\n            }",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-14",
    "variant": "a",
    "method": "GraphAndTrustBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/GraphAndTrustBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Source.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Source.cs",
        "before": "Require(evaluation.RegeneratedIndex.AsSpan().SequenceEqual(raw), \"p2-incomplete:index-regeneration:\" + side);",
        "after": "Require(raw.AsSpan().SequenceEqual(raw), \"p2-incomplete:index-regeneration:\" + side);",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-15",
    "variant": "a",
    "method": "GraphAndTrustBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/GraphAndTrustBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs",
        "before": "foreach (var edge in direct) hits.Add(new Hit(change, edge));",
        "after": "foreach (var edge in direct) if (change.Side != \"B\" || change.Status != \"D\") hits.Add(new Hit(change, edge));",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-16",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs",
        "before": "var equivalent = entry.Baseline is not null && !RcChunkDigest.Compare(entry.Baseline, entry.Candidate).Dirty;",
        "after": "var equivalent = entry.Baseline is not null && entry.Baseline.CaseDigest == entry.Candidate.CaseDigest;",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-17",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "if (mismatch is not null) return Full(discovery, mismatch);",
        "after": "if (mismatch == \"p2-receipt:receipts\" && snapshot.OpenProof is not null)\n            {\n                foreach (var row in proposed.Projections[\"selection\"].GetProperty(\"decisions\").EnumerateArray())\n                {\n                    if (row.GetProperty(\"disposition\").GetString() != \"carry-candidate\") continue;\n                    var suppliedOrigin = JsonSerializer.Deserialize<Origin>(row.GetProperty(\"origin\").GetRawText(), P2Json);\n                    if (suppliedOrigin is null) continue;\n                    using var copied = snapshot.OpenProof(suppliedOrigin.EvidenceDigest);\n                    var copyBytes = ReleaseImpactIntegrity.ReadBounded(copied, ReleaseImpactIntegrity.ProofLimit, \"receipts\");\n                    if (ReleaseImpactIntegrity.RawSha256(copyBytes) != suppliedOrigin.EvidenceDigest) continue;\n                    var id = row.GetProperty(\"chunk\").GetString();\n                    return new Decision(new Verified(request, proposed.RootDigest), snapshot.Rows.Select(expected =>\n                        expected.Chunk == id ? expected with { Disposition = \"carry-candidate\", Origin = suppliedOrigin } : expected),\n                        false, \"\");\n                }\n            }\n            if (mismatch is not null) return Full(discovery, mismatch);",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-18",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Receipts.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Receipts.cs",
        "before": "var exact = observed.Select(c => (c.Assembly, c.CaseKey, c.Class, c.Method))\n                .OrderBy(key => key, CaseTupleComparer.Instance)\n                .SequenceEqual(chunk.Cases.Select(c => (c.Assembly, c.CaseUid, c.Class, c.Method))\n                    .OrderBy(key => key, CaseTupleComparer.Instance));",
        "after": "var exact = chunk.Cases.All(required => observed.Any(actual => actual.CaseKey == required.CaseUid && actual.Outcome == \"Passed\"));",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-19",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "if (mismatch is not null) return Full(discovery, mismatch);",
        "after": "if (mismatch == \"p2-context:revision\" &&\n                proposed.Root[\"candidateSha\"].GetString() == request.CandidateSha &&\n                proposed.Root[\"runId\"].GetString() == request.RunId)\n                return new Decision(new Verified(request, proposed.RootDigest), snapshot.Rows, false, \"\");\n            if (mismatch is not null) return Full(discovery, mismatch);",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-20",
    "variant": "a",
    "method": "GraphAndTrustBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/GraphAndTrustBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs",
        "before": "RcTrustedProjectGate.Entries(policyB, refusal)",
        "after": "RcTrustedProjectGate.Entries(policyA, refusal)",
        "matchCount": 1
      },
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs",
        "before": "if (policyChanged) grants = [];",
        "after": "_ = policyChanged;",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-21",
    "variant": "a",
    "method": "GraphAndTrustBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/GraphAndTrustBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs",
        "before": "Chunk = row.Chunk, Disposition = row.Disposition, Result = row.Reason is null ? row.Result : \"unproven\",",
        "after": "Chunk = row.Chunk, Disposition = row.Disposition == \"carried\" ? \"executed\" : row.Disposition,\n            Result = row.Reason is null or \"owned-run-not-executed\" ? row.Result : \"unproven\",",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-22",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "if (mismatch is not null) return Full(discovery, mismatch);",
        "after": "if (mismatch == \"p2-selection:universe\" && snapshot.Rows.All(row => row.Disposition == \"execute\"))\n            {\n                var submittedCases = JsonSerializer.Deserialize<Case[]>(proposed.Projections[\"universe\"].GetRawText(), P2Json) ?? [];\n                return new Decision(new Verified(request, proposed.RootDigest), snapshot.Rows.Select(row =>\n                    row with { Cases = Freeze(row.Cases.Where(submittedCases.Contains)) }), false, \"\");\n            }\n            if (mismatch is not null) return Full(discovery, mismatch);",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-23",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "if (!snapshot.Complete) return Full(discovery, \"p2-incomplete:selection\");",
        "after": "_ = snapshot.Complete;",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-24",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "var mismatch = snapshot.Match(proposed);",
        "after": "string? mismatch = null;",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-25",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "if (mismatch is not null) return Full(discovery, mismatch);",
        "after": "if (mismatch == \"p2-selection:selection\" &&\n                proposed.Projections[\"selection\"].GetProperty(\"decisions\")[0].GetProperty(\"disposition\").GetString() == \"carry-candidate\")\n                return new Decision(null, snapshot.Rows, false, mismatch);\n            if (mismatch is not null) return Full(discovery, mismatch);",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-26",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "Rows = Freeze(rows);",
        "after": "Rows = rows.ToArray();",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-27",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "Rows = Freeze(rows);",
        "after": "Rows = Freeze(rows.Select(row => row.Disposition == \"carry-candidate\" ? row with { Disposition = \"carried\" } : row));",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-28",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactIntegrity.cs",
        "before": "if ((ulong)bytes.Length != byteCount || RawSha256(bytes) != sha) throw Invalid(role);",
        "after": "if ((ulong)bytes.Length != byteCount) throw Invalid(role);",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-29",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Derive.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Derive.cs",
        "before": "            using var stream = tool.OpenInstalledFile(file.Path);\n            var bytes = ReleaseImpactIntegrity.ReadBounded(stream, (int)file.Size, \"algorithm-size\");\n            Require((ulong)bytes.Length == file.Size && ReleaseImpactIntegrity.RawSha256(bytes) == file.Digest\n                && ReleaseImpactEvidence.BlobId(bytes, 40) == file.BlobId, \"p2-input:actual-installation\");",
        "after": "            _ = file.Digest;",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-30",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Receipts.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Receipts.cs",
        "before": "using var evidence = new MemoryStream(custody.Read(pin.Evidence), writable: false);",
        "after": "_ = custody.Read(pin.Evidence);\n            using var evidence = pin.Evidence.Open();",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-31",
    "variant": "a",
    "method": "GraphAndTrustBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/GraphAndTrustBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/RcTrustedProjectGate.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/RcTrustedProjectGate.cs",
        "before": "if (b[0].ExtractorVersion != a[0].ExtractorVersion || b[0].ContractHash != a[0].ContractHash) return \"contract-drift\";",
        "after": "if (b[0].ExtractorVersion != a[0].ExtractorVersion) return \"contract-drift\";",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-32",
    "variant": "a",
    "method": "GraphAndTrustBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/GraphAndTrustBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs",
        "before": "DirectHit = direct.Any(hit => Applies(hit.Edge, chunk)),",
        "after": "DirectHit = false,",
        "matchCount": 1
      },
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Selection.cs",
        "before": "if (Applies(edge, chunk))",
        "after": "if (Applies(edge, chunk) && !gated.Reasons.Any(reason => reason.StartsWith(\"trust-gate-passed:\", StringComparison.Ordinal)))",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-33",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "return new Decision(new Verified(request, proposed.RootDigest), snapshot.Rows, false, \"\");",
        "after": "var comparison = proposed.Projections[\"inventory\"];\n            return new Decision(new Verified(request, proposed.RootDigest), snapshot.Rows.Select(row =>\n                row.Origin is { } origin && origin.Sha != request.BaselineSha ? row with\n                {\n                    Origin = origin with { Sha = request.BaselineSha, RunId = comparison.GetProperty(\"runId\").GetString()!,\n                        Attempt = comparison.GetProperty(\"attempt\").GetUInt32() },\n                } : row), false, \"\");",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-34",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/RcChunkReceipt.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/RcChunkReceipt.cs",
        "before": "            if (!row.Contains(\"exitCode\") || PsOps.ToInt(row[\"exitCode\"]) != 0) reasons.Add(\"exit:\" + PsValue.Str(row.Contains(\"exitCode\") ? row[\"exitCode\"] : \"absent\"));",
        "after": "            _ = row[\"exitCode\"];",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-35",
    "variant": "a",
    "method": "GraphAndTrustBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/GraphAndTrustBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/TestSelection.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/TestSelection.cs",
        "before": "if (policy.SelectionCatalog?.Hash != catalogHash) throw new TestSelectionRefusal(\"catalog-pin-mismatch\");",
        "after": "_ = catalogHash;",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-36",
    "variant": "a",
    "method": "GlobalContractChangesRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/GlobalContractChangesRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Derive.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Derive.cs",
        "before": "globalReasons, incompleteReasons,",
        "after": "globalReasons = Array.Empty<string>(), incompleteReasons,",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-37",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.cs",
        "before": "if (!SameDiscovery(discovery, snapshot.Chunks)) return Full(discovery, \"p2-selection:roster\");",
        "after": "",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-38",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Receipts.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Receipts.cs",
        "before": "Require((ulong)bytes.Length == artifact.ByteCount &&\n                    ReleaseImpactIntegrity.RawSha256(bytes) == artifact.Digest, \"p2-receipt:proof-bytes\");",
        "after": "Require((ulong)bytes.Length == artifact.ByteCount, \"p2-receipt:proof-bytes\");",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-39",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Receipts.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Receipts.cs",
        "before": "else if (pin.InputDigest != expectedInput) absence = \"origin-inputs-changed\";",
        "after": "",
        "matchCount": 1
      }
    ]
  },
  {
    "id": "PC-40",
    "variant": "a",
    "method": "TamperedBindingsRefuse",
    "filter": "/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse",
    "allowedFiles": [
      "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Receipts.cs"
    ],
    "edits": [
      {
        "file": "scripts/lib/Antiphon.Nightly/ReleaseImpactAdmission.Receipts.cs",
        "before": "Require(git.Commit(pin.Sha) && git.Ancestor(pin.Sha, inventory.Sha), \"p2-receipt:origin-ancestry\");",
        "after": "Require(git.Commit(pin.Sha), \"p2-receipt:origin-ancestry\");",
        "matchCount": 1
      }
    ]
  }
]
```

## Round 3 mutation handoff

The original Code and landing owner is `61d71670-581e-4328-ab78-dbb3602ee237`. The prior candidate register remains the source of the 40 unbound single-variant PCs. This addendum records the witness locations after the timeout split. Mutation must bind the landed preimages and verify each exact method with separate baseline, deliberate defect, restoration and green evidence.

| Existing variants | Exact method on ReleaseImpactEnvelopeTests | Pending coverage |
|---|---|---|
| PC-1/a, PC-2/a, PC-3/a, PC-4/a, PC-5/a, PC-6/a, PC-7/a | P2CanonicalBytes | Original decisive labels |
| PC-8/a, PC-9/a, PC-10/a, PC-11/a, PC-12/a, PC-13/a, PC-16/a, PC-17/a, PC-18/a, PC-19/a, PC-22/a, PC-23/a, PC-24/a, PC-25/a, PC-26/a, PC-27/a, PC-28/a, PC-29/a, PC-30/a, PC-33/a, PC-34/a, PC-37/a, PC-38/a, PC-39/a, PC-40/a | TamperedBindingsRefuse | Original decisive labels. PC-23/a uses T.incomplete.agreedFalse.mode here. Its selector-drift cross-check moved to GlobalContractChangesRefuse. |
| PC-14/a, PC-15/a, PC-20/a, PC-21/a, PC-31/a, PC-32/a, PC-35/a | GraphAndTrustBindingsRefuse | Original decisive labels |
| PC-36/a | GlobalContractChangesRefuse | T.incomplete.globalContract |

Review 9a5bb8c1 observed the following four defects. They remain pending deliberate execution on the new witnesses. Each method filter is `/*/*/ReleaseImpactEnvelopeTests/<method>`. The original recipes are in `/work/code-evidence/9a5bb8c1-review/review-mutants-and-probes.patch`. Review 801d2b17 stored the round 2 and round 3 recipes in `/work/code-evidence/801d2b17-review/review-mutants.patch`.

| New variant | Production site and edit | Witness and expected failure |
|---|---|---|
| PC-41/a (n1) | ReleaseImpactAdmission.Derive.cs, Derive. Enumerate only runtimeHash and selectorHash. | GlobalContractChangesRefuse. T.incomplete.globalPolicyContract and T.incomplete.globalCatalogContract return Scoped with Q eligible. Their .reasons rows also fail. |
| PC-42/a (n2) | ReleaseImpactAdmission.Derive.cs, Derive. Clear globalReasons when its first item is anything other than contract-changed:selectorHash. | GlobalContractChangesRefuse. Same policy/catalog rows and exposure as n1. |
| PC-43/a (n7) | ReleaseImpactAdmission.Receipts.cs, ReadReceipt. Accept an origin if the inventory SHA is also its ancestor (reverse direction). | TamperedBindingsRefuse. T.receipts.originChain.descendant admits the candidate execution as Q's origin. |
| PC-44/a (n8) | ReleaseImpactAdmission.cs, SameDiscovery. Compare ordered CaseUid strings in place of complete Case records. | TamperedBindingsRefuse. T.discovery.renamedAtAdmit admits the old Q method. |

An isolated catalog omission can still force Full because a valid catalog repin changes policyHash. T.incomplete.globalCatalogContract.reasons requires the catalog reason as well. An isolated runtimeHash omission has no observable admission effect for a genuine global.json edit. The pinned Git diff reaches RcTrustedProjectGate's global-input veto before the contract comparison. T.incomplete.globalRuntimeContract verifies that the genuine SDK pin changes only runtimeHash and refuses with p2-incomplete:trust-global, the exact roster owed and zero carry.
