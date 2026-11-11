using Antiphon.Tests.Scripts;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("usage: Antiphon.DerivedImpactIndex <repo>");
    return 1;
}

var repo = Path.GetFullPath(args[0]);
var path = Path.Combine(repo, "tests", "derived-impact-index.json");
var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
try
{
    var index = DerivedImpactIndexBuilder.Build(repo);
    DerivedImpactIndexBuilder.Write(repo, index);
}
catch (Exception ex)
{
    Console.Error.WriteLine("derived-impact-index failed: " + ex.GetType().Name + ": " + ex.Message);
    return 1;
}

var after = File.ReadAllBytes(path);
var changed = before is null || !before.AsSpan().SequenceEqual(after);
Console.WriteLine(changed ? "derived-impact-index changed" : "derived-impact-index unchanged");
return 0;
