// The PowerShell readings the deploy-server2 command keeps (unit 65 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// W8 slice C): scripts/deploy-server2.ps1 decided on values as PowerShell 7 read them, so the command reads them the same way.
//
// - ConvertFrom-Json (Newtonsoft under PowerShell 7.6): an object is a PlainObject (names compared ignoring case; an empty name or
//   a name repeated in any case refuses), an array is object[], a whole number is Int64 (BigInteger past it), any other number
//   Double, a string shaped like an ISO date-time a DateTime (RoundtripKind: an offset becomes local time, as Newtonsoft read it),
//   true, false and null as themselves. Comments and trailing commas are allowed. The pipeline form (no -NoEnumerate) hands a
//   top-level array on element by element: none is null, one is that element.
// - ConvertTo-Json: compact or indented (two spaces), a DateTime in round-trip form, a whole Double with ".0", and past -Depth
//   an object or array written as its [string] text, as PowerShell truncated it.
// - -eq and -ne ignore case, -ceq and -cne do not, the left operand's type deciding (PlainValues.Equal); a value's truth is
//   LanguagePrimitives.IsTrue; [string] is LanguagePrimitives' invariant text (a DateTime reads MM/dd/yyyy HH:mm:ss).
// - $o.Name: an object's value (ignoring case), member enumeration over an array.
//
// It also holds the functions deploy-server2.ps1 dot-sourced from scripts/c590-real.ps1 (Assert-C994RawManifest,
// ConvertTo-C994RetiredInstant, Assert-C994BridgeContext), scripts/c1008-host-proof.ps1 (Test-C1008SafeAuditDiagnosis) and
// scripts/c1609-read-packet.ps1 (New-C1609RetirementReadPacket). Those scripts stay for their W10 callers (verify-docker-stack.ps1's
// live bridge, c1609-manual.ps1); these are the command's copies, with the same refusals. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A value ConvertFrom-Json could not read (its message is PowerShell's lead-in and the reader's reason).</summary>
    public sealed class RollingJsonException : Exception
    {
        /// <summary>The reason.</summary>
        public RollingJsonException(string message) : base("Conversion from JSON failed with error: " + message) { }
    }

    /// <summary>PowerShell 7's readings of JSON values, as deploy-server2.ps1 made them.</summary>
    public static class RollingPs
    {
        private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

        // ---- ConvertFrom-Json ----

        /// <summary>ConvertFrom-Json -NoEnumerate: the value of the text. Throws RollingJsonException for text it cannot read.</summary>
        public static object Parse(string text)
        {
            if (text == null) throw new RollingJsonException("the input is null");
            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    MaxDepth = 1024,
                });
                if (!reader.Read()) throw new RollingJsonException("the input is empty");
                var value = Read(ref reader);
                if (reader.Read()) throw new RollingJsonException("additional text after the value");
                return value;
            }
            catch (JsonException e) { throw new RollingJsonException(e.Message); }
            catch (InvalidOperationException e) { throw new RollingJsonException(e.Message); }
            catch (FormatException e) { throw new RollingJsonException(e.Message); }
        }

        /// <summary>$text | ConvertFrom-Json, assigned: a top-level array is enumerated (none: null; one: the element).</summary>
        public static object ParseEnumerated(string text)
        {
            var value = Parse(text);
            if (value is object[] items)
            {
                if (items.Length == 0) return null;
                if (items.Length == 1) return items[0];
            }
            return value;
        }

        private static object Read(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    {
                        var result = new PlainObject();
                        while (true)
                        {
                            if (!reader.Read()) throw new RollingJsonException("unterminated object");
                            if (reader.TokenType == JsonTokenType.EndObject) return result;
                            var name = reader.GetString();
                            if (string.IsNullOrEmpty(name)) throw new RollingJsonException("a property name is empty");
                            if (result.Has(name)) throw new RollingJsonException("the key '" + name + "' is repeated");
                            if (!reader.Read()) throw new RollingJsonException("unterminated object");
                            result.Add(name, Read(ref reader));
                        }
                    }
                case JsonTokenType.StartArray:
                    {
                        var items = new List<object>();
                        while (true)
                        {
                            if (!reader.Read()) throw new RollingJsonException("unterminated array");
                            if (reader.TokenType == JsonTokenType.EndArray) return items.ToArray();
                            items.Add(Read(ref reader));
                        }
                    }
                case JsonTokenType.String:
                    {
                        var text = reader.GetString();
                        if (NightlyJson.ReadsAsDate(text) && !text.StartsWith("/Date(", StringComparison.Ordinal)
                            && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                            return date;
                        return text;
                    }
                case JsonTokenType.Number:
                    {
                        var raw = reader.ValueSpan;
                        if (raw.IndexOfAny((byte)'.', (byte)'e', (byte)'E') >= 0) return reader.GetDouble();
                        if (reader.TryGetInt64(out var whole)) return whole;
                        return BigInteger.Parse(Encoding.ASCII.GetString(raw), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                    }
                case JsonTokenType.True: return true;
                case JsonTokenType.False: return false;
                case JsonTokenType.Null: return null;
                default: throw new RollingJsonException("unexpected token " + reader.TokenType);
            }
        }

        // ---- ConvertTo-Json ----

        /// <summary>ConvertTo-Json -Compress -Depth depth.</summary>
        public static string Json(object value, int depth = 2) => Write(value, depth, false);

        /// <summary>ConvertTo-Json -Depth depth (indented by two spaces).</summary>
        public static string JsonIndented(object value, int depth = 2) => Write(value, depth, true);

        private static string Write(object value, int depth, bool indented)
        {
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = Encoder, MaxDepth = 4096 }))
            {
                WriteValue(writer, value, 0, depth);
            }
            var text = Encoding.UTF8.GetString(stream.ToArray());
            return indented ? text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) : text;
        }

        private static void WriteValue(Utf8JsonWriter writer, object value, int level, int depth)
        {
            switch (value)
            {
                case null: writer.WriteNullValue(); return;
                case string s: writer.WriteStringValue(s); return;
                case bool b: writer.WriteBooleanValue(b); return;
                case int n: writer.WriteNumberValue(n); return;
                case long n: writer.WriteNumberValue(n); return;
                case BigInteger n: writer.WriteRawValue(n.ToString(CultureInfo.InvariantCulture), true); return;
                case double n:
                    {
                        if (double.IsNaN(n) || double.IsInfinity(n)) { writer.WriteStringValue(n.ToString(CultureInfo.InvariantCulture)); return; }
                        var text = n.ToString("R", CultureInfo.InvariantCulture);
                        if (text.IndexOfAny(new[] { '.', 'E', 'e' }) < 0) text += ".0";
                        writer.WriteRawValue(text, true);
                        return;
                    }
                case DateTime d: writer.WriteStringValue(d); return;
                case DateTimeOffset d: writer.WriteStringValue(d); return;
            }
            if (level > depth)
            {
                writer.WriteStringValue(Str(value));
                return;
            }
            if (value is PlainObject plain)
            {
                writer.WriteStartObject();
                foreach (var entry in plain.Entries)
                {
                    writer.WritePropertyName(entry.Key);
                    WriteValue(writer, entry.Value, level + 1, depth);
                }
                writer.WriteEndObject();
                return;
            }
            if (value is IDictionary dictionary)
            {
                writer.WriteStartObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    writer.WritePropertyName(Str(entry.Key));
                    WriteValue(writer, entry.Value, level + 1, depth);
                }
                writer.WriteEndObject();
                return;
            }
            if (value is IEnumerable items)
            {
                writer.WriteStartArray();
                foreach (var item in items) WriteValue(writer, item, level + 1, depth);
                writer.WriteEndArray();
                return;
            }
            writer.WriteStringValue(Str(value));
        }

        /// <summary>$value | ConvertTo-Json -Depth depth | ConvertFrom-Json: the deep copy the script made.</summary>
        public static object Copy(object value, int depth) => ParseEnumerated(Json(value, depth));

        // ---- values ----

        /// <summary>$o.Name: an object's value (ignoring case), member enumeration over an array, else null.</summary>
        public static object Prop(object o, string name)
        {
            switch (o)
            {
                case null: return null;
                case PlainObject plain: return plain[name];
                case object[] items:
                    {
                        var found = new List<object>();
                        foreach (var item in items)
                        {
                            if (item is PlainObject p && p.TryGet(name, out var v))
                            {
                                // Member enumeration unrolls an array value and keeps a null one.
                                if (v is object[] inner) found.AddRange(inner);
                                else found.Add(v);
                            }
                            else if (item is object[]) { var nested = Prop(item, name); if (nested != null) found.Add(nested); }
                        }
                        if (found.Count == 0) return null;
                        return found.Count == 1 ? found[0] : found.ToArray();
                    }
                default: return null;
            }
        }

        /// <summary>$o.PSObject.Properties.Name for a custom object (empty for anything else).</summary>
        public static string[] Names(object o) => o is PlainObject plain ? plain.Names.ToArray() : Array.Empty<string>();

        /// <summary>$o.PSObject.Properties.Name -contains name (ignoring case).</summary>
        public static bool Has(object o, string name) => Names(o).Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>$o.PSObject.Properties.Name -ccontains name.</summary>
        public static bool HasExact(object o, string name) => Names(o).Any(n => string.Equals(n, name, StringComparison.Ordinal));

        /// <summary>left -eq right.</summary>
        public static bool Eq(object left, object right) => PlainValues.Equal(left, right, true);

        /// <summary>left -ceq right.</summary>
        public static bool Ceq(object left, object right) => PlainValues.Equal(left, right, false);

        /// <summary>The value's truth (if ($o), -not $o).</summary>
        public static bool True(object o) => PlainValues.IsTrue(o);

        /// <summary>[string]$o.</summary>
        public static string Str(object o) => PlainValues.Str(o);

        /// <summary>$o -is [int] -or $o -is [long].</summary>
        public static bool IsInt(object o) => o is int || o is long;

        /// <summary>A whole number's value (IsInt).</summary>
        public static long Long(object o) => o is int i ? i : (long)o;

        /// <summary>$o -cmatch pattern ([string] of the value, case-sensitive).</summary>
        public static bool CMatch(object o, string pattern) => Regex.IsMatch(Str(o), pattern);

        /// <summary>$o -match pattern (ignoring case).</summary>
        public static bool Match(object o, string pattern) => Regex.IsMatch(Str(o), pattern, RegexOptions.IgnoreCase);

        /// <summary>value -cin set.</summary>
        public static bool CIn(object value, params string[] set) => set.Any(s => Ceq(s, value));

        /// <summary>value -in set.</summary>
        public static bool In(object value, params string[] set) => set.Any(s => Eq(s, value));

        /// <summary>value -lt number for a whole number or a double; anything else compares as PowerShell would not reach here.</summary>
        public static bool LessThan(object value, long number) => value is double d ? d < number : Long(value) < number;

        /// <summary>@($o).Count.</summary>
        public static int Count(object o) => o == null ? 0 : o is object[] items ? items.Length : 1;

        /// <summary>@($o).</summary>
        public static object[] Items(object o) => o == null ? Array.Empty<object>() : o is object[] items ? items : new[] { o };

        /// <summary>A custom object from name/value pairs, in order ([pscustomobject]@{...} or [ordered]@{...}).</summary>
        public static PlainObject Object(params (string Name, object Value)[] entries) => new PlainObject(entries);

        /// <summary>The culture-free sort Sort-Object gave the lower-case ids and names the script sorted.</summary>
        public static IEnumerable<string> Sorted(IEnumerable<string> values) => values.OrderBy(v => v, StringComparer.Create(CultureInfo.CurrentCulture, true));

        // ---- scripts/c590-real.ps1 ----

        /// <summary>
        /// Assert-C994RawManifest: the raw manifest text (System.Text.Json, before PowerShell's date reading) has no repeated name
        /// in any object, and a top-level tempContainerCleanup's retiredAt is a string ConvertTo-C994RetiredInstant admits. Anything
        /// else, an unreadable text among it, throws TempContainerContextInvalid.
        /// </summary>
        public static void AssertC994RawManifest(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
                AssertUnique(document.RootElement);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (!string.Equals(property.Name, "tempContainerCleanup", StringComparison.Ordinal)) continue;
                        var stamp = property.Value.GetProperty("retiredAt");
                        if (stamp.ValueKind != JsonValueKind.String) throw new Server2RollingRefusal("TempContainerContextInvalid");
                        ConvertToC994RetiredInstant(stamp.GetString());
                    }
                }
            }
            catch (Exception) { throw new Server2RollingRefusal("TempContainerContextInvalid"); }
        }

        private static void AssertUnique(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new Server2RollingRefusal("TempContainerContextInvalid");
                    AssertUnique(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) AssertUnique(item);
            }
        }

        private static readonly Regex C994Instant = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,9})?(Z|[+-]\d{2}:\d{2})$");

        /// <summary>
        /// ConvertTo-C994RetiredInstant: a DateTime (its UTC round-trip text) or a string with an explicit zone, as the UTC
        /// DateTimeOffset round-trip text ("...+00:00"). Anything else throws TempContainerContextInvalid.
        /// </summary>
        public static string ConvertToC994RetiredInstant(object value)
        {
            string text;
            if (value is DateTime date) text = date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            else if (value is DateTimeOffset offset) text = offset.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            else if (value is string s) text = s;
            else throw new Server2RollingRefusal("TempContainerContextInvalid");
            if (!C994Instant.IsMatch(text)) throw new Server2RollingRefusal("TempContainerContextInvalid");
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
                throw new Server2RollingRefusal("TempContainerContextInvalid");
            return instant.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        /// <summary>Assert-C994BridgeContext: the retire-temp-containers context's exact keys and values.</summary>
        public static void AssertC994BridgeContext(object context, string hostCase)
        {
            if (!string.Equals(hostCase, "retire-temp-containers", StringComparison.Ordinal) || context == null || context is object[])
                throw new Server2RollingRefusal("TempContainerContextInvalid");
            var expected = new[] { "version", "operationId", "project", "projectId", "dryRun", "retiredAt" };
            var keys = Names(context);
            var version = Prop(context, "version");
            var operationId = Prop(context, "operationId");
            var project = Prop(context, "project");
            var projectId = Prop(context, "projectId");
            if (keys.Length != expected.Length || keys.Any(k => !expected.Contains(k, StringComparer.Ordinal))
                || !IsInt(version) || !Eq(version, 1)
                || !(operationId is string) || !CMatch(operationId, "^c994[0-9a-f]{32}$")
                || !(project is string) || !Ceq(project, "antiphon-runner-temp")
                || !(projectId is string) || !CMatch(projectId, "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
                || !(Prop(context, "dryRun") is bool))
                throw new Server2RollingRefusal("TempContainerContextInvalid");
            ConvertToC994RetiredInstant(Prop(context, "retiredAt"));
        }

        // ---- scripts/c1008-host-proof.ps1 ----

        private static readonly Regex AuditMoved = new Regex(@"^RecycleOriginMoved reason=withdrawn-ref repo=[A-Za-z0-9._/-]+ ref=refs/[A-Za-z0-9._/-]+ old=[0-9a-f]{40} new=([0-9a-f]{40}|deleted)$");
        private static readonly Regex AuditStatus = new Regex(@"\ARecycleGitAuditUnknown audit check=worktree-status status=[0-9]+ repo=[A-Za-z0-9._/-]+ stderr=(?:redacted|missing-executable:([A-Za-z0-9_][A-Za-z0-9_.+-]{0,63}))\z");
        private static readonly Regex AuditSecret = new Regex(@"credential|token|secret|password|^(?:gh[pousr]_|github_pat_|glpat-|xox|sk-|akia|eyj)", RegexOptions.IgnoreCase);
        private static readonly Regex AuditGeneric = new Regex(@"^Recycle[A-Za-z0-9]+( audit check=[A-Za-z0-9_-]+ status=[0-9]* repo=[A-Za-z0-9._/-]+( reason=[A-Za-z0-9_-]+)?)?$");

        /// <summary>Test-C1008SafeAuditDiagnosis: whether a host's audit refusal text may cross into the wrapper's refusal.</summary>
        public static bool TestC1008SafeAuditDiagnosis(string diagnosis)
        {
            if (string.IsNullOrEmpty(diagnosis)) return false;
            if (diagnosis.IndexOf('@') >= 0 || diagnosis.IndexOf("://", StringComparison.Ordinal) >= 0) return false;
            if (AuditMoved.IsMatch(diagnosis)) return true;
            var status = AuditStatus.Match(diagnosis);
            if (status.Success) return !AuditSecret.IsMatch(status.Groups[1].Value);
            return AuditGeneric.IsMatch(diagnosis);
        }

        // ---- scripts/c1609-read-packet.ps1 ----

        /// <summary>
        /// New-C1609RetirementReadPacket: the bash text the retirement census reads on server2 (observation only, no lock). coordinator
        /// is scripts/c1008-retirement.sh, remote scripts/c590-remote.sh (c1008_volume and c1609_temp_retirement_clearable are taken
        /// from it). A proof read needs an exact operation id (else RecycleContextInvalid); a missing function is
        /// TempRetirementJournalUnknown.
        /// </summary>
        public static string NewC1609RetirementReadPacket(string coordinator, string remote, string operation, bool clearable)
        {
            if (!clearable && !Regex.IsMatch(operation ?? "", "^c1008[0-9a-f]{32}$")) throw new Server2RollingRefusal("RecycleContextInvalid");
            var functions = new List<string>();
            foreach (var name in new[] { "c1008_volume", "c1609_temp_retirement_clearable" })
            {
                var match = Regex.Match(remote ?? "", "(?ms)^" + name + "\\(\\) \\{\\n.*?^\\}");
                if (!match.Success) throw new Server2RollingRefusal("TempRetirementJournalUnknown");
                functions.Add(match.Value);
            }
            var packet = Convert.ToBase64String(Encoding.UTF8.GetBytes((coordinator ?? "") + "\n" + string.Join("\n", functions)));
            var mode = clearable ? "clear" : "prior";
            var body = ReadPacketBody.Replace("\r\n", "\n").Replace("__OP__", operation ?? "").Replace("__MODE__", mode).Replace("__PACKET__", packet);
            return "timeout --kill-after=2s 25s bash <<'C1609_READ_PACKET_EOF'\n" + body + "\nC1609_READ_PACKET_EOF\n";
        }

        private const string ReadPacketBody = @"set -euo pipefail
umask 077
SERVER2_ROOT=/home/mc/antiphon-server2
C1008_PROJECT=antiphon-runner-temp
C1008_OPERATION='__OP__'
eval ""$(printf '%s' '__PACKET__' | base64 -d)""
c1609_load_retirement() { :; }
c1609_custody ""$SERVER2_ROOT"" dir
exe=""$(command -v docker)""; [[ ""$exe"" = /* ]]
docker() { command timeout --kill-after=2s 10s ""$exe"" ""$@""; }
if [ '__MODE__' = clear ]; then
    if c1609_temp_retirement_clearable >/dev/null; then
        printf '%s\n' '{""volumes"":[],""journals"":[]}'
    else
        jq -cn --arg reason ""${c1609_clear_reason:-TempRetirementJournalUnknown}"" '{error:$reason}'
    fi
    exit 0
fi
root=""$SERVER2_ROOT/retirement/$C1008_OPERATION""
file=""$root/receipt.json""
[ ! -e ""$root/closure.json"" ] && [ ! -L ""$root/closure.json"" ]
[ ! -e ""$root/close-pending.json"" ] && [ ! -L ""$root/close-pending.json"" ]
c1609_read_canonical ""$file"" >/dev/null
c1609_close_receipt_valid ""$file""
c1609_lifecycle_valid ""$root/lifecycle.json""
jq -e '.active==false and .complete==true and all(.children[];.joined==true)' ""$root/lifecycle.json"" >/dev/null
pid=""$(jq -r .owner.pid ""$root/lifecycle.json"")""
ticks=""$(jq -r .owner.startTicks ""$root/lifecycle.json"")""
if [ -e ""/proc/$pid"" ]; then
    [ ""$(awk '{sub(/^.*\) /, """"); print $20}' ""/proc/$pid/stat"")"" != ""$ticks"" ]
fi
jq -e --arg op ""$C1008_OPERATION"" '.state==""Ready"" and .mode==""scripted"" and .consumed==false and .operationId==$op' ""$file"" >/dev/null
nonce=""$(jq -er '.phaseNonce|select(test(""^[0-9a-f]{32}$""))' ""$file"")""
preview=""$root/preview-$nonce.json""
c1609_read_canonical ""$preview"" >/dev/null
cmp -s -- ""$preview"" ""$file""
ready=""$(date -u -d ""$(jq -r .readyAt ""$file"")"" +%s)""; now=""$(date -u +%s)""
[ ""$((now-ready))"" -ge -60 ] && [ ""$((now-ready))"" -le 900 ]
[ ""$(docker info --format '{{.ID}}')"" = ""$(jq -r .daemon ""$file"")"" ]
digest=""$(sha256sum ""$file"" | cut -d' ' -f1)""
jq -cn --arg op ""$C1008_OPERATION"" --arg digest ""$digest"" '{operationId:$op,digest:$digest,state:""Ready"",mode:""scripted""}'";
    }
}
