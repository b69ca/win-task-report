using System.Text.Json.Nodes;
using WinDiagnostics;
int passed = 0, failed = 0;
void Assert(bool value, string why) { if (!value) throw new Exception(why); }
void Reject(Action body) { try { body(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid evidence/options rejection"); }
void Check(string name, Action body) { try { body(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + e.Message); } }
JsonObject Snapshot() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixture.json")))!;
JsonObject Evidence() => (JsonObject)Snapshot()["evidence"]!.DeepClone();
bool Has(JsonObject e, string id) => Tool.Analyze(e).Any(f => f.Id == id);
Check("snapshot replay retains evidence and rejects wrong tool/schema", () => {
    var s = Snapshot(); var c = Core.ReadSnapshot(s); Assert(c.Issues.Count == 0, "fixture issues");
    c.Evidence["testOnly"] = true; Assert(s["evidence"]!["testOnly"] is null, "clone isolation");
    s["tool"] = "other"; Reject(() => Core.ReadSnapshot(s)); s = Snapshot(); s["schemaVersion"] = 2; Reject(() => Core.ReadSnapshot(s));
    s = Snapshot(); s["issues"] = null; Reject(() => Core.ReadSnapshot(s));
});
Check("strict CLI and offline/live option separation", () => {
    Reject(() => Options.Parse(["--unknown"], Tool.ValueOptions, Tool.Flags));
    Reject(() => Options.Parse(["--input"], Tool.ValueOptions, Tool.Flags));
    Reject(() => Options.Parse(["--json", "--json"], Tool.ValueOptions, Tool.Flags));
    Reject(() => Options.Parse(["--input", "f", Tool.ValueOptions[0], "x"], Tool.ValueOptions, Tool.Flags));
    Tool.ValidateOptions(Options.Parse([], Tool.ValueOptions, Tool.Flags));
});
Check("malformed rows are rejected instead of silently analyzed", () => {
    Reject(() => Tool.ValidateEvidence(new JsonObject()));
    var e = Evidence(); e["tasks"] = new JsonArray { 7 }; Reject(() => Tool.ValidateEvidence(e));
});

Check("success and scheduler running/ready/queued statuses are not errors", () => { foreach(var code in new long[]{0,0x41300,0x41301,0x41302,0x41304,0x41305,0x41308,0x41325}) { var e=Evidence(); var a=(JsonObject)e["tasks"]![1]!.DeepClone(); a["lastResult"]=code; e["tasks"]=new JsonArray(a); Assert(!Has(e,"task-result-review"),$"status {code:X}"); } });
Check("application and HRESULT results warrant review without guessed cause", () => { foreach(var code in new long[]{1,0x80070005,0x8004130F,0x40001}) { var e=Evidence(); var a=(JsonObject)e["tasks"]![1]!.DeepClone(); a["lastResult"]=code; e["tasks"]=new JsonArray(a); Assert(Has(e,"task-result-review"),$"code {code:X}"); } });
Check("missed runs disabled tasks and never-run have distinct findings", () => { var e=Evidence(); Assert(Has(e,"missed-runs")&&Has(e,"task-disabled")&&Has(e,"task-never-run"),"distinct findings"); });
Check("terminated and invalid-trigger success constants still warrant review", () => { foreach(var code in new[]{0x41306,0x41307,0x4131B,0x4131C}) { var e=Evidence(); e["tasks"]![1]!["lastResult"]=code; Assert(Tool.Analyze(e).Count(f=>f.Id=="task-result-review")==2,"review"); } });
Check("empty filtered scope is explicit", () => { var e=Evidence(); e["tasks"]=new JsonArray(); e["matchedCount"]=0; Assert(Has(e,"no-tasks"),"empty"); });
Check("unavailable info never fabricates success or missed runs", () => { var e=Evidence(); var a=(JsonObject)e["tasks"]![0]!.DeepClone(); a["infoAvailable"]=false; a["lastResult"]=null; a["missedRuns"]=null; e["tasks"]=new JsonArray(a); Tool.ValidateEvidence(e); Assert(Tool.Analyze(e).Count==0,"unknown"); });
Check("unsigned result bounds and dates validated", () => { var e=Evidence(); e["tasks"]![0]!["lastResult"]=-1; Reject(()=>Tool.ValidateEvidence(e)); e=Evidence(); e["tasks"]![0]!["lastRunUtc"]="yesterday"; Reject(()=>Tool.ValidateEvidence(e)); });
Check("count limits must agree", () => { var e=Evidence(); e["matchedCount"]=0; Reject(()=>Tool.ValidateEvidence(e)); e=Evidence(); e["limit"]=1; Reject(()=>Tool.ValidateEvidence(e)); });
Check("CLI limits and literal filters", () => { Reject(()=>Tool.ValidateOptions(Options.Parse(["--limit","5001"],Tool.ValueOptions,Tool.Flags))); Reject(()=>Tool.ValidateOptions(Options.Parse(["--task"," "],Tool.ValueOptions,Tool.Flags))); Tool.ValidateOptions(Options.Parse(["--task","backup[*];'"],Tool.ValueOptions,Tool.Flags)); });

var capture = new StringWriter(); var oldOut = Console.Out; var oldErr = Console.Error;
try {
    Console.SetOut(capture); Console.SetError(capture);
    var code = await Core.Run(["--input", Path.Combine(AppContext.BaseDirectory, "fixture.json"), "--json"]);
    Console.SetOut(oldOut); Console.SetError(oldErr);
    Check("CLI JSON offline round-trip", () => { Assert(code == 0, "exit code"); var report = (JsonObject)JsonNode.Parse(capture.ToString())!; Assert(J.Text(report, "mode") == "offline", "mode"); Core.ReadSnapshot(report); });
} finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
if (OperatingSystem.IsWindows()) {
    try {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        var options = Options.Parse(Tool.SmokeArgs, Tool.ValueOptions, Tool.Flags);
        Tool.ValidateOptions(options); var live = await Tool.Collect(options, timeout.Token); Tool.ValidateEvidence(live.Evidence);
        Assert(live.Evidence["tasks"] is JsonArray, "missing live rows");
        Assert(((JsonArray)live.Evidence["tasks"]!).Count > 0, "expected live evidence");
        passed++; Console.WriteLine($"PASS Windows live collection ({((JsonArray)live.Evidence["tasks"]!).Count} rows, {live.Issues.Count} issues)");
    } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL Windows live collection: " + e); }
}
Console.WriteLine($"{passed} passed; {failed} failed"); return failed == 0 ? 0 : 1;
