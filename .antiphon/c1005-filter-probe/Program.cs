using System.Reflection;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Testing.Platform.Requests;
using Microsoft.Testing.Platform.Extensions.Messages;
var constructor = typeof(TreeNodeFilter).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,[typeof(string)],null)!;
var roster = File.ReadAllLines(args[0]).Skip(1).Select(l=>l.Split(',').Select(x=>x.Trim('"')).ToArray()).ToArray();
var exclusions = new HashSet<string>(["windows_quick_row_finishes_beside_a_slow_row", "windows_row_arguments_round_trip_intact", "windows_chatty_row_drains_interleaved_stdout_and_stderr", "windows_row_timeout_kills_the_start_b_grandchild", "C665_LockedFileMidDeleteResumesOnLaterPass", "C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded"]);
foreach(var file in args.Skip(1)) {
var text=File.ReadAllText(file).Trim();
var full=(TreeNodeFilter)constructor.Invoke([text]);
var pathOnly=Regex.Replace(text,@"\[([^\]]*)\]", "");
int selected=0, cases=0, wrong=0; var watch=Stopwatch.StartNew();
var unitBag=new PropertyBag(new TestMetadataProperty("Category","Unit"));
var integrationBag=new PropertyBag(new TestMetadataProperty("Category","Integration"));
foreach(var row in roster) {
var pos=row[0].LastIndexOf('.'); var path="/Antiphon.Tests/"+row[0][..pos]+"/"+row[0][(pos+1)..]+"/"+row[1];
var match=full.MatchesFilter(path,unitBag);
if(match != !exclusions.Contains(row[1]) || full.MatchesFilter(path,integrationBag)) wrong++;
if(match) {selected++; cases+=int.Parse(row[2]);}
}
Console.WriteLine($"{file}: identities={selected}, cases={cases}, mismatches={wrong}, elapsed={watch.Elapsed.TotalSeconds:F3}s");
if(wrong!=0 || selected!=2771 || cases!=4000) return 1;
watch.Restart();
// TUnit MetadataFilterMatcher strips properties and constructs this per metadata entry.
foreach(var row in roster.Take(100)) {
var filter=(TreeNodeFilter)constructor.Invoke([pathOnly]);
filter.MatchesFilter("/Antiphon.Tests/Antiphon.Tests.Checkpoints/PlanCoverageCensusTests/"+row[1],new PropertyBag());
}
Console.WriteLine($"100 TUnit-style reconstructions: {watch.Elapsed.TotalSeconds:F3}s");
}
return 0;
