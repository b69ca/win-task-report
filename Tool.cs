using System.Text.Json.Nodes;
namespace WinDiagnostics;

public static class Tool
{
    public const string Name = "win-task-report";
    public static readonly string[] ValueOptions = ["--task", "--limit"];
    public static readonly string[] Flags = ["--include-microsoft"];
    public const string Help = "win-task-report [--task NAME-SUBSTRING] [--limit 1000] [--include-microsoft] [--json]\nRead local scheduled tasks and runtime information. Excludes the \\Microsoft\\ tree by default. Filter is a literal case-insensitive substring of task path+name. Does not run, enable, disable, or modify tasks; never collects action arguments or account names.";
    public static void ValidateOptions(Options o)
    {
        o.Number("--limit", 1000, 1, 5000);
        if (o.Has("--task") && (string.IsNullOrWhiteSpace(o.Get("--task")) || o.Get("--task").Length > 256)) throw new ArgumentException("--task must contain 1–256 nonblank characters.");
    }
    public static Task<Collection> Collect(Options o, CancellationToken ct) => PowerShell.Collect(new()
    {
        ["UTIL_TASK_FILTER"] = o.Get("--task"), ["UTIL_TASK_LIMIT"] = o.Number("--limit", 1000, 1, 5000).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["UTIL_TASK_MICROSOFT"] = o.Has("--include-microsoft") ? "1" : "0"
    }, ct);
    public static string ResultLabel(uint code) => code switch
    {
        0 => "Success (0)", 0x41300 => "Ready", 0x41301 => "Running", 0x41302 => "Disabled", 0x41303 => "Has not run",
        0x41304 => "No more runs", 0x41305 => "Not scheduled", 0x41306 => "Terminated", 0x41307 => "No valid triggers", 0x41308 => "Event trigger",
        0x4131B => "Some triggers failed", 0x4131C => "Batch logon problem", 0x41325 => "Queued",
        _ => "Unmapped result; consult task/application documentation"
    };
    public static void ValidateEvidence(JsonObject e)
    {
        V.Text(e, "filter"); V.Bool(e, "includeMicrosoft"); V.Int(e, "limit", 1, 5000); V.Int(e, "matchedCount", 0, int.MaxValue); V.Rows(e, "tasks", 5000);
        var rows = J.Rows(e, "tasks").ToArray();
        if (rows.Length > J.Number(e, "limit") || rows.Length > J.Number(e, "matchedCount")) throw new ArgumentException("Task count exceeds scope.");
        foreach (var row in rows)
        {
            foreach (var key in new[] { "name", "path", "state" }) V.Text(row, key);
            V.Bool(row, "infoAvailable"); V.Int(row, "actionCount", 0, 1000);
            foreach (var key in new[] { "lastRunUtc", "nextRunUtc" })
            {
                if (!row.ContainsKey(key)) throw new ArgumentException($"Missing '{key}'.");
                if (row[key] is not null && !DateTimeOffset.TryParse(V.Text(row, key), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) throw new ArgumentException("Invalid task timestamp.");
            }
            if (J.Bool(row, "infoAvailable")) { V.Int(row, "lastResult", 0, uint.MaxValue); V.Int(row, "missedRuns", 0, uint.MaxValue); }
            else if (!row.ContainsKey("lastResult") || !row.ContainsKey("missedRuns") || row["lastResult"] is not null || row["missedRuns"] is not null)
                throw new ArgumentException("Unavailable runtime info must use null counters.");
        }
    }
    public static List<Finding> Analyze(JsonObject e)
    {
        var findings = new List<Finding>(); var rows = J.Rows(e, "tasks").ToArray();
        if (rows.Length == 0) findings.Add(new("no-tasks", "info", "No tasks in the selected scope", "Microsoft tasks are excluded by default; visibility also depends on permissions.", "Review the filter, --include-microsoft, and collection issues."));
        foreach (var row in rows)
        {
            var name = J.Text(row, "path") + J.Text(row, "name");
            if (J.Text(row, "state") == "Disabled") findings.Add(new("task-disabled", "info", "Task is disabled", name, "Confirm whether this is intentional; this report never enables tasks."));
            if (!J.Bool(row, "infoAvailable")) continue;
            uint code = (uint)J.Number(row, "lastResult");
            if (J.Number(row, "missedRuns") > 0) findings.Add(new("missed-runs", "warning", "Task reports missed runs", $"{name}: {J.Text(row, "missedRuns")}.", "Review Task Scheduler history, trigger conditions, and availability. Missed runs do not identify a root cause."));
            if (code == 0x41303) findings.Add(new("task-never-run", "info", "Task has not run", name, "Check whether its trigger should already have fired; new tasks may be expected to have no run history."));
            else if (code == 0x41306 || code == 0x41307 || code == 0x4131B || code == 0x4131C || (ResultLabel(code).StartsWith("Unmapped", StringComparison.Ordinal) && code != 0))
                findings.Add(new("task-result-review", "warning", "Task result warrants review", $"{name}: 0x{code:X8} ({ResultLabel(code)}). Current state: {J.Text(row, "state")}.", "Consult Task Scheduler history and the application's exit-code documentation. A nonzero application code does not universally mean failure; a previous result may coexist with a running task."));
        }
        return findings;
    }
    public static IEnumerable<string> Describe(JsonObject e)
    {
        yield return $"{J.Rows(e, "tasks").Count()} of {J.Text(e, "matchedCount")} matched tasks; Microsoft tree included: {J.Text(e, "includeMicrosoft")}.";
        foreach (var row in J.Rows(e, "tasks"))
        {
            var code = (uint)J.Number(row, "lastResult");
            yield return $"{J.Text(row, "path")}{J.Text(row, "name")} [{J.Text(row, "state")}]";
            yield return J.Bool(row, "infoAvailable") ? $"  Result 0x{code:X8}: {ResultLabel(code)} | missed {J.Text(row, "missedRuns")}" : "  Runtime information unavailable";
            yield return $"  Last run UTC {J.Text(row, "lastRunUtc", "not recorded")} | next UTC {J.Text(row, "nextRunUtc", "not scheduled")}";
        }
    }
    public static string[] SmokeArgs => ["--include-microsoft", "--limit", "5"];
}
