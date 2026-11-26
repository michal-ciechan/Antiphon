# CARD-2082 repository reader census

The Plan used this read-only inventory probe at `771bc1a46afe5893894b050f4da47effb6c758e7`. Run it from the repository root. PowerShell 7 supplies Roslyn. It parses tracked test sources in-process and writes one JSON object per declaring type to stdout. It constructs no project, starts no test host and takes no release lock. Redirect stdout to an external scratch file for analysis.

```powershell
$file = 'docs/superpowers/plans/2026-11-26-card-2082-invariant-detector.md'
$body = [regex]::Match((Get-Content -LiteralPath $file -Raw), '(?ms)^<!-- census -->\r?\n```powershell\r?\n(.*?)^```')
if (-not $body.Success) { throw 'Missing census source.' }
& ([scriptblock]::Create($body.Groups[1].Value))
```

`key` includes the source project directory and fully qualified type. Partial files share a key. `methods` are directly declared test methods, before argument expansion. The inventory includes helper calls, fields, properties, constructors, data sources and setup reachable through the syntax lookup. Exact paths and unambiguous repository leaf names, root accessors, directory walks, Git tracking, importer calls, compiled rosters and EF migration metadata are independent discovery signals. `sites` identifies the member and source line, and `direct` gives the literals at the owning member. Paths ending in `/**` are search hints reconstructed from path components. Fixture paths, examples, outputs and invocation of a tracked executable also produce hits. Use the assertion and its root to decide membership. The executable emits all types with direct TUnit methods, including zero-hit types, for independent comparison.

This syntax lookup deliberately exposes uncertain references. It combines overloaded helper members, misses some virtual dispatch, inherited tests, parameter flows and runtime-generated paths, and can attach an unrelated helper with the same spelling. The Plan reconciles source declarations, linked assemblies, known failures and execution evidence separately. Code should implement the narrower membership guard against actual repository input and give each reviewed fixture exception a method and reason. Do not install the discovery probe itself as a pass/fail gate.

