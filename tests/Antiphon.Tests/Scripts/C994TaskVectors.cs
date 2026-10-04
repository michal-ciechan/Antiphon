using System.Text.Json.Nodes;
namespace Antiphon.Tests.Scripts;

// Inputs only. Acceptance is decided by the actual host and wrapper census routines.
internal static class C994TaskVectors
{
    internal const string Project = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
    internal static JsonArray Build(JsonObject empty)
    {
        var cases=new JsonArray();
        JsonObject Input()=>new(){["scopes"]=new JsonObject{[Project]=empty.DeepClone()},["details"]=new JsonObject()};
        void Add(string key,bool accepted,JsonObject input)=>cases.Add(new JsonObject{["key"]=key,["accepted"]=accepted,["input"]=input});
        JsonObject WithTask(string status="Succeeded",string runner="server2-temp",string? requested=null,string? started=null,string? land=null)
        {
            var input=Input();var id="22222222-2222-2222-2222-222222222222";
            var row=new JsonObject{["id"]=id,["status"]=status,["runnerId"]=runner,["projectId"]=Project,["scopeSource"]="Task",["landRequestedAt"]=requested,["landStartedAt"]=started};
            input["scopes"]![Project]!["items"]!.AsArray().Add(row);
            input["details"]![id]=new JsonObject{["summary"]=row.DeepClone(),["landRequest"]=land==null?null:new JsonObject{["state"]=land,["terminalEventId"]="33333333-3333-3333-3333-333333333333"}};
            return input;
        }
        Add("empty",true,Input());Add("published-terminal",true,WithTask());
        foreach(var status in new[]{"Queued","Dispatched","Working","Blocked","Failed"})Add("bound-"+status,false,WithTask(status));
        Add("requested",false,WithTask(requested:"2026-10-04T00:00:00Z"));Add("started",false,WithTask(started:"2026-10-04T00:00:00Z"));
        foreach(var land in new[]{"Queued","Held","Running","NeedsResolution","Unknown","Completed","Superseded","Canceled"})Add("land-"+land,land is "Completed" or "Superseded" or "Canceled",WithTask(land:land));
        foreach(var fault in new[]{"wrong-count","withheld","unscoped","malformed","incomplete","detail","api"}){
            var input=Input();var envelope=input["scopes"]![Project]!;
            switch(fault){
                case "wrong-count": envelope["excluded"]!["total"]=1;break;
                case "withheld": envelope["excluded"]!["total"]=1;envelope["excluded"]!["byProject"]!.AsArray().Add(new JsonObject{["projectId"]="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",["count"]=1});break;
                case "unscoped":input=WithTask();input["scopes"]![Project]!["items"]![0]!["scopeSource"]="None";break;
                case "malformed":envelope["items"]=new JsonObject();break;
                case "incomplete":envelope.AsObject().Remove("excluded");break;
                case "detail":input=WithTask();input["details"]!["22222222-2222-2222-2222-222222222222"]!["summary"]!["runnerId"]="other";break;
                case "api":input["scopes"]!.AsObject().Remove(Project);break;
            }
            Add(fault,false,input);
        }
        var closed=WithTask(runner:"other");var other="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        var excluded=empty.DeepClone();excluded["scope"]!["projectId"]=other;
        var otherRow=closed["scopes"]![Project]!["items"]![0]!.DeepClone();otherRow["id"]="44444444-4444-4444-4444-444444444444";otherRow["projectId"]=other;
        excluded["items"]!.AsArray().Add(otherRow);closed["scopes"]![other]=excluded;
        closed["scopes"]![Project]!["excluded"]!["total"]=1;closed["scopes"]![Project]!["excluded"]!["byProject"]!.AsArray().Add(new JsonObject{["projectId"]=other,["count"]=1});
        closed["details"]!["44444444-4444-4444-4444-444444444444"]=new JsonObject{["summary"]=otherRow.DeepClone(),["landRequest"]=null};
        Add("closed-exclusion",true,closed);return cases;
    }
}
