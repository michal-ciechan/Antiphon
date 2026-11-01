namespace Antiphon.TestSupport.LiveClaude;

internal static class LiveClaudeParityCases
{
    public static void Run(LiveClaudePortableContext context,LiveClaudeArgvCapture[] captures,Dictionary<string,string[]> expected)
    {
        foreach(var capture in captures)
        {
            var production=expected[capture.Model+"/"+capture.Role];
            LiveClaudeArgvCapture.Compare(capture,production);
            if(capture.Role is "off" or "slash")
            {
                LiveClaudeProfileCases.Refuses(()=>LiveClaudeArgvCapture.Compare(capture with {Args=capture.Args.Take(capture.Args.Length-2).ToArray()},production),"argv-parity","V-17","off-missing");
                LiveClaudeProfileCases.Refuses(()=>LiveClaudeArgvCapture.Compare(capture with {OverlayJson="{\"remoteControlAtStartup\":true}"},production),"argv-parity","V-17","off-json");
            }
            LiveClaudeProfileCases.Refuses(()=>LiveClaudeArgvCapture.Compare(capture with {Args=capture.Args.Append("--unexpected").ToArray()},production),"argv-parity","V-17","independent-"+capture.Role);
            foreach(var changed in new[]{capture with {Source="wrong"},capture with {Run="wrong"},capture with {ProducerBuild="wrong"},capture with {Uid="wrong"}})
            {
                var reason=changed.Source!=context.Source?"capture-source":changed.Run!=context.Run?"capture-run":changed.ProducerBuild!=context.ProducerBuild?"capture-build":"capture-uid";
                LiveClaudeProfileCases.Refuses(()=>LiveClaudeArgvCapture.Validate(context,changed,capture.Model,capture.Role),reason,"V-17",reason);
            }
            LiveClaudeProfileCases.Refuses(()=>LiveClaudeArgvCapture.Validate(context,capture with {Model="wrong"},capture.Model,capture.Role),"capture-request","V-17","capture-request");
            foreach(var changed in new[]{capture with {Complete=false},capture with {Args=[]}})
                LiveClaudeProfileCases.Refuses(()=>LiveClaudeArgvCapture.Validate(context,changed,capture.Model,capture.Role),"partial-capture","V-17","capture-cut");
        }
        LiveClaudeProfileCases.Refuses(()=>LiveClaudeArgvCapture.RequireProducer(context with {ProducerReceipt=null}),"producer-incomplete","V-17","producer-incomplete");
        var digest=LiveClaudeArgvCapture.RequireProducer(context);
        var payload=File.ReadAllText(Path.Combine(context.Root,"captures.json"));
        foreach(var cut in new[]{null,payload[..(payload.Length/2)],payload.Replace("--session-id","--altered-id",StringComparison.Ordinal)})
            LiveClaudeProfileCases.Refuses(()=>LiveClaudeArgvCapture.ReadPayload(cut,digest),"partial-capture","V-17","publication-cut");
    }
}
