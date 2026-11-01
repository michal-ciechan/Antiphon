using Antiphon.NativeLinuxMigrationAudit;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: extract|digest|pins|audit");
    return 2;
}

try
{
    return args[0] switch
    {
        "extract" => Extract(args),
        "digest" => Digest(args),
        "pins" => Pins(args),
        "audit" => Audit(args),
        _ => Fail("unknown command " + args[0]),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
    return 1;
}

static int Extract(string[] args)
{
    var repo = Required(args, "--repo");
    var policy = Required(args, "--policy");
    var output = Required(args, "--out");
    var sha = Required(args, "--pre-s1-sha");
    var document = NativeLinuxWindowsReasonAudit.ExtractRepository(repo, policy, sha);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllText(output, document.ToJson());
    Console.WriteLine("rows=" + document.Placements.Count
        + " signatures=" + document.Reasons.Count
        + " problems=" + document.Problems.Count
        + " out=" + output);
    foreach (var problem in document.Problems)
        Console.Error.WriteLine(problem);
    return document.Problems.Count == 0 ? 0 : 2;
}

static int Digest(string[] args)
{
    var policy = Required(args, "--policy");
    var text = File.ReadAllText(policy);
    var witnessed = NativeLinuxWindowsReasonAudit.ReadRows(text)
        .Where(static row => row.Witnessed)
        .Select(static row => row.Key)
        .ToHashSet(StringComparer.Ordinal);
    var legacy = NativeLinuxWindowsReasonAudit.LegacyCanonicalRecords(text, witnessed);
    Console.WriteLine("witnessedCount=" + witnessed.Count);
    Console.WriteLine("policyHash=" + NativeLinuxWindowsReasonAudit.Sha256Utf8(NativeLinuxWindowsReasonAudit.PolicyCanonical(text)));
    Console.WriteLine("legacyCount=" + legacy.Count);
    Console.WriteLine("legacyDigest=" + NativeLinuxWindowsReasonAudit.Sha256Utf8(legacy.Payload));
    Console.WriteLine("payloadBytes=" + System.Text.Encoding.UTF8.GetByteCount(legacy.Payload));
    return 0;
}

static int Pins(string[] args)
{
    var path = Required(args, "--attribute");
    var bytes = File.ReadAllBytes(path);
    if (Array.IndexOf(bytes, (byte)'\r') >= 0)
        return Fail("attribute file contains CR: " + path);
    Console.WriteLine("attributeSha256=" + NativeLinuxWindowsReasonAudit.Sha256Utf8(bytes));
    Console.WriteLine("attributeBytes=" + bytes.Length);
    return 0;
}

static int Audit(string[] args)
{
    var before = File.ReadAllText(Required(args, "--before"));
    var after = File.ReadAllText(Required(args, "--after"));
    var diffs = NativeLinuxWindowsReasonAudit.CompareAnnotationDelta(before, after);
    foreach (var diff in diffs)
        Console.WriteLine(diff);
    return diffs.Count == 0 ? 0 : 2;
}

static string Required(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name)
            return args[i + 1];
    }
    throw new ArgumentException("missing " + name);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}
