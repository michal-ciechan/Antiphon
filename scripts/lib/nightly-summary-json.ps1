#requires -Version 5.1
# CARD-2005. Stream summary.json without one giant ConvertTo-Json string.
# ASCII-only. The qualification hop launches this script with pwsh (nightly-run-impl.ps1),
# and the file also loads under Windows PowerShell 5.1. System.Text.Json is not in
# .NET Framework, Newtonsoft is not loaded here, and JavaScriptSerializer is
# Framework-only and still builds one string. The writer below is the shared path.

if ($script:NightlySummaryJsonLoaded) { return }
$script:NightlySummaryJsonLoaded = $true

function Initialize-NightlyStreamingJson {
    if ('Antiphon.NightlySummary.StreamingJson' -as [type]) { return }
    # Default Add-Type references already include mscorlib/.NET and
    # System.Management.Automation on both Windows PowerShell 5.1 and pwsh 7.
    # Passing -ReferencedAssemblies replaces that set and hides System.Text.
    $code = @'
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;

namespace Antiphon.NightlySummary {
    public static class StreamingJson {
        public const int BufferBytes = 65536;

        public static void Write(string path, object value, int maxDepth) {
            UTF8Encoding utf8 = new UTF8Encoding(false);
            FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes);
            try {
                Writer writer = new Writer(stream, utf8, maxDepth);
                writer.WriteValue(value, 0);
                writer.Flush();
                stream.Flush(true);
            } finally {
                stream.Dispose();
            }
        }

        sealed class Writer {
            readonly FileStream stream;
            readonly UTF8Encoding utf8;
            readonly int maxDepth;
            readonly byte[] buffer;
            int length;

            public Writer(FileStream stream, UTF8Encoding utf8, int maxDepth) {
                this.stream = stream;
                this.utf8 = utf8;
                this.maxDepth = maxDepth;
                this.buffer = new byte[StreamingJson.BufferBytes];
            }

            public void Flush() {
                if (this.length > 0) {
                    this.stream.Write(this.buffer, 0, this.length);
                    this.length = 0;
                }
            }

            void Byte(byte value) {
                if (this.length == this.buffer.Length) this.Flush();
                this.buffer[this.length++] = value;
            }

            void Ascii(string text) {
                for (int i = 0; i < text.Length; i++) this.Byte((byte)text[i]);
            }

            public void WriteValue(object value, int depth) {
                value = Unwrap(value);
                if (IsNull(value)) { this.Ascii("null"); return; }
                string text = value as string;
                if (text != null) { this.WriteString(text); return; }
                if (value is bool) { this.Ascii((bool)value ? "true" : "false"); return; }
                if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong) {
                    this.Ascii(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                }
                if (value is float || value is double) {
                    double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(number)) { this.WriteString("NaN"); return; }
                    if (double.IsPositiveInfinity(number)) { this.WriteString("Infinity"); return; }
                    if (double.IsNegativeInfinity(number)) { this.WriteString("-Infinity"); return; }
                    this.Ascii(EnsureDecimal(number.ToString("R", CultureInfo.InvariantCulture)));
                    return;
                }
                if (value is decimal) {
                    decimal number = (decimal)value;
                    this.Ascii(number.ToString("0.0#############################", CultureInfo.InvariantCulture));
                    return;
                }
                if (value is DateTime) {
                    this.WriteString(FormatDate((DateTime)value));
                    return;
                }
                if (value is DateTimeOffset) {
                    this.WriteString(FormatOffset((DateTimeOffset)value));
                    return;
                }
                if (value is Enum) { this.WriteString(value.ToString()); return; }

                bool container = value is IDictionary || value is IEnumerable || value is PSObject;
                if (container && depth > this.maxDepth) {
                    this.WriteString(PowerShellString(value));
                    return;
                }
                IDictionary dictionary = value as IDictionary;
                if (dictionary != null) { this.WriteDictionary(dictionary, depth); return; }
                IEnumerable items = value as IEnumerable;
                if (items != null) { this.WriteArray(items, depth); return; }
                PSObject custom = value as PSObject;
                if (custom != null) { this.WriteObject(custom, depth); return; }
                this.WriteString(Convert.ToString(value, CultureInfo.InvariantCulture));
            }

            void WriteDictionary(IDictionary dictionary, int depth) {
                this.Byte((byte)'{');
                bool first = true;
                foreach (DictionaryEntry entry in dictionary) {
                    if (entry.Key == null) continue;
                    if (!first) this.Byte((byte)',');
                    first = false;
                    this.WriteString(Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    this.Byte((byte)':');
                    this.WriteValue(entry.Value, depth + 1);
                }
                this.Byte((byte)'}');
            }

            void WriteArray(IEnumerable items, int depth) {
                this.Byte((byte)'[');
                bool first = true;
                foreach (object item in items) {
                    if (!first) this.Byte((byte)',');
                    first = false;
                    this.WriteValue(item, depth + 1);
                }
                this.Byte((byte)']');
            }

            void WriteObject(PSObject custom, int depth) {
                this.Byte((byte)'{');
                bool first = true;
                foreach (PSPropertyInfo property in custom.Properties) {
                    if (property.MemberType != PSMemberTypes.NoteProperty) continue;
                    if (!first) this.Byte((byte)',');
                    first = false;
                    this.WriteString(property.Name);
                    this.Byte((byte)':');
                    this.WriteValue(property.Value, depth + 1);
                }
                this.Byte((byte)'}');
            }

            void WriteString(string text) {
                this.Byte((byte)'"');
                if (text != null) {
                    for (int i = 0; i < text.Length; i++) {
                        char ch = text[i];
                        if (ch == '"' || ch == '\\') { this.Byte((byte)'\\'); this.Byte((byte)ch); }
                        else if (ch == '\n') { this.Ascii("\\n"); }
                        else if (ch == '\r') { this.Ascii("\\r"); }
                        else if (ch == '\t') { this.Ascii("\\t"); }
                        else if (ch < 32) { this.Ascii("\\u"); this.Ascii(((int)ch).ToString("x4", CultureInfo.InvariantCulture)); }
                        else if (ch < 128) { this.Byte((byte)ch); }
                        else {
                            int take = (char.IsHighSurrogate(ch) && i + 1 < text.Length) ? 2 : 1;
                            byte[] encoded = this.utf8.GetBytes(text.Substring(i, take));
                            for (int b = 0; b < encoded.Length; b++) this.Byte(encoded[b]);
                            if (take == 2) i++;
                        }
                    }
                }
                this.Byte((byte)'"');
            }

            static string EnsureDecimal(string rendered) {
                if (rendered.IndexOf('.') < 0 && rendered.IndexOf('E') < 0 && rendered.IndexOf('e') < 0) return rendered + ".0";
                return rendered;
            }

            static string FormatFraction(string fraction) {
                fraction = fraction.TrimEnd('0');
                if (fraction.Length == 0) return "";
                return "." + fraction;
            }

            static string FormatDate(DateTime when) {
                string body = when.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + FormatFraction(when.ToString("fffffff", CultureInfo.InvariantCulture));
                if (when.Kind == DateTimeKind.Utc) return body + "Z";
                if (when.Kind == DateTimeKind.Local) return body + new DateTimeOffset(when).ToString("zzz", CultureInfo.InvariantCulture);
                return body;
            }

            static string FormatOffset(DateTimeOffset when) {
                string body = when.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + FormatFraction(when.ToString("fffffff", CultureInfo.InvariantCulture));
                return body + when.ToString("zzz", CultureInfo.InvariantCulture);
            }

            static bool IsNull(object value) {
                if (value == null) return true;
                return value.GetType().FullName == "System.Management.Automation.Internal.AutomationNull";
            }

            static object Unwrap(object value) {
                for (int i = 0; i < 5 && value != null; i++) {
                    PSObject wrapped = value as PSObject;
                    if (wrapped == null) return value;
                    object inner = wrapped.BaseObject;
                    if (inner == null || object.ReferenceEquals(inner, wrapped) || inner is PSCustomObject) return wrapped;
                    value = inner;
                }
                return value;
            }

            static string PowerShellString(object value) {
                value = Unwrap(value);
                if (IsNull(value)) return "";
                string text = value as string;
                if (text != null) return text;
                IDictionary dictionary = value as IDictionary;
                if (dictionary != null) return value.GetType().FullName;
                IEnumerable items = value as IEnumerable;
                if (items != null && !(value is PSObject)) {
                    object only = null;
                    int count = 0;
                    foreach (object item in items) {
                        count++;
                        if (count == 1) only = item;
                        else break;
                    }
                    if (count == 1) return PowerShellString(only);
                    if (count == 0) return "";
                    return value.GetType().FullName;
                }
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
'@
    Add-Type -TypeDefinition $code
}

function Write-NightlyStreamingJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Object,
        [int]$Depth = 8
    )
    Initialize-NightlyStreamingJson
    $directory = Split-Path -Parent $Path
    if ($directory -and -not [IO.Directory]::Exists($directory)) {
        [void][IO.Directory]::CreateDirectory($directory)
    }
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $backup = $Path + '.' + [guid]::NewGuid().ToString('N') + '.bak'
    try {
        [Antiphon.NightlySummary.StreamingJson]::Write($temporary, $Object, $Depth)
        if ([IO.File]::Exists($Path)) {
            [IO.File]::Replace($temporary, $Path, $backup)
            if ([IO.File]::Exists($backup)) { [IO.File]::Delete($backup) }
        } else {
            [IO.File]::Move($temporary, $Path)
        }
    } catch {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
        if ([IO.File]::Exists($backup)) { [IO.File]::Delete($backup) }
        throw
    }
}

# Scheduling snapshot only. Full chunk rows, including census exclusions, stay on summary.suites.
function Get-NightlyDispatchProgressSuites {
    param($Rows)
    $names = @('id', 'chunk', 'timedOut', 'result', 'exitCode', 'skipped', 'coverageComplete')
    $projected = New-Object System.Collections.Generic.List[object]
    foreach ($row in @($Rows)) {
        if ($null -eq $row) { continue }
        $present = $false
        $chunk = $null
        if ($row -is [System.Collections.IDictionary]) {
            if ($row.Contains('chunk')) { $present = $true; $chunk = $row['chunk'] }
        } elseif ($null -ne $row.PSObject.Properties['chunk']) {
            $present = $true
            $chunk = $row.PSObject.Properties['chunk'].Value
        }
        if (-not $present -or -not $chunk) { continue }
        $copy = [ordered]@{}
        foreach ($name in $names) {
            $has = $false
            $value = $null
            if ($row -is [System.Collections.IDictionary]) {
                if ($row.Contains($name)) { $has = $true; $value = $row[$name] }
            } elseif ($null -ne $row.PSObject.Properties[$name]) {
                $has = $true
                $value = $row.PSObject.Properties[$name].Value
            }
            if ($has) { $copy[$name] = $value }
        }
        $projected.Add($copy)
    }
    return ,$projected.ToArray()
}