<!-- census -->
```powershell
# Run at the root of a clean checkout using PowerShell 7 with its bundled Roslyn.
$ErrorActionPreference = 'Stop'
$refs = @((Get-ChildItem "$PSHOME/ref/*.dll").FullName) + @("$PSHOME/Microsoft.CodeAnalysis.dll", "$PSHOME/Microsoft.CodeAnalysis.CSharp.dll")
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class InvariantCensus {
    sealed class Member {
        public string Type, Project, Name, File;
        public int Line;
        public bool Test, Setup;
        public HashSet<string> Signals = new(), Paths = new(), References = new();
    }
    static readonly Regex Root = new(@"^(RepoRoot|RepositoryRoot|SolutionRoot|FindRepo(Root|sitoryRoot)?|FindSolutionRoot|FindRepository|FindCheckoutRoot|GetRepo(sitory)?Root|RepoPath|LocateRepoRoot|FindRegistryPath|MigrationIds|GetMigrations|GetPendingMigrations|HasPendingModelChanges)$", RegexOptions.IgnoreCase);
    static string Id(TypeDeclarationSyntax t) => string.Join(".", t.AncestorsAndSelf().Reverse().Select(n =>
        n is BaseNamespaceDeclarationSyntax ns ? ns.Name.ToString() : n is TypeDeclarationSyntax td ? td.Identifier.ValueText : null).Where(s => s != null));
    static IEnumerable<string> Attrs(MemberDeclarationSyntax m) => m.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString().Split('.').Last());
    static IEnumerable<(string, MemberDeclarationSyntax)> Parts(MemberDeclarationSyntax m) {
        if (m is MethodDeclarationSyntax a) yield return (a.Identifier.ValueText,m);
        if (m is ConstructorDeclarationSyntax) yield return (".ctor",m);
        if (m is PropertyDeclarationSyntax p) yield return (p.Identifier.ValueText,m);
        if (m is FieldDeclarationSyntax f) foreach(var v in f.Declaration.Variables) yield return (v.Identifier.ValueText,m);
    }
    public static IEnumerable<string> Scan(string repository, string[] tracked) {
        var paths = tracked.ToHashSet(StringComparer.Ordinal);
        var leaves = tracked.GroupBy(p => Path.GetFileName(p)).ToDictionary(g => g.Key,g => g.ToArray());
        var members = new List<Member>();
        var usings = new Dictionary<string,string[]>();
        var fixtures = new Dictionary<string,string[]>();
        var bases = new Dictionary<string,string[]>();
        foreach(var file in tracked.Where(s=>s.StartsWith("tests/") && s.EndsWith(".cs"))) {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(repository, file)), new CSharpParseOptions(LanguageVersion.Preview), file);
            var root=tree.GetCompilationUnitRoot();
            usings[file]=root.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(u=>!u.StaticKeyword.IsKind(SyntaxKind.None))
                .Select(u=>u.Name.ToString().Split('.').Last()).ToArray();
            foreach(var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>()) {
                var full=Id(type); var project=file.Split('/')[1];
                var typeKey = project+"/"+full;
                var sources = type.AttributeLists.SelectMany(a=>a.DescendantNodes().OfType<GenericNameSyntax>())
                    .Where(a=>a.Identifier.ValueText=="ClassDataSource").SelectMany(a=>a.TypeArgumentList.Arguments).Select(a=>a.ToString()).ToArray();
                if(sources.Length>0) fixtures[typeKey]=sources;
                var receiverTypes = new Dictionary<string,string>();
                foreach(var field in type.Members.OfType<FieldDeclarationSyntax>())
                    foreach(var v in field.Declaration.Variables) receiverTypes[v.Identifier.ValueText]=field.Declaration.Type.ToString().Split('.').Last();
                if(type.BaseList!=null) bases[project+"/"+full]=type.BaseList.Types.Select(b=>b.Type.ToString().Split('.').Last()).ToArray();
                foreach(var (name,decl) in type.Members.SelectMany(Parts)) {
                    var row=new Member{Type=full,Project=project,Name=name,File=file,Line=decl.GetLocation().GetLineSpan().StartLinePosition.Line+1,
                        Test=Attrs(decl).Any(a=>a=="Test" || a=="TestAttribute"),Setup=name==".ctor" || Attrs(decl).Any(a=>a=="Before" || a=="After")};
                    var nodes=decl.DescendantNodes().ToArray();
                    foreach(var id in nodes.OfType<IdentifierNameSyntax>()) {
                        var n=id.Identifier.ValueText; if(Root.IsMatch(n)) row.Signals.Add(n);
                        if(id.Parent is MemberAccessExpressionSyntax ma && ma.Name==id) {
                            var receiver=ma.Expression.GetLastToken().ValueText;
                            row.References.Add(receiver+"."+n);
                            if(receiverTypes.TryGetValue(receiver,out var owner)) row.References.Add(owner+"."+n);
                            if(n=="GetTypes" || n=="DefinedTypes") row.Signals.Add("assembly inventory");
                        } else row.References.Add("."+n);
                    }
                    foreach(var create in nodes.OfType<ObjectCreationExpressionSyntax>()) row.References.Add(create.Type.GetLastToken().ValueText+"..ctor");
                    foreach(var s in nodes.OfType<LiteralExpressionSyntax>().Where(l=>l.IsKind(SyntaxKind.StringLiteralExpression)).Select(l=>l.Token.ValueText.Replace('\\','/'))) {
                        if(s=="ls-files" || s.StartsWith("ls-files ")) row.Signals.Add("git ls-files");
                        if(s.Length==0 || s.Length>250 || s.Any(char.IsControl)) continue;
                        if(s=="*.cs" || s=="*.csproj" || s=="*.sln" || s=="*.md") row.Paths.Add(s);
                        if(paths.Contains(s)) row.Paths.Add(s);
                        else if(leaves.TryGetValue(s,out var options) && options.Length==1) row.Paths.Add(options[0]);
                    }
                    foreach(var inv in nodes.OfType<InvocationExpressionSyntax>()) {
                        var expr=inv.Expression.ToString();
                        if(expr.EndsWith("Directory.EnumerateFiles") || expr.EndsWith("Directory.GetFiles")) row.Signals.Add("directory walk");
                        if(expr.EndsWith("PlanTableImporter.ImportFile") || expr.EndsWith("PlanTableImporter.Import")
                            || expr.EndsWith("PlanTableImporter.Parse")) row.Signals.Add("plan importer");
                        if(expr.EndsWith("DockerStackDocuments.Read")) row.Signals.Add("DockerStackDocuments.Read");
                    }
                    // File names in bare assertion messages and synthetic source literals alone are insufficient.
                    if(nodes.OfType<TypeOfExpressionSyntax>().Count()>2 && row.References.Any(r=>r.Contains("Attribute"))) row.Signals.Add("compiled attribute roster");
                    foreach(var call in nodes.OfType<InvocationExpressionSyntax>().Where(i=>i.Expression.ToString().EndsWith("Path.Combine"))) {
                        var parts=call.ArgumentList.Arguments.Select(a=>a.Expression as LiteralExpressionSyntax).Where(a=>a!=null && a.IsKind(SyntaxKind.StringLiteralExpression)).Select(a=>a.Token.ValueText).ToArray();
                        if(parts.Length==0 || parts.Any(p=>p.Length==0 || p.Length>170 || p.Any(char.IsControl))) continue;
                        var hint=string.Join("/",parts).Replace('\\','/');
                        if(paths.Contains(hint)) row.Paths.Add(hint);
                        else if(parts.Length>0 && new[]{"docs","tests","scripts","server","src","tools"}.Contains(parts[0]) && !hint.Contains(".."))
                            row.Paths.Add(hint.TrimEnd('/')+"/**");
                    }
                    if(row.Paths.Count>0 && (row.References.Any(r=>Regex.IsMatch(r,@"^(?:File\.(?:Read|Open|Exists)|Path\.(?:Combine|Join)|Directory\.|XDocument\.Load)"))
                        || row.Paths.Any(p=>p.StartsWith("docs/") || p.StartsWith("server/") || p.StartsWith("src/") || p.StartsWith("scripts/") || (p.StartsWith("tests/") && p.EndsWith(".json")))))
                        row.Signals.Add("tracked path");
                    members.Add(row);
                }
            }
        }
        var byType=members.GroupBy(m=>m.Project+"/"+m.Type).ToDictionary(g=>g.Key,g=>g.ToArray());
        var lookup=members.ToLookup(m=>m.Type.Split('.').Last()+"."+m.Name);
        var local=members.ToLookup(m=>m.Project+"/"+m.Type+"/"+m.Name);
        foreach(var g in byType.OrderBy(k=>k.Key,StringComparer.Ordinal)) {
            var entry=g.Value.Where(m=>m.Test).ToArray();
            if(entry.Length==0) continue;
            var visited=new HashSet<Member>(); var queue=new Queue<Member>(entry.Concat(g.Value.Where(m=>m.Setup)));
            var inputs=new SortedSet<string>(StringComparer.Ordinal); var signs=new SortedSet<string>(StringComparer.Ordinal);
            var sites=new SortedSet<string>(StringComparer.Ordinal);
            if(fixtures.TryGetValue(g.Key,out var fixtureNames))
                foreach(var fixture in fixtureNames) foreach(var m in lookup[fixture+"..ctor"]) queue.Enqueue(m);
            while(queue.Count>0) {
                var m=queue.Dequeue(); if(!visited.Add(m)) continue;
                if(m.Signals.Count>0) {
                    sites.Add(m.File+":"+m.Line+" "+m.Name+" ["+string.Join(", ",m.Signals.OrderBy(x=>x,StringComparer.Ordinal))+"]");
                    signs.UnionWith(m.Signals);
                }
                inputs.UnionWith(m.Paths);
                foreach(var r in m.References) {
                    if(r.StartsWith(".")) {
                        foreach(var n in local[m.Project+"/"+m.Type+"/"+r.Substring(1)]) queue.Enqueue(n);
                        IEnumerable<string> imports=usings[m.File];
                        if(bases.TryGetValue(m.Project+"/"+m.Type,out var bs)) imports=imports.Concat(bs);
                        foreach(var owner in imports) foreach(var n in lookup[owner+r]) queue.Enqueue(n);
                    } else {
                        var possible=lookup[r];
                        foreach(var n in possible.Where(n=>n.Project==m.Project || n.Project=="Shared")) queue.Enqueue(n);
                    }
                }
            }
            yield return JsonSerializer.Serialize(new {key=g.Key,methodCount=entry.Length,methods=entry.Select(m=>m.Name).ToArray(),files=g.Value.Select(m=>m.File).Distinct().ToArray(),signals=signs,inputs,sites,direct=g.Value.Where(m=>m.Signals.Count>0 || m.Paths.Count>0).Select(m=>new {m.Name,m.Line,m.File,m.Signals,m.Paths})});
        }
    }
}
'@
$tracked = @(git -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0 -or -not ($tracked -contains 'Antiphon.sln')) { throw 'Run at the repository root.' }
[InvariantCensus]::Scan((Get-Location).ProviderPath, $tracked)
```
